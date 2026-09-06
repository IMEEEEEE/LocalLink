#pragma once

void playStartupAnimation() {
  FastLED.clear(true);

  // A short center-out white self-test across all eight rows.
  for (int step = 0; step < MATRIX_WIDTH / 2; step++) {
    clearScreen();

    int left = MATRIX_WIDTH / 2 - 1 - step;
    int right = MATRIX_WIDTH / 2 + step;
    CRGB color = CRGB::White;
    for (int y = 0; y < MATRIX_HEIGHT; y++) {
      screen[y][left] = color;
      screen[y][right] = color;
    }

    drawScreen();
    delay(28);
  }

  for (int step = 0; step < 6; step++) {
    fadeToBlackBy(LEDs, NUM_LEDS, 48);
    FastLED.show();
    delay(28);
  }

  clearScreen();
  drawScreen();
}

void showTimeUpdateIndicator() {
  showWiFiWaitingIndicator();
}

void showWiFiWaitingIndicator() {
  clearScreen();

  struct SpinnerPixel {
    uint8_t x;
    uint8_t y;
  };

  // Clockwise path around the outer edge of a centered 6x6 square.
  static const SpinnerPixel squarePath[20] = {
    { 13, 1 }, { 14, 1 }, { 15, 1 }, { 16, 1 }, { 17, 1 }, { 18, 1 },
    { 18, 2 }, { 18, 3 }, { 18, 4 }, { 18, 5 }, { 18, 6 },
    { 17, 6 }, { 16, 6 }, { 15, 6 }, { 14, 6 }, { 13, 6 },
    { 13, 5 }, { 13, 4 }, { 13, 3 }, { 13, 2 }
  };

  int head = (millis() / 70UL) % 20;
  for (int index = 0; index < 20; index++) {
    int distanceBehindHead = (head - index + 20) % 20;
    int fadedLevel = 255 - distanceBehindHead * 18;
    uint8_t level = (uint8_t)max(20, fadedLevel);
    CRGB color = CRGB(level, level, level);
    screen[squarePath[index].y][squarePath[index].x] = color;
  }

  drawScreen();
}

bool getCurrentClockMonth(int &month) {
  if (!hasValidTime()) {
    return false;
  }

  time_t currentTime = time(nullptr);
  struct tm currentTimeInfo;
  localtime_r(&currentTime, &currentTimeInfo);
  month = currentTimeInfo.tm_mon + 1;
  return true;
}

void startClockMonthTransition(int month) {
  int normalizedMonth = constrain(month, 1, 12);
  displayedClockMonth = normalizedMonth;
  clockColorStart = clockColor;
  clockColorTarget = clockMonthColors[normalizedMonth - 1];
  clockColorLerpElapsed = 0;
  lastClockColorAnimation = millis();
  clockColorLerpActive = true;
}

void maintainClockMonthColor() {
  unsigned long now = millis();
  if (now - lastClockMonthCheck < 250) {
    return;
  }
  lastClockMonthCheck = now;

  int currentMonth;
  if (!getCurrentClockMonth(currentMonth)) {
    return;
  }

  if (temporaryClockMonth &&
      now - lastClockMonthCommand < CLOCK_MONTH_RETURN_MS) {
    return;
  }

  temporaryClockMonth = false;
  if (displayedClockMonth != currentMonth) {
    startClockMonthTransition(currentMonth);
  }
}

void animateClockColor() {
  if (!clockColorLerpActive) {
    return;
  }

  unsigned long now = millis();
  unsigned long elapsed = now - lastClockColorAnimation;
  if (elapsed < 10) {
    return;
  }
  lastClockColorAnimation = now;

  // TSL2591 æˆ– NTP é˜»å¡žåŽä¹Ÿåªå‰è¿›ä¸€å°æ­¥ï¼Œä¿è¯é¢œè‰²è¿‡æ¸¡è¿žç»­ã€‚
  clockColorLerpElapsed += min(elapsed, 50UL);
  float progress = constrain(
    clockColorLerpElapsed / (float)CLOCK_COLOR_RESPONSE_MS,
    0.0f,
    1.0f
  );

  clockColor.r = (uint8_t)roundf(
    clockColorStart.r + ((int)clockColorTarget.r - clockColorStart.r) * progress
  );
  clockColor.g = (uint8_t)roundf(
    clockColorStart.g + ((int)clockColorTarget.g - clockColorStart.g) * progress
  );
  clockColor.b = (uint8_t)roundf(
    clockColorStart.b + ((int)clockColorTarget.b - clockColorStart.b) * progress
  );

  if (progress >= 1.0f) {
    clockColor = clockColorTarget;
    clockColorLerpActive = false;
  }
}

