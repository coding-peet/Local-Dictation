#pragma once

#include <cstddef>
#include <cstdint>

template <typename SerialPort>
constexpr void writeUsbDataFrame(SerialPort& port, const std::uint8_t* data, std::size_t length) {
  const std::size_t queued = port.write(data, length);
  if (queued < length && port.write(data + queued, length - queued) != length - queued) return;

  // A response ending in a full 64-byte packet can remain pending in the host
  // driver. Drain the TX queue before adding an empty line as a short packet.
  port.flush();
  port.write(static_cast<std::uint8_t>('\n'));
}
