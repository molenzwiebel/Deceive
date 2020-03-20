using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Deceive
{
    /**
     * Utility class that automatically has a specified window follow the main
     * window of a different process. This uses event hooking instead of polling
     * to give a very smooth following.
     */
    public class WindowFollower
    {
        // These are needed, see the constructor why.
        private WinEventDelegate _focusChangedCallback;
        private WinEventDelegate _targetMovedCallback;

        private Matrix _dpiMatrix = new Matrix(1, 1, 1, 1, 1, 1);
        private bool _hasDpiMatrix;

        private Window _overlay;
        private IntPtr _overlayHandle;
        private Process _target;
        private Rect _targetPosition;
        private IntPtr _handle;

        public WindowFollower(Window overlay, Process p)
        {
            _overlay = overlay;
            _target = p;
            _handle = p.MainWindowHandle;
            _overlayHandle = new WindowInteropHelper(_overlay).Handle;
            GetWindowRect(_handle, ref _targetPosition); // get initial window position

            // If we just use these functions directly, C# will create a closure to capture the current `this`.
            // We don't want this, since C# will at some point GC this closure while it is still in use and
            // cause the callbacks to reference invalid memory, causing Deceive to crash. Instead, we can assign
            // them to a local variable which will prevent them from being GCd while still in use.
            _focusChangedCallback = FocusChanged;
            _targetMovedCallback = TargetMoved;
        }

        /**
         * Starts following the specified window, the first time it gains focus.
         */
        public void StartFollowing()
        {
            // Listen to moves.
            SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _targetMovedCallback,
                (uint) _target.Id,
                GetWindowThreadProcessId(_handle, IntPtr.Zero),
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // Listen to focus changes.
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _focusChangedCallback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // Listen to window being unminimized.
            SetWinEventHook(EVENT_SYSTEM_MINIMIZEEND, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _focusChangedCallback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // Listen to ALT + TAB ending.
            SetWinEventHook(EVENT_SYSTEM_SWITCHEND, EVENT_SYSTEM_SWITCHEND, IntPtr.Zero, _focusChangedCallback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            // Manually trigger the focus command to check if we should focus right now.
            FocusChanged(IntPtr.Zero, 0, IntPtr.Zero, 0, 0, 0, 0);
        }

        /**
         * Called when the followed window moves.
         */
        private void TargetMoved(IntPtr hWinEventHook, uint eventType, IntPtr lParam, uint idObject, int idChild,
            uint dwEventThread, uint dwmsEventTime)
        {
            // Ignore mouse location changes on window, since window itself does not move.
            if (idObject == OBJID_CURSOR) return;

            if (!GetWindowRect(_handle, ref _targetPosition)) return;

            LoadDpiMatrix();
            
            _overlay.Left = _targetPosition.Left / _dpiMatrix.M11;
            _overlay.Top = _targetPosition.Top / _dpiMatrix.M22;
            _overlay.Width = (_targetPosition.Right - _targetPosition.Left) / _dpiMatrix.M11;
            _overlay.Height = (_targetPosition.Bottom - _targetPosition.Top) / _dpiMatrix.M22;
        }

        /**
         * Called when the main focused window changes, to hide/show on demand.
         */
        private void FocusChanged(IntPtr hWinEventHook, uint eventType, IntPtr lParam, uint idObject, int idChild,
            uint dwEventThread, uint dwmsEventTime)
        {
            // Check if we should appear or not.
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground != _handle)
            {
                _overlay.Topmost = false;

                // Make sure that the new window is higher in the z position than us.
                SetWindowPos(foreground, _overlayHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            }
            else
            {
                if (!GetWindowRect(_handle, ref _targetPosition)) return;

                LoadDpiMatrix();

                _overlay.Show();
                _overlay.Topmost = true;
                _overlay.Left = _targetPosition.Left / _dpiMatrix.M11;
                _overlay.Top = _targetPosition.Top / _dpiMatrix.M22;
                _overlay.Width = (_targetPosition.Right - _targetPosition.Left) / _dpiMatrix.M11;
                _overlay.Height = (_targetPosition.Bottom - _targetPosition.Top) / _dpiMatrix.M22;
            }
        }

        /**
         * The DPI matrix is not always available. This method will attempt to find
         * the current DPI matrix, or default to the [1,1,1,1] matrix if not able to.
         */
        private void LoadDpiMatrix()
        {
            if (_hasDpiMatrix) return;

            var source = PresentationSource.FromVisual(_overlay);
            if (source?.CompositionTarget == null) return;

            _dpiMatrix = source.CompositionTarget.TransformToDevice;
            _hasDpiMatrix = true;
        }

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, uint idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, ref Rect lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy,
            uint uFlags);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        private const uint EVENT_SYSTEM_SWITCHEND = 0x0015;
        private const uint OBJID_CURSOR = 0xFFFFFFF7;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public readonly int Left;
            public readonly int Top;
            public readonly int Right;
            public readonly int Bottom;
        }
    }
}