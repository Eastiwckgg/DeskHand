using System;
using System.Collections.Generic;
using System.Text;

namespace DeskHand
{
    struct EngineAction
    {
        public int Backspaces;
        public string Text;
        public bool ToggleCapsLock;
        public Lang SwitchLayout;
        public int CaretLeft, CaretRight, DeleteRight;
        public bool Replace { get { return Text != null; } }
    }

    sealed class Engine
    {
        const int MaxBuffer = 64;

        readonly StringBuilder buf = new StringBuilder();
        bool startCap;
        bool truncated;
        bool userOverride;
        bool secure;
        int autoCapIndex = -1;
        long lastSpaceMs = long.MinValue / 2;
        string undoScreen, undoOriginal;
        Lang undoLayout;
        readonly List<char> closers = new List<char>();
        bool swallowHeldSpace;

        public Config Cfg;
        public Func<DateTime> Now = () => DateTime.Now;
        public Func<string> ClipboardText = () => "";
        public Lang Lang = Lang.Ru;
        public bool CapsLock;
        public bool BothLayouts = true;

        public Engine(Config cfg) { Cfg = cfg; }

        public int Version { get; private set; }

        public string Buffer { get { return buf.ToString(); } }
        public bool BufferEmpty { get { return buf.Length == 0; } }

        public void Reset(bool capitalizeNext)
        {
            buf.Clear();
            startCap = capitalizeNext;
            truncated = false;
            userOverride = false;
            secure = false;
            autoCapIndex = -1;
            lastSpaceMs = long.MinValue / 2;
            closers.Clear();
            swallowHeldSpace = false;
            ClearUndo();
            Version++;
        }

        public void SetContext(string before, bool atStart, bool password, bool allowStartCap)
        {
            Reset(false);
            int nl = before.LastIndexOfAny(new[] { '\n', '\r', '\v', (char)0x2028, (char)0x2029, (char)0xFFFC });
            if (nl >= 0)
            {
                before = before.Substring(nl + 1);
                atStart = true;
            }
            if (before.Length > MaxBuffer)
            {
                before = before.Substring(before.Length - MaxBuffer);
                atStart = false;
            }
            buf.Append(before);
            startCap = atStart && allowStartCap;
            truncated = !atStart;
            secure = password;
        }

        public bool CapPending
        {
            get { return Cfg.Capitalize && !secure && !userOverride && !CapsLock && ShouldCapitalize(); }
        }

        bool Active { get { return !secure && Cfg.LangEnabled(Lang); } }

        public EngineAction OnChar(char c, long nowMs, bool repeat, bool capAllowed)
        {
            Version++;
            ClearUndo();
            if (swallowHeldSpace && repeat && c == ' ')
            {
                var swallowed = default(EngineAction);
                swallowed.Text = "";
                return swallowed;
            }
            swallowHeldSpace = false;
            if (!Active)
            {
                buf.Append(c);
                Trim();
                return default(EngineAction);
            }

            string before = buf.ToString();
            var act = default(EngineAction);
            bool autoCapOnly = false;
            char closer = '\0';

            if (c == ' ' && TryLeaveBracket(nowMs, repeat))
            {
                act.Backspaces = 1;
                act.Text = "";
                act.CaretRight = 1;
                userOverride = false;
                Trim();
                return act;
            }

            if (c == ' ' && TryDoubleSpace(nowMs, repeat))
            {
            }
            else
            {
                if (Cfg.AutoSpace && IsTightPunct(c)) RemoveSpaceBefore();

                char outChar = c;
                if (c == '"' && Cfg.SmartQuotes && Lang == Lang.Ru) outChar = Quote();

                if (!repeat && IsBoundary(c)) FixWord(c == ' ' ? Boundary.Space : Boundary.Punct, ref act);

                if (Cfg.AutoBrackets && TryStepOverCloser(outChar, before, ref act))
                {
                    userOverride = false;
                    Trim();
                    return act;
                }

                if (Cfg.AutoSpace && char.IsLetter(c)) AddSpaceAfterPunct(c);
                if (Cfg.AutoSpace && c == '(') AddSpaceBeforeBracket();
                if (Cfg.AutoBrackets && !repeat) closer = PairFor(c, outChar);

                if (char.IsLetter(outChar) && char.IsLower(outChar) && capAllowed && CapPending)
                {
                    autoCapOnly = buf.ToString() == before;
                    buf.Append(char.ToUpper(outChar));
                    autoCapIndex = buf.Length - 1;
                }
                else buf.Append(outChar);
            }
            if (c == ' ') lastSpaceMs = nowMs;
            userOverride = false;

            if (closer != '\0')
            {
                closers.Add(closer);
                Diff(before, buf.ToString(), ref act);
                act.Text += closer;
                act.CaretLeft = 1;
                Trim();
                return act;
            }

            Finish(before, before + c, ref act, !autoCapOnly);
            return act;
        }

