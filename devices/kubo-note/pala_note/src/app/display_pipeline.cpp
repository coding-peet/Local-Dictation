#include "Arduino.h"
#include "display_pipeline.h"
#include "ui.h"
#include "stage2_bench.h"
#include "../../globals.h"
#include <esp_heap_caps.h>
#include <cstring>

namespace note_display {
namespace {
using display_frames::Frame;
using display_frames::Image;
using display_frames::PriorSave;
constexpr size_t bytes = 25 * 200;
portMUX_TYPE stateLock = portMUX_INITIALIZER_UNLOCKED;
display_frames::Mailbox mailbox;

// Legacy screens have one foreground painter. The audio screen painter has
// a separate, optional surface which doubles as the localized REC cache.
thread_local uint8_t* paintTarget = nullptr;
thread_local uint32_t paintEpoch = 0;
thread_local bool paintingHome = false;
thread_local bool lastOk = false;
thread_local uint32_t lastVisibleAt = 0;
uint8_t* scanout = nullptr;
uint8_t* recordingPixels = nullptr;
SemaphoreHandle_t sources = nullptr;
TaskHandle_t panelTask = nullptr, renderer = nullptr;
Frame uploading{};
uint32_t completed = 0;
bool completedOk = false;
uint32_t transferVisibleAt = 0, completedVisibleAt = 0;
bool homeStarted = false, homeEnded = false;
uint32_t homeStart = 0, homeEnd = 0, homeId = 0;
bool baseReady = false;
uint8_t partials = 0;
constexpr uint8_t cleanInterval = 8;
uint32_t requestVersion = 0, recordEdge = 0, markerTime = 0, cacheRevision = 1;
int markerCount = 0;
bool captureActive = false, asking = false, builtinOnly = false;

void notify(TaskHandle_t task) { if (task) xTaskNotifyGive(task); }
void finishHome(uint32_t at) {
  // The caller owns stateLock.
  homeEnded = true;
  homeEnd = at;
}
void panelPoint(const char* name) {
  const auto at = millis();
  if (strcmp(name, "idle") == 0) transferVisibleAt = at;
  if (uploading.priorSave != PriorSave::None && strcmp(name, "idle") == 0)
    stage2::previousSaveVisible(uploading.edge, uploading.priorSave == PriorSave::Pending, at);
  if (uploading.image == Image::Recording) stage2::displayEvent(uploading.edge, name, at);
  if (uploading.image == Image::Saving)
    stage2::releaseEvent(uploading.edge, strcmp(name, "ready") == 0 ? "saving_ready" : name, at);
  if (uploading.image == Image::Home) {
    portENTER_CRITICAL(&stateLock);
    const bool boot = uploading.id == homeId;
    portEXIT_CRITICAL(&stateLock);
    if (boot) stage2::homeEvent(name, at);
  }
}
bool transfer(const uint8_t* image, Refresh mode) {
  transferVisibleAt = 0;
  const bool clean = mode == Refresh::Clean || !baseReady || (mode == Refresh::Regular && partials >= cleanInterval);
  bool ok = false;
  if (clean) {
    display->EPD_Init();
    if (display->healthy()) {
      display->EPD_DisplayPartBaseImage(image);
      if (display->waveAcknowledged()) {
        display->EPD_Init_Partial();
        ok = display->healthy();
      }
    }
    partials = 0;
  } else {
    display->EPD_DisplayPart(image);
    ok = display->waveAcknowledged();
    if (ok && partials != UINT8_MAX) ++partials;
  }
  baseReady = ok;
  return ok;
}
void servePanel(void*) {
  for (;;) {
    ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
    for (;;) {
      xSemaphoreTake(sources, portMAX_DELAY);
      portENTER_CRITICAL(&stateLock);
      uploading = mailbox.claim();
      portEXIT_CRITICAL(&stateLock);
      if (uploading.id && !uploading.builtin) {
        const auto* source = uploading.capture() ? recordingPixels : display->getBuffer();
        memcpy(scanout, source, bytes);
      }
      xSemaphoreGive(sources);
      if (!uploading.id) break;
      panelPoint("claim");
      // The mailbox owns the base pixels. Only this task adds the indicator
      // to its private copy, before SPI starts and outside both shared locks.
      paintTarget = scanout;
      if (uploading.builtin) {
        if (uploading.image == Image::Saving) paintSavingScreen();
        else if (uploading.image == Image::Question) paintQuestionScreen();
        else if (uploading.image == Image::Progress) paintRecordingProgress(uploading.elapsed);
        else paintRecordingScreen(true);
      }
#if PALA_STAGE3
      if (uploading.image==Image::Recording || uploading.image==Image::Progress)
        paintRecordingStatus(uploading.elapsed,uploading.offer,uploading.locked);
#endif
      if (uploading.priorSave == PriorSave::Pending) paintPriorSaveIndicator();
      paintTarget = nullptr;
      if (uploading.builtin) panelPoint("ready");
      const bool ok = transfer(scanout, uploading.refresh);
      panelPoint("done");
      const auto now = millis();
      portENTER_CRITICAL(&stateLock);
      completed = uploading.id;
      completedOk = ok;
      completedVisibleAt = transferVisibleAt;
      if (uploading.image == Image::Home && uploading.id == homeId) finishHome(now);
      uploading = {};
      portEXIT_CRITICAL(&stateLock);
    }
  }
}

void renderCapture(void*) {
  uint32_t renderedRevision = 0;
  uint32_t handled = 0;
  bool cached = false;
  for (;;) {
    ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
    portENTER_CRITICAL(&stateLock);
    const uint32_t version = requestVersion, edge = recordEdge, revision = cacheRevision, elapsed = markerTime;
    const int moments = markerCount;
    const bool active = captureActive, question = asking;
    portEXIT_CRITICAL(&stateLock);
    if (!active && cached && renderedRevision == revision) continue;
    if (active && version == handled) continue;

    // A panel copies under sources, then releases it before any SPI or BUSY wait.
    // Mark this source unavailable before repainting so it cannot be selected
    // as a stale frame. The normal UI canvas stays independent throughout.
    xSemaphoreTake(sources, portMAX_DELAY);
    portENTER_CRITICAL(&stateLock);
    mailbox.invalidateCaptureImage();
    portEXIT_CRITICAL(&stateLock);
    xSemaphoreGive(sources);
    paintTarget = recordingPixels;
    if (!active) {
      paintRecordingScreen(false);
      cached = true;
      renderedRevision = revision;
      stage2::homeEvent("cache", millis());
    } else if (!moments) {
      stage2::displayEvent(edge, "draw", millis());
      const bool hit = !question && cached && renderedRevision == revision;
      stage2::displayEvent(edge, hit ? "cache" : "fallback", millis());
      if (!hit) {
        if (question) paintQuestionScreen();
        else paintRecordingScreen(true);
        cached = false;
      }
    } else {
      paintRecordingProgress(elapsed);
      cached = false;
    }
    paintTarget = nullptr;
    if (!active) continue;
    const auto kind = question ? Image::Question : moments ? Image::Progress : Image::Recording;
    portENTER_CRITICAL(&stateLock);
    const auto ticket = requestVersion == version ? mailbox.publishCapture(edge, kind) : 0;
    portEXIT_CRITICAL(&stateLock);
    if (ticket && kind == Image::Recording) stage2::displayEvent(edge, "ready", millis());
    handled = version;
    if (ticket) notify(panelTask);
  }
}
} // namespace

void initialize() {
  cacheRecordingCaptions();
  display->setObserver(panelPoint);
  scanout = static_cast<uint8_t*>(heap_caps_malloc(bytes, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
  if (!scanout) return;
  sources = xSemaphoreCreateMutex();
  if (!sources || xTaskCreatePinnedToCore(servePanel, "epd-panel", 4096, nullptr, 2, &panelTask, 0) != pdPASS) {
    if (sources) vSemaphoreDelete(sources);
    sources = nullptr;
    heap_caps_free(scanout);
    scanout = nullptr;
    return;
  }
  recordingPixels = static_cast<uint8_t*>(heap_caps_malloc(bytes, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
  TaskHandle_t task = nullptr;
  if (recordingPixels && xTaskCreatePinnedToCore(renderCapture, "epd-compose", 5120, nullptr, 2, &task, 0) == pdPASS) {
    portENTER_CRITICAL(&stateLock);
    renderer = task;
    portEXIT_CRITICAL(&stateLock);
    notify(task);
  } else {
    heap_caps_free(recordingPixels);
    recordingPixels = nullptr;
    portENTER_CRITICAL(&stateLock);
    builtinOnly = true;
    if (captureActive) mailbox.publishCapture(recordEdge, asking ? Image::Question : Image::Recording, true);
    portEXIT_CRITICAL(&stateLock);
    notify(panelTask);
  }
}

static bool beginUiPaint(const uint32_t* savedEdge) {
  if (sources) xSemaphoreTake(sources, portMAX_DELAY);
  portENTER_CRITICAL(&stateLock);
  const bool allowed = !savedEdge || mailbox.canShowSaved(*savedEdge);
  if (allowed) {
    if (homeId && mailbox.pending(homeId) && !homeEnded) finishHome(millis());
    paintEpoch = mailbox.beginUi();
    paintTarget = display->getBuffer();
  }
  portEXIT_CRITICAL(&stateLock);
  if (sources) xSemaphoreGive(sources);
  return allowed;
}

uint8_t* pixels() {
  if (!paintTarget) beginUiPaint(nullptr);
  return paintTarget;
}

bool beginSaved(uint32_t edge) {
  lastOk = false;
  return beginUiPaint(&edge);
}

bool latestCapture(uint32_t edge) {
  portENTER_CRITICAL(&stateLock);
  const bool current = mailbox.latestCapture(edge);
  portEXIT_CRITICAL(&stateLock);
  return current;
}

void startHome() {
  paintingHome = true;
  const auto at = millis();
  portENTER_CRITICAL(&stateLock);
  const bool first = !homeStarted;
  if (first) { homeStarted = true; homeStart = at; }
  portEXIT_CRITICAL(&stateLock);
  if (first) {
    stage2::homeEvent("request", at);
    stage2::homeEvent("draw", at);
  }
}
bool abandonSupersededUi() {
  if (!paintTarget) return false;
  portENTER_CRITICAL(&stateLock);
  const bool stale = !mailbox.currentUi(paintEpoch);
  if (stale && paintingHome && !homeId && !homeEnded) finishHome(millis());
  portEXIT_CRITICAL(&stateLock);
  if (stale) {
    paintTarget = nullptr;
    paintingHome = false;
    lastOk = false;
  }
  return stale;
}
bool bootHomeOutstanding() {
  portENTER_CRITICAL(&stateLock);
  const bool value = homeStarted && !homeEnded;
  portEXIT_CRITICAL(&stateLock);
  return value;
}
bool inBootHome(uint32_t edge) {
  portENTER_CRITICAL(&stateLock);
  const bool value = homeStarted && int32_t(edge - homeStart) >= 0 && (!homeEnded || int32_t(homeEnd - edge) >= 0);
  portEXIT_CRITICAL(&stateLock);
  return value;
}
bool present(Refresh mode, void (*service)(), bool asynchronousHome) {
  pixels();
  paintTarget = nullptr;
  const bool home = paintingHome;
  paintingHome = false;
  if (!panelTask) {
    display->setWaitCallback(service);
    lastOk = transfer(display->getBuffer(), mode);
    lastVisibleAt = transferVisibleAt;
    display->setWaitCallback(nullptr);
    if (home) { portENTER_CRITICAL(&stateLock); finishHome(millis()); portEXIT_CRITICAL(&stateLock); }
    return lastOk;
  }
  portENTER_CRITICAL(&stateLock);
  const auto replacedSaving = mailbox.pendingSavingEdge();
  const auto id = mailbox.publishUi(paintEpoch, home ? Image::Home : Image::Ui, mode);
  const bool firstHome = home && !homeId && !homeEnded;
  if (firstHome) {
    homeId = id;
    if (!id) finishHome(millis());
  }
  portEXIT_CRITICAL(&stateLock);
  if (id && replacedSaving) stage2::releaseEvent(replacedSaving, "superseded", millis());
  if (firstHome && id) stage2::homeEvent("ready", millis());
  notify(panelTask);
  lastOk = false;
  if (!id || asynchronousHome) return false;
  for (;;) {
    portENTER_CRITICAL(&stateLock);
    const bool shown = completed == id && completedOk;
    const uint32_t visibleAt = completedVisibleAt;
    const bool waiting = mailbox.pending(id) || uploading.id == id;
    portEXIT_CRITICAL(&stateLock);
    if (!waiting) {
      lastOk = shown;
      lastVisibleAt = visibleAt;
      return shown;
    }
    if (service) service();
    vTaskDelay(1);
  }
}
bool lastPresentationOk() { return lastOk; }
uint32_t lastPresentationVisibleAt() { return lastOk ? lastVisibleAt : 0; }

void setPriorSavePending(uint32_t activeEdge, bool pending) {
  portENTER_CRITICAL(&stateLock);
  const bool queued = mailbox.setPriorSavePending(activeEdge, pending);
  const auto task = panelTask;
  portEXIT_CRITICAL(&stateLock);
  if (queued) notify(task);
}

void firstPcm(uint32_t edge, bool question) {
  const auto now = millis();
  portENTER_CRITICAL(&stateLock);
  if (homeId && mailbox.pending(homeId) && !homeEnded) finishHome(now);
  const auto replacedSaving = mailbox.pendingSavingEdge();
  mailbox.firstPcm(edge);
  captureActive = true;
  recordEdge = edge;
  markerTime = 0;
  markerCount = 0;
  asking = question;
  ++requestVersion;
  const bool builtin = builtinOnly;
  if (builtin) mailbox.publishCapture(edge, question ? Image::Question : Image::Recording, true);
  const auto task = builtin ? panelTask : renderer;
  portEXIT_CRITICAL(&stateLock);
  if (replacedSaving) stage2::releaseEvent(replacedSaving, "superseded", now);
  if (!question) {
    stage2::displayEvent(edge, "request", now);
    if (builtin) stage2::displayEvent(edge, "fallback", now);
  }
  notify(task);
}
void recordingStatus(uint32_t edge,uint32_t elapsed,bool offer,bool locked) {
  portENTER_CRITICAL(&stateLock);
  const bool queued = mailbox.recordingStatus(edge,elapsed,offer,locked);
  const auto task = panelTask;
  portEXIT_CRITICAL(&stateLock);
  if (queued) notify(task);
}
void progress(uint32_t edge, uint32_t elapsed, int moments) {
  portENTER_CRITICAL(&stateLock);
  const bool current = mailbox.active(edge);
  if (current) {
    markerTime = elapsed; markerCount = moments; ++requestVersion;
    if (builtinOnly)
      mailbox.publishCapture(edge, asking ? Image::Question : moments ? Image::Progress : Image::Recording, true, elapsed);
  }
  const auto task = builtinOnly ? panelTask : renderer;
  portEXIT_CRITICAL(&stateLock);
  if (current) notify(task);
}
void endCapture(uint32_t edge, bool saving) {
  const auto at = millis();
  portENTER_CRITICAL(&stateLock);
  const bool active = mailbox.active(edge);
  const bool queueSaving = active && saving && !asking && panelTask;
  if (active) {
    mailbox.end(edge, queueSaving);
    captureActive = false;
    ++requestVersion;
  }
  const auto task = renderer;
  portEXIT_CRITICAL(&stateLock);
  if (queueSaving) {
    stage2::releaseEvent(edge, "queued", at);
    notify(panelTask);
  }
  if (active) notify(task);
}
void invalidateRecordingCache() {
  cacheRecordingCaptions();
  portENTER_CRITICAL(&stateLock);
  ++cacheRevision;
  const auto task = renderer;
  portEXIT_CRITICAL(&stateLock);
  notify(task);
}
}
