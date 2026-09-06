using System.Buffers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace LocalLink
{
    public sealed partial class MainWindow : Window
    {
        private sealed record HourlyForecastPoint(
            string Time,
            double Temperature,
            int PrecipitationProbability,
            int WeatherCode);

        private sealed record DailyForecastPoint(
            DateOnly Date,
            double High,
            double Low,
            int WeatherCode,
            int PrecipitationProbability,
            DateTime Sunrise,
            DateTime Sunset);

        private readonly LocalLinkSettings _settings;
        private readonly LocalServer _server;
        private readonly LocalLinkStatus _status = new();
        private readonly MemoStore _memoStore;
        private readonly Dictionary<int, MemoCalendarDay> _memoCalendarDays = new();
        private readonly List<MemoCalendarCell> _memoCalendarCells = new();
        private readonly HttpClient _httpClient = new();
        private readonly UISettings _uiSettings = new();
        private AppWindow? _appWindow;
        private TrayIcon? _trayIcon;
        private IntPtr _windowHandle;
        private IntPtr _previousWindowProc;
        private WindowProc? _windowProc;
        private bool _isExiting;
        private DispatcherQueueTimer? _locationTimer;
        private DispatcherQueueTimer? _weatherTimer;
        private bool _isInitializing = true;
        private bool _isUpdatingStartupToggle;
        private bool _isCheckingLocation;
        private bool _isCheckingWeather;
        private int _openWeatherRequestCount;
        private bool _tokensLocked;
        private DispatcherQueueTimer? _memoDateTimer;
        private DispatcherQueueTimer? _memoMonthHoverTimer;
        private DispatcherQueueTimer? _ws2812PreviewTimer;
        private DispatcherQueueTimer? _forecastDayReturnTimer;
        private readonly List<HourlyForecastPoint> _hourlyForecast = [];
        private readonly List<DailyForecastPoint> _dailyForecast = [];
        private readonly List<Rectangle> _hourlyPrecipitationBars = [];
        private readonly List<TextBlock> _hourlyPrecipitationLabels = [];
        private readonly List<TextBlock> _hourlyAxisLabels = [];
        private readonly List<UIElement> _hourlyCurrentTimeMarkerElements = [];
        private Image? _hourlyDaylightBackground;
        private Microsoft.UI.Xaml.Shapes.Path? _hourlyTemperatureCurve;
        private LinearGradientBrush? _hourlyTemperatureBrush;
        private EventHandler<object>? _hourlyForecastRenderingHandler;
        private Storyboard? _hourlyChromeTransition;
        private Storyboard? _hourlyDaylightStretchTransition;
        private Image? _hourlyOutgoingDaylightBackground;
        private readonly List<UIElement> _hourlyOutgoingChromeElements = [];
        private Storyboard? _hourlyHoverStoryboard;
        private EventHandler<object>? _hourlyHoverTrackingHandler;
        private double _hourlyHoverTargetX;
        private DateTimeOffset _hourlyHoverLastFrameAt;
        private Line? _hourlyHoverLine;
        private TextBlock? _hourlyHoverLabel;
        private SolidColorBrush? _hourlyHoverBrush;
        private Windows.UI.Color[] _hourlyBackgroundColumnColors = [];
        private bool _hourlyHoverVisible;
        private int _selectedForecastDay;
        private readonly List<Ellipse> _ws2812PreviewPixels = new(256);
        private readonly List<Line> _ws2812PanelSeparators = new(3);
        private readonly Rectangle _ws2812PanelOutline = new();
        private readonly SolidColorBrush _ws2812OffBrush = new(Windows.UI.Color.FromArgb(255, 24, 24, 24));
        private readonly SolidColorBrush _ws2812WhiteBrush = new(Microsoft.UI.Colors.White);
        private readonly SolidColorBrush _ws2812RedBrush = new(Microsoft.UI.Colors.Red);
        private DateTimeOffset? _ws2812TimerEndsAt;
        private DateTimeOffset? _ws2812TimerFinishedAt;
        private TimeSpan _ws2812TimerOriginalDuration;
        private TimeSpan _ws2812PausedTimerRemaining;
        private bool _ws2812TimerPaused;
        private bool _ws2812TimerAlarmDialogOpen;
        private string? _lastLatitude;
        private string? _lastLongitude;
        private string? _lastCity;
        private string? _lastState;
        private int _selectedMemoYear = DateTimeOffset.Now.Year;
        private int _selectedMemoMonth = DateTimeOffset.Now.Month;
        private DateOnly _currentMemoDate = DateOnly.FromDateTime(DateTime.Now);
        private int _highlightedMemoMonth = DateTimeOffset.Now.Month;
        private int? _dragHoverMemoMonth;
        private int? _pointerHoverMemoMonth;
        private int? _pendingMemoHoverMonth;
        private readonly Dictionary<string, CameraMonitor> _cameraMonitors = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CameraMonitor> _cameraMonitorOrder = new();
        private Border? _cameraDropIndicator;
        private CameraMonitor? _fullscreenCameraMonitor;
        private CameraMonitor? _draggingCameraMonitor;
        private CameraMonitor? _lastClickedCameraMonitor;
        private DateTimeOffset _lastCameraTileClickTime;
        private Point _lastCameraTileClickPoint;
        private Point _cameraDragStartPoint;
        private int _cameraDragSourceIndex = -1;
        private int _cameraDragTargetIndex = -1;
        private bool _isCameraTileDragging;
        private bool _isCompletingCameraTileDrag;
        private bool _isScanningCamera;
        private const int Amb82DiscoveryPort = 2390;
        private const int CameraReconnectSeconds = 30;
        private const double CameraTileSpacing = 12;
        private const double CameraTileCornerRadius = 8;
        private const double CameraTileChromeInset = 3;
        private const double CameraTileChromeBarHeight = 28;
        private const double CameraTileControlSize = 22;
        private const double CameraTileControlCornerRadius = CameraTileCornerRadius - CameraTileChromeInset;
        private const double CameraTileIconSize = 12;
        private const double CameraTileDoubleClickMaxDistance = 18;
        private const double PageEdgeInset = 20;
        private static readonly TimeSpan CameraTileDoubleClickThreshold = TimeSpan.FromMilliseconds(420);
        private static readonly TimeSpan Amb82DiscoveryQuietPeriod = TimeSpan.FromMilliseconds(450);
        private static readonly string[] MemoWeekdayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        private static readonly (byte R, byte G, byte B)[] MemoLightMonthColors =
        [
            (218, 78, 78), (218, 124, 78), (218, 160, 78), (218, 203, 78),
            (183, 218, 78), (78, 218, 122), (78, 218, 157), (62, 218, 200),
            (78, 174, 218), (78, 131, 218), (78, 98, 218), (98, 78, 218)
        ];
        private static readonly (byte R, byte G, byte B)[] MemoDarkMonthColors =
        [
            (194, 116, 116), (194, 142, 116), (194, 162, 116), (194, 186, 116),
            (174, 194, 116), (116, 194, 142), (116, 194, 162), (106, 194, 184),
            (116, 170, 194), (116, 146, 194), (116, 128, 194), (128, 116, 194)
        ];
        private static readonly (byte R, byte G, byte B)[] Ws2812MonthColors =
        [
            (218, 78, 78), (218, 124, 78), (218, 160, 78), (218, 203, 78),
            (183, 218, 78), (78, 218, 122), (78, 218, 157), (62, 218, 200),
            (78, 174, 218), (78, 131, 218), (78, 98, 218), (98, 78, 218)
        ];

        public MainWindow()
        {
            InitializeComponent();
            SetInitialWindowSize();
            CameraPreviewGrid.SizeChanged += (_, _) => ApplyCameraTileSizing();
            RootNavigationView.SizeChanged += (_, _) =>
            {
                UpdateCameraPreviewContainerHeight();
                UpdateExpandablePageHeights();
            };
            CamerasPage.SizeChanged += (_, _) => UpdateCameraPreviewContainerHeight();

            _settings = LocalLinkSettings.LoadOrCreate();
            _memoStore = MemoStore.LoadOrCreate();
            _server = new LocalServer(_settings, _status, _memoStore);
            _status.Changed += Status_Changed;
            InitializeTrayIcon();

            ApplyAppThemeFromWindows();
            InitializeMemoCalendar();
            StartMemoDateTimer();
            InitializeWs2812Preview();
            InitializeWs2812DisplayContent();
            OpenWeatherKeyBox.Text = _settings.OpenWeatherApiKey ?? "";
            SelectWindowsTheme(_settings.WindowsThemeMode == "auto"
                ? "auto"
                : WindowsThemeService.GetCurrentMode());
            SelectWeatherUnits(_settings.WeatherUnits);
            AutoThemeOpenWeatherToggle.IsOn = _settings.AutoThemeUseOpenWeather;
            AutoThemeEspToggle.IsOn = _settings.AutoThemeUseEsp;
            DarkBelowLuxNumberBox.Value = _settings.AutoThemeDarkBelowLux;
            LightAboveLuxNumberBox.Value = _settings.AutoThemeLightAboveLux;
            UpdateAutoThemeOptionsVisibility();
            UpdateEspCapabilityVisibility();
            StartupToggleSwitch.IsOn = _settings.StartWithWindows;
            _tokensLocked = HasWeatherKey();
            ApplyTokenEditState();
            UpdateTokenButtonState();
            _isInitializing = false;
            _ = InitializeStartupToggleAsync();

            Closed += MainWindow_Closed;
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            _ = StartServerAsync();
            _ = StartAutomaticChecksIfConfiguredAsync();
            this.AppWindow.SetIcon("Assets/LocalLink.ico");
        }

        private void SetInitialWindowSize()
        {
            _windowHandle = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Closing += AppWindow_Closing;
            InstallCloseHandler();

            _appWindow.Resize(new SizeInt32(1366, 1024));
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new TrayIcon(
                "LocalLink",
                "Assets/LocalLink.ico",
                () => DispatcherQueue.TryEnqueue(ShowWindowFromTray),
                () => DispatcherQueue.TryEnqueue(ExitApplication));
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExiting)
            {
                return;
            }

            args.Cancel = true;
            sender.Hide();
        }

        private void InstallCloseHandler()
        {
            _windowProc = WindowMessageHandler;
            _previousWindowProc = SetWindowLongPtr(_windowHandle, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_windowProc));
        }

        private IntPtr WindowMessageHandler(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam)
        {
            if (msg == WmClose && !_isExiting)
            {
                _appWindow?.Hide();
                return IntPtr.Zero;
            }

            return CallWindowProc(_previousWindowProc, hwnd, msg, wParam, lParam);
        }

        private void ShowWindowFromTray()
        {
            _appWindow?.Show();
            Activate();
        }

        public void ShowFromExternalActivation()
        {
            ShowWindowFromTray();
        }

        private void ExitApplication()
        {
            _isExiting = true;
            _trayIcon?.Dispose();
            Close();
        }

        private async Task StartServerAsync()
        {
            try
            {
                await _server.StartAsync();
            }
            catch (Exception){}
        }

        private async Task StartAutomaticChecksIfConfiguredAsync()
        {
            if (!HasWeatherKey())
            {
                return;
            }

            StartTimers();
            await RefreshLocationAsync();
            await RefreshWeatherAsync();
        }

        private async void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _locationTimer?.Stop();
            _weatherTimer?.Stop();
            _memoDateTimer?.Stop();
            _memoMonthHoverTimer?.Stop();
            _ws2812PreviewTimer?.Stop();
            _forecastDayReturnTimer?.Stop();
            StopHourlyForecastMorph();
            StopHourlyChromeTransition();
            StopHourlyHoverTracking();
            await StopAllCamerasAsync();
            _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
            _status.Changed -= Status_Changed;
            if (_windowHandle != IntPtr.Zero && _previousWindowProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_windowHandle, GwlWndProc, _previousWindowProc);
                _previousWindowProc = IntPtr.Zero;
            }

            if (_appWindow is not null)
            {
                _appWindow.Closing -= AppWindow_Closing;
            }

            _trayIcon?.Dispose();
            _httpClient.Dispose();
            await _server.DisposeAsync();
        }

        private void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var currentMode = WindowsThemeService.GetCurrentMode();
                ApplyAppThemeFromWindows(currentMode);
                RenderMemoCalendar();
                if (_settings.WindowsThemeMode == "auto")
                {
                    _status.NotifyThemeChanged();
                    return;
                }

                _isInitializing = true;
                SelectWindowsTheme(currentMode);
                _isInitializing = false;
                _settings.WindowsThemeMode = currentMode;
                _settings.Save();
                _status.NotifyThemeChanged();
            });
        }

        private void TokenBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            UpdateTokenButtonState();
        }

        private async void SetTokensButton_Click(object sender, RoutedEventArgs e)
        {
            if (_tokensLocked)
            {
                _tokensLocked = false;
                ApplyTokenEditState();
                UpdateTokenButtonState();
                return;
            }

            if (!HasWeatherKey())
            {
                return;
            }

            _settings.OpenWeatherApiKey = OpenWeatherKeyBox.Text.Trim();
            _settings.Save();
            _tokensLocked = true;
            ApplyTokenEditState();

            StartTimers();
            await RefreshLocationAsync();
            await RefreshWeatherAsync();
            UpdateTokenButtonState();
        }

        private void StartTimers()
        {
            _locationTimer?.Stop();
            _weatherTimer?.Stop();

            _locationTimer = DispatcherQueue.CreateTimer();
            _locationTimer.Interval = TimeSpan.FromHours(1);
            _locationTimer.Tick += async (_, _) =>
            {
                if (await RefreshLocationAsync())
                {
                    await RefreshWeatherAsync();
                }
            };
            _locationTimer.Start();

            _weatherTimer = DispatcherQueue.CreateTimer();
            _weatherTimer.Interval = TimeSpan.FromMinutes(10);
            _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
            _weatherTimer.Start();
        }

        private void StopTimers()
        {
            _locationTimer?.Stop();
            _weatherTimer?.Stop();
        }

        private async Task<bool> RefreshLocationAsync(bool notify = true)
        {
            if (_isCheckingLocation || string.IsNullOrWhiteSpace(_settings.OpenWeatherApiKey))
            {
                return false;
            }

            _isCheckingLocation = true;
            try
            {
                var location = await RequestLocationAsync(_settings.OpenWeatherApiKey);
                ApplyLocation(location, notify);
                return true;
            }
            catch (Exception){}
            finally
            {
                _isCheckingLocation = false;
            }

            return false;
        }

        private async Task<bool> RefreshWeatherAsync(bool notify = true)
        {
            if (_isCheckingWeather || string.IsNullOrWhiteSpace(_settings.OpenWeatherApiKey))
            {
                return false;
            }

            _isCheckingWeather = true;
            try
            {
                if (string.IsNullOrWhiteSpace(_lastLatitude) || string.IsNullOrWhiteSpace(_lastLongitude))
                {
                    var hasLocation = await RefreshLocationAsync(notify);
                    if (!hasLocation &&
                        (string.IsNullOrWhiteSpace(_lastLatitude) || string.IsNullOrWhiteSpace(_lastLongitude)))
                    {
                        return false;
                    }
                }

                var weather = await RequestWeatherAsync(_settings.OpenWeatherApiKey, _lastLatitude, _lastLongitude, _lastCity);
                ApplyWeather(weather, notify);
                await RefreshHourlyForecastAsync(new LocationResult(
                    _lastCity ?? "",
                    _lastState ?? "",
                    _lastLatitude!,
                    _lastLongitude!));
                return true;
            }
            catch (Exception){}
            finally
            {
                _isCheckingWeather = false;
            }

            return false;
        }

        private async Task<LocationResult> RequestLocationAsync(string openWeatherApiKey)
        {
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
            {
                throw new UnauthorizedAccessException("Windows location access was denied.");
            }

            var geolocator = new Geolocator
            {
                DesiredAccuracyInMeters = 1000
            };
            var position = await geolocator.GetGeopositionAsync(
                TimeSpan.FromMinutes(5),
                TimeSpan.FromSeconds(15));
            var coordinate = position.Coordinate.Point.Position;
            var latitude = coordinate.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
            var longitude = coordinate.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
            var (city, state) = await RequestLocationNameAsync(openWeatherApiKey, latitude, longitude);

            return new LocationResult(city, state, latitude, longitude);
        }

        private async Task<(string City, string State)> RequestLocationNameAsync(
            string token,
            string latitude,
            string longitude)
        {
            var url = $"https://api.openweathermap.org/geo/1.0/reverse?lat={Uri.EscapeDataString(latitude)}&lon={Uri.EscapeDataString(longitude)}&limit=1&appid={Uri.EscapeDataString(token)}";
            var payload = await GetJsonArrayAsync(url, () => _openWeatherRequestCount++);
            var location = payload?.FirstOrDefault() as JsonObject;
            var city = location?["name"]?.GetValue<string>();
            var state = location?["state"]?.GetValue<string>();
            var country = location?["country"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(city))
            {
                throw new HttpRequestException("OpenWeather reverse geocoding returned no location.");
            }

            return (city, string.IsNullOrWhiteSpace(state) ? country ?? "--" : state);
        }

        private async Task<WeatherResult> RequestWeatherAsync(string token, string? latitude, string? longitude, string? city)
        {
            string url;
            if (!string.IsNullOrWhiteSpace(latitude) && !string.IsNullOrWhiteSpace(longitude))
            {
                url = $"https://api.openweathermap.org/data/2.5/weather?lat={Uri.EscapeDataString(latitude)}&lon={Uri.EscapeDataString(longitude)}&units={Uri.EscapeDataString(_settings.WeatherUnits)}&appid={Uri.EscapeDataString(token)}";
            }
            else if (!string.IsNullOrWhiteSpace(city))
            {
                url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(city)}&units={Uri.EscapeDataString(_settings.WeatherUnits)}&appid={Uri.EscapeDataString(token)}";
            }
            else
            {
                throw new HttpRequestException("OpenWeather request needs a city or location");
            }

            var payload = await GetJsonAsync(url, () => _openWeatherRequestCount++);
            var code = payload?["cod"]?.ToString();
            if (code is "401" or "403")
            {
                throw new HttpRequestException("OpenWeather returned an authorization error");
            }

            var description = payload?["weather"]?[0]?["description"]?.GetValue<string>();
            var icon = payload?["weather"]?[0]?["icon"]?.GetValue<string>();
            var temperature = payload?["main"]?["temp"]?.GetValue<double>();
            var humidity = payload?["main"]?["humidity"]?.GetValue<int>();
            var sunriseUnix = payload?["sys"]?["sunrise"]?.GetValue<long>();
            var sunsetUnix = payload?["sys"]?["sunset"]?.GetValue<long>();

            if (string.IsNullOrWhiteSpace(description) ||
                string.IsNullOrWhiteSpace(icon) ||
                temperature is null ||
                humidity is null ||
                sunriseUnix is null ||
                sunsetUnix is null)
            {
                throw new HttpRequestException("OpenWeather weather data was incomplete");
            }

            return new WeatherResult(
                CapitalizeWords(description),
                temperature.Value,
                humidity.Value,
                icon,
                DateTimeOffset.FromUnixTimeSeconds(sunriseUnix.Value),
                DateTimeOffset.FromUnixTimeSeconds(sunsetUnix.Value));
        }

        private async Task<JsonObject?> GetJsonAsync(string url, Action countRequest)
        {
            return (await GetJsonNodeAsync(url, countRequest))?.AsObject();
        }

        private async Task<JsonArray?> GetJsonArrayAsync(string url, Action countRequest)
        {
            return (await GetJsonNodeAsync(url, countRequest))?.AsArray();
        }

        private async Task<JsonNode?> GetJsonNodeAsync(string url, Action countRequest)
        {
            countRequest();

            using var response = await _httpClient.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return JsonNode.Parse(body);
        }

        private void ApplyLocation(LocationResult location, bool notify = true)
        {
            _lastCity = location.City;
            _lastState = location.State;
            _lastLatitude = location.Latitude;
            _lastLongitude = location.Longitude;
            if (notify)
            {
                _status.SetLocation(location);
            }
            else
            {
                _status.SetLocationSilently(location);
            }

            CityTextBlock.Text = $"City: {location.City}";
            StateTextBlock.Text = $"State: {location.State}";
            MemoCityTextBlock.Text = location.City;
            MemoStateTextBlock.Text = location.State;
            MemoCityTextBlock.Visibility = Visibility.Visible;
            MemoStateTextBlock.Visibility = Visibility.Visible;
            LocationSeparatorTextBlock.Visibility = Visibility.Visible;
        }

        private async Task RefreshHourlyForecastAsync(LocationResult location)
        {
            if (!double.TryParse(location.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(location.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                return;
            }

            var temperatureUnit = _settings.WeatherUnits == "imperial" ? "fahrenheit" : "celsius";
            var url = string.Create(
                CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={latitude:F6}&longitude={longitude:F6}&hourly=temperature_2m,precipitation_probability,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset&forecast_days=10&timezone=auto&temperature_unit={temperatureUnit}");

            var hourlyLoaded = false;
            var dailyLoaded = false;
            try
            {
                var payload = await GetJsonAsync(url, () => { });
                var hourly = payload?["hourly"]?.AsObject();
                var times = hourly?["time"]?.AsArray();
                var temperatures = hourly?["temperature_2m"]?.AsArray();
                var precipitation = hourly?["precipitation_probability"]?.AsArray();
                var hourlyWeatherCodes = hourly?["weather_code"]?.AsArray();
                if (times is null || temperatures is null || precipitation is null || hourlyWeatherCodes is null)
                {
                    throw new HttpRequestException("Hourly forecast data was incomplete");
                }

                _hourlyForecast.Clear();
                var count = new[]
                {
                    times.Count,
                    temperatures.Count,
                    precipitation.Count,
                    hourlyWeatherCodes.Count
                }.Min();
                for (var index = 0; index < count; index++)
                {
                    var time = times[index]?.GetValue<string>() ?? "";
                    if (string.IsNullOrWhiteSpace(time) ||
                        !double.TryParse(temperatures[index]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature))
                    {
                        continue;
                    }

                    _ = int.TryParse(
                        precipitation[index]?.ToString(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var precipitationProbability);
                    _ = int.TryParse(hourlyWeatherCodes[index]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weatherCode);
                    _hourlyForecast.Add(new HourlyForecastPoint(time, temperature, precipitationProbability, weatherCode));
                }

                var daily = payload?["daily"]?.AsObject();
                var dailyTimes = daily?["time"]?.AsArray();
                var dailyHighs = daily?["temperature_2m_max"]?.AsArray();
                var dailyLows = daily?["temperature_2m_min"]?.AsArray();
                var dailyWeatherCodes = daily?["weather_code"]?.AsArray();
                var dailyPrecipitation = daily?["precipitation_probability_max"]?.AsArray();
                var dailySunrises = daily?["sunrise"]?.AsArray();
                var dailySunsets = daily?["sunset"]?.AsArray();
                if (dailyTimes is null || dailyHighs is null || dailyLows is null ||
                    dailyWeatherCodes is null || dailyPrecipitation is null ||
                    dailySunrises is null || dailySunsets is null)
                {
                    throw new HttpRequestException("Daily forecast data was incomplete");
                }

                _dailyForecast.Clear();
                var dailyCount = new[]
                {
                    dailyTimes.Count,
                    dailyHighs.Count,
                    dailyLows.Count,
                    dailyWeatherCodes.Count,
                    dailyPrecipitation.Count,
                    dailySunrises.Count,
                    dailySunsets.Count
                }.Min();
                for (var index = 0; index < dailyCount; index++)
                {
                    if (!DateOnly.TryParse(dailyTimes[index]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                        !double.TryParse(dailyHighs[index]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var high) ||
                        !double.TryParse(dailyLows[index]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var low) ||
                        !DateTime.TryParse(dailySunrises[index]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var sunrise) ||
                        !DateTime.TryParse(dailySunsets[index]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var sunset))
                    {
                        continue;
                    }

                    if (date < DateOnly.FromDateTime(DateTime.Now))
                    {
                        continue;
                    }

                    _ = int.TryParse(dailyWeatherCodes[index]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dailyWeatherCode);
                    _ = int.TryParse(dailyPrecipitation[index]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dailyPrecipitationProbability);
                    _dailyForecast.Add(new DailyForecastPoint(
                        date,
                        high,
                        low,
                        dailyWeatherCode,
                        dailyPrecipitationProbability,
                        sunrise,
                        sunset));
                }

                hourlyLoaded = _hourlyForecast.Count >= 2;
                dailyLoaded = _dailyForecast.Count > 0;
                _selectedForecastDay = Math.Clamp(_selectedForecastDay, 0, Math.Max(0, _dailyForecast.Count - 1));
            }
            catch
            {
                _hourlyForecast.Clear();
                _dailyForecast.Clear();
            }

            HourlyForecastProgressRing.IsActive = false;
            HourlyForecastProgressRing.Visibility = Visibility.Collapsed;
            HourlyForecastCanvas.Visibility = hourlyLoaded ? Visibility.Visible : Visibility.Collapsed;
            HourlyForecastStatusTextBlock.Text = "Hourly forecast unavailable";
            HourlyForecastStatusTextBlock.Visibility = hourlyLoaded ? Visibility.Collapsed : Visibility.Visible;
            DailyForecastProgressRing.IsActive = false;
            DailyForecastProgressRing.Visibility = Visibility.Collapsed;
            DailyForecastStatusTextBlock.Visibility = dailyLoaded ? Visibility.Collapsed : Visibility.Visible;
            if (dailyLoaded)
            {
                RenderDailyForecast();
            }
            else
            {
                DailyForecastPanel.Children.Clear();
            }

            if (hourlyLoaded)
            {
                DispatcherQueue.TryEnqueue(() => RenderHourlyForecast());
            }
        }

        private void RenderDailyForecast()
        {
            RemoveExpiredDailyForecast();
            DailyForecastPanel.Children.Clear();
            DailyForecastPanel.ColumnDefinitions.Clear();
            for (var index = 0; index < _dailyForecast.Count; index++)
            {
                DailyForecastPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var isDarkTheme = RootNavigationView.RequestedTheme == ElementTheme.Dark;
            for (var index = 0; index < _dailyForecast.Count; index++)
            {
                var day = _dailyForecast[index];
                var isSelected = index == _selectedForecastDay;
                var selectedBorderBrush = GetMemoMonthBrush(day.Date.Month);
                var selectedBackgroundBrush = GetMemoMonthBrush(
                    day.Date.Month,
                    isDarkTheme ? (byte)42 : (byte)24);
                var selectedHoverBackgroundBrush = GetMemoMonthBrush(
                    day.Date.Month,
                    isDarkTheme ? (byte)54 : (byte)34);
                var selectedPressedBackgroundBrush = GetMemoMonthBrush(
                    day.Date.Month,
                    isDarkTheme ? (byte)66 : (byte)44);
                var defaultBackgroundBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    isDarkTheme ? (byte)20 : (byte)10,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0));
                var defaultHoverBackgroundBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    isDarkTheme ? (byte)32 : (byte)18,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0));
                var defaultPressedBackgroundBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    isDarkTheme ? (byte)42 : (byte)26,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0));
                var defaultBorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    24,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0,
                    isDarkTheme ? (byte)255 : (byte)0));
                var label = day.Date == today
                    ? "Today"
                    : day.Date.ToString("ddd d", CultureInfo.CurrentCulture);

                var content = new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                content.Children.Add(new TextBlock
                {
                    Text = label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
                });
                content.Children.Add(new TextBlock
                {
                    Text = GetWeatherSymbol(day.WeatherCode),
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 28,
                    Foreground = GetWeatherSymbolBrush(day.WeatherCode),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var temperatures = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                temperatures.Children.Add(new TextBlock { Text = $"{day.High:0}°", FontSize = 18 });
                temperatures.Children.Add(new TextBlock { Text = $"{day.Low:0}°", FontSize = 14, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Bottom });
                content.Children.Add(temperatures);
                content.Children.Add(new TextBlock
                {
                    Text = GetWeatherDescription(day.WeatherCode),
                    FontSize = 12,
                    Opacity = 0.7,
                    MaxWidth = 92,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var button = new Button
                {
                    Tag = index,
                    Content = content,
                    Height = 136,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
                    BorderBrush = isSelected ? selectedBorderBrush : defaultBorderBrush,
                    Background = isSelected ? selectedBackgroundBrush : defaultBackgroundBrush
                };
                button.Resources["ButtonBackground"] = isSelected
                    ? selectedBackgroundBrush
                    : defaultBackgroundBrush;
                button.Resources["ButtonBackgroundPointerOver"] = isSelected
                    ? selectedHoverBackgroundBrush
                    : defaultHoverBackgroundBrush;
                button.Resources["ButtonBackgroundPressed"] = isSelected
                    ? selectedPressedBackgroundBrush
                    : defaultPressedBackgroundBrush;
                button.Resources["ButtonBorderBrush"] = isSelected
                    ? selectedBorderBrush
                    : defaultBorderBrush;
                button.Resources["ButtonBorderBrushPointerOver"] = isSelected
                    ? selectedBorderBrush
                    : defaultBorderBrush;
                button.Resources["ButtonBorderBrushPressed"] = isSelected
                    ? selectedBorderBrush
                    : defaultBorderBrush;
                button.Click += DayForecastButton_Click;
                Grid.SetColumn(button, index);
                DailyForecastPanel.Children.Add(button);
            }
        }

        private void RemoveExpiredDailyForecast()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var removedCount = _dailyForecast.RemoveAll(day => day.Date < today);
            if (removedCount == 0)
            {
                return;
            }

            _selectedForecastDay = Math.Clamp(
                _selectedForecastDay - removedCount,
                0,
                Math.Max(0, _dailyForecast.Count - 1));
        }

        private void DayForecastButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: int index } || index < 0 || index >= _dailyForecast.Count)
            {
                return;
            }

            if (index == _selectedForecastDay)
            {
                return;
            }

            var previousForecast = GetSelectedHourlyForecast();
            _selectedForecastDay = index;
            RenderDailyForecast();
            AnimateHourlyForecastDayTransition(previousForecast);
            ScheduleForecastDayReturn();
        }

        private void ScheduleForecastDayReturn()
        {
            if (_dailyForecast.Count == 0 ||
                _dailyForecast[_selectedForecastDay].Date == DateOnly.FromDateTime(DateTime.Now))
            {
                _forecastDayReturnTimer?.Stop();
                return;
            }

            if (_forecastDayReturnTimer is null)
            {
                _forecastDayReturnTimer = DispatcherQueue.CreateTimer();
                _forecastDayReturnTimer.Interval = TimeSpan.FromSeconds(60);
                _forecastDayReturnTimer.IsRepeating = false;
                _forecastDayReturnTimer.Tick += (_, _) => ReturnToCurrentForecastDay();
            }

            _forecastDayReturnTimer.Stop();
            _forecastDayReturnTimer.Start();
        }

        private void ReturnToCurrentForecastDay()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var todayIndex = _dailyForecast.FindIndex(day => day.Date == today);
            if (todayIndex < 0 || todayIndex == _selectedForecastDay)
            {
                return;
            }

            var previousForecast = GetSelectedHourlyForecast();
            _selectedForecastDay = todayIndex;
            RenderDailyForecast();
            AnimateHourlyForecastDayTransition(previousForecast);
        }

        private List<HourlyForecastPoint> GetSelectedHourlyForecast()
        {
            if (_dailyForecast.Count == 0)
            {
                return DeduplicateHourlyForecast(_hourlyForecast.Take(24));
            }

            var date = _dailyForecast[_selectedForecastDay].Date;
            return DeduplicateHourlyForecast(_hourlyForecast
                .Where(point => DateTime.TryParse(point.Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
                                DateOnly.FromDateTime(time) == date));
        }

        private static List<HourlyForecastPoint> DeduplicateHourlyForecast(IEnumerable<HourlyForecastPoint> forecast)
        {
            return forecast
                .Select(point => new
                {
                    Point = point,
                    Parsed = DateTime.TryParse(point.Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                        ? time
                        : DateTime.MaxValue
                })
                .Where(item => item.Parsed != DateTime.MaxValue)
                .GroupBy(item => item.Parsed)
                .Select(group => group.First())
                .OrderBy(item => item.Parsed)
                .Select(item => item.Point)
                .ToList();
        }

        private void HourlyForecastCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderHourlyForecast();
        }

        private void HourlyForecastCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() => RenderHourlyForecast());
        }

        private void RenderHourlyForecast()
        {
            StopHourlyForecastMorph();
            StopHourlyChromeTransition();
            StopHourlyHoverTracking();
            HourlyForecastCanvas.Children.Clear();
            _hourlyPrecipitationBars.Clear();
            _hourlyPrecipitationLabels.Clear();
            _hourlyAxisLabels.Clear();
            _hourlyCurrentTimeMarkerElements.Clear();
            _hourlyDaylightBackground = null;
            _hourlyTemperatureCurve = null;
            _hourlyTemperatureBrush = null;
            _hourlyHoverLine = null;
            _hourlyHoverLabel = null;
            _hourlyHoverBrush = null;
            _hourlyBackgroundColumnColors = [];
            _hourlyHoverVisible = false;
            var width = HourlyForecastCanvas.ActualWidth;
            var height = HourlyForecastCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var displayedForecast = GetSelectedHourlyForecast();
            if (displayedForecast.Count < 2)
            {
                var unavailable = new TextBlock
                {
                    Text = "Hourly forecast unavailable",
                    Opacity = 0.65
                };
                HourlyForecastCanvas.Children.Add(unavailable);
                Canvas.SetLeft(unavailable, Math.Max(0, width / 2 - 80));
                Canvas.SetTop(unavailable, Math.Max(0, height / 2 - 10));
                return;
            }

            const double left = 52;
            const double right = 22;
            const double top = 24;
            const double bottom = 42;
            var plotWidth = Math.Max(1, width - left - right);
            var plotHeight = Math.Max(1, height - top - bottom);
            var precipitationAreaHeight = Math.Max(24, plotHeight * 0.20);
            var temperaturePlotHeight = Math.Max(1, plotHeight - precipitationAreaHeight - 20);
            var precipitationBaseline = top + plotHeight;
            var minimum = displayedForecast.Min(point => point.Temperature);
            var maximum = displayedForecast.Max(point => point.Temperature);
            if (maximum - minimum < 2)
            {
                minimum -= 1;
                maximum += 1;
            }

            var gridBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 128, 128, 128));
            var precipitationBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(105, 60, 150, 255));
            var temperatureBrush = CreateHourlyTemperatureBrush(displayedForecast);
            RenderHourlyDaylightBackground(displayedForecast, left, plotWidth, width, height);
            var unit = _settings.WeatherUnits == "imperial" ? "°F" : "°C";

            for (var lineIndex = 0; lineIndex < 3; lineIndex++)
            {
                var progress = lineIndex / 2.0;
                var y = top + progress * temperaturePlotHeight;
                HourlyForecastCanvas.Children.Add(new Line
                {
                    X1 = left,
                    X2 = left + plotWidth,
                    Y1 = y,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 1
                });

                var value = maximum - progress * (maximum - minimum);
                var label = new TextBlock
                {
                    Text = $"{value:0}{unit}",
                    FontSize = 12,
                    Opacity = 0.7
                };
                HourlyForecastCanvas.Children.Add(label);
                _hourlyAxisLabels.Add(label);
                Canvas.SetLeft(label, 4);
                Canvas.SetTop(label, y - 9);
            }

            var temperaturePoints = new List<Windows.Foundation.Point>(displayedForecast.Count);

            for (var index = 0; index < displayedForecast.Count; index++)
            {
                var point = displayedForecast[index];
                var x = left + index * plotWidth / (displayedForecast.Count - 1);
                var temperatureY = top + (maximum - point.Temperature) / (maximum - minimum) * temperaturePlotHeight;
                temperaturePoints.Add(new Windows.Foundation.Point(x, temperatureY));

                var barHeight = point.PrecipitationProbability / 100.0 * precipitationAreaHeight;
                var bar = new Rectangle
                {
                    Width = Math.Max(3, plotWidth / displayedForecast.Count * 0.55),
                    Height = barHeight,
                    Fill = precipitationBrush,
                    RadiusX = 2,
                    RadiusY = 2
                };
                HourlyForecastCanvas.Children.Add(bar);
                _hourlyPrecipitationBars.Add(bar);
                Canvas.SetLeft(bar, x - bar.Width / 2);
                Canvas.SetTop(bar, precipitationBaseline - barHeight);

                var precipitationLabel = new TextBlock
                {
                    Text = point.PrecipitationProbability > 0
                        ? $"{point.PrecipitationProbability}%"
                        : "",
                    Width = 42,
                    FontSize = 10,
                    Opacity = point.PrecipitationProbability > 0 ? 0.7 : 0,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false
                };
                HourlyForecastCanvas.Children.Add(precipitationLabel);
                _hourlyPrecipitationLabels.Add(precipitationLabel);
                Canvas.SetLeft(precipitationLabel, x - precipitationLabel.Width / 2);
                Canvas.SetTop(precipitationLabel, precipitationBaseline - barHeight - 18);

                var timeLabel = new TextBlock
                {
                    Text = FormatHourlyForecastTime(point.Time),
                    FontSize = 10,
                    Opacity = 0.7,
                    Width = 42,
                    TextAlignment = TextAlignment.Center
                };
                HourlyForecastCanvas.Children.Add(timeLabel);
                Canvas.SetLeft(timeLabel, Math.Max(left - 21, Math.Min(left + plotWidth - 21, x - 21)));
                Canvas.SetTop(timeLabel, top + plotHeight + 12);
            }

            var temperatureCurve = CreateTemperatureBezierCurve(temperaturePoints, temperatureBrush);
            HourlyForecastCanvas.Children.Add(temperatureCurve);
            _hourlyTemperatureCurve = temperatureCurve;
            _hourlyTemperatureBrush = temperatureBrush;
            RenderCurrentTimeMarker(displayedForecast, left, top, plotWidth, plotHeight);
            CreateHourlyHoverOverlay(left, top, plotHeight);
        }

        private void RenderHourlyDaylightBackground(
            IReadOnlyList<HourlyForecastPoint> forecast,
            double left,
            double plotWidth,
            double canvasWidth,
            double canvasHeight)
        {
            if (_selectedForecastDay < 0 ||
                _selectedForecastDay >= _dailyForecast.Count ||
                forecast.Count < 2 ||
                !DateTime.TryParse(forecast[0].Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstTime) ||
                !DateTime.TryParse(forecast[^1].Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastTime) ||
                lastTime <= firstTime)
            {
                return;
            }

            var day = _dailyForecast[_selectedForecastDay];
            const byte nightAlpha = 40;
            var transition = TimeSpan.FromMinutes(20);
            var duration = (lastTime - firstTime).TotalMilliseconds;
            const double cardInset = 8;
            var backgroundWidth = canvasWidth + cardInset * 2;
            var backgroundHeight = canvasHeight + cardInset * 2;
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(backgroundWidth));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(backgroundHeight));
            var sunriseStart = day.Sunrise - transition;
            var sunriseEnd = day.Sunrise + transition;
            var sunsetStart = day.Sunset - transition;
            var sunsetEnd = day.Sunset + transition;
            double SmoothStep(DateTime start, DateTime end, DateTime value)
            {
                var progress = Math.Clamp(
                    (value - start).TotalMilliseconds / (end - start).TotalMilliseconds,
                    0,
                    1);
                return progress * progress * (3 - 2 * progress);
            }
            double AlphaAt(DateTime time)
            {
                if (time < sunriseStart || time >= sunsetEnd) return nightAlpha;
                if (time < sunriseEnd) return nightAlpha * (1 - SmoothStep(sunriseStart, sunriseEnd, time));
                if (time < sunsetStart) return 0;
                return nightAlpha * SmoothStep(sunsetStart, sunsetEnd, time);
            }

            var card = (HourlyForecastCanvas.Parent as Grid)?.Parent as Border;
            var themeSurface = HourlyForecastCanvas.ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                : Windows.UI.Color.FromArgb(255, 243, 243, 243);
            var cardBrushColor = (card?.Background as SolidColorBrush)?.Color;
            var cardColor = themeSurface;
            if (cardBrushColor is { } overlay)
            {
                var opacity = overlay.A / 255.0;
                cardColor = Windows.UI.Color.FromArgb(
                    255,
                    (byte)Math.Round(overlay.R * opacity + themeSurface.R * (1 - opacity)),
                    (byte)Math.Round(overlay.G * opacity + themeSurface.G * (1 - opacity)),
                    (byte)Math.Round(overlay.B * opacity + themeSurface.B * (1 - opacity)));
            }
            var pixels = new byte[pixelWidth * pixelHeight * 4];
            var backgroundColumnColors = new Windows.UI.Color[pixelWidth];
            for (var x = 0; x < pixelWidth; x++)
            {
                var forecastPosition = Math.Clamp(
                    (x - cardInset - left) / plotWidth,
                    0,
                    1);
                var time = firstTime + TimeSpan.FromMilliseconds(duration * forecastPosition);
                var desiredAlpha = AlphaAt(time);
                var shade = 1 - desiredAlpha / 255.0;
                var targetRed = cardColor.R * shade;
                var targetGreen = cardColor.G * shade;
                var targetBlue = cardColor.B * shade;
                backgroundColumnColors[x] = Windows.UI.Color.FromArgb(
                    255,
                    (byte)Math.Round(targetRed),
                    (byte)Math.Round(targetGreen),
                    (byte)Math.Round(targetBlue));
                for (var y = 0; y < pixelHeight; y++)
                {
                    var hash = unchecked((uint)(x * 374761393) ^ (uint)(y * 668265263));
                    hash = unchecked((hash ^ (hash >> 13)) * 1274126177);
                    var threshold = (hash & 0xFFFF) / 65536.0;
                    byte Dither(double value)
                    {
                        var lower = Math.Floor(value);
                        return (byte)Math.Clamp(
                            lower + (threshold < value - lower ? 1 : 0),
                            0,
                            255);
                    }
                    var pixel = (y * pixelWidth + x) * 4;
                    pixels[pixel] = Dither(targetBlue);
                    pixels[pixel + 1] = Dither(targetGreen);
                    pixels[pixel + 2] = Dither(targetRed);
                    pixels[pixel + 3] = 255;
                }
            }

            var bitmap = new WriteableBitmap(pixelWidth, pixelHeight);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            bitmap.Invalidate();
            _hourlyBackgroundColumnColors = backgroundColumnColors;

            var background = new Image
            {
                Width = backgroundWidth,
                Height = backgroundHeight,
                Source = bitmap,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            _hourlyDaylightBackground = background;
            HourlyForecastCanvas.Children.Add(background);
            Canvas.SetLeft(background, -cardInset);
            Canvas.SetTop(background, -cardInset);
        }

        private void AnimateHourlyForecastDayTransition(IReadOnlyList<HourlyForecastPoint> previousForecast)
        {
            StopHourlyForecastMorph();
            AnimateHourlyHover(false);
            StopHourlyChromeTransition();
            var outgoingBackground = _hourlyDaylightBackground;
            var outgoingChrome = _hourlyAxisLabels
                .Cast<UIElement>()
                .Concat(_hourlyCurrentTimeMarkerElements)
                .ToArray();
            foreach (var element in outgoingChrome)
            {
                HourlyForecastCanvas.Children.Remove(element);
            }
            if (outgoingBackground is not null)
            {
                HourlyForecastCanvas.Children.Remove(outgoingBackground);
            }
            StartHourlyForecastDayMorph(previousForecast, outgoingChrome, outgoingBackground);
        }

        private void StartHourlyForecastDayMorph(
            IReadOnlyList<HourlyForecastPoint> previousForecast,
            IReadOnlyList<UIElement> outgoingChrome,
            Image? outgoingBackground)
        {
            var targetForecast = GetSelectedHourlyForecast();
            RenderHourlyForecast();
            if (outgoingBackground is not null)
            {
                HourlyForecastCanvas.Children.Insert(
                    Math.Min(1, HourlyForecastCanvas.Children.Count),
                    outgoingBackground);
            }
            foreach (var element in outgoingChrome)
            {
                HourlyForecastCanvas.Children.Add(element);
            }
            AnimateHourlyChromeCrossfade(outgoingChrome);
            AnimateHourlyDaylightStretch(previousForecast, targetForecast, outgoingBackground);
            if (previousForecast.Count < 2 ||
                targetForecast.Count < 2 ||
                _hourlyTemperatureCurve is null ||
                _hourlyTemperatureBrush is null ||
                _hourlyAxisLabels.Count != 3 ||
                _hourlyPrecipitationBars.Count != targetForecast.Count ||
                _hourlyPrecipitationLabels.Count != targetForecast.Count)
            {
                return;
            }

            const double left = 52;
            const double right = 22;
            const double top = 24;
            const double bottom = 42;
            var plotWidth = Math.Max(1, HourlyForecastCanvas.ActualWidth - left - right);
            var plotHeight = Math.Max(1, HourlyForecastCanvas.ActualHeight - top - bottom);
            var precipitationAreaHeight = Math.Max(24, plotHeight * 0.20);
            var temperaturePlotHeight = Math.Max(1, plotHeight - precipitationAreaHeight - 20);
            var precipitationBaseline = top + plotHeight;
            GetHourlyForecastRange(previousForecast, out var previousMinimum, out var previousMaximum);
            GetHourlyForecastRange(targetForecast, out var targetMinimum, out var targetMaximum);

            var sourcePoints = new List<Windows.Foundation.Point>(targetForecast.Count);
            var targetPoints = new List<Windows.Foundation.Point>(targetForecast.Count);
            var sourceBarHeights = new double[targetForecast.Count];
            var targetBarHeights = new double[targetForecast.Count];
            var sourceColors = new Windows.UI.Color[targetForecast.Count];
            var targetColors = new Windows.UI.Color[targetForecast.Count];
            var framePoints = new Windows.Foundation.Point[targetForecast.Count];
            for (var index = 0; index < targetForecast.Count; index++)
            {
                var normalizedPosition = index / (double)(targetForecast.Count - 1);
                var sourceTemperature = SampleHourlyTemperature(previousForecast, normalizedPosition);
                var sourcePrecipitation = SampleHourlyPrecipitation(previousForecast, normalizedPosition);
                var x = left + normalizedPosition * plotWidth;
                sourcePoints.Add(new Windows.Foundation.Point(
                    x,
                    top + (previousMaximum - sourceTemperature) /
                    (previousMaximum - previousMinimum) * temperaturePlotHeight));
                targetPoints.Add(new Windows.Foundation.Point(
                    x,
                    top + (targetMaximum - targetForecast[index].Temperature) /
                    (targetMaximum - targetMinimum) * temperaturePlotHeight));
                sourceBarHeights[index] = sourcePrecipitation / 100 * precipitationAreaHeight;
                targetBarHeights[index] =
                    targetForecast[index].PrecipitationProbability / 100.0 * precipitationAreaHeight;
                sourceColors[index] = GetWs2812TemperatureColor(
                    sourceTemperature,
                    _settings.WeatherUnits);
                targetColors[index] = GetWs2812TemperatureColor(
                    targetForecast[index].Temperature,
                    _settings.WeatherUnits);
            }

            ApplyHourlyForecastMorphFrame(
                sourcePoints,
                targetPoints,
                framePoints,
                sourceBarHeights,
                targetBarHeights,
                sourceColors,
                targetColors,
                precipitationBaseline,
                precipitationAreaHeight,
                0);

            var startedAt = DateTimeOffset.Now;
            EventHandler<object>? renderingHandler = null;
            renderingHandler = (_, _) =>
            {
                var linearProgress = Math.Clamp(
                    (DateTimeOffset.Now - startedAt).TotalMilliseconds / 200,
                    0,
                    1);
                var easedProgress = linearProgress < 0.5
                    ? 4 * linearProgress * linearProgress * linearProgress
                    : 1 - Math.Pow(-2 * linearProgress + 2, 3) / 2;
                ApplyHourlyForecastMorphFrame(
                    sourcePoints,
                    targetPoints,
                    framePoints,
                    sourceBarHeights,
                    targetBarHeights,
                    sourceColors,
                    targetColors,
                    precipitationBaseline,
                    precipitationAreaHeight,
                    easedProgress);

                if (linearProgress >= 1)
                {
                    if (renderingHandler is not null)
                    {
                        CompositionTarget.Rendering -= renderingHandler;
                    }
                    if (ReferenceEquals(_hourlyForecastRenderingHandler, renderingHandler))
                    {
                        _hourlyForecastRenderingHandler = null;
                    }
                }
            };
            _hourlyForecastRenderingHandler = renderingHandler;
            CompositionTarget.Rendering += renderingHandler;
        }

        private void AnimateHourlyDaylightStretch(
            IReadOnlyList<HourlyForecastPoint> previousForecast,
            IReadOnlyList<HourlyForecastPoint> targetForecast,
            Image? outgoingBackground)
        {
            _hourlyDaylightStretchTransition?.Stop();
            _hourlyDaylightStretchTransition = null;
            if (_hourlyOutgoingDaylightBackground is not null)
            {
                HourlyForecastCanvas.Children.Remove(_hourlyOutgoingDaylightBackground);
                _hourlyOutgoingDaylightBackground = null;
            }
            if (outgoingBackground is null ||
                previousForecast.Count == 0 ||
                targetForecast.Count == 0 ||
                !DateTime.TryParse(previousForecast[0].Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var previousTime) ||
                !DateTime.TryParse(targetForecast[0].Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var targetTime))
            {
                return;
            }

            var previousDate = DateOnly.FromDateTime(previousTime);
            var targetDate = DateOnly.FromDateTime(targetTime);
            var previousDay = _dailyForecast.FirstOrDefault(day => day.Date == previousDate);
            var targetDay = _dailyForecast.FirstOrDefault(day => day.Date == targetDate);
            if (previousDay is null || targetDay is null)
            {
                return;
            }

            var previousDaylight = (previousDay.Sunset - previousDay.Sunrise).TotalMinutes;
            var targetDaylight = (targetDay.Sunset - targetDay.Sunrise).TotalMinutes;
            if (previousDaylight <= 0 || targetDaylight <= 0)
            {
                return;
            }

            var transform = new ScaleTransform
            {
                ScaleX = 1,
                ScaleY = 1,
                CenterX = outgoingBackground.Width / 2,
                CenterY = outgoingBackground.Height / 2
            };
            outgoingBackground.RenderTransform = transform;
            var animation = new DoubleAnimation
            {
                To = Math.Clamp(targetDaylight / previousDaylight, 0.85, 1.15),
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, nameof(ScaleTransform.ScaleX));
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Completed += (_, _) =>
            {
                HourlyForecastCanvas.Children.Remove(outgoingBackground);
                if (ReferenceEquals(_hourlyOutgoingDaylightBackground, outgoingBackground))
                {
                    _hourlyOutgoingDaylightBackground = null;
                }
                if (ReferenceEquals(_hourlyDaylightStretchTransition, storyboard))
                {
                    _hourlyDaylightStretchTransition = null;
                }
            };
            _hourlyOutgoingDaylightBackground = outgoingBackground;
            _hourlyDaylightStretchTransition = storyboard;
            storyboard.Begin();
        }

        private void AnimateHourlyChromeCrossfade(IReadOnlyList<UIElement> outgoingElements)
        {
            StopHourlyChromeTransition();
            var incomingElements = _hourlyAxisLabels
                .Cast<UIElement>()
                .Concat(_hourlyCurrentTimeMarkerElements)
                .ToArray();
            if (incomingElements.Length == 0 && outgoingElements.Count == 0)
            {
                return;
            }

            foreach (var element in incomingElements)
            {
                element.Opacity = 0;
            }
            foreach (var element in outgoingElements)
            {
                element.Opacity = 1;
                _hourlyOutgoingChromeElements.Add(element);
            }

            var storyboard = new Storyboard();
            var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
            foreach (var element in incomingElements)
            {
                var animation = new DoubleAnimation
                {
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = easing
                };
                Storyboard.SetTarget(animation, element);
                Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
                storyboard.Children.Add(animation);
            }
            foreach (var element in outgoingElements)
            {
                var animation = new DoubleAnimation
                {
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = easing
                };
                Storyboard.SetTarget(animation, element);
                Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
                storyboard.Children.Add(animation);
            }
            storyboard.Completed += (_, _) =>
            {
                if (ReferenceEquals(_hourlyChromeTransition, storyboard))
                {
                    _hourlyChromeTransition = null;
                }
                foreach (var element in outgoingElements)
                {
                    HourlyForecastCanvas.Children.Remove(element);
                    _hourlyOutgoingChromeElements.Remove(element);
                }
            };
            _hourlyChromeTransition = storyboard;
            storyboard.Begin();
        }

        private void StopHourlyChromeTransition()
        {
            _hourlyChromeTransition?.Stop();
            _hourlyChromeTransition = null;
            foreach (var element in _hourlyOutgoingChromeElements)
            {
                HourlyForecastCanvas.Children.Remove(element);
            }
            _hourlyOutgoingChromeElements.Clear();
        }

        private void ApplyHourlyForecastMorphFrame(
            IReadOnlyList<Windows.Foundation.Point> sourcePoints,
            IReadOnlyList<Windows.Foundation.Point> targetPoints,
            Windows.Foundation.Point[] framePoints,
            IReadOnlyList<double> sourceBarHeights,
            IReadOnlyList<double> targetBarHeights,
            IReadOnlyList<Windows.UI.Color> sourceColors,
            IReadOnlyList<Windows.UI.Color> targetColors,
            double precipitationBaseline,
            double precipitationAreaHeight,
            double progress)
        {
            if (_hourlyTemperatureCurve is null ||
                sourcePoints.Count != targetPoints.Count ||
                _hourlyPrecipitationBars.Count != targetBarHeights.Count ||
                _hourlyPrecipitationLabels.Count != targetBarHeights.Count ||
                _hourlyTemperatureBrush is null ||
                _hourlyTemperatureBrush.GradientStops.Count != targetColors.Count)
            {
                return;
            }

            for (var index = 0; index < sourcePoints.Count; index++)
            {
                framePoints[index] = new Windows.Foundation.Point(
                    sourcePoints[index].X + (targetPoints[index].X - sourcePoints[index].X) * progress,
                    sourcePoints[index].Y + (targetPoints[index].Y - sourcePoints[index].Y) * progress);

                var barHeight = sourceBarHeights[index] +
                    (targetBarHeights[index] - sourceBarHeights[index]) * progress;
                var bar = _hourlyPrecipitationBars[index];
                bar.Height = barHeight;
                Canvas.SetTop(bar, precipitationBaseline - barHeight);
                var precipitation = precipitationAreaHeight <= 0
                    ? 0
                    : barHeight / precipitationAreaHeight * 100;
                var precipitationLabel = _hourlyPrecipitationLabels[index];
                precipitationLabel.Text = precipitation > 0.05 ? $"{precipitation:0}%" : "";
                precipitationLabel.Opacity = precipitation > 0.05 ? 0.7 : 0;
                Canvas.SetTop(precipitationLabel, precipitationBaseline - barHeight - 18);
                _hourlyTemperatureBrush.GradientStops[index].Color = InterpolateColor(
                    sourceColors[index],
                    targetColors[index],
                    progress);
            }
            UpdateTemperatureBezierGeometry(_hourlyTemperatureCurve.Data as PathGeometry, framePoints);
        }

        private void StopHourlyForecastMorph()
        {
            if (_hourlyForecastRenderingHandler is null)
            {
                return;
            }

            CompositionTarget.Rendering -= _hourlyForecastRenderingHandler;
            _hourlyForecastRenderingHandler = null;
        }

        private static Windows.UI.Color InterpolateColor(
            Windows.UI.Color source,
            Windows.UI.Color target,
            double progress)
        {
            return Windows.UI.Color.FromArgb(
                (byte)Math.Round(source.A + (target.A - source.A) * progress),
                (byte)Math.Round(source.R + (target.R - source.R) * progress),
                (byte)Math.Round(source.G + (target.G - source.G) * progress),
                (byte)Math.Round(source.B + (target.B - source.B) * progress));
        }

        private static void GetHourlyForecastRange(
            IReadOnlyList<HourlyForecastPoint> forecast,
            out double minimum,
            out double maximum)
        {
            minimum = forecast.Min(point => point.Temperature);
            maximum = forecast.Max(point => point.Temperature);
            if (maximum - minimum < 2)
            {
                minimum -= 1;
                maximum += 1;
            }
        }

        private static double SampleHourlyTemperature(
            IReadOnlyList<HourlyForecastPoint> forecast,
            double normalizedPosition)
        {
            var forecastPosition = Math.Clamp(normalizedPosition, 0, 1) * (forecast.Count - 1);
            var segmentIndex = Math.Min((int)Math.Floor(forecastPosition), forecast.Count - 2);
            return InterpolateHourlyTemperature(
                forecast,
                segmentIndex,
                Math.Clamp(forecastPosition - segmentIndex, 0, 1));
        }

        private static double SampleHourlyPrecipitation(
            IReadOnlyList<HourlyForecastPoint> forecast,
            double normalizedPosition)
        {
            var forecastPosition = Math.Clamp(normalizedPosition, 0, 1) * (forecast.Count - 1);
            var segmentIndex = Math.Min((int)Math.Floor(forecastPosition), forecast.Count - 2);
            var position = Math.Clamp(forecastPosition - segmentIndex, 0, 1);
            return forecast[segmentIndex].PrecipitationProbability +
                (forecast[segmentIndex + 1].PrecipitationProbability -
                 forecast[segmentIndex].PrecipitationProbability) * position;
        }

        private void CreateHourlyHoverOverlay(double left, double top, double plotHeight)
        {
            var hoverBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(190, 210, 210, 210));
            _hourlyHoverBrush = hoverBrush;
            _hourlyHoverLine = new Line
            {
                X1 = left,
                X2 = left,
                Y1 = top,
                Y2 = top + plotHeight,
                Stroke = hoverBrush,
                StrokeThickness = 1.5,
                Opacity = 0,
                IsHitTestVisible = false
            };
            _hourlyHoverLabel = new TextBlock
            {
                Width = 56,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = hoverBrush,
                TextAlignment = TextAlignment.Center,
                Opacity = 0,
                IsHitTestVisible = false
            };
            HourlyForecastCanvas.Children.Add(_hourlyHoverLine);
            HourlyForecastCanvas.Children.Add(_hourlyHoverLabel);
            Canvas.SetTop(_hourlyHoverLabel, 2);
        }

        private void HourlyForecastCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_hourlyHoverLine is null || _hourlyHoverLabel is null)
            {
                return;
            }

            const double left = 52;
            const double right = 22;
            var width = HourlyForecastCanvas.ActualWidth;
            var x = e.GetCurrentPoint(HourlyForecastCanvas).Position.X;
            if (x < left || x > width - right)
            {
                StopHourlyHoverTracking();
                AnimateHourlyHover(false);
                return;
            }

            var forecast = GetSelectedHourlyForecast();
            if (forecast.Count < 2)
            {
                StopHourlyHoverTracking();
                AnimateHourlyHover(false);
                return;
            }

            if (!_hourlyHoverVisible)
            {
                StopHourlyHoverTracking();
                UpdateHourlyHoverPosition(x);
            }
            else
            {
                AnimateHourlyHoverTo(x);
            }
            AnimateHourlyHover(true);
        }

        private void AnimateHourlyHoverTo(double targetX)
        {
            if (_hourlyHoverLine is null)
            {
                return;
            }

            _hourlyHoverTargetX = targetX;
            if (_hourlyHoverTrackingHandler is not null)
            {
                return;
            }

            _hourlyHoverLastFrameAt = DateTimeOffset.Now;
            EventHandler<object>? renderingHandler = null;
            renderingHandler = (_, _) =>
            {
                if (_hourlyHoverLine is null)
                {
                    StopHourlyHoverTracking();
                    return;
                }

                var now = DateTimeOffset.Now;
                var elapsedSeconds = Math.Clamp(
                    (now - _hourlyHoverLastFrameAt).TotalSeconds,
                    0,
                    0.05);
                _hourlyHoverLastFrameAt = now;

                // A continuous low-pass pursuit avoids restarting the easing curve
                // for every pointer event and settles visually in about 200 ms.
                var blend = 1 - Math.Exp(-elapsedSeconds / 0.045);
                var currentX = _hourlyHoverLine.X1;
                var remaining = _hourlyHoverTargetX - currentX;
                if (Math.Abs(remaining) < 0.1)
                {
                    UpdateHourlyHoverPosition(_hourlyHoverTargetX);
                    StopHourlyHoverTracking();
                    return;
                }

                UpdateHourlyHoverPosition(currentX + remaining * blend);
            };
            _hourlyHoverTrackingHandler = renderingHandler;
            CompositionTarget.Rendering += renderingHandler;
        }

        private void UpdateHourlyHoverPosition(double x)
        {
            if (_hourlyHoverLine is null || _hourlyHoverLabel is null)
            {
                return;
            }

            const double left = 52;
            const double right = 22;
            var width = HourlyForecastCanvas.ActualWidth;
            var forecast = GetSelectedHourlyForecast();
            if (forecast.Count < 2 || width <= left + right)
            {
                return;
            }

            x = Math.Clamp(x, left, width - right);
            var plotWidth = Math.Max(1, width - left - right);
            var forecastPosition = Math.Clamp((x - left) / plotWidth, 0, 1) * (forecast.Count - 1);
            var segmentIndex = Math.Min((int)Math.Floor(forecastPosition), forecast.Count - 2);
            var segmentPosition = Math.Clamp(forecastPosition - segmentIndex, 0, 1);
            var temperature = InterpolateHourlyTemperature(forecast, segmentIndex, segmentPosition);
            var unit = _settings.WeatherUnits == "imperial" ? "°F" : "°C";

            UpdateHourlyHoverContrast(x);
            _hourlyHoverLine.X1 = x;
            _hourlyHoverLine.X2 = x;
            _hourlyHoverLabel.Text = $"{temperature:0}{unit}";
            Canvas.SetLeft(
                _hourlyHoverLabel,
                Math.Clamp(x - _hourlyHoverLabel.Width / 2, 0, Math.Max(0, width - _hourlyHoverLabel.Width)));
        }

        private void StopHourlyHoverTracking()
        {
            if (_hourlyHoverTrackingHandler is null)
            {
                return;
            }

            CompositionTarget.Rendering -= _hourlyHoverTrackingHandler;
            _hourlyHoverTrackingHandler = null;
        }

        private void UpdateHourlyHoverContrast(double canvasX)
        {
            if (_hourlyHoverBrush is null || _hourlyBackgroundColumnColors.Length == 0)
            {
                return;
            }

            const double cardInset = 8;
            var column = Math.Clamp(
                (int)Math.Round(canvasX + cardInset),
                0,
                _hourlyBackgroundColumnColors.Length - 1);
            var background = _hourlyBackgroundColumnColors[column];
            _hourlyHoverBrush.Color = Windows.UI.Color.FromArgb(
                190,
                (byte)(255 - background.R),
                (byte)(255 - background.G),
                (byte)(255 - background.B));
        }

        private void HourlyForecastCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            StopHourlyHoverTracking();
            AnimateHourlyHover(false);
        }

        private void AnimateHourlyHover(bool show)
        {
            if (_hourlyHoverLine is null || _hourlyHoverLabel is null || _hourlyHoverVisible == show)
            {
                return;
            }

            _hourlyHoverVisible = show;
            _hourlyHoverStoryboard?.Stop();
            var storyboard = new Storyboard();
            var easing = new CubicEase
            {
                EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn
            };
            foreach (var element in new UIElement[] { _hourlyHoverLine, _hourlyHoverLabel })
            {
                var animation = new DoubleAnimation
                {
                    To = show ? 1 : 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = easing
                };
                Storyboard.SetTarget(animation, element);
                Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
                storyboard.Children.Add(animation);
            }
            _hourlyHoverStoryboard = storyboard;
            storyboard.Begin();
        }

        private static Microsoft.UI.Xaml.Shapes.Path CreateTemperatureBezierCurve(
            IReadOnlyList<Windows.Foundation.Point> points,
            Brush stroke)
        {
            return new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = CreateTemperatureBezierGeometry(points),
                Stroke = stroke,
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
        }

        private static PathGeometry CreateTemperatureBezierGeometry(
            IReadOnlyList<Windows.Foundation.Point> points)
        {
            var figure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = false,
                IsFilled = false
            };

            // Convert a Catmull-Rom spline to cubic Bezier segments. Each segment
            // ends on the next measured hourly point, so the curve passes through
            // every endpoint while keeping first-derivative continuity.
            for (var index = 0; index < points.Count - 1; index++)
            {
                var previous = index > 0 ? points[index - 1] : points[index];
                var current = points[index];
                var next = points[index + 1];
                var following = index + 2 < points.Count ? points[index + 2] : next;
                figure.Segments.Add(new BezierSegment
                {
                    Point1 = new Windows.Foundation.Point(
                        current.X + (next.X - previous.X) / 6.0,
                        current.Y + (next.Y - previous.Y) / 6.0),
                    Point2 = new Windows.Foundation.Point(
                        next.X - (following.X - current.X) / 6.0,
                        next.Y - (following.Y - current.Y) / 6.0),
                    Point3 = next
                });
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static void UpdateTemperatureBezierGeometry(
            PathGeometry? geometry,
            IReadOnlyList<Windows.Foundation.Point> points)
        {
            if (geometry?.Figures.Count != 1 ||
                points.Count < 2 ||
                geometry.Figures[0].Segments.Count != points.Count - 1)
            {
                return;
            }

            var figure = geometry.Figures[0];
            figure.StartPoint = points[0];
            for (var index = 0; index < points.Count - 1; index++)
            {
                if (figure.Segments[index] is not BezierSegment segment)
                {
                    return;
                }

                var previous = index > 0 ? points[index - 1] : points[index];
                var current = points[index];
                var next = points[index + 1];
                var following = index + 2 < points.Count ? points[index + 2] : next;
                segment.Point1 = new Windows.Foundation.Point(
                    current.X + (next.X - previous.X) / 6.0,
                    current.Y + (next.Y - previous.Y) / 6.0);
                segment.Point2 = new Windows.Foundation.Point(
                    next.X - (following.X - current.X) / 6.0,
                    next.Y - (following.Y - current.Y) / 6.0);
                segment.Point3 = next;
            }
        }

        private LinearGradientBrush CreateHourlyTemperatureBrush(IReadOnlyList<HourlyForecastPoint> forecast)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.5),
                EndPoint = new Windows.Foundation.Point(1, 0.5)
            };
            for (var index = 0; index < forecast.Count; index++)
            {
                brush.GradientStops.Add(new GradientStop
                {
                    Offset = forecast.Count == 1 ? 0 : index / (double)(forecast.Count - 1),
                    Color = GetWs2812TemperatureColor(forecast[index].Temperature, _settings.WeatherUnits)
                });
            }
            return brush;
        }

        private void RenderCurrentTimeMarker(
            IReadOnlyList<HourlyForecastPoint> forecast,
            double left,
            double top,
            double plotWidth,
            double plotHeight)
        {
            if (forecast.Count < 2 ||
                !DateTime.TryParse(forecast[0].Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstTime) ||
                !DateTime.TryParse(forecast[^1].Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastTime))
            {
                return;
            }

            var now = DateTime.Now;
            if (DateOnly.FromDateTime(now) != DateOnly.FromDateTime(firstTime) ||
                now < firstTime || now >= lastTime.AddHours(1))
            {
                return;
            }

            // Each forecast point represents one hour. Use the same Catmull-Rom
            // interpolation as the visible curve to obtain the value at this minute.
            var elapsedHours = Math.Clamp((now - firstTime).TotalHours, 0, forecast.Count - 1);
            var segmentIndex = Math.Min((int)Math.Floor(elapsedHours), forecast.Count - 2);
            var segmentPosition = Math.Clamp(elapsedHours - segmentIndex, 0, 1);
            var currentTemperature = InterpolateHourlyTemperature(forecast, segmentIndex, segmentPosition);
            var markerBrush = new SolidColorBrush(
                GetWs2812TemperatureColor(currentTemperature, _settings.WeatherUnits));
            var position = elapsedHours / (forecast.Count - 1);
            var x = left + position * plotWidth;
            var currentTimeLine = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top,
                Y2 = top + plotHeight,
                Stroke = markerBrush,
                StrokeThickness = 2
            };
            HourlyForecastCanvas.Children.Add(currentTimeLine);
            _hourlyCurrentTimeMarkerElements.Add(currentTimeLine);

            const double labelWidth = 56;
            var currentTemperatureLabel = new TextBlock
            {
                Text = $"{currentTemperature:0}{(_settings.WeatherUnits == "imperial" ? "°F" : "°C")}",
                Width = labelWidth,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = markerBrush,
                TextAlignment = TextAlignment.Center
            };
            HourlyForecastCanvas.Children.Add(currentTemperatureLabel);
            _hourlyCurrentTimeMarkerElements.Add(currentTemperatureLabel);
            Canvas.SetLeft(
                currentTemperatureLabel,
                Math.Clamp(x - labelWidth / 2, 0, Math.Max(0, HourlyForecastCanvas.ActualWidth - labelWidth)));
            Canvas.SetTop(currentTemperatureLabel, 2);
        }

        private static double InterpolateHourlyTemperature(
            IReadOnlyList<HourlyForecastPoint> forecast,
            int segmentIndex,
            double position)
        {
            var previous = forecast[Math.Max(0, segmentIndex - 1)].Temperature;
            var current = forecast[segmentIndex].Temperature;
            var next = forecast[Math.Min(forecast.Count - 1, segmentIndex + 1)].Temperature;
            var following = forecast[Math.Min(forecast.Count - 1, segmentIndex + 2)].Temperature;
            var squared = position * position;
            var cubed = squared * position;
            return 0.5 * (
                2 * current +
                (-previous + next) * position +
                (2 * previous - 5 * current + 4 * next - following) * squared +
                (-previous + 3 * current - 3 * next + following) * cubed);
        }

        private static string FormatHourlyForecastTime(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time.ToString("htt", CultureInfo.InvariantCulture)
                : value;
        }

        private static string GetWeatherSymbol(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "☀",
                1 or 2 => "⛅",
                3 => "☁",
                45 or 48 => "≋",
                >= 51 and <= 67 => "☂",
                >= 71 and <= 77 => "❄",
                >= 80 and <= 82 => "☂",
                85 or 86 => "❄",
                >= 95 => "ϟ",
                _ => "☁"
            };
        }

        private static Brush GetWeatherSymbolBrush(int weatherCode)
        {
            var color = weatherCode switch
            {
                0 => Windows.UI.Color.FromArgb(255, 255, 196, 48),
                1 or 2 => Windows.UI.Color.FromArgb(255, 255, 190, 64),
                3 => Windows.UI.Color.FromArgb(255, 205, 214, 224),
                45 or 48 => Windows.UI.Color.FromArgb(255, 166, 178, 191),
                >= 51 and <= 67 => Windows.UI.Color.FromArgb(255, 91, 164, 232),
                >= 71 and <= 77 => Windows.UI.Color.FromArgb(255, 166, 225, 245),
                >= 80 and <= 82 => Windows.UI.Color.FromArgb(255, 91, 164, 232),
                85 or 86 => Windows.UI.Color.FromArgb(255, 166, 225, 245),
                >= 95 => Windows.UI.Color.FromArgb(255, 190, 147, 255),
                _ => Windows.UI.Color.FromArgb(255, 205, 214, 224)
            };
            return new SolidColorBrush(color);
        }

        private static string GetWeatherDescription(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "Clear",
                1 => "Mostly clear",
                2 => "Partly cloudy",
                3 => "Cloudy",
                45 or 48 => "Foggy",
                >= 51 and <= 57 => "Drizzle",
                >= 61 and <= 67 => "Rain",
                >= 71 and <= 77 => "Snow",
                >= 80 and <= 82 => "Rain showers",
                85 or 86 => "Snow showers",
                >= 95 => "Thunderstorms",
                _ => "Cloudy"
            };
        }

