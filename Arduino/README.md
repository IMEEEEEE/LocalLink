# LocalLink

A feature-rich ESP32-S3 LocalLink display node combining a 32 x 8 WS2812 matrix, adaptive brightness, NTP time, weather pages, countdown timers, and UDP capability discovery.

## Hardware

- ESP32-S3 development board
- Four chained 8 x 8 WS2812 panels, 256 LEDs total
- TSL2591 ambient-light sensor
- Stable external LED power supply with common ground

## Software dependencies

- FastLED
- Adafruit TSL2591 and Adafruit Unified Sensor
- ESP32 Arduino core with WiFi, WiFiUdp, time, and FreeRTOS support

## Configuration and wiring

- Set `ssid` and `password` in `LocalLink.ino`.
- Matrix data is GPIO 11 and status LED is GPIO 48.
- TSL2591 uses the Wire bus selected by the sketch.
- LocalLink receives on UDP 5124 and uses UDP 5125 for its ESP-side channel.
- Timezone is set to US Eastern with daylight-saving rules; change `timeZone` for another location.

## Getting started

- Upload and watch startup at 115200 baud.
- Wait for Wi-Fi and NTP synchronization.
- Run a LocalLink controller on the same LAN, or send the documented UDP commands manually.
- The display and networking work are divided across FreeRTOS tasks.

## Data and protocol

- Display config: `L,<F|S>,<content-mask>,<switch-seconds>[,<unix-ms>]`; mask bit 0 is time and bit 1 is weather.
- Weather: `W,<temperature>,<humidity>,<C|F|S>`.
- Timer: `T,S,<seconds>`, `T,P`, `T,R`, `T,C`, and `T,A`.
- Temporary month color: `clock_month:<1-12>`.
- Capability broadcasts on port 5124 advertise `ws2812`, optional `tsl2591`, and current display mode.

## Notes and limitations

- UDP commands are unauthenticated and best-effort.
- The matrix mapping and panel orientation are specific to this build.
- Do not power the matrix through the ESP32-S3 board.
- NTP fallback addresses and timezone are currently hard-coded.

## Project files

- `DisplayAssets.h`
- `DisplayModule.h`
- `LocalLink.ino`
- `NetworkModule.h`
- `SensorModule.h`

## License

No license file is included. Unless a license is added, all rights remain with the copyright holder.
