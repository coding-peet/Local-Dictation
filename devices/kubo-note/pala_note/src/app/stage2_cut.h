#pragma once

namespace stage2 {
enum class Point { None, Open, Pcm, Publish, Text, Receipt, LoseReceipt };

// Firmware serializes configuration and consumption under a short critical section.
class CutSelection {
 public:
  bool arm(Point point, int note) {
    if ((point == Point::Text || point == Point::Receipt || point == Point::LoseReceipt) && note <= 0) return false;
    if ((point == Point::None || point == Point::Open || point == Point::Pcm) && note != 0) return false;
    point_ = point;
    note_ = note;
    return true;
  }
  bool consume(Point point, int note) {
    if (point == Point::None || point != point_ || (note_ && note != note_)) return false;
    point_ = Point::None;
    return true;
  }
 private:
  Point point_ = Point::None;
  int note_ = 0;
};
}
