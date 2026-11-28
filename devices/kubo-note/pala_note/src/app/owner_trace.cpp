#include "owner_trace.h"

#if PALA_VFS_OWNER_TRACE
#include "owner_trace_hooks.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "ff.h"

namespace owner_trace {
namespace {

// Never call the kernel or a driver while holding this mux. The only nested
// order is a kernel queue lock followed by this mux at its block/give markers.
// Read the lock-free systimer under the mux so dual-core transitions stay ordered.
portMUX_TYPE monitor = portMUX_INITIALIZER_UNLOCKED;
constexpr uint32_t noCommand = UINT32_MAX;
static_assert(sizeof(TaskHandle_t) == sizeof(uint32_t));
static_assert(configMAX_TASK_NAME_LEN <= sizeof(Wait::task));

struct Context {
  uint32_t task, depth, work, fat, pc, vfs, vfsPc, opcode;
  bool eventArmed, eventBlocked, inDelay;
};
Context contexts[12];
alignas(4) uint32_t publishedTasks[12], contextCount;
uint32_t drops;
alignas(4) uint32_t enabled, eventObservers, watchedHolder;
Evidence* output;
uint32_t dropStart, sequence;
struct Observation {
  Wait wait;
  uint32_t start, last, givingQueue;
  bool active;
};
Observation observation;

inline __attribute__((always_inline)) uint32_t nowUs() {
  return static_cast<uint32_t>(esp_timer_get_time());
}
inline __attribute__((always_inline)) uint32_t currentTask() {
  return reinterpret_cast<uintptr_t>(xTaskGetCurrentTaskHandle());
}
inline __attribute__((always_inline)) bool wanted() {
  return __atomic_load_n(&enabled, __ATOMIC_ACQUIRE) != 0;
}
inline __attribute__((always_inline)) bool following(uint32_t task) {
  return task && __atomic_load_n(&watchedHolder, __ATOMIC_ACQUIRE) == task;
}

int IRAM_ATTR findTask(uint32_t task) {
  for (unsigned i = 0; i < sizeof(contexts) / sizeof(contexts[0]); ++i)
    if (contexts[i].task == task) return static_cast<int>(i);
  return -1;
}

void IRAM_ATTR account(const Context& c, uint32_t at) {
  auto& o = observation;
  if (c.task != o.wait.holder || (o.wait.flags & ReleaseSeen)) return;
  const uint32_t duration = at - o.last;
  if (c.opcode != noCommand) o.wait.hostUs += duration;
  if (c.eventBlocked) o.wait.eventUs += duration;
  if (c.inDelay) o.wait.delayUs += duration;
  o.last = at;
}

void IRAM_ATTR remember(const Context& c, bool initial) {
  Wait& w = observation.wait;
  if (c.task != w.holder || (w.flags & ReleaseSeen)) return;
  if (c.work) {
    if (w.work && w.work != c.work) w.flags |= WorkChanged;
    if (!w.work) w.work = c.work;
    if (initial) w.flags |= WorkAtBlock;
  }
  const uint32_t operation = c.fat ? c.fat : c.vfs;
  const uint32_t pc = c.fat ? c.pc : c.vfsPc;
  const uint32_t firstVfs = static_cast<uint32_t>(FatOp::VfsOpen);
  if (operation) {
    const bool deeper = c.fat && w.fatOp >= firstVfs;
    const bool shallower = !c.fat && w.fatOp && w.fatOp < firstVfs;
    if (w.fatOp && !shallower && (w.fatOp != operation || w.fatPc != pc)) w.flags |= FatChanged;
    if (!w.fatOp || deeper) {
      w.fatOp = operation;
      w.fatPc = pc;
      if (!initial) w.flags &= ~FatAtBlock;
    }
    if (initial) w.flags |= FatAtBlock;
  }
  if (c.opcode != noCommand) {
    if (w.opcode != noCommand && w.opcode != c.opcode) w.flags |= OpcodeChanged;
    if (w.opcode == noCommand) w.opcode = c.opcode;
    if (initial) w.flags |= CommandAtBlock;
  }
  if (initial && c.eventBlocked) w.flags |= EventAtBlock;
  if (c.inDelay) {
    w.flags |= DelayObserved;
    if (initial) w.flags |= DelayAtBlock;
  }
}

void setEvent(Context& c, bool armed, bool blocked) {
  if (armed != c.eventArmed) {
    const uint32_t count = __atomic_load_n(&eventObservers, __ATOMIC_RELAXED);
    __atomic_store_n(&eventObservers, armed ? count + 1 : count - 1, __ATOMIC_RELEASE);
  }
  c.eventArmed = armed;
  c.eventBlocked = blocked;
}

Token enter(Layer layer, uint32_t value, uint32_t pc) {
  Token token{-1, 0, 0};
  if (!wanted() || xPortInIsrContext() || (layer == Layer::Event && !value)) return token;
  const uint32_t task = currentTask();
  portENTER_CRITICAL(&monitor);
  int slot = findTask(task);
  if (layer == Layer::Event && (slot < 0 || contexts[slot].opcode == noCommand)) {
    portEXIT_CRITICAL(&monitor);
    return token;
  }
  if (slot < 0) {
    slot = findTask(0);
    if (slot < 0) {
      ++drops;
      portEXIT_CRITICAL(&monitor);
      return token;
    }
    contexts[slot] = {};
    contexts[slot].task = task;
    contexts[slot].opcode = noCommand;
    __atomic_store_n(&publishedTasks[slot], task, __ATOMIC_RELEASE);
    const uint32_t count = __atomic_load_n(&contextCount, __ATOMIC_RELAXED);
    __atomic_store_n(&contextCount, count + 1, __ATOMIC_RELEASE);
  }
  Context& c = contexts[slot];
  token.slot = slot;
  ++c.depth;
  if (following(task)) account(c, nowUs());
  switch (layer) {
    case Layer::Work: token.value = c.work; c.work = value; break;
    case Layer::Vfs: token.value = c.vfs; token.pc = c.vfsPc; c.vfs = value; c.vfsPc = pc; break;
    case Layer::Fat: token.value = c.fat; token.pc = c.pc; c.fat = value; c.pc = pc; break;
    case Layer::Command: token.value = c.opcode; c.opcode = value; break;
    case Layer::Event:
      token.value = (c.eventArmed ? 1U : 0U) | (c.eventBlocked ? 2U : 0U);
      setEvent(c, true, false); break;
  }
  if (following(task)) remember(c, false);
  portEXIT_CRITICAL(&monitor);
  return token;
}

void leave(Layer layer, Token token) {
  if (token.slot < 0) return;
  portENTER_CRITICAL(&monitor);
  Context& c = contexts[token.slot];
  if (following(c.task)) account(c, nowUs());
  switch (layer) {
    case Layer::Work: c.work = token.value; break;
    case Layer::Vfs: c.vfs = token.value; c.vfsPc = token.pc; break;
    case Layer::Fat: c.fat = token.value; c.pc = token.pc; break;
    case Layer::Command: c.opcode = token.value; break;
    case Layer::Event: setEvent(c, token.value & 1U, token.value & 2U); break;
  }
  if (following(c.task)) remember(c, false);
  if (--c.depth == 0) {
    __atomic_store_n(&publishedTasks[token.slot], 0U, __ATOMIC_RELEASE);
    const uint32_t count = __atomic_load_n(&contextCount, __ATOMIC_RELAXED);
    __atomic_store_n(&contextCount, count - 1, __ATOMIC_RELEASE);
    c.task = 0;
  }
  portEXIT_CRITICAL(&monitor);
}

void IRAM_ATTR finishWait(uint32_t at) {
  if (!output || !observation.active) return;
  Wait& w = observation.wait;
  w.waitUs = at - observation.start;
  if (!(w.flags & ReleaseSeen)) {
    // The identity at the block is proven. Without an exact give the end of
    // continuous ownership is unknown, so publish no owner I/O/hold duration.
    w.heldUs = w.hostUs = w.eventUs = w.delayUs = 0;
  }
  const uint32_t count = output->waits;
  if (count == 1 || w.waitUs > output->longest[0].waitUs) {
    output->longest[1] = output->longest[0];
    output->longest[0] = w;
  } else if (count == 2 || w.waitUs > output->longest[1].waitUs) {
    output->longest[1] = w;
  }
  output->omitted = count > 2 ? count - 2 : 0;
  __atomic_store_n(&watchedHolder, 0U, __ATOMIC_RELEASE);
  observation.active = false;
}

}  // namespace

WorkScope::WorkScope(Work work) {
  // App annotations run in task context. No hooks consult the scheduler at
  // boot until the first annotated task or recorder operation arms this build.
  __atomic_store_n(&enabled, 1U, __ATOMIC_RELEASE);
  const Token token = enter(Layer::Work, static_cast<uint32_t>(work), 0);
  slot_ = token.slot;
  previous_ = token.value;
}
WorkScope::~WorkScope() { leave(Layer::Work, {slot_, previous_, 0}); }

Frame::Frame(Layer layer, uint32_t value, uint32_t pc)
    : layer_(layer), token_(enter(layer, value, pc)) {}
Frame::~Frame() { leave(layer_, token_); }

void writerBegin(Evidence& evidence) {
  __atomic_store_n(&enabled, 1U, __ATOMIC_RELEASE);
  portENTER_CRITICAL(&monitor);
  output = &evidence;
  dropStart = drops;
  portEXIT_CRITICAL(&monitor);
}

void writerEnd() {
  portENTER_CRITICAL(&monitor);
  finishWait(nowUs());
  if (output) output->contextDrops = drops - dropStart;
  output = nullptr;
  portEXIT_CRITICAL(&monitor);
}

void IRAM_ATTR blocked(uint32_t mutex, uint32_t holder, uint32_t pc) {
  // The exact kernel holder is still protected by its queue lock here. The
  // same live TCB is about to be used by the real priority-inheritance call.
  const char* name = holder ? pcTaskGetName(reinterpret_cast<TaskHandle_t>(holder)) : nullptr;
  portENTER_CRITICAL(&monitor);
  const uint32_t at = nowUs();
  finishWait(at);
  if (output) {
    ++sequence;
    if (!sequence) ++sequence;
    ++output->waits;
    observation = {};
    observation.active = true;
    Wait& w = observation.wait;
    w.mutex = mutex; w.holder = holder; w.waiterPc = pc; w.opcode = noCommand;
    w.flags = mutex ? QueueKnown : 0;
    for (unsigned n = 0; name && n + 1 < sizeof(w.task) && name[n]; ++n) w.task[n] = name[n];
    observation.start = observation.last = at;
    const int slot = holder ? findTask(holder) : -1;
    if (slot >= 0) remember(contexts[slot], true);
    __atomic_store_n(&watchedHolder, holder, __ATOMIC_RELEASE);
  }
  portEXIT_CRITICAL(&monitor);
}

void IRAM_ATTR acquired() {
  portENTER_CRITICAL(&monitor);
  finishWait(nowUs());
  portEXIT_CRITICAL(&monitor);
}

void IRAM_ATTR eventBlock() {
  if (!__atomic_load_n(&eventObservers, __ATOMIC_ACQUIRE) || xPortInIsrContext()) return;
  const uint32_t task = currentTask();
  portENTER_CRITICAL(&monitor);
  const int slot = findTask(task);
  if (slot >= 0 && contexts[slot].eventArmed && !contexts[slot].eventBlocked) {
    Context& c = contexts[slot];
    if (following(task)) account(c, nowUs());
    c.eventBlocked = true;
  }
  portEXIT_CRITICAL(&monitor);
}

int IRAM_ATTR startDelay(TickType_t ticks) {
  // Only inspect task IDs when a covered firmware/FatFS/SD operation exists.
  // Ordinary delays on other tasks avoid the registry critical section.
  if (!ticks || !__atomic_load_n(&contextCount, __ATOMIC_ACQUIRE) || xPortInIsrContext()) return -1;
  const uint32_t task = currentTask();
  for (unsigned i = 0; i < sizeof(publishedTasks) / sizeof(publishedTasks[0]); ++i) {
    if (__atomic_load_n(&publishedTasks[i], __ATOMIC_ACQUIRE) != task) continue;
    portENTER_CRITICAL(&monitor);
    Context& c = contexts[i];
    int token = -1;
    if (c.task == task && !c.inDelay) {
      if (following(task)) account(c, nowUs());
      c.inDelay = true;
      if (following(task)) remember(c, false);
      token = static_cast<int>(i);
    }
    portEXIT_CRITICAL(&monitor);
    return token;
  }
  return -1;
}

void IRAM_ATTR endDelay(int slot) {
  portENTER_CRITICAL(&monitor);
  Context& c = contexts[slot];
  if (following(c.task)) account(c, nowUs());
  c.inDelay = false;
  portEXIT_CRITICAL(&monitor);
}

uint32_t IRAM_ATTR startGive(uint32_t queue, uint32_t& prior) {
  const uint32_t holder = __atomic_load_n(&watchedHolder, __ATOMIC_ACQUIRE);
  if (!holder || xPortInIsrContext() || currentTask() != holder) return 0;
  portENTER_CRITICAL(&monitor);
  uint32_t stamp = 0;
  if (following(holder) && !(observation.wait.flags & ReleaseSeen)) {
    prior = observation.givingQueue;
    observation.givingQueue = queue;
    stamp = sequence;
  }
  portEXIT_CRITICAL(&monitor);
  return stamp;
}

void IRAM_ATTR endGive(uint32_t stamp, uint32_t prior) {
  if (!stamp) return;
  portENTER_CRITICAL(&monitor);
  if (sequence == stamp) observation.givingQueue = prior;
  portEXIT_CRITICAL(&monitor);
}

void IRAM_ATTR giving(uint32_t holder) {
  if (!following(holder) || xPortInIsrContext() || currentTask() != holder) return;
  portENTER_CRITICAL(&monitor);
  Wait& w = observation.wait;
  if (following(holder) && !(w.flags & (ReleaseSeen | ReleaseUncertain))) {
    if (!observation.givingQueue) {
      // A give was already in progress when the writer started observing.
      // It might have been this queue. Do not claim a later continuous hold.
      w.flags |= ReleaseUncertain;
    } else if (w.mutex == observation.givingQueue) {
      const uint32_t at = nowUs();
      const int slot = findTask(holder);
      if (slot >= 0) account(contexts[slot], at);
      w.heldUs = at - observation.start;
      w.flags |= ReleaseSeen;
    }
  }
  portEXIT_CRITICAL(&monitor);
}
}  // namespace owner_trace