        public bool WaitsForHeldSpace
        {
            get
            {
                int n = buf.Length;
                return Active && Cfg.AutoBrackets && Cfg.BracketExit == "hold" && closers.Count > 0
                    && n >= 2 && buf[n - 1] == ' ' && !IsSpace(buf[n - 2]);
            }
        }

        public EngineAction OnSpaceHeld()
        {
            var act = default(EngineAction);
            if (!WaitsForHeldSpace) return act;
            Version++;
            ClearUndo();
            TryLeaveBracket(0, true);
            act.Backspaces = 1;
            act.Text = "";
            act.CaretRight = 1;
            userOverride = false;
            Trim();
            return act;
        }

        public EngineAction OnEnter()
        {
            var act = default(EngineAction);
            if (!Active) return act;
            string before = buf.ToString();
            FixWord(Boundary.Enter, ref act);
            Finish(before, before, ref act, false);
            return act;
        }

        public EngineAction OnBackspace()
        {
            Version++;
            lastSpaceMs = long.MinValue / 2;
            var act = default(EngineAction);

            if (undoScreen != null)
            {
                buf.Length -= Math.Min(buf.Length, undoScreen.Length);
                buf.Append(undoOriginal);
                Diff(undoScreen, undoOriginal, ref act);
                act.SwitchLayout = undoLayout;
                if (undoLayout != Lang.Other) Lang = undoLayout;
                ClearUndo();
                autoCapIndex = -1;
                Trim();
                return act;
            }

            if (closers.Count > 0 && buf.Length > 0 && OpenerOf(closers[closers.Count - 1]) == buf[buf.Length - 1])
            {
                buf.Length -= 1;
                closers.RemoveAt(closers.Count - 1);
                autoCapIndex = -1;
                act.Backspaces = 1;
                act.Text = "";
                act.DeleteRight = 1;
                return act;
            }

            if (buf.Length == 0)
            {
                startCap = false;
                userOverride = false;
                return act;
            }
            bool wasAuto = autoCapIndex == buf.Length - 1;
            buf.Length -= 1;
            autoCapIndex = -1;
            if (wasAuto) userOverride = true;
            return act;
        }

        void Finish(string before, string typed, ref EngineAction act, bool undoable)
        {
            string after = buf.ToString();
            if (after != typed)
            {
                Diff(before, after, ref act);
                if (undoable)
                {
                    int common = CommonPrefix(after, typed);
                    undoScreen = after.Substring(common);
                    undoOriginal = typed.Substring(common);
                    undoLayout = act.SwitchLayout == Lang.Ru ? Lang.En : act.SwitchLayout == Lang.En ? Lang.Ru : Lang.Other;
                }
            }
            if (autoCapIndex >= 0 && autoCapIndex != buf.Length - 1) autoCapIndex = -1;
            Trim();
        }

        static void Diff(string from, string to, ref EngineAction act)
        {
            int common = CommonPrefix(from, to);
            act.Backspaces = from.Length - common;
            act.Text = to.Substring(common);
        }

        static int CommonPrefix(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }

        void Trim()
        {
            if (buf.Length <= MaxBuffer) return;
            int cut = buf.Length - MaxBuffer / 2;
            buf.Remove(0, cut);
            truncated = true;
            if (autoCapIndex >= 0) autoCapIndex -= cut;
        }

        void ClearUndo() { undoScreen = undoOriginal = null; undoLayout = Lang.Other; }

