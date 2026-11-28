#pragma once

#include <ArduinoJson.h>
#include <cstdint>
#include <cstring>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace workspace_view {
constexpr int rowsPerPage = 3;
constexpr int maxRows = 32;
constexpr size_t maxTitleBytes = 400;

enum class Status { Unavailable, Offline, Online };

inline Status cacheStatus(int64_t now, int64_t updated, bool paired, bool offline) {
  if (!paired || updated <= 0 || now < updated || now - updated >= 24 * 60 * 60)
    return Status::Unavailable;
  return offline || now - updated >= 4 * 60 ? Status::Offline : Status::Online;
}

struct SavedItems {
  std::string title;
  std::vector<std::string> rows;
  bool truncated;
};

inline std::optional<SavedItems> readSavedItems(JsonVariantConst saved) {
  if (!saved.is<JsonObjectConst>() || !saved["title"].is<const char*>() ||
      !saved["truncated"].is<bool>()) return std::nullopt;
  const auto rows = saved["rows"].as<JsonArrayConst>();
  const auto* title = saved["title"].as<const char*>();
  if (rows.isNull() || rows.size() > maxRows || std::strlen(title) > maxTitleBytes)
    return std::nullopt;

  SavedItems result{title, {}, saved["truncated"].as<bool>()};
  result.rows.reserve(rows.size());
  for (JsonObjectConst row : rows) {
    if (!row["title"].is<const char*>() || !row["completed"].is<bool>()) return std::nullopt;
    const auto* text = row["title"].as<const char*>();
    if (row["completed"].as<bool>()) continue;
    if (std::strlen(text) > maxTitleBytes) return std::nullopt;
    result.rows.emplace_back(text);
  }
  return result;
}

// The title is normalized to the display alphabet before measuring.
template<class Fits>
size_t firstTitleLine(std::string_view title, Fits fits) {
  size_t count = 0;
  while (count < title.size() && fits(title.substr(0, count + 1))) ++count;
  if (count == title.size()) return count;
  const auto space = title.rfind(' ', count);
  return space != std::string_view::npos && space > 0 ? space : count;
}

inline int savedPages(const SavedItems* saved) {
  if (!saved || saved->rows.empty()) return 1;
  return (static_cast<int>(saved->rows.size()) + rowsPerPage - 1) / rowsPerPage;
}
} // namespace workspace_view
