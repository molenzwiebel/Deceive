using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static Deceive.Native.Win32;

namespace Deceive.Native;

/// Minimal base class for a top-level Win32 window with an overridable window procedure.
/// Windows must be created and pumped on the same thread.
internal abstract unsafe class NativeWindow
{
    private const string ClassName = "DeceiveWindow";

    private static readonly Dictionary<nint, NativeWindow> Windows = new();
    [ThreadStatic] private static NativeWindow? _creating;
    private static bool _registered;
    private static readonly object RegisterLock = new();

    internal nint Handle { get; private set; }

    internal static nint ModuleHandle => GetModuleHandleW(null);

    protected void CreateHandle(string title, uint style, uint exStyle, int x, int y, int width, int height)
    {
        EnsureClassRegistered();

        _creating = this;
        try
        {
            var hwnd = CreateWindowExW(exStyle, ClassName, title, style, x, y, width, height, 0, 0, ModuleHandle, 0);
            if (hwnd == 0)
                throw new InvalidOperationException($"CreateWindowEx failed with error {Marshal.GetLastPInvokeError()}.");
            Handle = hwnd;
        }
        finally
        {
            _creating = null;
        }
    }

    protected virtual nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam) => DefWindowProcW(hWnd, msg, wParam, lParam);

    protected virtual void OnHandleDestroyed()
    {
    }

    private static void EnsureClassRegistered()
    {
        lock (RegisterLock)
        {
            if (_registered)
                return;

            fixed (char* className = ClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &StaticWndProc,
                    hInstance = ModuleHandle,
                    hCursor = LoadCursorW(0, IDC_ARROW),
                    hbrBackground = GetSysColorBrush(COLOR_BTNFACE),
                    hIcon = AppIcon.Large,
                    hIconSm = AppIcon.Small,
                    lpszClassName = className
                };

                if (RegisterClassExW(&wc) == 0)
                    throw new InvalidOperationException("RegisterClassEx failed.");
            }

            _registered = true;
        }
    }

    [UnmanagedCallersOnly]
    private static nint StaticWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            NativeWindow? window;
            lock (Windows)
            {
                if (!Windows.TryGetValue(hWnd, out window) && _creating is not null)
                {
                    window = _creating;
                    window.Handle = hWnd;
                    Windows[hWnd] = window;
                }
            }

            if (window is null)
                return DefWindowProcW(hWnd, msg, wParam, lParam);

            var result = window.WndProc(hWnd, msg, wParam, lParam);

            if (msg == WM_NCDESTROY)
            {
                lock (Windows)
                    Windows.Remove(hWnd);
                window.Handle = 0;
                window.OnHandleDestroyed();
            }

            return result;
        }
        catch (Exception ex)
        {
            // Exceptions must never cross the native boundary.
            Trace.WriteLine(ex);
            return 0;
        }
    }

    /// Pumps messages for the current thread until WM_QUIT is received or <paramref name="until" /> returns true.
    /// If <paramref name="dialog" /> is set, keyboard navigation (tab, enter, escape) is handled for it.
    internal static void RunMessageLoop(Func<bool>? until = null, nint dialog = 0)
    {
        MSG msg;
        while (until?.Invoke() != true)
        {
            var result = GetMessageW(&msg, 0, 0, 0);
            if (result == 0)
            {
                // Nested loop: propagate WM_QUIT to the outer loop.
                if (until is not null)
                    PostQuitMessage((int)msg.wParam);
                return;
            }

            if (result == -1)
                return;

            if (dialog != 0 && IsDialogMessageW(dialog, &msg) != 0)
                continue;

            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
}

/// The application icon, extracted from the executable's own Win32 resources.
internal static unsafe class AppIcon
{
    private static nint _large;
    private static nint _small;

    internal static nint Large
    {
        get
        {
            Load();
            return _large;
        }
    }

    internal static nint Small
    {
        get
        {
            Load();
            return _small;
        }
    }

    private static void Load()
    {
        if (_large != 0)
            return;

        nint large = 0, small = 0;
        if (Environment.ProcessPath is { } path)
            ExtractIconExW(path, 0, &large, &small, 1);

        _large = large != 0 ? large : LoadIconW(0, IDI_APPLICATION);
        _small = small != 0 ? small : _large;
    }
}
