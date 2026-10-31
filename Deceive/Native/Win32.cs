using System;
using System.Runtime.InteropServices;

namespace Deceive.Native;

/// Raw Win32 interop. All imports are source-generated (LibraryImport) so they are trimming/AOT safe.
internal static unsafe partial class Win32
{
    // Window messages
    public const uint WM_NULL = 0x0000;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_DRAWITEM = 0x002B;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_SETICON = 0x0080;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_CTLCOLORBTN = 0x0135;
    public const uint WM_CTLCOLORSTATIC = 0x0138;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_APP = 0x8000;

    // Window styles
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_TABSTOP = 0x00010000;
    public const uint WS_EX_CONTROLPARENT = 0x00010000;

    // Control styles / messages
    public const uint BS_AUTOCHECKBOX = 0x0003;
    public const uint BS_OWNERDRAW = 0x000B;
    public const uint SS_CENTER = 0x0001;
    public const uint BM_GETCHECK = 0x00F0;
    public const int BST_CHECKED = 1;
    public const int IDOK = 1;
    public const int IDCANCEL = 2;

    // Owner draw
    public const uint ODS_SELECTED = 0x0001;
    public const uint ODS_FOCUS = 0x0010;
    public const uint ODS_HOTLIGHT = 0x0040;
    public const uint ODS_NOFOCUSRECT = 0x0200;

    // Menus
    public const uint MF_STRING = 0x0000;
    public const uint MF_GRAYED = 0x0001;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_POPUP = 0x0010;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;

    // Misc
    public const int SW_SHOW = 5;
    public const int COLOR_BTNFACE = 15;
    public const int TRANSPARENT = 1;
    public const uint SPI_GETWORKAREA = 0x0030;
    public const nint IDC_ARROW = 32512;
    public const nint IDI_APPLICATION = 32512;
    public const nint ICON_SMALL = 0;
    public const nint ICON_BIG = 1;

    // Shell_NotifyIcon
    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;
    public const uint NIF_INFO = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWITEMSTRUCT
    {
        public uint CtlType;
        public uint CtlID;
        public uint itemID;
        public uint itemAction;
        public uint itemState;
        public nint hwndItem;
        public nint hDC;
        public RECT rcItem;
        public nuint itemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    /// Copies a string into a fixed-size, null-terminated UTF-16 buffer, truncating if needed.
    public static void CopyToBuffer(string value, char* buffer, int capacity)
    {
        var length = Math.Min(value.Length, capacity - 1);
        value.AsSpan(0, length).CopyTo(new Span<char>(buffer, capacity));
        buffer[length] = '\0';
    }

    // kernel32
    [LibraryImport("kernel32.dll")]
    public static partial nint GetModuleHandleW(char* moduleName);

    // user32
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wndClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(MSG* msg, nint hWnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    public static partial int TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial int IsDialogMessageW(nint hDlg, MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial int PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    public static partial int DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint hWnd, int cmdShow);

    [LibraryImport("user32.dll")]
    public static partial int SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    public static partial int GetDlgCtrlID(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int GetCursorPos(POINT* point);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenuW(nint hMenu, uint flags, nuint idNewItem, string? text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint hMenu, uint flags, int x, int y, nint hWnd, nint tpmParams);

    [LibraryImport("user32.dll")]
    public static partial int DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    public static partial int AdjustWindowRectEx(RECT* rect, uint style, int menu, uint exStyle);

    [LibraryImport("user32.dll")]
    public static partial int SystemParametersInfoW(uint action, uint param, void* pvParam, uint winIni);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll")]
    public static partial nint LoadIconW(nint instance, nint iconName);

    [LibraryImport("user32.dll")]
    public static partial int DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    public static partial nint GetSysColorBrush(int index);

    [LibraryImport("user32.dll")]
    public static partial uint GetSysColor(int index);

    [LibraryImport("user32.dll")]
    public static partial int DrawFocusRect(nint hdc, RECT* rect);

    [LibraryImport("user32.dll")]
    public static partial int DrawFrameControl(nint hdc, RECT* rect, uint type, uint state);

    // gdi32
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport("gdi32.dll")]
    public static partial int DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int mode);

    // shell32
    [LibraryImport("shell32.dll")]
    public static partial int Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ExtractIconExW(string file, int iconIndex, nint* largeIcons, nint* smallIcons, uint icons);

    // shlwapi
    [LibraryImport("shlwapi.dll")]
    public static partial nint SHCreateMemStream(byte* data, uint size);

    // uxtheme
    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenThemeData(nint hWnd, string classList);

    [LibraryImport("uxtheme.dll")]
    public static partial int CloseThemeData(nint theme);

    [LibraryImport("uxtheme.dll")]
    public static partial int DrawThemeBackground(nint theme, nint hdc, int partId, int stateId, RECT* rect, RECT* clip);

    [LibraryImport("uxtheme.dll")]
    public static partial int IsThemeBackgroundPartiallyTransparent(nint theme, int partId, int stateId);

    [LibraryImport("uxtheme.dll")]
    public static partial int DrawThemeParentBackground(nint hWnd, nint hdc, RECT* rect);

    // gdiplus (flat API)
    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [LibraryImport("gdiplus.dll")]
    public static partial int GdiplusStartup(nuint* token, GdiplusStartupInput* input, nint output);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateBitmapFromStream(nint stream, nint* bitmap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetImageWidth(nint image, uint* width);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipGetImageHeight(nint image, uint* height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateFromHDC(nint hdc, nint* graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetInterpolationMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPixelOffsetMode(nint graphics, int mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawImageRectI(nint graphics, nint image, int x, int y, int width, int height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDisposeImage(nint image);
}
