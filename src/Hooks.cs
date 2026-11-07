using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DeskHand
{
    sealed class Hooks : Native
    {
        static readonly IntPtr Magic = (IntPtr)0x42575254;

        const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11,
                  VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_SPACE = 0x20, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        readonly Engine engine;
        readonly HookProc kbProc, mouseProc;
        readonly WinEventProc foregroundProc;
        readonly byte[] keyState = new byte[256];
        readonly char[] chars = new char[8];
        readonly INPUT[] inputs = new INPUT[256];
        readonly StringBuilder sb = new StringBuilder(1024);
        readonly uint selfPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        readonly System.Windows.Forms.Timer holdTimer = new System.Windows.Forms.Timer { Interval = 200 };

        IntPtr kbHook, mouseHook, fgHook;
        IntPtr lastFg;
        uint fgThread;
        bool fgExcluded, fgIsDialog, spaceHeld, afterDeadKey;
        ContextReader reader;
        Config baseCfg;
        bool fgIsSettings;
        EventWaitHandle demoFocus;
        int lastHookTick;
        string fgName;
        int layoutSettleUntil;
        int shownLang = -1;
        int shownCaps = -1;

        public event Action LayoutsChanged;
        public event Action StateMayChange;
        public event Action<string> IndicatorText;
        public event Action<int, int> MouseButtonDown;
        public event Action<int, int> RightButtonUp;

        public string ForegroundProcess { get; private set; }

        public Hooks(Engine engine)
        {
            this.engine = engine;
            baseCfg = engine.Cfg;
            engine.ClipboardText = () => System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : "";
            kbProc = KeyboardProc;
            mouseProc = MouseProc;
            foregroundProc = OnForegroundEvent;
            holdTimer.Tick += delegate
            {
                holdTimer.Stop();
                if (!spaceHeld || GetForegroundWindow() != lastFg) return;
                var leave = engine.OnSpaceHeld();
                if (leave.Replace) Send(leave, 0);
            };
        }

        public bool Installed { get { return kbHook != IntPtr.Zero; } }

        public void Install()
        {
            if (Installed) return;
            IntPtr mod = GetModuleHandle(null);
            kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, kbProc, mod, 0);
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, mod, 0);
            fgHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundProc, 0, 0, 0);
            lastFg = IntPtr.Zero;
            lastHookTick = Environment.TickCount;
            shownCaps = GetKeyState(VK_CAPITAL) & 1;
            if (baseCfg.LayoutFix) Dictionaries.Preload();
        }

        public void Watch()
        {
            var info = new LASTINPUTINFO { cbSize = Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            bool alive = kbHook != IntPtr.Zero && mouseHook != IntPtr.Zero;
            if (alive && (!GetLastInputInfo(ref info) || unchecked((int)info.dwTime - lastHookTick) <= 2000)) return;
            IntPtr fg = lastFg;
            Uninstall();
            Install();
            lastFg = fg;
            engine.Reset(false);
        }

        public void SetConfig(Config cfg)
        {
            baseCfg = cfg;
            engine.Cfg = cfg;
            lastFg = IntPtr.Zero;
            if (cfg.LayoutFix && Installed) Dictionaries.Preload();
        }

        public void Uninstall()
        {
            if (kbHook != IntPtr.Zero) UnhookWindowsHookEx(kbHook);
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            if (fgHook != IntPtr.Zero) UnhookWinEvent(fgHook);
            kbHook = mouseHook = fgHook = IntPtr.Zero;
        }

        public void Invalidate() { lastFg = IntPtr.Zero; }

        bool ActiveHere(IntPtr fg)
        {
            if (fgIsSettings && (demoFocus == null || !demoFocus.WaitOne(0))) return false;
            return !fgExcluded && !(engine.Cfg.SkipFullscreen && IsFullscreen(fg));
        }

        bool ReadsAfterCursorMove { get { return engine.Cfg.Capitalize && engine.Cfg.CapitalizeAfterClick; } }

        void RequestContext(bool allowStartCap)
        {
            if (reader == null)
                reader = new ContextReader(SynchronizationContext.Current ?? new SynchronizationContext(), OnContext);
            reader.Request(engine.Version, allowStartCap);
        }

        void OnContext(int version, bool allowStartCap, CaretContext ctx)
        {
            if (version != engine.Version) return;
            if (ctx.Known || ctx.Password) engine.SetContext(ctx.Before, ctx.AtStart, ctx.Password, allowStartCap);
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            lastHookTick = Environment.TickCount;
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                if (msg != WM_MOUSEMOVE)
                {
                    try
                    {
                        HandleMouse(msg);
                        int x = Marshal.ReadInt32(lParam, 0), y = Marshal.ReadInt32(lParam, 4);
                        if (msg == WM_RBUTTONUP && RightButtonUp != null) RightButtonUp(x, y);
                        if ((msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN) && MouseButtonDown != null) MouseButtonDown(x, y);
                    }
                    catch { }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        void HandleMouse(int msg)
        {
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                engine.Reset(engine.Cfg.CapitalizeAfterClick);
            }
            else if (msg == WM_LBUTTONUP)
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != lastFg) OnForegroundChanged(fg, engine.Cfg.CapitalizeAfterClick);
                else if (ReadsAfterCursorMove && ActiveHere(fg)) RequestContext(true);
            }
        }

        void OnForegroundEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != lastFg) OnForegroundChanged(fg, engine.Cfg.CapitalizeOnWindowSwitch);
            }
            catch { }
        }

        void OnForegroundChanged(IntPtr fg, bool allowStartCap)
        {
            lastFg = fg;
            uint pid;
            fgThread = GetWindowThreadProcessId(fg, out pid);
            string name = ProcessName(pid);
            fgName = name;
            if (pid != selfPid) ForegroundProcess = name;
            fgExcluded = pid == selfPid || (name != null && baseCfg.IsExcluded(name));
            engine.Cfg = baseCfg.ForApp(name);
            engine.BothLayouts = HasLayout(Lang.Ru) && HasLayout(Lang.En);
            fgIsSettings = !fgExcluded && string.Equals(name, "DeskHand", StringComparison.OrdinalIgnoreCase);
            if (fgIsSettings && demoFocus == null)
            {
                EventWaitHandle h;
                if (EventWaitHandle.TryOpenExisting(Program.DemoEventName, out h)) demoFocus = h;
            }

            sb.Clear();
            GetClassName(fg, sb, 256);
            fgIsDialog = sb.ToString() == "#32770";
            bool cap = allowStartCap && !fgIsDialog;
            engine.Reset(cap);
            if (cap && engine.Cfg.Capitalize && ActiveHere(fg)) RequestContext(true);
            ApplyRememberedLayout(fg);
        }

        void ApplyRememberedLayout(IntPtr fg)
        {
            int current = (int)((long)GetKeyboardLayout(fgThread) & 0xFFFF);
            layoutSettleUntil = Environment.TickCount + 200;
            string saved;
            if (baseCfg.LayoutPerApp && fgName != null && !fgIsSettings && ActiveHere(fg)
                && baseCfg.AppLayouts.TryGetValue(fgName, out saved) && saved != current.ToString("X4"))
            {
                IntPtr target = FindLayoutById(saved);
                if (target != IntPtr.Zero)
                {
                    PostMessage(fg, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
                    layoutSettleUntil = Environment.TickCount + 700;
                    shownLang = current;
                    if (StateMayChange != null) StateMayChange();
                    return;
                }
            }
            shownLang = current;
        }

        void RememberLayout(IntPtr hkl)
        {
            if (!baseCfg.LayoutPerApp || fgName == null || fgIsSettings) return;
            if (unchecked(Environment.TickCount - layoutSettleUntil) < 0) return;
            string id = ((int)((long)hkl & 0xFFFF)).ToString("X4");
            string saved;
            if (baseCfg.AppLayouts.TryGetValue(fgName, out saved) && saved == id) return;
            baseCfg.AppLayouts[fgName] = id;
            if (LayoutsChanged != null) LayoutsChanged();
        }

        public void CheckIndicator()
        {
            int lang = (int)((long)GetKeyboardLayout(fgThread) & 0xFFFF);
            int caps = GetKeyState(VK_CAPITAL) & 1;
            bool langChanged = shownLang >= 0 && lang != shownLang;
            bool capsChanged = shownCaps >= 0 && caps != shownCaps;
            shownLang = lang;
            shownCaps = caps;
            if (!baseCfg.LayoutIndicator || IndicatorText == null || (!langChanged && !capsChanged)) return;
            if (!ActiveHere(GetForegroundWindow())) return;
            IndicatorText(capsChanged ? (caps != 0 ? "CAPS ON" : "CAPS OFF") : LangLabel(lang));
        }

        public void AfterExternalEdit()
        {
            engine.Reset(false);
            if (ReadsAfterCursorMove && ActiveHere(GetForegroundWindow())) RequestContext(true);
        }

        public static string LangLabel(int langId)
        {
            int primary = langId & 0x3FF;
            if (primary == 0x19) return "РУС";
            if (primary == 0x09) return "ENG";
            if (primary == 0x22) return "УКР";
            if (primary == 0x23) return "БЕЛ";
            try { return new System.Globalization.CultureInfo(langId).TwoLetterISOLanguageName.ToUpperInvariant(); }
            catch (System.Globalization.CultureNotFoundException) { return langId.ToString("X4"); }
        }

        public void SendPaste()
        {
            var keys = new System.Collections.Generic.List<INPUT>();
            if (Pressed(VK_MENU) || Pressed(VK_LWIN) || Pressed(VK_RWIN))
            {
                keys.Add(Key(0xFF, '\0', 0));
                keys.Add(Key(0xFF, '\0', KEYEVENTF_KEYUP));
            }
            foreach (int vk in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, VK_LWIN, VK_RWIN })
                if (Pressed(vk)) keys.Add(Key(vk, '\0', KEYEVENTF_KEYUP));
            keys.Add(Key(VK_CONTROL, '\0', 0));
            keys.Add(Key(0x56, '\0', 0));
            keys.Add(Key(0x56, '\0', KEYEVENTF_KEYUP));
            keys.Add(Key(VK_CONTROL, '\0', KEYEVENTF_KEYUP));
            SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public static void ReleaseModifiers()
        {
            var keys = new System.Collections.Generic.List<INPUT>();
            if (Pressed(VK_MENU) || Pressed(VK_LWIN) || Pressed(VK_RWIN))
            {
                keys.Add(Key(0xFF, '\0', 0));
                keys.Add(Key(0xFF, '\0', KEYEVENTF_KEYUP));
            }
            foreach (int vk in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, VK_LWIN, VK_RWIN })
                if (Pressed(vk)) keys.Add(Key(vk, '\0', KEYEVENTF_KEYUP));
            if (keys.Count > 0) SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public static bool ModifiersHeld()
        {
            foreach (int vk in new[] { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN })
                if (Pressed(vk)) return true;
            return false;
        }

        public static void TypeText(string text)
        {
            var keys = new System.Collections.Generic.List<INPUT>();
            foreach (char ch in text)
            {
                if (ch == '\r') continue;
                if (ch == '\n')
                {
                    keys.Add(Key(VK_SHIFT, '\0', 0));
                    keys.Add(Key(VK_RETURN, '\0', 0));
                    keys.Add(Key(VK_RETURN, '\0', KEYEVENTF_KEYUP));
                    keys.Add(Key(VK_SHIFT, '\0', KEYEVENTF_KEYUP));
                    continue;
                }
                keys.Add(Key(0, ch, KEYEVENTF_UNICODE));
                keys.Add(Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
            if (keys.Count > 0) SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public static void PressKeys(uint mods, int vk)
        {
            var down = new System.Collections.Generic.List<int>();
            if ((mods & Hotkey.ModWin) != 0) down.Add(VK_LWIN);
            if ((mods & Hotkey.ModControl) != 0) down.Add(VK_CONTROL);
            if ((mods & Hotkey.ModAlt) != 0) down.Add(VK_MENU);
            if ((mods & Hotkey.ModShift) != 0) down.Add(VK_SHIFT);
            bool extended = (vk >= 0x21 && vk <= 0x2E) || (vk >= 0xA6 && vk <= 0xB7) || vk == 0x5D;
            uint ext = extended ? KEYEVENTF_EXTENDEDKEY : 0;
            var keys = new System.Collections.Generic.List<INPUT>();
            foreach (int m in down) keys.Add(Key(m, '\0', 0));
            keys.Add(Key(vk, '\0', ext));
            keys.Add(Key(vk, '\0', ext | KEYEVENTF_KEYUP));
            for (int i = down.Count - 1; i >= 0; i--) keys.Add(Key(down[i], '\0', KEYEVENTF_KEYUP));
            SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SwitchForegroundLayout(string which)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            IntPtr hkl = which == "ru" ? FindLayout(Lang.Ru) : which == "en" ? FindLayout(Lang.En) : (IntPtr)1;
            if (hkl != IntPtr.Zero) PostMessage(fg, WM_INPUTLANGCHANGEREQUEST, which == "next" ? (IntPtr)2 : IntPtr.Zero, hkl);
        }

        IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            lastHookTick = Environment.TickCount;
            if (nCode >= 0)
            {
                try
                {
                    if (HandleKey((int)wParam, lParam)) return (IntPtr)1;
                }
                catch { }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        bool HandleKey(int msg, IntPtr lParam)
        {
            int vk = Marshal.ReadInt32(lParam, 0);
            int scan = Marshal.ReadInt32(lParam, 4);
            int flags = Marshal.ReadInt32(lParam, 8);
            IntPtr extra = Marshal.ReadIntPtr(lParam, 16);

            if ((flags & LLKHF_INJECTED) != 0 && (extra == Magic || engine.Cfg.IgnoreInjected)) return false;

            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            if (vk == VK_SPACE && !down)
            {
                spaceHeld = false;
                holdTimer.Stop();
            }
            if (!down)
            {
                if (StateMayChange != null && (vk == VK_SPACE || vk == VK_CAPITAL || vk == VK_LWIN || vk == VK_RWIN
                    || (vk >= VK_SHIFT && vk <= VK_MENU) || (vk >= 0xA0 && vk <= 0xA5))) StateMayChange();
                return false;
            }

            IntPtr fg = GetForegroundWindow();
            if (fg != lastFg) OnForegroundChanged(fg, engine.Cfg.CapitalizeOnWindowSwitch);

            if (!ActiveHere(fg))
            {
                engine.Reset(false);
                return false;
            }

            if (IsIgnoredKey(vk)) return false;

            IntPtr hkl = GetKeyboardLayout(fgThread);
            bool capsLock = (GetKeyState(VK_CAPITAL) & 1) != 0;
            engine.Lang = LangOf(hkl);
            engine.CapsLock = capsLock;

            bool ctrl = Pressed(VK_CONTROL), alt = Pressed(VK_MENU), shift = Pressed(VK_SHIFT);
            bool win = Pressed(VK_LWIN) || Pressed(VK_RWIN);
            bool altGr = ctrl && alt;

            if (vk == VK_RETURN)
            {
                var fix = engine.OnEnter();
                engine.Reset(engine.Cfg.CapitalizeAfterEnter);
                if (!fix.Replace) return false;
                Send(fix, VK_RETURN);
                return true;
            }
            if (win)
            {
                if (vk != VK_SPACE) engine.Reset(false);
                return false;
            }
            if ((ctrl || alt) && !altGr)
            {
                engine.Reset(false);
                if (ctrl && ReadsAfterCursorMove) RequestContext(true);
                return false;
            }
            if (vk == VK_BACK)
            {
                bool unseen = engine.BufferEmpty;
                var undo = engine.OnBackspace();
                if (undo.Replace)
                {
                    Send(undo, 0);
                    return true;
                }
                if (unseen && ReadsAfterCursorMove) RequestContext(true);
                return false;
            }
            if (vk == VK_TAB || (vk >= 0x21 && vk <= 0x28))
            {
                engine.Reset(false);
                if (ReadsAfterCursorMove) RequestContext(true);
                return false;
            }

            bool repeat = false;
            if (vk == VK_SPACE) { repeat = spaceHeld; spaceHeld = true; }

            Array.Clear(keyState, 0, 256);
            if (shift) keyState[VK_SHIFT] = 0x80;
            if (ctrl) keyState[VK_CONTROL] = 0x80;
            if (alt) keyState[VK_MENU] = 0x80;
            if (capsLock) keyState[VK_CAPITAL] = 1;

            int n = ToUnicodeEx((uint)vk, (uint)scan, keyState, chars, chars.Length, 4, hkl);
            if (n < 0) { afterDeadKey = true; return false; }
            bool wasDead = afterDeadKey;
            afterDeadKey = false;
            if (n == 0 || chars[0] < ' ') return false;
            if (n > 1)
            {
                for (int i = 0; i < n; i++) engine.OnChar(chars[i], 0, true, false);
                return false;
            }

            char c = chars[0];
            RememberLayout(hkl);
            bool capAllowed = char.IsLetter(c) && char.IsLower(c) && !wasDead && !IsPasswordFocus();

            var act = engine.OnChar(c, (long)GetTickCount64(), repeat, capAllowed);
            if (vk == VK_SPACE && !repeat && engine.WaitsForHeldSpace) holdTimer.Start();
            if (!act.Replace) return false;
            Send(act, 0);
            return true;
        }

        static Lang LangOf(IntPtr hkl)
        {
            int primary = (int)((long)hkl & 0x3FF);
            if (primary == 0x19 || primary == 0x22 || primary == 0x23) return Lang.Ru;
            if (primary == 0x09) return Lang.En;
            return Lang.Other;
        }

        void SwitchLayout(Lang lang)
        {
            IntPtr hkl = FindLayout(lang);
            if (hkl != IntPtr.Zero) PostMessage(lastFg, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        }

        static IntPtr FindLayoutById(string id)
        {
            var list = new IntPtr[32];
            int count = GetKeyboardLayoutList(list.Length, list);
            for (int i = 0; i < count; i++)
                if (((int)((long)list[i] & 0xFFFF)).ToString("X4") == id) return list[i];
            return IntPtr.Zero;
        }

        static bool HasLayout(Lang lang) { return FindLayout(lang) != IntPtr.Zero; }

        static IntPtr FindLayout(Lang lang)
        {
            var list = new IntPtr[32];
            int count = GetKeyboardLayoutList(list.Length, list);
            for (int i = 0; i < count; i++)
                if (LangOf(list[i]) == lang) return list[i];
            return IntPtr.Zero;
        }

        string ProcessName(uint pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                sb.Clear();
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return null;
                return Path.GetFileNameWithoutExtension(sb.ToString());
            }
            finally { CloseHandle(h); }
        }

        static bool IsFullscreen(IntPtr fg)
        {
            if (fg == IntPtr.Zero) return false;
            if ((GetWindowLong(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;
            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref mi)) return false;
            var m = mi.rcMonitor;
            if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return false;
            var csb = new StringBuilder(32);
            GetClassName(fg, csb, 32);
            string cls = csb.ToString();
            return cls != "Progman" && cls != "WorkerW";
        }

        bool IsPasswordFocus()
        {
            var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            if (!GetGUIThreadInfo(fgThread, ref gti) || gti.hwndFocus == IntPtr.Zero) return false;
            if ((GetWindowLong(gti.hwndFocus, GWL_STYLE) & ES_PASSWORD) == 0) return false;
            var csb = new StringBuilder(64);
            GetClassName(gti.hwndFocus, csb, 64);
            return csb.ToString().IndexOf("edit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Send(EngineAction act, int vkAfter)
        {
            int k = 0;
            int needed = (act.Backspaces + act.CaretLeft + act.CaretRight + act.DeleteRight + 9) * 2 + act.Text.Length * 4;
            INPUT[] inputs = needed <= this.inputs.Length ? this.inputs : new INPUT[needed];
            for (int i = 0; i < act.Backspaces; i++)
            {
                inputs[k++] = Key(VK_BACK, '\0', 0);
                inputs[k++] = Key(VK_BACK, '\0', KEYEVENTF_KEYUP);
            }
            foreach (char ch in act.Text)
            {
                if (ch == '\n')
                {
                    inputs[k++] = Key(VK_SHIFT, '\0', 0);
                    inputs[k++] = Key(VK_RETURN, '\0', 0);
                    inputs[k++] = Key(VK_RETURN, '\0', KEYEVENTF_KEYUP);
                    inputs[k++] = Key(VK_SHIFT, '\0', KEYEVENTF_KEYUP);
                    continue;
                }
                inputs[k++] = Key(0, ch, KEYEVENTF_UNICODE);
                inputs[k++] = Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
            }
            var heldModifiers = new System.Collections.Generic.List<int>();
            if (act.CaretLeft + act.CaretRight + act.DeleteRight > 0)
            {
                foreach (int vk in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
                    if (Pressed(vk)) heldModifiers.Add(vk);
                if (Pressed(VK_MENU))
                {
                    inputs[k++] = Key(0xFF, '\0', 0);
                    inputs[k++] = Key(0xFF, '\0', KEYEVENTF_KEYUP);
                }
                foreach (int vk in heldModifiers) inputs[k++] = Key(vk, '\0', KEYEVENTF_KEYUP);
            }
            for (int i = 0; i < act.CaretLeft; i++)
            {
                inputs[k++] = Key(0x25, '\0', KEYEVENTF_EXTENDEDKEY);
                inputs[k++] = Key(0x25, '\0', KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP);
            }
            for (int i = 0; i < act.CaretRight; i++)
            {
                inputs[k++] = Key(0x27, '\0', KEYEVENTF_EXTENDEDKEY);
                inputs[k++] = Key(0x27, '\0', KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP);
            }
            for (int i = 0; i < act.DeleteRight; i++)
            {
                inputs[k++] = Key(0x2E, '\0', KEYEVENTF_EXTENDEDKEY);
                inputs[k++] = Key(0x2E, '\0', KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP);
            }
            foreach (int vk in heldModifiers) inputs[k++] = Key(vk, '\0', 0);
            if (act.ToggleCapsLock)
            {
                inputs[k++] = Key(VK_CAPITAL, '\0', 0);
                inputs[k++] = Key(VK_CAPITAL, '\0', KEYEVENTF_KEYUP);
            }
            if (vkAfter != 0)
            {
                inputs[k++] = Key(vkAfter, '\0', 0);
                inputs[k++] = Key(vkAfter, '\0', KEYEVENTF_KEYUP);
            }
            SendInput((uint)k, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (act.SwitchLayout != Lang.Other) SwitchLayout(act.SwitchLayout);
            if ((act.SwitchLayout != Lang.Other || act.ToggleCapsLock) && StateMayChange != null) StateMayChange();
        }

        static INPUT Key(int vk, char ch, uint flags)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = ch, dwFlags = flags, dwExtraInfo = Magic };
            return input;
        }

        static bool Pressed(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        static bool IsIgnoredKey(int vk)
        {
            return (vk >= 0x10 && vk <= 0x14)
                || (vk >= 0x15 && vk <= 0x1F)
                || vk == 0x2C || vk == 0x2D || vk == 0x2E
                || vk == VK_LWIN || vk == VK_RWIN || vk == 0x5D || vk == 0x5F
                || (vk >= 0x70 && vk <= 0x87)
                || vk == 0x90 || vk == 0x91
                || (vk >= 0xA0 && vk <= 0xB7);
        }
    }
}
