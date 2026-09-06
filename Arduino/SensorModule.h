#pragma once

void initializeLightSensorAndBrightness() {
  tslAvailable = tsl.begin();
  if (!tslAvailable) {
    Serial.println("TSL2591 not found; using minimum brightness");
    return;
  }

  tsl.setGain(TSL2591_GAIN_MED);
  tsl.setTiming(TSL2591_INTEGRATIONTIME_300MS);

  uint32_t data = tsl.getFullLuminosity();
  uint16_t infrared = data >> 16;
  uint16_t full = data & 0xFFFF;
  float lux = tsl.calculateLux(full, infrared);
  if (lux >= 0) {
    float measuredLux = roundf(lux);
    filteredLux = measuredLux;
    appliedLux = measuredLux;
    filteredLuxInitialized = true;
    appliedLuxInitialized = true;

    float constrainedLux = constrain(
      measuredLux,
      (float)MIN_ADAPTIVE_LUX,
      (float)MAX_ADAPTIVE_LUX
    );
    float normalizedLux = (constrainedLux - MIN_ADAPTIVE_LUX) /
      (float)(MAX_ADAPTIVE_LUX - MIN_ADAPTIVE_LUX);
    float perceivedLevel =
      normalizedLux * normalizedLux * (3.0f - 2.0f * normalizedLux);
    float initialBrightness =
      MIN_LED_BRIGHTNESS +
      perceivedLevel * (MAX_LED_BRIGHTNESS - MIN_LED_BRIGHTNESS);

    currentLedBrightness = initialBrightness;
    targetLedBrightness = initialBrightness;
    brightnessLerpStart = initialBrightness;
    brightnessLerpElapsed = 0;
    brightnessLerpActive = false;
    outputLedBrightness = (uint8_t)constrain(
      (int)roundf(initialBrightness),
      MIN_LED_BRIGHTNESS,
      MAX_LED_BRIGHTNESS
    );
  }

  Serial.print("TSL2591 ready, brightness=");
  Serial.println(outputLedBrightness);
}

void sendLux() {
  if (!tslAvailable) {
    return;
  }

  uint32_t data = tsl.getFullLuminosity();
  uint16_t infrared = data >> 16;
  uint16_t full = data & 0xFFFF;
  float lux = tsl.calculateLux(full, infrared);
  int32_t luxInteger = (int32_t)roundf(lux);

  if (lux >= 0) {
    updateLedBrightness(luxInteger);
  }

  if (lux >= 0 && WiFi.status() == WL_CONNECTED) {
    udp.beginPacket(IPAddress(255, 255, 255, 255), localLinkPort);
    udp.print("{\"tag\":\"tsl2591_lux\",\"value\":");
    udp.print(luxInteger);
    udp.print(",\"unit\":\"lux\"}");
    udp.endPacket();
  }
}

void updateLedBrightness(int32_t lux) {
  float measuredLux = max(0.0f, (float)lux);

  if (!filteredLuxInitialized) {
    filteredLux = measuredLux;
    filteredLuxInitialized = true;
  } else {
    filteredLux += (measuredLux - filteredLux) * LUX_EMA_ALPHA;
  }

  if (!appliedLuxInitialized) {
    appliedLux = filteredLux;
    appliedLuxInitialized = true;
  } else {
    // EMA ç›¸å¯¹ä¸Šä¸€æ¬¡å·²åº”ç”¨å€¼ç´¯è®¡å˜åŒ–è¶…è¿‡ 20 luxï¼Œæ‰å¯åŠ¨ä¸€æ¬¡ lerpã€‚
    if (fabsf(filteredLux - appliedLux) <= LUX_CHANGE_THRESHOLD) {
      return;
    }

    // EMA åªè´Ÿè´£ç¡®è®¤å˜åŒ–ã€‚è§¦å‘åŽç›´æŽ¥é‡‡ç”¨å½“å‰æµ‹é‡å€¼ä½œä¸ºæœ¬æ¬¡æœ€ç»ˆç›®æ ‡ï¼Œ
    // å¹¶åŒæ­¥é‡ç½® EMAï¼Œé¿å…ä¸‹é™é€”ä¸­æ¯è·¨è¿‡ 20 lux å°±é‡å¯ä¸€æ¬¡ lerpã€‚
    appliedLux = measuredLux;
    filteredLux = measuredLux;
  }

  float constrainedLux = constrain(
    appliedLux,
    (float)MIN_ADAPTIVE_LUX,
    (float)MAX_ADAPTIVE_LUX
  );
  float normalizedLux = (constrainedLux - MIN_ADAPTIVE_LUX) /
                        (float)(MAX_ADAPTIVE_LUX - MIN_ADAPTIVE_LUX);
  // Smoothstep S curve: changes gently near 20 lux and 200 lux,
  // avoiding the sudden low-end jump caused by pow(x, 0.45).
  float perceivedLevel =
    normalizedLux * normalizedLux * (3.0f - 2.0f * normalizedLux);
  float newTargetBrightness =
    MIN_LED_BRIGHTNESS +
    perceivedLevel * (MAX_LED_BRIGHTNESS - MIN_LED_BRIGHTNESS);

  if (fabsf(newTargetBrightness - targetLedBrightness) < 0.5f) {
    return;
  }

  brightnessLerpStart = currentLedBrightness;
  targetLedBrightness = newTargetBrightness;
  brightnessLerpElapsed = 0;
  brightnessLerpActive = true;
}

void animateLedBrightness() {
  unsigned long now = millis();
  if (now - lastBrightnessAnimation < 10) {
    return;
  }
  unsigned long elapsed = lastBrightnessAnimation == 0
    ? 10
    : now - lastBrightnessAnimation;
  lastBrightnessAnimation = now;

  // Sensor reads and NTP requests can block the loop. Do not let a long pause
  // turn into one large visible brightness jump when execution resumes.
  elapsed = min(elapsed, 50UL);

  if (brightnessLerpActive) {
    brightnessLerpElapsed += elapsed;
    float progress = constrain(
      brightnessLerpElapsed / (float)BRIGHTNESS_RESPONSE_MS,
      0.0f,
      1.0f
    );
    currentLedBrightness = brightnessLerpStart +
      (targetLedBrightness - brightnessLerpStart) * progress;

    if (progress >= 1.0f) {
      currentLedBrightness = targetLedBrightness;
      brightnessLerpActive = false;
    }
  }

  int outputBrightness = (int)roundf(currentLedBrightness);

  outputLedBrightness = (uint8_t)constrain(
    outputBrightness,
    MIN_LED_BRIGHTNESS,
    MAX_LED_BRIGHTNESS
  );
}