#if false
        private async Task UpdateWeatherRadarAsync(LocationResult location)
        {
            if (!double.TryParse(location.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(location.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.OpenWeatherApiKey))
            {
                WeatherRadarProgressRing.IsActive = false;
                WeatherRadarProgressRing.Visibility = Visibility.Collapsed;
                WeatherRadarStatusTextBlock.Text = "OpenWeather token required";
                WeatherRadarStatusTextBlock.Visibility = Visibility.Visible;
                return;
            }

            var token = Uri.EscapeDataString(_settings.OpenWeatherApiKey);
            var html = string.Create(CultureInfo.InvariantCulture, $$"""
                <!doctype html>
                <html>
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
                  <style>html,body,#map{height:100%;margin:0;background:#202020} .message{height:100%;display:grid;place-items:center;color:#aaa;font:14px Segoe UI,sans-serif}</style>
                </head>
                <body>
                  <div id="map"></div>
                  <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
                  <script>
                    const map = L.map('map').setView([{{latitude:F6}}, {{longitude:F6}}], 7);
                    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                      maxZoom: 19,
                      attribution: '&copy; OpenStreetMap contributors'
                    }).addTo(map);
                    L.tileLayer('https://tile.openweathermap.org/map/precipitation_new/{z}/{x}/{y}.png?appid={{token}}', {
                      opacity: 0.72,
                      attribution: '&copy; OpenWeather'
                    }).addTo(map);
                    L.circleMarker([{{latitude:F6}}, {{longitude:F6}}], {radius:6,color:'#fff',weight:2,fillColor:'#3edac8',fillOpacity:1}).addTo(map);
                  </script>
                </body>
                </html>
                """);

            try
            {
                // The control must be visible before WebView2 is initialized.
                WeatherRadarWebView.Visibility = Visibility.Visible;
                await WeatherRadarWebView.EnsureCoreWebView2Async();
                WeatherRadarWebView.NavigateToString(html);
                WeatherRadarStatusTextBlock.Visibility = Visibility.Collapsed;
            }
            catch
            {
                WeatherRadarWebView.Visibility = Visibility.Collapsed;
                WeatherRadarStatusTextBlock.Text = "Weather radar unavailable";
                WeatherRadarStatusTextBlock.Visibility = Visibility.Visible;
            }
            finally
            {
                if (WeatherRadarWebView.Visibility != Visibility.Visible)
                {
                    WeatherRadarProgressRing.IsActive = false;
                    WeatherRadarProgressRing.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void WeatherRadarWebView_NavigationCompleted(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
        {
            WeatherRadarProgressRing.IsActive = false;
            WeatherRadarProgressRing.Visibility = Visibility.Collapsed;
            WeatherRadarWebView.Visibility = args.IsSuccess ? Visibility.Visible : Visibility.Collapsed;
            WeatherRadarStatusTextBlock.Text = "Weather radar unavailable";
            WeatherRadarStatusTextBlock.Visibility = args.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
        }

        private void WeatherDetailsTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActivateSelectedWeatherDetail();
        }

        private void ActivateSelectedWeatherDetail()
        {
            if (WeatherDetailsTabView.SelectedItem is not TabViewItem item)
            {
                return;
            }

            var tag = item.Tag?.ToString();
            if (tag == "map" &&
                !string.IsNullOrWhiteSpace(_lastLatitude) &&
                !string.IsNullOrWhiteSpace(_lastLongitude))
            {
                LocationMapProgressRing.Visibility = Visibility.Visible;
                LocationMapProgressRing.IsActive = true;
                LocationMapStatusTextBlock.Visibility = Visibility.Collapsed;
                _ = UpdateLocationMapAsync(new LocationResult(
                    _lastCity ?? "",
                    _lastState ?? "",
                    _lastLatitude,
                    _lastLongitude));
                return;
            }

            if (tag == "hourly")
            {
                if (_hourlyForecast.Count < 2 &&
                    !string.IsNullOrWhiteSpace(_lastLatitude) &&
                    !string.IsNullOrWhiteSpace(_lastLongitude))
                {
                    HourlyForecastProgressRing.Visibility = Visibility.Visible;
                    HourlyForecastProgressRing.IsActive = true;
                    HourlyForecastStatusTextBlock.Visibility = Visibility.Collapsed;
                    _ = RefreshHourlyForecastAsync(new LocationResult(
                        _lastCity ?? "",
                        _lastState ?? "",
                        _lastLatitude,
                        _lastLongitude));
                }
                else
                {
                    DispatcherQueue.TryEnqueue(() => RenderHourlyForecast());
                }
                return;
            }

            if (tag == "radar" &&
                !string.IsNullOrWhiteSpace(_lastLatitude) &&
                !string.IsNullOrWhiteSpace(_lastLongitude))
            {
                WeatherRadarProgressRing.Visibility = Visibility.Visible;
                WeatherRadarProgressRing.IsActive = true;
                WeatherRadarStatusTextBlock.Visibility = Visibility.Collapsed;
                _ = UpdateWeatherRadarAsync(new LocationResult(
                    _lastCity ?? "",
                    _lastState ?? "",
                    _lastLatitude,
                    _lastLongitude));
            }
        }

#endif

        private void ApplyWeather(WeatherResult weather, bool notify = true)
        {
            WeatherTextBlock.Text = $"Weather: {weather.Description}";
            TemperatureTextBlock.Text = $"Temperature: {weather.Temperature.ToString("0.0", CultureInfo.InvariantCulture)} {GetTemperatureUnit()}";
            HumidityTextBlock.Text = $"Humidity: {weather.Humidity}%";
            MemoWeatherTextBlock.Text = weather.Description;
            MemoTemperatureTextBlock.Text = $"{weather.Temperature:0}° {(_settings.WeatherUnits == "imperial" ? "F" : "C")}";
            MemoHumidityTextBlock.Text = $"Humidity: {weather.Humidity}%";
            CurrentWeatherSymbolTextBlock.Text = GetOpenWeatherSymbol(weather.Icon);
            CurrentWeatherSymbolTextBlock.Visibility = Visibility.Visible;
            MemoWeatherTextBlock.Visibility = Visibility.Visible;
            MemoTemperatureTextBlock.Visibility = Visibility.Visible;
            MemoHumidityTextBlock.Visibility = Visibility.Visible;
            if (notify)
            {
                _status.SetWeather(weather, _settings.WeatherUnits);
            }
            else
            {
                _status.SetWeatherSilently(weather, _settings.WeatherUnits);
            }
        }

        private static string GetOpenWeatherSymbol(string icon)
        {
            var code = icon.Length >= 2 ? icon[..2] : icon;
            var isNight = icon.EndsWith('n');
            return code switch
            {
                "01" => isNight ? "🌙" : "☀️",
                "02" => isNight ? "☁️" : "🌤️",
                "03" or "04" => "☁️",
                "09" or "10" => "🌧️",
                "11" => "⛈️",
                "13" => "❄️",
                "50" => "🌫️",
                _ => "☁️"
            };
        }

        private async Task ApplyAutoThemeAsync()
        {
            if (_settings.WindowsThemeMode != "auto")
            {
                return;
            }

            string? mode = null;
            var weather = _status.Weather;
            var now = DateTimeOffset.UtcNow;
            SensorStatus? espLightSensor = null;
            var canUseEspLightSensor =
                _settings.AutoThemeUseEsp &&
                _status.HasEspCapability("tsl2591") &&
                _status.TryGetSensor("tsl2591_lux", out espLightSensor) &&
                espLightSensor is not null &&
                now - espLightSensor.UpdatedAt <= TimeSpan.FromSeconds(15);

            if (_settings.AutoThemeUseOpenWeather && weather is not null)
            {
                var isDay = now >= weather.Sunrise && now < weather.Sunset;
                if (!isDay)
                {
                    mode = "dark";
                }
                else if (!canUseEspLightSensor)
                {
                    mode = "light";
                }
            }

            if (mode is null && canUseEspLightSensor && espLightSensor is not null)
            {
                var lux = (int)Math.Round(espLightSensor.Value, MidpointRounding.AwayFromZero);
                var darkBelowLux = (int)Math.Round(_settings.AutoThemeDarkBelowLux, MidpointRounding.AwayFromZero);
                var lightAboveLux = (int)Math.Round(_settings.AutoThemeLightAboveLux, MidpointRounding.AwayFromZero);

                if (lux < darkBelowLux)
                {
                    mode = "dark";
                }
                else if (lux > lightAboveLux)
                {
                    mode = "light";
                }
            }

            if (mode is null)
            {
                return;
            }

            if (WindowsThemeService.GetCurrentMode().Equals(mode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await WindowsThemeService.ApplyAsync(mode);
            ApplyAppThemeFromWindows(mode);
            RenderMemoCalendar();
            _status.NotifyThemeChanged();
        }

        private void Status_Changed(LocalLinkStatusEvent statusEvent)
        {
            var weatherChanged = statusEvent.Changes.Any(change =>
                change.Equals("weather", StringComparison.OrdinalIgnoreCase));
            var sensorChanged = statusEvent.Changes.Any(change =>
                change.StartsWith("sensors.", StringComparison.OrdinalIgnoreCase));
            var capabilitiesChanged = statusEvent.Changes.Any(change =>
                change.Equals("espCapabilities", StringComparison.OrdinalIgnoreCase));
            var displayChanged = statusEvent.Changes.Any(change =>
                change.Equals("espDisplay", StringComparison.OrdinalIgnoreCase));
            var accentMonthChanged = statusEvent.Changes.Any(change =>
                change.Equals("accentMonth", StringComparison.OrdinalIgnoreCase));
            var timerAlarmChanged = statusEvent.Changes.Any(change =>
                change.Equals("espTimerAlarm", StringComparison.OrdinalIgnoreCase));

            if (!weatherChanged && !sensorChanged && !capabilitiesChanged && !displayChanged && !accentMonthChanged && !timerAlarmChanged)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(async () =>
            {
                if (sensorChanged)
                {
                    UpdateEspLuxDisplay();
                }

                if (capabilitiesChanged)
                {
                    UpdateEspCapabilityVisibility();
                }

                if (weatherChanged || displayChanged || capabilitiesChanged || accentMonthChanged)
                {
                    RenderWs2812Preview();
                }

                if (timerAlarmChanged && _status.EspTimerAlarm)
                {
                    _ws2812TimerFinishedAt ??= DateTimeOffset.Now;
                    RenderWs2812Preview();
                    _ = ShowWs2812TimerAlarmDialogAsync();
                }

                if (weatherChanged)
                {
                    await _server.BroadcastWeatherAsync();
                }

                await ApplyAutoThemeAsync();
            });
        }

        private void UpdateEspLuxDisplay()
        {
            EspLuxValueTextBlock.Text = _status.TryGetSensor("tsl2591_lux", out var sensor) && sensor is not null
                ? $"{Math.Round(sensor.Value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)} {sensor.Unit ?? "lux"}"
                : "-- lux";
        }

        private void InitializeWs2812Preview()
        {
            var panelBorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(18, 255, 255, 255));

            for (var index = 0; index < 256; index++)
            {
                var pixel = new Ellipse
                {
                    Fill = _ws2812OffBrush
                };
                _ws2812PreviewPixels.Add(pixel);
                Ws2812PreviewCanvas.Children.Add(pixel);
            }

            for (var index = 0; index < 3; index++)
            {
                var separator = new Line
                {
                    Stroke = panelBorderBrush,
                    StrokeThickness = 1
                };
                _ws2812PanelSeparators.Add(separator);
                Ws2812PreviewCanvas.Children.Add(separator);
            }

            _ws2812PanelOutline.Stroke = panelBorderBrush;
            _ws2812PanelOutline.StrokeThickness = 1;
            _ws2812PanelOutline.IsHitTestVisible = false;
            Ws2812PreviewCanvas.Children.Add(_ws2812PanelOutline);

            _ws2812PreviewTimer = DispatcherQueue.CreateTimer();
            _ws2812PreviewTimer.Interval = TimeSpan.FromMilliseconds(100);
            _ws2812PreviewTimer.Tick += (_, _) =>
            {
                if (Ws2812Page.Visibility == Visibility.Visible)
                {
                    UpdateWs2812TimerState();
                    RenderWs2812Preview();
                }
            };
            _ws2812PreviewTimer.Start();
        }

        private void InitializeWs2812DisplayContent()
        {
            Ws2812ContentModeComboBox.SelectedItem = Ws2812ContentModeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _settings.Ws2812ContentMode)
                ?? Ws2812ContentModeComboBox.Items[0];
            Ws2812FixedContentComboBox.SelectedItem = Ws2812FixedContentComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == _settings.Ws2812FixedContent)
                ?? Ws2812FixedContentComboBox.Items[0];
            Ws2812TimeCheckBox.IsChecked = _settings.Ws2812ShowTime;
            Ws2812WeatherCheckBox.IsChecked = _settings.Ws2812ShowWeather;
            Ws2812ContentSwitchSecondsComboBox.SelectedItem = Ws2812ContentSwitchSecondsComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => int.TryParse(item.Tag?.ToString(), out var seconds) &&
                    seconds == _settings.Ws2812ContentSwitchSeconds)
                ?? Ws2812ContentSwitchSecondsComboBox.Items[2];
            PopulateWs2812TimerComboBox(Ws2812TimerHoursComboBox, 99);
            PopulateWs2812TimerComboBox(Ws2812TimerMinutesComboBox, 59);
            PopulateWs2812TimerComboBox(Ws2812TimerSecondsComboBox, 59);
            Ws2812TimerHoursComboBox.SelectedIndex = Math.Clamp(_settings.Ws2812TimerHours, 0, 99);
            Ws2812TimerMinutesComboBox.SelectedIndex = Math.Clamp(_settings.Ws2812TimerMinutes, 0, 59);
            Ws2812TimerSecondsComboBox.SelectedIndex = Math.Clamp(_settings.Ws2812TimerSeconds, 0, 59);
            UpdateWs2812DisplayContentVisibility();
        }

        private static void PopulateWs2812TimerComboBox(ComboBox comboBox, int maximum)
        {
            for (var value = 0; value <= maximum; value++)
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = value.ToString("00", CultureInfo.InvariantCulture),
                    Tag = value
                });
            }
        }

        private void Ws2812ContentModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Ws2812ContentModeComboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            _settings.Ws2812ContentMode = item.Tag?.ToString() == "scrolling" ? "scrolling" : "fixed";
            UpdateWs2812DisplayContentVisibility();
            RenderWs2812Preview();
            if (!_isInitializing)
            {
                _settings.Save();
                _ = _server.BroadcastDisplayConfigAsync();
            }
        }

        private void Ws2812FixedContentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Ws2812FixedContentComboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            _settings.Ws2812FixedContent = item.Tag?.ToString() == "weather" ? "weather" : "time";
            RenderWs2812Preview();
            if (!_isInitializing)
            {
                _settings.Save();
                _ = _server.BroadcastDisplayConfigAsync();
            }
        }

        private void Ws2812DisplayContentCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _settings.Ws2812ShowTime = Ws2812TimeCheckBox.IsChecked == true;
            _settings.Ws2812ShowWeather = Ws2812WeatherCheckBox.IsChecked == true;
            _settings.Save();
            UpdateWs2812DisplayContentVisibility();
            RenderWs2812Preview();
            _ = _server.BroadcastDisplayConfigAsync();
        }

        private void Ws2812ContentSwitchSecondsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing ||
                Ws2812ContentSwitchSecondsComboBox.SelectedItem is not ComboBoxItem item ||
                !int.TryParse(item.Tag?.ToString(), out var seconds))
            {
                return;
            }

            _settings.Ws2812ContentSwitchSeconds = seconds;
            _settings.Save();
            _ = _server.BroadcastDisplayConfigAsync();
        }

        private void Ws2812TimerDurationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing ||
                Ws2812TimerHoursComboBox.SelectedIndex < 0 ||
                Ws2812TimerMinutesComboBox.SelectedIndex < 0 ||
                Ws2812TimerSecondsComboBox.SelectedIndex < 0)
            {
                return;
            }

            _settings.Ws2812TimerHours = Ws2812TimerHoursComboBox.SelectedIndex;
            _settings.Ws2812TimerMinutes = Ws2812TimerMinutesComboBox.SelectedIndex;
            _settings.Ws2812TimerSeconds = Ws2812TimerSecondsComboBox.SelectedIndex;
            _settings.Save();
        }

        private void Ws2812TimerPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                !int.TryParse(button.Tag?.ToString(), out var totalSeconds))
            {
                return;
            }

            Ws2812TimerHoursComboBox.SelectedIndex = Math.Clamp(totalSeconds / 3600, 0, 99);
            Ws2812TimerMinutesComboBox.SelectedIndex = Math.Clamp(totalSeconds / 60 % 60, 0, 59);
            Ws2812TimerSecondsComboBox.SelectedIndex = Math.Clamp(totalSeconds % 60, 0, 59);
        }

        private void UpdateWs2812DisplayContentVisibility()
        {
            var isScrolling = _settings.Ws2812ContentMode == "scrolling";
            Ws2812FixedContentComboBox.Visibility = isScrolling
                ? Visibility.Collapsed
                : Visibility.Visible;
            Ws2812ScrollingContentPanel.Visibility = isScrolling
                ? Visibility.Visible
                : Visibility.Collapsed;
            Ws2812ContentSwitchPanel.Visibility = isScrolling &&
                Ws2812TimeCheckBox.IsChecked == true &&
                Ws2812WeatherCheckBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void Ws2812TimerStartButton_Click(object sender, RoutedEventArgs e)
        {
            var totalSeconds = Ws2812TimerHoursComboBox.SelectedIndex * 3600
                + Ws2812TimerMinutesComboBox.SelectedIndex * 60
                + Ws2812TimerSecondsComboBox.SelectedIndex;
            var duration = TimeSpan.FromSeconds(totalSeconds);
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            _ws2812PausedTimerRemaining = duration;
            _ws2812TimerOriginalDuration = duration;
            _ws2812TimerEndsAt = DateTimeOffset.Now + duration;
            _ws2812TimerFinishedAt = null;
            _ws2812TimerPaused = false;
            Ws2812TimerPauseButton.Content = "Pause";
            Ws2812TimerRunningPanel.Visibility = Visibility.Visible;
            UpdateWs2812TimerState();
            RenderWs2812Preview();
            await _server.BroadcastTimerStartAsync(totalSeconds);
        }

        private async void Ws2812TimerPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_ws2812TimerPaused)
            {
                _ws2812TimerEndsAt = DateTimeOffset.Now + _ws2812PausedTimerRemaining;
                _ws2812TimerPaused = false;
                Ws2812TimerPauseButton.Content = "Pause";
                await _server.BroadcastTimerResumeAsync();
            }
            else if (_ws2812TimerEndsAt is { } endsAt)
            {
                _ws2812PausedTimerRemaining = endsAt - DateTimeOffset.Now;
                _ws2812TimerEndsAt = null;
                _ws2812TimerPaused = true;
                Ws2812TimerPauseButton.Content = "Resume";
                await _server.BroadcastTimerPauseAsync();
            }

            UpdateWs2812TimerState();
        }

        private async void Ws2812TimerCancelButton_Click(object sender, RoutedEventArgs e)
        {
            StopWs2812Timer();
            RenderWs2812Preview();
            await _server.BroadcastTimerCancelAsync();
        }

        private void UpdateWs2812TimerState()
        {
            if (_ws2812TimerEndsAt is { } endsAt)
            {
                _ws2812PausedTimerRemaining = endsAt - DateTimeOffset.Now;
                if (_ws2812PausedTimerRemaining <= TimeSpan.Zero)
                {
                    _ws2812TimerEndsAt = null;
                    _ws2812PausedTimerRemaining = TimeSpan.Zero;
                    _ws2812TimerPaused = false;
                    _ws2812TimerFinishedAt = DateTimeOffset.Now;
                    Ws2812TimerRunningPanel.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            if (!IsWs2812TimerActive())
            {
                return;
            }

            var totalSeconds = Math.Max(0, (int)Math.Ceiling(_ws2812PausedTimerRemaining.TotalSeconds));
            var hours = totalSeconds / 3600;
            var minutes = totalSeconds / 60 % 60;
            var seconds = totalSeconds % 60;
            Ws2812TimerRemainingTextBlock.Text = hours > 0
                ? $"{hours:00}:{minutes:00}:{seconds:00}"
                : $"{minutes:00}:{seconds:00}";
        }

        private bool IsWs2812TimerActive()
        {
            return _ws2812TimerEndsAt is not null || _ws2812TimerPaused;
        }

        private void StopWs2812Timer()
        {
            _ws2812TimerEndsAt = null;
            _ws2812TimerFinishedAt = null;
            _ws2812TimerOriginalDuration = TimeSpan.Zero;
            _ws2812PausedTimerRemaining = TimeSpan.Zero;
            _ws2812TimerPaused = false;
            Ws2812TimerRunningPanel.Visibility = Visibility.Collapsed;
            Ws2812TimerPauseButton.Content = "Pause";
        }

        private async Task ShowWs2812TimerAlarmDialogAsync()
        {
            if (_ws2812TimerAlarmDialogOpen || _isExiting)
            {
                return;
            }

            _ws2812TimerAlarmDialogOpen = true;
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = RootNavigationView.XamlRoot,
                    Title = "Timer finished",
                    PrimaryButtonText = "OK",
                    DefaultButton = ContentDialogButton.Primary
                };
                await dialog.ShowAsync();
                _ws2812TimerFinishedAt = null;
                _ws2812TimerOriginalDuration = TimeSpan.Zero;
                _ws2812PausedTimerRemaining = TimeSpan.Zero;
                RenderWs2812Preview();
                await _server.BroadcastTimerAcknowledgeAsync();
            }
            finally
            {
                _ws2812TimerAlarmDialogOpen = false;
            }
        }

        private void Ws2812PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            LayoutWs2812Preview();
            RenderWs2812Preview();
        }

        private void LayoutWs2812Preview()
        {
            var width = Ws2812PreviewCanvas.ActualWidth;
            var height = Ws2812PreviewCanvas.ActualHeight;
            if (width <= 0 || height <= 0 || _ws2812PreviewPixels.Count != 256)
            {
                return;
            }

            const double pixelGap = 2;
            var pixelSize = Math.Max(2, Math.Min(
                (width - pixelGap * 31) / 32,
                (height - pixelGap * 7) / 8));
            var matrixWidth = pixelSize * 32 + pixelGap * 31;
            var matrixHeight = pixelSize * 8 + pixelGap * 7;
            var startX = (width - matrixWidth) / 2;
            var startY = (height - matrixHeight) / 2;

            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var pixel = _ws2812PreviewPixels[y * 32 + x];
                    pixel.Width = pixelSize;
                    pixel.Height = pixelSize;
                    Canvas.SetLeft(pixel, startX + x * (pixelSize + pixelGap));
                    Canvas.SetTop(pixel, startY + y * (pixelSize + pixelGap));
                }
            }

            for (var index = 0; index < _ws2812PanelSeparators.Count; index++)
            {
                var separatorX = startX + (index + 1) * 8 * pixelSize + ((index + 1) * 8 - 0.5) * pixelGap;
                var separator = _ws2812PanelSeparators[index];
                separator.X1 = separatorX;
                separator.X2 = separatorX;
                separator.Y1 = startY - pixelGap / 2;
                separator.Y2 = startY + matrixHeight + pixelGap / 2;
            }

            Canvas.SetLeft(_ws2812PanelOutline, startX - pixelGap / 2);
            Canvas.SetTop(_ws2812PanelOutline, startY - pixelGap / 2);
            _ws2812PanelOutline.Width = matrixWidth + pixelGap;
            _ws2812PanelOutline.Height = matrixHeight + pixelGap;
        }

        private void RenderWs2812Preview()
        {
            if (_ws2812PreviewPixels.Count != 256)
            {
                return;
            }

            foreach (var pixel in _ws2812PreviewPixels)
            {
                pixel.Fill = _ws2812OffBrush;
            }

            var display = _status.EspDisplay;
            if (!_status.HasEspCapability("ws2812") || display is null)
            {
                return;
            }

            // WS2812 uses a fixed hardware palette. App light/dark theme must not
            // alter clock, timer, temperature, humidity, alarm, or status colors.
            var monthColor = Ws2812MonthColors[Math.Clamp(_status.AccentMonth, 0, 11)];
            var color = Windows.UI.Color.FromArgb(255, monthColor.R, monthColor.G, monthColor.B);
            var litBrush = new SolidColorBrush(color);

            if (display.Mode == "waiting")
            {
                RenderWs2812Waiting(color);
                return;
            }

            if (_ws2812TimerFinishedAt is not null)
            {
                RenderWs2812TimerFinished(litBrush);
                return;
            }

            if (IsWs2812TimerActive())
            {
                if (_settings.Ws2812ContentMode == "scrolling")
                {
                    RenderWs2812RegularContent(litBrush, true);
                }
                else
                {
                    RenderWs2812Timer(_ws2812WhiteBrush);
                }
                RenderWs2812TimerProgress(_ws2812WhiteBrush);
                return;
            }

            RenderWs2812RegularContent(litBrush);
        }

        private void RenderWs2812RegularContent(Brush litBrush, bool includeTimer = false)
        {
            var contents = GetWs2812EnabledContents(includeTimer);
            var contentIndex = 0;
            if (_settings.Ws2812ContentMode == "scrolling" && contents.Count > 1)
            {
                var interval = Math.Clamp(_settings.Ws2812ContentSwitchSeconds, 3, 60);
                var rotationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                contentIndex = (int)(rotationTime / (interval * 1000L) % contents.Count);
            }

            RenderWs2812Content(contents[contentIndex], litBrush);
        }

        private List<string> GetWs2812EnabledContents(bool includeTimer = false)
        {
            var contents = new List<string>(2);
            if (_settings.Ws2812ContentMode != "scrolling")
            {
                contents.Add(_settings.Ws2812FixedContent == "weather" && _status.Weather is not null
                    ? "weather"
                    : "time");
                return contents;
            }

            if (Ws2812TimeCheckBox.IsChecked == true)
            {
                contents.Add("time");
            }
            if (Ws2812WeatherCheckBox.IsChecked == true && _status.Weather is not null)
            {
                contents.Add("weather");
            }
            if (includeTimer)
            {
                contents.Add("timer");
            }
            if (contents.Count == 0)
            {
                contents.Add("blank");
            }
            return contents;
        }

        private void RenderWs2812Content(string content, Brush litBrush)
        {
            if (content == "weather")
            {
                RenderWs2812Weather(litBrush);
            }
            else if (content == "time")
            {
                RenderWs2812Clock();
            }
            else if (content == "timer")
            {
                RenderWs2812Timer(_ws2812WhiteBrush);
            }
        }

        private void RenderWs2812Clock()
        {
            var now = DateTime.Now;
            RenderWs2812FourDigits(
                now.Hour,
                now.Minute,
                _ws2812WhiteBrush,
                DateTimeOffset.Now.ToUnixTimeMilliseconds() % 3000 < 2000);
        }

        private void RenderWs2812Timer(Brush brush)
        {
            var totalSeconds = Math.Max(0, (int)Math.Ceiling(_ws2812PausedTimerRemaining.TotalSeconds));
            if (totalSeconds >= 3600)
            {
                RenderWs2812FourDigits(
                    Math.Min(99, totalSeconds / 3600),
                    totalSeconds / 60 % 60,
                    brush,
                    true);
            }
            else
            {
                RenderWs2812FourDigits(totalSeconds / 60, totalSeconds % 60, brush, true);
            }
        }

        private void RenderWs2812TimerProgress(Brush brush)
        {
            if (_ws2812TimerOriginalDuration <= TimeSpan.Zero)
            {
                return;
            }

            var progress = Math.Clamp(
                _ws2812PausedTimerRemaining.TotalMilliseconds / _ws2812TimerOriginalDuration.TotalMilliseconds,
                0,
                1);
            var litPixels = (int)Math.Ceiling(progress * 32);
            for (var x = 0; x < litPixels; x++)
            {
                SetWs2812PreviewPixel(x, 7, brush);
            }
        }

        private void RenderWs2812TimerFinished(Brush brush)
        {
            if (_ws2812TimerFinishedAt is not { } finishedAt)
            {
                return;
            }

            var elapsedMilliseconds = (DateTimeOffset.Now - finishedAt).TotalMilliseconds;
            RenderWs2812RegularContent(
                brush,
                _settings.Ws2812ContentMode == "scrolling");
            if (elapsedMilliseconds < 0 || (int)(elapsedMilliseconds / 250) % 2 != 0)
            {
                return;
            }

            foreach (var pixel in _ws2812PreviewPixels)
            {
                if (!ReferenceEquals(pixel.Fill, _ws2812OffBrush))
                {
                    pixel.Fill = _ws2812RedBrush;
                }
            }
        }

        private void RenderWs2812FourDigits(int leftValue, int rightValue, Brush brush, bool showColon)
        {
            int[][] digitRows =
            [
                [0b111, 0b101, 0b101, 0b101, 0b111],
                [0b010, 0b110, 0b010, 0b010, 0b111],
                [0b111, 0b001, 0b111, 0b100, 0b111],
                [0b111, 0b001, 0b111, 0b001, 0b111],
                [0b101, 0b101, 0b111, 0b001, 0b001],
                [0b111, 0b100, 0b111, 0b001, 0b111],
                [0b111, 0b100, 0b111, 0b101, 0b111],
                [0b111, 0b001, 0b001, 0b001, 0b001],
                [0b111, 0b101, 0b111, 0b101, 0b111],
                [0b111, 0b101, 0b111, 0b001, 0b111]
            ];
            int[] digits = [leftValue / 10 % 10, leftValue % 10, rightValue / 10 % 10, rightValue % 10];
            int[] positions = [6, 10, 18, 22];

            for (var digitIndex = 0; digitIndex < digits.Length; digitIndex++)
            {
                for (var row = 0; row < 5; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        if (((digitRows[digits[digitIndex]][row] >> (2 - column)) & 1) != 0)
                        {
                            SetWs2812PreviewPixel(positions[digitIndex] + column, 1 + row, brush);
                        }
                    }
                }
            }

            if (showColon)
            {
                SetWs2812PreviewPixel(15, 2, brush);
                SetWs2812PreviewPixel(15, 4, brush);
            }
        }

        private void RenderWs2812Weather(Brush brush)
        {
            var weather = _status.Weather;
            if (weather is null)
            {
                return;
            }

            var temperature = Math.Clamp((int)Math.Round(weather.Temperature), -99, 999);
            var text = temperature.ToString(CultureInfo.InvariantCulture);
            var temperatureBrush = new SolidColorBrush(GetWs2812TemperatureColor(weather.Temperature, weather.Units));
            var unit = weather.Units.ToLowerInvariant() switch
            {
                "imperial" => 'F',
                "standard" => '\0',
                _ => 'C'
            };
            DrawWs2812Temperature(text, unit, 2, temperatureBrush);

            var humidityBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 190, 235));
            DrawWs2812Humidity(Math.Clamp(weather.Humidity, 0, 100), humidityBrush);
        }

        private void DrawWs2812Temperature(string text, char unit, int startX, Brush brush)
        {
            int[][] digits =
            [
                [7, 5, 5, 5, 7], [2, 6, 2, 2, 7], [7, 1, 7, 4, 7], [7, 1, 7, 1, 7], [5, 5, 7, 1, 1],
                [7, 4, 7, 1, 7], [7, 4, 7, 5, 7], [7, 1, 1, 1, 1], [7, 5, 7, 5, 7], [7, 5, 7, 1, 7]
            ];
            var cursor = startX;
            foreach (var character in text)
            {
                if (character == '-')
                {
                    for (var column = 0; column < 3; column++)
                        SetWs2812PreviewPixel(cursor + column, 3, brush);
                }
                else if (character is >= '0' and <= '9')
                {
                    var glyph = digits[character - '0'];
                    for (var row = 0; row < 5; row++)
                        for (var column = 0; column < 3; column++)
                            if (((glyph[row] >> (2 - column)) & 1) != 0)
                                SetWs2812PreviewPixel(cursor + column, 1 + row, brush);
                }
                cursor += 4;
            }

            SetWs2812PreviewPixel(cursor, 1, brush);
            SetWs2812PreviewPixel(cursor + 1, 1, brush);
            SetWs2812PreviewPixel(cursor, 2, brush);
            SetWs2812PreviewPixel(cursor + 1, 2, brush);
            cursor += 2;

            if (unit == '\0')
            {
                return;
            }

            int[] unitRows = unit switch
            {
                'F' => [0b111, 0b100, 0b110, 0b100, 0b100],
                _ => [0b111, 0b100, 0b100, 0b100, 0b111]
            };
            for (var row = 0; row < 5; row++)
                for (var column = 0; column < 3; column++)
                    if (((unitRows[row] >> (2 - column)) & 1) != 0)
                        SetWs2812PreviewPixel(cursor + column, 1 + row, brush);
        }

        private void DrawWs2812Humidity(int humidity, Brush brush)
        {
            int[][] digits =
            [
                [0b111, 0b101, 0b101, 0b101, 0b111],
                [0b010, 0b110, 0b010, 0b010, 0b111],
                [0b111, 0b001, 0b111, 0b100, 0b111],
                [0b111, 0b001, 0b111, 0b001, 0b111],
                [0b101, 0b101, 0b111, 0b001, 0b001],
                [0b111, 0b100, 0b111, 0b001, 0b111],
                [0b111, 0b100, 0b111, 0b101, 0b111],
                [0b111, 0b001, 0b001, 0b001, 0b001],
                [0b111, 0b101, 0b111, 0b101, 0b111],
                [0b111, 0b101, 0b111, 0b001, 0b111]
            ];
            var text = humidity.ToString(CultureInfo.InvariantCulture);
            var cursor = 32 - (text.Length * 4 + 3) - 2;
            foreach (var character in text)
            {
                var glyph = digits[character - '0'];
                for (var row = 0; row < 5; row++)
                    for (var column = 0; column < 3; column++)
                        if (((glyph[row] >> (2 - column)) & 1) != 0)
                            SetWs2812PreviewPixel(cursor + column, 1 + row, brush);
                cursor += 4;
            }

            int[] percentRows = [0b101, 0b001, 0b010, 0b100, 0b101];
            for (var row = 0; row < 5; row++)
                for (var column = 0; column < 3; column++)
                    if (((percentRows[row] >> (2 - column)) & 1) != 0)
                        SetWs2812PreviewPixel(cursor + column, 1 + row, brush);
        }

        private void DrawWs2812Sun(int centerX, int centerY, Brush brush)
        {
            for (var y = centerY - 1; y <= centerY + 1; y++)
                for (var x = centerX - 1; x <= centerX + 1; x++)
                    SetWs2812PreviewPixel(x, y, brush);

            (int X, int Y)[] rays =
            [
                (centerX, centerY - 3), (centerX, centerY + 3),
                (centerX - 3, centerY), (centerX + 3, centerY),
                (centerX - 2, centerY - 2), (centerX + 2, centerY - 2),
                (centerX - 2, centerY + 2), (centerX + 2, centerY + 2)
            ];
            foreach (var ray in rays)
            {
                SetWs2812PreviewPixel(ray.X, ray.Y, brush);
            }
        }

        private void DrawWs2812Moon(int centerX, int centerY, Brush brush)
        {
            int[] rows = [0b01110, 0b11100, 0b11000, 0b11000, 0b11100, 0b01110];
            for (var row = 0; row < rows.Length; row++)
                for (var column = 0; column < 5; column++)
                    if (((rows[row] >> (4 - column)) & 1) != 0)
                        SetWs2812PreviewPixel(centerX - 2 + column, centerY - 3 + row, brush);
        }

        private void DrawWs2812Snowflake(int centerX, int centerY, Brush brush)
        {
            for (var offset = -3; offset <= 3; offset++)
            {
                SetWs2812PreviewPixel(centerX + offset, centerY, brush);
                SetWs2812PreviewPixel(centerX, centerY + offset, brush);
            }
            SetWs2812PreviewPixel(centerX - 2, centerY - 2, brush);
            SetWs2812PreviewPixel(centerX + 2, centerY - 2, brush);
            SetWs2812PreviewPixel(centerX - 2, centerY + 2, brush);
            SetWs2812PreviewPixel(centerX + 2, centerY + 2, brush);
        }

        private void DrawWs2812Fog(int x, int y, Brush brush)
        {
            for (var column = 0; column < 7; column++)
            {
                SetWs2812PreviewPixel(x + column, y, brush);
                SetWs2812PreviewPixel(x + column, y + 4, brush);
            }
            for (var column = 1; column < 6; column++)
            {
                SetWs2812PreviewPixel(x + column, y + 2, brush);
            }
        }

        private static Windows.UI.Color GetWs2812TemperatureColor(double temperature, string units)
        {
            var celsius = units.ToLowerInvariant() switch
            {
                "imperial" => (temperature - 32) * 5 / 9,
                "standard" => temperature - 273.15,
                _ => temperature
            };

            // Temperature uses the Calendar daytime palette in reverse; green
            // begins at 23 C and continues through the cooler green range.
            var clampedCelsius = Math.Clamp(celsius, -20, 30);
            var palettePosition = clampedCelsius >= 23
                ? (30 - clampedCelsius) / 7 * 4
                : clampedCelsius >= 15
                    ? 4 + (23 - clampedCelsius) / 8 * 3
                    : 7 + (15 - clampedCelsius) / 35 * 4;
            var startIndex = Math.Clamp((int)Math.Floor(palettePosition), 0, MemoLightMonthColors.Length - 1);
            var endIndex = Math.Min(startIndex + 1, MemoLightMonthColors.Length - 1);
            var localPosition = palettePosition - startIndex;
            var start = MemoLightMonthColors[startIndex];
            var end = MemoLightMonthColors[endIndex];

            return Windows.UI.Color.FromArgb(
                255,
                (byte)Math.Round(start.R + (end.R - start.R) * localPosition),
                (byte)Math.Round(start.G + (end.G - start.G) * localPosition),
                (byte)Math.Round(start.B + (end.B - start.B) * localPosition));
        }

        private void DrawWs2812TinyText(string text, int x, int y, Brush brush)
        {
            int[][] digits =
            [
                [7, 5, 5, 5, 7], [2, 6, 2, 2, 7], [7, 1, 7, 4, 7], [7, 1, 7, 1, 7], [5, 5, 7, 1, 1],
                [7, 4, 7, 1, 7], [7, 4, 7, 5, 7], [7, 1, 1, 1, 1], [7, 5, 7, 5, 7], [7, 5, 7, 1, 7]
            ];
            var cursor = x;
            foreach (var character in text)
            {
                if (character == '-')
                {
                    for (var column = 0; column < 3; column++) SetWs2812PreviewPixel(cursor + column, y + 2, brush);
                }
                else if (character is >= '0' and <= '9')
                {
                    var glyph = digits[character - '0'];
                    for (var row = 0; row < 5; row++)
                        for (var column = 0; column < 3; column++)
                            if (((glyph[row] >> (2 - column)) & 1) != 0) SetWs2812PreviewPixel(cursor + column, y + row, brush);
                }
                cursor += 4;
            }
        }

        private void DrawWs2812Cloud(int x, int y, Brush brush)
        {
            int[] rows = [0b001100, 0b011110, 0b111111, 0b111111, 0b011110];
            for (var row = 0; row < rows.Length; row++)
                for (var column = 0; column < 6; column++)
                    if (((rows[row] >> (5 - column)) & 1) != 0) SetWs2812PreviewPixel(x + column, y + row, brush);
        }

        private void RenderWs2812Waiting(Windows.UI.Color color)
        {
            (int X, int Y)[] perimeter =
            [
                (13, 1), (14, 1), (15, 1), (16, 1), (17, 1), (18, 1),
                (18, 2), (18, 3), (18, 4), (18, 5), (18, 6),
                (17, 6), (16, 6), (15, 6), (14, 6), (13, 6),
                (13, 5), (13, 4), (13, 3), (13, 2)
            ];
            var head = (int)((DateTimeOffset.Now.ToUnixTimeMilliseconds() / 70) % perimeter.Length);
            for (var trail = 0; trail < 7; trail++)
            {
                var point = perimeter[(head - trail + perimeter.Length) % perimeter.Length];
                var alpha = 1.0 - trail / 7.0;
                var brush = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    255,
                    (byte)Math.Round(color.R * alpha),
                    (byte)Math.Round(color.G * alpha),
                    (byte)Math.Round(color.B * alpha)));
                SetWs2812PreviewPixel(point.X, point.Y, brush);
            }
        }

        private void SetWs2812PreviewPixel(int x, int y, Brush brush)
        {
            if (x is >= 0 and < 32 && y is >= 0 and < 8)
            {
                _ws2812PreviewPixels[y * 32 + x].Fill = brush;
            }
        }

        private void UpdateTokenButtonState()
        {
            SetTokensButton.IsEnabled = _tokensLocked || HasWeatherKey();
            SetTokensButton.Content = _tokensLocked ? "Edit Token" : "Set Token";
        }

        private bool HasWeatherKey()
        {
            return !string.IsNullOrWhiteSpace(OpenWeatherKeyBox.Text);
        }

        private void ApplyTokenEditState()
        {
            OpenWeatherKeyBox.IsEnabled = !_tokensLocked;
        }

        private string GetTemperatureUnit()
        {
            return _settings.WeatherUnits.ToLowerInvariant() switch
            {
                "imperial" => "°F",
                "standard" => "K",
                _ => "°C"
            };
        }

        private static string CapitalizeWords(string value)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
        }

        private async void WindowsThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            var mode = GetSelectedWindowsThemeMode();
            _settings.WindowsThemeMode = mode;
            _settings.Save();

            if (mode == "auto")
            {
                UpdateAutoThemeOptionsVisibility();
                ApplyAppThemeFromWindows();
                RenderMemoCalendar();
                _status.NotifyThemeChanged();
                await RefreshWeatherAsync();
                await ApplyAutoThemeAsync();
                return;
            }

            UpdateAutoThemeOptionsVisibility();

            await WindowsThemeService.ApplyAsync(mode);
            ApplyAppThemeFromWindows(mode);
            RenderMemoCalendar();
            _status.NotifyThemeChanged();
        }

        private async void AutoThemeSourceToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _settings.AutoThemeUseOpenWeather = AutoThemeOpenWeatherToggle.IsOn;
            _settings.AutoThemeUseEsp = AutoThemeEspToggle.IsOn;
            _settings.Save();
            UpdateAutoThemeOptionsVisibility();
            await ApplyAutoThemeAsync();
        }

        private async void LuxThresholdNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isInitializing || double.IsNaN(DarkBelowLuxNumberBox.Value) || double.IsNaN(LightAboveLuxNumberBox.Value))
            {
                return;
            }

            var darkBelowLux = Math.Max(0, Math.Round(DarkBelowLuxNumberBox.Value, MidpointRounding.AwayFromZero));
            var lightAboveLux = Math.Max(0, Math.Round(LightAboveLuxNumberBox.Value, MidpointRounding.AwayFromZero));

            _isInitializing = true;
            DarkBelowLuxNumberBox.Value = darkBelowLux;
            LightAboveLuxNumberBox.Value = lightAboveLux;
            _isInitializing = false;

            _settings.AutoThemeDarkBelowLux = darkBelowLux;
            _settings.AutoThemeLightAboveLux = lightAboveLux;
            _settings.Save();
            await ApplyAutoThemeAsync();
        }

        private void UpdateAutoThemeOptionsVisibility()
        {
            var isAuto = GetSelectedWindowsThemeMode() == "auto";
            var hasLightSensor = _status.HasEspCapability("tsl2591");
            AutoThemeOptionsPanel.Visibility = isAuto ? Visibility.Visible : Visibility.Collapsed;
            EspLightSensorSourceRow.Visibility = isAuto && hasLightSensor
                ? Visibility.Visible
                : Visibility.Collapsed;
            EspLuxThresholdPanel.Visibility = isAuto && hasLightSensor && AutoThemeEspToggle.IsOn
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateEspCapabilityVisibility()
        {
            var hasWs2812 = _status.HasEspCapability("ws2812");
            Ws2812NavItem.Visibility = hasWs2812 ? Visibility.Visible : Visibility.Collapsed;
            UpdateAutoThemeOptionsVisibility();

            if (!hasWs2812 && Ws2812Page.Visibility == Visibility.Visible)
            {
                RootNavigationView.SelectedItem = MemoNavItem;
                ShowNavigationPage(MemoPage);
            }
        }

        private async void WeatherUnitsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _settings.WeatherUnits = GetSelectedWeatherUnits();
            _settings.Save();
            await RefreshWeatherAsync();
            if (!string.IsNullOrWhiteSpace(_lastLatitude) && !string.IsNullOrWhiteSpace(_lastLongitude))
            {
                await RefreshHourlyForecastAsync(new LocationResult(
                    _lastCity ?? "",
                    _lastState ?? "",
                    _lastLatitude,
                    _lastLongitude));
            }
        }

        private async Task InitializeStartupToggleAsync()
        {
            _isUpdatingStartupToggle = true;
            try
            {
                var enabled = await StartupService.IsEnabledAsync();
                if (_settings.StartWithWindows)
                {
                    try
                    {
                        // Re-register enabled legacy startup entries as the
                        // zero-delay logon task used by the fast startup mode.
                        await StartupService.SetEnabledAsync(true);
                        enabled = await StartupService.IsEnabledAsync();
                    }
                    catch
                    {
                        // Windows does not allow an app to override a startup task
                        // that the user disabled in Settings or Task Manager.
                    }
                }
                StartupToggleSwitch.IsOn = enabled;
                _settings.StartWithWindows = enabled;
                _settings.Save();
            }
            catch
            {
                StartupToggleSwitch.IsOn = false;
            }
            finally
            {
                _isUpdatingStartupToggle = false;
            }
        }

        private async void StartupToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _isUpdatingStartupToggle)
            {
                return;
            }

            _isUpdatingStartupToggle = true;
            try
            {
                var requestedState = StartupToggleSwitch.IsOn;
                await StartupService.SetEnabledAsync(requestedState);
                var enabled = await StartupService.IsEnabledAsync();
                StartupToggleSwitch.IsOn = enabled;
                _settings.StartWithWindows = enabled;
                _settings.Save();
            }
            catch
            {
                StartupToggleSwitch.IsOn = await StartupService.IsEnabledAsync();
            }
            finally
            {
                _isUpdatingStartupToggle = false;
            }
        }

        private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            FrameworkElement targetPage = tag switch
            {
                "status" => StatusPage,
                "cameras" => CamerasPage,
                "location" => LocationPage,
                "ws2812" => Ws2812Page,
                "settings" => SettingsPage,
                _ => MemoPage
            };

            ShowNavigationPage(targetPage);
        }

        private void ShowNavigationPage(FrameworkElement targetPage)
        {
            FrameworkElement[] pages = [StatusPage, SettingsPage, MemoPage, CamerasPage, LocationPage, Ws2812Page];
            if (pages.All(page => page == targetPage ? page.Visibility == Visibility.Visible : page.Visibility == Visibility.Collapsed))
            {
                return;
            }

            foreach (var page in pages)
            {
                if (page != targetPage)
                {
                    page.Visibility = Visibility.Collapsed;
                    page.Opacity = 1;
                }
            }

            var translateTransform = new TranslateTransform { Y = 24 };
            targetPage.RenderTransform = translateTransform;
            targetPage.Opacity = 0;
            targetPage.Visibility = Visibility.Visible;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromMilliseconds(200));
            var opacityAnimation = new DoubleAnimation
            {
                To = 1,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(opacityAnimation, targetPage);
            Storyboard.SetTargetProperty(opacityAnimation, nameof(UIElement.Opacity));

            var slideAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, translateTransform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.Y));

            var storyboard = new Storyboard();
            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(slideAnimation);
            storyboard.Begin();
            if (targetPage == CamerasPage)
            {
                UpdateCameraPreviewContainerHeight();
            }
            else if (targetPage == MemoPage || targetPage == LocationPage)
            {
                UpdateExpandablePageHeights();
            }
        }

        private async void ScanCameraButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanningCamera)
            {
                return;
            }

            _isScanningCamera = true;
            ScanCameraButton.IsEnabled = false;
            SetCameraStatus("Scanning for AMB82...");

            try
            {
                var addedCount = 0;
                var restartedCount = 0;
                var cameras = await ScanForAmb82RtspAsync(
                    TimeSpan.FromSeconds(4),
                    onCameraFound: camera => RunOnUiThreadAsync(() =>
                    {
                        // Start each camera as soon as it replies; discovery can
                        // continue in parallel with video initialization.
                        if (AddCameraMonitor(camera))
                        {
                            addedCount++;
                        }
                        else if (RestartExistingCameraMonitor(camera))
                        {
                            restartedCount++;
                        }

                        return Task.CompletedTask;
                    }));
                if (cameras.Count == 0)
                {
                    SetCameraStatus("No AMB82 reply received.");
                    return;
                }

                if (addedCount > 0 || restartedCount > 0)
                {
                    _settings.CameraRtspUrl = cameras[0].RtspUrl;
                    _settings.Save();
                }

                SetCameraStatus(addedCount == 0
                    ? $"Found {cameras.Count} AMB82 camera(s). Restarted {restartedCount}."
                    : $"Found {cameras.Count} AMB82 camera(s). Added {addedCount}, restarted {restartedCount}.");
            }
            catch (Exception ex)
            {
                SetCameraStatus($"Scan failed: {ex.Message}");
            }
            finally
            {
                _isScanningCamera = false;
                ScanCameraButton.IsEnabled = true;
            }
        }

        private bool AddCameraMonitor(Amb82CameraInfo camera)
        {
            if (_cameraMonitors.ContainsKey(camera.RtspUrl))
            {
                return false;
            }

            var videoView = new Image
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Stretch = Stretch.Uniform
            };
            var blackout = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
                Visibility = Visibility.Visible,
                IsHitTestVisible = false
            };
            var placeholder = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                Opacity = 0.56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var reconnectButton = new Button
            {
                Content = new SymbolIcon(Symbol.Refresh),
                Width = 42,
                Height = 42,
                MinWidth = 42,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            ToolTipService.SetToolTip(reconnectButton, "Reconnect");

            var closeButton = new Button
            {
                Content = CreateCameraToolbarGlyph("\uE711"),
                Width = CameraTileControlSize,
                Height = CameraTileControlSize,
                MinWidth = CameraTileControlSize,
                CornerRadius = new CornerRadius(CameraTileControlCornerRadius),
                Padding = new Thickness(0),
                Opacity = 0.86,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(closeButton, "Close");

            var nightModeText = new TextBlock
            {
                Text = "Night",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                Height = 22,
                LineHeight = 22,
                VerticalAlignment = VerticalAlignment.Center
            };
            var nightModeToggle = new ToggleSwitch
            {
                OnContent = "",
                OffContent = "",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                MinWidth = 44,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                RequestedTheme = ElementTheme.Dark
            };
            nightModeToggle.Resources["ToggleSwitchForeground"] = new SolidColorBrush(Microsoft.UI.Colors.White);
            nightModeToggle.Resources["ToggleSwitchStrokeOff"] = new SolidColorBrush(Microsoft.UI.Colors.White);
            nightModeToggle.Resources["ToggleSwitchStrokeOn"] = new SolidColorBrush(Microsoft.UI.Colors.White);
            nightModeToggle.Resources["ToggleSwitchKnobFillOff"] = new SolidColorBrush(Microsoft.UI.Colors.White);
            nightModeToggle.Resources["ToggleSwitchKnobFillOn"] = new SolidColorBrush(Microsoft.UI.Colors.White);
            ToolTipService.SetToolTip(nightModeToggle, "Night Mode");
            var nightModeHost = new Viewbox
            {
                Child = nightModeToggle,
                Height = 22,
                MaxWidth = 48,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
            var nightModePanel = new Grid
            {
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center
            };
            nightModePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nightModePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            nightModePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(nightModeText, 0);
            Grid.SetColumn(nightModeHost, 2);
            nightModePanel.Children.Add(nightModeText);
            nightModePanel.Children.Add(nightModeHost);

            var statusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusBar = new Border
            {
                Child = statusText,
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(148, 0, 0, 0)),
                Height = CameraTileChromeBarHeight,
                CornerRadius = new CornerRadius(0, 0, CameraTileCornerRadius, CameraTileCornerRadius),
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            var ipText = new TextBlock
            {
                Text = GetCameraDisplayName(camera),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                Height = 22,
                LineHeight = 22,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(ipText, camera.RtspUrl);
            var renameButton = new Button
            {
                Content = CreateCameraToolbarIcon(Symbol.Edit),
                Width = CameraTileControlSize,
                Height = CameraTileControlSize,
                MinWidth = CameraTileControlSize,
                CornerRadius = new CornerRadius(CameraTileControlCornerRadius),
                Padding = new Thickness(0),
                Opacity = 0.86,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(renameButton, "Rename");
            var topBarContent = new Grid
            {
                ColumnSpacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            topBarContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBarContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBarContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBarContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(renameButton, 0);
            Grid.SetColumn(ipText, 1);
            Grid.SetColumn(nightModePanel, 2);
            Grid.SetColumn(closeButton, 3);
            topBarContent.Children.Add(renameButton);
            topBarContent.Children.Add(ipText);
            topBarContent.Children.Add(nightModePanel);
            topBarContent.Children.Add(closeButton);

            var topBar = new Border
            {
                Child = topBarContent,
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(148, 0, 0, 0)),
                Height = CameraTileChromeBarHeight,
                CornerRadius = new CornerRadius(CameraTileCornerRadius, CameraTileCornerRadius, 0, 0),
                Padding = new Thickness(CameraTileChromeInset),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var content = new Grid();
            content.Children.Add(videoView);
            content.Children.Add(blackout);
            content.Children.Add(placeholder);
            content.Children.Add(reconnectButton);
            content.Children.Add(topBar);
            content.Children.Add(statusBar);

            var tile = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
                CornerRadius = new CornerRadius(CameraTileCornerRadius),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = new TranslateTransform(),
                Child = content
            };

            var monitor = new CameraMonitor(camera.RtspUrl, camera.Name, tile, videoView, blackout, placeholder, ipText, statusText, reconnectButton, renameButton, closeButton, nightModeToggle);
            monitor.GridIndex = GetFirstAvailableCameraSlotIndex();
            tile.Tag = monitor;
            tile.PointerPressed += CameraTile_PointerPressed;
            tile.PointerMoved += CameraTile_PointerMoved;
            tile.PointerReleased += CameraTile_PointerReleased;
            tile.PointerCanceled += CameraTile_PointerCanceled;
            tile.PointerCaptureLost += CameraTile_PointerCaptureLost;
            reconnectButton.Tag = monitor;
            reconnectButton.Click += ReconnectCameraButton_Click;
            renameButton.Tag = monitor;
            renameButton.Click += RenameCameraButton_Click;
            closeButton.Tag = monitor;
            closeButton.Click += CloseCameraButton_Click;
            nightModeToggle.Tag = monitor;
            nightModeToggle.Toggled += CameraTileNightModeToggle_Toggled;

            _cameraMonitors[camera.RtspUrl] = monitor;
            _cameraMonitorOrder.Add(monitor);
            RenderCameraGrid();
            StartCameraMonitor(monitor);
            return true;
        }

        private static UIElement CreateCameraToolbarIcon(Symbol symbol)
        {
            return new Viewbox
            {
                Width = CameraTileIconSize,
                Height = CameraTileIconSize,
                Stretch = Stretch.Uniform,
                Child = new SymbolIcon(symbol)
            };
        }

        private static UIElement CreateCameraToolbarGlyph(string glyph)
        {
            return new FontIcon
            {
                Glyph = glyph,
                FontSize = CameraTileIconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private bool RestartExistingCameraMonitor(Amb82CameraInfo camera)
        {
            if (!_cameraMonitors.TryGetValue(camera.RtspUrl, out var monitor))
            {
                return false;
            }

            monitor.UpdateCameraName(camera.Name);
            if (monitor.Cancellation is not null)
            {
                return false;
            }

            monitor.AutoReconnectAttempts = 0;
            StartCameraMonitor(monitor);
            return true;
        }

        private async void RenameCameraButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: CameraMonitor monitor })
            {
                CamerasPage.Focus(FocusState.Programmatic);
                await ShowRenameCameraDialogAsync(monitor);
            }
        }

        private async Task ShowRenameCameraDialogAsync(CameraMonitor monitor)
        {
            var textBox = new TextBox
            {
                Text = monitor.CameraName ?? "",
                PlaceholderText = "AMB82 name"
            };
            var applyRequested = false;
            var dialog = new ContentDialog
            {
                Title = "Rename AMB82",
                Content = textBox,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = RootNavigationView.RequestedTheme,
                XamlRoot = CamerasPage.XamlRoot
            };
            dialog.Opened += (_, _) =>
            {
                textBox.Focus(FocusState.Programmatic);
                textBox.SelectAll();
            };
            textBox.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter)
                {
                    return;
                }

                applyRequested = true;
                e.Handled = true;
                dialog.Hide();
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary && !applyRequested)
            {
                return;
            }

            var name = textBox.Text.Trim();
            await SendCameraRenameAsync(monitor, name);
            monitor.UpdateCameraName(string.IsNullOrWhiteSpace(name) ? null : name);
        }

        private async void CloseCameraButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: CameraMonitor monitor })
            {
                return;
            }

            await RemoveCameraMonitorAsync(monitor);
        }

        private async void ReconnectCameraButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: CameraMonitor monitor } ||
                monitor.Cancellation is not null)
            {
                return;
            }

            monitor.AutoReconnectAttempts = 0;
            StartCameraMonitor(monitor, "Reconnecting...", true);
            await Task.CompletedTask;
        }

        private async void CameraTileNightModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch { Tag: CameraMonitor monitor } toggle ||
                monitor.IsSendingNightMode)
            {
                return;
            }

            monitor.IsSendingNightMode = true;
            toggle.IsEnabled = false;
            try
            {
                monitor.IsNightModeEnabled = toggle.IsOn;
                await SendCameraNightModeAsync(monitor, toggle.IsOn);
            }
            finally
            {
                monitor.IsSendingNightMode = false;
                toggle.IsEnabled = true;
            }
        }

        private async Task RemoveCameraMonitorAsync(CameraMonitor monitor)
        {
            await StopCameraAsync(monitor);
            monitor.Tile.PointerPressed -= CameraTile_PointerPressed;
            monitor.Tile.PointerMoved -= CameraTile_PointerMoved;
            monitor.Tile.PointerReleased -= CameraTile_PointerReleased;
            monitor.Tile.PointerCanceled -= CameraTile_PointerCanceled;
            monitor.Tile.PointerCaptureLost -= CameraTile_PointerCaptureLost;
            monitor.ReconnectButton.Click -= ReconnectCameraButton_Click;
            monitor.RenameButton.Click -= RenameCameraButton_Click;
            monitor.CloseButton.Click -= CloseCameraButton_Click;
            monitor.NightModeToggle.Toggled -= CameraTileNightModeToggle_Toggled;
            var key = _cameraMonitors.FirstOrDefault(item => ReferenceEquals(item.Value, monitor)).Key;
            if (!string.IsNullOrWhiteSpace(key))
            {
                _cameraMonitors.Remove(key);
            }

            if (ReferenceEquals(_fullscreenCameraMonitor, monitor))
            {
                _fullscreenCameraMonitor = null;
            }

            if (ReferenceEquals(_lastClickedCameraMonitor, monitor))
            {
                _lastClickedCameraMonitor = null;
            }

            _cameraMonitorOrder.Remove(monitor);
            RenderCameraGrid();
        }

        private void RenderCameraGrid()
        {
            CameraPreviewGrid.ColumnSpacing = CameraTileSpacing;
            CameraPreviewGrid.RowSpacing = CameraTileSpacing;
            var monitors = _cameraMonitorOrder
                .Where(monitor => _cameraMonitors.ContainsValue(monitor))
                .ToArray();
            if (_fullscreenCameraMonitor is not null &&
                !monitors.Contains(_fullscreenCameraMonitor))
            {
                _fullscreenCameraMonitor = null;
            }

            var activeTiles = monitors
                .Select(monitor => (UIElement)monitor.Tile)
                .ToHashSet();
            for (var childIndex = CameraPreviewGrid.Children.Count - 1; childIndex >= 0; childIndex--)
            {
                var child = CameraPreviewGrid.Children[childIndex];
                if (ReferenceEquals(child, _cameraDropIndicator) ||
                    activeTiles.Contains(child))
                {
                    continue;
                }

                CameraPreviewGrid.Children.RemoveAt(childIndex);
            }

            if (_fullscreenCameraMonitor is not null)
            {
                RenderFullscreenCameraGrid(monitors, _fullscreenCameraMonitor);
                return;
            }

            CameraPreviewGrid.ColumnSpacing = CameraTileSpacing;
            CameraPreviewGrid.RowSpacing = CameraTileSpacing;
            if (CameraPreviewGrid.ColumnDefinitions.Count != 3)
            {
                CameraPreviewGrid.ColumnDefinitions.Clear();
                for (var column = 0; column < 3; column++)
                {
                    CameraPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                }
            }

            var maxSlotIndex = monitors.Length == 0 ? 0 : monitors.Max(monitor => Math.Max(0, monitor.GridIndex));
            var rows = Math.Max(GetVisibleCameraRowCount(), (int)Math.Ceiling((maxSlotIndex + 1) / 3.0));
            var cell = GetCameraCellSize();
            while (CameraPreviewGrid.RowDefinitions.Count < rows)
            {
                CameraPreviewGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = cell.Height > 0 ? new GridLength(cell.Height) : GridLength.Auto
                });
            }

            while (CameraPreviewGrid.RowDefinitions.Count > rows)
            {
                CameraPreviewGrid.RowDefinitions.RemoveAt(CameraPreviewGrid.RowDefinitions.Count - 1);
            }

            foreach (var monitor in monitors)
            {
                var slotIndex = Math.Max(0, monitor.GridIndex);
                monitor.Tile.Visibility = Visibility.Visible;
                monitor.Tile.Width = double.NaN;
                monitor.Tile.MinWidth = 0;
                monitor.Tile.MaxWidth = double.PositiveInfinity;
                monitor.Tile.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRowSpan(monitor.Tile, 1);
                Grid.SetColumnSpan(monitor.Tile, 1);
                Grid.SetRow(monitor.Tile, slotIndex / 3);
                Grid.SetColumn(monitor.Tile, slotIndex % 3);
                Canvas.SetZIndex(monitor.Tile, 0);
                EnsureCameraTileInPreviewGrid(monitor);
            }

            ApplyCameraTileSizing();
        }

        private void RenderFullscreenCameraGrid(IReadOnlyList<CameraMonitor> monitors, CameraMonitor fullscreenMonitor)
        {
            HideCameraDropIndicator();
            CameraPreviewGrid.ColumnSpacing = 0;
            CameraPreviewGrid.RowSpacing = 0;

            CameraPreviewGrid.ColumnDefinitions.Clear();
            CameraPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            CameraPreviewGrid.RowDefinitions.Clear();
            CameraPreviewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            foreach (var monitor in monitors)
            {
                EnsureCameraTileInPreviewGrid(monitor);
                monitor.Tile.Visibility = ReferenceEquals(monitor, fullscreenMonitor)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                Grid.SetRow(monitor.Tile, 0);
                Grid.SetColumn(monitor.Tile, 0);
                Grid.SetRowSpan(monitor.Tile, 1);
                Grid.SetColumnSpan(monitor.Tile, 1);
                Canvas.SetZIndex(monitor.Tile, ReferenceEquals(monitor, fullscreenMonitor) ? 100 : 0);
            }

            ApplyCameraTileSizing();
        }

        private void EnsureCameraTileInPreviewGrid(CameraMonitor monitor)
        {
            if (CameraPreviewGrid.Children.Contains(monitor.Tile))
            {
                return;
            }

            if (monitor.Tile.Parent is Panel parentPanel &&
                !ReferenceEquals(parentPanel, CameraPreviewGrid))
            {
                parentPanel.Children.Remove(monitor.Tile);
            }

            CameraPreviewGrid.Children.Add(monitor.Tile);
        }

        private void ApplyCameraTileSizing()
        {
            if (_fullscreenCameraMonitor is not null)
            {
                ApplyFullscreenCameraTileSizing(_fullscreenCameraMonitor);
                return;
            }

            var cell = GetCameraCellSize();
            if (cell.Width <= 0 || cell.Height <= 0)
            {
                return;
            }

            foreach (var row in CameraPreviewGrid.RowDefinitions)
            {
                row.Height = new GridLength(cell.Height);
            }

            var maxSlotIndex = _cameraMonitorOrder.Count == 0
                ? 0
                : _cameraMonitorOrder.Max(monitor => Math.Max(0, monitor.GridIndex));
            var requiredRows = Math.Max(
                Math.Max(GetVisibleCameraRowCount(), CameraPreviewGrid.RowDefinitions.Count),
                (int)Math.Ceiling((maxSlotIndex + 1) / 3.0));
            while (CameraPreviewGrid.RowDefinitions.Count < requiredRows)
            {
                CameraPreviewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell.Height) });
            }

            foreach (var monitor in _cameraMonitorOrder)
            {
                monitor.Tile.Height = cell.Height;
                monitor.Tile.MinHeight = cell.Height;
                monitor.Tile.MaxHeight = cell.Height;
            }

            if (_cameraDropIndicator is not null)
            {
                _cameraDropIndicator.Height = cell.Height;
                _cameraDropIndicator.MinHeight = cell.Height;
                _cameraDropIndicator.MaxHeight = cell.Height;
            }
        }

        private void ApplyFullscreenCameraTileSizing(CameraMonitor fullscreenMonitor)
        {
            var width = CameraPreviewGrid.ActualWidth;
            var height = CameraPreviewGrid.ActualHeight;
            if (double.IsNaN(width) || width <= 0)
            {
                width = CameraPreviewContainerBorder.ActualWidth
                    - CameraPreviewContainerBorder.Padding.Left
                    - CameraPreviewContainerBorder.Padding.Right;
            }

            if (double.IsNaN(height) || height <= 0)
            {
                height = CameraPreviewContainerBorder.ActualHeight
                    - CameraPreviewContainerBorder.Padding.Top
                    - CameraPreviewContainerBorder.Padding.Bottom;
            }

            fullscreenMonitor.Tile.HorizontalAlignment = HorizontalAlignment.Stretch;
            fullscreenMonitor.Tile.VerticalAlignment = VerticalAlignment.Stretch;
            fullscreenMonitor.Tile.Width = width > 0 ? width : double.NaN;
            fullscreenMonitor.Tile.Height = height > 0 ? height : double.NaN;
            fullscreenMonitor.Tile.MinHeight = 0;
            fullscreenMonitor.Tile.MaxHeight = double.PositiveInfinity;
        }

        private void UpdateCameraPreviewContainerHeight()
        {
            if (CamerasPage.Visibility != Visibility.Visible ||
                RootScrollViewer.ActualHeight <= 0)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (CamerasPage.Visibility != Visibility.Visible ||
                    RootScrollViewer.ActualHeight <= 0)
                {
                    return;
                }

                var height = RootScrollViewer.ActualHeight - PageEdgeInset * 2;
                if (double.IsNaN(height) || height < 160)
                {
                    return;
                }

                CamerasPage.Height = height;
                CameraPreviewContainerBorder.Height = double.NaN;
                CameraPreviewGrid.Height = double.NaN;
                ApplyCameraTileSizing();
            });
        }

        private void UpdateExpandablePageHeights()
        {
            if (RootScrollViewer.ActualHeight <= 0 ||
                (MemoPage.Visibility != Visibility.Visible && LocationPage.Visibility != Visibility.Visible))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                var height = RootScrollViewer.ActualHeight - PageEdgeInset * 2;
                if (double.IsNaN(height) || height < 160)
                {
                    return;
                }

                if (MemoPage.Visibility == Visibility.Visible)
                {
                    MemoPage.Height = height;
                }

                if (LocationPage.Visibility == Visibility.Visible)
                {
                    LocationPage.Height = height;
                }

            });
        }

        private void CameraTile_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border { Tag: CameraMonitor monitor } tile ||
                IsInsideCameraInteractiveControl(e.OriginalSource as DependencyObject))
            {
                return;
            }

            var pointer = e.GetCurrentPoint(CameraPreviewGrid);
            if (!pointer.Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (TryHandleCameraTileDoubleClick(monitor, pointer.Position))
            {
                e.Handled = true;
                return;
            }

            if (_fullscreenCameraMonitor is not null)
            {
                e.Handled = true;
                return;
            }

            _draggingCameraMonitor = monitor;
            _cameraDragStartPoint = pointer.Position;
            _cameraDragSourceIndex = Math.Max(0, monitor.GridIndex);
            _cameraDragTargetIndex = _cameraDragSourceIndex;
            _isCameraTileDragging = false;
            tile.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void CameraTile_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingCameraMonitor is null ||
                sender is not Border tile)
            {
                return;
            }

            var pointer = e.GetCurrentPoint(CameraPreviewGrid);
            if (!pointer.Properties.IsLeftButtonPressed)
            {
                return;
            }

            var point = pointer.Position;
            var deltaX = point.X - _cameraDragStartPoint.X;
            var deltaY = point.Y - _cameraDragStartPoint.Y;
            if (!_isCameraTileDragging &&
                Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < 6)
            {
                return;
            }

            if (!_isCameraTileDragging)
            {
                _isCameraTileDragging = true;
                _lastClickedCameraMonitor = null;
                tile.Opacity = 0.94;
                Canvas.SetZIndex(tile, 1000);
                ShowCameraDropIndicator(_cameraDragTargetIndex);
            }

            var transform = GetCameraTileTransform(_draggingCameraMonitor);
            transform.X = deltaX;
            transform.Y = deltaY;

            var targetIndex = GetCameraDropTargetIndex(point);
            if (targetIndex != _cameraDragTargetIndex)
            {
                _cameraDragTargetIndex = targetIndex;
                ShowCameraDropIndicator(targetIndex);
            }

            e.Handled = true;
        }

        private async void CameraTile_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingCameraMonitor is null ||
                sender is not Border tile)
            {
                return;
            }

            var monitor = _draggingCameraMonitor;
            var targetIndex = _cameraDragTargetIndex;
            var shouldReorder = _isCameraTileDragging;
            _isCompletingCameraTileDrag = true;
            tile.ReleasePointerCapture(e.Pointer);
            if (shouldReorder)
            {
                await CompleteCameraTileDragAsync(monitor, targetIndex, true);
            }
            else
            {
                ResetCameraTileDrag(monitor);
            }

            _isCompletingCameraTileDrag = false;
            e.Handled = true;
        }

        private async void CameraTile_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingCameraMonitor is null)
            {
                return;
            }

            var monitor = _draggingCameraMonitor;
            _isCompletingCameraTileDrag = true;
            await CompleteCameraTileDragAsync(monitor, _cameraDragSourceIndex, false);
            _isCompletingCameraTileDrag = false;
        }

        private async void CameraTile_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isCompletingCameraTileDrag ||
                _draggingCameraMonitor is null)
            {
                return;
            }

            var monitor = _draggingCameraMonitor;
            _isCompletingCameraTileDrag = true;
            await CompleteCameraTileDragAsync(monitor, _cameraDragSourceIndex, false);
            _isCompletingCameraTileDrag = false;
        }

        private async Task CompleteCameraTileDragAsync(CameraMonitor monitor, int targetIndex, bool reorder)
        {
            var sourceIndex = _cameraDragSourceIndex >= 0
                ? _cameraDragSourceIndex
                : _cameraMonitorOrder.IndexOf(monitor);
            if (sourceIndex < 0)
            {
                ResetCameraTileDrag(monitor);
                return;
            }

            targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, GetAvailableCameraSlotCount() - 1));
            if (!reorder)
            {
                targetIndex = sourceIndex;
            }

            var transform = GetCameraTileTransform(monitor);
            var sourceBounds = GetCameraSlotBounds(sourceIndex);
            var targetBounds = GetCameraSlotBounds(targetIndex);
            await AnimateCameraTileToAsync(
                transform,
                targetBounds.X - sourceBounds.X,
                targetBounds.Y - sourceBounds.Y);

            CameraMonitor? swappedMonitor = null;
            if (reorder && sourceIndex != targetIndex)
            {
                swappedMonitor = MoveCameraMonitorToSlot(monitor, targetIndex);
            }

            ResetCameraTileDrag(monitor);
            RenderCameraGrid();
        }

        private CameraMonitor? MoveCameraMonitorToSlot(CameraMonitor monitor, int targetIndex)
        {
            var currentIndex = Math.Max(0, monitor.GridIndex);
            var otherMonitor = _cameraMonitorOrder.FirstOrDefault(item =>
                !ReferenceEquals(item, monitor) &&
                item.GridIndex == targetIndex);
            if (otherMonitor is not null)
            {
                otherMonitor.GridIndex = currentIndex;
            }

            monitor.GridIndex = targetIndex;
            return otherMonitor;
        }

        private Task AnimateCameraTileToAsync(TranslateTransform transform, double x, double y)
        {
            var completion = new TaskCompletionSource();
            var duration = new Duration(TimeSpan.FromMilliseconds(200));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var storyboard = new Storyboard();

            var xAnimation = new DoubleAnimation
            {
                To = x,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(xAnimation, transform);
            Storyboard.SetTargetProperty(xAnimation, nameof(TranslateTransform.X));

            var yAnimation = new DoubleAnimation
            {
                To = y,
                Duration = duration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(yAnimation, transform);
            Storyboard.SetTargetProperty(yAnimation, nameof(TranslateTransform.Y));

            storyboard.Children.Add(xAnimation);
            storyboard.Children.Add(yAnimation);
            storyboard.Completed += (_, _) => completion.TrySetResult();
            storyboard.Begin();
            return completion.Task;
        }

        private void ResetCameraTileDrag(CameraMonitor monitor)
        {
            var transform = GetCameraTileTransform(monitor);
            transform.X = 0;
            transform.Y = 0;
            monitor.Tile.Opacity = 1;
            Canvas.SetZIndex(monitor.Tile, 0);
            HideCameraDropIndicator();
            _draggingCameraMonitor = null;
            _cameraDragSourceIndex = -1;
            _cameraDragTargetIndex = -1;
            _isCameraTileDragging = false;
        }

        private bool TryHandleCameraTileDoubleClick(CameraMonitor monitor, Point point)
        {
            var now = DateTimeOffset.Now;
            var elapsed = now - _lastCameraTileClickTime;
            var deltaX = point.X - _lastCameraTileClickPoint.X;
            var deltaY = point.Y - _lastCameraTileClickPoint.Y;
            var isDoubleClick =
                ReferenceEquals(_lastClickedCameraMonitor, monitor) &&
                elapsed <= CameraTileDoubleClickThreshold &&
                Math.Sqrt(deltaX * deltaX + deltaY * deltaY) <= CameraTileDoubleClickMaxDistance;

            _lastClickedCameraMonitor = monitor;
            _lastCameraTileClickTime = now;
            _lastCameraTileClickPoint = point;

            if (!isDoubleClick)
            {
                return false;
            }

            _lastClickedCameraMonitor = null;
            ToggleCameraTileFullscreen(monitor);
            return true;
        }

        private void ToggleCameraTileFullscreen(CameraMonitor monitor)
        {
            if (!ReferenceEquals(_fullscreenCameraMonitor, monitor))
            {
                _fullscreenCameraMonitor = monitor;
            }
            else
            {
                _fullscreenCameraMonitor = null;
            }

            HideCameraDropIndicator();
            _draggingCameraMonitor = null;
            _cameraDragSourceIndex = -1;
            _cameraDragTargetIndex = -1;
            _isCameraTileDragging = false;
            RenderCameraGrid();
        }

        private TranslateTransform GetCameraTileTransform(CameraMonitor monitor)
        {
            if (monitor.Tile.RenderTransform is TranslateTransform transform)
            {
                return transform;
            }

            transform = new TranslateTransform();
            monitor.Tile.RenderTransform = transform;
            return transform;
        }

        private int GetCameraDropTargetIndex(Point point)
        {
            var slotCount = GetAvailableCameraSlotCount();
            if (slotCount <= 1)
            {
                return 0;
            }

            var cell = GetCameraCellSize();
            var columnStride = cell.Width + CameraPreviewGrid.ColumnSpacing;
            var rowStride = cell.Height + CameraPreviewGrid.RowSpacing;
            var column = columnStride <= 0 ? 0 : (int)Math.Floor(point.X / columnStride);
            var row = rowStride <= 0 ? 0 : (int)Math.Floor(point.Y / rowStride);
            column = Math.Clamp(column, 0, 2);
            row = Math.Clamp(row, 0, Math.Max(0, (slotCount - 1) / 3));
            return Math.Clamp(row * 3 + column, 0, slotCount - 1);
        }

        private (double X, double Y, double Width, double Height) GetCameraSlotBounds(int index)
        {
            var cell = GetCameraCellSize();
            var row = Math.Max(0, index) / 3;
            var column = Math.Max(0, index) % 3;
            return (
                column * (cell.Width + CameraPreviewGrid.ColumnSpacing),
                row * (cell.Height + CameraPreviewGrid.RowSpacing),
                cell.Width,
                cell.Height);
        }

        private (double Width, double Height) GetCameraCellSize()
        {
            var width = (CameraPreviewGrid.ActualWidth - CameraPreviewGrid.ColumnSpacing * 2) / 3;
            if (double.IsNaN(width) || width <= 0)
            {
                width = _draggingCameraMonitor?.Tile.ActualWidth ?? 0;
            }

            var height = width * 9 / 16;
            return (Math.Max(0, width), Math.Max(0, height));
        }

        private int GetAvailableCameraSlotCount()
        {
            var visibleSlots = GetVisibleCameraRowCount() * 3;
            var highestUsedSlot = _cameraMonitorOrder.Count == 0
                ? 0
                : _cameraMonitorOrder.Max(monitor => Math.Max(0, monitor.GridIndex)) + 1;
            return Math.Max(1, Math.Max(visibleSlots, highestUsedSlot));
        }

        private int GetVisibleCameraRowCount()
        {
            var cell = GetCameraCellSize();
            if (cell.Height <= 0)
            {
                return 1;
            }

            var availableHeight = CameraPreviewGrid.Height > 0
                ? CameraPreviewGrid.Height
                : CameraPreviewGrid.ActualHeight;
            if (double.IsNaN(availableHeight) || availableHeight <= 0)
            {
                return Math.Max(1, (int)Math.Ceiling(Math.Max(1, _cameraMonitorOrder.Count) / 3.0));
            }

            var rowStride = cell.Height + CameraPreviewGrid.RowSpacing;
            return Math.Max(1, (int)Math.Floor((availableHeight + CameraPreviewGrid.RowSpacing) / rowStride));
        }

        private int GetFirstAvailableCameraSlotIndex()
        {
            var usedSlots = _cameraMonitorOrder
                .Select(monitor => Math.Max(0, monitor.GridIndex))
                .ToHashSet();
            var slotCount = Math.Max(GetAvailableCameraSlotCount(), _cameraMonitorOrder.Count + 1);
            for (var index = 0; index < slotCount; index++)
            {
                if (!usedSlots.Contains(index))
                {
                    return index;
                }
            }

            return slotCount;
        }

        private void ShowCameraDropIndicator(int targetIndex)
        {
            if (targetIndex < 0)
            {
                return;
            }

            _cameraDropIndicator ??= new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(CameraTileCornerRadius),
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                IsHitTestVisible = false
            };

            if (_cameraDropIndicator.Parent is null)
            {
                CameraPreviewGrid.Children.Add(_cameraDropIndicator);
            }

            var cell = GetCameraCellSize();
            while (CameraPreviewGrid.RowDefinitions.Count <= targetIndex / 3)
            {
                CameraPreviewGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = cell.Height > 0 ? new GridLength(cell.Height) : GridLength.Auto
                });
            }

            Grid.SetRow(_cameraDropIndicator, targetIndex / 3);
            Grid.SetColumn(_cameraDropIndicator, targetIndex % 3);
            _cameraDropIndicator.Margin = new Thickness(0);
            ApplyCameraTileSizing();
            Canvas.SetZIndex(_cameraDropIndicator, 900);
        }

        private void HideCameraDropIndicator()
        {
            if (_cameraDropIndicator is not null)
            {
                CameraPreviewGrid.Children.Remove(_cameraDropIndicator);
            }
        }

        private static bool IsInsideCameraInteractiveControl(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is Button or ToggleSwitch or TextBox or ComboBox)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void StartCameraMonitor(CameraMonitor monitor, string status = "Connecting...", bool isReconnect = false)
        {
            monitor.Cancellation = new CancellationTokenSource();
            var cancellationToken = monitor.Cancellation.Token;
            monitor.FrameGeneration++;
            var frameGeneration = monitor.FrameGeneration;
            monitor.IsHandlingStreamLoss = false;
            monitor.ReconnectDeadline = isReconnect
                ? DateTimeOffset.Now + TimeSpan.FromSeconds(CameraReconnectSeconds)
                : null;
            monitor.HasReceivedFrame = false;
            monitor.ReconnectButton.Visibility = Visibility.Collapsed;
            monitor.Placeholder.Visibility = Visibility.Collapsed;
            if (!isReconnect)
            {
                monitor.Blackout.Visibility = Visibility.Visible;
            }

            SetCameraTileStatus(monitor, isReconnect ? $"Reconnecting... {CameraReconnectSeconds}s" : status);
            StartCameraReconnectCountdown(monitor, frameGeneration);

            var stream = new CameraStream();
            monitor.Stream = stream;
            monitor.PreviewBitmap ??= new WriteableBitmap(CameraStream.Width, CameraStream.Height);
            monitor.VideoView.Source = monitor.PreviewBitmap;
            var frameTimer = DispatcherQueue.CreateTimer();
            frameTimer.Interval = TimeSpan.FromMilliseconds(33);
            frameTimer.Tick += (_, _) => RenderCameraFrame(monitor, stream, frameGeneration);
            monitor.FrameTimer = frameTimer;
            frameTimer.Start();
            monitor.Task = Task.Run(() => RunCameraMonitorAsync(monitor, stream, frameGeneration, cancellationToken));
        }

        private void StartCameraReconnectCountdown(CameraMonitor monitor, long frameGeneration)
        {
            monitor.ReconnectCountdownTimer?.Stop();
            if (monitor.ReconnectDeadline is null)
            {
                return;
            }

            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (_, _) =>
            {
                if (monitor.FrameGeneration != frameGeneration ||
                    monitor.Cancellation is null ||
                    monitor.ReconnectDeadline is null)
                {
                    timer.Stop();
                    return;
                }

                var remainingSeconds = GetRemainingSeconds(monitor.ReconnectDeadline.Value);
                if (remainingSeconds <= 0)
                {
                    timer.Stop();
                    return;
                }

                SetCameraTileStatus(monitor, $"Reconnecting... {remainingSeconds}s");
            };

            monitor.ReconnectCountdownTimer = timer;
            SetCameraTileStatus(monitor, $"Reconnecting... {GetRemainingSeconds(monitor.ReconnectDeadline.Value)}s");
            timer.Start();
        }

        private async Task StopAllCamerasAsync()
        {
            var monitors = _cameraMonitors.Values.ToArray();
            foreach (var monitor in monitors)
            {
                await StopCameraAsync(monitor);
            }
        }

        private async Task StopCameraAsync(CameraMonitor monitor)
        {
            var cancellation = monitor.Cancellation;
            var task = monitor.Task;
            monitor.FrameGeneration++;
            monitor.Cancellation = null;
            monitor.Task = null;
            cancellation?.Cancel();
            monitor.ReconnectCountdownTimer?.Stop();
            await RunOnUiThreadAsync(() =>
            {
                StopCameraPlayback(monitor);
                return Task.CompletedTask;
            });

            try
            {
                if (task is not null) await task;
            }
            catch (OperationCanceledException) { }
            finally
            {
                cancellation?.Dispose();
            }
        }

        private async Task RunCameraMonitorAsync(CameraMonitor monitor, CameraStream stream, long frameGeneration, CancellationToken cancellationToken)
        {
            try
            {
                if (monitor.ReconnectDeadline is not null)
                {
                    var discoveredCameras = await ScanForAmb82RtspAsync(TimeSpan.FromSeconds(2), monitor.Host);
                    var discoveredCamera = discoveredCameras.FirstOrDefault();
                    if (discoveredCamera is not null)
                    {
                        await UpdateCameraInfoAsync(monitor, discoveredCamera, frameGeneration);
                    }
                }

                await SetCameraTileStatusAsync(
                    monitor,
                    monitor.ReconnectDeadline is null ? "Connecting..." : $"Reconnecting... {CameraReconnectSeconds}s",
                    frameGeneration);

                await stream.RunAsync(monitor.RtspUrl, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) return;
                Debug.WriteLine($"AMB82 {monitor.Host}: {ex.Message}");
                if (monitor.HasReceivedFrame)
                {
                    await HandleCameraStreamLostAsync(monitor, frameGeneration);
                    return;
                }

                await MarkCameraDisconnectedAsync(monitor, "Disconnected", frameGeneration);
            }
        }

        private void RenderCameraFrame(CameraMonitor monitor, CameraStream stream, long frameGeneration)
        {
            if (monitor.FrameGeneration != frameGeneration) return;
            var frame = stream.TakeLatestFrame();
            if (frame is null) return;
            try
            {
                // Copy only the newest frame, never enqueue video work on the UI.
                using var pixels = monitor.PreviewBitmap!.PixelBuffer.AsStream();
                pixels.Write(frame, 0, CameraStream.FrameBytes);
                monitor.PreviewBitmap.Invalidate();
                if (!monitor.HasReceivedFrame)
                {
                    monitor.HasReceivedFrame = true;
                    monitor.AutoReconnectAttempts = 0;
                    monitor.ReconnectDeadline = null;
                    monitor.ReconnectCountdownTimer?.Stop();
                    SetCameraTileStatus(monitor, "Connected");
                    monitor.Blackout.Visibility = Visibility.Collapsed;
                    monitor.Placeholder.Visibility = Visibility.Collapsed;
                    monitor.ReconnectButton.Visibility = Visibility.Collapsed;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame);
            }
        }

        private Task SetCameraTileStatusAsync(CameraMonitor monitor, string message)
        {
            return RunOnUiThreadAsync(() =>
            {
                SetCameraTileStatus(monitor, message);
                return Task.CompletedTask;
            });
        }

        private Task SetCameraTileStatusAsync(CameraMonitor monitor, string message, long frameGeneration)
        {
            return RunOnUiThreadAsync(() =>
            {
                if (monitor.FrameGeneration == frameGeneration)
                {
                    SetCameraTileStatus(monitor, message);
                }

                return Task.CompletedTask;
            });
        }

        private static void SetCameraTileStatus(CameraMonitor monitor, string message)
        {
            monitor.StatusText.Text = message;
        }

        private Task UpdateCameraInfoAsync(CameraMonitor monitor, Amb82CameraInfo camera, long frameGeneration)
        {
            return RunOnUiThreadAsync(() =>
            {
                if (monitor.FrameGeneration != frameGeneration)
                {
                    return Task.CompletedTask;
                }

                if (!string.Equals(monitor.RtspUrl, camera.RtspUrl, StringComparison.OrdinalIgnoreCase))
                {
                    _cameraMonitors.Remove(monitor.RtspUrl);
                    monitor.UpdateRtspUrl(camera.RtspUrl);
                    _cameraMonitors[camera.RtspUrl] = monitor;
                }

                monitor.UpdateCameraName(camera.Name);
                return Task.CompletedTask;
            });
        }

        private static string GetCameraDisplayName(Amb82CameraInfo camera)
        {
            if (!string.IsNullOrWhiteSpace(camera.Name))
            {
                return camera.Name.Trim();
            }

            return GetHostFromRtspUrl(camera.RtspUrl);
        }

        private static int GetRemainingSeconds(DateTimeOffset deadline)
        {
            return Math.Max(0, (int)Math.Ceiling((deadline - DateTimeOffset.Now).TotalSeconds));
        }

        private static void StopCameraPlayback(CameraMonitor monitor)
        {
            monitor.FrameTimer?.Stop();
            monitor.FrameTimer = null;
            monitor.Stream?.Dispose();
            monitor.Stream = null;
        }

        private Task HandleCameraStreamLostAsync(CameraMonitor monitor, long frameGeneration)
        {
            return RunOnUiThreadAsync(() =>
            {
                if (monitor.FrameGeneration != frameGeneration)
                {
                    return Task.CompletedTask;
                }

                if (!monitor.HasReceivedFrame && monitor.ReconnectDeadline is not null)
                {
                    return Task.CompletedTask;
                }

                if (monitor.IsHandlingStreamLoss)
                {
                    return Task.CompletedTask;
                }

                monitor.IsHandlingStreamLoss = true;
                monitor.Placeholder.Visibility = Visibility.Collapsed;
                monitor.ReconnectCountdownTimer?.Stop();

                if (monitor.AutoReconnectAttempts == 0)
                {
                    monitor.ReconnectButton.Visibility = Visibility.Collapsed;
                    monitor.AutoReconnectAttempts = 1;
                    monitor.StatusText.Text = $"Reconnecting... {CameraReconnectSeconds}s";
                    monitor.ReconnectDeadline = DateTimeOffset.Now + TimeSpan.FromSeconds(CameraReconnectSeconds);
                    var cancellation = monitor.Cancellation;
                    monitor.Cancellation = null;
                    cancellation?.Cancel();
                    var task = monitor.Task;
                    monitor.Task = null;
                    StopCameraPlayback(monitor);
                    _ = RestartCameraAfterStreamLossAsync(monitor, task, cancellation, frameGeneration);
                    return Task.CompletedTask;
                }

                StopCameraPlayback(monitor);
                monitor.StatusText.Text = "Disconnected";
                monitor.FrameGeneration++;
                monitor.ReconnectButton.Visibility = Visibility.Visible;
                monitor.Blackout.Visibility = Visibility.Visible;
                monitor.Cancellation?.Cancel();
                monitor.Cancellation?.Dispose();
                monitor.Cancellation = null;
                monitor.Task = null;
                return Task.CompletedTask;
            });
        }

        private Task MarkCameraDisconnectedAsync(CameraMonitor monitor, string message, long frameGeneration)
        {
            return RunOnUiThreadAsync(() =>
            {
                if (monitor.FrameGeneration != frameGeneration)
                {
                    return Task.CompletedTask;
                }

                monitor.FrameGeneration++;
                monitor.ReconnectCountdownTimer?.Stop();
                monitor.ReconnectDeadline = null;
                StopCameraPlayback(monitor);
                monitor.StatusText.Text = message;
                monitor.Placeholder.Visibility = Visibility.Collapsed;
                monitor.ReconnectButton.Visibility = Visibility.Visible;
                monitor.Blackout.Visibility = Visibility.Visible;
                monitor.Cancellation?.Cancel();
                monitor.Cancellation?.Dispose();
                monitor.Cancellation = null;
                monitor.Task = null;
                return Task.CompletedTask;
            });
        }

        private async Task RestartCameraAfterStreamLossAsync(
            CameraMonitor monitor,
            Task? task,
            CancellationTokenSource? cancellation,
            long frameGeneration)
        {
            try
            {
                if (task is not null)
                {
                    await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
                }
            }
            catch
            {
            }
            finally
            {
                cancellation?.Dispose();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));

            DispatcherQueue.TryEnqueue(() =>
            {
                if (monitor.FrameGeneration != frameGeneration)
                {
                    return;
                }

                monitor.Task = null;
                StartCameraMonitor(monitor, "Reconnecting...", true);
            });
        }

        private void SetCameraStatus(string message)
        {
        }

        private static async Task SendCameraNightModeAsync(CameraMonitor monitor, bool enabled)
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var endpoints = new List<IPEndPoint>();
            if (IPAddress.TryParse(monitor.Host, out var address))
            {
                endpoints.Add(new IPEndPoint(address, Amb82DiscoveryPort));
            }

            var payload = $"amb night={(enabled ? 1 : 0)}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            foreach (var endpoint in endpoints)
            {
                await udp.SendAsync(bytes, bytes.Length, endpoint);
            }
        }

        private static async Task SendCameraRenameAsync(CameraMonitor monitor, string name)
        {
            if (!IPAddress.TryParse(monitor.Host, out var address))
            {
                return;
            }

            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var payload = $"amb rename={Uri.EscapeDataString(name)}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(address, Amb82DiscoveryPort));
        }

        private static string GetHostFromRtspUrl(string rtspUrl)
        {
            return TryGetHostFromRtspUrl(rtspUrl, out var host) ? host : rtspUrl;
        }

        private static bool TryGetHostFromRtspUrl(string rtspUrl, out string host)
        {
            host = "";
            if (!Uri.TryCreate(rtspUrl, UriKind.Absolute, out var uri) ||
                !uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                return false;
            }

            host = uri.Host;
            return true;
        }

        private static async Task<IReadOnlyList<Amb82CameraInfo>> ScanForAmb82RtspAsync(
            TimeSpan timeout,
            string? preferredHost = null,
            Func<Amb82CameraInfo, Task>? onCameraFound = null)
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var targets = GetAmb82DiscoveryTargets().ToArray();
            if (targets.Length == 0)
            {
                targets = [new Amb82DiscoveryTarget(IPAddress.Parse("255.255.255.255"), "0.0.0.0")];
            }

            foreach (var target in targets)
            {
                var payload = $"who is AMB82 locallink_ip={target.LocalIp} unity_ip={target.LocalIp}";
                var bytes = Encoding.UTF8.GetBytes(payload);
                await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(target.BroadcastAddress, Amb82DiscoveryPort));
            }

            var cameras = new List<Amb82CameraInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deadline = DateTimeOffset.Now + timeout;
            DateTimeOffset? quietDeadline = null;
            while (DateTimeOffset.Now < deadline)
            {
                var receiveTask = udp.ReceiveAsync();
                var effectiveDeadline = quietDeadline is null
                    ? deadline
                    : quietDeadline.Value < deadline
                        ? quietDeadline.Value
                        : deadline;
                var remaining = effectiveDeadline - DateTimeOffset.Now;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var completed = await Task.WhenAny(receiveTask, Task.Delay(remaining));
                if (completed != receiveTask)
                {
                    break;
                }

                var result = await receiveTask;
                var text = Encoding.UTF8.GetString(result.Buffer).Trim();
                var rtspUrl = ExtractRtspUrl(text);
                if (!string.IsNullOrWhiteSpace(rtspUrl) &&
                    (string.IsNullOrWhiteSpace(preferredHost) ||
                        (TryGetHostFromRtspUrl(rtspUrl, out var host) &&
                            string.Equals(host, preferredHost, StringComparison.OrdinalIgnoreCase))) &&
                    seen.Add(rtspUrl))
                {
                    var camera = new Amb82CameraInfo(rtspUrl, ExtractCameraName(text));
                    cameras.Add(camera);
                    quietDeadline = DateTimeOffset.Now + Amb82DiscoveryQuietPeriod;
                    if (onCameraFound is not null)
                    {
                        await onCameraFound(camera);
                    }

                    if (!string.IsNullOrWhiteSpace(preferredHost))
                    {
                        break;
                    }
                }
            }

            return cameras;
        }

        private static string? ExtractRtspUrl(string text)
        {
            const string key = "rtsp=";
            var start = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start += key.Length;
            }
            else
            {
                start = text.IndexOf("rtsp://", StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    return null;
                }
            }

            var end = text.IndexOf(' ', start);
            var url = end >= 0 ? text[start..end] : text[start..];
            return url.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ? url : null;
        }

        private static string? ExtractCameraName(string text)
        {
            var value = ExtractTokenValue(text, "name=");
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                return Uri.UnescapeDataString(value).Trim();
            }
            catch (UriFormatException)
            {
                return value.Trim();
            }
        }

        private static string? ExtractTokenValue(string text, string key)
        {
            var start = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += key.Length;
            var end = text.IndexOf(' ', start);
            return end >= 0 ? text[start..end] : text[start..];
        }

        private static IEnumerable<Amb82DiscoveryTarget> GetAmb82DiscoveryTargets()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            yield return new Amb82DiscoveryTarget(IPAddress.Parse("255.255.255.255"), "0.0.0.0");

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = networkInterface.GetIPProperties();
                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(address.Address) ||
                        address.IPv4Mask is null)
                    {
                        continue;
                    }

                    var broadcastAddress = GetBroadcastAddress(address.Address, address.IPv4Mask);
                    var key = $"{broadcastAddress}|{address.Address}";
                    if (seen.Add(key))
                    {
                        yield return new Amb82DiscoveryTarget(broadcastAddress, address.Address.ToString());
                    }
                }
            }
        }

        private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress mask)
        {
            var addressBytes = address.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            var broadcastBytes = new byte[addressBytes.Length];

            for (var index = 0; index < addressBytes.Length; index++)
            {
                broadcastBytes[index] = (byte)(addressBytes[index] | ~maskBytes[index]);
            }

            return new IPAddress(broadcastBytes);
        }

        private Task RunOnUiThreadAsync(Func<Task> action)
        {
            var completion = new TaskCompletionSource();
            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
            {
                completion.TrySetResult();
            }

            return completion.Task;
        }

        private void InitializeMemoCalendar()
        {
            var now = DateTimeOffset.Now;
            _selectedMemoYear = now.Year;
            _selectedMemoMonth = now.Month;
            _highlightedMemoMonth = now.Month;

            var monthItems = GetMemoMonthNavigationItems();
            for (var index = 0; index < monthItems.Length; index++)
            {
                monthItems[index].Content = CreateMemoMonthButtonContent(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(index + 1));
                monthItems[index].PointerEntered += MemoMonthNavigationItem_PointerEntered;
                monthItems[index].PointerExited += MemoMonthNavigationItem_PointerExited;
                monthItems[index].PointerPressed += MemoMonthNavigationItem_PointerPressed;
                monthItems[index].SizeChanged += MemoMonthNavigationItem_SizeChanged;
            }

            RenderMemoCalendar();
        }

        private void StartMemoDateTimer()
        {
            _memoDateTimer?.Stop();
            _memoDateTimer = DispatcherQueue.CreateTimer();
            _memoDateTimer.Interval = TimeSpan.FromMinutes(1);
            _memoDateTimer.Tick += async (_, _) =>
            {
                var previousDate = _currentMemoDate;
                RefreshMemoDateIfNeeded();
                if (_currentMemoDate != previousDate)
                {
                    RemoveExpiredDailyForecast();
                    RenderDailyForecast();
                    await RefreshWeatherAsync();
                }
                if (LocationPage.Visibility == Visibility.Visible)
                {
                    RenderHourlyForecast();
                }
            };
            _memoDateTimer.Start();
        }

        private void RefreshMemoDateIfNeeded()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (today == _currentMemoDate)
            {
                return;
            }

            _currentMemoDate = today;
            _selectedMemoYear = today.Year;
            SelectMemoMonth(today.Month, true);
        }

        private void UpdateMemoMonthHighlights()
        {
            var currentMonth = DateTimeOffset.Now.Month;
            var defaultTextBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            var transparentBrush = GetTransparentBrush();
            var items = GetMemoMonthNavigationItems();
            for (var index = 0; index < items.Length; index++)
            {
                var month = index + 1;
                var isCurrentMonth = month == currentMonth;
                var isHighlightedMonth = month == _highlightedMemoMonth;
                var isPointerHoverMonth = month == _pointerHoverMemoMonth;
                var item = items[index];
                item.MinHeight = 0;
                item.ClearValue(FrameworkElement.HeightProperty);
                item.Margin = new Thickness(0);
                item.HorizontalAlignment = HorizontalAlignment.Stretch;
                item.VerticalAlignment = VerticalAlignment.Stretch;
                item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                item.VerticalContentAlignment = VerticalAlignment.Stretch;
                item.Padding = new Thickness(0);
                item.BorderThickness = new Thickness(0);
                item.BorderBrush = transparentBrush;
                item.Background = transparentBrush;
                item.Resources["ButtonBorderBrush"] = transparentBrush;
                item.Resources["ButtonBorderBrushPointerOver"] = transparentBrush;
                item.Resources["ButtonBorderBrushPressed"] = transparentBrush;
                item.Resources["ButtonBorderBrushDisabled"] = transparentBrush;
                item.Resources["ButtonBackground"] = transparentBrush;
                item.Resources["ButtonBackgroundPointerOver"] = transparentBrush;
                item.Resources["ButtonBackgroundPressed"] = transparentBrush;
                item.Foreground = isCurrentMonth ? GetMemoMonthBrush(month) : defaultTextBrush;
                item.FontWeight = isCurrentMonth
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal;
                StretchMemoMonthButtonContent(item);
                SetMemoMonthButtonBackground(item, isPointerHoverMonth || isHighlightedMonth
                    ? GetMemoMonthNavigationHoverBrush()
                    : transparentBrush);

                if (GetMemoMonthButtonText(item) is { } textBlock)
                {
                    textBlock.Foreground = item.Foreground;
                    textBlock.FontWeight = item.FontWeight;
                }

                if (GetMemoMonthButtonIndicator(item) is { } indicator)
                {
                    indicator.Background = isCurrentMonth ? GetMemoMonthBrush(month) : transparentBrush;
                }
            }
        }

        private void MemoMonthNavigationView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMemoMonthHighlights();
        }

        private void MemoMonthNavigationView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMemoMonthHighlights();
        }

        private void DisableMemoMonthNavigationScrollBars()
        {
            foreach (var scrollViewer in FindVisualChildren<ScrollViewer>(MemoMonthNavigationView))
            {
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scrollViewer.VerticalScrollMode = ScrollMode.Disabled;
                scrollViewer.IsVerticalRailEnabled = false;
                scrollViewer.IsHorizontalRailEnabled = false;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (var nestedChild in FindVisualChildren<T>(child))
                {
                    yield return nestedChild;
                }
            }
        }

        private static SolidColorBrush GetTransparentBrush()
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        private Button[] GetMemoMonthNavigationItems()
        {
            return
            [
                MemoMonthNavItem1,
                MemoMonthNavItem2,
                MemoMonthNavItem3,
                MemoMonthNavItem4,
                MemoMonthNavItem5,
                MemoMonthNavItem6,
                MemoMonthNavItem7,
                MemoMonthNavItem8,
                MemoMonthNavItem9,
                MemoMonthNavItem10,
                MemoMonthNavItem11,
                MemoMonthNavItem12
            ];
        }

        private static Grid CreateMemoMonthButtonContent(string monthName)
        {
            var grid = new Grid
            {
                ColumnSpacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var background = new Border
            {
                Tag = "background",
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var contentGrid = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(8, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var indicator = new Border
            {
                Tag = "indicator",
                Width = 4,
                Height = 20,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBlock = new TextBlock
            {
                Text = monthName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(indicator, 0);
            Grid.SetColumn(textBlock, 1);
            contentGrid.Children.Add(indicator);
            contentGrid.Children.Add(textBlock);
            grid.Children.Add(background);
            grid.Children.Add(contentGrid);
            return grid;
        }

        private static TextBlock? GetMemoMonthButtonText(Button item)
        {
            return item.Content is Grid grid
                ? FindVisualChildren<TextBlock>(grid).FirstOrDefault()
                : null;
        }

        private static Border? GetMemoMonthButtonIndicator(Button item)
        {
            return item.Content is Grid grid
                ? FindVisualChildren<Border>(grid).FirstOrDefault(border => (border.Tag as string) == "indicator")
                : null;
        }

        private static Border? GetMemoMonthButtonBackground(Button item)
        {
            return item.Content is Grid grid
                ? FindVisualChildren<Border>(grid).FirstOrDefault(border => (border.Tag as string) == "background")
                : null;
        }

        private void MemoMonthNavigationItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var month))
            {
                SelectMemoMonth(month, true);
            }
        }

        private void MemoMonthNavigationItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (GetMemoMonthFromElement(sender) is not { } month)
            {
                return;
            }

            SelectMemoMonth(month, true);
        }

        private void MemoMonthNavigationItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button item)
            {
                _pointerHoverMemoMonth = GetMemoMonthFromElement(item);
                UpdateMemoMonthHighlights();
            }
        }

        private void MemoMonthNavigationItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button item)
            {
                var month = GetMemoMonthFromElement(item);
                if (_pointerHoverMemoMonth == month)
                {
                    _pointerHoverMemoMonth = null;
                }

                UpdateMemoMonthHighlights();
            }
        }

        private void MemoMonthNavigationItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Button item)
            {
                StretchMemoMonthButtonContent(item);
            }
        }

        private void ScheduleMemoMonthFromElement(object sender)
        {
            if (GetMemoMonthFromElement(sender) is { } month)
            {
                ScheduleMemoHoverMonthSelection(month);
            }
        }

        private static int? GetMemoMonthFromElement(object? sender)
        {
            return sender is FrameworkElement { Tag: string value } && int.TryParse(value, out var month)
                ? Math.Clamp(month, 1, 12)
                : null;
        }

        private void ClearDragHoverMemoMonth()
        {
            _memoMonthHoverTimer?.Stop();
            _pendingMemoHoverMonth = null;
            _dragHoverMemoMonth = null;
            _pointerHoverMemoMonth = null;
            ClearMemoMonthDragHoverVisuals();
        }

        private void ScheduleMemoHoverMonthSelection(int month)
        {
            var normalizedMonth = Math.Clamp(month, 1, 12);
            SetMemoMonthDragHover(normalizedMonth);
            if (_pointerHoverMemoMonth != normalizedMonth)
            {
                _pointerHoverMemoMonth = normalizedMonth;
                UpdateMemoMonthHighlights();
            }

            if (_pendingMemoHoverMonth == normalizedMonth)
            {
                return;
            }

            _memoMonthHoverTimer?.Stop();
            _pendingMemoHoverMonth = normalizedMonth;
            _memoMonthHoverTimer ??= DispatcherQueue.CreateTimer();
            _memoMonthHoverTimer.Interval = TimeSpan.FromMilliseconds(200);
            _memoMonthHoverTimer.Tick -= MemoMonthHoverTimer_Tick;
            _memoMonthHoverTimer.Tick += MemoMonthHoverTimer_Tick;
            _memoMonthHoverTimer.Start();
        }

        private void SetMemoMonthDragHover(int? month)
        {
            if (_dragHoverMemoMonth == month)
            {
                return;
            }

            _dragHoverMemoMonth = month;
        }

        private void ApplyMemoMonthDragHover(Button item, bool isHovering)
        {
            if (!isHovering)
            {
                ClearMemoMonthDragHoverVisual(item);
                return;
            }

            var hoverBrush = GetMemoMonthNavigationHoverBrush();
            SetMemoMonthButtonBackground(item, hoverBrush);
        }

        private void ClearMemoMonthDragHoverVisuals()
        {
            foreach (var item in GetMemoMonthNavigationItems())
            {
                ClearMemoMonthDragHoverVisual(item);
            }

            UpdateMemoMonthHighlights();
        }

        private void ClearMemoMonthDragHoverVisual(Button item)
        {
            RestoreMemoMonthButtonBackground(item);
        }

        private static void SetMemoMonthButtonBackground(Button item, Brush brush)
        {
            StretchMemoMonthButtonContent(item);
            if (GetMemoMonthButtonBackground(item) is { } background)
            {
                background.Background = brush;
            }
        }

        private static void StretchMemoMonthButtonContent(Button item)
        {
            if (item.Content is not Grid grid || item.ActualWidth <= 0)
            {
                return;
            }

            grid.Width = item.ActualWidth;
            foreach (var child in grid.Children.OfType<FrameworkElement>())
            {
                child.Width = item.ActualWidth;
            }
        }

        private void RestoreMemoMonthButtonBackground(Button item)
        {
            var month = GetMemoMonthFromElement(item);
            SetMemoMonthButtonBackground(item, month == _highlightedMemoMonth || month == _pointerHoverMemoMonth
                ? GetMemoMonthNavigationHoverBrush()
                : GetTransparentBrush());
        }

        private static Brush GetMemoMonthNavigationHoverBrush()
        {
            return Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var subtleHoverBrush)
                       && subtleHoverBrush is Brush brush
                ? brush
                : (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
        }

        private static Brush GetMemoMonthNavigationSelectedBrush()
        {
            return Application.Current.Resources.TryGetValue("SubtleFillColorTransparentBrush", out var transparentBrush)
                       && transparentBrush is Brush brush
                ? brush
                : GetTransparentBrush();
        }

        private void MemoMonthHoverTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            if (_pendingMemoHoverMonth is not { } month)
            {
                return;
            }

            SelectMemoMonth(month, true);
        }

        private void SelectMemoMonth(int month, bool updateHighlight = false)
        {
            var normalizedMonth = Math.Clamp(month, 1, 12);
            if (updateHighlight)
            {
                _highlightedMemoMonth = normalizedMonth;
            }

            if (_selectedMemoMonth == normalizedMonth)
            {
                UpdateMemoMonthHighlights();
                return;
            }

            _selectedMemoMonth = normalizedMonth;
            RenderMemoCalendar();
        }

        private void ApplyAppThemeFromWindows(string? mode = null)
        {
            RootNavigationView.RequestedTheme = (mode ?? WindowsThemeService.GetCurrentMode()) == "dark"
                ? ElementTheme.Dark
                : ElementTheme.Light;
            if (_dailyForecast.Count > 0)
            {
                RenderDailyForecast();
            }
            if (_hourlyForecast.Count >= 2)
            {
                DispatcherQueue.TryEnqueue(() => RenderHourlyForecast());
            }
        }

        private bool IsMemoDarkTheme()
        {
            if (MemoPage.ActualTheme == ElementTheme.Dark)
            {
                return true;
            }

            if (MemoPage.ActualTheme == ElementTheme.Light)
            {
                return false;
            }

            return WindowsThemeService.GetCurrentMode() == "dark";
        }

        private SolidColorBrush GetMemoMonthBrush(int month, byte alpha = 255)
        {
            var color = GetMemoMonthColor(month);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        private (byte R, byte G, byte B) GetMemoMonthColor(int month)
        {
            var colors = IsMemoDarkTheme() ? MemoDarkMonthColors : MemoLightMonthColors;
            return colors[Math.Clamp(month, 1, 12) - 1];
        }

        private void RenderMemoCalendar()
        {
            EnsureMemoCalendarCells();
            _memoCalendarDays.Clear();

            var firstDay = new DateTime(_selectedMemoYear, _selectedMemoMonth, 1);
            var leadingDays = ((int)firstDay.DayOfWeek + 6) % 7;
            const int rowCount = 6;

            DispatcherQueue.TryEnqueue(UpdateMemoMonthHighlights);

            var visibleDates = Enumerable.Range(0, rowCount * 7)
                .Select(offset => firstDay.AddDays(offset - leadingDays))
                .ToArray();
            var visibleDateKeys = visibleDates
                .Select(date => (date.Year, date.Month, date.Day))
                .ToHashSet();
            var visibleMemos = _memoStore.GetAll()
                .Where(memo => memo.Year == _selectedMemoYear &&
                    memo.Month == _selectedMemoMonth &&
                    visibleDateKeys.Contains((memo.Year, memo.Month, memo.Day)))
                .GroupBy(memo => (memo.Year, memo.Month, memo.Day))
                .ToDictionary(group => group.Key, group => group.ToArray());

            for (var index = 0; index < visibleDates.Length; index++)
            {
                var date = visibleDates[index];
                var row = index / 7;
                var column = index % 7;
                var cell = _memoCalendarCells[index];
                var dayModel = cell.Day;
                dayModel.Update(
                    date.Year,
                    date.Month,
                    date.Day,
                    date.Month == _selectedMemoMonth);
                dayModel.Memos.Clear();
                if (dayModel.IsInSelectedMonth)
                {
                    _memoCalendarDays[dayModel.Day] = dayModel;
                }

                if (visibleMemos.TryGetValue((date.Year, date.Month, date.Day), out var memos))
                {
                    foreach (var memo in memos)
                    {
                        dayModel.Memos.Add(MemoCalendarItem.FromMemo(memo));
                    }
                }

                UpdateMemoDayCell(cell, row, column);
            }
        }

        private void EnsureMemoCalendarCells()
        {
            if (_memoCalendarCells.Count == 42)
            {
                return;
            }

            MemoCalendarGrid.Children.Clear();
            MemoCalendarGrid.RowDefinitions.Clear();
            MemoCalendarGrid.ColumnDefinitions.Clear();
            _memoCalendarCells.Clear();

            for (var column = 0; column < 7; column++)
            {
                MemoCalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (var row = 0; row < 6; row++)
            {
                MemoCalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            }

            for (var index = 0; index < 42; index++)
            {
                var row = index / 7;
                var column = index % 7;
                var cell = CreateMemoDayCell(row, column);
                _memoCalendarCells.Add(cell);
                Grid.SetRow(cell.Border, row);
                Grid.SetColumn(cell.Border, column);
                MemoCalendarGrid.Children.Add(cell.Border);
            }
        }

        private MemoCalendarCell CreateMemoDayCell(int row, int column)
        {
            var day = new MemoCalendarDay(1, 1, 1, false);
            var itemContainerStyle = new Style(typeof(ListViewItem));
            itemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            itemContainerStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 22d));
            itemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            var listView = new ListView
            {
                ItemsSource = day.Memos,
                ItemTemplate = (DataTemplate)MemoPage.Resources["MemoCalendarItemTemplate"],
                ItemContainerStyle = itemContainerStyle,
                SelectionMode = ListViewSelectionMode.None,
                CanDragItems = day.IsInSelectedMonth,
                CanReorderItems = day.IsInSelectedMonth,
                AllowDrop = day.IsInSelectedMonth,
                ReorderMode = day.IsInSelectedMonth ? ListViewReorderMode.Enabled : ListViewReorderMode.Disabled,
                Tag = day,
                Padding = new Thickness(0),
                MaxHeight = 88
            };
            listView.DragItemsCompleted += MemoDayListView_DragItemsCompleted;
            listView.DragItemsStarting += MemoDayListView_DragItemsStarting;
            listView.DragOver += MemoDayDropTarget_DragOver;
            listView.Drop += MemoDayDropTarget_Drop;

            var header = new Grid
            {
                Margin = new Thickness(0, -3, 0, 0)
            };
            var dayText = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var weekdayText = new TextBlock
            {
                Text = MemoWeekdayLabels[column],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            header.Children.Add(dayText);
            header.Children.Add(weekdayText);

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(header);
            stack.Children.Add(listView);

            var border = new Border
            {
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                Child = stack,
                Tag = day,
            };
            border.Tapped += MemoDayCell_Tapped;
            border.DragOver += MemoDayDropTarget_DragOver;
            border.Drop += MemoDayDropTarget_Drop;
            return new MemoCalendarCell(day, border, listView, dayText, weekdayText);
        }

        private void UpdateMemoDayCell(MemoCalendarCell cell, int row, int column)
        {
            var day = cell.Day;
            var isCurrentDay = day.Year == _currentMemoDate.Year &&
                day.Month == _currentMemoDate.Month &&
                day.Day == _currentMemoDate.Day;
            var hasMemo = day.IsInSelectedMonth && day.Memos.Count > 0;
            var currentDayBrush = GetMemoMonthBrush(day.Month);
            var inactiveTextBrush = (Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
            var dayTextBrush = isCurrentDay
                ? currentDayBrush
                : day.IsInSelectedMonth
                    ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                    : inactiveTextBrush;

            cell.DayText.Text = day.Day.ToString(CultureInfo.InvariantCulture);
            cell.DayText.Foreground = dayTextBrush;
            cell.WeekdayText.Text = MemoWeekdayLabels[column];
            cell.WeekdayText.Foreground = dayTextBrush;
            cell.WeekdayText.Visibility = row == 0 ? Visibility.Visible : Visibility.Collapsed;
            cell.ListView.CanDragItems = day.IsInSelectedMonth;
            cell.ListView.CanReorderItems = day.IsInSelectedMonth;
            cell.ListView.AllowDrop = day.IsInSelectedMonth;
            cell.ListView.ReorderMode = day.IsInSelectedMonth ? ListViewReorderMode.Enabled : ListViewReorderMode.Disabled;
            cell.Border.Background = isCurrentDay
                ? GetMemoMonthBrush(day.Month, IsMemoDarkTheme() ? (byte)42 : (byte)24)
                : hasMemo
                    ? GetMemoMonthBrush(day.Month, IsMemoDarkTheme() ? (byte)16 : (byte)7)
                    : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            cell.Border.BorderBrush = isCurrentDay
                ? currentDayBrush
                : day.IsInSelectedMonth
                    ? (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
                    : (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
            cell.Border.BorderThickness = isCurrentDay ? new Thickness(2) : new Thickness(1);
            cell.Border.AllowDrop = day.IsInSelectedMonth;
            cell.Border.Opacity = day.IsInSelectedMonth || isCurrentDay ? 1 : 0.62;
        }

        private async void MemoDayCell_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (sender is Border { Tag: MemoCalendarDay { IsInSelectedMonth: true } day })
            {
                await ShowAddMemoDialogAsync(day);
            }
        }

        private async Task ShowAddMemoDialogAsync(MemoCalendarDay day)
        {
            var typeBox = new ComboBox { SelectedIndex = 1, Width = 180 };
            typeBox.Items.Add(new ComboBoxItem { Content = "Holiday", Tag = "holiday" });
            typeBox.Items.Add(new ComboBoxItem { Content = "Event", Tag = "event" });
            typeBox.Items.Add(new ComboBoxItem { Content = "Item", Tag = "item" });

            var textBox = new TextBox { PlaceholderText = "Memo text" };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(typeBox);
            content.Children.Add(textBox);
            var applyRequested = false;

            var dialog = new ContentDialog
            {
                Title = $"{day.Month}/{day.Day}/{day.Year}",
                Content = content,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = RootNavigationView.RequestedTheme,
                XamlRoot = MemoPage.XamlRoot
            };
            dialog.Opened += (_, _) => textBox.Focus(FocusState.Programmatic);

            textBox.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter)
                {
                    return;
                }

                applyRequested = true;
                e.Handled = true;
                dialog.Hide();
            };

            var result = await dialog.ShowAsync();
            if ((result != ContentDialogResult.Primary && !applyRequested) || string.IsNullOrWhiteSpace(textBox.Text))
            {
                return;
            }

            var memo = _memoStore.Add(new MemoEntry
            {
                Year = day.Year,
                Month = day.Month,
                Day = day.Day,
                Type = (typeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "event",
                Text = textBox.Text.Trim()
            });

            if (day.Memos.Any(item => string.Equals(item.Id, memo.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            day.Memos.Add(MemoCalendarItem.FromMemo(memo));
            RefreshMemoDayCell(day);
            _status.NotifyStatusChanged("memos");
        }

        private async void MemoCalendarItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (sender is FrameworkElement { Tag: MemoCalendarItem item })
            {
                e.Handled = true;
                await ShowEditMemoDialogAsync(item);
            }
        }

        private async Task ShowEditMemoDialogAsync(MemoCalendarItem item)
        {
            var typeBox = new ComboBox { SelectedIndex = MemoTypeToIndex(item.Type), Width = 180 };
            typeBox.Items.Add(new ComboBoxItem { Content = "Holiday", Tag = "holiday" });
            typeBox.Items.Add(new ComboBoxItem { Content = "Event", Tag = "event" });
            typeBox.Items.Add(new ComboBoxItem { Content = "Item", Tag = "item" });

            var textBox = new TextBox { Text = item.RawText };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(typeBox);
            content.Children.Add(textBox);
            var applyRequested = false;

            var dialog = new ContentDialog
            {
                Title = "Edit memo",
                Content = content,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = RootNavigationView.RequestedTheme,
                XamlRoot = MemoPage.XamlRoot
            };
            dialog.Opened += (_, _) =>
            {
                textBox.Focus(FocusState.Programmatic);
                textBox.SelectAll();
            };
            textBox.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter)
                {
                    return;
                }

                applyRequested = true;
                e.Handled = true;
                dialog.Hide();
            };

            var result = await dialog.ShowAsync();
            if ((result != ContentDialogResult.Primary && !applyRequested) || string.IsNullOrWhiteSpace(textBox.Text))
            {
                return;
            }

            var updated = _memoStore.Update(
                item.Id,
                (typeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "event",
                textBox.Text.Trim());
            if (updated is null)
            {
                return;
            }

            RenderMemoCalendar();
            _status.NotifyStatusChanged("memos");
        }

        private void DeleteCalendarMemoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id)
            {
                return;
            }

            if (_memoStore.Delete(id))
            {
                RemoveMemoFromCalendar(id);
                _status.NotifyStatusChanged("memos");
            }
        }

        private static bool IsInsideButton(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is Button)
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void RemoveMemoFromCalendar(string id)
        {
            foreach (var day in _memoCalendarDays.Values)
            {
                var removedAny = false;
                for (var index = day.Memos.Count - 1; index >= 0; index--)
                {
                    if (!string.Equals(day.Memos[index].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    day.Memos.RemoveAt(index);
                    removedAny = true;
                }

                if (removedAny)
                {
                    RefreshMemoDayCell(day);
                    return;
                }
            }
        }

        private void RefreshMemoDayCell(MemoCalendarDay day)
        {
            for (var index = 0; index < _memoCalendarCells.Count; index++)
            {
                var cell = _memoCalendarCells[index];
                if (!ReferenceEquals(cell.Day, day))
                {
                    continue;
                }

                UpdateMemoDayCell(cell, index / 7, index % 7);
                return;
            }
        }

        private void MemoDayListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            ClearDragHoverMemoMonth();
            if (sender.Tag is not MemoCalendarDay day)
            {
                return;
            }

            var orderedIds = day.Memos.Select(memo => memo.Id).ToArray();
            if (_memoStore.UpdateDayOrder(day.Year, day.Month, day.Day, orderedIds))
            {
                _status.NotifyStatusChanged("memos");
            }
        }

        private void MemoDayListView_DragItemsStarting(object sender, DragItemsStartingEventArgs args)
        {
            if (args.Items.FirstOrDefault() is not MemoCalendarItem item)
            {
                return;
            }

            ClearDragHoverMemoMonth();
            args.Data.SetText(item.Id);
            args.Data.RequestedOperation = DataPackageOperation.Move;
        }

        private void MemoCalendarRegion_DragLeave(object sender, DragEventArgs e)
        {
            if (e.DataView is null)
            {
                return;
            }

            var position = e.GetPosition(MemoCalendarRegion);
            var isStillInside = position.X >= 0
                && position.Y >= 0
                && position.X <= MemoCalendarRegion.ActualWidth
                && position.Y <= MemoCalendarRegion.ActualHeight;
            if (isStillInside)
            {
                return;
            }

            SwitchMemoToCurrentMonthFromCalendarDrag();
        }

        private void SwitchMemoToCurrentMonthFromCalendarDrag()
        {
            _memoMonthHoverTimer?.Stop();
            _pendingMemoHoverMonth = null;
            _dragHoverMemoMonth = null;
            _pointerHoverMemoMonth = null;

            var now = DateTimeOffset.Now;
            _selectedMemoYear = now.Year;
            SelectMemoMonth(now.Month, true);
        }

        private void MemoDayDropTarget_DragOver(object sender, DragEventArgs e)
        {
            if (GetMemoDayFromElement(sender) is null)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            e.Handled = true;
        }

        private async void MemoDayDropTarget_Drop(object sender, DragEventArgs e)
        {
            ClearDragHoverMemoMonth();
            var day = GetMemoDayFromElement(sender);
            if (day is null || !e.DataView.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var id = await e.DataView.GetTextAsync();
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            var moved = _memoStore.Move(id, day.Year, day.Month, day.Day);
            if (moved is null)
            {
                return;
            }

            RenderMemoCalendar();
            _status.NotifyStatusChanged("memos");
        }

        private void MemoMonthNavigationItem_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.Handled = true;
            ScheduleMemoMonthFromElement(sender);
        }

        private void MemoMonthNavigationItem_DragLeave(object sender, DragEventArgs e)
        {
            var month = GetMemoMonthFromElement(sender);
            if (_dragHoverMemoMonth == month)
            {
                _memoMonthHoverTimer?.Stop();
                _pendingMemoHoverMonth = null;
                _dragHoverMemoMonth = null;
            }

            if (_pointerHoverMemoMonth == month)
            {
                _pointerHoverMemoMonth = null;
                UpdateMemoMonthHighlights();
            }
        }

        private void MemoMonthNavigationView_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.Handled = true;
        }

        private void MemoMonthNavigationView_DragLeave(object sender, DragEventArgs e)
        {
            ClearDragHoverMemoMonth();
        }

        private void MemoMonthNavigation_DragFinished(object sender, DragEventArgs e)
        {
            ClearDragHoverMemoMonth();
        }

        private static MemoCalendarDay? GetMemoDayFromElement(object sender)
        {
            return sender switch
            {
                FrameworkElement { Tag: MemoCalendarDay { IsInSelectedMonth: true } day } => day,
                _ => null
            };
        }

        private static int MemoTypeToIndex(string type)
        {
            return type.ToLowerInvariant() switch
            {
                "holiday" => 0,
                "item" => 2,
                _ => 1
            };
        }

        private async void ImportIcsButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickMemoFileAsync(".ics");
            if (file is null)
            {
                return;
            }

            var text = await FileIO.ReadTextAsync(file);
            AddImportedMemos(ParseIcsMemos(text));
        }

        private async void ImportTxtButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickMemoFileAsync(".txt");
            if (file is null)
            {
                return;
            }

            var text = await FileIO.ReadTextAsync(file);
            AddImportedMemos(ParseTextMemos(text, _selectedMemoYear));
        }

        private async Task<StorageFile?> PickMemoFileAsync(string extension)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(extension);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            return await picker.PickSingleFileAsync();
        }

        private void AddImportedMemos(IEnumerable<MemoEntry> memos)
        {
            var added = _memoStore.AddRange(memos);
            if (added == 0)
            {
                return;
            }

            RenderMemoCalendar();
            _status.NotifyStatusChanged("memos");
        }

        private static IEnumerable<MemoEntry> ParseTextMemos(string text, int defaultYear)
        {
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var parts = line.Split('=', 3);
                if (parts.Length != 3)
                {
                    continue;
                }

                var dateParts = parts[0].Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                int year;
                int month;
                int day;
                if (dateParts.Length == 2)
                {
                    year = defaultYear;
                    if (!int.TryParse(dateParts[0], out month) || !int.TryParse(dateParts[1], out day))
                    {
                        continue;
                    }
                }
                else if (dateParts.Length == 3)
                {
                    if (!int.TryParse(dateParts[0], out year) ||
                        !int.TryParse(dateParts[1], out month) ||
                        !int.TryParse(dateParts[2], out day))
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                yield return new MemoEntry
                {
                    Year = year,
                    Month = month,
                    Day = day,
                    Type = parts[1],
                    Text = parts[2]
                };
            }
        }

        private static IEnumerable<MemoEntry> ParseIcsMemos(string text)
        {
            var lines = UnfoldIcsLines(text).ToArray();
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? dateValue = null;
                string? summary = null;
                for (i++; i < lines.Length && !lines[i].Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase); i++)
                {
                    var line = lines[i];
                    if (line.StartsWith("DTSTART", StringComparison.OrdinalIgnoreCase))
                    {
                        dateValue = GetIcsValue(line);
                    }
                    else if (line.StartsWith("SUMMARY", StringComparison.OrdinalIgnoreCase))
                    {
                        summary = UnescapeIcsText(GetIcsValue(line));
                    }
                }

                if (string.IsNullOrWhiteSpace(dateValue) || string.IsNullOrWhiteSpace(summary))
                {
                    continue;
                }

                if (TryParseIcsDate(dateValue, out var date))
                {
                    yield return new MemoEntry
                    {
                        Year = date.Year,
                        Month = date.Month,
                        Day = date.Day,
                        Type = "event",
                        Text = summary
                    };
                }
            }
        }

        private static IEnumerable<string> UnfoldIcsLines(string text)
        {
            string? current = null;
            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if ((line.StartsWith(' ') || line.StartsWith('\t')) && current is not null)
                {
                    current += line[1..];
                    continue;
                }

                if (current is not null)
                {
                    yield return current;
                }

                current = line;
            }

            if (current is not null)
            {
                yield return current;
            }
        }

        private static string GetIcsValue(string line)
        {
            var index = line.IndexOf(':');
            return index >= 0 ? line[(index + 1)..].Trim() : "";
        }

        private static string UnescapeIcsText(string value)
        {
            return value
                .Replace("\\n", " ")
                .Replace("\\N", " ")
                .Replace("\\,", ",")
                .Replace("\\;", ";")
                .Replace("\\\\", "\\")
                .Trim();
        }

        private static bool TryParseIcsDate(string value, out DateTimeOffset date)
        {
            var normalized = value.Trim();
            if (normalized.Length >= 8 &&
                DateTimeOffset.TryParseExact(normalized[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
            {
                return true;
            }

            return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
        }

        private void SelectWindowsTheme(string mode)
        {
            foreach (var item in WindowsThemeComboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem &&
                    string.Equals(comboBoxItem.Tag?.ToString(), mode, StringComparison.OrdinalIgnoreCase))
                {
                    WindowsThemeComboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            WindowsThemeComboBox.SelectedIndex = 0;
        }

        private void SelectWeatherUnits(string units)
        {
            foreach (var item in WeatherUnitsComboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem &&
                    string.Equals(comboBoxItem.Tag?.ToString(), units, StringComparison.OrdinalIgnoreCase))
                {
                    WeatherUnitsComboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            WeatherUnitsComboBox.SelectedIndex = 0;
        }

        private string GetSelectedWeatherUnits()
        {
            return (WeatherUnitsComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "metric";
        }

        private string GetSelectedWindowsThemeMode()
        {
            return (WindowsThemeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        }

        private const int GwlWndProc = -4;
        private const uint WmClose = 0x0010;

        private delegate IntPtr WindowProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    }

    public sealed record LocationResult(string City, string State, string Latitude, string Longitude);

    public sealed record WeatherResult(
        string Description,
        double Temperature,
        int Humidity,
        string Icon,
        DateTimeOffset Sunrise,
        DateTimeOffset Sunset);

    public sealed class MemoCalendarDay
    {
        public MemoCalendarDay(int year, int month, int day, bool isInSelectedMonth = true)
        {
            Update(year, month, day, isInSelectedMonth);
        }

        public int Year { get; private set; }

        public int Month { get; private set; }

        public int Day { get; private set; }

        public bool IsInSelectedMonth { get; private set; }

        public ObservableCollection<MemoCalendarItem> Memos { get; } = new();

        public void Update(int year, int month, int day, bool isInSelectedMonth)
        {
            Year = year;
            Month = month;
            Day = day;
            IsInSelectedMonth = isInSelectedMonth;
        }
    }

    public sealed record MemoCalendarCell(
        MemoCalendarDay Day,
        Border Border,
        ListView ListView,
        TextBlock DayText,
        TextBlock WeekdayText);

    public sealed record Amb82DiscoveryTarget(IPAddress BroadcastAddress, string LocalIp);

    public sealed record Amb82CameraInfo(string RtspUrl, string? Name);

    public sealed class CameraMonitor
    {
        public CameraMonitor(
            string rtspUrl,
            string? cameraName,
            Border tile,
            Image videoView,
            Border blackout,
            TextBlock placeholder,
            TextBlock ipText,
            TextBlock statusText,
            Button reconnectButton,
            Button renameButton,
            Button closeButton,
            ToggleSwitch nightModeToggle)
        {
            RtspUrl = rtspUrl;
            Host = GetHostFromRtspUrl(rtspUrl);
            CameraName = NormalizeCameraName(cameraName);
            Tile = tile;
            VideoView = videoView;
            Blackout = blackout;
            Placeholder = placeholder;
            IpText = ipText;
            StatusText = statusText;
            ReconnectButton = reconnectButton;
            RenameButton = renameButton;
            CloseButton = closeButton;
            NightModeToggle = nightModeToggle;
        }

        public string RtspUrl { get; private set; }

        public string Host { get; private set; }

        public string? CameraName { get; private set; }

        public Border Tile { get; }

        public Image VideoView { get; }

        public Border Blackout { get; }

        public TextBlock Placeholder { get; }

        public TextBlock IpText { get; }

        public TextBlock StatusText { get; }

        public Button ReconnectButton { get; }

        public Button RenameButton { get; }

        public Button CloseButton { get; }

        public ToggleSwitch NightModeToggle { get; }

        public CancellationTokenSource? Cancellation { get; set; }

        public Task? Task { get; set; }

        internal CameraStream? Stream { get; set; }

        public WriteableBitmap? PreviewBitmap { get; set; }

        public DispatcherQueueTimer? FrameTimer { get; set; }

        public DispatcherQueueTimer? ReconnectCountdownTimer { get; set; }

        public DateTimeOffset? ReconnectDeadline { get; set; }

        public int AutoReconnectAttempts { get; set; }

        public bool IsSendingNightMode { get; set; }

        public bool IsNightModeEnabled { get; set; }

        public bool IsHandlingStreamLoss { get; set; }

        public bool HasReceivedFrame { get; set; }

        public long FrameGeneration { get; set; }

        public int GridIndex { get; set; }

        public void UpdateRtspUrl(string rtspUrl)
        {
            RtspUrl = rtspUrl;
            Host = GetHostFromRtspUrl(rtspUrl);
            RefreshTitle();
        }

        public void UpdateCameraName(string? cameraName)
        {
            CameraName = NormalizeCameraName(cameraName);
            RefreshTitle();
        }

        private void RefreshTitle()
        {
            IpText.Text = string.IsNullOrWhiteSpace(CameraName) ? Host : CameraName;
            ToolTipService.SetToolTip(IpText, RtspUrl);
        }

        private static string? NormalizeCameraName(string? cameraName)
        {
            var normalized = cameraName?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string GetHostFromRtspUrl(string rtspUrl)
        {
            return Uri.TryCreate(rtspUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(uri.Host)
                ? uri.Host
                : rtspUrl;
        }
    }

    public sealed class MemoCalendarItem
    {
        public string Id { get; init; } = "";

        public string Type { get; init; } = "event";

        public string RawText { get; init; } = "";

        public string Text { get; init; } = "";

        public static MemoCalendarItem FromMemo(MemoEntry memo)
        {
            return new MemoCalendarItem
            {
                Id = memo.Id,
                Type = memo.Type,
                RawText = memo.Text,
                Text = memo.Type.Equals("item", StringComparison.OrdinalIgnoreCase)
                    ? $"• {memo.Text}"
                    : memo.Text
            };
        }
    }

}
