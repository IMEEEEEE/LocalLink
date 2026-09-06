#include <Wire.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <time.h>
#include <sys/time.h>
#include <FastLED.h>
#include <Adafruit_TSL2591.h>

#define LED_PIN 11
#define STATUS_LED_PIN 48
#define BRIGHTNESS 10
#define MIN_LED_BRIGHTNESS 5
#define MAX_LED_BRIGHTNESS 50
#define MIN_ADAPTIVE_LUX 20
#define MAX_ADAPTIVE_LUX 200
#define LUX_EMA_ALPHA 0.35f
#define LUX_CHANGE_THRESHOLD 20.0f
#define BRIGHTNESS_RESPONSE_MS 900
#define CLOCK_COLOR_RESPONSE_MS 450
#define CLOCK_MONTH_RETURN_MS 10000UL
#define MATRIX_WIDTH 32
#define MATRIX_HEIGHT 8
#define PANEL_SIZE 8
#define PANEL_COUNT 4
#define NUM_LEDS (MATRIX_WIDTH * MATRIX_HEIGHT)
#define DIGIT_WIDTH 3
#define DIGIT_HEIGHT 5

const char* ssid = "";
const char* password = "";
const uint16_t localLinkPort = 5124;
const uint16_t espUdpPort = 5125;
const IPAddress primaryDns(1, 1, 1, 1);
const IPAddress secondaryDns(8, 8, 8, 8);
const char* ntpServers[] = {
  "time.cloudflare.com"
};
const uint8_t ntpServerCount = sizeof(ntpServers) / sizeof(ntpServers[0]);
const IPAddress ntpFallbackServers[] = {
  IPAddress(162, 159, 200, 1),
  IPAddress(162, 159, 200, 123)
};
const uint8_t ntpFallbackServerCount =
  sizeof(ntpFallbackServers) / sizeof(ntpFallbackServers[0]);
unsigned long lastNtpTimeAttempt = 0;
uint8_t nextNtpServerIndex = 0;
uint8_t nextNtpFallbackServerIndex = 0;
unsigned long lastLuxSend = 0;

// America/New_York，自动处理夏令时。
const char* timeZone = "EST5EDT,M3.2.0/2,M11.1.0/2";

Adafruit_TSL2591 tsl(2591);
bool tslAvailable = false;
float currentLedBrightness = BRIGHTNESS;
float targetLedBrightness = BRIGHTNESS;
float brightnessLerpStart = BRIGHTNESS;
float filteredLux = MIN_ADAPTIVE_LUX;
float appliedLux = MIN_ADAPTIVE_LUX;
bool filteredLuxInitialized = false;
bool appliedLuxInitialized = false;
bool brightnessLerpActive = false;
uint8_t outputLedBrightness = BRIGHTNESS;
unsigned long lastBrightnessAnimation = 0;
unsigned long brightnessLerpElapsed = 0;
CRGB clockColor = CRGB(0, 255, 128);
CRGB clockColorStart = clockColor;
CRGB clockColorTarget = clockColor;
bool clockColorLerpActive = false;
unsigned long lastClockColorAnimation = 0;
unsigned long clockColorLerpElapsed = 0;
unsigned long lastClockMonthCommand = 0;
unsigned long lastClockMonthCheck = 0;
uint8_t displayedClockMonth = 0;
bool temporaryClockMonth = false;
enum DisplayMode : uint8_t {
  DISPLAY_WAITING,
  DISPLAY_CLOCK,
  DISPLAY_WEATHER,
  DISPLAY_TIMER
};
volatile DisplayMode displayMode = DISPLAY_WAITING;
volatile unsigned long lastLocalLinkSeen = 0;
const unsigned long LOCAL_LINK_TIMEOUT_MS = 15000UL;
enum ContentMode : uint8_t {
  CONTENT_FIXED,
  CONTENT_SCROLLING
};
volatile ContentMode contentMode = CONTENT_FIXED;
volatile bool contentTimeEnabled = true;
volatile bool contentWeatherEnabled = false;
volatile uint16_t contentSwitchSeconds = 10;
volatile uint64_t contentClockBaseUnixMs = 0;
volatile uint32_t contentClockBaseMillis = 0;
portMUX_TYPE contentClockMux = portMUX_INITIALIZER_UNLOCKED;
volatile bool weatherAvailable = false;
volatile int16_t weatherTemperature = 0;
volatile uint8_t weatherHumidity = 0;
volatile char weatherUnit = 'C';
volatile bool countdownRunning = false;
volatile bool countdownPaused = false;
volatile uint32_t countdownDurationMs = 0;
volatile uint32_t countdownRemainingMs = 0;
volatile unsigned long countdownEndsAt = 0;
volatile bool countdownAlarmActive = false;
volatile unsigned long countdownAlarmStarted = 0;
unsigned long lastCountdownAlarmSend = 0;
TaskHandle_t animationTaskHandle = nullptr;
TaskHandle_t mainLogicTaskHandle = nullptr;
TaskHandle_t capabilityTaskHandle = nullptr;
WiFiUDP udp;
WiFiUDP capabilityUdp;
WiFiUDP ntpUdp;
IPAddress ntpServerAddress;
bool ntpServerAddressReady = false;
CRGB LEDs[NUM_LEDS];
CRGB statusLED[1];
CRGB screen[MATRIX_HEIGHT][MATRIX_WIDTH];

