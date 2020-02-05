using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace Deceive
{
    public sealed class LCUOverlay : Window
    {
        private static readonly Brush BackgroundColor = new SolidColorBrush(Color.FromRgb(1, 10, 19));
        private static readonly FontFamily Helvetica = new FontFamily("Helvetica");

        private static readonly Brush OfflineMobileColor = new SolidColorBrush(Color.FromRgb(137, 141, 127));
        private static readonly Brush InactiveColor = new SolidColorBrush(Colors.LimeGreen);
        
        // 1920x1080 sizes.
        private static readonly Rect HugeBackgroundRect = new Rect(1692, 72, 180, 41);
        private static readonly Point HugeTextOrigin = new Point(1697, 75);
        private static readonly double HugeFontSize = 18.0;
        
        // 1600x900 sizes.
        private static readonly Rect LargeBackgroundRect = new Rect(1412, 57, 147, 33);
        private static readonly Point LargeTextOrigin = new Point(1414, 60);
        private static readonly double LargeFontSize = 16.0;
        
        // 1280x720 sizes.
        private static readonly Rect MediumBackgroundRect = new Rect(1129, 46, 121, 28);
        private static readonly Point MediumTextOrigin = new Point(1130, 48);
        private static readonly double MediumFontSize = 12.0;
        
        // 1024x576 sizes.
        private static readonly Rect SmallBackgroundRect = new Rect(904, 38, 93, 19);
        private static readonly Point SmallTextOrigin = new Point(903, 38);
        private static readonly double SmallFontSize = 10.0;

        private Canvas _canvas;
        private Label _textLabel;
        private Canvas _background;
        
        public LCUOverlay()
        {
            // Set up main window.
            Width = 300;
            Height = 300;
            Title = "Deceive LCU Overlay";
            WindowStyle = WindowStyle.None;
            Topmost = true;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Colors.Black)
            {
                Opacity = 0.0
            };

            _canvas = new Canvas
            {
                Width = Width,
                Height = Height
            };
            AddChild(_canvas);

            _background = new Canvas
            {
                Background = BackgroundColor
            };
            _canvas.Children.Add(_background);

            _textLabel = new Label
            {
                FontFamily = Helvetica,
                FontSize = MediumFontSize,
                Foreground = OfflineMobileColor,
                Content = "Deceive",
                Width = 10000,
                Height = 100000
            };
            _canvas.Children.Add(_textLabel);
        }

        public void UpdateStatus(string status, bool enabled)
        {
            var content = "";
            var color = OfflineMobileColor;
            
            if (!enabled)
            {
                content = "Online (No Deceive)";
                color = InactiveColor;
            }
            else
            {
                content = status.Equals("mobile") ? "Mobile (Deceive)" : "Offline (Deceive)";
            }

            _textLabel.Content = content;
            _textLabel.Foreground = color;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            _canvas.Width = Width;
            _canvas.Height = Height;

            if (Width == 1920 && Height == 1080)
            {
                // Huge
                _textLabel.FontSize = HugeFontSize;
                Canvas.SetLeft(_textLabel, HugeTextOrigin.X);
                Canvas.SetTop(_textLabel, HugeTextOrigin.Y);

                _background.Width = HugeBackgroundRect.Width;
                _background.Height = HugeBackgroundRect.Height;
                Canvas.SetLeft(_background, HugeBackgroundRect.X);
                Canvas.SetTop(_background, HugeBackgroundRect.Y);
            } else if (Width == 1600 && Height == 900)
            {
                // Large
                _textLabel.FontSize = LargeFontSize;
                Canvas.SetLeft(_textLabel, LargeTextOrigin.X);
                Canvas.SetTop(_textLabel, LargeTextOrigin.Y);

                _background.Width = LargeBackgroundRect.Width;
                _background.Height = LargeBackgroundRect.Height;
                Canvas.SetLeft(_background, LargeBackgroundRect.X);
                Canvas.SetTop(_background, LargeBackgroundRect.Y);
            } else if (Width == 1280 && Height == 720)
            {
                // Medium
                _textLabel.FontSize = MediumFontSize;
                Canvas.SetLeft(_textLabel, MediumTextOrigin.X);
                Canvas.SetTop(_textLabel, MediumTextOrigin.Y);

                _background.Width = MediumBackgroundRect.Width;
                _background.Height = MediumBackgroundRect.Height;
                Canvas.SetLeft(_background, MediumBackgroundRect.X);
                Canvas.SetTop(_background, MediumBackgroundRect.Y);
            }
            else
            {
                // Small
                _textLabel.FontSize = SmallFontSize;
                Canvas.SetLeft(_textLabel, SmallTextOrigin.X);
                Canvas.SetTop(_textLabel, SmallTextOrigin.Y);

                _background.Width = SmallBackgroundRect.Width;
                _background.Height = SmallBackgroundRect.Height;
                Canvas.SetLeft(_background, SmallBackgroundRect.X);
                Canvas.SetTop(_background, SmallBackgroundRect.Y);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            this.MakeWindowTransparent();
        }
    }

    internal static class DrawingExtensions
    {
        private const int GwlExstyle = -20;
        private const int WsExTransparent = 0x00000020;

        internal static void MakeWindowTransparent(this Window wnd)
        {
            var hwnd = new WindowInteropHelper(wnd).Handle;
            var extendedStyle = GetWindowLongPtr(hwnd, GwlExstyle);
            SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(extendedStyle.ToInt32() | WsExTransparent));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        // This static method is required because Win32 does not support
        // GetWindowLongPtr directly
        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
            => IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}