        bool TryDoubleSpace(long nowMs, bool repeat)
        {
            int n = buf.Length;
            if (!Cfg.DoubleSpacePeriod || repeat || n < 2 || buf[n - 1] != ' '
                || nowMs - lastSpaceMs > Cfg.DoubleSpaceIntervalMs || !IsWordEnd(buf[n - 2])) return false;
            buf.Length = n - 1;
            buf.Append(". ");
            lastSpaceMs = long.MinValue / 2;
            return true;
        }

        bool TryLeaveBracket(long nowMs, bool repeat)
        {
            int n = buf.Length;
            if (!Cfg.AutoBrackets || closers.Count == 0 || n < 2 || buf[n - 1] != ' ' || IsSpace(buf[n - 2])) return false;
            if (Cfg.BracketExit == "hold")
            {
                if (!repeat) return false;
                swallowHeldSpace = true;
            }
            else if (Cfg.BracketExit != "double" || repeat || nowMs - lastSpaceMs > Cfg.DoubleSpaceIntervalMs) return false;
            buf.Length = n - 1;
            buf.Append(closers[closers.Count - 1]);
            closers.RemoveAt(closers.Count - 1);
            lastSpaceMs = long.MinValue / 2;
            return true;
        }

        bool TryStepOverCloser(char typed, string before, ref EngineAction act)
        {
            int at = closers.LastIndexOf(typed);
            if (at < 0) return false;
            string typedSoFar = buf.ToString();
            int steps = closers.Count - at;
            for (int i = closers.Count - 1; i >= at; i--) buf.Append(closers[i]);
            closers.RemoveRange(at, steps);
            Diff(before, typedSoFar, ref act);
            act.CaretRight = steps;
            return true;
        }

        char PairFor(char typed, char output)
        {
            int n = buf.Length;
            char prev = n == 0 ? ' ' : buf[n - 1];
            bool freeStart = n == 0 || char.IsWhiteSpace(prev) || IsOpener(prev);
            if (!freeStart || prev == output) return '\0';
            if (typed == '"') return output == '«' ? '»' : output == '„' ? '“' : output == '"' ? '"' : '\0';
            switch (output)
            {
                case '(': return ')';
                case '[': return ']';
                case '{': return '}';
                case '«': return '»';
                case '„': return '“';
                case '“': return '”';
                case '‘': return '’';
            }
            return '\0';
        }

        static char OpenerOf(char closer)
        {
            switch (closer)
            {
                case ')': return '(';
                case ']': return '[';
                case '}': return '{';
                case '»': return '«';
                case '“': return '„';
                case '”': return '“';
                case '’': return '‘';
                case '"': return '"';
            }
            return '\0';
        }

        static bool IsTightPunct(char c) { return c == ',' || c == '.' || c == '!' || c == '?'; }

        void RemoveSpaceBefore()
        {
            int n = buf.Length;
            if (n >= 2 && buf[n - 1] == ' ' && IsWordEnd(buf[n - 2])) buf.Length = n - 1;
        }

        void AddSpaceAfterPunct(char letter)
        {
            int n = buf.Length;
            if (n < 2) return;
            char p = buf[n - 1];
            if (!(p == ',' || p == '.' || p == '!' || p == '?' || p == ';' || p == ':')) return;
            char prev = buf[n - 2];
            if (!char.IsLetter(prev) && !IsCloser(prev) && !(p == '.' && prev == '.')) return;

            int s = n - 1;
            while (s > 0 && !char.IsWhiteSpace(buf[s - 1])) s--;
            string token = buf.ToString(s, n - s);
            if (token.IndexOf('/') >= 0 || token.IndexOf('@') >= 0 || token.IndexOf('\\') >= 0
                || token.StartsWith("www", StringComparison.OrdinalIgnoreCase)) return;

            if (p == '.')
            {
                int w = n - 2;
                while (w >= 0 && char.IsLetter(buf[w])) w--;
                if (n - 2 - w == 1) return;
                if (IsLatin(letter) && char.IsLower(letter)) return;
            }
            if (p == ':' && IsLatin(letter) && IsLatin(prev)) return;
            buf.Append(' ');
        }

