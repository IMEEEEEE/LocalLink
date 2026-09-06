using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LocalLink;

public static class WindowsThemeService
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";
    private const string SystemUsesLightThemeValue = "SystemUsesLightTheme";
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001a;
    private static int _broadcastVersion;

    public static void Apply(string mode)
    {
        TryApply(mode);
    }

    private static bool TryApply(string mode)
    {
        switch (mode.ToLowerInvariant())
        {
            case "light":
                return SetLightTheme(true);
            case "dark":
                return SetLightTheme(false);
            default:
                return false;
        }
    }

    public static Task ApplyAsync(string mode)
    {
        if (!TryApply(mode))
        {
            return Task.CompletedTask;
        }

        return BroadcastThemeChangeAgainAsync(Volatile.Read(ref _broadcastVersion));
    }

    public static string GetCurrentMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
        var appsValue = key?.GetValue(AppsUseLightThemeValue);
        var systemValue = key?.GetValue(SystemUsesLightThemeValue);

        var appsMode = ToMode(appsValue);
        var systemMode = ToMode(systemValue);

        if (appsMode == systemMode)
        {
            return appsMode ?? "auto";
        }

        return appsMode ?? systemMode ?? "auto";
    }

    private static bool SetLightTheme(bool isLight)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKeyPath);
        var value = isLight ? 1 : 0;

        if (ToMode(key?.GetValue(AppsUseLightThemeValue)) == (isLight ? "light" : "dark") &&
            ToMode(key?.GetValue(SystemUsesLightThemeValue)) == (isLight ? "light" : "dark"))
        {
            return false;
        }

        key?.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
        key?.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
        Interlocked.Increment(ref _broadcastVersion);
        BroadcastThemeChange();
        return true;
    }

    private static string? ToMode(object? value)
    {
        return value switch
        {
            int number => number == 0 ? "dark" : "light",
            long number => number == 0 ? "dark" : "light",
            _ => null
        };
    }

    private static void BroadcastThemeChange()
    {
        SendNotifyMessage(
            HwndBroadcast,
            WmSettingChange,
            UIntPtr.Zero,
            "ImmersiveColorSet");
    }

    private static async Task BroadcastThemeChangeAgainAsync(int version)
    {
        await Task.Delay(150);
        if (version != Volatile.Read(ref _broadcastVersion))
        {
            return;
        }

        BroadcastThemeChange();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SendNotifyMessage(
        int hWnd,
        int msg,
        UIntPtr wParam,
        string lParam);

}
