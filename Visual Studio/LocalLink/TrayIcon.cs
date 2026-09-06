using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LocalLink;

public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x0400 + 42;
    private const int OpenCommand = 1001;
    private const int ExitCommand = 1002;
    private const int Id = 1;
    private const int ImageIcon = 1;
    private const int LrLoadFromFile = 0x0010;
    private const int LrShared = 0x8000;
    private const int IdiApplication = 32512;
    private const int MfString = 0x0000;
    private const int TpmRightButton = 0x0002;
    private const int TpmBottomAlign = 0x0020;
    private const int TpmLeftAlign = 0x0000;
    private const int WmCommand = 0x0111;
    private const int WmDestroy = 0x0002;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmLButtonUp = 0x0202;
    private const int NimAdd = 0x00000000;
    private const int NimDelete = 0x00000002;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;

    private readonly Action _open;
    private readonly Action _exit;
    private readonly string? _iconPath;
    private readonly WndProc _wndProc;
    private readonly string _className = $"LocalLinkTrayWindow-{Guid.NewGuid()}";
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool _disposed;

    public TrayIcon(string tooltip, string? iconPath, Action open, Action exit)
    {
        _open = open;
        _exit = exit;
        _iconPath = iconPath;
        _wndProc = WindowProcedure;

        RegisterWindowClass();
        _windowHandle = CreateWindowEx(
            0,
            _className,
            _className,
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        AddIcon(tooltip);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveIcon();

        if (_windowHandle != IntPtr.Zero)
        {
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    private void AddIcon(string tooltip)
    {
        var data = CreateNotifyIconData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = LoadTrayIcon();
        data.szTip = tooltip;

        Shell_NotifyIcon(NimAdd, ref data);
    }

    private IntPtr LoadTrayIcon()
    {
        if (!string.IsNullOrWhiteSpace(_iconPath))
        {
            var fullPath = Path.Combine(AppContext.BaseDirectory, _iconPath);
            if (File.Exists(fullPath))
            {
                _iconHandle = LoadImage(IntPtr.Zero, fullPath, ImageIcon, 0, 0, LrLoadFromFile);
                if (_iconHandle != IntPtr.Zero)
                {
                    return _iconHandle;
                }
            }
        }

        return LoadImage(IntPtr.Zero, new IntPtr(IdiApplication), ImageIcon, 0, 0, LrShared);
    }

    private void RemoveIcon()
    {
        var data = CreateNotifyIconData();
        Shell_NotifyIcon(NimDelete, ref data);
    }

    private NotifyIconData CreateNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = Id
        };
    }

    private IntPtr WindowProcedure(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            var mouseMessage = lParam.ToInt32();
            if (mouseMessage is WmLButtonDblClk or WmLButtonUp)
            {
                _open();
                return IntPtr.Zero;
            }

            if (mouseMessage == WmRButtonUp)
            {
                ShowMenu();
                return IntPtr.Zero;
            }
        }

        if (message == WmCommand)
        {
            var commandId = wParam.ToInt32() & 0xffff;
            if (commandId == OpenCommand)
            {
                _open();
                return IntPtr.Zero;
            }

            if (commandId == ExitCommand)
            {
                _exit();
                return IntPtr.Zero;
            }
        }

        if (message == WmDestroy)
        {
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, OpenCommand, "Open");
        AppendMenu(menu, MfString, ExitCommand, "Exit");

        GetCursorPos(out var point);
        SetForegroundWindow(_windowHandle);
        TrackPopupMenu(menu, TpmLeftAlign | TpmBottomAlign | TpmRightButton, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
        DestroyMenu(menu);
    }

    private void RegisterWindowClass()
    {
        var windowClass = new WindowClass
        {
            cbSize = Marshal.SizeOf<WindowClass>(),
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = _className
        };

        RegisterClassEx(ref windowClass);
    }

    private delegate IntPtr WndProc(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public int cbSize;
        public int style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, int type, int width, int height, int load);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int width, int height, int load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, int id, string text);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);
}
