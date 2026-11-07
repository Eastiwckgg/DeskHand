using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskHand
{
    sealed class ScenarioTriggers : IDisposable
    {
        readonly SynchronizationContext ui;
        readonly Action<Scenario> run;
        Config cfg = new Config();

        readonly System.Windows.Forms.Timer schedule = new System.Windows.Forms.Timer { Interval = 15000 };
        readonly System.Windows.Forms.Timer idle = new System.Windows.Forms.Timer { Interval = 5000 };
        readonly System.Windows.Forms.Timer sound = new System.Windows.Forms.Timer { Interval = 3000 };
        readonly System.Windows.Forms.Timer battery = new System.Windows.Forms.Timer { Interval = 60000 };
        readonly System.Windows.Forms.Timer deviceDelay = new System.Windows.Forms.Timer { Interval = 1500 };
        readonly System.Windows.Forms.Timer networkDelay = new System.Windows.Forms.Timer { Interval = 2500 };
        readonly HashSet<string> fired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, HashSet<uint>> seenPids = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Dictionary<int, Process>> tracked = new Dictionary<string, Dictionary<int, Process>>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> away = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppWatcher watcher;
        DeviceWindow devices;
        Dictionary<string, int> lastDevices;
        string lastSound, lastWifi;
        bool lastInternet, lastOnBattery;
        int lastBattery = -1, lastScreens;
        bool systemHooked, networkHooked;
        HashSet<string> events = new HashSet<string>();

        public ScenarioTriggers(SynchronizationContext ui, Action<Scenario> run)
        {
            this.ui = ui;
            this.run = run;
            schedule.Tick += delegate { CheckSchedule(); };
            idle.Tick += delegate { CheckIdle(); };
            sound.Tick += delegate { CheckSound(); };
            battery.Tick += delegate { CheckPower(); };
            deviceDelay.Tick += delegate { deviceDelay.Stop(); CheckDevices(); };
            networkDelay.Tick += delegate { networkDelay.Stop(); CheckNetwork(); };
        }

        public void Apply(Config config)
        {
            cfg = config;
            var list = cfg.Scenarios;
            schedule.Enabled = list.Exists(s => s.Time.Length > 0);
            idle.Enabled = list.Exists(s => s.Idle.Length > 0 && Durations.Parse(s.Idle) > 0);
            if (!idle.Enabled) away.Clear();

            var opened = new HashSet<string>(list.SelectMany(s => Scenario.Names(s.WhenOpened)), StringComparer.OrdinalIgnoreCase);
            var closed = new HashSet<string>(list.SelectMany(s => Scenario.Names(s.WhenClosed)), StringComparer.OrdinalIgnoreCase);
            foreach (string name in opened)
                if (!seenPids.ContainsKey(name)) seenPids[name] = new HashSet<uint>(Pids(name));
            foreach (string old in seenPids.Keys.ToList())
                if (!opened.Contains(old)) seenPids.Remove(old);
            foreach (string name in closed)
                if (!tracked.ContainsKey(name))
                {
                    tracked[name] = new Dictionary<int, Process>();
                    TrackAll(name);
                }
            foreach (string old in tracked.Keys.ToList())
                if (!closed.Contains(old)) Untrack(old);
            bool watch = opened.Count > 0 || closed.Count > 0;
            if (watch && watcher == null) watcher = new AppWatcher(OnActivated);
            if (!watch && watcher != null)
            {
                watcher.Dispose();
                watcher = null;
            }

            var now = new HashSet<string>(list.Where(s => s.Event.Length > 0).Select(s => s.Event));
            bool hadDevices = events.Contains("device-in") || events.Contains("device-out");
            events = now;
            bool wantDevices = Has("device-in", "device-out");
            if (wantDevices && devices == null)
            {
                lastDevices = SystemState.Devices();
                devices = new DeviceWindow(() => { deviceDelay.Stop(); deviceDelay.Start(); });
            }
            if (!wantDevices && devices != null)
            {
                devices.DestroyHandle();
                devices = null;
            }
            if (!hadDevices && wantDevices && lastDevices == null) lastDevices = SystemState.Devices();

            bool wantSound = Has("sound");
            if (wantSound && !sound.Enabled) lastSound = SystemState.SoundOutput();
            sound.Enabled = wantSound;

            bool wantSystem = Has("lock", "unlock", "wake", "ac-on", "ac-off", "battery-low", "display-on", "display-off");
            if (wantSystem && !systemHooked)
            {
                lastOnBattery = SystemState.OnBattery();
                lastBattery = SystemState.BatteryPercent();
                lastScreens = SystemState.ScreenCount();
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerMode;
                SystemEvents.DisplaySettingsChanged += OnDisplay;
                systemHooked = true;
            }
            if (!wantSystem && systemHooked) UnhookSystem();
            battery.Enabled = Has("battery-low");

            bool wantNetwork = Has("wifi-on", "wifi-off", "net-on", "net-off");
            if (wantNetwork && !networkHooked)
            {
                lastWifi = Has("wifi-on", "wifi-off") ? SystemState.WifiName() : null;
                lastInternet = SystemState.Internet();
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetwork;
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailability;
                networkHooked = true;
            }
            if (!wantNetwork && networkHooked) UnhookNetwork();
        }

        bool Has(params string[] kinds)
        {
            foreach (string k in kinds) if (events.Contains(k)) return true;
            return false;
        }

        void Fire(Func<Scenario, bool> match)
        {
            foreach (var sc in cfg.Scenarios.ToList())
                if (match(sc)) run(sc);
        }

        void FireEvent(string kind, Func<string, bool> argMatches)
        {
            Fire(sc => sc.Event == kind && argMatches(sc.EventArg.Trim()));
        }

        void CheckSchedule()
        {
            DateTime now = DateTime.Now;
            string stamp = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            if (fired.Count > 100) fired.RemoveWhere(k => !k.EndsWith("|" + stamp));
            Fire(sc => sc.DueAt(now) && fired.Add(sc.Name + "|" + stamp));
        }

        void CheckIdle()
        {
            var info = new Native.LASTINPUTINFO { cbSize = Marshal.SizeOf(typeof(Native.LASTINPUTINFO)) };
            if (!Native.GetLastInputInfo(ref info)) return;
            double idleSeconds = unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
            foreach (var sc in cfg.Scenarios.ToList())
            {
                double limit = Durations.Parse(sc.Idle);
                if (limit <= 0) continue;
                if (idleSeconds >= limit)
                {
                    if (away.Add(sc.Name) && sc.IdleEvent == "away") run(sc);
                }
                else if (away.Remove(sc.Name) && sc.IdleEvent == "back") run(sc);
            }
        }

        void OnActivated(string name, uint pid)
        {
            HashSet<uint> pids;
            if (name != null && seenPids.TryGetValue(name, out pids) && pids.Add(pid))
            {
                if (pids.Count > 64) pids.Clear();
                Fire(sc => sc.OpensWith(name));
            }
            Dictionary<int, Process> list;
            if (name != null && tracked.TryGetValue(name, out list) && !list.ContainsKey((int)pid)) Track(name, (int)pid);
        }

        static IEnumerable<uint> Pids(string name)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                uint id = (uint)p.Id;
                p.Dispose();
                yield return id;
            }
        }

        void TrackAll(string name)
        {
            foreach (uint pid in Pids(name).ToList()) Track(name, (int)pid);
        }

        void Track(string name, int pid)
        {
            Dictionary<int, Process> list;
            if (!tracked.TryGetValue(name, out list) || list.ContainsKey(pid)) return;
            Process p = null;
            try
            {
                p = Process.GetProcessById(pid);
                p.EnableRaisingEvents = true;
                p.Exited += delegate { ui.Post(delegate { OnExited(name, pid); }, null); };
                list[pid] = p;
            }
            catch (ArgumentException) { if (p != null) p.Dispose(); }
            catch (InvalidOperationException) { if (p != null) p.Dispose(); }
            catch (System.ComponentModel.Win32Exception) { if (p != null) p.Dispose(); }
        }

        void OnExited(string name, int pid)
        {
            Dictionary<int, Process> list;
            if (!tracked.TryGetValue(name, out list)) return;
            Process p;
            if (list.TryGetValue(pid, out p))
            {
                list.Remove(pid);
                p.Dispose();
            }
            if (SystemState.ProgramRunning(name))
            {
                TrackAll(name);
                return;
            }
            Fire(sc => sc.ClosesWith(name));
        }

        void Untrack(string name)
        {
            Dictionary<int, Process> list;
            if (!tracked.TryGetValue(name, out list)) return;
            foreach (var p in list.Values) p.Dispose();
            tracked.Remove(name);
        }

        void CheckDevices()
        {
            var now = SystemState.Devices();
            var before = lastDevices ?? now;
            lastDevices = now;
            var added = now.Where(d => { int n; return !before.TryGetValue(d.Key, out n) || n < d.Value; }).Select(d => d.Key).ToList();
            var removed = before.Where(d => { int n; return !now.TryGetValue(d.Key, out n) || n < d.Value; }).Select(d => d.Key).ToList();
            if (added.Count > 0) FireEvent("device-in", arg => arg.Length == 0 || added.Any(d => d.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0));
            if (removed.Count > 0) FireEvent("device-out", arg => arg.Length == 0 || removed.Any(d => d.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        void CheckSound()
        {
            string now = SystemState.SoundOutput();
            if (now == null || now == lastSound) return;
            lastSound = now;
            FireEvent("sound", arg => arg.Length == 0 || now.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        void OnNetwork(object sender, EventArgs e) { ui.Post(delegate { networkDelay.Stop(); networkDelay.Start(); }, null); }

        void OnNetworkAvailability(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e) { OnNetwork(sender, e); }

        void CheckNetwork()
        {
            if (Has("wifi-on", "wifi-off"))
            {
                string wifi = SystemState.WifiName();
                string before = lastWifi;
                lastWifi = wifi;
                if (wifi != null && !string.Equals(wifi, before, StringComparison.OrdinalIgnoreCase))
                    FireEvent("wifi-on", arg => arg.Length == 0 || arg.Equals(wifi, StringComparison.OrdinalIgnoreCase));
                if (before != null && !string.Equals(wifi, before, StringComparison.OrdinalIgnoreCase))
                    FireEvent("wifi-off", arg => arg.Length == 0 || arg.Equals(before, StringComparison.OrdinalIgnoreCase));
            }
            bool internet = SystemState.Internet();
            if (internet != lastInternet)
            {
                lastInternet = internet;
                FireEvent(internet ? "net-on" : "net-off", arg => true);
            }
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            string kind = e.Reason == SessionSwitchReason.SessionLock ? "lock" : e.Reason == SessionSwitchReason.SessionUnlock ? "unlock" : null;
            if (kind != null) ui.Post(delegate { FireEvent(kind, arg => true); }, null);
        }

        void OnPowerMode(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) ui.Post(delegate { FireEvent("wake", arg => true); }, null);
            else if (e.Mode == PowerModes.StatusChange) ui.Post(delegate { CheckPower(); }, null);
        }

        void CheckPower()
        {
            bool onBattery = SystemState.OnBattery();
            if (onBattery != lastOnBattery)
            {
                lastOnBattery = onBattery;
                FireEvent(onBattery ? "ac-off" : "ac-on", arg => true);
            }
            int percent = SystemState.BatteryPercent();
            if (percent < 0) return;
            int before = lastBattery;
            lastBattery = percent;
            FireEvent("battery-low", arg =>
            {
                int limit;
                if (!int.TryParse(arg, out limit) || limit <= 0) limit = 20;
                return onBattery && percent <= limit && (before < 0 || before > limit);
            });
        }

        void OnDisplay(object sender, EventArgs e)
        {
            ui.Post(delegate
            {
                int now = SystemState.ScreenCount();
                if (now == lastScreens) return;
                string kind = now > lastScreens ? "display-on" : "display-off";
                lastScreens = now;
                FireEvent(kind, arg => true);
            }, null);
        }

        void UnhookSystem()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerMode;
            SystemEvents.DisplaySettingsChanged -= OnDisplay;
            systemHooked = false;
        }

        void UnhookNetwork()
        {
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetwork;
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailability;
            networkHooked = false;
        }

        public void Dispose()
        {
            foreach (var t in new[] { schedule, idle, sound, battery, deviceDelay, networkDelay }) t.Dispose();
            if (watcher != null) watcher.Dispose();
            if (devices != null) devices.DestroyHandle();
            foreach (string name in tracked.Keys.ToList()) Untrack(name);
            if (systemHooked) UnhookSystem();
            if (networkHooked) UnhookNetwork();
        }

        sealed class DeviceWindow : NativeWindow
        {
            readonly Action changed;

            public DeviceWindow(Action changed)
            {
                this.changed = changed;
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x219)
                {
                    int code = (int)m.WParam;
                    if (code == 0x0007 || code == 0x8000 || code == 0x8004) changed();
                }
                base.WndProc(ref m);
            }
        }
    }

    sealed class ConfirmPopup : Form
    {
        bool answer;

        ConfirmPopup(string message, string yes, string no)
        {
            bool dark = Program.IsDark();
            Color back = dark ? Program.Rgb(0x2C2C2C) : Program.Rgb(0xFBFBFB);
            Color fore = dark ? Program.Rgb(0xE6E6E6) : Program.Rgb(0x1F1F1F);
            Color dim = dark ? Program.Rgb(0x9A9A9A) : Program.Rgb(0x6A6A6A);
            Color accent = dark ? Program.Rgb(0xC9BE98) : Program.Rgb(0x5F6148);
            Color accentText = dark ? Program.Rgb(0x26261F) : Color.White;
            Color plain = dark ? Program.Rgb(0x3A3A3A) : Program.Rgb(0xE9E9E9);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = back;
            Font = new Font("Segoe UI", 10f);
            float scale = DeviceDpi / 96f;
            Padding = new Padding((int)(16 * scale));
            Size = new Size((int)(360 * scale), (int)(150 * scale));
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - (int)(16 * scale), area.Bottom - Height - (int)(16 * scale));

            var title = new Label { Text = "DeskHand", ForeColor = dim, AutoSize = true, Location = new Point(Padding.Left, Padding.Top), Font = new Font("Segoe UI", 9f) };
            var text = new Label
            {
                Text = message, ForeColor = fore, Font = new Font("Segoe UI Semibold", 11f),
                Location = new Point(Padding.Left, Padding.Top + (int)(22 * scale)),
                Size = new Size(Width - Padding.Horizontal, (int)(48 * scale))
            };
            var ok = Flat(yes, accent, accentText);
            var cancel = Flat(no, plain, fore);
            int bw = (int)(120 * scale), bh = (int)(32 * scale);
            ok.Size = cancel.Size = new Size(bw, bh);
            ok.Location = new Point(Width - Padding.Right - bw * 2 - (int)(8 * scale), Height - Padding.Bottom - bh);
            cancel.Location = new Point(Width - Padding.Right - bw, Height - Padding.Bottom - bh);
            ok.Click += delegate { answer = true; Close(); };
            cancel.Click += delegate { Close(); };
            Controls.Add(title);
            Controls.Add(text);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            var timeout = new System.Windows.Forms.Timer { Interval = 60000 };
            timeout.Tick += delegate { timeout.Stop(); Close(); };
            Shown += delegate
            {
                timeout.Start();
                Activate();
                ok.Focus();
            };
            FormClosed += delegate { timeout.Dispose(); };
        }

        static Button Flat(string text, Color back, Color fore)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9.5f) };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Program.IsDark() ? Program.Rgb(0x454545) : Program.Rgb(0xD5D5D5)))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        public static bool Ask(string message, string yes, string no)
        {
            using (var popup = new ConfirmPopup(message, yes, no))
            {
                popup.ShowDialog();
                return popup.answer;
            }
        }
    }
}