        void AddSpaceBeforeBracket()
        {
            int n = buf.Length;
            if (n == 0 || !char.IsLetter(buf[n - 1])) return;
            int s = n;
            while (s > 0 && !char.IsWhiteSpace(buf[s - 1])) s--;
            string token = buf.ToString(s, n - s);
            if (token.IndexOf('/') >= 0 || token.IndexOf('@') >= 0 || token.IndexOf('.') >= 0 || token.IndexOf('_') >= 0) return;
            buf.Append(' ');
        }

        char Quote()
        {
            int n = buf.Length;
            char prev = n == 0 ? ' ' : buf[n - 1];
            bool afterSpace = n == 0 || char.IsWhiteSpace(prev);
            int depth = 0;
            bool innerOpen = false;
            for (int i = 0; i < n; i++)
            {
                if (buf[i] == '«') depth++;
                else if (buf[i] == '»') depth = Math.Max(0, depth - 1);
                else if (buf[i] == '„') innerOpen = true;
                else if (buf[i] == '“') innerOpen = false;
            }
            if (afterSpace) return depth > 0 ? '„' : '«';
            if (depth > 0) return innerOpen ? '“' : '»';
            return IsOpener(prev) ? '«' : '»';
        }

        enum Boundary { Space, Punct, Enter }

        void FixWord(Boundary b, ref EngineAction act)
        {
            if (buf.Length == 0) return;
            if (Cfg.Snippets && TrySnippet(b)) return;
            if (Cfg.LayoutFix && b != Boundary.Punct && TryLayout(ref act)) return;
            if (Cfg.CapsLockFix && CapsLock && TryCapsLock(ref act)) return;
            if (Cfg.Typos && TryTypo()) return;
            if (Cfg.EnglishI && TryEnglishI()) return;
            if (Cfg.AutoHyphen) TryHyphen(b != Boundary.Space);
        }

        bool TrySnippet(Boundary b)
        {
            if (Cfg.SnippetList.Count == 0) return false;
            int s;
            if (b != Boundary.Punct)
            {
                string token = TokenBefore(buf.Length, out s);
                if (token != null && Expand(token, s)) return true;
            }
            string word = WordBefore(buf.Length, out s);
            return word != null && Expand(word, s);
        }

        bool Expand(string trigger, int start)
        {
            string text;
            if (!Cfg.SnippetList.TryGetValue(trigger, out text) || text.Length == 0) return false;
            text = RenderVariables(text);
            if (text.Length == 0) return false;
            if (char.IsUpper(trigger[0]) && char.IsLower(text[0])) text = char.ToUpper(text[0]) + text.Substring(1);
            buf.Length = start;
            buf.Append(text);
            return true;
        }

        public string RenderVariables(string text)
        {
            if (text.IndexOf('{') < 0) return text;
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open < 0 ? -1 : text.IndexOf('}', open);
                if (close < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, open - i);
                string value = VariableValue(text.Substring(open + 1, close - open - 1));
                sb.Append(value ?? text.Substring(open, close - open + 1));
                i = close + 1;
            }
            return sb.ToString();
        }

        string VariableValue(string spec)
        {
            int colon = spec.IndexOf(':');
            string name = (colon < 0 ? spec : spec.Substring(0, colon)).Trim();
            string format = colon < 0 ? null : spec.Substring(colon + 1);
            string lower = name.ToLowerInvariant();
            try
            {
                if (lower == "date" || lower == "дата") return Now().ToString(format ?? "d");
                if (lower == "time" || lower == "время") return Now().ToString(format ?? "HH:mm");
            }
            catch (FormatException) { return null; }
            if (lower == "clipboard" || lower == "буфер")
            {
                string clip = (ClipboardText() ?? "").Replace("\r\n", "\n");
                return clip.Length > 4000 ? clip.Substring(0, 4000) : clip;
            }
            return null;
        }

