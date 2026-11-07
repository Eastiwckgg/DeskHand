using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskHand
{
    static class ClickRecorder
    {
        static Native.HookProc proc;
        static IntPtr hook;
        static Window hint, owner;
        static Action<ClickSpot> done;
        static Native.RECT hintRect;
        static bool swallowUp;
        static int downX, downY;
        static DispatcherTimer timeout;

        public static void Start(Window ownerWindow, Action<ClickSpot> onDone)
        {
            if (hook != IntPtr.Zero) return;
            owner = ownerWindow;
            done = onDone;

            bool dark = Program.IsDark();
            var text = new TextBlock
            {
                Text = Loc.T("Click the spot the scenario should click. That click is only remembered", "Кликните там, где сценарий должен нажимать. Этот клик только запомнится"),
                Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xE6, 0xE6, 0xE6) : Color.FromRgb(0x1F, 0x1F, 0x1F)),
                FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0)
            };
            var cancel = new Button { Content = Loc.T("Cancel", "Отмена"), Padding = new Thickness(14, 5, 14, 5), Cursor = System.Windows.Input.Cursors.Hand };
            cancel.Click += delegate { Stop(null); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(text);
            row.Children.Add(cancel);
            hint = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight, ShowActivated = false, ResizeMode = ResizeMode.NoResize,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                Content = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 12, 12, 12), BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xFB, 0xFB, 0xFB)),
                    BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(0x50, 0x50, 0x50) : Color.FromRgb(0xD0, 0xD0, 0xD0)),
                    Child = row
                }
            };
            hint.Loaded += delegate
            {
                var area = SystemParameters.WorkArea;
                hint.Left = area.Left + (area.Width - hint.ActualWidth) / 2;
                hint.Top = area.Top + 24;
            };
            hint.ContentRendered += delegate { Native.GetWindowRect(new WindowInteropHelper(hint).Handle, out hintRect); };
            hint.LocationChanged += delegate { Native.GetWindowRect(new WindowInteropHelper(hint).Handle, out hintRect); };

            owner.WindowState = WindowState.Minimized;
            hint.Show();
            proc = Proc;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
            timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            timeout.Tick += delegate { Stop(null); };
            timeout.Start();
        }

        static IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && hook != IntPtr.Zero)
            {
                int msg = (int)wParam;
                int x = Marshal.ReadInt32(lParam, 0), y = Marshal.ReadInt32(lParam, 4);
                bool inside = x >= hintRect.Left && x < hintRect.Right && y >= hintRect.Top && y < hintRect.Bottom;
                if ((msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN) && !inside && !swallowUp)
                {
                    swallowUp = true;
                    downX = x;
                    downY = y;
                    return (IntPtr)1;
                }
                if (swallowUp && (msg == Native.WM_LBUTTONUP || msg == Native.WM_RBUTTONUP))
                {
                    swallowUp = false;
                    int fx = downX, fy = downY;
                    owner.Dispatcher.BeginInvoke(new Action(() => Finish(fx, fy)));
                    return (IntPtr)1;
                }
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        static void Finish(int x, int y)
        {
            if (hook == IntPtr.Zero) return;
            var spot = new ClickSpot { Mode = "screen", ScreenX = x, ScreenY = y };
            IntPtr window = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT { X = x, Y = y }), Native.GA_ROOT);
            if (window != IntPtr.Zero && !IsShell(window))
            {
                uint pid;
                Native.GetWindowThreadProcessId(window, out pid);
                string name = null;
                try
                {
                    using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                Native.RECT r;
                if (name != null && pid != (uint)Process.GetCurrentProcess().Id
                    && (Native.DwmGetWindowAttribute(window, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0 || Native.GetWindowRect(window, out r))
                    && r.Right > r.Left && r.Bottom > r.Top)
                {
                    spot.Mode = "window";
                    spot.Program = name;
                    spot.X = Math.Max(0, Math.Min(1, (double)(x - r.Left) / (r.Right - r.Left)));
                    spot.Y = Math.Max(0, Math.Min(1, (double)(y - r.Top) / (r.Bottom - r.Top)));
                }
            }
            Stop(spot);
        }

        static bool IsShell(IntPtr window)
        {
            var cls = new StringBuilder(64);
            Native.GetClassName(window, cls, 64);
            string c = cls.ToString();
            return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
        }

        static void Stop(ClickSpot spot)
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            if (timeout != null) timeout.Stop();
            if (hint != null) hint.Close();
            hint = null;
            if (owner != null)
            {
                owner.WindowState = WindowState.Normal;
                owner.Activate();
            }
            var callback = done;
            done = null;
            if (spot != null && callback != null) callback(spot);
        }
    }
}
