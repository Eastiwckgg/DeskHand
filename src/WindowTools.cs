using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DeskHand
{
    sealed class OverlayPill : Form
    {
        readonly bool clickThrough;
        readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();

        public OverlayPill(bool clickThrough)
        {
            this.clickThrough = clickThrough;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            hideTimer.Tick += delegate { HidePill(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x8 | 0x80000;
                if (clickThrough) cp.ExStyle |= 0x20;
                return cp;
            }
        }

        public void ShowAt(Point near, string iconGlyph, string message, int ms, bool above, string size, Rectangle avoid = default(Rectangle))
        {
            bool dark = Program.IsDark();
            Color back = dark ? Program.Rgb(0x2C2C2C) : Program.Rgb(0xFFFFFF);
            Color fore = dark ? Program.Rgb(0xF2F2F2) : Program.Rgb(0x1F1F1F);
            Color edge = dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(36, 0, 0, 0);

            float scale = DeviceDpi / 96f;
            float fontPx = size == "large" ? 16f : size == "medium" ? 13f : 11f;
            using (var textFont = new Font("Segoe UI Semibold", fontPx * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var iconFont = new Font("Segoe MDL2 Assets", fontPx * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                Rectangle textInk, iconInk = Rectangle.Empty;
                using (Bitmap text = RenderInk(message, textFont, fore, out textInk))
                using (Bitmap icon = iconGlyph == null ? null : RenderInk(iconGlyph, iconFont, fore, out iconInk))
                {
                    int pad = (int)Math.Round(fontPx * 0.85f * scale);
                    int gap = icon == null ? 0 : (int)Math.Round(fontPx * 0.5f * scale);
                    int width = pad * 2 + iconInk.Width + gap + textInk.Width;
                    int height = (int)Math.Round(fontPx * 2.1f * scale);

                    Point place = Place(width, height, near, above, avoid, Screen.FromPoint(near).WorkingArea, scale);
                    int x = place.X, y = place.Y;
                    Bounds = new Rectangle(x, y, width, height);

                    using (var frame = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                    using (var g = Graphics.FromImage(frame))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.None;
                        using (var path = Rounded(new RectangleF(0.5f, 0.5f, width - 1, height - 1), (height - 1) / 2f))
                        using (var fill = new SolidBrush(back))
                        using (var pen = new Pen(edge, 1f))
                        {
                            g.FillPath(fill, path);
                            g.DrawPath(pen, path);
                        }
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        int left = pad;
                        if (icon != null)
                        {
                            g.DrawImage(icon, new Rectangle(left, (height - iconInk.Height) / 2, iconInk.Width, iconInk.Height), iconInk, GraphicsUnit.Pixel);
                            left += iconInk.Width + gap;
                        }
                        g.DrawImage(text, new Rectangle(left, (height - textInk.Height) / 2, textInk.Width, textInk.Height), textInk, GraphicsUnit.Pixel);
                        Present(frame, x, y);
                    }
                }
            }

            if (!Visible) Show();
            Native.SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            hideTimer.Stop();
            hideTimer.Interval = ms;
            hideTimer.Start();
        }

        static Point Place(int width, int height, Point near, bool above, Rectangle avoid, Rectangle area, float scale)
        {
            Func<int, int> clampX = v => Math.Max(area.Left, Math.Min(v, area.Right - width));
            Func<int, int> clampY = v => Math.Max(area.Top, Math.Min(v, area.Bottom - height));
            if (avoid.IsEmpty)
            {
                if (!above) return new Point(clampX(near.X + (int)(14 * scale)), clampY(near.Y + (int)(18 * scale)));
                avoid = new Rectangle(near.X, near.Y, 1, 1);
            }
            int gap = (int)(8 * scale);
            if (avoid.Top - height - gap >= area.Top) return new Point(clampX(avoid.Left), avoid.Top - height - gap);
            if (avoid.Left - width - gap >= area.Left) return new Point(avoid.Left - width - gap, clampY(avoid.Top));
            if (avoid.Right + gap + width <= area.Right) return new Point(avoid.Right + gap, clampY(avoid.Top));
            return new Point(clampX(avoid.Left), clampY(avoid.Bottom + gap));
        }

        public void HidePill()
        {
            hideTimer.Stop();
            if (Visible) Hide();
        }

        static Bitmap RenderInk(string text, Font font, Color color, out Rectangle ink)
        {
            Size measured = TextRenderer.MeasureText(text, font);
            var bmp = new Bitmap(measured.Width + 16, measured.Height + 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(color))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(text, font, brush, 6f, 6f, StringFormat.GenericTypographic);
            }
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).A < 48) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            ink = maxX < 0 ? new Rectangle(0, 0, 1, 1) : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return bmp;
        }

        void Present(Bitmap frame, int x, int y)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr memory = Native.CreateCompatibleDC(screen);
            IntPtr bitmap = frame.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memory, bitmap);
            var dst = new Native.POINT { X = x, Y = y };
            var src = new Native.POINT();
            var size = new Native.SIZE { Width = frame.Width, Height = frame.Height };
            var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, memory, ref src, 0, ref blend, Native.ULW_ALPHA);
            Native.SelectObject(memory, old);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    sealed class WindowTools
    {
        const string PinGlyph = "", UnpinGlyph = "";

        readonly SynchronizationContext ui;
        readonly OverlayPill indicator = new OverlayPill(true);
        readonly OverlayPill pinButton = new OverlayPill(false);
        readonly System.Windows.Forms.Timer menuWatch = new System.Windows.Forms.Timer();
        IntPtr pinTarget, pinMenu;
        Point pinAnchor;
        int menuTries;

        public string IndicatorSize = "small";

        public WindowTools(SynchronizationContext ui)
        {
            this.ui = ui;
            menuWatch.Tick += delegate { WatchMenu(); };
        }

        public void ShowIndicator(string text)
        {
            indicator.ShowAt(Cursor.Position, null, text, 900, false, IndicatorSize);
        }

        public void ShowNotice(string glyph, string text)
        {
            indicator.ShowAt(Cursor.Position, glyph, text, 1400, false, IndicatorSize);
        }

        public void TogglePinForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (!IsNormalWindow(fg)) return;
            bool pinned = Toggle(fg);
            indicator.ShowAt(Cursor.Position, pinned ? PinGlyph : UnpinGlyph,
                pinned ? Loc.T("Always on top", "Поверх всех окон") : Loc.T("Not on top", "Не поверх окон"), 1200, false, IndicatorSize);
        }

        public void OnMouseDown(int x, int y)
        {
            ui.Post(delegate
            {
                menuWatch.Stop();
                if (!pinButton.Visible) return;
                bool hit = pinButton.Bounds.Contains(x, y);
                pinButton.HidePill();
                if (hit && pinTarget != IntPtr.Zero) Toggle(pinTarget);
            }, null);
        }

        public void OnRightButtonUp(int x, int y)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var pt = new Native.POINT { X = x, Y = y };
                IntPtr window = Native.GetAncestor(Native.WindowFromPoint(pt), Native.GA_ROOT);
                if (!IsNormalWindow(window)) return;
                IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
                IntPtr result;
                if (Native.SendMessageTimeout(window, Native.WM_NCHITTEST, IntPtr.Zero, lParam, Native.SMTO_ABORTIFHUNG, 200, out result) == IntPtr.Zero) return;
                int hit = (int)result;
                if (hit != Native.HTCAPTION && hit != Native.HTSYSMENU) return;
                ui.Post(delegate
                {
                    pinTarget = window;
                    pinAnchor = new Point(x, y);
                    pinMenu = IntPtr.Zero;
                    menuTries = 0;
                    pinButton.HidePill();
                    menuWatch.Interval = 30;
                    menuWatch.Start();
                }, null);
            });
        }

        void WatchMenu()
        {
            if (pinMenu != IntPtr.Zero)
            {
                if (pinButton.Visible && Native.IsWindowVisible(pinMenu)) return;
                menuWatch.Stop();
                pinButton.HidePill();
                return;
            }
            IntPtr menu = FindOpenMenu();
            if (menu != IntPtr.Zero)
            {
                Native.RECT r;
                Native.GetWindowRect(menu, out r);
                pinMenu = menu;
                menuWatch.Interval = 150;
                ShowPinButton(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
                return;
            }
            if (++menuTries < 8) return;
            menuWatch.Stop();
            ShowPinButton(Rectangle.Empty);
        }

        void ShowPinButton(Rectangle avoid)
        {
            bool pinned = IsPinned(pinTarget);
            pinButton.ShowAt(pinAnchor, pinned ? UnpinGlyph : PinGlyph,
                pinned ? Loc.T("Stop keeping on top", "Убрать поверх окон") : Loc.T("Keep on top", "Поверх всех окон"), 6000, true, "medium", avoid);
        }

        static IntPtr FindOpenMenu()
        {
            IntPtr menu = IntPtr.Zero;
            while ((menu = Native.FindWindowEx(IntPtr.Zero, menu, "#32768", null)) != IntPtr.Zero)
                if (Native.IsWindowVisible(menu)) return menu;
            return IntPtr.Zero;
        }

        static bool IsPinned(IntPtr window) { return (Native.GetWindowLong(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0; }

        static bool Toggle(IntPtr window)
        {
            bool pin = !IsPinned(window);
            Native.SetWindowPos(window, pin ? (IntPtr)(-1) : (IntPtr)(-2), 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            return pin;
        }

        static bool IsNormalWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            var cls = new StringBuilder(64);
            Native.GetClassName(window, cls, 64);
            string name = cls.ToString();
            return name != "Shell_TrayWnd" && name != "Shell_SecondaryTrayWnd" && name != "Progman" && name != "WorkerW";
        }
    }
}
