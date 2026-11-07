using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DeskHand
{
    sealed partial class ScenarioRunner
    {
        bool WaitWindow(ScenarioStep step, Context ctx, CancellationToken token)
        {
            var names = Names(step.Value, ctx);
            double limit = Durations.Parse(step.Option);
            if (limit <= 0) limit = 30;
            var until = DateTime.UtcNow.AddSeconds(limit);
            while (DateTime.UtcNow < until)
            {
                string which = null;
                IntPtr found = IntPtr.Zero;
                if (names.Count == 0) found = ctx.Opened;
                foreach (string n in names)
                {
                    found = FindProgramWindow(n);
                    if (found != IntPtr.Zero) { which = n; break; }
                }
                if (found != IntPtr.Zero && Native.IsWindow(found))
                {
                    ctx.Opened = found;
                    if (which != null) ctx.LastProgram = which;
                    return true;
                }
                if (token.WaitHandle.WaitOne(200)) return false;
            }
            host.Notice(Loc.T("The window did not appear, the scenario stopped", "Окно не появилось, сценарий остановлен"));
            return false;
        }

        bool WaitClose(ScenarioStep step, Context ctx, CancellationToken token)
        {
            var names = Names(step.Value, ctx);
            if (names.Count == 0) return true;
            double limit = Durations.Parse(step.Option);
            var until = limit > 0 ? DateTime.UtcNow.AddSeconds(limit) : DateTime.MaxValue;
            while (names.Any(SystemState.ProgramRunning))
            {
                if (DateTime.UtcNow >= until) return true;
                if (token.WaitHandle.WaitOne(1000)) return false;
            }
            return true;
        }

        public static bool Check(string kind, string arg, DateTime now, string buffer)
        {
            string a = (arg ?? "").Trim();
            switch (kind)
            {
                case "text": return ScenarioCondition.TextMatches(arg, ClipboardText(), buffer);
                case "running": return Scenario.Names(a).Any(SystemState.ProgramRunning);
                case "active":
                    string front = SystemState.ActiveProgram();
                    return front != null && Scenario.Names(a).Any(x => x.Equals(front, StringComparison.OrdinalIgnoreCase));
                case "time": return ScenarioCondition.InTimeRange(a, now);
                case "days": return ScenarioCondition.OnDay(a, now);
                case "wifi":
                    string wifi = SystemState.WifiName();
                    return wifi != null && (a.Length == 0 || string.Equals(wifi, a, StringComparison.OrdinalIgnoreCase));
                case "internet": return SystemState.Internet();
                case "battery": return SystemState.OnBattery();
                case "device": return a.Length > 0 && SystemState.DeviceConnected(a);
                case "screens":
                    int n;
                    return SystemState.ScreenCount() >= (int.TryParse(a, out n) && n > 0 ? n : 2);
                case "sound":
                    string sound = SystemState.SoundOutput();
                    return sound != null && sound.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        static IntPtr FindProgramWindow(string name)
        {
            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName(name))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
            if (pids.Count == 0) return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, l) =>
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (!pids.Contains(pid) || !Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r) || r.Right - r.Left < 50 || r.Bottom - r.Top < 50) return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static List<string> Names(string value, Context ctx)
        {
            var names = Scenario.Names(value).ToList();
            if (names.Count == 0 && !string.IsNullOrEmpty(ctx.LastProgram)) names.Add(ctx.LastProgram);
            return names;
        }

        static string ProgramName(string value)
        {
            string name = value.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        static double ParseNumber(string text, double fallback)
        {
            double v;
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        void ArrangeWindow(ScenarioStep step, Context ctx, CancellationToken token)
        {
            IntPtr target = WindowTarget(step.Value, ctx, token);
            if (!Closable(target)) return;

            switch (step.Option)
            {
                case "max": Native.ShowWindowAsync(target, 3); return;
                case "min": Native.ShowWindowAsync(target, 6); return;
                case "restore": Native.ShowWindowAsync(target, 9); return;
                case "front":
                    if (Native.IsIconic(target)) Native.ShowWindowAsync(target, 9);
                    Native.SetForegroundWindow(target);
                    return;
            }

            bool wasMax = Native.IsZoomed(target);
            if (wasMax || Native.IsIconic(target))
            {
                Native.ShowWindowAsync(target, 9);
                if (token.WaitHandle.WaitOne(150)) return;
            }
            Native.RECT outer;
            if (!Native.GetWindowRect(target, out outer)) return;
            Native.RECT frame = outer;
            Native.DwmGetWindowAttribute(target, 9, out frame, Marshal.SizeOf(typeof(Native.RECT)));
            int padL = frame.Left - outer.Left, padT = frame.Top - outer.Top, padR = outer.Right - frame.Right, padB = outer.Bottom - frame.Bottom;
            int w = frame.Right - frame.Left, h = frame.Bottom - frame.Top;

            var screen = System.Windows.Forms.Screen.FromHandle(target);
            var area = screen.WorkingArea;
            int x, y;
            switch (step.Option)
            {
                case "left": x = area.Left; y = area.Top; w = area.Width / 2; h = area.Height; break;
                case "right": x = area.Left + area.Width - area.Width / 2; y = area.Top; w = area.Width / 2; h = area.Height; break;
                case "top": x = area.Left; y = area.Top; w = area.Width; h = area.Height / 2; break;
                case "bottom": x = area.Left; y = area.Top + area.Height - area.Height / 2; w = area.Width; h = area.Height / 2; break;
                case "center":
                    w = Math.Min(w, area.Width);
                    h = Math.Min(h, area.Height);
                    x = area.Left + (area.Width - w) / 2;
                    y = area.Top + (area.Height - h) / 2;
                    break;
                case "next":
                    var all = System.Windows.Forms.Screen.AllScreens;
                    if (all.Length < 2) return;
                    int at = Array.IndexOf(all, screen);
                    var next = all[(at + 1) % all.Length].WorkingArea;
                    w = Math.Min(w, next.Width);
                    h = Math.Min(h, next.Height);
                    x = next.Left + Math.Max(0, Math.Min(frame.Left - area.Left, next.Width - w));
                    y = next.Top + Math.Max(0, Math.Min(frame.Top - area.Top, next.Height - h));
                    break;
                default: return;
            }
            Native.SetWindowPos(target, IntPtr.Zero, x - padL, y - padT, w + padL + padR, h + padT + padB, 0x0004 | Native.SWP_NOACTIVATE);
            if (wasMax && step.Option == "next") Native.ShowWindowAsync(target, 3);
        }

        static IntPtr WindowTarget(string spec, Context ctx, CancellationToken token)
        {
            string s = (spec ?? "").Trim();
            if (s == "active") return Native.GetForegroundWindow();
            if (s.StartsWith("program:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string name in Scenario.Names(s.Substring(8)))
                {
                    IntPtr w = FindProgramWindow(name);
                    if (w != IntPtr.Zero) return w;
                }
                return IntPtr.Zero;
            }
            WaitHandle.WaitAny(new[] { ctx.Watching, token.WaitHandle }, 3000);
            if (s.StartsWith("step:", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                IntPtr w;
                if (!int.TryParse(s.Substring(5), out n)) return IntPtr.Zero;
                lock (ctx.ByStep) if (ctx.ByStep.TryGetValue(n - 1, out w) && Native.IsWindow(w)) return w;
                return IntPtr.Zero;
            }
            IntPtr opened = ctx.Opened;
            return opened != IntPtr.Zero && Native.IsWindow(opened) ? opened : Native.GetForegroundWindow();
        }

        string Fill(string text, Context ctx)
        {
            text = (text ?? "").Replace("{buffer}", ctx.Buffer).Replace("{буфер}", ctx.Buffer);
            return host.Render(text);
        }

        static string ClipboardText()
        {
            for (int i = 0; i < 5; i++)
            {
                try { return System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : ""; }
                catch (ExternalException) { Thread.Sleep(80); }
            }
            return "";
        }

        static void SetClipboard(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (string.IsNullOrEmpty(text)) System.Windows.Forms.Clipboard.Clear();
                    else System.Windows.Forms.Clipboard.SetDataObject(text, true, 5, 40);
                    return;
                }
                catch (ExternalException) { Thread.Sleep(100); }
            }
        }

        void Buffer(ScenarioStep step, Context ctx)
        {
            switch (step.Option)
            {
                case "set": ctx.Buffer = Fill(step.Value, ctx); break;
                case "append": ctx.Buffer += Fill(step.Value, ctx); break;
                case "prepend": ctx.Buffer = Fill(step.Value, ctx) + ctx.Buffer; break;
                case "fromclip": ctx.Buffer = ClipboardText(); break;
                case "toclip": SetClipboard(ctx.Buffer); break;
                case "clear": ctx.Buffer = ""; break;
            }
        }

        bool Fetch(ScenarioStep step, Context ctx)
        {
            string[] lines = (step.Value ?? "").Split(new[] { '\n' }, 3);
            string url = Fill(lines[0], ctx).Trim();
            string first = lines.Length > 1 ? lines[1] : "";
            string second = lines.Length > 2 ? lines[2] : "";
            string[] opt = (step.Option ?? "").Split(':');
            string mode = opt[0].Length > 0 ? opt[0] : "all";
            string target = opt.Length > 1 ? opt[1] : "set";
            if (url.Length == 0) return true;
            if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "https://" + url;
            string page;
            try { page = WebText.Download(url); }
            catch (Exception ex)
            {
                host.Notice(Loc.T("Could not open the site: ", "Не удалось открыть сайт: ") + ex.Message);
                return false;
            }
            string found = WebText.Extract(page, mode, first, second);
            if (found == null)
            {
                host.Notice(Loc.T("Nothing found on the site, the scenario stopped", "На сайте ничего не нашлось, сценарий остановлен"));
                return false;
            }
            if (target == "append") ctx.Buffer += found;
            else ctx.Buffer = found;
            return true;
        }

        bool Click(ScenarioStep step, CancellationToken token)
        {
            var spot = ClickSpot.Parse(step.Value);
            int x, y;
            if (spot.Mode == "window")
            {
                IntPtr window = IntPtr.Zero;
                foreach (string name in Scenario.Names(spot.Program))
                {
                    window = FindProgramWindow(name);
                    if (window != IntPtr.Zero) break;
                }
                if (window == IntPtr.Zero)
                {
                    host.Notice(Loc.T("The window to click was not found, the scenario stopped", "Окно для клика не найдено, сценарий остановлен"));
                    return false;
                }
                if (Native.IsIconic(window)) Native.ShowWindowAsync(window, 9);
                Native.SetForegroundWindow(window);
                if (token.WaitHandle.WaitOne(250)) return false;
                Native.RECT r;
                if (Native.DwmGetWindowAttribute(window, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0 && !Native.GetWindowRect(window, out r)) return false;
                x = r.Left + (int)Math.Round(spot.X * (r.Right - r.Left));
                y = r.Top + (int)Math.Round(spot.Y * (r.Bottom - r.Top));
            }
            else
            {
                x = spot.ScreenX;
                y = spot.ScreenY;
            }
            WaitForModifiers(token);
            Native.POINT back;
            Native.GetCursorPos(out back);
            Native.SetCursorPos(x, y);
            if (token.WaitHandle.WaitOne(40)) return false;
            uint down = step.Option == "right" ? 0x0008u : 0x0002u;
            uint up = step.Option == "right" ? 0x0010u : 0x0004u;
            int times = step.Option == "double" ? 2 : 1;
            for (int i = 0; i < times; i++)
            {
                Native.mouse_event(down, 0, 0, 0, IntPtr.Zero);
                Native.mouse_event(up, 0, 0, 0, IntPtr.Zero);
                if (i + 1 < times) Thread.Sleep(60);
            }
            Thread.Sleep(60);
            if (step.Has("stay")) return true;
            Native.SetCursorPos(back.X, back.Y);
            return true;
        }

        void CopyText(ScenarioStep step, Context ctx)
        {
            string text = Fill(step.Value, ctx);
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetDataObject(text, true, 5, 40);
                    return;
                }
                catch (ExternalException) { Thread.Sleep(100); }
            }
        }

        public static string DefaultSoundName() { return Audio.DefaultName(); }

        static void ChangeVolume(ScenarioStep step)
        {
            switch (step.Option)
            {
                case "playpause": Hooks.PressKeys(0, 0xB3); return;
                case "next": Hooks.PressKeys(0, 0xB0); return;
                case "prev": Hooks.PressKeys(0, 0xB1); return;
            }
            var volume = Audio.Endpoint();
            if (volume == null) return;
            try
            {
                float level;
                volume.GetMasterVolumeLevelScalar(out level);
                float amount = (float)Math.Max(0, Math.Min(100, ParseNumber(step.Value, 10))) / 100f;
                Guid none = Guid.Empty;
                bool muted;
                switch (step.Option)
                {
                    case "set": volume.SetMasterVolumeLevelScalar(amount, ref none); volume.SetMute(false, ref none); break;
                    case "up": volume.SetMasterVolumeLevelScalar(Math.Min(1f, level + amount), ref none); volume.SetMute(false, ref none); break;
                    case "down": volume.SetMasterVolumeLevelScalar(Math.Max(0f, level - amount), ref none); break;
                    case "mute": volume.SetMute(true, ref none); break;
                    case "unmute": volume.SetMute(false, ref none); break;
                    case "togglemute":
                        volume.GetMute(out muted);
                        volume.SetMute(!muted, ref none);
                        break;
                }
            }
            finally { Marshal.ReleaseComObject(volume); }
        }

        bool RunCommand(ScenarioStep step, Context ctx, CancellationToken token)
        {
            string command = Fill(step.Value, ctx).Trim();
            if (command.Length == 0) return true;
            ProcessStartInfo info;
            if (step.Option == "ps")
            {
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded);
            }
            else info = new ProcessStartInfo("cmd.exe", "/d /c " + command);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            using (var p = Process.Start(info))
            {
                if (p == null) return true;
                while (!p.WaitForExit(200))
                    if (token.IsCancellationRequested) return false;
            }
            return true;
        }

        static class Audio
        {
            [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
            class MMDeviceEnumerator { }

            [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMMDeviceEnumerator
            {
                int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
                int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            }

            [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IMMDevice
            {
                int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
                int OpenPropertyStore(int access, out IPropertyStore store);
                int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
                int GetState(out int state);
            }

            [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IPropertyStore
            {
                int GetCount(out uint count);
                int GetAt(uint index, out PropertyKey key);
                int GetValue(ref PropertyKey key, out PropVariant value);
            }

            [StructLayout(LayoutKind.Sequential)]
            struct PropertyKey
            {
                public Guid Format;
                public int Id;
            }

            [StructLayout(LayoutKind.Explicit, Size = 24)]
            struct PropVariant
            {
                [FieldOffset(0)] public short Type;
                [FieldOffset(8)] public IntPtr Pointer;
            }

            [DllImport("ole32.dll")]
            static extern int PropVariantClear(ref PropVariant value);

            public static string DefaultName()
            {
                object enumerator = null;
                IMMDevice device = null;
                IPropertyStore store = null;
                try
                {
                    enumerator = new MMDeviceEnumerator();
                    if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return null;
                    if (device.OpenPropertyStore(0, out store) != 0 || store == null) return null;
                    var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                    PropVariant value;
                    if (store.GetValue(ref key, out value) != 0) return null;
                    try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
                    finally { PropVariantClear(ref value); }
                }
                catch (COMException) { return null; }
                catch (InvalidCastException) { return null; }
                finally
                {
                    if (store != null) Marshal.ReleaseComObject(store);
                    if (device != null) Marshal.ReleaseComObject(device);
                    if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                }
            }

            public static IAudioEndpointVolume Endpoint()
            {
                object enumerator = null;
                IMMDevice device = null;
                try
                {
                    enumerator = new MMDeviceEnumerator();
                    if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return null;
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    object result;
                    if (device.Activate(ref iid, 23, IntPtr.Zero, out result) != 0) return null;
                    return (IAudioEndpointVolume)result;
                }
                catch (COMException) { return null; }
                finally
                {
                    if (device != null) Marshal.ReleaseComObject(device);
                    if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                }
            }
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr notify);
            int UnregisterControlChangeNotify(IntPtr notify);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float levelDb, ref Guid context);
            int SetMasterVolumeLevelScalar(float level, ref Guid context);
            int GetMasterVolumeLevel(out float levelDb);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
            int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            int GetChannelVolumeLevel(uint channel, out float levelDb);
            int GetChannelVolumeLevelScalar(uint channel, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }

    static partial class WebText
    {
        public static string Download(string url)
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12 | (System.Net.SecurityProtocolType)12288; }
            catch (NotSupportedException) { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; }
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            request.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
            request.Accept = "text/html,application/xhtml+xml,text/plain,application/json;q=0.9,*/*;q=0.8";
            request.Headers["Accept-Language"] = "ru,en;q=0.8";
            using (var response = (System.Net.HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var memory = new System.IO.MemoryStream())
            {
                var buffer = new byte[16384];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0 && memory.Length < 8 * 1024 * 1024) memory.Write(buffer, 0, read);
                var bytes = memory.ToArray();
                Encoding encoding = Encoding.UTF8;
                try { if (!string.IsNullOrEmpty(response.CharacterSet)) encoding = Encoding.GetEncoding(response.CharacterSet.Trim('"')); }
                catch (ArgumentException) { }
                return encoding.GetString(bytes);
            }
        }
    }

    sealed class AppWatcher : IDisposable
    {
        readonly Native.WinEventProc proc;
        readonly Action<string, uint> onActivated;
        IntPtr hook;

        public AppWatcher(Action<string, uint> onActivated)
        {
            this.onActivated = onActivated;
            proc = OnEvent;
            hook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, 0);
        }

        void OnEvent(IntPtr h, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                if (pid == 0) return;
                IntPtr handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (handle == IntPtr.Zero) return;
                string name;
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (!Native.QueryFullProcessImageName(handle, 0, sb, ref size)) return;
                    name = System.IO.Path.GetFileNameWithoutExtension(sb.ToString());
                }
                finally { Native.CloseHandle(handle); }
                onActivated(name, pid);
            }
            catch { }
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }
    }
}