void showLocalTime() {
  time_t currentTime = time(nullptr);
  if (currentTime < 1700000000) {
    return;
  }

  struct tm timeInfo;
  localtime_r(&currentTime, &timeInfo);

  clearScreen();

  CRGB color = CRGB::White;
  const int clockWidth = DIGIT_WIDTH * 4 + 7;
  const int clockStartX = (MATRIX_WIDTH - clockWidth) / 2;
  const int clockStartY = (MATRIX_HEIGHT - DIGIT_HEIGHT) / 2;
  const int timeDigits[4] = {
    timeInfo.tm_hour / 10,
    timeInfo.tm_hour % 10,
    timeInfo.tm_min / 10,
    timeInfo.tm_min % 10
  };

  // å°† HH:MM ä½œä¸ºä¸€ä¸ªæ•´ä½“å±…ä¸­ï¼›æ•°å­—ä¹‹é—´ç•™ 1 åˆ—ï¼Œå†’å·ä¸¤ä¾§å„ç•™ 1 åˆ—ã€‚
  addDigit(timeDigits[0], clockStartX, clockStartY, color);
  addDigit(timeDigits[1], clockStartX + 4, clockStartY, color);
  addDigit(timeDigits[2], clockStartX + 12, clockStartY, color);
  addDigit(timeDigits[3], clockStartX + 16, clockStartY, color);

  // å†’å·å ä½ 3 åˆ—ï¼Œä½†åªç‚¹äº®ä¸­é—´ 1 åˆ—ï¼›äº® 2 ç§’ã€ç­ 1 ç§’ã€‚
  if (millis() % 3000 < 2000) {
    int colonX = clockStartX + 9;
    screen[clockStartY + 1][colonX] = color;
    screen[clockStartY + 3][colonX] = color;
  }

  drawScreen();
}

void composeLocalTime() {
  time_t currentTime = time(nullptr);
  if (currentTime < 1700000000) {
    return;
  }
  struct tm timeInfo;
  localtime_r(&currentTime, &timeInfo);
  composeFourDigits(
    timeInfo.tm_hour,
    timeInfo.tm_min,
    millis() % 3000UL < 2000UL,
    CRGB::White
  );
  displayMode = DISPLAY_CLOCK;
}

void composeFourDigits(int leftValue, int rightValue, bool colonVisible, CRGB color) {
  const int startX = (MATRIX_WIDTH - (DIGIT_WIDTH * 4 + 7)) / 2;
  const int startY = (MATRIX_HEIGHT - DIGIT_HEIGHT) / 2;
  const int digits[4] = {
    (leftValue / 10) % 10,
    leftValue % 10,
    (rightValue / 10) % 10,
    rightValue % 10
  };
  addDigit(digits[0], startX, startY, color);
  addDigit(digits[1], startX + 4, startY, color);
  addDigit(digits[2], startX + 12, startY, color);
  addDigit(digits[3], startX + 16, startY, color);
  if (colonVisible) {
    screen[startY + 1][startX + 9] = color;
    screen[startY + 3][startX + 9] = color;
  }
}

uint8_t getConfiguredContentCount(bool includeCountdown) {
  if (contentMode == CONTENT_FIXED) {
    return 1;
  }
  uint8_t count = 0;
  if (contentTimeEnabled) count++;
  if (contentWeatherEnabled && weatherAvailable) count++;
  if (includeCountdown) count++;
  return max((uint8_t)1, count);
}

void composeConfiguredContentPage(uint8_t selectedIndex, bool includeCountdown) {
  if (contentMode == CONTENT_FIXED) {
    if (contentWeatherEnabled && weatherAvailable) {
      composeWeather();
    } else {
      composeLocalTime();
    }
    return;
  }

  uint8_t currentIndex = 0;
  if (contentTimeEnabled) {
    if (selectedIndex == currentIndex) {
      composeLocalTime();
      return;
    }
    currentIndex++;
  }
  if (contentWeatherEnabled && weatherAvailable) {
    if (selectedIndex == currentIndex) {
      composeWeather();
      return;
    }
    currentIndex++;
  }
  if (includeCountdown && selectedIndex == currentIndex) {
    composeCountdownFace(millis());
    return;
  }
  displayMode = DISPLAY_CLOCK;
}

