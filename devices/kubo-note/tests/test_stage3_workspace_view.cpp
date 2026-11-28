#include "workspace_view_data.h"

#include <cassert>

using namespace workspace_view;

int main() {
  JsonDocument doc;
  assert(!deserializeJson(doc, R"({"saved":{"title":"Ideas","truncated":true,"rows":[
    {"title":"Zulu first","completed":false},{"title":"Alpha second","completed":true},
    {"title":"Middle third","completed":false},{"title":"Another open","completed":false},
    {"title":"Final page","completed":false}]}})"));
  const auto saved = readSavedItems(doc["saved"]);
  assert(saved && saved->title == "Ideas" && saved->truncated);
  assert(saved->rows.size() == 4 && saved->rows[0] == "Zulu first");
  assert(saved->rows[1] == "Middle third" && saved->rows[3] == "Final page");
  assert(savedPages(&*saved) == 2 && savedPages(nullptr) == 1);

  assert(!deserializeJson(doc, R"({"saved":{"title":"Empty list","truncated":false,"rows":[]}})"));
  const auto empty = readSavedItems(doc["saved"]);
  assert(empty && empty->rows.empty() && savedPages(&*empty) == 1);

  assert(!deserializeJson(doc, R"({"saved":{"title":"All done","truncated":false,
    "rows":[{"title":"Hidden","completed":true}]}})"));
  const auto done = readSavedItems(doc["saved"]);
  assert(done && done->rows.empty());

  assert(!deserializeJson(doc, R"({"rows":[]})"));
  assert(!readSavedItems(doc["saved"])); // Old Bridge payload.

  assert(!deserializeJson(doc, R"({"saved":{"title":"Bad","truncated":false,"rows":[
    {"title":"Missing flag"},{"title":"Wrong flag","completed":1}]}})"));
  assert(!readSavedItems(doc["saved"]));

  const auto fitsEight = [](std::string_view text) { return text.size() <= 8; };
  assert(firstTitleLine("short", fitsEight) == 5);
  assert(firstTitleLine("two words and more", fitsEight) == 3);
  assert(firstTitleLine("uninterruptedword", fitsEight) == 8);

  constexpr int64_t updated = 100000;
  assert(cacheStatus(updated, updated, true, false) == Status::Online);
  assert(cacheStatus(updated + 4 * 60, updated, true, false) == Status::Offline);
  assert(cacheStatus(updated + 1, updated, true, true) == Status::Offline);
  assert(cacheStatus(updated + 24 * 60 * 60, updated, true, true) == Status::Unavailable);
  assert(cacheStatus(updated + 1, updated, false, false) == Status::Unavailable);
  assert(cacheStatus(updated - 1, updated, true, false) == Status::Unavailable);
}
