using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalLink;

public sealed class LocalLinkStatus
{
    private readonly object _lock = new();
    private LocalLinkSettings? _settings;
    private long _revision;

    public event Action<LocalLinkStatusEvent>? Changed;

    public LocationStatus? Location { get; private set; }

    public WeatherStatus? Weather { get; private set; }

    public Dictionary<string, SensorStatus> Sensors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> EspCapabilities { get; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? EspLastSeen { get; private set; }

    public EspDisplayStatus? EspDisplay { get; private set; }

    public bool EspTimerAlarm { get; private set; }

    public int AccentMonth { get; private set; } = DateTimeOffset.Now.Month - 1;

    public void AttachSettings(LocalLinkSettings settings)
    {
        _settings = settings;
    }

    public void SetLocation(LocationResult location)
    {
        lock (_lock)
        {
            Location = new LocationStatus(
                location.City,
                location.State,
                location.Latitude,
                location.Longitude,
                DateTimeOffset.Now);
        }

        NotifyChanged("location");
    }

    public void SetLocationSilently(LocationResult location)
    {
        lock (_lock)
        {
            Location = new LocationStatus(
                location.City,
                location.State,
                location.Latitude,
                location.Longitude,
                DateTimeOffset.Now);
        }
    }

    public void SetWeather(WeatherResult weather, string units)
    {
        lock (_lock)
        {
            Weather = new WeatherStatus(
                weather.Description,
                weather.Icon,
                weather.Temperature,
                weather.Humidity,
                units,
                weather.Sunrise,
                weather.Sunset,
                DateTimeOffset.Now);
        }

        NotifyChanged("weather");
    }

    public void SetWeatherSilently(WeatherResult weather, string units)
    {
        lock (_lock)
        {
            Weather = new WeatherStatus(
                weather.Description,
                weather.Icon,
                weather.Temperature,
                weather.Humidity,
                units,
                weather.Sunrise,
                weather.Sunset,
                DateTimeOffset.Now);
        }
    }

    public void SetSensor(string tag, double value, string? unit)
    {
        var normalizedTag = tag.Trim();
        lock (_lock)
        {
            Sensors[normalizedTag] = new SensorStatus(value, unit, DateTimeOffset.Now);
        }

        NotifyChanged($"sensors.{normalizedTag}");
    }

    public bool TryGetSensor(string tag, out SensorStatus? sensor)
    {
        lock (_lock)
        {
            return Sensors.TryGetValue(tag, out sensor);
        }
    }

    public void SetEspCapabilities(IEnumerable<string> capabilities)
    {
        var normalized = capabilities
            .Select(capability => capability.Trim())
            .Where(capability => capability.Length is > 0 and <= 64)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        lock (_lock)
        {
            changed = !EspCapabilities.SetEquals(normalized);
            if (changed)
            {
                EspCapabilities.Clear();
                EspCapabilities.UnionWith(normalized);
            }

            EspLastSeen = DateTimeOffset.Now;
        }

        if (changed)
        {
            NotifyChanged("espCapabilities");
        }
    }

    public bool HasEspCapability(string capability)
    {
        lock (_lock)
        {
            return EspCapabilities.Contains(capability);
        }
    }

    public void SetEspDisplay(string mode)
    {
        var normalizedMode = mode.Trim().ToLowerInvariant() switch
        {
            "clock" => "clock",
            "weather" => "weather",
            "timer" => "timer",
            _ => "waiting"
        };
        var changed = false;

        lock (_lock)
        {
            changed = EspDisplay is null ||
                !EspDisplay.Mode.Equals(normalizedMode, StringComparison.OrdinalIgnoreCase);
            EspDisplay = new EspDisplayStatus(
                normalizedMode,
                DateTimeOffset.Now);
        }

        if (changed)
        {
            NotifyChanged("espDisplay");
        }
    }

    public void SetEspTimerAlarm(bool active)
    {
        lock (_lock)
        {
            if (EspTimerAlarm == active)
            {
                return;
            }

            EspTimerAlarm = active;
        }

        NotifyChanged("espTimerAlarm");
    }

    public void ExpireEspCapabilities(TimeSpan maximumAge)
    {
        var changed = false;
        lock (_lock)
        {
            if (EspCapabilities.Count > 0 &&
                EspLastSeen is { } lastSeen &&
                DateTimeOffset.Now - lastSeen > maximumAge)
            {
                EspCapabilities.Clear();
                EspLastSeen = null;
                EspDisplay = null;
                EspTimerAlarm = false;
                changed = true;
            }
        }

        if (changed)
        {
            NotifyChanged("espCapabilities");
        }
    }

    public void NotifyThemeChanged()
    {
        NotifyChanged("theme");
    }

    public void SetAccentMonth(int month)
    {
        var normalizedMonth = Math.Clamp(month, 0, 11);
        lock (_lock)
        {
            if (AccentMonth == normalizedMonth)
            {
                return;
            }

            AccentMonth = normalizedMonth;
        }

        NotifyChanged("accentMonth");
    }

    public void NotifySettingsChanged(params string[] changes)
    {
        NotifyChanged(changes.Length == 0 ? new[] { "settings" } : changes);
    }

    public void NotifyStatusChanged(params string[] changes)
    {
        NotifyChanged(changes.Length == 0 ? new[] { "status" } : changes);
    }

    public object Snapshot(LocalLinkSettings settings)
    {
        lock (_lock)
        {
            var configuredTheme = settings.WindowsThemeMode;
            var effectiveTheme = configuredTheme.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? WindowsThemeService.GetCurrentMode()
                : configuredTheme;

            return new
            {
                ok = Location is not null || Weather is not null || Sensors.Count > 0,
                revision = _revision,
                updatedAt = DateTimeOffset.Now,
                location = Location,
                weather = Weather,
                sensors = Sensors,
                esp = new
                {
                    capabilities = EspCapabilities.OrderBy(capability => capability).ToArray(),
                    lastSeen = EspLastSeen,
                    display = EspDisplay,
                    timerAlarm = EspTimerAlarm
                },
                settings = new
                {
                    weatherUnits = settings.WeatherUnits
                },
                appearance = new
                {
                    accentMonth = AccentMonth
                },
                theme = new
                {
                    configured = configuredTheme,
                    effective = effectiveTheme
                }
            };
        }
    }

    private void NotifyChanged(params string[] changes)
    {
        if (_settings is not null)
        {
            lock (_lock)
            {
                _revision++;
            }

            Changed?.Invoke(new LocalLinkStatusEvent(
                changes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Snapshot(_settings)));
        }
    }
}

public sealed record LocalLinkStatusEvent(IReadOnlyList<string> Changes, object Status);

public sealed record LocationStatus(
    string City,
    string State,
    string Latitude,
    string Longitude,
    DateTimeOffset UpdatedAt);

public sealed record WeatherStatus(
    string Description,
    string Icon,
    double Temperature,
    int Humidity,
    string Units,
    DateTimeOffset Sunrise,
    DateTimeOffset Sunset,
    DateTimeOffset UpdatedAt);

public sealed record SensorStatus(
    double Value,
    string? Unit,
    DateTimeOffset UpdatedAt);

public sealed record EspDisplayStatus(
    string Mode,
    DateTimeOffset UpdatedAt);