        bool TryLayout(ref EngineAction act)
        {
            if (Lang == Lang.Other || !BothLayouts || !Cfg.LangRussian || !Cfg.LangEnglish) return false;
            Lang other = Lang == Lang.Ru ? Lang.En : Lang.Ru;
            var fromDict = Dictionaries.Get(Lang);
            var toDict = Dictionaries.Get(other);
            if (fromDict == null || toDict == null) return false;

            int s;
            string token = TokenBefore(buf.Length, out s);
            if (token == null || token.Length < 2 || (s == 0 && truncated)) return false;

            string converted = Layouts.Convert(token, Lang);
            if (converted == null) return false;
            string core = TrimTail(converted);
            string origCore = TrimTail(token);
            if (core.Length < 2 || !AllLetters(core, other) || !CaseLooksTyped(core)) return false;

            string lower = core.ToLowerInvariant();
            string origLower = origCore.ToLowerInvariant();
            if (Cfg.LayoutIgnore.Contains(origLower) || Cfg.LayoutIgnore.Contains(lower) || Cfg.CustomTypos.ContainsKey(origLower)) return false;
            if (core.Length == 2)
            {
                var shortTo = other == Lang.Ru ? ShortRu : ShortEn;
                var shortFrom = Lang == Lang.Ru ? ShortRu : ShortEn;
                if (!shortTo.Contains(lower) || shortFrom.Contains(origLower)) return false;
                if (origCore.Length > 2 && AllLetters(origCore, Lang) && fromDict.Contains(origLower)) return false;
                buf.Length = s;
                buf.Append(converted);
                act.SwitchLayout = other;
                Lang = other;
                return true;
            }
            bool origKnown = AllLetters(origCore, Lang) && fromDict.Contains(origLower);
            if (origKnown || !toDict.Contains(lower)) return false;

            buf.Length = s;
            buf.Append(converted);
            act.SwitchLayout = other;
            Lang = other;
            return true;
        }

        static readonly HashSet<string> ShortRu = Set(
            "да не он но на по за из от до же ли бы мы вы ты их её ей им уж ну то со ко во об ой ах эх ох ок");
        static readonly HashSet<string> ShortEn = Set(
            "of to in it is be as at so we he by or on do if me my up an go no us am ok hi oh");

        bool TryCapsLock(ref EngineAction act)
        {
            int s;
            string w = WordBefore(buf.Length, out s);
            if (w == null || w.Length < 3 || !char.IsLower(w[0])) return false;
            for (int i = 1; i < w.Length; i++)
                if (char.IsLetter(w[i]) && !char.IsUpper(w[i])) return false;
            bool sentenceStart = Cfg.Capitalize && ShouldCapitalizeAt(s);
            buf.Length = s;
            buf.Append(sentenceStart ? char.ToUpper(w[0]) : w[0]).Append(w.Substring(1).ToLowerInvariant());
            act.ToggleCapsLock = true;
            CapsLock = false;
            return true;
        }

        static readonly Dictionary<string, string> TypoList = Pairs(
            "вообщем=в общем|вобщем=в общем|впринципе=в принципе|вобще=вообще|извени=извини|извените=извините|" +
            "извене=извини|извенити=извините|извенте=извините|извеняюсь=извиняюсь|извеняю=извиняю|" +
            "здраствуйте=здравствуйте|здраствуй=здравствуй|здрасте=здравствуйте|сдесь=здесь|кароче=короче|" +
            "пожалуста=пожалуйста|пожайлуста=пожалуйста|пожалуйсто=пожалуйста|агенство=агентство|прийдти=прийти|" +
            "прийти=прийти|координально=кардинально|учавствовать=участвовать|учавствую=участвую|расчитать=рассчитать|" +
            "будующий=будущий|будующее=будущее|будующем=будущем|приветсвую=приветствую|чуствовать=чувствовать|" +
            "чуство=чувство|чуствую=чувствую|лесница=лестница|интиресно=интересно|интерестно=интересно|" +
            "обьявление=объявление|обьяснить=объяснить|обьясни=объясни|обьект=объект|подьезд=подъезд|сьесть=съесть|" +
            "сьел=съел|девчёнка=девчонка|ихний=их|симпотичный=симпатичный|симпотичная=симпатичная|" +
            "teh=the|recieve=receive|recieved=received|doesnt=doesn't|didnt=didn't|isnt=isn't|wasnt=wasn't|" +
            "dont=don't|alot=a lot|definately=definitely|seperate=separate|occured=occurred|untill=until|" +
            "wich=which|becuase=because|beleive=believe|freind=friend|thier=their|tommorow=tomorrow|" +
            "tomorow=tomorrow|goverment=government|enviroment=environment|wierd=weird");