extern "C" {
BaseType_t __real_xQueueGenericSend(QueueHandle_t, const void*, TickType_t, BaseType_t) noexcept;
BaseType_t __real_xQueueGiveMutexRecursive(QueueHandle_t) noexcept;
BaseType_t __real_xTaskPriorityDisinherit(TaskHandle_t) noexcept;
void __real_vTaskDelay(TickType_t) noexcept;

void IRAM_ATTR __wrap_vTaskDelay(TickType_t ticks) {
  const int slot = owner_trace::startDelay(ticks);
  __real_vTaskDelay(ticks);
  if (slot >= 0) owner_trace::endDelay(slot);
}

BaseType_t IRAM_ATTR __wrap_xQueueGenericSend(QueueHandle_t q, const void* item, TickType_t ticks, BaseType_t pos) {
  uint32_t previous = 0;
  const uint32_t stamp = owner_trace::startGive(reinterpret_cast<uintptr_t>(q), previous);
  const BaseType_t result = __real_xQueueGenericSend(q, item, ticks, pos);
  if (stamp) owner_trace::endGive(stamp, previous);
  return result;
}
BaseType_t IRAM_ATTR __wrap_xQueueGiveMutexRecursive(QueueHandle_t q) {
  uint32_t previous = 0;
  const uint32_t stamp = owner_trace::startGive(reinterpret_cast<uintptr_t>(q), previous);
  const BaseType_t result = __real_xQueueGiveMutexRecursive(q);
  if (stamp) owner_trace::endGive(stamp, previous);
  return result;
}
BaseType_t IRAM_ATTR __wrap_xTaskPriorityDisinherit(TaskHandle_t holder) {
  owner_trace::giving(reinterpret_cast<uintptr_t>(holder));
  return __real_xTaskPriorityDisinherit(holder);
}

// FatFS functions are called by separate VFS objects in the pinned IDF.
// The captured PC belongs to the caller of this public entry point.
#define OBSERVE_FAT(symbol, operation, signature, arguments)                              \
  FRESULT __real_##symbol signature noexcept;                                             \
  FRESULT __wrap_##symbol signature {                                                     \
    owner_trace::Frame frame(owner_trace::Layer::Fat,                                     \
        static_cast<uint32_t>(owner_trace::FatOp::operation),                             \
        reinterpret_cast<uintptr_t>(__builtin_return_address(0)));                       \
    return __real_##symbol arguments;                                                     \
  }

