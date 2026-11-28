#include "storage_probe.h"

#if PALA_STORAGE_PROBE

#include <sys/lock.h>
#include "driver/sdmmc_host.h"
#include "esp_attr.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "xt_utils.h"
#if PALA_VFS_OWNER_TRACE
#include "owner_trace_hooks.h"
#define VFS_CALLER_PC reinterpret_cast<uintptr_t>(__builtin_return_address(0))
#else
#define VFS_CALLER_PC 0U
#endif

namespace {

using storage_probe::Sample;

// The core libraries can take newlib locks very early and with flash cache
// disabled. The fast paths use only internal BSS, IRAM and lock-free loads.
alignas(4) uint32_t owner;
alignas(4) uint32_t hostTakeArmed;
alignas(4) uint32_t eventWaitArmed;
#if PALA_VFS_OWNER_TRACE
alignas(4) uint32_t vfsQueueArmed;
#endif
static_assert(sizeof(TaskHandle_t) == sizeof(owner));
static_assert(XCHAL_HAVE_S32C1I, "The bench owner claim requires Xtensa hardware CAS");

struct PendingLock {
  uint32_t* bucket;
  uint32_t* interruptedPhase;
#if PALA_VFS_OWNER_TRACE
  uint32_t waiterPc;
  uint32_t queue;
#endif
};

struct Capture {
  Sample sample;
  uint64_t started;
  uint32_t since;
  uint32_t* phase;
  PendingLock* lock;
  uint32_t* eventInterruptedPhase;
  uint32_t nestedBegins;
};
Capture capture;

inline __attribute__((always_inline)) bool onRecordingTask() {
  const uint32_t task = __atomic_load_n(&owner, __ATOMIC_ACQUIRE);
  return task && !xPortInIsrContext() &&
         reinterpret_cast<uintptr_t>(xTaskGetCurrentTaskHandle()) == task;
}

inline __attribute__((always_inline)) uint32_t nowUs() {
  return static_cast<uint32_t>(esp_timer_get_time());
}

inline __attribute__((always_inline)) void enterPhase(uint32_t* next, uint32_t now) {
  *capture.phase += now - capture.since;
  capture.phase = next;
  capture.since = now;
}

class LockInterval {
 public:
  inline __attribute__((always_inline)) explicit LockInterval(uint32_t& bucket, uint32_t callerPc = 0)
      : pending_{&bucket, nullptr}, previous_(capture.lock) {
#if PALA_VFS_OWNER_TRACE
    pending_.waiterPc = callerPc;
    __atomic_store_n(&vfsQueueArmed, &bucket == &capture.sample.vfsLockUs ? 1U : 0U, __ATOMIC_RELEASE);
#endif
    ++capture.sample.lockCalls;
    capture.lock = &pending_;
  }

  inline __attribute__((always_inline)) ~LockInterval() {
    if (pending_.interruptedPhase) enterPhase(pending_.interruptedPhase, nowUs());
#if PALA_VFS_OWNER_TRACE
    if (pending_.bucket == &capture.sample.vfsLockUs) {
      if (pending_.interruptedPhase) owner_trace::acquired();
    }
    const bool previousVfs = previous_ && previous_->bucket == &capture.sample.vfsLockUs;
    __atomic_store_n(&vfsQueueArmed, previousVfs ? 1U : 0U, __ATOMIC_RELEASE);
#endif
    capture.lock = previous_;
  }

 private:
  PendingLock pending_;
  PendingLock* previous_;
};

// No extra semaphore take, zero-timeout try, polling or priority change.
// Newlib front ends may delegate to another tracked acquisition. Count once.
template<class Call>
inline __attribute__((always_inline)) auto observeLock(uint32_t& bucket, Call real, uint32_t callerPc = 0)
    -> decltype(real()) {
  if (!onRecordingTask()) return real();
  // A recursive/newlib alias is one acquisition. A different nested layer
  // owns its own kernel wait and restores the preceding acquisition on return.
  if (capture.lock && capture.lock->bucket == &bucket) return real();
  LockInterval lock(bucket, callerPc);
  return real();
}

#if PALA_VFS_OWNER_TRACE
template<class Call>
inline __attribute__((always_inline)) BaseType_t captureVfsQueue(QueueHandle_t queue, Call real) {
  if (!__atomic_load_n(&vfsQueueArmed, __ATOMIC_ACQUIRE) || !onRecordingTask()) return real();
  PendingLock* lock = capture.lock;
  const uint32_t previous = lock->queue;
  lock->queue = reinterpret_cast<uintptr_t>(queue);
  const BaseType_t result = real();
  lock->queue = previous;
  return result;
}
#endif

class HostPhase {
 public:
  explicit HostPhase(uint32_t& bucket) : previous_(capture.phase) {
    enterPhase(&bucket, nowUs());
  }
  ~HostPhase() { enterPhase(previous_, nowUs()); }