void composeConfiguredContent(unsigned long now, bool includeCountdown) {
  uint8_t count = getConfiguredContentCount(includeCountdown);
  uint8_t selectedIndex = 0;
  if (contentMode == CONTENT_SCROLLING && count > 1) {
    uint64_t hostUnixMs;
    uint32_t hostClockReceivedAt;
    portENTER_CRITICAL(&contentClockMux);
    hostUnixMs = contentClockBaseUnixMs;
    hostClockReceivedAt = contentClockBaseMillis;
    portEXIT_CRITICAL(&contentClockMux);

    uint64_t rotationTimeMs = hostUnixMs != 0
      ? hostUnixMs + (uint32_t)(now - hostClockReceivedAt)
      : (uint64_t)time(nullptr) * 1000ULL;
    selectedIndex = (rotationTimeMs / (contentSwitchSeconds * 1000ULL)) % count;
  }
  composeConfiguredContentPage(selectedIndex, includeCountdown);
}

CRGB getTemperatureColor(int temperature, char unit) {
  float celsius = (float)temperature;
  if (unit == 'F') {
    celsius = (celsius - 32.0f) * 5.0f / 9.0f;
  } else if (unit == '\0') {
    celsius -= 273.15f;
  }

  // Use Calendar's daytime palette in reverse; green starts at 23 C.
  float clampedCelsius = constrain(celsius, -20.0f, 30.0f);
  float palettePosition;
  if (clampedCelsius >= 23.0f) {
    palettePosition = (30.0f - clampedCelsius) / 7.0f * 4.0f;
  } else if (clampedCelsius >= 15.0f) {
    palettePosition = 4.0f + (23.0f - clampedCelsius) / 8.0f * 3.0f;
  } else {
    palettePosition = 7.0f + (15.0f - clampedCelsius) / 35.0f * 4.0f;
  }
  uint8_t startIndex = constrain((int)floorf(palettePosition), 0, 11);
  uint8_t endIndex = min((int)startIndex + 1, 11);
  float localPosition = palettePosition - startIndex;
  CRGB startColor = clockMonthColors[startIndex];
  CRGB endColor = clockMonthColors[endIndex];

  return CRGB(
    (uint8_t)roundf(startColor.r + ((int)endColor.r - startColor.r) * localPosition),
    (uint8_t)roundf(startColor.g + ((int)endColor.g - startColor.g) * localPosition),
    (uint8_t)roundf(startColor.b + ((int)endColor.b - startColor.b) * localPosition)
  );
}

void composeWeather() {
  char temperatureText[6];
  snprintf(temperatureText, sizeof(temperatureText), "%d", (int)weatherTemperature);
  CRGB temperatureColor = getTemperatureColor(weatherTemperature, weatherUnit);
  int cursor = 2;
  for (int index = 0; temperatureText[index] != '\0'; index++) {
    char character = temperatureText[index];
    if (character == '-') {
      for (int column = 0; column < 3; column++) screen[3][cursor + column] = temperatureColor;
    } else if (character >= '0' && character <= '9') {
      addDigit(character - '0', cursor, 1, temperatureColor);
    }
    cursor += 4;
  }

  screen[1][cursor] = temperatureColor;
  screen[1][cursor + 1] = temperatureColor;
  screen[2][cursor] = temperatureColor;
  screen[2][cursor + 1] = temperatureColor;
  cursor += 2;

  if (weatherUnit != '\0') {
    const uint8_t cRows[5] = { 0b111, 0b100, 0b100, 0b100, 0b111 };
    const uint8_t fRows[5] = { 0b111, 0b100, 0b110, 0b100, 0b100 };
    const uint8_t* unitRows = weatherUnit == 'F' ? fRows : cRows;
    for (int row = 0; row < 5; row++) {
      for (int column = 0; column < 3; column++) {
        if ((unitRows[row] >> (2 - column)) & 1) screen[1 + row][cursor + column] = temperatureColor;
      }
    }
  }

  char humidityText[4];
  snprintf(humidityText, sizeof(humidityText), "%d", (int)weatherHumidity);
  int humidityLength = strlen(humidityText);
  cursor = MATRIX_WIDTH - (humidityLength * 4 + 3) - 2;
  CRGB humidityColor = CRGB(70, 190, 235);
  for (int index = 0; index < humidityLength; index++) {
    addDigit(humidityText[index] - '0', cursor, 1, humidityColor);
    cursor += 4;
  }
  const uint8_t percentRows[5] = { 0b101, 0b001, 0b010, 0b100, 0b101 };
  for (int row = 0; row < 5; row++) {
    for (int column = 0; column < 3; column++) {
      if ((percentRows[row] >> (2 - column)) & 1) screen[1 + row][cursor + column] = humidityColor;
    }
  }
  displayMode = DISPLAY_WEATHER;
}

