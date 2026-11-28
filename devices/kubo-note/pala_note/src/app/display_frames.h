#pragma once
#include <cstdint>
#include <algorithm>

// The caller serializes these transitions. A slot waiting for upload can be
// replaced. The frame already claimed by the panel has its own frozen pixels.
namespace display_frames {
enum class Image { Ui, Home, Recording, Progress, Question, Saving };
enum class PriorSave { None, Pending, Finished };
enum class Refresh { Regular, Fast, Clean };
struct Frame {
  uint32_t id = 0;
  uint32_t edge = 0;
  Image image = Image::Ui;
  Refresh refresh = Refresh::Regular;
  bool builtin = false;
  PriorSave priorSave = PriorSave::None;
  uint32_t elapsed = 0; // Used when the panel itself paints progress.
  bool offer = false, locked = false;
  bool capture() const { return image == Image::Recording || image == Image::Progress || image == Image::Question; }
};
class Mailbox {
 public:
  uint32_t beginUi() {
    if (!pending_.capture() && !pending_.builtin) pending_ = {};
    return epoch_;
  }
  bool currentUi(uint32_t epoch) const { return !active_ && epoch == epoch_; }
  uint32_t publishUi(uint32_t epoch, Image image, Refresh refresh) {
    if (!currentUi(epoch)) return 0;
    pending_ = {next(), 0, image, refresh};
    return pending_.id;
  }
  void firstPcm(uint32_t edge) {
    ++epoch_;
    active_ = true;
    edge_ = edge;
    offer_=locked_=false;
    elapsed_=0;
    haveEdge_ = true;
    priorSave_ = edge == armedEdge_ ? armedPriorSave_ : PriorSave::None;
    armedPriorSave_ = PriorSave::None;
    captureBase_ = {};
    pending_ = {};
  }
  void end(uint32_t edge, bool saving = true) {
    if (!active(edge)) return;
    active_ = false;
    captureBase_ = {};
    ++epoch_;
    pending_ = {};
    if (saving) pending_ = {next(), edge, Image::Saving, Refresh::Fast, true};
  }
  uint32_t pendingSavingEdge() const { return pending_.image == Image::Saving ? pending_.edge : 0; }
  bool active(uint32_t edge) const { return active_ && edge == edge_; }
  bool latestCapture(uint32_t edge) const { return haveEdge_ && edge == edge_; }
  bool canShowSaved(uint32_t edge) const { return !active_ && latestCapture(edge); }

  // The producer pairs these notifications by edge. The earlier WAV can finish
  // between the next button edge and its first PCM, or during a panel transfer.
  bool setPriorSavePending(uint32_t edge, bool pending) {
    const auto state = pending ? PriorSave::Pending : PriorSave::Finished;
    if (!active(edge)) {
      if (!latestCapture(edge) && (pending || armedEdge_ == edge)) {
        if (armedEdge_ != edge || armedPriorSave_ != PriorSave::Finished) {
          armedEdge_ = edge;
          armedPriorSave_ = state;
        }
      }
      return false;
    }
    if (priorSave_ == state || priorSave_ == PriorSave::Finished) return false;
    priorSave_ = state;
    if (!captureBase_.id || captureBase_.image == Image::Question) return false;
    captureBase_.priorSave = state;
    pending_ = captureBase_;
    pending_.id = next();
    pending_.refresh = Refresh::Fast;
    return true;
  }

  uint32_t publishCapture(uint32_t edge, Image image, bool builtin = false, uint32_t elapsed = 0) {
    if (!active(edge)) return 0;
    pending_ = {next(), edge, image, image == Image::Progress ? Refresh::Fast : Refresh::Regular,
                builtin, image == Image::Question ? PriorSave::None : priorSave_, elapsed};
    pending_.offer=offer_;
    pending_.locked=locked_;
    pending_.elapsed=std::max(elapsed_,elapsed);
    captureBase_ = pending_;
    return pending_.id;
  }
  bool recordingStatus(uint32_t edge,uint32_t elapsed,bool offer,bool locked) {
    if (!active(edge)) return false;
    elapsed_=elapsed;
    offer_=offer;
    locked_=locked;
    if (!captureBase_.id) return false;
    captureBase_.offer=offer;
    captureBase_.locked=locked;
    captureBase_.elapsed=elapsed;
    pending_=captureBase_;
    pending_.refresh=Refresh::Fast;
    pending_.id=next();
    return true;
  }
  void invalidateCaptureImage() {
    if (pending_.capture()) pending_ = {};
    captureBase_ = {};
  }
  Frame claim() { const auto frame = pending_; pending_ = {}; return frame; }
  bool pending(uint32_t id) const { return id && id == pending_.id; }
 private:
  uint32_t next() { if (++serial_ == 0) ++serial_; return serial_; }
  // Valid only while its unadorned source pixels can be copied under sources.
  Frame pending_{}, captureBase_{};
  uint32_t armedEdge_ = 0;
  PriorSave armedPriorSave_ = PriorSave::None, priorSave_ = PriorSave::None;
  bool haveEdge_ = false;
  uint32_t serial_ = 0, epoch_ = 0, edge_ = 0;
  bool active_ = false, offer_ = false, locked_ = false;
  uint32_t elapsed_ = 0;
};
}
