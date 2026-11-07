using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DeskHand
{
    sealed class Config
    {
        public const int CurrentVersion = 2;

        public static readonly string[] Features =
        {
            "Capitalize", "DoubleSpacePeriod", "AutoSpace", "AutoHyphen", "SmartQuotes",
            "AutoBrackets", "CapsLockFix", "Typos", "EnglishI", "LayoutFix", "Snippets"
        };

        public bool Enabled = true;
        public string UiLanguage = "en";
        public string Theme = "dark";
        int fileVersion = 1;
        public bool Capitalize = true;
        public bool DoubleSpacePeriod = true;
        public int DoubleSpaceIntervalMs = 600;
        public bool AutoSpace = true;
        public bool AutoHyphen = true;
        public bool SmartQuotes = true;
        public bool AutoBrackets = false;
        public string BracketExit = "hold";
        public bool CapitalizeInBrackets = false;
        public bool LayoutPerApp = false;
        public bool LayoutIndicator = true;
        public string IndicatorSize = "small";
        public bool PinOnTitleBar = true;
        public string PinHotkey = "";
        public string PlainPasteHotkey = "";
        public string KeepAwakeHotkey = "";
        public string KeepAwakeUntil = "";
        public int KeepAwakeHours = 1;
        public bool KeepAwakeScreen = false;
        public Dictionary<string, string> AppLayouts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool CapsLockFix = true;
        public bool Typos = true;
        public bool EnglishI = true;
        public bool LayoutFix = true;
        public bool Snippets = true;
        public bool CapitalizeAfterEnter = true;
        public bool CapitalizeOnWindowSwitch = true;
        public bool CapitalizeAfterClick = true;
        public bool CapitalizeAfterEllipsis = false;
        public bool LangRussian = true;
        public bool LangEnglish = true;
        public bool LangOther = false;
        public bool SkipFullscreen = true;
        public bool IgnoreInjected = true;
        public string PauseHotkey = "Ctrl+Alt+B";
        public HashSet<string> LayoutIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> CustomTypos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludeApps = List(
            "cmd, powershell, pwsh, WindowsTerminal, conhost, OpenConsole, wsl, bash, mintty, Code, Cursor, Windsurf, devenv, " +
            "idea64, pycharm64, webstorm64, rider64, clion64, goland64, studio64, sublime_text, notepad++, KeePass, KeePassXC, " +
            "1Password, Bitwarden, mstsc, vmconnect, vmware, VirtualBoxVM, Ableton*, FL64, reaper, Figma, Photoshop, " +
            "Illustrator, AfterFX, Adobe Premiere Pro, Resolve, blender, krita, obs64");
        public HashSet<string> Abbreviations = List(
            "т.е, т.к, т.н, т.ч, напр, ср, им, ул, г, гг, кв, стр, рис, e.g, i.e, vs, mr, mrs, ms, dr, prof, fig");

        public Dictionary<string, HashSet<string>> AppRules = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SnippetList = DefaultSnippets();
        public List<Scenario> Scenarios = new List<Scenario>();

        public static Config Load(string path)
        {
            var cfg = new Config();
            if (!File.Exists(path))
            {
                try { cfg.Save(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return cfg;
            }

            string section = "main";
            bool snippetsSeen = false;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line == ";" || line.StartsWith("; ") || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    section = line.Trim('[', ']').Trim().ToLowerInvariant();
                    if (section == "snippets" && !snippetsSeen)
                    {
                        snippetsSeen = true;
                        cfg.SnippetList.Clear();
                    }
                    if (section == "scenario") cfg.Scenarios.Add(new Scenario());
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (section == "apps") cfg.AppRules[key] = List(val);
                else if (section == "snippets") cfg.SnippetList[key] = Unescape(val);
                else if (section == "corrections") cfg.CustomTypos[key] = Unescape(val);
                else if (section == "layouts") cfg.AppLayouts[key] = val;
                else if (section == "scenario") cfg.Scenarios[cfg.Scenarios.Count - 1].SetValue(key, val);
                else if (section == "main") cfg.SetValue(key, val);
            }
            cfg.Upgrade(path);
            return cfg;
        }

        void Upgrade(string path)
        {
            if (fileVersion == CurrentVersion) return;
            try
            {
                string backup = path + ".v" + fileVersion + ".bak";
                File.Copy(path, backup, true);
                foreach (string old in Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".v*.bak"))
                    if (!string.Equals(old, backup, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
                if (fileVersion > CurrentVersion) return;
                if (fileVersion < 2)
                {
                    AutoBrackets = false;
                    CapitalizeInBrackets = false;
                    LayoutPerApp = false;
                }
                Save(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void SetValue(string key, string val)
        {
            switch (key.ToLowerInvariant())
            {
                case "enabled": Enabled = Bool(val); break;
                case "language": UiLanguage = val.ToLowerInvariant() == "ru" ? "ru" : "en"; break;
                case "settingsversion": int.TryParse(val, out fileVersion); break;
                case "theme":
                    string theme = val.ToLowerInvariant();
                    Theme = theme == "light" || theme == "system" ? theme : "dark";
                    break;
                case "doublespaceintervalms": int.TryParse(val, out DoubleSpaceIntervalMs); break;
                case "capitalizeafterenter": CapitalizeAfterEnter = Bool(val); break;
                case "capitalizeonwindowswitch": CapitalizeOnWindowSwitch = Bool(val); break;
                case "capitalizeafterclick": CapitalizeAfterClick = Bool(val); break;
                case "capitalizeafterellipsis": CapitalizeAfterEllipsis = Bool(val); break;
                case "langrussian": LangRussian = Bool(val); break;
                case "langenglish": LangEnglish = Bool(val); break;
                case "langother": LangOther = Bool(val); break;
                case "skipfullscreen": SkipFullscreen = Bool(val); break;
                case "ignoreinjected": IgnoreInjected = Bool(val); break;
                case "pausehotkey": PauseHotkey = val; break;
                case "pinhotkey": PinHotkey = val; break;
                case "plainpastehotkey": PlainPasteHotkey = val; break;
                case "keepawakehotkey": KeepAwakeHotkey = val; break;
                case "keepawakeuntil": KeepAwakeUntil = val; break;
                case "keepawakehours": int.TryParse(val, out KeepAwakeHours); break;
                case "keepawakescreen": KeepAwakeScreen = Bool(val); break;
                case "bracketexit": BracketExit = val.ToLowerInvariant() == "double" ? "double" : "hold"; break;
                case "indicatorsize": IndicatorSize = val.ToLowerInvariant(); break;
                case "capitalizeinbrackets": CapitalizeInBrackets = Bool(val); break;
                case "layoutperapp": LayoutPerApp = Bool(val); break;
                case "layoutindicator": LayoutIndicator = Bool(val); break;
                case "pinontitlebar": PinOnTitleBar = Bool(val); break;
                case "layoutignore": LayoutIgnore = List(val); break;
                case "excludeapps": ExcludeApps = List(val); break;
                case "abbreviations": Abbreviations = List(val); break;
                default:
                    foreach (string f in Features)
                        if (f.Equals(key, StringComparison.OrdinalIgnoreCase)) SetFeature(f, Bool(val));
                    break;
            }
        }

        public static readonly string[] Toggles =
        {
            "Capitalize", "DoubleSpacePeriod", "AutoSpace", "AutoHyphen", "SmartQuotes", "AutoBrackets", "CapsLockFix", "Typos", "EnglishI", "Snippets",
            "CapitalizeAfterEnter", "CapitalizeOnWindowSwitch", "CapitalizeAfterClick", "CapitalizeAfterEllipsis", "CapitalizeInBrackets",
            "LayoutFix", "LayoutPerApp", "LayoutIndicator", "LangRussian", "LangEnglish", "LangOther",
            "PinOnTitleBar", "KeepAwakeScreen", "SkipFullscreen", "IgnoreInjected"
        };

        public bool? GetToggle(string key)
        {
            foreach (string f in Features) if (f == key) return GetFeature(f);
            switch (key)
            {
                case "CapitalizeAfterEnter": return CapitalizeAfterEnter;
                case "CapitalizeOnWindowSwitch": return CapitalizeOnWindowSwitch;
                case "CapitalizeAfterClick": return CapitalizeAfterClick;
                case "CapitalizeAfterEllipsis": return CapitalizeAfterEllipsis;
                case "CapitalizeInBrackets": return CapitalizeInBrackets;
                case "LayoutPerApp": return LayoutPerApp;
                case "LayoutIndicator": return LayoutIndicator;
                case "LangRussian": return LangRussian;
                case "LangEnglish": return LangEnglish;
                case "LangOther": return LangOther;
                case "PinOnTitleBar": return PinOnTitleBar;
                case "KeepAwakeScreen": return KeepAwakeScreen;
                case "SkipFullscreen": return SkipFullscreen;
                case "IgnoreInjected": return IgnoreInjected;
            }
            return null;
        }

        public void SetToggle(string key, bool on)
        {
            foreach (string f in Features) if (f == key) { SetFeature(f, on); return; }
            switch (key)
            {
                case "CapitalizeAfterEnter": CapitalizeAfterEnter = on; break;
                case "CapitalizeOnWindowSwitch": CapitalizeOnWindowSwitch = on; break;
                case "CapitalizeAfterClick": CapitalizeAfterClick = on; break;
                case "CapitalizeAfterEllipsis": CapitalizeAfterEllipsis = on; break;
                case "CapitalizeInBrackets": CapitalizeInBrackets = on; break;
                case "LayoutPerApp": LayoutPerApp = on; break;
                case "LayoutIndicator": LayoutIndicator = on; break;
                case "LangRussian": LangRussian = on; break;
                case "LangEnglish": LangEnglish = on; break;
                case "LangOther": LangOther = on; break;
                case "PinOnTitleBar": PinOnTitleBar = on; break;
                case "KeepAwakeScreen": KeepAwakeScreen = on; break;
                case "SkipFullscreen": SkipFullscreen = on; break;
                case "IgnoreInjected": IgnoreInjected = on; break;
            }
        }

        public bool GetFeature(string f)
        {
            switch (f)
            {
                case "Capitalize": return Capitalize;
                case "DoubleSpacePeriod": return DoubleSpacePeriod;
                case "AutoSpace": return AutoSpace;
                case "AutoHyphen": return AutoHyphen;
                case "SmartQuotes": return SmartQuotes;
                case "AutoBrackets": return AutoBrackets;
                case "CapsLockFix": return CapsLockFix;
                case "Typos": return Typos;
                case "EnglishI": return EnglishI;
                case "LayoutFix": return LayoutFix;
                case "Snippets": return Snippets;
            }
            return false;
        }

        public void SetFeature(string f, bool on)
        {
            switch (f)
            {
                case "Capitalize": Capitalize = on; break;
                case "DoubleSpacePeriod": DoubleSpacePeriod = on; break;
                case "AutoSpace": AutoSpace = on; break;
                case "AutoHyphen": AutoHyphen = on; break;
                case "SmartQuotes": SmartQuotes = on; break;
                case "AutoBrackets": AutoBrackets = on; break;
                case "CapsLockFix": CapsLockFix = on; break;
                case "Typos": Typos = on; break;
                case "EnglishI": EnglishI = on; break;
                case "LayoutFix": LayoutFix = on; break;
                case "Snippets": Snippets = on; break;
            }
        }

        public Config ForApp(string processName)
        {
            HashSet<string> off;
            if (processName == null || !AppRules.TryGetValue(processName, out off) || off.Count == 0) return this;
            var copy = (Config)MemberwiseClone();
            foreach (string f in off) copy.SetFeature(f, false);
            return copy;
        }

        public bool IsExcluded(string processName)
        {
            if (ExcludeApps.Contains(processName)) return true;
            foreach (string e in ExcludeApps)
                if (e.EndsWith("*") && processName.StartsWith(e.Substring(0, e.Length - 1), StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public bool LangEnabled(Lang lang)
        {
            return lang == Lang.Ru ? LangRussian : lang == Lang.En ? LangEnglish : LangOther;
        }

        public bool KeepAwakeActive(DateTime now)
        {
            if (KeepAwakeUntil.Length == 0) return false;
            if (KeepAwakeUntil == "forever") return true;
            DateTime until;
            return DateTime.TryParse(KeepAwakeUntil, CultureInfo.InvariantCulture, DateTimeStyles.None, out until) && until > now;
        }

        public void StartKeepAwake(DateTime now)
        {
            KeepAwakeUntil = KeepAwakeHours <= 0 ? "forever" : now.AddHours(KeepAwakeHours).ToString("s", CultureInfo.InvariantCulture);
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; DeskHand settings. It is easier to change them in the settings window (click the tray icon).");
            sb.AppendLine("; 1 = on, 0 = off.");
            sb.AppendLine();
            sb.AppendLine("[Main]");
            Line(sb, "Version of this file. The app updates it by itself", "SettingsVersion", CurrentVersion.ToString());
            Line(sb, "Working or paused", "Enabled", Enabled);
            Line(sb, "Color theme: dark, light or system", "Theme", Theme);
            Line(sb, "Interface language: en or ru", "Language", UiLanguage);
            Line(sb, "Start sentences with a capital letter", "Capitalize", Capitalize);
            Line(sb, "Double space -> period and space", "DoubleSpacePeriod", DoubleSpacePeriod);
            Line(sb, "Max milliseconds between the two spaces", "DoubleSpaceIntervalMs", DoubleSpaceIntervalMs.ToString());
            Line(sb, "Spaces around punctuation: \"hello ,world\" -> \"hello, world\"", "AutoSpace", AutoSpace);
            Line(sb, "Russian hyphens: \"кое как\" -> \"кое-как\"", "AutoHyphen", AutoHyphen);
            Line(sb, "Russian quotes «» in the Russian layout", "SmartQuotes", SmartQuotes);
            Line(sb, "Close brackets and quotes automatically", "AutoBrackets", AutoBrackets);
            Line(sb, "How to leave an auto-closed bracket: hold (hold Space) or double (two spaces)", "BracketExit", BracketExit);
            Line(sb, "Capital letter right after an opening bracket", "CapitalizeInBrackets", CapitalizeInBrackets);
            Line(sb, "Remember the keyboard layout for each app", "LayoutPerApp", LayoutPerApp);
            Line(sb, "Show the layout and Caps Lock next to the mouse when they change", "LayoutIndicator", LayoutIndicator);
            Line(sb, "Indicator size: small, medium or large", "IndicatorSize", IndicatorSize);
            Line(sb, "Offer a keep-on-top button when you right-click a title bar", "PinOnTitleBar", PinOnTitleBar);
            Line(sb, "Hotkey to keep the active window on top. Empty = no hotkey", "PinHotkey", PinHotkey);
            Line(sb, "Hotkey to paste without formatting. Empty = no hotkey", "PlainPasteHotkey", PlainPasteHotkey);
            Line(sb, "Hotkey to keep the computer awake. Empty = no hotkey", "KeepAwakeHotkey", KeepAwakeHotkey);
            Line(sb, "Keep the computer awake until this time. Empty = off, forever = until turned off", "KeepAwakeUntil", KeepAwakeUntil);
            Line(sb, "How many hours to keep awake when turned on. 0 = until turned off", "KeepAwakeHours", KeepAwakeHours.ToString());
            Line(sb, "Keep the screen on as well", "KeepAwakeScreen", KeepAwakeScreen);
            Line(sb, "Accidental Caps Lock: \"hELLO\" -> \"Hello\"", "CapsLockFix", CapsLockFix);
            Line(sb, "Common typos: \"teh\" -> \"the\"", "Typos", Typos);
            Line(sb, "\"i\" -> \"I\" in English", "EnglishI", EnglishI);
            Line(sb, "Wrong keyboard layout: \"ghbdtn\" -> \"привет\"", "LayoutFix", LayoutFix);
            Line(sb, "Text shortcuts (list in the [Snippets] section)", "Snippets", Snippets);
            Line(sb, "Capital after Enter", "CapitalizeAfterEnter", CapitalizeAfterEnter);
            Line(sb, "Capital after switching to another window", "CapitalizeOnWindowSwitch", CapitalizeOnWindowSwitch);
            Line(sb, "Capital after a mouse click, if the cursor is at the start of a sentence", "CapitalizeAfterClick", CapitalizeAfterClick);
            Line(sb, "Capital after an ellipsis", "CapitalizeAfterEllipsis", CapitalizeAfterEllipsis);
            Line(sb, "Work in the Russian layout (also Ukrainian and Belarusian)", "LangRussian", LangRussian);
            Line(sb, "Work in the English layout", "LangEnglish", LangEnglish);
            Line(sb, "Work in other layouts", "LangOther", LangOther);
            Line(sb, "Do nothing in full-screen apps", "SkipFullscreen", SkipFullscreen);
            Line(sb, "Ignore input typed by other programs", "IgnoreInjected", IgnoreInjected);
            Line(sb, "Hotkey to pause and resume, for example Ctrl+Alt+B. Empty = no hotkey", "PauseHotkey", PauseHotkey);
            Line(sb, "Words the keyboard layout fix never touches", "LayoutIgnore", Join(LayoutIgnore));
            Line(sb, "Programs to leave alone (.exe name; * at the end matches anything)", "ExcludeApps", Join(ExcludeApps));
            Line(sb, "Abbreviations not followed by a capital (without the last period)", "Abbreviations", Join(Abbreviations));

            sb.AppendLine();
            sb.AppendLine("; Per-app rules: program = features turned off in it.");
            sb.AppendLine("; Feature names: " + string.Join(", ", Features));
            sb.AppendLine("[Apps]");
            foreach (var r in AppRules) sb.AppendLine(r.Key + " = " + Join(r.Value));

            sb.AppendLine();
            sb.AppendLine("; Remembered keyboard layouts: program = language id.");
            sb.AppendLine("[Layouts]");
            foreach (var l in AppLayouts) sb.AppendLine(l.Key + " = " + l.Value);

            sb.AppendLine();
            sb.AppendLine("; Your own corrections: wrong = right.");
            sb.AppendLine("[Corrections]");
            foreach (var c in CustomTypos) sb.AppendLine(c.Key + " = " + Escape(c.Value));

            sb.AppendLine();
            sb.AppendLine("; Text shortcuts: shortcut = text. Write a line break as \\n.");
            sb.AppendLine("[Snippets]");
            foreach (var s in SnippetList) sb.AppendLine(s.Key + " = " + Escape(s.Value));

            sb.AppendLine();
            sb.AppendLine("; Scenarios. Each [Scenario] section is one scenario, its steps run one after another.");
            sb.AppendLine("; Step = kind | option | value. Kinds: " + string.Join(", ", ScenarioStep.Kinds));
            foreach (var sc in Scenarios)
            {
                sb.AppendLine("[Scenario]");
                sb.AppendLine("Name = " + Escape(sc.Name));
                sb.AppendLine("Hotkey = " + sc.Hotkey);
                sb.AppendLine("Tray = " + (sc.InTray ? "1" : "0"));
                sb.AppendLine("Startup = " + (sc.OnStartup ? "1" : "0"));
                if (sc.Time.Length > 0) sb.AppendLine("Time = " + sc.Time);
                if (sc.Days != "1111111") sb.AppendLine("Days = " + sc.Days);
                if (sc.WhenOpened.Length > 0) sb.AppendLine("WhenOpened = " + sc.WhenOpened);
                if (sc.WhenClosed.Length > 0) sb.AppendLine("WhenClosed = " + sc.WhenClosed);
                if (sc.Idle.Length > 0)
                {
                    sb.AppendLine("Idle = " + sc.Idle);
                    sb.AppendLine("IdleEvent = " + sc.IdleEvent);
                }
                if (sc.Event.Length > 0)
                {
                    sb.AppendLine("Event = " + sc.Event);
                    if (sc.EventArg.Length > 0) sb.AppendLine("EventArg = " + Escape(sc.EventArg));
                }
                if (sc.Ask) sb.AppendLine("Ask = 1");
                foreach (var st in sc.Steps)
                    sb.AppendLine("Step = " + st.Kind + " | " + st.Option.Replace("|", "/") + (st.Flags.Length > 0 || st.Value.TrimStart().StartsWith("~") ? " | ~" + st.Flags.Replace("|", "/") : "") + " | " + Escape(st.Value));
                sb.AppendLine();
            }

            string temp = path + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(true));
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException)
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                File.Delete(temp);
            }
        }

        static Dictionary<string, string> DefaultSnippets()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            d["brb"] = "be right back";
            d["спс"] = "спасибо";
            return d;
        }

        static void Line(StringBuilder sb, string comment, string key, bool value) { Line(sb, comment, key, value ? "1" : "0"); }

        static void Line(StringBuilder sb, string comment, string key, string value)
        {
            sb.AppendLine();
            sb.AppendLine("; " + comment);
            sb.AppendLine(key + "=" + value);
        }

        static string Join(IEnumerable<string> items) { return string.Join(", ", items.ToArray()); }

        public static string Escape(string s)
        {
            string e = s.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\n", "\\n");
            int lead = 0;
            while (lead < e.Length && e[lead] == ' ') lead++;
            if (lead == e.Length) return string.Concat(Enumerable.Repeat("\\s", lead));
            int trail = 0;
            while (trail < e.Length && e[e.Length - 1 - trail] == ' ') trail++;
            return string.Concat(Enumerable.Repeat("\\s", lead)) + e.Substring(lead, e.Length - lead - trail) + string.Concat(Enumerable.Repeat("\\s", trail));
        }

        public static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 's' ? ' ' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static bool Bool(string v) { return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); }

        public static HashSet<string> List(string v)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in v.Split(','))
            {
                string p = part.Trim();
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 4);
                if (p.Length > 0) set.Add(p);
            }
            return set;
        }
    }

    static class Durations
    {
        public static double Parse(string text)
        {
            string number, unit;
            Split(text, out number, out unit);
            double v;
            if (!double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0) return 0;
            double seconds = unit == "h" ? v * 3600 : unit == "m" ? v * 60 : v;
            return Math.Min(seconds, 7 * 86400);
        }

        public static void Split(string text, out string number, out string unit)
        {
            string t = (text ?? "").Trim().ToLowerInvariant();
            unit = "s";
            if (t.EndsWith("h")) unit = "h";
            else if (t.EndsWith("m")) unit = "m";
            number = unit != "s" || t.EndsWith("s") ? t.Substring(0, Math.Max(0, t.Length - 1)).Trim() : t;
        }

        public static string Join(string number, string unit)
        {
            number = (number ?? "").Trim();
            return number.Length == 0 || unit == "s" ? number : number + unit;
        }
    }

    static class ScenarioCondition
    {
        public static void Parse(string option, out string kind, out bool negate, out int skip)
        {
            string o = (option ?? "").Trim();
            string tail = "stop";
            int colon = o.IndexOf(':');
            if (colon >= 0)
            {
                tail = o.Substring(colon + 1).Trim();
                o = o.Substring(0, colon).Trim();
            }
            negate = o.EndsWith("!");
            kind = negate ? o.Substring(0, o.Length - 1) : o;
            if (kind.Length == 0) kind = "running";
            skip = 0;
            if (tail.StartsWith("skip"))
            {
                int n;
                skip = int.TryParse(tail.Substring(4), out n) && n > 0 ? n : 1;
            }
        }

        public static string Format(string kind, bool negate, int skip)
        {
            return kind + (negate ? "!" : "") + ":" + (skip > 0 ? "skip" + skip : "stop");
        }

        public static bool InTimeRange(string range, DateTime now)
        {
            string[] parts = (range ?? "").Split('-');
            TimeSpan from, to;
            if (parts.Length != 2 || !TryTime(parts[0], out from) || !TryTime(parts[1], out to)) return false;
            var t = now.TimeOfDay;
            return from <= to ? t >= from && t < to : t >= from || t < to;
        }

        public static bool TryTime(string text, out TimeSpan time)
        {
            return TimeSpan.TryParseExact((text ?? "").Trim(), new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out time) && time.TotalHours < 24;
        }

        public static bool TextMatches(string arg, string clipboard, string buffer)
        {
            string[] parts = (arg ?? "").Split(new[] { '\n' }, 3);
            string source = parts.Length > 0 ? parts[0] : "clipboard";
            string mode = parts.Length > 1 ? parts[1] : "contains";
            string wanted = parts.Length > 2 ? parts[2] : "";
            string have = (source == "buffer" ? buffer : clipboard) ?? "";
            switch (mode)
            {
                case "exact": return have == wanted;
                case "nocase": return string.Equals(have.Trim(), wanted.Trim(), StringComparison.CurrentCultureIgnoreCase);
                case "empty": return have.Trim().Length == 0;
            }
            return wanted.Length == 0 || have.IndexOf(wanted, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        public static bool OnDay(string days, DateTime now)
        {
            int day = ((int)now.DayOfWeek + 6) % 7;
            return days != null && days.Length == 7 && days[day] == '1';
        }
    }

    sealed class ClickSpot
    {
        public double X = 0.5, Y = 0.5;
        public string Mode = "window";
        public string Program = "";
        public int ScreenX, ScreenY;

        public static ClickSpot Parse(string value)
        {
            var spot = new ClickSpot();
            string[] p = (value ?? "").Split(';');
            double d;
            int n;
            if (p.Length > 0 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out d)) spot.X = Math.Max(0, Math.Min(1, d));
            if (p.Length > 1 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d)) spot.Y = Math.Max(0, Math.Min(1, d));
            if (p.Length > 2 && p[2] == "screen") spot.Mode = "screen";
            if (p.Length > 3) spot.Program = p[3].Trim();
            if (p.Length > 4 && int.TryParse(p[4], out n)) spot.ScreenX = n;
            if (p.Length > 5 && int.TryParse(p[5], out n)) spot.ScreenY = n;
            return spot;
        }

        public bool Recorded { get { return Mode == "screen" ? ScreenX != 0 || ScreenY != 0 : Program.Length > 0; } }

        public override string ToString()
        {
            return X.ToString("0.0000", CultureInfo.InvariantCulture) + ";" + Y.ToString("0.0000", CultureInfo.InvariantCulture) + ";" + Mode + ";" +
                   Program.Replace(";", "") + ";" + ScreenX + ";" + ScreenY;
        }
    }

    static partial class WebText
    {
        public static string Strip(string html)
        {
            if (html == null) return "";
            string s = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style|noscript|svg)[^>]*>[\s\S]*?</\1\s*>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"<!--[\s\S]*?-->", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*(br|/p|/div|/li|/tr|/h[1-6]|/title)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"<[^>]+>", " ");
            s = System.Net.WebUtility.HtmlDecode(s);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[ \t\r\f\v\u00A0]+", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, @" ?\n[ \n]*", "\n");
            return s.Trim();
        }

        public static string Extract(string page, string mode, string first, string second)
        {
            string text = Strip(page);
            switch (mode)
            {
                case "between":
                    if (string.IsNullOrEmpty(first)) return null;
                    int a = text.IndexOf(first, StringComparison.CurrentCultureIgnoreCase);
                    if (a < 0) return null;
                    int from = a + first.Length;
                    int b;
                    if (string.IsNullOrEmpty(second))
                    {
                        b = text.IndexOf('\n', from);
                        if (b < 0) b = text.Length;
                    }
                    else
                    {
                        b = text.IndexOf(second, from, StringComparison.CurrentCultureIgnoreCase);
                        if (b < 0) return null;
                    }
                    string between = text.Substring(from, b - from).Trim();
                    return between.Length == 0 ? null : between;
                case "regex":
                    try
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(text, first ?? "", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                        if (!m.Success) m = System.Text.RegularExpressions.Regex.Match(page ?? "", first ?? "", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                        if (!m.Success) return null;
                        return (m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value).Trim();
                    }
                    catch (ArgumentException) { return null; }
                    catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return null; }
            }
            return text.Length == 0 ? null : text;
        }
    }

    static class Blocks
    {
        public static void Match(List<ScenarioStep> steps, out Dictionary<int, int> elseOf, out Dictionary<int, int> endOf, out Dictionary<int, int> elseEnd)
        {
            elseOf = new Dictionary<int, int>();
            endOf = new Dictionary<int, int>();
            elseEnd = new Dictionary<int, int>();
            var open = new Stack<int>();
            for (int i = 0; i < steps.Count; i++)
            {
                string k = steps[i].Kind;
                if (k == "if") open.Push(i);
                else if (k == "else" && open.Count > 0 && !elseOf.ContainsKey(open.Peek())) elseOf[open.Peek()] = i;
                else if (k == "endif" && open.Count > 0)
                {
                    int start = open.Pop();
                    endOf[start] = i;
                    int e;
                    if (elseOf.TryGetValue(start, out e)) elseEnd[e] = i;
                }
            }
            while (open.Count > 0)
            {
                int start = open.Pop();
                endOf[start] = steps.Count - 1;
                int e;
                if (elseOf.TryGetValue(start, out e)) elseEnd[e] = steps.Count - 1;
            }
        }

        public static int[] Depths(List<ScenarioStep> steps)
        {
            var depth = new int[steps.Count];
            int d = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                string k = steps[i].Kind;
                if (k == "if") { depth[i] = d; d++; }
                else if (k == "else") depth[i] = Math.Max(0, d - 1);
                else if (k == "endif") { d = Math.Max(0, d - 1); depth[i] = d; }
                else depth[i] = d;
            }
            return depth;
        }
    }

    static class RepeatSpec
    {
        public static string Mode(string option)
        {
            string o = (option ?? "").Trim();
            if (o.StartsWith("while:", StringComparison.OrdinalIgnoreCase) || o.StartsWith("if:", StringComparison.OrdinalIgnoreCase)) return "if";
            return o == "0" ? "forever" : "count";
        }

        public static void ParseIf(string option, out string kind, out bool negate, out string arg)
        {
            string o = (option ?? "").Trim();
            kind = "running";
            negate = false;
            arg = "";
            if (o.StartsWith("while:", StringComparison.OrdinalIgnoreCase))
            {
                arg = o.Substring(6).Trim();
                return;
            }
            if (!o.StartsWith("if:", StringComparison.OrdinalIgnoreCase)) return;
            o = o.Substring(3);
            int colon = o.IndexOf(':');
            string k = colon >= 0 ? o.Substring(0, colon) : o;
            arg = colon >= 0 ? o.Substring(colon + 1) : "";
            negate = k.EndsWith("!");
            kind = negate ? k.Substring(0, k.Length - 1) : k;
            if (kind.Length == 0) kind = "running";
        }

        public static string FormatIf(string kind, bool negate, string arg)
        {
            return "if:" + kind + (negate ? "!" : "") + ":" + (arg ?? "").Replace("|", "/");
        }
    }

    sealed class ScenarioStep
    {
        public static readonly string[] Kinds =
        {
            "site", "run", "explorer", "command", "waitwindow", "waitclose", "window", "close", "wait", "repeat", "condition", "if", "else", "endif", "stop", "click", "buffer", "fetch",
            "type", "keys", "clipboard", "volume", "awake", "pause", "setting", "layout", "notice", "lock", "screenoff", "power"
        };

        public string Kind = "wait";
        public string Option = "";
        public string Value = "";
        public string Flags = "";

        public string FlagValue(string name)
        {
            foreach (string f in Flags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (f.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return f.Substring(name.Length + 1);
            return "";
        }

        public void SetFlagValue(string name, string value)
        {
            var list = Flags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(f => !f.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrEmpty(value)) list.Add(name + ":" + value.Replace(" ", ""));
            Flags = string.Join(" ", list.ToArray());
        }

        public bool Has(string flag) { return (" " + Flags + " ").IndexOf(" " + flag + " ", StringComparison.OrdinalIgnoreCase) >= 0; }

        public void Set(string flag, bool on)
        {
            var list = Flags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(f => !f.Equals(flag, StringComparison.OrdinalIgnoreCase)).ToList();
            if (on) list.Add(flag);
            Flags = string.Join(" ", list.ToArray());
        }

        public ScenarioStep Clone() { return (ScenarioStep)MemberwiseClone(); }

        public static ScenarioStep Parse(string text)
        {
            string[] parts = text.Split(new[] { '|' }, 3);
            string kind = parts[0].Trim().ToLowerInvariant();
            if (Array.IndexOf(Kinds, kind) < 0) return null;
            string rest = parts.Length > 2 ? parts[2] : "";
            string flags = "";
            int bar = rest.IndexOf('|');
            if (rest.TrimStart().StartsWith("~") && bar >= 0)
            {
                flags = rest.Substring(0, bar).Trim().Substring(1).Trim();
                rest = rest.Substring(bar + 1);
            }
            return new ScenarioStep
            {
                Kind = kind,
                Option = parts.Length > 1 ? parts[1].Trim() : "",
                Value = Config.Unescape(rest.Trim()),
                Flags = flags
            };
        }

        public double Seconds { get { return Durations.Parse(Value); } }
    }

    sealed class Scenario
    {
        public string Name = "";
        public string Hotkey = "";
        public bool InTray = true;
        public bool OnStartup = false;
        public bool Ask = false;
        public string Time = "";
        public string Days = "1111111";
        public string WhenOpened = "";
        public string WhenClosed = "";
        public string Idle = "";
        public string IdleEvent = "away";
        public string Event = "";
        public string EventArg = "";
        public List<ScenarioStep> Steps = new List<ScenarioStep>();

        public bool DueAt(DateTime now)
        {
            TimeSpan at;
            if (Time.Length == 0 || !ScenarioCondition.TryTime(Time, out at)) return false;
            return ScenarioCondition.OnDay(Days, now) && now.Hour == at.Hours && now.Minute == at.Minutes;
        }

        public bool OpensWith(string program) { return NameIn(WhenOpened, program); }

        public bool ClosesWith(string program) { return NameIn(WhenClosed, program); }

        public static IEnumerable<string> Names(string list)
        {
            foreach (string part in (list ?? "").Split(','))
            {
                string p = part.Trim();
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 4);
                if (p.Length > 0) yield return p;
            }
        }

        static bool NameIn(string list, string program)
        {
            if (program == null) return false;
            foreach (string p in Names(list))
                if (p.Equals(program, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public Scenario Clone()
        {
            var copy = (Scenario)MemberwiseClone();
            copy.Steps = Steps.Select(s => s.Clone()).ToList();
            return copy;
        }

        public void SetValue(string key, string val)
        {
            switch (key.ToLowerInvariant())
            {
                case "name": Name = Config.Unescape(val); break;
                case "hotkey": Hotkey = val; break;
                case "tray": InTray = Config.Bool(val); break;
                case "startup": OnStartup = Config.Bool(val); break;
                case "ask": Ask = Config.Bool(val); break;
                case "time": Time = val; break;
                case "days": Days = val.Length == 7 && val.All(c => c == '0' || c == '1') ? val : "1111111"; break;
                case "whenopened": WhenOpened = val; break;
                case "whenclosed": WhenClosed = val; break;
                case "idle": Idle = val; break;
                case "idleevent": IdleEvent = val == "back" ? "back" : "away"; break;
                case "event": Event = val.ToLowerInvariant(); break;
                case "eventarg": EventArg = Config.Unescape(val); break;
                case "step":
                    var step = ScenarioStep.Parse(val);
                    if (step != null) Steps.Add(step);
                    break;
            }
        }
    }
}