void updateCountdownState(unsigned long now) {
  if (countdownRunning) {
    long signedRemaining = (long)(countdownEndsAt - now);
    if (signedRemaining <= 0) {
      countdownRunning = false;
      countdownPaused = false;
      countdownRemainingMs = 0;
      countdownAlarmActive = true;
      countdownAlarmStarted = now;
      return;
    }
    countdownRemainingMs = (uint32_t)signedRemaining;
  }
}

void composeCountdownFace(unsigned long now) {
  uint32_t remainingMs = countdownRemainingMs;
  uint32_t totalSeconds = (remainingMs + 999UL) / 1000UL;
  if (totalSeconds >= 3600UL) {
    composeFourDigits(min((uint32_t)99, totalSeconds / 3600UL), (totalSeconds / 60UL) % 60UL, true, CRGB::White);
  } else {
    composeFourDigits(totalSeconds / 60UL, totalSeconds % 60UL, true, CRGB::White);
  }

  displayMode = DISPLAY_TIMER;
}

void composeCountdownProgress() {
  if (countdownDurationMs == 0) return;
  uint8_t progressPixels = (uint8_t)min(
    (uint32_t)MATRIX_WIDTH,
    (uint32_t)(((uint64_t)countdownRemainingMs * MATRIX_WIDTH + countdownDurationMs - 1) / countdownDurationMs)
  );
  for (uint8_t x = 0; x < progressPixels; x++) screen[MATRIX_HEIGHT - 1][x] = CRGB::White;
}

void composeCountdown(unsigned long now) {
  updateCountdownState(now);
  if (countdownAlarmActive) {
    composeCountdownAlarm(now);
    return;
  }
  composeCountdownFace(now);
  composeCountdownProgress();
}

void composeCountdownAlarm(unsigned long now) {
  bool includeCountdown = contentMode == CONTENT_SCROLLING;
  composeConfiguredContent(now, includeCountdown);
  bool redVisible = ((now - countdownAlarmStarted) % 500UL) < 250UL;
  if (redVisible) {
    for (uint8_t y = 0; y < MATRIX_HEIGHT; y++) {
      for (uint8_t x = 0; x < MATRIX_WIDTH; x++) {
        if (screen[y][x].r != 0 || screen[y][x].g != 0 || screen[y][x].b != 0) {
          screen[y][x] = CRGB(255, 0, 0);
        }
      }
    }
  }
}

void addDigit(int digit, int x, int y, CRGB color) {
  for (int row = 0; row < 5; row++) {
    for (int column = 0; column < 3; column++) {
      if ((digitRows[digit][row] >> (2 - column)) & 1) {
        screen[y + row][x + column] = color;
      }
    }
  }
}

void clearScreen() {
  for (int y = 0; y < MATRIX_HEIGHT; y++) {
    for (int x = 0; x < MATRIX_WIDTH; x++) {
      screen[y][x] = CRGB::Black;
    }
  }
}

void drawScreen() {
  for (int y = 0; y < MATRIX_HEIGHT; y++) {
    for (int x = 0; x < MATRIX_WIDTH; x++) {
      LEDs[getLedIndex(x, y)] = screen[y][x];
    }
  }

  const bool wifiConnected = WiFi.status() == WL_CONNECTED;
  const unsigned long now = millis();
  const bool localLinkOnline = lastLocalLinkSeen != 0 &&
    now - lastLocalLinkSeen < LOCAL_LINK_TIMEOUT_MS;

  CRGB statusColor;
  if (!wifiConnected) {
    statusColor = CRGB::Red;
  } else if (!localLinkOnline) {
    statusColor = CRGB(255, 190, 0);
  } else {
    statusColor = CRGB::Green;
  }
  statusLED[0] = statusColor;

  FastLED.setBrightness(outputLedBrightness);
  FastLED.show();
}

int getLedIndex(int x, int y) {
  // ç‰©ç†æ¿ä»Žå·¦åˆ°å³ç¼–å·ï¼Œä½†æ•°æ®ä»Žæœ€å³è¾¹çš„æ¿è¿›å…¥ã€‚
  int panelFromLeft = x / PANEL_SIZE;
  int panelFromRight = PANEL_COUNT - 1 - panelFromLeft;
  int localX = x % PANEL_SIZE;

  // æ¯å—æ¿æŒ‰åˆ—èµ°çº¿ï¼šä»Žå³ä¸Šè§’å¼€å§‹ï¼Œæ¯åˆ—ä»Žä¸Šå‘ä¸‹ï¼Œæœ€åŽåˆ°å·¦ä¸‹è§’ã€‚
  int indexInsidePanel = (PANEL_SIZE - 1 - localX) * PANEL_SIZE + y;
  return panelFromRight * PANEL_SIZE * PANEL_SIZE + indexInsidePanel;
}

