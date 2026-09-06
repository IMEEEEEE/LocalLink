#pragma once

void printWiFiDisconnectReason(WiFiEvent_t event, WiFiEventInfo_t info) {
  if (event != ARDUINO_EVENT_WIFI_STA_DISCONNECTED) {
    return;
  }

  Serial.print("WiFi disconnected, reason=");
  Serial.println((int)info.wifi_sta_disconnected.reason);
}

void startWiFiAssociation() {
  WiFi.mode(WIFI_OFF);
  delay(1000);
  WiFi.mode(WIFI_STA);
  WiFi.setSleep(false);
  WiFi.setAutoReconnect(false);
  WiFi.config(INADDR_NONE, INADDR_NONE, INADDR_NONE, primaryDns, secondaryDns);
  WiFi.begin(ssid, password);
}

void connectWiFi() {
  displayMode = DISPLAY_WAITING;
  WiFi.persistent(false);
  static bool eventHandlerRegistered = false;
  if (!eventHandlerRegistered) {
    WiFi.onEvent(printWiFiDisconnectReason, ARDUINO_EVENT_WIFI_STA_DISCONNECTED);
    eventHandlerRegistered = true;
  }

  startWiFiAssociation();
  while (WiFi.status() != WL_CONNECTED) {
    Serial.print("Connecting to WiFi");

    // Give one association attempt enough time to finish before starting the
    // next one. This also prevents an access point from rate-limiting retries.
    for (int attempt = 0; attempt < 120 && WiFi.status() != WL_CONNECTED; attempt++) {
      if (attempt % 4 == 0) {
        Serial.print('.');
      }
      delay(150);
    }

    Serial.println();

    if (WiFi.status() != WL_CONNECTED) {
      Serial.print("WiFi retry, status=");
      Serial.println((int)WiFi.status());
      delay(2000);
      startWiFiAssociation();
    }
  }

  Serial.print("WiFi connected, IP=");
  Serial.println(WiFi.localIP());
  Serial.print("Gateway=");
  Serial.println(WiFi.gatewayIP());
  Serial.print("DNS=");
  Serial.println(WiFi.dnsIP());

  ntpServerAddressReady =
    WiFi.hostByName("time.cloudflare.com", ntpServerAddress) &&
    ntpServerAddress != IPAddress(0, 0, 0, 0);
  if (ntpServerAddressReady) {
    Serial.print("NTP DNS resolved=");
    Serial.println(ntpServerAddress);
  } else {
    Serial.println("NTP DNS resolution failed");
  }

  if (hasValidTime()) {
    displayMode = DISPLAY_CLOCK;
  }
}

bool syncTimeFromNtpClient() {
  Serial.println("Updating time from NTP");
  displayMode = DISPLAY_WAITING;

  for (int attempt = 0; attempt < 3; attempt++) {
    lastNtpTimeAttempt = millis();

    if (updateTimeFromNextNtpServer()) {
      displayMode = DISPLAY_CLOCK;
      return true;
    }

    delay(500);
  }

  Serial.println("NTP update failed");
  if (hasValidTime()) {
    displayMode = DISPLAY_CLOCK;
  }
  return false;
}

void maintainNtpTime() {
  const unsigned long retryInterval = hasValidTime() ? 3600000UL : 5000UL;
  if (millis() - lastNtpTimeAttempt < retryInterval) {
    return;
  }

  lastNtpTimeAttempt = millis();
  bool timeWasValid = hasValidTime();
  if (!timeWasValid) {
    displayMode = DISPLAY_WAITING;
  }
  updateTimeFromNextNtpServer();
  if (hasValidTime()) {
    displayMode = DISPLAY_CLOCK;
  }
}

bool updateTimeFromNextNtpServer() {
  const char* server = ntpServers[nextNtpServerIndex];
  nextNtpServerIndex = (nextNtpServerIndex + 1) % ntpServerCount;

  Serial.print("Trying NTP server: ");
  Serial.println(server);

  IPAddress requestAddress = ntpServerAddress;
  if (!ntpServerAddressReady) {
    ntpServerAddressReady =
      WiFi.hostByName(server, ntpServerAddress) &&
      ntpServerAddress != IPAddress(0, 0, 0, 0);
    if (ntpServerAddressReady) {
      requestAddress = ntpServerAddress;
      Serial.print("NTP DNS resolved=");
      Serial.println(requestAddress);
    } else {
      requestAddress = ntpFallbackServers[nextNtpFallbackServerIndex];
      nextNtpFallbackServerIndex =
        (nextNtpFallbackServerIndex + 1) % ntpFallbackServerCount;
      Serial.print("NTP DNS failed, using fallback IP=");
      Serial.println(requestAddress);
    }
  }

  while (ntpUdp.parsePacket() > 0) {
    while (ntpUdp.available()) {
      ntpUdp.read();
    }
  }

  byte packet[48] = { 0 };
  packet[0] = 0b00100011;  // LI=0, NTP v4, client mode.
  ntpUdp.beginPacket(requestAddress, 123);
  ntpUdp.write(packet, sizeof(packet));
  ntpUdp.endPacket();

  unsigned long requestStarted = millis();
  while (millis() - requestStarted < 2000UL) {
    int packetSize = ntpUdp.parsePacket();
    if (packetSize >= (int)sizeof(packet)) {
      ntpUdp.read(packet, sizeof(packet));
      uint32_t secondsSince1900 =
        ((uint32_t)packet[40] << 24) |
        ((uint32_t)packet[41] << 16) |
        ((uint32_t)packet[42] << 8) |
        (uint32_t)packet[43];

      if (secondsSince1900 >= 2208988800UL) {
        setSystemTime(secondsSince1900 - 2208988800UL);
        Serial.println("NTP update succeeded");
        return true;
      }
    }

    delay(20);
  }

  Serial.println("NTP attempt failed");
  return false;
}

