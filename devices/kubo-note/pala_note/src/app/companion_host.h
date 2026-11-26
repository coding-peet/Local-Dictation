#pragma once
#include <string>
#include <string_view>

namespace companion_host {
inline std::string label(std::string_view name) {
  std::string result;
  bool separator = false;
  for (char c : name) {
    if (c == ' ' || c == '-') {
      separator = !result.empty();
      continue;
    }
    if (separator) result += '-';
    separator = false;
    result += (c >= 'A' && c <= 'Z') ? static_cast<char>(c + ('a' - 'A')) : c;
  }
  return result;
}

inline bool accepts(std::string_view host, std::string_view ip, std::string_view localName) {
  if (ip.empty()) return false;
  if (host.size() >= 3 && host.substr(host.size() - 3) == ":80") host.remove_suffix(3);
  return host == ip || (!localName.empty() && host == localName);
}
}  // namespace companion_host