        bool TryTypo()
        {
            int s;
            string w = WordBefore(buf.Length, out s, true);
            if (w == null) return false;
            string fix;
            if (!Cfg.CustomTypos.TryGetValue(w, out fix) && !TypoList.TryGetValue(w.ToLowerInvariant(), out fix)) return false;
            if (fix == w.ToLowerInvariant()) return false;
            if (w.Length > 1 && w.ToUpperInvariant() == w) fix = fix.ToUpperInvariant();
            else if (char.IsUpper(w[0])) fix = char.ToUpper(fix[0]) + fix.Substring(1);
            buf.Length = s;
            buf.Append(fix);
            return true;
        }

        bool TryEnglishI()
        {
            int s;
            string w = WordBefore(buf.Length, out s, true);
            if (w == null || w[0] != 'i') return false;
            string l = w.Replace('’', '\'');
            if (l != "i" && l != "i'm" && l != "i'd" && l != "i'll" && l != "i've") return false;
            buf[s] = 'I';
            return true;
        }

        static readonly HashSet<string> Pronouns = Set(
            "кто кого кому кем ком что чего чему чем чём " +
            "какой какая какое какие какого какому каким каком какую каких какими " +
            "чей чья чьё чье чьи чьего чьему чьим чьём чьем чьей чью чьих чьими " +
            "где куда откуда когда как почему зачем отчего сколько скольких скольким " +
            "такой такая такое такие такого такому таким таком такую таких такими");
        static readonly HashSet<string> GluedToOnly = Set("вообще наконец");
        static readonly HashSet<string> GluedPairs = Set(
            "по-моему по-твоему по-своему по-нашему по-вашему по-разному по-другому по-новому по-старому по-прежнему " +
            "по-настоящему по-хорошему по-плохому по-иному по-всякому по-любому по-русски по-английски по-немецки " +
            "по-французски по-испански по-итальянски по-китайски по-японски по-украински по-человечески по-дружески " +
            "во-первых во-вторых в-третьих в-четвёртых в-четвертых в-пятых в-шестых в-седьмых в-восьмых в-девятых в-десятых " +
            "из-за из-под всё-таки все-таки опять-таки довольно-таки " +
            "чуть-чуть еле-еле едва-едва вот-вот только-только давным-давно более-менее худо-бедно мало-помалу " +
            "крест-накрест как-никак нет-нет ну-ну видимо-невидимо волей-неволей всего-навсего мало-мальски туда-сюда точь-в-точь " +
            "дай-ка давай-ка ну-ка смотри-ка гляди-ка глянь-ка скажи-ка иди-ка пойдём-ка пойдем-ка попробуй-ка налей-ка " +
            "подожди-ка покажи-ка возьми-ка сделай-ка послушай-ка");
        static readonly Dictionary<string, string> GluedWords = BuildGlued();

