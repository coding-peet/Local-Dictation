#include "../pala_note/src/app/stage2_cut.h"
#include <cassert>

int main() {
  using namespace stage2;
  CutSelection cut;
  assert(!cut.arm(Point::Text, 0));
  assert(!cut.arm(Point::LoseReceipt, 0));
  assert(cut.arm(Point::Receipt, 42));
  assert(!cut.consume(Point::Receipt, 43));
  assert(!cut.consume(Point::Text, 42));
  assert(cut.consume(Point::Receipt, 42));
  assert(!cut.consume(Point::Receipt, 42));
  assert(cut.arm(Point::Open, 0) && cut.consume(Point::Open, -1));
  assert(cut.arm(Point::Pcm, 0) && cut.arm(Point::None, 0));
  assert(!cut.consume(Point::Pcm, -1));
}
