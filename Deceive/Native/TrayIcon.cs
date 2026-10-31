using System;
using System.Collections.Generic;
using static Deceive.Native.Win32;

namespace Deceive.Native;

internal sealed class TrayMenuItem
{
    internal TrayMenuItem(string text, Action? onClick = null, bool isChecked = false, bool enabled = true, params TrayMenuItem[] children)
    {
        Text = text;
        OnClick = onClick;
        Checked = isChecked;
        Enabled = enabled;
        Children = children;
    }

    internal string Text { get; }
    internal Action? OnClick { get; }
    internal bool Checked { get; }
    internal bool Enabled { get; }
    internal TrayMenuItem[] Children { get; }
}

/// A notification area icon with a context menu, implemented on top of Shell_NotifyIcon.
/// The menu is rebuilt from <see cref="_menuProvider" /> every time it is opened, so it always reflects current state.
internal sealed unsafe class TrayIcon : NativeWindow
{
    private const uint CallbackMessage = WM_APP + 1;
    private const uint IconId = 1;

    private readonly Func<IReadOnlyList<TrayMenuItem>> _menuProvider;
    private readonly string _tooltip;
    private readonly uint _taskbarCreatedMessage;
    private bool _added;

    internal TrayIcon(string tooltip, Func<IReadOnlyList<TrayMenuItem>> menuProvider)
    {
        _tooltip = tooltip;
        _menuProvider = menuProvider;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        // Hidden window that only exists to receive tray callbacks.
        CreateHandle(tooltip, WS_OVERLAPPED, 0, 0, 0, 0, 0);
        AddIcon();

        // Make sure we never leave a ghost icon behind, even on Environment.Exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveIcon();
    }

    internal void ShowBalloonTip(string title, string text, int timeoutMs)
    {
        var data = CreateData();
        data.uFlags = NIF_INFO;
        data.uTimeoutOrVersion = (uint)timeoutMs;
        CopyToBuffer(title, data.szInfoTitle, 64);
        CopyToBuffer(text, data.szInfo, 256);
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    /// Removes the icon and ends the message loop. Safe to call from any thread.
    internal void Close()
    {
        if (Handle != 0)
            PostMessageW(Handle, WM_CLOSE, 0, 0);
    }

    protected override nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == CallbackMessage)
        {
            if ((uint)(lParam & 0xFFFF) == WM_RBUTTONUP)
                ShowContextMenu();
            return 0;
        }

        if (msg == _taskbarCreatedMessage)
        {
            // Explorer restarted; our icon is gone and must be re-added.
            _added = false;
            AddIcon();
            return 0;
        }

        switch (msg)
        {
            case WM_CLOSE:
                DestroyWindow(hWnd);
                return 0;
            case WM_DESTROY:
                RemoveIcon();
                PostQuitMessage(0);
                return 0;
        }

        return base.WndProc(hWnd, msg, wParam, lParam);
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = Handle,
        uID = IconId
    };

    private void AddIcon()
    {
        if (_added)
            return;

        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = AppIcon.Small;
        CopyToBuffer(_tooltip, data.szTip, 128);
        _added = Shell_NotifyIconW(NIM_ADD, &data) != 0;
    }

    private void RemoveIcon()
    {
        if (!_added)
            return;

        var data = CreateData();
        Shell_NotifyIconW(NIM_DELETE, &data);
        _added = false;
    }

    private void ShowContextMenu()
    {
        var commands = new List<Action?>();
        var menu = BuildMenu(_menuProvider(), commands);

        try
        {
            POINT pt;
            GetCursorPos(&pt);

            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(Handle);
            var selected = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, pt.X, pt.Y, Handle, 0);
            PostMessageW(Handle, WM_NULL, 0, 0);

            if (selected > 0 && selected <= commands.Count)
                commands[selected - 1]?.Invoke();
        }
        finally
        {
            // Also destroys all submenus.
            DestroyMenu(menu);
        }
    }

    private static nint BuildMenu(IReadOnlyList<TrayMenuItem> items, List<Action?> commands)
    {
        var menu = CreatePopupMenu();
        foreach (var item in items)
        {
            var flags = MF_STRING;
            if (item.Checked)
                flags |= MF_CHECKED;
            if (!item.Enabled)
                flags |= MF_GRAYED;

            if (item.Children.Length > 0)
            {
                var submenu = BuildMenu(item.Children, commands);
                AppendMenuW(menu, flags | MF_POPUP, (nuint)submenu, item.Text);
            }
            else
            {
                commands.Add(item.OnClick);
                AppendMenuW(menu, flags, (nuint)commands.Count, item.Text);
            }
        }

        return menu;
    }
}