        static Dictionary<string, string> BuildGlued()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string w in GluedPairs) d[w.Replace("-", "")] = w;
            return d;
        }

        static int GluedHyphenAt(string l)
        {
            foreach (string tail in new[] { "нибудь", "либо", "то" })
            {
                if (!l.EndsWith(tail, StringComparison.Ordinal)) continue;
                string head = l.Substring(0, l.Length - tail.Length);
                if (Pronouns.Contains(head) || (tail == "то" && GluedToOnly.Contains(head))) return head.Length;
            }
            if ((l.StartsWith("кое", StringComparison.Ordinal) || l.StartsWith("кой", StringComparison.Ordinal)) && Pronouns.Contains(l.Substring(3))) return 3;
            return -1;
        }

        bool TryGluedHyphen()
        {
            int s;
            string w = WordBefore(buf.Length, out s);
            if (w == null || w.IndexOf('-') >= 0) return false;
            string l = w.ToLowerInvariant();
            string full;
            if (GluedWords.TryGetValue(l, out full))
            {
                for (int i = 0; i < full.Length; i++)
                    if (full[i] == '-') buf.Insert(s + i, '-');
                return true;
            }
            int at = GluedHyphenAt(l);
            if (at <= 0) return false;
            buf.Insert(s + at, '-');
            return true;
        }
        static readonly HashSet<string> Taki = Set("всё все так опять довольно наконец прямо вправду верно");
        static readonly HashSet<string> Prepositions = Set(
            "с со у в во о об обо на за к ко по из от до для про при без над под через перед между");
        static readonly HashSet<string> Ordinals = Set(
            "первых вторых третьих четвёртых четвертых пятых шестых седьмых восьмых девятых десятых");
        static readonly HashSet<string> HyphenPairs = Set(
            "чуть|чуть еле|еле давным|давно более|менее худо|бедно мало|помалу волей|неволей крест|накрест " +
            "как|никак да|да нет|нет ну|ну видимо|невидимо едва|едва вот|вот только|только всего|навсего мало|мальски туда|сюда " +
            "вообще|то наконец|то по|прежнему по|разному");
        static readonly HashSet<string> AfterPoAdverb = Set("не ни же бы ли и а но");

        bool TryHyphen(bool punct)
        {
            if (buf.Length == 0 || !char.IsLetter(buf[buf.Length - 1])) return false;
            if (TryGluedHyphen()) return true;

            int s2, s1 = -1, s0 = -1;
            string w2 = WordBefore(buf.Length, out s2);
            string w1 = s2 >= 2 && buf[s2 - 1] == ' ' ? WordBefore(s2 - 1, out s1) : null;
            if (w2 == null || w1 == null) return false;
            string w0 = s1 >= 2 && buf[s1 - 1] == ' ' ? WordBefore(s1 - 1, out s0) : null;

            string l0 = w0 == null ? null : w0.ToLowerInvariant();
            string l1 = w1.ToLowerInvariant(), l2 = w2.ToLowerInvariant();

            if (l0 == "точь" && l1 == "в" && l2 == "точь") { buf[s1 - 1] = '-'; buf[s2 - 1] = '-'; return true; }
            if (l0 == "по" && IsPoAdverb(l1) && AfterPoAdverb.Contains(l2)) { buf[s1 - 1] = '-'; return true; }

            bool join =
                l2 == "нибудь"
                || ((l2 == "то" || l2 == "либо") && Pronouns.Contains(l1))
                || (l2 == "ка" && w1.Length >= 2)
                || (l2 == "таки" && Taki.Contains(l1))
                || ((l1 == "кое" || l1 == "кой") && !Prepositions.Contains(l2))
                || (l1 == "из" && (l2 == "за" || l2 == "под"))
                || ((l1 == "во" || l1 == "в") && Ordinals.Contains(l2))
                || HyphenPairs.Contains(l1 + "|" + l2)
                || (l1 == "по" && (EndsWithAny(l2, "ски", "цки", "ьи") || (punct && IsPoAdverb(l2))));
            if (join) buf[s2 - 1] = '-';
            return join;
        }

        static bool IsPoAdverb(string w) { return w.Length >= 4 && EndsWithAny(w, "ому", "ему"); }

        bool ShouldCapitalize() { return ShouldCapitalizeAt(buf.Length); }

        bool ShouldCapitalizeAt(int end)
        {
            int i = end - 1;
            if (Cfg.CapitalizeInBrackets && i >= 0 && (buf[i] == '(' || buf[i] == '[' || buf[i] == '{')
                && (i == 0 || IsSpace(buf[i - 1]) || IsOpener(buf[i - 1]))) return true;
            bool sawSpace = false;
            while (i >= 0 && (IsSpace(buf[i]) || IsOpener(buf[i])))
            {
                if (IsSpace(buf[i])) sawSpace = true;
                i--;
            }
            if (i < 0) return !truncated && startCap;
            if (buf[i] == '\n') return Cfg.CapitalizeAfterEnter;
            if (!sawSpace) return false;

            while (i >= 0 && IsCloser(buf[i])) i--;
            if (i < 0) return false;

            char c = buf[i];
            if (c == '!' || c == '?') return true;
            if (c == '…') return Cfg.CapitalizeAfterEllipsis;
            if (c != '.') return false;
            if (i >= 1 && buf[i - 1] == '.') return Cfg.CapitalizeAfterEllipsis;

            int s = i - 1;
            while (s >= 0 && (char.IsLetter(buf[s]) || buf[s] == '.')) s--;
            string token = buf.ToString(s + 1, i - s - 1);
            if (token.Length == 0) return true;
            if (token.Length == 1 && char.IsUpper(token[0])) return false;
            return !Cfg.Abbreviations.Contains(token.ToLowerInvariant());
        }

        string WordBefore(int end, out int start, bool apostrophes = false)
        {
            int s = end;
            while (s > 0)
            {
                char ch = buf[s - 1];
                if (char.IsLetter(ch) || ch == '-' || (apostrophes && (ch == '\'' || ch == '’'))) s--;
                else break;
            }
            start = s;
            if (s == end || !char.IsLetter(buf[s])) return null;
            return buf.ToString(s, end - s);
        }

        string TokenBefore(int end, out int start)
        {
            int s = end;
            while (s > 0 && !char.IsWhiteSpace(buf[s - 1])) s--;
            start = s;
            return s == end ? null : buf.ToString(s, end - s);
        }

        static string TrimTail(string w)
        {
            int e = w.Length;
            while (e > 0 && ".,!?;:".IndexOf(w[e - 1]) >= 0) e--;
            return w.Substring(0, e);
        }

        static bool AllLetters(string w, Lang lang)
        {
            if (w.Length == 0) return false;
            foreach (char c in w)
            {
                if (c == '\'' && lang == Lang.En) continue;
                if (!char.IsLetter(c)) return false;
                bool cyr = c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё';
                if (lang == Lang.Ru ? !cyr : !IsLatin(c)) return false;
            }
            return true;
        }

        static bool CaseLooksTyped(string w)
        {
            bool restUpper = true, restLower = true;
            for (int i = 1; i < w.Length; i++)
            {
                if (!char.IsLetter(w[i])) continue;
                if (char.IsUpper(w[i])) restLower = false; else restUpper = false;
            }
            return restLower || (restUpper && char.IsUpper(w[0]));
        }

        static bool IsLatin(char c) { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'; }

        static bool IsBoundary(char c)
        {
            return c == ' ' || c == '.' || c == ',' || c == '!' || c == '?' || c == ';' || c == ':'
                || c == ')' || c == ']' || c == '}' || c == '»' || c == '"' || c == '…';
        }

        static bool EndsWithAny(string w, params string[] ends)
        {
            foreach (string e in ends) if (w.EndsWith(e, StringComparison.Ordinal)) return true;
            return false;
        }

        static HashSet<string> Set(string words)
        {
            return new HashSet<string>(words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }

        static Dictionary<string, string> Pairs(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string p in s.Split('|'))
            {
                int eq = p.IndexOf('=');
                d[p.Substring(0, eq)] = p.Substring(eq + 1);
            }
            return d;
        }

        static bool IsSpace(char c) { return c == ' ' || c == '\t' || c == ' '; }

        static bool IsOpener(char c)
        {
            return c == '(' || c == '[' || c == '{' || c == '«' || c == '"' || c == '\'' || c == '„'
                || c == '“' || c == '‘' || c == '—' || c == '–' || c == '-';
        }

        static bool IsCloser(char c)
        {
            return c == ')' || c == ']' || c == '}' || c == '»' || c == '"' || c == '\'' || c == '”' || c == '’' || c == '“';
        }

        static bool IsWordEnd(char c) { return char.IsLetterOrDigit(c) || IsCloser(c); }
    }

    static class Layouts
    {
        const string En = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&";
        const string Ru = "ёйцукенгшщзхъфывапролджэячсмитьбю.ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,\"№;:?";

        public static string Convert(string s, Lang from)
        {
            string src = from == Lang.En ? En : Ru, dst = from == Lang.En ? Ru : En;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                int i = src.IndexOf(c);
                if (i < 0) return null;
                sb.Append(dst[i]);
            }
            return sb.ToString();
        }
    }
}