void setSystemTime(int64_t unixTime) {
  if (unixTime < 1700000000LL) {
    return;
  }

  struct timeval currentTime;
  currentTime.tv_sec = (time_t)unixTime;
  currentTime.tv_usec = 0;
  settimeofday(&currentTime, nullptr);
}

bool hasValidTime() {
  return time(nullptr) >= 1700000000;
}

void showTimeWaitingIndicator() {
  showTimeUpdateIndicator();
}

void receiveLocalLinkCommands() {
  int packetSize = udp.parsePacket();
  while (packetSize > 0) {
    char message[64];
    int length = udp.read(message, sizeof(message) - 1);
    if (length > 0) {
      message[length] = '\0';
      lastLocalLinkSeen = millis();

      int month;
      if (sscanf(message, "clock_month:%d", &month) == 1 &&
          month >= 1 && month <= 12) {
        if (displayedClockMonth != month) {
          startClockMonthTransition(month);
        }

        lastClockMonthCommand = millis();
        int currentMonth;
        temporaryClockMonth = !getCurrentClockMonth(currentMonth) ||
          currentMonth != month;
      } else {
        char mode;
        int contentMask;
        int switchSeconds;
        unsigned long long hostUnixMs = 0;
        int displayConfigFields = sscanf(
          message,
          "L,%c,%d,%d,%llu",
          &mode,
          &contentMask,
          &switchSeconds,
          &hostUnixMs
        );
        if (displayConfigFields >= 3) {
          ContentMode newMode = mode == 'S' ? CONTENT_SCROLLING : CONTENT_FIXED;
          bool newTimeEnabled = (contentMask & 1) != 0;
          bool newWeatherEnabled = (contentMask & 2) != 0;
          uint16_t newSwitchSeconds = (uint16_t)constrain(switchSeconds, 3, 60);
          contentMode = newMode;
          contentTimeEnabled = newTimeEnabled;
          contentWeatherEnabled = newWeatherEnabled;
          if (!contentTimeEnabled && !contentWeatherEnabled && contentMode == CONTENT_FIXED) {
            contentTimeEnabled = true;
          }
          contentSwitchSeconds = newSwitchSeconds;
          if (displayConfigFields == 4 && hostUnixMs >= 1700000000000ULL) {
            portENTER_CRITICAL(&contentClockMux);
            contentClockBaseUnixMs = (uint64_t)hostUnixMs;
            contentClockBaseMillis = millis();
            portEXIT_CRITICAL(&contentClockMux);
          }
        } else {
          int temperature;
          int humidity;
          char unit;
          if (sscanf(message, "W,%d,%d,%c", &temperature, &humidity, &unit) == 3) {
            weatherTemperature = (int16_t)constrain(temperature, -99, 999);
            weatherHumidity = (uint8_t)constrain(humidity, 0, 100);
            weatherUnit = unit == 'F' ? 'F' : (unit == 'S' ? '\0' : 'C');
            weatherAvailable = true;
          } else {
            unsigned long countdownSeconds;
            if (sscanf(message, "T,S,%lu", &countdownSeconds) == 1 && countdownSeconds > 0) {
              countdownSeconds = min(countdownSeconds, 359999UL);
              countdownDurationMs = (uint32_t)(countdownSeconds * 1000UL);
              countdownRemainingMs = countdownDurationMs;
              countdownEndsAt = millis() + countdownDurationMs;
              countdownRunning = true;
              countdownPaused = false;
              countdownAlarmActive = false;
            } else if (strcmp(message, "T,P") == 0 && countdownRunning) {
              long remaining = (long)(countdownEndsAt - millis());
              countdownRemainingMs = remaining > 0 ? (uint32_t)remaining : 0;
              countdownRunning = false;
              countdownPaused = countdownRemainingMs > 0;
            } else if (strcmp(message, "T,R") == 0 && countdownPaused) {
              countdownEndsAt = millis() + countdownRemainingMs;
              countdownRunning = true;
              countdownPaused = false;
            } else if (strcmp(message, "T,C") == 0) {
              countdownRunning = false;
              countdownPaused = false;
              countdownAlarmActive = false;
              countdownRemainingMs = 0;
            } else if (strcmp(message, "T,A") == 0) {
              countdownAlarmActive = false;
              countdownRemainingMs = 0;
              countdownDurationMs = 0;
              lastCountdownAlarmSend = 0;
            }
          }
        }
      }
    }

    packetSize = udp.parsePacket();
  }
}

void sendCapabilities() {
  if (WiFi.status() != WL_CONNECTED) {
    return;
  }

  capabilityUdp.beginPacket(IPAddress(255, 255, 255, 255), localLinkPort);
  capabilityUdp.print("{\"tag\":\"esp_capabilities\",\"capabilities\":[\"ws2812\"");
  if (tslAvailable) {
    capabilityUdp.print(",\"tsl2591\"");
  }
  capabilityUdp.print("],\"display\":{\"mode\":\"");
  const char* modeName = "waiting";
  if (displayMode == DISPLAY_CLOCK) modeName = "clock";
  else if (displayMode == DISPLAY_WEATHER) modeName = "weather";
  else if (displayMode == DISPLAY_TIMER) modeName = "timer";
  capabilityUdp.print(modeName);
  capabilityUdp.print("\"}}");
  capabilityUdp.endPacket();
}
