using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Deceive.Native;
using static Deceive.Native.Win32;

namespace Deceive;

/// Modal window asking which game to launch. Replaces the old WinForms GamePromptForm.
internal sealed unsafe class GamePrompt : NativeWindow
{
    private const int CheckboxId = 200;
    private const int LabelId = 201;

    // Layout in 96 DPI pixels, matching the original form.
    private const int ClientWidth = 472;
    private const int ClientHeight = 351;
    private const int ButtonWidth = 221;
    private const int ButtonHeight = 91;

    private static readonly (LaunchGame Game, string Image, int X, int Y)[] Games =
    [
        (LaunchGame.LoL, "lol.png", 10, 31),
        (LaunchGame.LoR, "lor.png", 237, 31),
        (LaunchGame.VALORANT, "valorant.png", 10, 127),
        (LaunchGame.Lion, "lion.png", 237, 127),
        (LaunchGame.TeamfightTactics, "tft.png", 10, 224),
        (LaunchGame.RiotClient, "riot_client.png", 237, 224)
    ];

    private static bool _gdiplusStarted;

    private readonly Dictionary<int, GdiplusImage> _images = new();
    private readonly double _scale;
    private nint _checkbox;
    private nint _font;
    private bool _closed;

    private GamePrompt()
    {
        _scale = GetDpiForSystem() / 96.0;
    }

    private LaunchGame SelectedGame { get; set; } = LaunchGame.Auto;

    /// Shows the prompt and blocks until it is closed. Returns <see cref="LaunchGame.Auto" /> if the user cancelled.
    internal static LaunchGame Show()
    {
        var prompt = new GamePrompt();
        prompt.Create();
        RunMessageLoop(() => prompt._closed, prompt.Handle);
        return prompt.SelectedGame;
    }

    private int S(int value) => (int)Math.Round(value * _scale);

    private void Create()
    {
        EnsureGdiplus();

        const uint style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
        const uint exStyle = WS_EX_CONTROLPARENT;

        var rect = new RECT { Right = S(ClientWidth), Bottom = S(ClientHeight) };
        AdjustWindowRectEx(&rect, style, 0, exStyle);

        RECT workArea;
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &workArea, 0);
        var x = workArea.Left + (workArea.Width - rect.Width) / 2;
        var y = workArea.Top + (workArea.Height - rect.Height) / 2;

        CreateHandle(StartupHandler.DeceiveTitle, style, exStyle, x, y, rect.Width, rect.Height);

        _font = CreateFontW(-(int)Math.Round(9.75 * GetDpiForSystem() / 72.0), 0, 0, 0, 400, 0, 0, 0,
            1 /* DEFAULT_CHARSET */, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, "Segoe UI");

        CreateChild("STATIC", "Which game would you like to launch?", SS_CENTER, 0, S(8), S(ClientWidth), S(20), LabelId);

        nint firstButton = 0;
        for (var i = 0; i < Games.Length; i++)
        {
            var (_, image, bx, by) = Games[i];
            var button = CreateChild("BUTTON", "", BS_OWNERDRAW | WS_TABSTOP, S(bx), S(by), S(ButtonWidth), S(ButtonHeight), 100 + i);
            if (firstButton == 0)
                firstButton = button;

            var loaded = GdiplusImage.FromResource("games." + image);
            if (loaded is not null)
                _images[100 + i] = loaded;
        }

        _checkbox = CreateChild("BUTTON", "Remember my decision and skip this screen on future launches.",
            BS_AUTOCHECKBOX | WS_TABSTOP, S(13), S(321), S(440), S(21), CheckboxId);