OBSERVE_FAT(f_open, Open, (FIL* f, const TCHAR* path, BYTE mode), (f, path, mode))
OBSERVE_FAT(f_read, Read, (FIL* f, void* data, UINT size, UINT* count), (f, data, size, count))
OBSERVE_FAT(f_write, Write, (FIL* f, const void* data, UINT size, UINT* count), (f, data, size, count))
OBSERVE_FAT(f_opendir, OpenDir, (FF_DIR* d, const TCHAR* path), (d, path))
OBSERVE_FAT(f_readdir, ReadDir, (FF_DIR* d, FILINFO* info), (d, info))
OBSERVE_FAT(f_stat, Stat, (const TCHAR* path, FILINFO* info), (path, info))
OBSERVE_FAT(f_close, Close, (FIL* f), (f))
OBSERVE_FAT(f_closedir, CloseDir, (FF_DIR* d), (d))
OBSERVE_FAT(f_sync, Sync, (FIL* f), (f))
OBSERVE_FAT(f_lseek, Seek, (FIL* f, FSIZE_t offset), (f, offset))
OBSERVE_FAT(f_unlink, Unlink, (const TCHAR* path), (path))
OBSERVE_FAT(f_rename, Rename, (const TCHAR* from, const TCHAR* to), (from, to))
OBSERVE_FAT(f_mkdir, Mkdir, (const TCHAR* path), (path))
OBSERVE_FAT(f_truncate, Truncate, (FIL* f), (f))
OBSERVE_FAT(f_getfree, Free, (const TCHAR* path, DWORD* count, FATFS** fs), (path, count, fs))
#undef OBSERVE_FAT
}
#endif