 private:
  uint32_t* previous_;
};

}  // namespace

namespace storage_probe {

void begin() {
  const uint32_t task = reinterpret_cast<uintptr_t>(xTaskGetCurrentTaskHandle());
  // The pinned compiler emits aligned L32I/S32I and MEMW for our word
  // loads/stores. Use IDF's hardware S32C1I explicitly for the owner claim.
  __atomic_thread_fence(__ATOMIC_RELEASE);
  const bool claimed = xt_utils_compare_and_set(&owner, 0, task);
  __atomic_thread_fence(__ATOMIC_ACQUIRE);
  if (!claimed) {
    if (__atomic_load_n(&owner, __ATOMIC_ACQUIRE) == task) {
      ++capture.nestedBegins;
      capture.sample.valid = 0;
    }
    return;
  }
  capture = {};
  capture.sample.valid = 1;
#if PALA_VFS_OWNER_TRACE
  owner_trace::writerBegin(capture.sample.owners);
#endif
  capture.phase = &capture.sample.otherUs;
  capture.started = esp_timer_get_time();
  capture.since = static_cast<uint32_t>(capture.started);
}

Sample finish() {
  if (!onRecordingTask()) return {};
  if (capture.nestedBegins) {
    --capture.nestedBegins;
    return {};
  }
  const uint64_t elapsed = static_cast<uint64_t>(esp_timer_get_time()) - capture.started;
  const uint32_t end = static_cast<uint32_t>(capture.started + elapsed);
  enterPhase(&capture.sample.otherUs, end);
  capture.sample.totalUs = static_cast<uint32_t>(elapsed);
  if (elapsed > UINT32_MAX) capture.sample.valid = 0;
#if PALA_VFS_OWNER_TRACE
  owner_trace::writerEnd();
#endif
  const Sample result = capture.sample;
  __atomic_store_n(&owner, 0U, __ATOMIC_RELEASE);
  return result;
}

}  // namespace storage_probe