        ShowWindow(Handle, SW_SHOW);
        SetForegroundWindow(Handle);
        SetFocus(firstButton);
    }

    private nint CreateChild(string className, string text, uint style, int x, int y, int width, int height, int id)
    {
        var hwnd = CreateWindowExW(0, className, text, WS_CHILD | WS_VISIBLE | style, x, y, width, height, Handle, id, ModuleHandle, 0);
        SendMessageW(hwnd, WM_SETFONT, _font, 1);
        return hwnd;
    }

    protected override nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_COMMAND:
                OnCommand((int)(wParam & 0xFFFF));
                return 0;

            case WM_DRAWITEM:
                DrawGameButton((DRAWITEMSTRUCT*)lParam);
                return 1;

            case WM_CTLCOLORSTATIC:
            case WM_CTLCOLORBTN:
                SetBkMode(wParam, TRANSPARENT);
                SetBkColor(wParam, GetSysColor(COLOR_BTNFACE));
                return GetSysColorBrush(COLOR_BTNFACE);

            case WM_DESTROY:
                foreach (var image in _images.Values)
                    image.Dispose();
                _images.Clear();
                if (_font != 0)
                    DeleteObject(_font);
                _font = 0;
                _closed = true;
                return 0;
        }

        return base.WndProc(hWnd, msg, wParam, lParam);
    }

    private void OnCommand(int id)
    {
        // Enter: activate whichever game button has focus.
        if (id == IDOK)
            id = GetDlgCtrlID(GetFocus());

        if (id == IDCANCEL)
        {
            DestroyWindow(Handle);
            return;
        }

        var index = id - 100;
        if (index < 0 || index >= Games.Length)
            return;

        var game = Games[index].Game;
        if ((int)SendMessageW(_checkbox, BM_GETCHECK, 0, 0) == BST_CHECKED)
            Persistence.SetDefaultLaunchGame(game);

        SelectedGame = game;
        DestroyWindow(Handle);
    }

    private void DrawGameButton(DRAWITEMSTRUCT* item)
    {
        var rect = item->rcItem;
        var hdc = item->hDC;
        var pressed = (item->itemState & ODS_SELECTED) != 0;
        var focused = (item->itemState & ODS_FOCUS) != 0;
        var hot = (item->itemState & ODS_HOTLIGHT) != 0;

        // Button chrome: themed if available, classic otherwise.
        var theme = OpenThemeData(item->hwndItem, "BUTTON");
        if (theme != 0)
        {
            const int partPushButton = 1;
            var state = pressed ? 3 : hot ? 2 : focused ? 5 : 1; // PRESSED, HOT, DEFAULTED, NORMAL
            if (IsThemeBackgroundPartiallyTransparent(theme, partPushButton, state) != 0)
                DrawThemeParentBackground(item->hwndItem, hdc, &rect);
            DrawThemeBackground(theme, hdc, partPushButton, state, &rect, null);
            CloseThemeData(theme);
        }
        else
        {
            const uint dfcButton = 4, dfcsButtonPush = 0x10, dfcsPushed = 0x200;
            DrawFrameControl(hdc, &rect, dfcButton, dfcsButtonPush | (pressed ? dfcsPushed : 0));
        }

        // Game logo, scaled to fit while preserving aspect ratio (like ImageLayout.Zoom).
        if (_images.TryGetValue((int)item->CtlID, out var image))
        {
            var inset = S(6);
            var availableWidth = rect.Width - inset * 2;
            var availableHeight = rect.Height - inset * 2;
            var ratio = Math.Min((double)availableWidth / image.Width, (double)availableHeight / image.Height);
            var width = (int)(image.Width * ratio);
            var height = (int)(image.Height * ratio);
            var x = rect.Left + (rect.Width - width) / 2 + (pressed ? 1 : 0);
            var y = rect.Top + (rect.Height - height) / 2 + (pressed ? 1 : 0);
            image.Draw(hdc, x, y, width, height);
        }

        if (focused && (item->itemState & ODS_NOFOCUSRECT) == 0)
        {
            var focusRect = new RECT { Left = rect.Left + 3, Top = rect.Top + 3, Right = rect.Right - 3, Bottom = rect.Bottom - 3 };
            DrawFocusRect(hdc, &focusRect);
        }
    }

    private static void EnsureGdiplus()
    {
        if (_gdiplusStarted)
            return;

        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        nuint token;
        _gdiplusStarted = GdiplusStartup(&token, &input, 0) == 0;
    }

    /// A PNG decoded with GDI+ from an embedded resource.
    private sealed class GdiplusImage : IDisposable
    {
        private nint _bitmap;
        private nint _stream;

        private GdiplusImage(nint stream, nint bitmap, int width, int height)
        {
            _stream = stream;
            _bitmap = bitmap;
            Width = width;
            Height = height;
        }

        internal int Width { get; }
        internal int Height { get; }

        public void Dispose()
        {
            if (_bitmap != 0)
                GdipDisposeImage(_bitmap);
            if (_stream != 0)
                Marshal.Release(_stream);
            _bitmap = _stream = 0;
        }

        internal static GdiplusImage? FromResource(string name)
        {
            using var resource = typeof(GamePrompt).Assembly.GetManifestResourceStream(name);
            if (resource is null || !_gdiplusStarted)
                return null;

            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            var bytes = buffer.ToArray();

            nint stream;
            fixed (byte* data = bytes)
                stream = SHCreateMemStream(data, (uint)bytes.Length); // copies the data
            if (stream == 0)
                return null;

            nint bitmap;
            if (GdipCreateBitmapFromStream(stream, &bitmap) != 0)
            {
                Marshal.Release(stream);
                return null;
            }

            uint width, height;
            GdipGetImageWidth(bitmap, &width);
            GdipGetImageHeight(bitmap, &height);
            return new GdiplusImage(stream, bitmap, (int)width, (int)height);
        }

        internal void Draw(nint hdc, int x, int y, int width, int height)
        {
            nint graphics;
            if (GdipCreateFromHDC(hdc, &graphics) != 0)
                return;

            GdipSetInterpolationMode(graphics, 7); // HighQualityBicubic
            GdipSetPixelOffsetMode(graphics, 4); // HighQuality
            GdipDrawImageRectI(graphics, _bitmap, x, y, width, height);
            GdipDeleteGraphics(graphics);
        }
    }
}
