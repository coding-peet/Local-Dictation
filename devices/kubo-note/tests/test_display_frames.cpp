#include "../pala_note/src/app/display_frames.h"
#include <cassert>
using namespace display_frames;
int main() {
  Mailbox m;
  auto paint = m.beginUi();
  auto pendingHome = m.publishUi(paint, Image::Home, Refresh::Regular);
  assert(pendingHome);
  const auto onPanel = m.claim();
  paint = m.beginUi();
  m.firstPcm(15);
  assert(!m.publishUi(paint, Image::Home, Refresh::Regular));
  const auto recReady = m.publishCapture(15, Image::Recording);
  assert(recReady && onPanel.image == Image::Home);
  assert(!m.publishUi(m.beginUi(), Image::Home, Refresh::Regular));
  assert(m.claim().id == recReady);
  m.publishCapture(15, Image::Progress);
  m.end(15);
  const auto finishing = m.claim();
  assert(finishing.id && finishing.builtin && !finishing.capture() && finishing.edge == 15);
  assert(!m.publishUi(paint, Image::Home, Refresh::Regular));
  assert(!m.publishCapture(15, Image::Recording));
  m.firstPcm(23);
  m.end(15);
  assert(m.active(23) && !m.active(15));
  const auto first = m.publishCapture(23, Image::Recording);
  m.beginUi();
  assert(m.pending(first));
  m.end(23);
  // A committed result can already be ready before the panel takes the "saving" image.
  auto own = m.publishUi(m.beginUi(), Image::Ui, Refresh::Clean);
  m.beginUi();
  assert(!m.pending(own));

  // Prepare B while A's release frame is pending. Foreground work for A
  // may finish even after B is released; it must never reclaim the screen.
  assert(m.canShowSaved(23));
  assert(!m.setPriorSavePending(31, true));
  m.firstPcm(31);
  assert(m.latestCapture(31) && !m.canShowSaved(23));
  assert(!m.setPriorSavePending(31, true));
  m.publishCapture(31, Image::Recording);
  const auto inFlight = m.claim();
  assert(inFlight.priorSave == PriorSave::Pending);

  // A changed badge reuses the same base pixels as soon as the panel is free.
  assert(m.setPriorSavePending(31, false));
  assert(!m.setPriorSavePending(31, false));
  const auto cleared = m.claim();
  assert(cleared.image == Image::Recording && cleared.refresh == Refresh::Fast);
  assert(cleared.priorSave == PriorSave::Finished && inFlight.priorSave == PriorSave::Pending);
  assert(!m.setPriorSavePending(31, true)); // late duplicate from the producer
  m.end(23);
  assert(m.active(31));
  m.end(31);
  assert(m.canShowSaved(31) && !m.canShowSaved(23));

  // A marker or renderer repaint coalesces a status change using the most
  // recent elapsed time. An unpublished surface cannot be reused by the panel.
  m.setPriorSavePending(42, true);
  m.firstPcm(42);
  m.publishCapture(42, Image::Recording);
  m.invalidateCaptureImage();
  assert(!m.setPriorSavePending(42, false));
  assert(!m.claim().id);
  m.publishCapture(42, Image::Progress, true, 1300);
  auto progress = m.claim();
  assert(progress.builtin && progress.elapsed == 1300 && progress.priorSave == PriorSave::Finished);
  m.end(42);

  // An old edge cannot unset another edge's preparing status. A normal take
  // and a question have no prior-save instrumentation or badge.
  m.setPriorSavePending(53, true);
  m.setPriorSavePending(42, false);
  m.firstPcm(53);
  m.publishCapture(53, Image::Progress, true, 2800);
  m.setPriorSavePending(53, false);
  progress = m.claim();
  assert(progress.builtin && progress.elapsed == 2800 && progress.priorSave == PriorSave::Finished);
  m.end(53);
  m.firstPcm(64);
  m.publishCapture(64, Image::Recording, true);
  assert(m.claim().priorSave == PriorSave::None);
  m.end(64);
  m.setPriorSavePending(71, true);
  m.firstPcm(71);
  m.publishCapture(71, Image::Question);
  assert(m.claim().priorSave == PriorSave::None);
  assert(!m.setPriorSavePending(71, false));
}
