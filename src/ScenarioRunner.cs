using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DeskHand
{
    interface IScenarioHost
    {
        string Render(string text);
        void Notice(string text);
        void Awake(string mode);
        void Pause(string mode);
        bool Confirm(string text);
        bool AskToRun(string name);
        void SetToggle(string key, string mode);
        void AfterTyping();
    }

    sealed partial class ScenarioRunner
    {
        readonly IScenarioHost host;
        readonly Dictionary<string, CancellationTokenSource> running = new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        static readonly uint selfPid = (uint)Process.GetCurrentProcess().Id;

        public ScenarioRunner(IScenarioHost host) { this.host = host; }

        public bool AnyRunning { get { lock (running) return running.Count > 0; } }

        public void StopAll()
        {
            lock (running)
                foreach (var c in running.Values) c.Cancel();
        }

        public void Run(Scenario scenario)
        {
            var sc = scenario.Clone();
            var cancel = new CancellationTokenSource();
            lock (running)
            {
                if (running.ContainsKey(sc.Name)) return;
                running[sc.Name] = cancel;
            }
            var thread = new Thread(() =>
            {
                try
                {
                    if (!sc.Ask || host.AskToRun(sc.Name)) Execute(sc, cancel.Token);
                }
                catch (Exception ex) { host.Notice(Loc.T("Scenario stopped: ", "Сценарий остановлен: ") + ex.Message); }
                finally
                {
                    lock (running) running.Remove(sc.Name);
                    cancel.Dispose();
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        sealed class Context
        {
            public string LastProgram;
            public IntPtr Opened;
            public int Index;
            public string Buffer = "";
            public readonly Dictionary<int, IntPtr> ByStep = new Dictionary<int, IntPtr>();
            public ManualResetEvent Watching = new ManualResetEvent(true);
        }

        void Execute(Scenario sc, CancellationToken token)
        {
            var ctx = new Context();
            var left = new Dictionary<int, int>();
            var steps = sc.Steps;
            Dictionary<int, int> elseOf, endOf, elseEnd;
            Blocks.Match(steps, out elseOf, out endOf, out elseEnd);
            for (int i = 0; i < steps.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                var step = steps[i];
                if (step.Kind == "repeat") continue;
                int next = i + 1;
                string kind;
                bool negate;
                int skip;
                switch (step.Kind)
                {
                    case "stop": return;
                    case "endif": break;
                    case "else":
                        int end;
                        if (elseEnd.TryGetValue(i, out end)) next = end + 1;
                        break;
                    case "if":
                        ScenarioCondition.Parse(step.Option, out kind, out negate, out skip);
                        if (Check(kind, step.Value, DateTime.Now, ctx.Buffer) == negate)
                        {
                            int target;
                            next = elseOf.TryGetValue(i, out target) ? target + 1 : endOf.TryGetValue(i, out target) ? target + 1 : steps.Count;
                        }
                        break;
                    case "condition":
                        ScenarioCondition.Parse(step.Option, out kind, out negate, out skip);
                        if (Check(kind, step.Value, DateTime.Now, ctx.Buffer) == negate)
                        {
                            if (skip <= 0) return;
                            next = i + 1 + skip;
                        }
                        break;
                    default:
                        ctx.Index = i;
                        if (!RunStep(step, ctx, token)) return;
                        if (step.Has("wait") && (step.Kind == "run" || step.Kind == "site" || step.Kind == "explorer"))
                            if (WaitHandle.WaitAny(new[] { ctx.Watching, token.WaitHandle }, 61000) == 1) return;
                        break;
                }
                int back = next - 1 < steps.Count ? LoopBack(steps, next - 1, left, ctx, token) : -1;
                if (back == -2) return;
                if (back >= 0) next = back;
                i = next - 1;
            }
        }

        public static void RepeatRange(ScenarioStep st, int at, out int from, out int to)
        {
            from = Math.Max(1, Math.Min(at, (int)ParseNumber(st.Value, 1))) - 1;
            int end;
            to = int.TryParse(st.FlagValue("to"), out end) ? Math.Max(from + 1, Math.Min(at, end)) - 1 : at - 1;
        }

        int LoopBack(List<ScenarioStep> steps, int end, Dictionary<int, int> left, Context ctx, CancellationToken token)
        {
            for (int r = end + 1; r < steps.Count; r++)
            {
                var st = steps[r];
                if (st.Kind != "repeat") continue;
                int from, to;
                RepeatRange(st, r, out from, out to);
                if (to != end || from < 0) continue;
                bool again;
                bool byCondition = RepeatSpec.Mode(st.Option) == "if";
                if (byCondition)
                {
                    string rk, ra;
                    bool rn;
                    RepeatSpec.ParseIf(st.Option, out rk, out rn, out ra);
                    again = Check(rk, ra, DateTime.Now, ctx.Buffer) != rn;
                }
                else
                {
                    int times = (int)ParseNumber(st.Option, 2);
                    int remaining;
                    if (!left.TryGetValue(r, out remaining)) remaining = times <= 0 ? -1 : times - 1;
                    again = remaining != 0;
                    if (again)
                    {
                        if (remaining > 0) remaining--;
                        left[r] = remaining;
                    }
                }
                if (!again)
                {
                    left.Remove(r);
                    continue;
                }
                var inner = new List<int>();
                foreach (int key in left.Keys)
                {
                    if (key == r) continue;
                    int f2, t2;
                    RepeatRange(steps[key], key, out f2, out t2);
                    if (f2 >= from && t2 <= end) inner.Add(key);
                }
                foreach (int key in inner) left.Remove(key);
                if (token.WaitHandle.WaitOne(byCondition ? 200 : 50)) return -2;
                return from;
            }
            return -1;
        }

        bool RunStep(ScenarioStep step, Context ctx, CancellationToken token)
        {
            switch (step.Kind)
            {
                case "site": OpenSite(step, ctx); break;
                case "run": RunProgram(step, ctx); break;
                case "explorer": OpenExplorer(step, ctx); break;
                case "wait":
                    return !token.WaitHandle.WaitOne(TimeSpan.FromSeconds(step.Seconds));
                case "close": Close(step, ctx, token); break;
                case "type":
                    WaitForModifiers(token);
                    Hooks.TypeText(Fill(step.Value, ctx));
                    host.AfterTyping();
                    break;
                case "keys":
                    uint mods;
                    int vk;
                    if (!KeyNames.TryParse(step.Value, out mods, out vk)) break;
                    WaitForModifiers(token);
                    Hooks.PressKeys(mods, vk);
                    host.AfterTyping();
                    break;
                case "awake": host.Awake(step.Option); break;
                case "pause": host.Pause(step.Option); break;
                case "setting": host.SetToggle(step.Option, step.Value); break;
                case "layout": Hooks.SwitchForegroundLayout(step.Option); break;
                case "notice": host.Notice(Fill(step.Value, ctx)); break;
                case "click": return Click(step, token);
                case "buffer": Buffer(step, ctx); break;
                case "fetch": return Fetch(step, ctx);
                case "lock": Native.LockWorkStation(); break;
                case "screenoff":
                    if (token.WaitHandle.WaitOne(600)) return false;
                    Native.PostMessage((IntPtr)0xFFFF, Native.WM_SYSCOMMAND, (IntPtr)0xF170, (IntPtr)2);
                    break;
                case "power": return Power(step);
                case "waitwindow": return WaitWindow(step, ctx, token);
                case "waitclose": return WaitClose(step, ctx, token);
                case "window": ArrangeWindow(step, ctx, token); break;
                case "clipboard": CopyText(step, ctx); break;
                case "volume": ChangeVolume(step); break;
                case "command": return RunCommand(step, ctx, token);
            }
            return true;
        }

        static void WaitForModifiers(CancellationToken token)
        {
            for (int i = 0; i < 30 && Hooks.ModifiersHeld(); i++)
                if (token.WaitHandle.WaitOne(50)) return;
            Hooks.ReleaseModifiers();
        }

        void OpenSite(ScenarioStep step, Context ctx)
        {
            string url = step.Value.Trim().Replace("\"", "");
            if (url.Length == 0) return;
            if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "https://" + url;
            string browser = DefaultBrowser();
            string name = browser == null ? null : Path.GetFileNameWithoutExtension(browser);
            var before = TopWindows();
            if (step.Option == "window" && browser != null) Process.Start(browser, "--new-window \"" + url + "\"");
            else Process.Start(url);
            ctx.LastProgram = name;
            WatchOpened(ctx, before, name, step.Has("wait") ? 60 : 8);
        }

        void RunProgram(ScenarioStep step, Context ctx)
        {
            string path = Environment.ExpandEnvironmentVariables(step.Value.Trim().Trim('"'));
            if (path.Length == 0) return;
            var before = TopWindows();
            if (path.StartsWith(@"shell:", StringComparison.OrdinalIgnoreCase))
            {
                using (Process.Start("explorer.exe", "\"" + path + "\"")) { }
                ctx.LastProgram = null;
                WatchOpened(ctx, before, null, step.Has("wait") ? 60 : 8);
                return;
            }
            var info = new ProcessStartInfo(path, step.Option) { UseShellExecute = true };
            if (File.Exists(path)) info.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            Process p = Process.Start(info);
            string name = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(path) : null;
            if (name == null && p != null)
            {
                try { name = p.ProcessName; }
                catch (InvalidOperationException) { }
            }
            if (p != null) p.Dispose();
            ctx.LastProgram = name;
            WatchOpened(ctx, before, name, step.Has("wait") ? 60 : 8);
        }

        void OpenExplorer(ScenarioStep step, Context ctx)
        {
            string path = Environment.ExpandEnvironmentVariables(step.Value.Trim().Trim('"'));
            var before = TopWindows();
            bool exists = path.Length > 0 && (File.Exists(path) || Directory.Exists(path));
            if (path.Length == 0) Process.Start("explorer.exe");
            else if (step.Option == "select" && exists) Process.Start("explorer.exe", "/select,\"" + path + "\"");
            else
            {
                using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
            }
            bool inExplorer = path.Length == 0 || step.Option == "select" || Directory.Exists(path);
            ctx.LastProgram = inExplorer ? "explorer" : null;
            WatchOpened(ctx, before, inExplorer ? "explorer" : null, step.Has("wait") ? 60 : 8);
        }

        static HashSet<IntPtr> TopWindows()
        {
            var set = new HashSet<IntPtr>();
            Native.EnumWindows((hwnd, l) =>
            {
                if (Native.IsWindowVisible(hwnd)) set.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return set;
        }

        static IntPtr NewWindow(HashSet<IntPtr> before, string expected)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, l) =>
            {
                if (before.Contains(hwnd) || !Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
                if (!Closable(hwnd) || !Matches(hwnd, expected)) return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static void WatchOpened(Context ctx, HashSet<IntPtr> before, string expected, int seconds)
        {
            ctx.Opened = IntPtr.Zero;
            var done = new ManualResetEvent(false);
            ctx.Watching = done;
            int index = ctx.Index;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (int i = 0; i < seconds * 10; i++)
                    {
                        IntPtr fresh = NewWindow(before, expected);
                        if (fresh != IntPtr.Zero)
                        {
                            ctx.Opened = fresh;
                            lock (ctx.ByStep) ctx.ByStep[index] = fresh;
                            return;
                        }
                        Thread.Sleep(100);
                    }
                    IntPtr last = Native.GetForegroundWindow();
                    if (expected != null && Matches(last, expected))
                    {
                        ctx.Opened = last;
                        lock (ctx.ByStep) ctx.ByStep[index] = last;
                    }
                }
                finally { done.Set(); }
            });
        }

        static bool Matches(IntPtr window, string expected)
        {
            uint pid;
            Native.GetWindowThreadProcessId(window, out pid);
            if (pid == selfPid) return false;
            return expected == null || string.Equals(ProcessName(pid), expected, StringComparison.OrdinalIgnoreCase);
        }

        void Close(ScenarioStep step, Context ctx, CancellationToken token)
        {
            string mode = step.Option;
            if (mode == "program" || mode == "kill")
            {
                var names = Scenario.Names(step.Value).ToList();
                if (names.Count == 0 && !string.IsNullOrEmpty(ctx.LastProgram)) names.Add(ctx.LastProgram);
                foreach (string name in names)
                {
                    if (name.Equals("DeskHand", StringComparison.OrdinalIgnoreCase)) continue;
                    if (mode == "kill" && !name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) KillProgram(name);
                    else CloseProgram(name);
                }
                return;
            }

            IntPtr target;
            if (mode == "active") target = Native.GetForegroundWindow();
            else
            {
                if (mode == "tab" && ctx.Opened == IntPtr.Zero) target = Native.GetForegroundWindow();
                else
                {
                    WaitHandle.WaitAny(new[] { ctx.Watching, token.WaitHandle }, 3000);
                    target = ctx.Opened;
                }
            }
            if (!Closable(target)) return;

            if (mode == "tab")
            {
                if (Native.GetForegroundWindow() != target)
                {
                    Native.SetForegroundWindow(target);
                    if (token.WaitHandle.WaitOne(200)) return;
                }
                if (Native.GetForegroundWindow() != target) return;
                WaitForModifiers(token);
                Hooks.PressKeys(Hotkey.ModControl, 0x57);
                return;
            }
            Native.PostMessage(target, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        static bool Closable(IntPtr window)
        {
            if (window == IntPtr.Zero || !Native.IsWindow(window)) return false;
            uint pid;
            Native.GetWindowThreadProcessId(window, out pid);
            if (pid == selfPid) return false;
            var cls = new StringBuilder(64);
            Native.GetClassName(window, cls, 64);
            string name = cls.ToString();
            return name != "Shell_TrayWnd" && name != "Shell_SecondaryTrayWnd" && name != "Progman" && name != "WorkerW";
        }

        static void CloseProgram(string name)
        {
            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName(name))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
            if (pids.Count == 0) return;
            var windows = new List<IntPtr>();
            Native.EnumWindows((hwnd, l) =>
            {
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pids.Contains(pid) && Native.IsWindowVisible(hwnd) && Native.GetWindow(hwnd, Native.GW_OWNER) == IntPtr.Zero && Closable(hwnd))
                    windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            foreach (IntPtr w in windows) Native.PostMessage(w, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        static void KillProgram(string name)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { if ((uint)p.Id != selfPid) p.Kill(); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally { p.Dispose(); }
            }
        }

        bool Power(ScenarioStep step)
        {
            string mode = step.Option;
            string question = mode == "shutdown" ? Loc.T("Shut down the computer?", "Выключить компьютер?")
                : mode == "restart" ? Loc.T("Restart the computer?", "Перезагрузить компьютер?")
                : Loc.T("Put the computer to sleep?", "Перевести компьютер в сон?");
            if (step.Value != "now" && !host.Confirm(question)) return false;
            if (mode == "shutdown" || mode == "restart")
            {
                var info = new ProcessStartInfo("shutdown.exe", mode == "shutdown" ? "/s /t 0" : "/r /t 0")
                {
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (Process.Start(info)) { }
                return false;
            }
            System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);
            return true;
        }

        static string ProcessName(uint pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? Path.GetFileNameWithoutExtension(sb.ToString()) : null;
            }
            finally { Native.CloseHandle(h); }
        }

        public static string DefaultBrowser()
        {
            try
            {
                string progId = null;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                    if (key != null) progId = key.GetValue("ProgId") as string;
                if (string.IsNullOrEmpty(progId)) return null;
                string command;
                using (var key = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
                    command = key == null ? null : key.GetValue("") as string;
                if (string.IsNullOrEmpty(command)) return null;
                command = command.Trim();
                string exe;
                if (command.StartsWith("\""))
                {
                    int end = command.IndexOf('"', 1);
                    exe = end > 0 ? command.Substring(1, end - 1) : command.Substring(1);
                }
                else
                {
                    int end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    exe = end > 0 ? command.Substring(0, end + 4) : command;
                }
                return File.Exists(exe) ? exe : null;
            }
            catch (System.Security.SecurityException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
