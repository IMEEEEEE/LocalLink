using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace LocalLink;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string ValueName = "LocalLink";
    private const string StartupShortcutValueName = "LocalLink.lnk";
    private const string PackagedStartupTaskId = "LocalLinkStartupTask";
    private const string FastStartupTaskName = "LocalLink Fast Startup";

    private static string StartupShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "LocalLink.lnk");

    public static async Task<bool> IsEnabledAsync()
    {
        if (await FastStartupTaskExistsAsync())
        {
            return true;
        }

        if (HasPackageIdentity())
        {
            var task = await StartupTask.GetAsync(PackagedStartupTaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        return IsStartupShortcutEnabled();
    }

    public static async Task SetEnabledAsync(bool enabled)
    {
        RemoveLegacyRunEntry();
        RemoveStartupShortcut();

        if (!enabled)
        {
            await DeleteFastStartupTaskAsync();
            await DisablePackagedStartupTaskAsync();
            return;
        }

        if (!await FastStartupTaskExistsAsync())
        {
            await CreateFastStartupTaskAsync();
        }
        await DisablePackagedStartupTaskAsync();
    }

    private static async Task DisablePackagedStartupTaskAsync()
    {
        if (!HasPackageIdentity())
        {
            return;
        }

        var task = await StartupTask.GetAsync(PackagedStartupTaskId);
        if (task.State == StartupTaskState.Enabled)
        {
            task.Disable();
        }
    }

    private static async Task<bool> FastStartupTaskExistsAsync()
    {
        var result = await RunScheduledTaskCommandAsync(
            "/Query",
            "/TN", FastStartupTaskName);
        return result.ExitCode == 0;
    }

    private static async Task CreateFastStartupTaskAsync()
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var (command, arguments) = GetFastStartupAction();
        var escapedSid = SecurityElement.Escape(userSid);
        var escapedCommand = SecurityElement.Escape(command);
        var escapedArguments = SecurityElement.Escape(arguments);
        var argumentsElement = string.IsNullOrWhiteSpace(escapedArguments)
            ? ""
            : $"<Arguments>{escapedArguments}</Arguments>";
        var taskXml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Start LocalLink immediately when this user signs in.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{escapedSid}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{escapedSid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{escapedCommand}</Command>
                  {argumentsElement}
                </Exec>
              </Actions>
            </Task>
            """;

        Directory.CreateDirectory(LocalLinkSettings.SettingsDirectory);
        var taskFile = Path.Combine(LocalLinkSettings.SettingsDirectory, "fast-startup-task.xml");
        try
        {
            await File.WriteAllTextAsync(taskFile, taskXml, Encoding.Unicode);
            var result = await RunScheduledTaskCommandAsync(
                "/Create",
                "/TN", FastStartupTaskName,
                "/XML", taskFile,
                "/F");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? "Windows could not create the fast startup task."
                        : result.Error.Trim());
            }
        }
        finally
        {
            File.Delete(taskFile);
        }
    }

    private static async Task DeleteFastStartupTaskAsync()
    {
        if (!await FastStartupTaskExistsAsync())
        {
            return;
        }

        var result = await RunScheduledTaskCommandAsync(
            "/Delete",
            "/TN", FastStartupTaskName,
            "/F");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Error.Trim());
        }
    }

    private static (string Command, string Arguments) GetFastStartupAction()
    {
        if (HasPackageIdentity())
        {
            var explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            var appUserModelId = $"{Package.Current.Id.FamilyName}!App";
            return (explorerPath, $"shell:AppsFolder\\{appUserModelId}");
        }

        return (GetExecutablePath(), "");
    }

    private static async Task<(int ExitCode, string Error)> RunScheduledTaskCommandAsync(
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Task Scheduler could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await outputTask;
        return (process.ExitCode, await errorTask);
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id.Name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsStartupShortcutEnabled()
    {
        if (!File.Exists(StartupShortcutPath))
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, false);
        var state = key?.GetValue(StartupShortcutValueName) as byte[];
        return state is not { Length: > 0 } || state[0] != 3;
    }

    private static void ClearStartupApprovalOverride()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true);
        key?.DeleteValue(StartupShortcutValueName, false);
    }

    private static void RemoveStartupShortcut()
    {
        if (File.Exists(StartupShortcutPath))
        {
            File.Delete(StartupShortcutPath);
        }
    }

    private static void CreateStartupShortcut()
    {
        var executablePath = GetExecutablePath();
        var shellLinkType = Type.GetTypeFromCLSID(ShellLinkClsid, true)
            ?? throw new InvalidOperationException("ShellLink COM type was not found.");
        var shortcut = (IShellLinkW)(Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("Could not create ShellLink COM object."));
        shortcut.SetPath(executablePath);
        shortcut.SetWorkingDirectory(Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory);
        shortcut.SetDescription("Start LocalLink");

        if (File.Exists(GetIconPath()))
        {
            shortcut.SetIconLocation(GetIconPath(), 0);
        }

        ((IPersistFile)shortcut).Save(StartupShortcutPath, true);
    }

    private static void RemoveLegacyRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.DeleteValue(ValueName, false);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LocalLink.exe");
    }

    private static string GetIconPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "LocalLink.ico");
    }

    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] string pszFile,
            int cchMaxPath,
            IntPtr pfd,
            uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] string pszName,
            int cchMaxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] string pszDir,
            int cchMaxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] string pszArgs,
            int cchMaxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
            int cchIconPath,
            out int piIcon);

        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
            int iIcon);

        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
            uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
