using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LocalLink;

public sealed class LocalServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LocalLinkSettings _settings;
    private readonly LocalLinkStatus _status;
    private readonly MemoStore _memoStore;
    private readonly HttpClient _httpClient = new();
    private WebApplication? _app;
    private UdpClient? _luxUdp;
    private CancellationTokenSource? _luxUdpCancellation;
    private Task? _luxUdpTask;
    private Task? _capabilityExpiryTask;
    private readonly SemaphoreSlim _espSendLock = new(1, 1);
    private DateTimeOffset _lastEspWeatherBroadcast = DateTimeOffset.MinValue;
    private long _timerAlarmSuppressedUntilUnixMs;

    private const int LuxUdpPort = 5124;
    private const int EspUdpPort = 5125;

    public LocalServer(LocalLinkSettings settings, LocalLinkStatus status, MemoStore memoStore)
    {
        _settings = settings;
        _status = status;
        _memoStore = memoStore;
        _status.AttachSettings(settings);
    }

    public string Url => $"http://127.0.0.1:{_settings.Port}";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(Url);

        var app = builder.Build();
        ConfigureRoutes(app);

        _app = app;
        await app.StartAsync(cancellationToken);

        _luxUdp = new UdpClient(new IPEndPoint(IPAddress.Any, LuxUdpPort))
        {
            EnableBroadcast = true
        };
        _luxUdpCancellation = new CancellationTokenSource();
        _luxUdpTask = ListenForLuxAsync(_luxUdp, _luxUdpCancellation.Token);
        _capabilityExpiryTask = ExpireCapabilitiesAsync(_luxUdpCancellation.Token);
        await BroadcastDisplayConfigAsync();
        await BroadcastWeatherAsync();
    }

    public async Task BroadcastClockMonthAsync(int month)
    {
        await SendEspCommandAsync($"clock_month:{Math.Clamp(month, 1, 12)}", true);
    }

    public Task BroadcastDisplayConfigAsync()
    {
        var scrolling = _settings.Ws2812ContentMode.Equals("scrolling", StringComparison.OrdinalIgnoreCase);
        var mask = scrolling
            ? (_settings.Ws2812ShowTime ? 1 : 0) | (_settings.Ws2812ShowWeather ? 2 : 0)
            : _settings.Ws2812FixedContent.Equals("weather", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        var mode = scrolling ? 'S' : 'F';
        var interval = Math.Clamp(_settings.Ws2812ContentSwitchSeconds, 3, 60);
        var rotationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return SendEspCommandAsync($"L,{mode},{mask},{interval},{rotationTime}");
    }

    public async Task BroadcastWeatherAsync()
    {
        var weather = _status.Weather;
        if (weather is null)
        {
            return;
        }

        var unit = weather.Units.ToLowerInvariant() switch
        {
            "imperial" => 'F',
            "standard" => 'S',
            _ => 'C'
        };
        var temperature = Math.Clamp((int)Math.Round(weather.Temperature), -99, 999);
        var humidity = Math.Clamp(weather.Humidity, 0, 100);
        await SendEspCommandAsync($"W,{temperature},{humidity},{unit}", true);
        _lastEspWeatherBroadcast = DateTimeOffset.Now;
    }

    public Task BroadcastTimerStartAsync(int seconds) =>
        SendEspCommandAsync($"T,S,{Math.Clamp(seconds, 1, 359999)}", true);

    public Task BroadcastTimerPauseAsync() => SendEspCommandAsync("T,P", true);

    public Task BroadcastTimerResumeAsync() => SendEspCommandAsync("T,R", true);

    public Task BroadcastTimerCancelAsync() => SendEspCommandAsync("T,C", true);

    public async Task BroadcastTimerAcknowledgeAsync()
    {
        Interlocked.Exchange(
            ref _timerAlarmSuppressedUntilUnixMs,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000);
        _status.SetEspTimerAlarm(false);
        await SendEspCommandAsync("T,A", true);
        await Task.Delay(100);
        await SendEspCommandAsync("T,A", true);
    }

    private async Task SendEspCommandAsync(string command, bool repeat = false)
    {
        var udp = _luxUdp;
        if (udp is null)
        {
            return;
        }

        var payload = Encoding.UTF8.GetBytes(command);
        await _espSendLock.WaitAsync();
        try
        {
            var sendCount = repeat ? 2 : 1;
            for (var index = 0; index < sendCount; index++)
            {
                await udp.SendAsync(
                    payload,
                    payload.Length,
                    new IPEndPoint(IPAddress.Broadcast, EspUdpPort));
                if (index + 1 < sendCount)
                {
                    await Task.Delay(35);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            _espSendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_luxUdpCancellation is not null)
        {
            await _luxUdpCancellation.CancelAsync();
            _luxUdp?.Dispose();

            if (_luxUdpTask is not null)
            {
                try
                {
                    await _luxUdpTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            if (_capabilityExpiryTask is not null)
            {
                try
                {
                    await _capabilityExpiryTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _luxUdpCancellation.Dispose();
        }

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _httpClient.Dispose();
    }

    private void ConfigureRoutes(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            context.Response.Headers.AccessControlAllowHeaders = "content-type";
            context.Response.Headers.AccessControlAllowMethods = "GET,POST,OPTIONS";

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });

        app.MapGet("/api/health", () => Results.Ok(new
        {
            ok = true,
            name = "LocalLink",
            serverTime = DateTimeOffset.Now,
            endpoints = new[]
            {
                "/api/weather?city=London",
                "/api/weather?lat=40.7128&lon=-74.0060",
                "/api/status",
                "/api/events",
                "/api/theme",
                "/api/accent-month",
                "/api/memos?year=2026",
                "/api/commands",
                "/api/command/open"
            }
        }));

        app.MapGet("/api/weather", GetWeatherAsync);
        app.MapGet("/api/status", () => Results.Ok(_status.Snapshot(_settings)));
        app.MapGet("/api/events", StreamEventsAsync);
        app.MapPost("/api/accent-month", async (AccentMonthRequest request) =>
        {
            var month = Math.Clamp(request.Month, 0, 11);
            _status.SetAccentMonth(month);
            await BroadcastClockMonthAsync(month + 1);
            return Results.Ok(new { ok = true, month });
        });
        app.MapGet("/api/memos", (HttpRequest request) =>
        {
            var currentYear = DateTimeOffset.Now.Year;
            var year = int.TryParse(request.Query["year"].ToString(), out var parsedYear)
                ? parsedYear
                : currentYear;

            return Results.Ok(_memoStore.GetByYear(year));
        });
        app.MapGet("/api/theme", () =>
        {
            var configured = _settings.WindowsThemeMode;
            var effective = configured.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? WindowsThemeService.GetCurrentMode()
                : configured;

            return Results.Ok(new
            {
                configured,
                effective
            });
        });
        app.MapGet("/api/commands", () => Results.Ok(_settings.Commands.Select(command => new
        {
            id = command.Key,
            command.Value.Description
        })));
        app.MapPost("/api/command/open", OpenCommandAsync);
    }

    private async Task ListenForLuxAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var packet = await udp.ReceiveAsync(cancellationToken);
            var message = Encoding.UTF8.GetString(packet.Buffer).Trim();

            if (message.Equals("T,D", StringComparison.Ordinal))
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <
                    Interlocked.Read(ref _timerAlarmSuppressedUntilUnixMs))
                {
                    await SendEspCommandAsync("T,A", true);
                    continue;
                }
                _status.SetEspTimerAlarm(true);
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (root.TryGetProperty("tag", out var tagElement) &&
                    tagElement.GetString()?.Equals("esp_capabilities", StringComparison.OrdinalIgnoreCase) == true &&
                    root.TryGetProperty("capabilities", out var capabilitiesElement) &&
                    capabilitiesElement.ValueKind == JsonValueKind.Array)
                {
                    var capabilities = capabilitiesElement
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!);
                    _status.SetEspCapabilities(capabilities);

                    var reportedDisplayMode = "waiting";
                    if (root.TryGetProperty("display", out var displayElement) &&
                        displayElement.ValueKind == JsonValueKind.Object &&
                        displayElement.TryGetProperty("mode", out var modeElement) &&
                        modeElement.ValueKind == JsonValueKind.String)
                    {
                        reportedDisplayMode = modeElement.GetString() ?? "waiting";
                        _status.SetEspDisplay(reportedDisplayMode);
                    }

                    // Treat each capability announcement as a discovery handshake.
                    // This lets a newly powered ESP recover the current display
                    // configuration even when it missed LocalLink's startup packet.
                    await BroadcastDisplayConfigAsync();
                    if (reportedDisplayMode.Equals("waiting", StringComparison.OrdinalIgnoreCase))
                    {
                        await BroadcastWeatherAsync();
                    }
                    continue;
                }

                var reading = JsonSerializer.Deserialize<SensorReadingRequest>(message, EventJsonOptions);
                if (reading is not null &&
                    !string.IsNullOrWhiteSpace(reading.Tag) &&
                    reading.Tag.Length <= 64 &&
                    double.IsFinite(reading.Value))
                {
                    _status.SetSensor(reading.Tag, reading.Value, reading.Unit);
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    private async Task ExpireCapabilitiesAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            _status.ExpireEspCapabilities(TimeSpan.FromSeconds(15));
            if (DateTimeOffset.Now - _lastEspWeatherBroadcast >= TimeSpan.FromMinutes(1))
            {
                await BroadcastWeatherAsync();
            }
        }
    }

    private async Task StreamEventsAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream";

        async void SendStatus(LocalLinkStatusEvent statusEvent)
        {
            try
            {
                var json = JsonSerializer.Serialize(statusEvent, EventJsonOptions);
                await context.Response.WriteAsync($"event: status\ndata: {json}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
            catch
            {
                _status.Changed -= SendStatus;
            }
        }

        _status.Changed += SendStatus;
        SendStatus(new LocalLinkStatusEvent(new[] { "all" }, _status.Snapshot(_settings)));

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _status.Changed -= SendStatus;
        }
    }

    private async Task<IResult> GetWeatherAsync(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(_settings.OpenWeatherApiKey))
        {
            return Results.BadRequest(new { error = "OpenWeather API key is not configured." });
        }

        var query = request.Query;
        var city = query["city"].ToString();
        var lat = query["lat"].ToString();
        var lon = query["lon"].ToString();
        var units = query["units"].FirstOrDefault() ?? _settings.WeatherUnits;

        string url;
        if (!string.IsNullOrWhiteSpace(city))
        {
            url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(city)}&units={Uri.EscapeDataString(units)}&appid={Uri.EscapeDataString(_settings.OpenWeatherApiKey)}";
        }
        else if (!string.IsNullOrWhiteSpace(lat) && !string.IsNullOrWhiteSpace(lon))
        {
            url = $"https://api.openweathermap.org/data/2.5/weather?lat={Uri.EscapeDataString(lat)}&lon={Uri.EscapeDataString(lon)}&units={Uri.EscapeDataString(units)}&appid={Uri.EscapeDataString(_settings.OpenWeatherApiKey)}";
        }
        else
        {
            return Results.BadRequest(new { error = "Pass either city, or lat and lon." });
        }

        try
        {
            var payload = await _httpClient.GetFromJsonAsync<JsonObject>(url);
            return Results.Ok(payload);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem($"OpenWeather request failed: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private IResult OpenCommandAsync(OpenCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || !_settings.Commands.TryGetValue(request.Id, out var command))
        {
            return Results.NotFound(new { error = $"Unknown command id: {request.Id}" });
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                WorkingDirectory = command.WorkingDirectory ?? "",
                UseShellExecute = true
            });

            return Results.Ok(new { ok = true, id = request.Id });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to open command '{request.Id}': {ex.Message}");
        }
    }
}

public sealed record SensorReadingRequest(string Tag, double Value, string? Unit);


public sealed record OpenCommandRequest(string Id);

public sealed record AccentMonthRequest(int Month);
