using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalLink;

public sealed class LocalLinkSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public int Port { get; set; } = 5123;

    public string? OpenWeatherApiKey { get; set; }

    public string WeatherUnits { get; set; } = "metric";

    public string WindowsThemeMode { get; set; } = "auto";

    public bool AutoThemeUseOpenWeather { get; set; } = true;

    public bool AutoThemeUseEsp { get; set; }

    public double AutoThemeDarkBelowLux { get; set; } = 50;

    public double AutoThemeLightAboveLux { get; set; } = 100;

    public bool StartWithWindows { get; set; }

    public bool Ws2812ShowTime { get; set; } = true;

    public bool Ws2812ShowWeather { get; set; }

    public string Ws2812ContentMode { get; set; } = "fixed";

    public string Ws2812FixedContent { get; set; } = "time";

    public int Ws2812ContentSwitchSeconds { get; set; } = 10;

    public int Ws2812TimerHours { get; set; }

    public int Ws2812TimerMinutes { get; set; } = 5;

    public int Ws2812TimerSeconds { get; set; }

    public string? CameraRtspUrl { get; set; }

    public int OpenWeatherRequestLimit { get; set; } = 0;

    public Dictionary<string, LaunchCommand> Commands { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["notepad"] = new LaunchCommand
        {
            FileName = "notepad.exe",
            Arguments = "",
            Description = "Open Notepad"
        }
    };

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLink");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static LocalLinkSettings LoadOrCreate()
    {
        Directory.CreateDirectory(SettingsDirectory);

        if (!File.Exists(SettingsPath))
        {
            var defaultSettings = new LocalLinkSettings();
            defaultSettings.Save();
            return defaultSettings;
        }

        var json = File.ReadAllText(SettingsPath);
        var settings = JsonSerializer.Deserialize<LocalLinkSettings>(json, SerializerOptions) ?? new LocalLinkSettings();
        settings.Commands = new Dictionary<string, LaunchCommand>(settings.Commands, StringComparer.OrdinalIgnoreCase);
        settings.Save();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, SerializerOptions));
    }
}

public sealed class LaunchCommand
{
    public string FileName { get; set; } = "";

    public string Arguments { get; set; } = "";

    public string? WorkingDirectory { get; set; }

    public string? Description { get; set; }
}