extern "C" {

int __real_ff_mutex_take(int volume) noexcept;
void __real__lock_acquire(_lock_t* lock) noexcept;
void __real__lock_acquire_recursive(_lock_t* lock) noexcept;
void __real___retarget_lock_acquire(_LOCK_T lock) noexcept;
void __real___retarget_lock_acquire_recursive(_LOCK_T lock) noexcept;
BaseType_t __real_xQueueSemaphoreTake(QueueHandle_t queue, TickType_t ticks) noexcept;
BaseType_t __real_xTaskPriorityInherit(TaskHandle_t holder) noexcept;
void __real_vTaskPlaceOnEventList(List_t* list, TickType_t ticks) noexcept;
esp_err_t __real_sdmmc_host_do_transaction(int slot, sdmmc_command_t* command) noexcept;

// IDF 5.4.1 keeps this event type in its driver's private sdmmc_internal.h,
// outside the Arduino SDK headers. The linked ABI is (int, event pointer).
// The probe forwards the opaque pointer without accessing its contents.
struct SdmmcDriverEvent;
esp_err_t __real_sdmmc_host_wait_for_event(int ticks, SdmmcDriverEvent* event) noexcept;

int __wrap_ff_mutex_take(int volume) {
  return observeLock(capture.sample.fatLockUs, [=] { return __real_ff_mutex_take(volume); });
}

void IRAM_ATTR __wrap__lock_acquire(_lock_t* lock) {
  observeLock(capture.sample.vfsLockUs, [=] { __real__lock_acquire(lock); }, VFS_CALLER_PC);
}

void IRAM_ATTR __wrap__lock_acquire_recursive(_lock_t* lock) {
  observeLock(capture.sample.vfsLockUs, [=] { __real__lock_acquire_recursive(lock); }, VFS_CALLER_PC);
}

void IRAM_ATTR __wrap___retarget_lock_acquire(_LOCK_T lock) {
  observeLock(capture.sample.vfsLockUs, [=] { __real___retarget_lock_acquire(lock); }, VFS_CALLER_PC);
}

void IRAM_ATTR __wrap___retarget_lock_acquire_recursive(_LOCK_T lock) {
  observeLock(capture.sample.vfsLockUs, [=] { __real___retarget_lock_acquire_recursive(lock); }, VFS_CALLER_PC);
}

BaseType_t IRAM_ATTR __wrap_xTaskPriorityInherit(TaskHandle_t holder) {
  // This kernel call is on the contended mutex branch, immediately before
  // vTaskPlaceOnEventList under the queue lock. A retry can block again.
  if (onRecordingTask() && capture.lock) {
    ++capture.sample.lockBlocks;
    if (!capture.lock->interruptedPhase) {
      capture.lock->interruptedPhase = capture.phase;
      enterPhase(capture.lock->bucket, nowUs());
    }
  }
#if PALA_VFS_OWNER_TRACE
  if (__atomic_load_n(&vfsQueueArmed, __ATOMIC_ACQUIRE) && onRecordingTask() && capture.lock)
    owner_trace::blocked(capture.lock->queue, reinterpret_cast<uintptr_t>(holder), capture.lock->waiterPc);
#endif
  return __real_xTaskPriorityInherit(holder);
}

void IRAM_ATTR __wrap_vTaskPlaceOnEventList(List_t* list, TickType_t ticks) {
  // A positive timeout may still find an event queued. Only placement on
  // the driver's actual receive wait list establishes an event wait.
  if (__atomic_load_n(&eventWaitArmed, __ATOMIC_RELAXED) && onRecordingTask() && !capture.lock) {
    ++capture.sample.events;
    if (!capture.eventInterruptedPhase) {
      capture.eventInterruptedPhase = capture.phase;
      enterPhase(&capture.sample.eventUs, nowUs());
    }
  }
#if PALA_VFS_OWNER_TRACE
  owner_trace::eventBlock();
#endif
  __real_vTaskPlaceOnEventList(list, ticks);
}

BaseType_t IRAM_ATTR __wrap_xQueueSemaphoreTake(QueueHandle_t queue, TickType_t ticks) {
#if PALA_VFS_OWNER_TRACE
  if (!__atomic_load_n(&hostTakeArmed, __ATOMIC_RELAXED) &&
      __atomic_load_n(&vfsQueueArmed, __ATOMIC_ACQUIRE) && onRecordingTask())
    return captureVfsQueue(queue, [=] { return __real_xQueueSemaphoreTake(queue, ticks); });
#endif
  // In the pinned driver the first call from do_transaction is the take of
  // s_request_mutex. Re-arm only at entry to another real host transaction.
  if (!__atomic_load_n(&hostTakeArmed, __ATOMIC_RELAXED) || !onRecordingTask())
    return __real_xQueueSemaphoreTake(queue, ticks);
  __atomic_store_n(&hostTakeArmed, 0U, __ATOMIC_RELAXED);
  LockInterval lock(capture.sample.hostLockUs);
  return __real_xQueueSemaphoreTake(queue, ticks);
}

#if PALA_VFS_OWNER_TRACE
BaseType_t __real_xQueueTakeMutexRecursive(QueueHandle_t, TickType_t) noexcept;
BaseType_t IRAM_ATTR __wrap_xQueueTakeMutexRecursive(QueueHandle_t queue, TickType_t ticks) {
  return captureVfsQueue(queue, [=] { return __real_xQueueTakeMutexRecursive(queue, ticks); });
}
#endif

esp_err_t __wrap_sdmmc_host_do_transaction(int slot, sdmmc_command_t* command) {
#if PALA_VFS_OWNER_TRACE
  owner_trace::Frame activity(owner_trace::Layer::Command, command ? command->opcode : UINT32_MAX);
#endif
  if (!onRecordingTask()) return __real_sdmmc_host_do_transaction(slot, command);
  ++capture.sample.commands;
  HostPhase transaction(capture.sample.commandUs);
  __atomic_store_n(&hostTakeArmed, 1U, __ATOMIC_RELAXED);
  const esp_err_t result = __real_sdmmc_host_do_transaction(slot, command);
  // In this SDK every host transaction takes s_request_mutex first. A
  // missing callback invalidates the split instead of hiding an untraced lock.
  if (__atomic_load_n(&hostTakeArmed, __ATOMIC_RELAXED)) capture.sample.valid = 0;
  __atomic_store_n(&hostTakeArmed, 0U, __ATOMIC_RELAXED);
  return result;
}

esp_err_t __wrap_sdmmc_host_wait_for_event(int ticks, SdmmcDriverEvent* event) {
#if PALA_VFS_OWNER_TRACE
  owner_trace::Frame activity(owner_trace::Layer::Event, ticks > 0);
#endif
  if (ticks <= 0 || !onRecordingTask() || capture.phase != &capture.sample.commandUs)
    return __real_sdmmc_host_wait_for_event(ticks, event);
  capture.eventInterruptedPhase = nullptr;
  __atomic_store_n(&eventWaitArmed, 1U, __ATOMIC_RELAXED);
  const esp_err_t result = __real_sdmmc_host_wait_for_event(ticks, event);
  __atomic_store_n(&eventWaitArmed, 0U, __ATOMIC_RELAXED);
  if (capture.eventInterruptedPhase) enterPhase(capture.eventInterruptedPhase, nowUs());
  return result;
}

}  // extern "C"

#endif  // PALA_STORAGE_PROBE
