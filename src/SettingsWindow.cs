using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskHand
{
    public sealed class AppItem
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public ImageSource Icon { get; set; }
    }

    static class SettingsApp
    {
        public static int Run()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\DeskHand.Settings", out created))
            {
                if (!created)
                {
                    ActivateExisting();
                    return 0;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var settings = new SettingsWindow();
                app.Run(settings.Window);
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static void ActivateExisting()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("DeskHand"))
            {
                IntPtr h = p.Id == self ? IntPtr.Zero : p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                if (IsIconic(h)) ShowWindow(h, 9);
                SetForegroundWindow(h);
            }
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    }

    sealed class SmoothScroll
    {
        const double Step = 30;

        readonly ScrollViewer sv;
        readonly ScrollViewer inner;
        double target;
        bool running;

        public SmoothScroll(ScrollViewer sv, ScrollViewer inner)
        {
            this.sv = sv;
            this.inner = inner;
            sv.PreviewMouseWheel += OnWheel;
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var source = e.OriginalSource as Visual;
            var here = PresentationSource.FromVisual(sv);
            var there = source == null ? null : PresentationSource.FromVisual(source);
            if (here != null && there != null && there != here) return;

            if (inner != null && inner.IsVisible && IsInside(e.OriginalSource as DependencyObject, inner)
                && (e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight))
                return;

            e.Handled = true;
            if (!running) target = sv.VerticalOffset;
            target = Math.Max(0, Math.Min(sv.ScrollableHeight, target - e.Delta * Step / 120.0));
            if (!running)
            {
                running = true;
                CompositionTarget.Rendering += Tick;
            }
        }

        void Tick(object sender, EventArgs e)
        {
            double delta = target - sv.VerticalOffset;
            if (Math.Abs(delta) < 0.5)
            {
                sv.ScrollToVerticalOffset(target);
                running = false;
                CompositionTarget.Rendering -= Tick;
                return;
            }
            sv.ScrollToVerticalOffset(sv.VerticalOffset + delta * 0.22);
        }

        static bool IsInside(DependencyObject node, DependencyObject parent)
        {
            while (node != null)
            {
                if (node == parent) return true;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }
    }

    sealed partial class SettingsWindow
    {
        static readonly string[,] NavItems =
        {
            { "fix", "", "Corrections", "Исправления" },
            { "caps", "", "Capital letters", "Заглавные буквы" },
            { "layout", "", "Keyboard layout", "Раскладка" },
            { "snippets", "", "Text shortcuts", "Быстрые вставки" },
            { "apps", "", "Apps", "Программы" },
            { "tools", "\uE90F", "Tools", "Инструменты" },
            { "scenarios", "\uE768", "Scenarios", "Сценарии" },
            { "system", "", "System", "Система" },
        };

        public readonly Window Window;

        readonly List<KeyValuePair<CheckBox, Func<Config, bool>>> toggles = new List<KeyValuePair<CheckBox, Func<Config, bool>>>();
        readonly Dictionary<string, FrameworkElement> pages = new Dictionary<string, FrameworkElement>();
        readonly Dictionary<string, RadioButton> navButtons = new Dictionary<string, RadioButton>();
        readonly EventWaitHandle demoFocus;
        Config cfg;
        bool loading, dark;
        string currentPage = "fix";

        ItemsControl appChips, abbrevChips, layoutIgnoreChips;
        StackPanel snippetList, ruleList, speedRow, correctionList;
        StackPanel bracketRow, indicatorRow;
        CheckBox awakeToggle;
        ItemsControl appLayoutChips;
        readonly List<Action> hotkeyRefresh = new List<Action>();
        System.Windows.Controls.Primitives.Popup hintPopup;
        Image hintImage;
        DispatcherTimer hintTimer;
        int hintSeq;
        RadioButton[] speedButtons, langButtons, themeButtons, bracketButtons, indicatorButtons, awakeButtons;
        CheckBox autostart;
        TextBox snipTrigger, snipText;
        TextBlock snipTitle, snipError;
        Button snipSave, snipCancel;
        string editingSnippet;
        Action<string> pickerTarget;
        Func<string, bool> pickerFilter;

        static string T(string en, string ru) { return Loc.T(en, ru); }

        public SettingsWindow()
        {
            using (var s = Resource("Settings.xaml"))
                Window = (Window)XamlReader.Load(s);

            cfg = Config.Load(Program.ConfigPath);
            Loc.Apply(cfg);

            demoFocus = new EventWaitHandle(false, EventResetMode.ManualReset, Program.DemoEventName);

            Program.Theme = cfg.Theme;
            dark = Program.IsDark();
            ApplyTheme();
            Window.Icon = BitmapFrame.Create(Resource("app.ico"));
            var logo = new BitmapImage();
            logo.BeginInit();
            logo.StreamSource = Resource("logo.png");
            logo.CacheOption = BitmapCacheOption.OnLoad;
            logo.EndInit();
            Get<Image>("Logo").Source = logo;

            BindCheck(Get<CheckBox>("Enabled"), c => c.Enabled, (c, v) => c.Enabled = v);
            BuildUi();

            Get<Button>("StartButton").Click += delegate
            {
                Process.Start(Program.ExePath, "--background");
                Later(800, UpdateStatus);
            };
            Get<Button>("GithubLink").Click += delegate { Process.Start("https://github.com/Eastiwckgg/DeskHand"); };
            Get<ItemsControl>("RunningApps").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var tag = TagOf(e);
                if (tag != null) PickApp(tag);
            }));
            Get<Button>("BrowseExe").Click += delegate
            {
                Get<Popup>("Picker").IsOpen = false;
                var dlg = new OpenFileDialog { Filter = T("Programs (*.exe)|*.exe", "Программы (*.exe)|*.exe") };
                if (dlg.ShowDialog(Window) == true) PickApp(System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
            };

            var picker = Get<ScrollViewer>("PickerScroll");
            new SmoothScroll(Get<ScrollViewer>("Scroll"), picker);
            new SmoothScroll(picker, null);

            var closeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.CloseSettingsEventName);
            var closeWait = ThreadPool.RegisterWaitForSingleObject(closeEvent,
                delegate { Window.Dispatcher.BeginInvoke(new Action(Window.Close)); }, null, -1, true);
            Window.Closed += delegate { closeWait.Unregister(null); closeEvent.Dispose(); };

            Window.SourceInitialized += delegate { ApplyBackdrop(); };
            Window.Activated += delegate { Refresh(); };
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Window.Closed += delegate
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                demoFocus.Reset();
            };

            Refresh();
            ShowPage("fix");
        }

        void BuildUi()
        {
            Get<Button>("StartButton").Content = T("Start", "Запустить");
            Get<CheckBox>("Enabled").ToolTip = T("Turn on or pause", "Включить или поставить на паузу");
            Get<TextBlock>("BrowseExeText").Text = T("Choose an .exe file…", "Выбрать файл .exe…");
            Get<TextBlock>("Footer").Text = T("Version ", "Версия ") + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + "\n" +
                T("You can close this window, the app keeps working in the tray",
                  "Окно можно закрыть, программа продолжит работать в трее");
            BuildPages();
            BuildNav();
        }

        void Rebuild()
        {
            toggles.RemoveRange(1, toggles.Count - 1);
            pages.Clear();
            navButtons.Clear();
            Get<Grid>("Pages").Children.Clear();
            Get<StackPanel>("Nav").Children.Clear();
            editingSnippet = null;
            CloseEditor();
            hotkeyRefresh.Clear();
            HideHint();
            BuildUi();
            Refresh();
            ShowPage(currentPage);
        }

        void BuildPages()
        {
            var demo = new TextBox { Style = St("Input"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
            var demoHint = Placeholder(T("hello. how are you? try i think, teh or two spaces after a word",
                                         "привет. как дела? попробуйте «кое как», «ghbdtn» или два пробела после слова"));
            demo.TextChanged += delegate { demoHint.Visibility = demo.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            demo.GotKeyboardFocus += delegate { demoFocus.Set(); };
            demo.LostKeyboardFocus += delegate { demoFocus.Reset(); };

            var speed = new StackPanel { Orientation = Orientation.Horizontal };
            speedButtons = new[]
            {
                Segment(T("Fast", "Быстро"), "speed", () => SetSpeed(400)),
                Segment(T("Normal", "Обычно"), "speed", () => SetSpeed(600)),
                Segment(T("Relaxed", "Не спеша"), "speed", () => SetSpeed(900)),
            };
            foreach (var rb in speedButtons) speed.Children.Add(rb);
            speedRow = new StackPanel();
            speedRow.Children.Add(FeatureRow("DoubleSpacePeriod", T("Period on double space", "Точка по двойному пробелу"),
                T("Two quick spaces after a word insert a period and a space, like on iPhone",
                  "Два быстрых пробела после слова ставят точку и пробел, как на iPhone"), false, "hint-double-space"));
            speedRow.Children.Add(SubRow(T("How fast to press the spaces", "Насколько быстро нажимать пробелы"), SegmentBox(speed)));

            var bracketExit = new StackPanel { Orientation = Orientation.Horizontal };
            bracketButtons = new[]
            {
                Segment(T("Hold Space", "Удержать пробел"), "bracket", () => SetBracketExit("hold")),
                Segment(T("Two spaces", "Два пробела"), "bracket", () => SetBracketExit("double")),
            };
            foreach (var rb in bracketButtons) bracketExit.Children.Add(rb);
            bracketRow = new StackPanel();
            bracketRow.Children.Add(FeatureRow("AutoBrackets", T("Auto-close brackets and quotes", "Автоматические скобки и кавычки"),
                T("Type “(” and “)” appears, with the cursor between them",
                  "Вводите «(», и появляется «)», а курсор остаётся между ними"), false, "hint-brackets"));
            bracketRow.Children.Add(SubRow(T("How to jump past the closing bracket, “(hello” → “(hello)”. Typing “)” always works too",
                                             "Как выйти за закрывающую скобку, «(привет» → «(привет)». Ввести «)» можно всегда"), SegmentBox(bracketExit)));

            AddPage("fix",
                Card(Padded(Stack(Txt(T("Try it here", "Попробуйте здесь"), "RowTitle"), Overlay(demo, demoHint, new Thickness(0, 10, 0, 0))))),
                Section(T("Basics", "Основное")),
                Card(FeatureRow("Capitalize", T("Capitalize sentences", "Заглавная в начале предложения"),
                         T("“hello. how are you” → “Hello. How are you”", "«привет. как дела» → «Привет. Как дела»")),
                     speedRow,
                     FeatureRow("AutoSpace", T("Spaces around punctuation", "Пробелы у знаков препинания"),
                         T("“hello ,world” → “hello, world”, “word(” → “word (”. Leaves links, numbers and abbreviations alone",
                           "«привет ,как дела» → «привет, как дела», «слово(» → «слово (». Адреса сайтов, числа и сокращения не трогает"), false, "hint-punctuation"),
                     FeatureRow("AutoHyphen", T("Hyphens in Russian words", "Дефис в словах"),
                         T("“кое как” → “кое-как”, “что нибудь” → “что-нибудь”, “из за” → “из-за”",
                           "«кое как» → «кое-как», «что нибудь» → «что-нибудь», «из за» → «из-за»")),
                     FeatureRow("SmartQuotes", T("Russian quotes «»", "Кавычки-ёлочки"),
                         T("In the Russian layout \"text\" → «text». Quotes stay straight in the English layout",
                           "В русской раскладке \"текст\" → «текст». В английской кавычки остаются обычными")),
                     bracketRow),
                Section(T("Typos", "Опечатки")),
                Card(FeatureRow("CapsLockFix", T("Accidental Caps Lock", "Случайный Caps Lock"),
                         T("“hELLO” → “Hello”, and Caps Lock turns off", "«пРИВЕТ» → «Привет», и Caps Lock сам выключится"), false, "hint-caps-lock"),
                     FeatureRow("Typos", T("Common mistakes", "Частые ошибки"),
                         T("“teh” → “the”, “definately” → “definitely”, “вообщем” → “в общем”",
                           "«вообщем» → «в общем», «извени» → «извини», «teh» → «the»")),
                     FeatureRow("EnglishI", T("“i” → “I” in English", "«i» → «I» в английском"),
                         T("“i think” → “I think”, “i'm” → “I'm”", "«i think» → «I think», «i'm» → «I'm»"))),
                Section(T("Your corrections", "Ваши исправления")),
                CorrectionsCard(),
                Note(T("Any correction can be undone: press Backspace right after it.",
                       "Любую автозамену можно отменить: нажмите Backspace сразу после неё.")));

            indicatorRow = new StackPanel();
            abbrevChips = Chips(name => { if (cfg.Abbreviations.Remove(name)) Save(); });
            var abbrevInput = new TextBox { Style = St("Input"), FontSize = 13 };
            var abbrevHint = Placeholder(T("New abbreviation, then Enter", "Новое сокращение и Enter"));
            abbrevInput.TextChanged += delegate { abbrevHint.Visibility = abbrevInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            abbrevInput.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                string a = abbrevInput.Text.Trim().TrimEnd('.').ToLowerInvariant();
                abbrevInput.Clear();
                if (a.Length > 0 && cfg.Abbreviations.Add(a)) Save();
                e.Handled = true;
            };
            var abbrevBox = Overlay(abbrevInput, abbrevHint, new Thickness(0, 4, 0, 0));
            abbrevBox.Width = 280;
            abbrevBox.HorizontalAlignment = HorizontalAlignment.Left;

            AddPage("caps",
                FirstSection(T("When to start with a capital", "Когда начинать с заглавной")),
                Card(ToggleRow(T("After Enter", "После Enter"), T("A new line or a new chat message", "Новая строка или новое сообщение в чате"),
                         c => c.CapitalizeAfterEnter, (c, v) => c.CapitalizeAfterEnter = v),
                     ToggleRow(T("After switching windows", "После перехода в другое окно"),
                         T("The first letter in a window you just switched to. On every switch the app peeks into the text field to see if it is empty",
                           "Первая буква в только что открытом окне. При каждом переходе программа заглядывает в поле ввода, чтобы понять, пустое ли оно"),
                         c => c.CapitalizeOnWindowSwitch, (c, v) => c.CapitalizeOnWindowSwitch = v, true),
                     ToggleRow(T("After a mouse click", "После клика мышью"),
                         T("After clicks and arrow keys the app reads the text before the cursor, so it never capitalizes mid-sentence",
                           "После клика и стрелок программа читает текст перед курсором, чтобы не ставить заглавную посреди фразы"),
                         c => c.CapitalizeAfterClick, (c, v) => c.CapitalizeAfterClick = v, true),
                     ToggleRow(T("After an ellipsis", "После многоточия"), T("“Well... okay” or “Well... Okay”", "«Ну... ладно» или «Ну... Ладно»"),
                         c => c.CapitalizeAfterEllipsis, (c, v) => c.CapitalizeAfterEllipsis = v),
                     ToggleRow(T("Inside brackets", "В скобках"), T("“word (like this)” → “word (Like this)”", "«слово (вот так)» → «слово (Вот так)»"),
                         c => c.CapitalizeInBrackets, (c, v) => c.CapitalizeInBrackets = v)),
                Section(T("Abbreviations", "Сокращения")),
                Card(Padded(Stack(
                    Desc(T("A period after these does not end a sentence: “e.g. this” will not become “e.g. This”",
                           "После них точка не значит конец предложения: «т.е. так» не станет «т.е. Так»"), 0, 12),
                    abbrevChips, abbrevBox))));

            AddPage("layout",
                Card(FeatureRow("LayoutFix", T("Fix the keyboard layout", "Исправлять раскладку"),
                    T("Typed “ghbdtn” but meant “привет”? The app retypes the word and switches the layout for you. Works both ways: “руддщ” → “hello”. Needs both Russian and English layouts and keeps word lists in memory, about 1 MB",
                      "Набрали «ghbdtn», а хотели «привет»? Программа исправит слово и сама переключит раскладку. Работает в обе стороны: «руддщ» → «hello». Держит в памяти словари, около 1 МБ"),
                    true, "hint-layout")),
                Note(T("Kicks in when a word is finished with Space or Enter. Backspace right after the fix brings back both the word and the layout.",
                       "Срабатывает, когда слово дописано и нажат пробел или Enter. Backspace сразу после исправления вернёт слово и раскладку.")),
                Section(T("Never fix these words", "Не трогать эти слова")),
                LayoutIgnoreCard(),
                Section(T("Layout for each app", "Раскладка для каждой программы")),
                AppLayoutsCard(),
                Section(T("Indicator", "Индикатор")),
                Card(indicatorRow),
                Section(T("Languages", "Языки")),
                Desc(T("DeskHand works only in the checked keyboard layouts", "DeskHand работает только в отмеченных раскладках"), 2, 8),
                Card(ToggleRow(T("Russian", "Русский"), T("Also Ukrainian and Belarusian layouts", "А также украинская и белорусская раскладки"),
                         c => c.LangRussian, (c, v) => c.LangRussian = v),
                     ToggleRow(T("English", "Английский"), null, c => c.LangEnglish, (c, v) => c.LangEnglish = v),
                     ToggleRow(T("Other languages", "Остальные языки"),
                         T("German, French and others: capitals, period on double space and punctuation spacing",
                           "Немецкий, французский и другие: заглавные, точка по двойному пробелу и пробелы у знаков"),
                         c => c.LangOther, (c, v) => c.LangOther = v)));

            var indicatorSizes = new StackPanel { Orientation = Orientation.Horizontal };
            indicatorButtons = new[]
            {
                Segment(T("Small", "Маленький"), "indicator", () => SetIndicatorSize("small")),
                Segment(T("Medium", "Средний"), "indicator", () => SetIndicatorSize("medium")),
                Segment(T("Large", "Большой"), "indicator", () => SetIndicatorSize("large")),
            };
            foreach (var rb in indicatorButtons) indicatorSizes.Children.Add(rb);
            indicatorRow.Children.Add(ToggleRow(T("Show near the mouse", "Показывать у курсора мыши"),
                T("When the layout or Caps Lock changes, “ENG”, “РУС” or “CAPS ON” appears for a second",
                  "При смене раскладки или Caps Lock на секунду появляется «РУС», «ENG» или «CAPS ON»"),
                c => c.LayoutIndicator, (c, v) => c.LayoutIndicator = v));
            indicatorRow.Children.Add(SubRow(T("Size. It also follows the Windows display scale", "Размер. Также подстраивается под масштаб экрана в Windows"), SegmentBox(indicatorSizes)));

            snippetList = new StackPanel();
            snipTitle = Txt("", "Section");
            snipTrigger = new TextBox { Style = St("Input"), Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
            snipText = new TextBox { Style = St("Input"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
            snipError = Desc("", 0, 8);
            snipError.Foreground = Brush("#E5534B");
            snipError.Visibility = Visibility.Collapsed;
            snipSave = new Button { Style = St("AccentBtn"), Padding = new Thickness(18, 6, 18, 6) };
            snipCancel = new Button { Style = St("Btn"), Content = T("Cancel", "Отмена"), Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
            snipSave.Click += delegate { SaveSnippet(); };
            snipCancel.Click += delegate { EditSnippet(null); };
            var tokens = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            tokens.Children.Add(TokenButton(T("+ Date", "+ Дата"), "{date}"));
            tokens.Children.Add(TokenButton(T("+ Time", "+ Время"), "{time}"));
            tokens.Children.Add(TokenButton(T("+ Clipboard", "+ Буфер обмена"), "{clipboard}"));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(snipSave);
            buttons.Children.Add(snipCancel);

            AddPage("snippets",
                Card(FeatureRow("Snippets", T("Text shortcuts", "Быстрые вставки"),
                    T("Type a shortcut and a space, and it turns into the full text. For example, “brb” → “be right back”",
                      "Напишите сокращение и пробел, и оно превратится в полный текст. Например, «спс» → «спасибо»"), false, "hint-shortcuts")),
                Section(T("Your shortcuts", "Ваши вставки")),
                Card(snippetList),
                snipTitle,
                Card(Padded(Stack(
                    Txt(T("Shortcut", "Сокращение"), "RowTitle"),
                    Desc(T("No spaces. Symbols are fine, for example “;email”", "Без пробелов. Можно со знаками, например «;почта»"), 0, 6), snipTrigger,
                    Spacer(14),
                    Txt(T("Text", "Текст"), "RowTitle"),
                    Desc(T("Line breaks are kept. If the shortcut starts with a capital, the text does too",
                           "Переносы строк сохраняются. Если сокращение написано с заглавной, текст тоже начнётся с заглавной"), 0, 6), snipText,
                    tokens,
                    Desc(T("These buttons add a value that is filled in at the moment you type the shortcut. For example, the text “Today is {date}” turns into “Today is 02.11.2026”",
                           "Эти кнопки добавляют значение, которое подставится в момент ввода сокращения. Например, текст «Сегодня {date}» превратится в «Сегодня 02.11.2026»"), 0, 0, true),
                    snipError, buttons))));
            EditSnippet(null);

            appChips = Chips(name => { if (cfg.ExcludeApps.Remove(name)) Save(); });
            var addExclude = new Button { Style = St("Btn"), Content = T("+  Add an app", "+  Добавить программу"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            addExclude.Click += delegate
            {
                OpenPicker(addExclude, T("Choose an app where DeskHand should not work:", "Выберите программу, в которой DeskHand не будет работать:"),
                    name => { if (cfg.ExcludeApps.Add(name)) Save(); }, name => !cfg.IsExcluded(name));
            };
            ruleList = new StackPanel();
            var addRule = new Button { Style = St("Btn"), Content = T("+  Add a rule", "+  Добавить правило"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            addRule.Click += delegate
            {
                OpenPicker(addRule, T("Choose an app for custom rules:", "Выберите программу для особых правил:"),
                    name =>
                    {
                        if (!cfg.AppRules.ContainsKey(name)) cfg.AppRules[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        Save();
                    },
                    name => !cfg.AppRules.ContainsKey(name) && !cfg.IsExcluded(name));
            };

            AddPage("apps",
                Card(ToggleRow(T("Games and full-screen apps", "Игры и полноэкранные приложения"),
                    T("Stay out of the way when a window fills the whole screen without a frame",
                      "Не вмешиваться, когда окно развёрнуто на весь экран без рамки"),
                    c => c.SkipFullscreen, (c, v) => c.SkipFullscreen = v)),
                Section(T("Never work in", "Не работать совсем")),
                Card(Padded(Stack(
                    Desc(T("DeskHand works in every other app. The list already has apps where letters act as hotkeys or where you write code: terminals, code editors, Figma, Ableton, password managers",
                           "Во всех остальных программах DeskHand работает. Сюда заранее добавлены программы, где буквы служат горячими клавишами или пишется код: терминалы, редакторы кода, Figma, Ableton, менеджеры паролей"), 0, 12),
                    appChips, addExclude))),
                Section(T("Custom rules", "Особые правила")),
                Desc(T("Turn off single features in a specific app. For example, no capitals in Discord. Crossed-out features do not work in that app",
                       "Выключите отдельные функции в конкретной программе. Например, в Discord писать без заглавных. Зачёркнутые функции в этой программе не работают"), 2, 10),
                ruleList,
                addRule);

            autostart = new CheckBox { Style = St("Toggle") };
            RoutedEventHandler onAutostart = delegate { if (!loading) Program.SetAutostart(autostart.IsChecked == true); };
            autostart.Checked += onAutostart;
            autostart.Unchecked += onAutostart;

            var langs = new StackPanel { Orientation = Orientation.Horizontal };
            langButtons = new[]
            {
                Segment("English", "lang", () => SetLanguage("en")),
                Segment("Русский", "lang", () => SetLanguage("ru")),
            };
            foreach (var rb in langButtons) langs.Children.Add(rb);
            var langBox = SegmentBox(langs);

            var themes = new StackPanel { Orientation = Orientation.Horizontal };
            themeButtons = new[]
            {
                Segment(T("Dark", "Тёмная"), "theme", () => SetTheme("dark")),
                Segment(T("Light", "Светлая"), "theme", () => SetTheme("light")),
                Segment(T("Same as Windows", "Как в Windows"), "theme", () => SetTheme("system")),
            };
            foreach (var rb in themeButtons) themes.Children.Add(rb);

            var openIni = new Button { Style = St("Link"), Content = T("Open the settings file", "Открыть файл настроек") };
            openIni.Click += delegate { Process.Start("notepad.exe", "\"" + Program.ConfigPath + "\""); };
            var quit = new Button { Style = St("Link"), Content = T("Quit DeskHand", "Выйти из DeskHand"), Margin = new Thickness(24, 0, 0, 0) };
            quit.Click += delegate
            {
                Program.Signal(Program.QuitEventName);
                Later(500, UpdateStatus);
            };
            var links = new StackPanel { Orientation = Orientation.Horizontal };
            links.Children.Add(openIni);
            links.Children.Add(quit);

            var awakeTimes = new StackPanel { Orientation = Orientation.Horizontal };
            awakeButtons = new[]
            {
                Segment(T("1 hour", "1 час"), "awake", () => SetAwakeHours(1)),
                Segment(T("3 hours", "3 часа"), "awake", () => SetAwakeHours(3)),
                Segment(T("Until I turn it off", "Пока не выключу"), "awake", () => SetAwakeHours(0)),
            };
            foreach (var rb in awakeButtons) awakeTimes.Children.Add(rb);
            awakeToggle = new CheckBox { Style = St("Toggle") };
            RoutedEventHandler onAwake = delegate
            {
                if (loading) return;
                if (awakeToggle.IsChecked == true) cfg.StartKeepAwake(DateTime.Now);
                else cfg.KeepAwakeUntil = "";
                Save();
            };
            awakeToggle.Checked += onAwake;
            awakeToggle.Unchecked += onAwake;

            AddPage("tools",
                FirstSection(T("Stay awake", "Не засыпать")),
                Card(Row(T("Keep the computer awake", "Не давать компьютеру заснуть"),
                         T("For long downloads and renders. Windows power settings do not change. Closing a laptop lid or choosing Sleep yourself still works",
                           "Для долгих загрузок и рендера. Настройки питания Windows не меняются. Закрыть крышку ноутбука или усыпить компьютер вручную всё равно можно"), awakeToggle),
                     Row(T("For how long", "На какое время"), null, SegmentBox(awakeTimes)),
                     ToggleRow(T("Keep the screen on", "Не гасить экран"), T("Otherwise the screen turns off as usual and only the computer stays awake", "Иначе экран погаснет как обычно, а компьютер продолжит работать"),
                         c => c.KeepAwakeScreen, (c, v) => c.KeepAwakeScreen = v),
                     HotkeyRow(T("Hotkey", "Горячая клавиша"),
                         T("Turns it on for the chosen time or turns it off. Empty by default", "Включает на выбранное время или выключает. По умолчанию не задана"),
                         c => c.KeepAwakeHotkey, (c, v) => c.KeepAwakeHotkey = v)),
                Section(T("Always on top", "Поверх всех окон")),
                Card(ToggleRow(T("Button on the title bar", "Кнопка на заголовке окна"),
                         T("Right-click a window's title bar, and a “Keep on top” button appears above the cursor",
                           "Нажмите правой кнопкой на заголовок окна, и над курсором появится кнопка «Поверх всех окон»"),
                         c => c.PinOnTitleBar, (c, v) => c.PinOnTitleBar = v),
                     HotkeyRow(T("Hotkey", "Горячая клавиша"),
                         T("Pins the active window or unpins it. Empty by default", "Закрепляет активное окно или открепляет его. По умолчанию не задана"),
                         c => c.PinHotkey, (c, v) => c.PinHotkey = v)),
                Section(T("Paste without formatting", "Вставка без форматирования")),
                Card(HotkeyRow(T("Hotkey", "Горячая клавиша"),
                    T("Pastes only the text, without fonts and colors. After that the clipboard holds only the text as well. Empty by default",
                      "Вставляет только текст, без шрифтов и цветов. После этого и в буфере обмена остаётся только текст. По умолчанию не задана"),
                    c => c.PlainPasteHotkey, (c, v) => c.PlainPasteHotkey = v)));

            AddPage("system",
                Card(Row(T("Theme", "Тема"), T("Colors of this window, the tray menu and the indicator", "Цвета этого окна, меню в трее и индикатора"), SegmentBox(themes)),
                     Row(T("Language", "Язык"), T("Language of this window and the tray menu", "Язык этого окна и меню в трее"), langBox),
                     Row(T("Start with Windows", "Запускать вместе с Windows"),
                         T("The app starts quietly in the tray when you sign in", "Программа тихо стартует в трее при входе в систему"), autostart),
                     HotkeyRow(T("Pause hotkey", "Горячая клавиша паузы"),
                         T("Click the field and press a combination. Backspace removes it", "Нажмите на поле и затем сочетание клавиш. Backspace убирает его"),
                         c => c.PauseHotkey, (c, v) => c.PauseHotkey = v),
                     ToggleRow(T("Ignore input from other programs", "Не реагировать на ввод других программ"),
                         T("Text typed by AutoHotkey, Punto Switcher and similar tools is left alone",
                           "Текст, который печатают AutoHotkey, Punto Switcher и подобные, не исправляется"),
                         c => c.IgnoreInjected, (c, v) => c.IgnoreInjected = v)),
                Card(Padded(links)));

            BuildScenariosPage();
        }

        void BuildNav()
        {
            var nav = Get<StackPanel>("Nav");
            for (int i = 0; i < NavItems.GetLength(0); i++)
            {
                string key = NavItems[i, 0];
                string title = T(NavItems[i, 2], NavItems[i, 3]);
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock
                {
                    Text = NavItems[i, 1], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0)
                });
                content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
                var rb = new RadioButton { Style = St("Nav"), GroupName = "nav", Content = content, Tag = title };
                rb.Checked += delegate { ShowPage(key); };
                navButtons[key] = rb;
                nav.Children.Add(rb);
            }
        }

        void ShowPage(string key)
        {
            currentPage = key;
            foreach (var p in pages) p.Value.Visibility = p.Key == key ? Visibility.Visible : Visibility.Collapsed;
            if (navButtons[key].IsChecked != true) navButtons[key].IsChecked = true;
            Get<TextBlock>("PageTitle").Text = (string)navButtons[key].Tag;
            Get<ScrollViewer>("Scroll").ScrollToVerticalOffset(0);
        }

        void AddPage(string key, params UIElement[] items)
        {
            var page = new StackPanel { Visibility = Visibility.Collapsed };
            foreach (var item in items) page.Children.Add(item);
            pages[key] = page;
            Get<Grid>("Pages").Children.Add(page);
        }

        static string FeatureTitle(string f)
        {
            switch (f)
            {
                case "Capitalize": return T("Capitals", "Заглавные");
                case "DoubleSpacePeriod": return T("Double-space period", "Точка по двум пробелам");
                case "AutoSpace": return T("Punctuation spacing", "Пробелы у знаков");
                case "AutoHyphen": return T("Hyphens", "Дефисы");
                case "SmartQuotes": return T("Quotes «»", "Кавычки");
                case "AutoBrackets": return T("Auto brackets", "Автоскобки");
                case "CapsLockFix": return "Caps Lock";
                case "Typos": return T("Typos", "Опечатки");
                case "EnglishI": return T("“I” in English", "«I» в английском");
                case "LayoutFix": return T("Layout fix", "Раскладка");
                case "Snippets": return T("Text shortcuts", "Быстрые вставки");
            }
            return f;
        }

        void BindCheck(CheckBox cb, Func<Config, bool> get, Action<Config, bool> set)
        {
            toggles.Add(new KeyValuePair<CheckBox, Func<Config, bool>>(cb, get));
            RoutedEventHandler handler = delegate
            {
                if (loading) return;
                set(cfg, cb.IsChecked == true);
                Save();
            };
            cb.Checked += handler;
            cb.Unchecked += handler;
        }

        void Refresh()
        {
            loading = true;
            try
            {
                cfg = Config.Load(Program.ConfigPath);
                foreach (var t in toggles) t.Key.IsChecked = t.Value(cfg);
                autostart.IsChecked = Program.IsAutostart();
                int ms = cfg.DoubleSpaceIntervalMs;
                speedButtons[ms < 500 ? 0 : ms < 750 ? 1 : 2].IsChecked = true;
                bracketButtons[cfg.BracketExit == "hold" ? 1 : cfg.BracketExit == "double" ? 2 : 0].IsChecked = true;
                indicatorButtons[cfg.IndicatorSize == "large" ? 2 : cfg.IndicatorSize == "medium" ? 1 : 0].IsChecked = true;
                awakeButtons[cfg.KeepAwakeHours == 1 ? 0 : cfg.KeepAwakeHours == 3 ? 1 : 2].IsChecked = true;
                awakeToggle.IsChecked = cfg.KeepAwakeActive(DateTime.Now);
                langButtons[cfg.UiLanguage == "ru" ? 1 : 0].IsChecked = true;
                themeButtons[cfg.Theme == "light" ? 1 : cfg.Theme == "system" ? 2 : 0].IsChecked = true;
                SyncTheme();
                RefreshDependent();
            }
            finally { loading = false; }

            if ((cfg.UiLanguage == "ru") != Loc.Ru)
            {
                Loc.Apply(cfg);
                Window.Dispatcher.BeginInvoke(new Action(Rebuild));
            }
        }

        void RefreshDependent()
        {
            appChips.ItemsSource = cfg.ExcludeApps.OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase).ToList();
            abbrevChips.ItemsSource = cfg.Abbreviations.OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase).ToList();
            Dim(speedRow, cfg.DoubleSpacePeriod);
            Dim(bracketRow, cfg.AutoBrackets);
            Dim(indicatorRow, cfg.LayoutIndicator);
            Get<TextBlock>("EnabledLabel").Text = cfg.Enabled ? T("On", "Включено") : T("Paused", "На паузе");
            layoutIgnoreChips.ItemsSource = cfg.LayoutIgnore.OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase).ToList();
            appLayoutChips.ItemsSource = cfg.AppLayouts.OrderBy(a => a.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => a.Key + " · " + LayoutLabel(a.Value)).ToList();
            foreach (var refresh in hotkeyRefresh) refresh();
            RebuildPairs(correctionList, cfg.CustomTypos, k => k);
            RebuildSnippets();
            RebuildRules();
            RebuildScenarioList();
            UpdateStatus();
        }

        void Save()
        {
            cfg.Save(Program.ConfigPath);
            Program.Signal(Program.ReloadEventName);
            RefreshDependent();
        }

        void SetSpeed(int ms)
        {
            if (loading) return;
            cfg.DoubleSpaceIntervalMs = ms;
            Save();
        }

        void SetTheme(string theme)
        {
            cfg.Theme = theme;
            Save();
            SyncTheme();
        }

        void SyncTheme()
        {
            Program.Theme = cfg.Theme;
            bool now = Program.IsDark();
            if (now == dark) return;
            dark = now;
            ApplyBackdrop();
        }

        Button TokenButton(string label, string token)
        {
            var button = new Button { Style = St("Btn"), Content = label, Margin = new Thickness(0, 0, 6, 0), ToolTip = token };
            button.Click += delegate
            {
                snipText.SelectedText = token;
                snipText.CaretIndex = snipText.SelectionStart + token.Length;
                snipText.Focus();
            };
            return button;
        }

        void SetBracketExit(string mode)
        {
            cfg.BracketExit = mode;
            Save();
        }

        void SetIndicatorSize(string size)
        {
            cfg.IndicatorSize = size;
            Save();
        }

        void SetAwakeHours(int hours)
        {
            cfg.KeepAwakeHours = hours;
            if (cfg.KeepAwakeActive(DateTime.Now)) cfg.StartKeepAwake(DateTime.Now);
            Save();
        }

        static void Dim(StackPanel row, bool on)
        {
            var line = row.Children[1];
            line.IsEnabled = on;
            line.Opacity = on ? 1 : 0.45;
        }

        void SetLanguage(string lang)
        {
            if (loading || cfg.UiLanguage == lang) return;
            cfg.UiLanguage = lang;
            Save();
            Loc.Apply(cfg);
            Window.Dispatcher.BeginInvoke(new Action(Rebuild));
        }

        void UpdateStatus()
        {
            bool running = Program.IsMainRunning();
            var dot = Get<Ellipse>("StatusDot");
            var text = Get<TextBlock>("StatusText");
            if (!running)
            {
                dot.Fill = Brush("#E5534B");
                text.Text = T("Not running", "Не запущена");
            }
            else
            {
                dot.Fill = Brush("#9A9A9A");
                text.Text = T("Paused", "На паузе");
            }
            Get<StackPanel>("StatusLine").Visibility = running && cfg.Enabled ? Visibility.Collapsed : Visibility.Visible;
            Get<Button>("StartButton").Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        }

        Border CorrectionsCard()
        {
            correctionList = new StackPanel();
            return PairCard(correctionList, T("Wrong", "Как ошибаетесь"), T("Right", "Как правильно"),
                T("Add your own typical mistakes. Capitals are kept: “Teh” → “The”",
                  "Добавьте свои типичные ошибки. Заглавная сохраняется: «Извени» → «Извини»"),
                (w, r) =>
                {
                    if (w.Length == 0 || !w.All(c => char.IsLetter(c) || c == '\'' || c == '’' || c == '-'))
                        return T("The wrong spelling must be one word, letters only", "Ошибка должна быть одним словом, только из букв");
                    if (r.Length == 0) return T("Type the right spelling", "Напишите, как правильно");
                    cfg.CustomTypos[w] = r;
                    return null;
                });
        }

        Border PairCard(StackPanel list, string leftHintText, string rightHintText, string description, Func<string, string, string> tryAdd)
        {
            var left = new TextBox { Style = St("Input"), FontSize = 13 };
            var leftHint = Placeholder(leftHintText);
            left.TextChanged += delegate { leftHint.Visibility = left.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            var right = new TextBox { Style = St("Input"), FontSize = 13 };
            var rightHint = Placeholder(rightHintText);
            right.TextChanged += delegate { rightHint.Visibility = right.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            var error = Desc("", 0, 0);
            error.Foreground = Brush("#E5534B");
            error.Margin = new Thickness(0, 8, 0, 0);
            error.Visibility = Visibility.Collapsed;
            var add = new Button { Style = St("AccentBtn"), Content = T("Add", "Добавить"), Padding = new Thickness(18, 6, 18, 6) };

            Action commit = () =>
            {
                string message = tryAdd(left.Text.Trim(), right.Text.Trim());
                error.Text = message ?? "";
                error.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
                if (message != null) return;
                left.Clear();
                right.Clear();
                left.Focus();
                Save();
            };
            add.Click += delegate { commit(); };
            KeyEventHandler onEnter = (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                commit();
                e.Handled = true;
            };
            left.KeyDown += onEnter;
            right.KeyDown += onEnter;

            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition());
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition());
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var leftBox = Overlay(left, leftHint, new Thickness(0));
            var arrow = Themed(new TextBlock { Text = "→", Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center },
                               TextBlock.ForegroundProperty, "TextSecondary");
            var rightBox = Overlay(right, rightHint, new Thickness(0));
            add.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(arrow, 1);
            Grid.SetColumn(rightBox, 2);
            Grid.SetColumn(add, 3);
            form.Children.Add(leftBox);
            form.Children.Add(arrow);
            form.Children.Add(rightBox);
            form.Children.Add(add);

            return Card(list, Padded(Stack(Desc(description, 0, 10), form, error)));
        }

        void RebuildPairs(StackPanel list, Dictionary<string, string> pairs, Func<string, string> show)
        {
            list.Children.Clear();
            if (pairs.Count == 0)
            {
                list.Children.Add(Padded(Desc(T("Nothing here yet.", "Пока пусто."), 0, 0)));
                return;
            }
            bool first = true;
            foreach (var pair in pairs.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!first) list.Children.Add(new Border { Style = St("Divider") });
                first = false;
                string key = pair.Key;
                var grid = new Grid { Margin = new Thickness(16, 7, 12, 7) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.Children.Add(new TextBlock { Text = show(key) + "  →  " + pair.Value, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var del = new Button { Style = St("IconBtn"), ToolTip = T("Delete", "Удалить"), VerticalAlignment = VerticalAlignment.Center };
                del.Click += delegate
                {
                    pairs.Remove(key);
                    Save();
                };
                Grid.SetColumn(del, 1);
                grid.Children.Add(del);
                list.Children.Add(grid);
            }
        }

        Border AppLayoutsCard()
        {
            appLayoutChips = Chips(text =>
            {
                int at = text.IndexOf(" · ", StringComparison.Ordinal);
                if (at > 0 && cfg.AppLayouts.Remove(text.Substring(0, at))) Save();
            });
            return Card(ToggleRow(T("Remember the layout for each app", "Запоминать раскладку для каждой программы"),
                    T("The layout you last typed with in an app comes back when you return to it",
                      "Раскладка, на которой вы печатали в программе, возвращается, когда вы снова в неё переходите"),
                    c => c.LayoutPerApp, (c, v) => c.LayoutPerApp = v),
                Padded(Stack(Desc(T("Remembered so far. Remove an app to make the program forget its layout",
                                    "Уже запомнено. Уберите программу, чтобы её раскладка забылась"), 0, 10), appLayoutChips)));
        }

        static string LayoutLabel(string id)
        {
            int lang;
            return int.TryParse(id, System.Globalization.NumberStyles.HexNumber, null, out lang) ? Hooks.LangLabel(lang) : id;
        }

        Border LayoutIgnoreCard()
        {
            layoutIgnoreChips = Chips(name => { if (cfg.LayoutIgnore.Remove(name)) Save(); });
            var input = new TextBox { Style = St("Input"), FontSize = 13 };
            var hint = Placeholder(T("New word, then Enter", "Новое слово и Enter"));
            input.TextChanged += delegate { hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            input.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                string word = input.Text.Trim().ToLowerInvariant();
                input.Clear();
                if (word.Length > 0 && cfg.LayoutIgnore.Add(word)) Save();
                e.Handled = true;
            };
            var box = Overlay(input, hint, new Thickness(0, 4, 0, 0));
            box.Width = 280;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            return Card(Padded(Stack(
                Desc(T("Nicknames, brands and other words the layout fix should leave alone, as you type them",
                       "Ники, названия и другие слова, которые исправление раскладки не должно менять. Пишите их так, как набираете"), 0, 12),
                layoutIgnoreChips, box)));
        }

        UIElement HotkeyRow(string title, string description, Func<Config, string> get, Action<Config, string> set)
        {
            var box = new TextBox
            {
                Style = St("Input"), FontSize = 13, Width = 170, IsReadOnly = true,
                IsReadOnlyCaretVisible = false, TextAlignment = TextAlignment.Center
            };
            var error = Desc("", 16, 12);
            error.Foreground = Brush("#E5534B");
            error.Visibility = Visibility.Collapsed;
            box.PreviewKeyDown += (s, e) => OnHotkeyKey(e, error, get, set);
            hotkeyRefresh.Add(() => box.Text = string.IsNullOrEmpty(get(cfg)) ? T("None", "Нет") : get(cfg));
            return Stack(Row(title, description, box), error);
        }

        void OnHotkeyKey(KeyEventArgs e, TextBlock errorText, Func<Config, string> get, Action<Config, string> set)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab) return;
            e.Handled = true;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;

            errorText.Visibility = Visibility.Collapsed;
            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None && key == Key.Escape)
            {
                Keyboard.ClearFocus();
                return;
            }
            if (mods == ModifierKeys.None && (key == Key.Back || key == Key.Delete))
            {
                set(cfg, "");
                Save();
                return;
            }

            string text = Hotkey.Format((mods & ModifierKeys.Control) != 0, (mods & ModifierKeys.Alt) != 0,
                (mods & ModifierKeys.Shift) != 0, (mods & ModifierKeys.Windows) != 0, KeyInterop.VirtualKeyFromKey(key));
            string error = text == null ? T("Use Ctrl, Alt or Win with a letter, a digit or an F key", "Нужен Ctrl, Alt или Win вместе с буквой, цифрой или клавишей F")
                : text != get(cfg) && !HotkeyFree(text) ? T("This combination is already taken", "Это сочетание уже занято")
                : null;
            if (error != null)
            {
                errorText.Text = error;
                errorText.Visibility = Visibility.Visible;
                return;
            }
            set(cfg, text);
            Save();
        }

        bool HotkeyFree(string text)
        {
            uint mods, vk;
            if (!Hotkey.TryParse(text, out mods, out vk)) return false;
            IntPtr hwnd = new WindowInteropHelper(Window).Handle;
            if (!Native.RegisterHotKey(hwnd, 77, mods, vk)) return false;
            Native.UnregisterHotKey(hwnd, 77);
            return true;
        }

        void RebuildSnippets()
        {
            snippetList.Children.Clear();
            if (cfg.SnippetList.Count == 0)
            {
                snippetList.Children.Add(Padded(Desc(T("Nothing here yet. Add your first shortcut below.", "Пока пусто. Добавьте первую вставку ниже."), 0, 0)));
                return;
            }
            bool first = true;
            foreach (var pair in cfg.SnippetList.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!first) snippetList.Children.Add(new Border { Style = St("Divider") });
                first = false;
                string key = pair.Key;

                var grid = new Grid { Margin = new Thickness(6, 4, 6, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var trigger = Themed(new Border
                {
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = key, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13 }
                }, Border.BackgroundProperty, "ChipBg");
                var preview = Themed(new TextBlock
                {
                    Text = "→  " + pair.Value.Replace("\n", " ⏎ "), TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center
                }, TextBlock.ForegroundProperty, "TextSecondary");
                var del = new Button { Style = St("IconBtn"), ToolTip = T("Delete", "Удалить"), VerticalAlignment = VerticalAlignment.Center };
                del.Click += (s, e) =>
                {
                    e.Handled = true;
                    if (editingSnippet == key) EditSnippet(null);
                    cfg.SnippetList.Remove(key);
                    Save();
                };
                Grid.SetColumn(preview, 1);
                Grid.SetColumn(del, 2);
                grid.Children.Add(trigger);
                grid.Children.Add(preview);
                grid.Children.Add(del);

                var row = new Button { Style = St("ListBtn"), Content = grid, ToolTip = T("Click to edit", "Нажмите, чтобы изменить") };
                row.Click += delegate { EditSnippet(key); };
                snippetList.Children.Add(new Border { Padding = new Thickness(4), Child = row });
            }
        }

        void EditSnippet(string key)
        {
            editingSnippet = key;
            snipError.Visibility = Visibility.Collapsed;
            snipTrigger.Text = key ?? "";
            snipText.Text = key == null ? "" : cfg.SnippetList[key].Replace("\n", Environment.NewLine);
            snipTitle.Text = key == null ? T("Add a shortcut", "Добавить вставку") : T("Edit the shortcut", "Изменить вставку");
            snipSave.Content = key == null ? T("Add", "Добавить") : T("Save", "Сохранить");
            snipCancel.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
            if (key != null) snipTrigger.Focus();
        }

        void SaveSnippet()
        {
            string trigger = snipTrigger.Text.Trim();
            string text = snipText.Text.Replace("\r\n", "\n").TrimEnd('\n');
            string error = trigger.Length == 0 ? T("Type a shortcut", "Напишите сокращение")
                : trigger.Any(char.IsWhiteSpace) ? T("A shortcut cannot contain spaces", "В сокращении не должно быть пробелов")
                : text.Trim().Length == 0 ? T("Type the text to replace the shortcut with", "Напишите текст, которым заменять сокращение")
                : null;
            if (error != null)
            {
                snipError.Text = error;
                snipError.Visibility = Visibility.Visible;
                return;
            }
            if (editingSnippet != null) cfg.SnippetList.Remove(editingSnippet);
            cfg.SnippetList[trigger] = text;
            EditSnippet(null);
            Save();
        }

        void RebuildRules()
        {
            ruleList.Children.Clear();
            foreach (var rule in cfg.AppRules.OrderBy(r => r.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                string app = rule.Key;
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition());
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.Children.Add(new TextBlock { Text = app, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Style = St("Link"), Content = T("Delete rule", "Удалить правило") };
                remove.Click += delegate
                {
                    cfg.AppRules.Remove(app);
                    Save();
                };
                Grid.SetColumn(remove, 1);
                header.Children.Add(remove);

                var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                foreach (string f in Config.Features)
                {
                    string feature = f;
                    var chip = new ToggleButton { Style = St("FeatureChip"), Content = FeatureTitle(f), IsChecked = !rule.Value.Contains(f) };
                    RoutedEventHandler changed = delegate
                    {
                        if (chip.IsChecked == true) cfg.AppRules[app].Remove(feature);
                        else cfg.AppRules[app].Add(feature);
                        Save();
                    };
                    chip.Checked += changed;
                    chip.Unchecked += changed;
                    chips.Children.Add(chip);
                }
                ruleList.Children.Add(Card(Padded(Stack(header, chips))));
            }
        }

        async void OpenPicker(Button anchor, string title, Action<string> onPick, Func<string, bool> filter)
        {
            var popup = Get<Popup>("Picker");
            pickerTarget = onPick;
            pickerFilter = filter;
            Get<TextBlock>("PickerTitle").Text = title;
            popup.PlacementTarget = anchor;
            var list = Get<ItemsControl>("RunningApps");
            list.ItemsSource = null;
            popup.IsOpen = true;
            var all = await Task.Run(() => RunningApps());
            list.ItemsSource = all.Where(a => pickerFilter(a.Name)).ToList();
        }

        void PickApp(string name)
        {
            Get<Popup>("Picker").IsOpen = false;
            if (pickerTarget != null) pickerTarget(name);
        }

        static List<AppItem> RunningApps()
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "DeskHand", "ApplicationFrameHost", "TextInputHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost"
            };
            int session = Process.GetCurrentProcess().SessionId;
            var items = new List<AppItem>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.SessionId != session || p.MainWindowHandle == IntPtr.Zero) continue;
                    string name = p.ProcessName;
                    if (!skip.Add(name)) continue;
                    var item = new AppItem { Name = name, Title = name };
                    try
                    {
                        string path = p.MainModule.FileName;
                        string desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                        if (!string.IsNullOrWhiteSpace(desc)) item.Title = desc.Trim();
                        item.Icon = IconOf(path);
                    }
                    catch { }
                    items.Add(item);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static ImageSource IconOf(string path)
        {
            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
        }

        Grid FeatureRow(string feature, string title, string desc, bool load = false, string hint = null)
        {
            return ToggleRow(title, desc, c => c.GetFeature(feature), (c, v) => c.SetFeature(feature, v), load, hint);
        }

        Grid ToggleRow(string title, string desc, Func<Config, bool> get, Action<Config, bool> set, bool load = false, string hint = null)
        {
            var cb = new CheckBox { Style = St("Toggle") };
            BindCheck(cb, get, set);
            return Row(title, desc, cb, load, hint);
        }

        UIElement InfoIcon(string hint)
        {
            var icon = Themed(new TextBlock
            {
                Text = "\uE946", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14,
                Margin = new Thickness(8, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Help
            }, TextBlock.ForegroundProperty, "TextSecondary");
            icon.MouseEnter += delegate { ShowHint(icon, hint); };
            icon.MouseLeave += delegate { HideHint(); };
            return icon;
        }

        sealed class HintFrame
        {
            public BitmapSource Image;
            public int DelayMs;
        }

        async void ShowHint(UIElement anchor, string name)
        {
            int seq = ++hintSeq;
            await Task.Delay(180);
            if (seq != hintSeq) return;
            var frames = await Task.Run(() => LoadHint(name));
            if (seq != hintSeq || frames.Count == 0) return;

            if (hintPopup == null)
            {
                hintImage = new Image { Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(hintImage, BitmapScalingMode.HighQuality);
                hintPopup = new System.Windows.Controls.Primitives.Popup
                {
                    AllowsTransparency = true, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, IsHitTestVisible = false,
                    Child = new Border
                    {
                        CornerRadius = new CornerRadius(8), Margin = new Thickness(6, 6, 6, 12), ClipToBounds = true, Child = hintImage,
                        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.35 }
                    }
                };
            }
            hintImage.Width = 320;
            hintImage.Height = 320.0 * frames[0].Image.PixelHeight / frames[0].Image.PixelWidth;
            int at = 0;
            hintImage.Source = frames[0].Image;
            hintPopup.PlacementTarget = anchor;
            hintPopup.IsOpen = true;
            if (hintTimer != null) hintTimer.Stop();
            hintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(frames[0].DelayMs) };
            hintTimer.Tick += delegate
            {
                at = (at + 1) % frames.Count;
                hintImage.Source = frames[at].Image;
                hintTimer.Interval = TimeSpan.FromMilliseconds(frames[at].DelayMs);
            };
            hintTimer.Start();
        }

        void HideHint()
        {
            hintSeq++;
            if (hintTimer != null)
            {
                hintTimer.Stop();
                hintTimer = null;
            }
            if (hintPopup != null) hintPopup.IsOpen = false;
            if (hintImage != null) hintImage.Source = null;
        }

        static List<HintFrame> LoadHint(string name)
        {
            var frames = new List<HintFrame>();
            using (var stream = Resource(name + ".gif"))
            using (var gif = System.Drawing.Image.FromStream(stream))
            {
                var dimension = new System.Drawing.Imaging.FrameDimension(gif.FrameDimensionsList[0]);
                int count = gif.GetFrameCount(dimension);
                byte[] delays = gif.GetPropertyItem(0x5100).Value;
                for (int i = 0; i < count; i++)
                {
                    gif.SelectActiveFrame(dimension, i);
                    using (var bmp = new System.Drawing.Bitmap(gif))
                    {
                        IntPtr handle = bmp.GetHbitmap();
                        try
                        {
                            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            source.Freeze();
                            frames.Add(new HintFrame { Image = source, DelayMs = Math.Max(20, BitConverter.ToInt32(delays, i * 4) * 10) });
                        }
                        finally { DeleteObject(handle); }
                    }
                }
            }
            return frames;
        }

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr handle);

        Grid Row(string title, string desc, UIElement right, bool load = false, string hint = null)
        {
            var grid = new Grid { Margin = new Thickness(16, 13, 16, 13) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            if (title != null)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(Txt(title, "RowTitle"));
                if (load)
                    line.Children.Add(new Border
                    {
                        Style = St("LoadBadge"),
                        ToolTip = T("This feature uses a little extra CPU or memory. Turn it off on a slow computer",
                                    "Эта функция немного нагружает систему. Если компьютер слабый, её можно выключить"),
                        Child = new TextBlock { Text = T("Extra load", "Нагрузка"), Style = St("LoadBadgeText") }
                    });
                if (hint != null && HasResource(hint + ".gif")) line.Children.Add(InfoIcon(hint));
                left.Children.Add(line);
            }
            if (desc != null) left.Children.Add(Desc(desc, 0, 0, title != null));
            grid.Children.Add(left);
            var fe = right as FrameworkElement;
            if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            return grid;
        }

        Border SegmentBox(UIElement content)
        {
            return Themed(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(2), BorderThickness = new Thickness(1), Child = content },
                          Border.BackgroundProperty, "ControlBg", Border.BorderBrushProperty, "ControlBorder");
        }

        Grid SubRow(string text, UIElement right)
        {
            var row = Row(null, text, right);
            row.Margin = new Thickness(16, 0, 16, 14);
            return row;
        }

        RadioButton Segment(string text, string group, Action onChecked)
        {
            var rb = new RadioButton { Style = St("Segment"), GroupName = group, Content = text };
            rb.Checked += delegate { if (!loading) onChecked(); };
            return rb;
        }

        ItemsControl Chips(Action<string> onRemove)
        {
            var ic = new ItemsControl
            {
                ItemTemplate = (DataTemplate)Window.Resources["Chip"],
                ItemsPanel = (ItemsPanelTemplate)Window.Resources["Wrap"],
                Focusable = false
            };
            ic.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var tag = TagOf(e);
                if (tag != null) onRemove(tag);
            }));
            return ic;
        }

        Border Card(params UIElement[] items)
        {
            var sp = new StackPanel();
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sp.Children.Add(new Border { Style = St("Divider") });
                sp.Children.Add(items[i]);
            }
            return new Border { Style = St("Card"), Child = sp };
        }

        static Border Padded(UIElement content) { return new Border { Padding = new Thickness(16, 13, 16, 14), Child = content }; }

        static StackPanel Stack(params UIElement[] items)
        {
            var sp = new StackPanel();
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }

        static FrameworkElement Spacer(double h) { return new Border { Height = h }; }

        Grid Overlay(TextBox box, TextBlock hint, Thickness margin)
        {
            var g = new Grid { Margin = margin };
            g.Children.Add(box);
            g.Children.Add(hint);
            return g;
        }

        TextBlock Placeholder(string text)
        {
            return Themed(new TextBlock
            {
                Text = text, IsHitTestVisible = false, Margin = new Thickness(12, 7, 12, 0), FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            }, TextBlock.ForegroundProperty, "TextTertiary");
        }

        TextBlock Txt(string text, string style) { return new TextBlock { Text = text, Style = St(style) }; }

        TextBlock Section(string title) { return Txt(title, "Section"); }

        TextBlock FirstSection(string title)
        {
            var t = Section(title);
            t.Margin = new Thickness(2, 4, 0, 8);
            return t;
        }

        TextBlock Desc(string text, double left, double bottom, bool top = false)
        {
            var t = Txt(text, "RowDesc");
            t.Margin = new Thickness(left, top ? 2 : 0, 0, bottom);
            return t;
        }

        TextBlock Note(string text)
        {
            var t = Desc(text, 2, 0);
            t.Margin = new Thickness(2, 12, 0, 0);
            return t;
        }

        Style St(string key) { return (Style)Window.Resources[key]; }

        static T Themed<T>(T element, DependencyProperty p1, string key1, DependencyProperty p2 = null, string key2 = null) where T : FrameworkElement
        {
            element.SetResourceReference(p1, key1);
            if (p2 != null) element.SetResourceReference(p2, key2);
            return element;
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                SyncTheme();
            }));
        }

        void ApplyBackdrop()
        {
            IntPtr hwnd = new WindowInteropHelper(Window).Handle;
            int on = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref on, 4);
            int caption = dark ? 0x00202020 : 0x00F3F3F3;
            DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            ApplyTheme();
        }

        void ApplyTheme()
        {
            var r = Window.Resources;
            if (dark)
            {
                r["WindowBg"] = Brush("#202020");
                r["CardBg"] = Brush("#2B2B2B");
                r["CardBorder"] = Brush("#19000000");
                r["DividerBrush"] = Brush("#14FFFFFF");
                r["Text"] = Brush("#E6E6E6");
                r["TextSecondary"] = Brush("#94FFFFFF");
                r["TextTertiary"] = Brush("#6BFFFFFF");
                r["ControlBg"] = Brush("#0FFFFFFF");
                r["ControlHover"] = Brush("#18FFFFFF");
                r["ControlBorder"] = Brush("#1CFFFFFF");
                r["NavSelected"] = Brush("#14FFFFFF");
                r["InputBg"] = Brush("#0BFFFFFF");
                r["InputFocusBg"] = Brush("#1F1F1F");
                r["SegmentOn"] = Brush("#22FFFFFF");
                r["ChipBg"] = Brush("#0FFFFFFF");
                r["ChipOn"] = Brush("#22FFFFFF");
                r["ChipOnBorder"] = Brush("#70FFFFFF");
                r["PopupBg"] = Brush("#323232");
                r["PopupBorder"] = Brush("#3DFFFFFF");
                r["ToggleOffBorder"] = Brush("#70FFFFFF");
                r["ToggleOffFill"] = Brush("#08FFFFFF");
                r["ToggleOffThumb"] = Brush("#A6FFFFFF");
                r["Accent"] = Brush("#C9BE98");
                r["AccentThumb"] = Brush("#26261F");
                r["AccentLink"] = Brush("#CFC5A4");
                r["BadgeBg"] = Brush("#26FFB347");
                r["BadgeText"] = Brush("#FFC97A");
                r["ScrollThumb"] = Brush("#55FFFFFF");
            }
            else
            {
                r["WindowBg"] = Brush("#F3F3F3");
                r["CardBg"] = Brush("#FFFFFF");
                r["CardBorder"] = Brush("#0F000000");
                r["DividerBrush"] = Brush("#0F000000");
                r["Text"] = Brush("#1F1F1F");
                r["TextSecondary"] = Brush("#A6000000");
                r["TextTertiary"] = Brush("#78000000");
                r["ControlBg"] = Brush("#B3FFFFFF");
                r["ControlHover"] = Brush("#0A000000");
                r["ControlBorder"] = Brush("#1F000000");
                r["NavSelected"] = Brush("#0F000000");
                r["InputBg"] = Brush("#B3FFFFFF");
                r["InputFocusBg"] = Brush("#FFFFFF");
                r["SegmentOn"] = Brush("#17000000");
                r["ChipBg"] = Brush("#0A000000");
                r["ChipOn"] = Brush("#14000000");
                r["ChipOnBorder"] = Brush("#70000000");
                r["PopupBg"] = Brush("#F9F9F9");
                r["PopupBorder"] = Brush("#26000000");
                r["ToggleOffBorder"] = Brush("#9E000000");
                r["ToggleOffFill"] = Brush("#05000000");
                r["ToggleOffThumb"] = Brush("#C2000000");
                r["Accent"] = Brush("#5F6148");
                r["AccentThumb"] = Brush("#FFFFFF");
                r["AccentLink"] = Brush("#4F513B");
                r["BadgeBg"] = Brush("#1FE08A00");
                r["BadgeText"] = Brush("#9A5A00");
                r["ScrollThumb"] = Brush("#5A000000");
            }
        }

        T Get<T>(string name) where T : class { return (T)Window.FindName(name); }

        static string TagOf(RoutedEventArgs e)
        {
            var b = e.OriginalSource as Button;
            return b == null ? null : b.Tag as string;
        }

        static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        static Stream Resource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskHand." + name);
        }

        static bool HasResource(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceInfo("DeskHand." + name) != null;
        }

        void Later(int ms, Action action)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); action(); };
            t.Start();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
