#pragma once

enum class HomeBridgeLink { None, Wifi, Usb, Bluetooth };

class HomeBridgeStatus {
 public:
  constexpr HomeBridgeLink latest() const { return latest_; }
  constexpr bool needsRedraw() const { return latest_ != drawn_; }
  constexpr void verified(HomeBridgeLink link) { latest_ = link; }
  constexpr void drawn(HomeBridgeLink link) { drawn_ = link; }
  constexpr void reset() {
    latest_ = HomeBridgeLink::None;
    drawn_ = HomeBridgeLink::None;
  }

 private:
  HomeBridgeLink latest_ = HomeBridgeLink::None;
  HomeBridgeLink drawn_ = HomeBridgeLink::None;
};

extern HomeBridgeStatus homeBridgeStatus;