// 固定的 WS2812 月份色表；不跟随 LocalLink 的浅色或深色主题。
#include "DisplayAssets.h"

void animationTask(void* parameter);
void mainLogicTask(void* parameter);
void capabilityBroadcastTask(void* parameter);
void composeLocalTime();
void composeFourDigits(int leftValue, int rightValue, bool colonVisible, CRGB color);
uint8_t getConfiguredContentCount(bool includeCountdown);
void composeConfiguredContentPage(uint8_t selectedIndex, bool includeCountdown);
void composeConfiguredContent(unsigned long now, bool includeCountdown);
CRGB getTemperatureColor(int temperature, char unit);
void composeWeather();
void composeCountdown(unsigned long now);
void composeCountdownAlarm(unsigned long now);
void updateCountdownState(unsigned long now);
void composeCountdownFace(unsigned long now);
void composeCountdownProgress();
void addDigit(int digit, int x, int y, CRGB color);
void clearScreen();
void drawScreen();
void connectWiFi();
void initializeLightSensorAndBrightness();
void playStartupAnimation();
void showTimeUpdateIndicator();
void showWiFiWaitingIndicator();
bool syncTimeFromNtpClient();
void maintainNtpTime();
bool updateTimeFromNextNtpServer();
void setSystemTime(int64_t unixTime);
bool hasValidTime();
void showTimeWaitingIndicator();
void receiveLocalLinkCommands();
bool getCurrentClockMonth(int &month);
void startClockMonthTransition(int month);
void maintainClockMonthColor();
void animateClockColor();
void sendLux();
void sendCapabilities();
void updateLedBrightness(int32_t lux);
void animateLedBrightness();
void showLocalTime();
int getLedIndex(int x, int y);

void setup() {
  Serial.begin(115200);
  Wire.begin(8, 9);

  FastLED.addLeds<WS2812, LED_PIN, GRB>(LEDs, NUM_LEDS);
  FastLED.addLeds<WS2812, STATUS_LED_PIN, GRB>(statusLED, 1);
  FastLED.setDither(DISABLE_DITHER);

  initializeLightSensorAndBrightness();
  FastLED.setBrightness(outputLedBrightness);
  playStartupAnimation();
  displayMode = DISPLAY_WAITING;
  xTaskCreatePinnedToCore(
    animationTask,
    "ws2812-animation",
    6144,
    nullptr,
    2,
    &animationTaskHandle,
    1
  );
  xTaskCreatePinnedToCore(
    mainLogicTask,
    "main-logic",
    8192,
    nullptr,
    1,
    &mainLogicTaskHandle,
    0
  );
}

void loop() {
  delay(1000);
}

void mainLogicTask(void* parameter) {
  connectWiFi();
  udp.begin(espUdpPort);

  xTaskCreatePinnedToCore(
    capabilityBroadcastTask,
    "capability-broadcast",
    3072,
    nullptr,
    1,
    &capabilityTaskHandle,
    1
  );

  // Time sync is intentionally lower priority so LocalLink can discover the
  // ESP and receive its first sensor value before any NTP wait begins.
  if (tslAvailable) {
    sendLux();
    lastLuxSend = millis();
  }

  setenv("TZ", timeZone, 1);
  tzset();
  ntpUdp.begin(2391);
  syncTimeFromNtpClient();

  if (hasValidTime()) {
    time_t currentTime = time(nullptr);
    struct tm currentTimeInfo;
    localtime_r(&currentTime, &currentTimeInfo);
    clockColor = clockMonthColors[currentTimeInfo.tm_mon];
    clockColorStart = clockColor;
    clockColorTarget = clockColor;
    displayedClockMonth = currentTimeInfo.tm_mon + 1;
    Serial.println("Time ready");
    displayMode = DISPLAY_CLOCK;
  } else {
    Serial.println("Time is not available yet");
  }

  while (true) {
    if (WiFi.status() != WL_CONNECTED) {
      connectWiFi();
    }

    maintainNtpTime();
    receiveLocalLinkCommands();
    maintainClockMonthColor();

    unsigned long now = millis();

    if (countdownAlarmActive &&
        (lastCountdownAlarmSend == 0 || now - lastCountdownAlarmSend >= 1000UL)) {
      lastCountdownAlarmSend = now;
      udp.beginPacket(IPAddress(255, 255, 255, 255), localLinkPort);
      udp.print("T,D");
      udp.endPacket();
    }

    if (lastLuxSend == 0 || now - lastLuxSend >= 1000) {
      lastLuxSend = now;
      sendLux();
    }

    delay(10);
  }
}

void capabilityBroadcastTask(void* parameter) {
  capabilityUdp.begin(5126);

  while (true) {
    if (WiFi.status() == WL_CONNECTED) {
      sendCapabilities();
    }
    vTaskDelay(pdMS_TO_TICKS(5000));
  }
}

void animationTask(void* parameter) {
  TickType_t nextFrame = xTaskGetTickCount();

  while (true) {
    animateClockColor();
    animateLedBrightness();

    if (!hasValidTime()) {
      displayMode = DISPLAY_WAITING;
      showWiFiWaitingIndicator();
      vTaskDelayUntil(&nextFrame, pdMS_TO_TICKS(10));
      continue;
    }

    unsigned long now = millis();
    bool localLinkOnline = lastLocalLinkSeen != 0 &&
      now - lastLocalLinkSeen < LOCAL_LINK_TIMEOUT_MS;

    clearScreen();
    if (!localLinkOnline) {
      displayMode = DISPLAY_CLOCK;
      composeLocalTime();
    } else {
      updateCountdownState(now);
      if (countdownAlarmActive) {
        composeCountdownAlarm(now);
      } else if ((countdownRunning || countdownPaused) && contentMode == CONTENT_SCROLLING) {
        composeConfiguredContent(now, true);
        composeCountdownProgress();
      } else if (countdownRunning || countdownPaused) {
        displayMode = DISPLAY_TIMER;
        composeCountdown(now);
      } else {
        composeConfiguredContent(now, false);
      }
    }
    drawScreen();

    vTaskDelayUntil(&nextFrame, pdMS_TO_TICKS(10));
  }
}

#include "NetworkModule.h"
#include "SensorModule.h"
#include "DisplayModule.h"
