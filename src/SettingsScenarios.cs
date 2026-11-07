using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskHand
{
    sealed partial class SettingsWindow
    {
        const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

        StackPanel scenarioList, scenarioEditor, stepsPanel;
        TextBlock scenarioStatus, scenarioError;
        Scenario editing;
        string editingName, editingHotkey;
        bool scheduleOn, appTriggerOn, closeTriggerOn, idleOn, eventOn;
        int segmentGroup;

        void BuildScenariosPage()
        {
            scenarioList = new StackPanel();
            scenarioEditor = new StackPanel();
            scenarioStatus = Desc("", 2, 0);
            scenarioStatus.Foreground = Brush("#E5534B");
            scenarioStatus.Margin = new Thickness(2, 8, 0, 0);
            scenarioStatus.Visibility = Visibility.Collapsed;

            var add = new Button { Style = St("Btn"), Content = T("+  New scenario", "+  Новый сценарий") };
            add.Click += delegate { EditScenario(new Scenario(), null); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(add);

            AddPage("scenarios",
                Card(Padded(Stack(
                    Txt(T("Steps one after another", "Шаги по очереди"), "RowTitle"),
                    Desc(T("A scenario does several things for you in a row: opens programs, folders and sites, waits, types text and presses keys. Run it with a hotkey, from the tray menu, on a schedule, when Windows starts or when you open a program",
                           "Сценарий сам выполняет несколько действий подряд: открывает программы, папки и сайты, ждёт, печатает текст и нажимает клавиши. Запускается горячей клавишей, из меню в трее, по расписанию, при входе в Windows или когда открываете программу"), 0, 0, true)))),
                Section(T("Your scenarios", "Ваши сценарии")),
                Card(scenarioList),
                scenarioStatus,
                buttons,
                scenarioEditor,
                Note(T("A running scenario can be stopped from the tray menu: Scenarios → Stop running scenarios.",
                       "Запущенный сценарий можно остановить в меню в трее: «Сценарии» → «Остановить сценарии».")));
        }

        void RebuildScenarioList()
        {
            scenarioList.Children.Clear();
            if (cfg.Scenarios.Count == 0)
            {
                scenarioList.Children.Add(Padded(Desc(T("Nothing here yet. Press “New scenario” below.",
                                                        "Пока пусто. Нажмите «Новый сценарий» ниже."), 0, 0)));
                return;
            }
            bool first = true;
            foreach (var sc in cfg.Scenarios)
            {
                if (!first) scenarioList.Children.Add(new Border { Style = St("Divider") });
                first = false;
                string name = sc.Name;

                var grid = new Grid { Margin = new Thickness(6, 2, 2, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(Themed(new TextBlock
                {
                    Text = Summary(sc), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
                }, TextBlock.ForegroundProperty, "TextSecondary"));
                grid.Children.Add(text);

                var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (sc.Hotkey.Length > 0) chips.Children.Add(ListChip(null, sc.Hotkey));
                if (sc.Time.Length > 0) chips.Children.Add(ListChip("\uE823", sc.Time));
                if (sc.WhenOpened.Length > 0) chips.Children.Add(ListChip("\uE8A7", sc.WhenOpened));
                if (sc.WhenClosed.Length > 0) chips.Children.Add(ListChip("\uE8BB", sc.WhenClosed));
                if (sc.Idle.Length > 0) chips.Children.Add(ListChip("\uE916", DurationText(sc.Idle)));
                if (sc.Event.Length > 0) chips.Children.Add(ListChip("\uE945", EventTitle(sc.Event)));
                if (sc.OnStartup) chips.Children.Add(ListChip("\uE7E8", T("Windows start", "Вход в Windows")));
                Grid.SetColumn(chips, 1);
                grid.Children.Add(chips);

                var copy = GlyphButton("\uE8C8", T("Make a copy", "Сделать копию"));
                copy.Margin = new Thickness(0, 0, 4, 0);
                var source = sc;
                copy.Click += (s2, e2) =>
                {
                    e2.Handled = true;
                    DuplicateScenario(source);
                };
                var link = GlyphButton("\uE71B", T("Shortcut on the desktop. A double click runs the scenario", "Ярлык на рабочем столе. Двойной клик запускает сценарий"));
                link.Margin = new Thickness(0, 0, 4, 0);
                link.Click += (s2, e2) =>
                {
                    e2.Handled = true;
                    CreateShortcut(name);
                };
                var run = GlyphButton("\uE768", T("Run now", "Запустить сейчас"));
                run.Margin = new Thickness(0, 0, 6, 0);
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(copy);
                actions.Children.Add(link);
                run.Click += (s, e) =>
                {
                    e.Handled = true;
                    RunScenario(name);
                };
                actions.Children.Add(run);
                Grid.SetColumn(actions, 2);
                grid.Children.Add(actions);

                var del = new Button { Style = St("IconBtn"), ToolTip = T("Delete", "Удалить"), VerticalAlignment = VerticalAlignment.Center };
                del.Click += (s, e) =>
                {
                    e.Handled = true;
                    cfg.Scenarios.RemoveAll(x => x.Name == name);
                    if (editingName == name) CloseEditor();
                    Save();
                };
                Grid.SetColumn(del, 3);
                grid.Children.Add(del);

                var row = new Button { Style = St("ListBtn"), Content = grid, ToolTip = T("Click to edit", "Нажмите, чтобы изменить") };
                var scenario = sc;
                row.Click += delegate { EditScenario(scenario, name); };
                scenarioList.Children.Add(new Border { Padding = new Thickness(4), Child = row });
            }
        }

        UIElement ListChip(string glyph, string text)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null)
                line.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
            line.Children.Add(new TextBlock { Text = text, FontSize = 12, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis });
            return Themed(new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, Child = line
            }, Border.BackgroundProperty, "ChipBg");
        }

        void RunScenario(string name)
        {
            if (Program.RequestRun(name)) ShowStatus(null, false);
            else ShowStatus(T("DeskHand is not running, so the scenario cannot run. Start it at the top of this window",
                              "DeskHand не запущена, поэтому сценарий не выполнится. Запустите её вверху окна"), true);
        }

        void ShowStatus(string text, bool error)
        {
            scenarioStatus.Text = text ?? "";
            scenarioStatus.Foreground = error ? Brush("#E5534B") : (Brush)Window.Resources["TextSecondary"];
            scenarioStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        void DuplicateScenario(Scenario source)
        {
            var copy = source.Clone();
            string baseName = source.Name + T(" (copy)", " (копия)");
            string name = baseName;
            for (int n = 2; cfg.Scenarios.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); n++) name = baseName.TrimEnd(')') + " " + n + ")";
            copy.Name = name;
            copy.Hotkey = "";
            int at = cfg.Scenarios.FindIndex(x => x.Name == source.Name);
            cfg.Scenarios.Insert(at < 0 ? cfg.Scenarios.Count : at + 1, copy);
            Save();
            ShowStatus(T("Copy made: ", "Копия создана: ") + name + T(". The hotkey is not copied, so the two do not clash", ". Горячая клавиша не копируется, чтобы они не мешали друг другу"), false);
        }

        void CreateShortcut(string name)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string file = new string(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
                if (file.Length == 0) file = "Scenario";
                string path = System.IO.Path.Combine(desktop, file + ".lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object link = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                    Type t = link.GetType();
                    t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Program.ExePath });
                    t.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { "--run \"" + name + "\"" });
                    t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { System.IO.Path.GetDirectoryName(Program.ExePath) });
                    t.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Program.ExePath + ",0" });
                    t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { T("Run the DeskHand scenario ", "Запустить сценарий DeskHand ") + name });
                    t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(link);
                }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
                ShowStatus(T("The shortcut is on the desktop: ", "Ярлык появился на рабочем столе: ") + file + T(". If you rename the scenario, make the shortcut again", ". Если переименуете сценарий, создайте ярлык заново"), false);
            }
            catch (Exception ex)
            {
                ShowStatus(T("Could not make the shortcut: ", "Не получилось создать ярлык: ") + ex.Message, true);
            }
        }

        Button GlyphButton(string glyph, string tip)
        {
            return new Button
            {
                Style = St("Btn"), ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 12 }
            };
        }

        string Summary(Scenario sc)
        {
            if (sc.Steps.Count == 0) return T("No steps", "Нет шагов");
            string text = string.Join("  →  ", sc.Steps.Select(StepShort).ToArray());
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        string StepShort(ScenarioStep st)
        {
            switch (st.Kind)
            {
                case "site":
                    string url = st.Value.Trim();
                    int scheme = url.IndexOf("://", StringComparison.Ordinal);
                    return T("open ", "открыть ") + (scheme >= 0 ? url.Substring(scheme + 3) : url).TrimEnd('/');
                case "run": return T("run ", "запустить ") + InstalledApps.NameOf(st.Value.Trim().Trim('"'));
                case "explorer":
                    string target = st.Value.Trim().Trim('"').TrimEnd('\\');
                    if (target.Length == 0) return T("open Explorer", "открыть проводник");
                    string leaf = System.IO.Path.GetFileName(target);
                    return T("open ", "открыть ") + (leaf.Length > 0 ? leaf : target);
                case "wait": return T("wait ", "подождать ") + DurationText(st.Value);
                case "waitclose": return T("wait until ", "ждать, пока не закроется ") + (st.Value.Trim().Length > 0 ? st.Value.Trim() : T("it closes", "программа"));
                case "else": return T("otherwise", "иначе");
                case "endif": return T("end", "конец");
                case "stop": return T("stop", "стоп");
                case "click":
                    var cs2 = ClickSpot.Parse(st.Value);
                    return (st.Option == "right" ? T("right click", "правый клик") : st.Option == "double" ? T("double click", "двойной клик") : T("click", "клик"))
                        + (cs2.Mode == "window" && cs2.Program.Length > 0 ? T(" in ", " в ") + cs2.Program : T(" on the screen", " по экрану"));
                case "buffer":
                    switch (st.Option)
                    {
                        case "append": return T("add to the buffer", "добавить в буфер");
                        case "prepend": return T("add to the buffer start", "добавить в начало буфера");
                        case "fromclip": return T("clipboard to the buffer", "буфер обмена в буфер");
                        case "toclip": return T("buffer to the clipboard", "буфер в буфер обмена");
                        case "clear": return T("clear the buffer", "очистить буфер");
                    }
                    return T("write the buffer", "записать буфер");
                case "fetch": return T("text from ", "текст с ") + StepTargetName(new ScenarioStep { Kind = "site", Value = FetchPart(st, 0) });
                case "if":
                case "condition":
                    string ck;
                    bool cn;
                    int cs;
                    ScenarioCondition.Parse(st.Option, out ck, out cn, out cs);
                    return (cn ? T("if not: ", "если не: ") : T("if: ", "если: ")) + ConditionTitle(ck).ToLower();
                case "close":
                    if (st.Option == "tab") return T("close the tab", "закрыть вкладку");
                    if (st.Option == "active") return T("close the active window", "закрыть активное окно");
                    if (st.Option == "opened") return T("close the window", "закрыть окно");
                    return T("close ", "закрыть ") + (st.Value.Trim().Length > 0 ? st.Value.Trim() : T("the program", "программу"));
                case "type": return T("type text", "напечатать текст");
                case "clipboard": return T("copy text", "скопировать текст");
                case "setting":
                    string what = ToggleNames.Title(st.Option);
                    what = what.Length > 0 ? char.ToLower(what[0]) + what.Substring(1) : what;
                    return what + ": " + (st.Value == "on" ? T("on", "вкл") : st.Value == "off" ? T("off", "выкл") : T("switch", "переключить"));
                case "command": return T("run a command", "выполнить команду");
                case "waitwindow": return T("wait for ", "дождаться ") + (st.Value.Trim().Length > 0 ? st.Value.Trim() : T("the window", "окна"));
                case "window": return WindowActionShort(st.Option);
                case "repeat":
                    if (RepeatSpec.Mode(st.Option) == "if")
                    {
                        string rk, ra;
                        bool rn;
                        RepeatSpec.ParseIf(st.Option, out rk, out rn, out ra);
                        return (rn ? T("repeat while not: ", "повторять, пока не: ") : T("repeat while: ", "повторять, пока: ")) + ConditionTitle(rk).ToLower() + (ra.Length > 0 && rk != "days" ? " " + ra : "");
                    }
                    return ParseCount(st.Option) == 0 ? T("repeat forever", "повторять без конца") : T("repeat ", "повторить ") + ParseCount(st.Option) + T(" times", " раз");
                case "volume": return VolumeShort(st);
                case "keys": return T("press ", "нажать ") + st.Value;
            }
            return StepTitle(st.Kind).ToLower();
        }

        static string StepTitle(string kind)
        {
            switch (kind)
            {
                case "site": return T("Open a site", "Открыть сайт");
                case "run": return T("Run a program", "Запустить программу");
                case "command": return T("Run a command", "Выполнить команду");
                case "waitwindow": return T("Wait for a window", "Дождаться окна");
                case "waitclose": return T("Wait until it closes", "Дождаться закрытия");
                case "condition": return T("If (old)", "Если (старый вид)");
                case "if": return T("If", "Если");
                case "else": return T("Otherwise", "Иначе");
                case "endif": return T("End of “If”", "Конец «Если»");
                case "stop": return T("Stop the scenario", "Остановить сценарий");
                case "click": return T("Mouse click", "Клик мышью");
                case "buffer": return T("Scenario buffer", "Буфер сценария");
                case "fetch": return T("Take text from a site", "Взять текст с сайта");
                case "window": return T("Arrange a window", "Расставить окно");
                case "repeat": return T("Repeat steps", "Повторить шаги");
                case "clipboard": return T("Copy to clipboard", "Скопировать в буфер");
                case "volume": return T("Sound and music", "Звук и музыка");
                case "explorer": return T("Open in Explorer", "Открыть в проводнике");
                case "wait": return T("Wait", "Подождать");
                case "close": return T("Close", "Закрыть");
                case "type": return T("Type text", "Напечатать текст");
                case "keys": return T("Press keys", "Нажать клавиши");
                case "awake": return T("Stay awake", "Не засыпать");
                case "pause": return T("Pause DeskHand", "Пауза DeskHand");
                case "setting": return T("DeskHand setting", "Настройка DeskHand");
                case "layout": return T("Switch the layout", "Переключить раскладку");
                case "notice": return T("Show a note near the mouse", "Показать подпись у курсора");
                case "lock": return T("Lock the computer", "Заблокировать компьютер");
                case "screenoff": return T("Turn off the screen", "Выключить экран");
                case "power": return T("Shut down or sleep", "Выключение и сон");
            }
            return kind;
        }

        static ScenarioStep NewStep(string kind)
        {
            var st = new ScenarioStep { Kind = kind };
            switch (kind)
            {
                case "site": st.Option = "window"; st.Value = "https://"; break;
                case "wait": st.Value = "5"; break;
                case "close": st.Option = "program"; break;
                case "explorer": st.Option = "open"; break;
                case "command": st.Option = "cmd"; break;
                case "waitwindow": st.Option = "30"; break;
                case "waitclose": st.Option = ""; break;
                case "condition": st.Option = "running:stop"; break;
                case "if": st.Option = "running"; break;
                case "click": st.Option = "left"; break;
                case "buffer": st.Option = "set"; break;
                case "fetch": st.Option = "all:set"; st.Value = "https://"; break;
                case "window": st.Option = "left"; st.Value = "opened"; break;
                case "repeat": st.Option = "2"; st.Value = "1"; break;
                case "volume": st.Option = "set"; st.Value = "30"; break;
                case "awake": case "pause": st.Option = "toggle"; break;
                case "setting": st.Option = "Capitalize"; st.Value = "toggle"; break;
                case "layout": st.Option = "next"; break;
                case "power": st.Option = "sleep"; st.Value = "ask"; break;
            }
            return st;
        }

        void EditScenario(Scenario sc, string originalName)
        {
            editing = sc.Clone();
            editingName = originalName;
            editingHotkey = originalName == null ? "" : sc.Hotkey;
            scheduleOn = editing.Time.Length > 0;
            appTriggerOn = editing.WhenOpened.Length > 0;
            closeTriggerOn = editing.WhenClosed.Length > 0;
            idleOn = editing.Idle.Length > 0;
            eventOn = editing.Event.Length > 0;
            RenderEditor();
            Window.Dispatcher.BeginInvoke(new Action(() => scenarioEditor.BringIntoView()), DispatcherPriority.Loaded);
        }

        void CloseEditor()
        {
            editing = null;
            editingName = null;
            if (scenarioEditor != null) scenarioEditor.Children.Clear();
        }

        void RenderEditor()
        {
            scenarioEditor.Children.Clear();
            if (editing == null) return;

            var name = Field(editing.Name, v => editing.Name = v, T("For example, Start work", "Например, Начать работу"));
            name.Width = 260;

            var inTray = new CheckBox { Style = St("Toggle"), IsChecked = editing.InTray };
            inTray.Click += delegate { editing.InTray = inTray.IsChecked == true; };
            var onStartup = new CheckBox { Style = St("Toggle"), IsChecked = editing.OnStartup };
            onStartup.Click += delegate { editing.OnStartup = onStartup.IsChecked == true; };
            var ask = new CheckBox { Style = St("Toggle"), IsChecked = editing.Ask };
            ask.Click += delegate { editing.Ask = ask.IsChecked == true; };

            stepsPanel = new StackPanel();
            RenderSteps();

            scenarioError = Desc("", 2, 0);
            scenarioError.Foreground = Brush("#E5534B");
            scenarioError.Margin = new Thickness(2, 10, 0, 0);
            scenarioError.Visibility = Visibility.Collapsed;

            var save = new Button { Style = St("AccentBtn"), Content = T("Save", "Сохранить"), Padding = new Thickness(18, 6, 18, 6) };
            save.Click += delegate { SaveScenario(false); };
            var test = new Button { Style = St("Btn"), Content = T("Save and run", "Сохранить и запустить"), Margin = new Thickness(8, 0, 0, 0) };
            test.Click += delegate { SaveScenario(true); };
            var cancel = new Button { Style = St("Btn"), Content = T("Cancel", "Отмена"), Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { CloseEditor(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(save);
            buttons.Children.Add(test);
            buttons.Children.Add(cancel);

            scenarioEditor.Children.Add(Section(editingName == null ? T("New scenario", "Новый сценарий") : T("Edit the scenario", "Изменить сценарий")));
            scenarioEditor.Children.Add(Card(
                Row(T("Name", "Название"), T("Shown in the tray menu", "Так он будет называться в меню в трее"), name),
                ScenarioHotkeyRow(),
                Row(T("Show in the tray menu", "Показывать в меню в трее"), T("Right-click the tray icon and choose Scenarios", "Правый клик по значку в трее, пункт «Сценарии»"), inTray),
                Row(T("Ask before running", "Спросить перед запуском"),
                    T("A small window in the corner of the screen asks whether to run the scenario. Handy for schedules and events",
                      "В углу экрана появится окно с вопросом, запускать ли сценарий. Удобно для расписания и событий"), ask)));
            scenarioEditor.Children.Add(Section(T("When else to run", "Когда ещё запускать")));
            scenarioEditor.Children.Add(Card(
                Row(T("When Windows starts", "При входе в Windows"),
                    T("A few seconds after DeskHand starts. Turn on “Start with Windows” in System for this",
                      "Через несколько секунд после запуска DeskHand. Для этого включите «Запускать вместе с Windows» в разделе «Система»"), onStartup),
                ScheduleRow(),
                AppTriggerRow(),
                CloseTriggerRow(),
                IdleRow(),
                EventRow()));
            scenarioEditor.Children.Add(Section(T("Steps", "Шаги")));
            scenarioEditor.Children.Add(Card(stepsPanel));
            scenarioEditor.Children.Add(AddStepButton());
            scenarioEditor.Children.Add(scenarioError);
            scenarioEditor.Children.Add(buttons);
        }

        UIElement ScheduleRow()
        {
            var toggle = new CheckBox { Style = St("Toggle"), IsChecked = scheduleOn };
            toggle.Click += delegate
            {
                scheduleOn = toggle.IsChecked == true;
                if (scheduleOn && editing.Time.Length == 0) editing.Time = "09:00";
                RenderEditor();
            };
            var row = Row(T("On a schedule", "По расписанию"),
                T("At the set time on the days you pick. The computer has to be on and DeskHand running",
                  "В заданное время в отмеченные дни. Компьютер должен быть включён, а DeskHand запущена"), toggle);
            if (!scheduleOn) return row;

            var time = Field(editing.Time, v => editing.Time = v.Trim(), "09:00");
            time.Width = 90;
            var days = new WrapPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            string[] names = Loc.Ru ? new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" } : new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int i = 0; i < 7; i++)
            {
                int day = i;
                var chip = new ToggleButton { Style = St("FeatureChip"), Content = names[i], IsChecked = editing.Days[i] == '1', Margin = new Thickness(0, 0, 6, 0) };
                RoutedEventHandler changed = delegate
                {
                    var d = editing.Days.ToCharArray();
                    d[day] = chip.IsChecked == true ? '1' : '0';
                    editing.Days = new string(d);
                };
                chip.Checked += changed;
                chip.Unchecked += changed;
                days.Children.Add(chip);
            }
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 14) };
            line.Children.Add(time);
            line.Children.Add(days);
            return Stack(row, line);
        }

        UIElement AppTriggerRow()
        {
            var toggle = new CheckBox { Style = St("Toggle"), IsChecked = appTriggerOn };
            toggle.Click += delegate
            {
                appTriggerOn = toggle.IsChecked == true;
                RenderEditor();
            };
            var row = Row(T("When I open a program", "Когда открываю программу"),
                T("Runs once each time the program starts and its window shows up. Add several programs and any of them will start it",
                  "Срабатывает один раз при каждом запуске программы, когда появляется её окно. Можно добавить несколько, сработает любая"), toggle);
            if (!appTriggerOn) return row;

            var line = ProgramChips(() => editing.WhenOpened, v => editing.WhenOpened = v, null, RenderEditor);
            ((FrameworkElement)line).Margin = new Thickness(16, 0, 16, 14);
            return Stack(row, line);
        }

        UIElement CloseTriggerRow()
        {
            var toggle = new CheckBox { Style = St("Toggle"), IsChecked = closeTriggerOn };
            toggle.Click += delegate
            {
                closeTriggerOn = toggle.IsChecked == true;
                RenderEditor();
            };
            var row = Row(T("When I close a program", "Когда закрываю программу"),
                T("Runs when the last window and process of the program are gone. Add several programs and any of them will start it",
                  "Срабатывает, когда программа закрылась полностью. Можно добавить несколько, сработает любая"), toggle);
            if (!closeTriggerOn) return row;
            var line = ProgramChips(() => editing.WhenClosed, v => editing.WhenClosed = v, null, RenderEditor);
            ((FrameworkElement)line).Margin = new Thickness(16, 0, 16, 14);
            return Stack(row, line);
        }

        UIElement ProgramChips(Func<string> get, Action<string> set, string emptyHint, Action rerender)
        {
            var names = Scenario.Names(get()).ToList();
            var chips = Chips(n =>
            {
                set(string.Join(", ", Scenario.Names(get()).Where(x => !x.Equals(n, StringComparison.OrdinalIgnoreCase)).ToArray()));
                rerender();
            });
            chips.ItemsSource = names;
            Action<string> addName = raw =>
            {
                string n = (raw ?? "").Trim();
                if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
                if (n.Length == 0 || Scenario.Names(get()).Contains(n, StringComparer.OrdinalIgnoreCase)) return;
                string now = get().Trim().TrimEnd(',').Trim();
                set(now.Length == 0 ? n : now + ", " + n);
                rerender();
            };
            var add = new Button { Style = St("Btn"), Content = T("+  Add a program", "+  Добавить программу") };
            add.Click += delegate
            {
                OpenPicker(add, T("Choose a program:", "Выберите программу:"), addName, n => !Scenario.Names(get()).Contains(n, StringComparer.OrdinalIgnoreCase));
            };
            var input = new TextBox { Style = St("Input"), FontSize = 13 };
            var hint = Placeholder(T("or type a name and press Enter", "или впишите имя и нажмите Enter"));
            hint.TextWrapping = TextWrapping.NoWrap;
            hint.TextTrimming = TextTrimming.CharacterEllipsis;
            input.TextChanged += delegate { hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            input.KeyDown += (sender, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                addName(input.Text);
            };
            var box = Overlay(input, hint, new Thickness(8, 0, 0, 0));
            box.Width = 280;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(add);
            line.Children.Add(box);
            var panel = new StackPanel();
            if (names.Count > 0) panel.Children.Add(chips);
            else if (emptyHint != null) panel.Children.Add(Desc(emptyHint, 2, 8));
            panel.Children.Add(line);
            return panel;
        }

        UIElement IdleRow()
        {
            var toggle = new CheckBox { Style = St("Toggle"), IsChecked = idleOn };
            toggle.Click += delegate
            {
                idleOn = toggle.IsChecked == true;
                if (idleOn && editing.Idle.Length == 0) editing.Idle = "10m";
                RenderEditor();
            };
            var row = Row(T("When I step away from the computer", "Когда отхожу от компьютера"),
                T("You count as away when the mouse and keyboard have not been touched for the time you set",
                  "Считается, что вы отошли, если мышь и клавиатура не трогались заданное время"), toggle);
            if (!idleOn) return row;

            var explain = Desc("", 2, 0, true);
            Action update = () =>
            {
                string d = DurationText(editing.Idle);
                explain.Text = editing.IdleEvent == "back"
                    ? T("The scenario runs when you touch the mouse or keyboard again after a break of " + d + " or more. For example, to turn the music back on",
                        "Сценарий запустится, когда вы снова тронете мышь или клавиатуру после перерыва от " + d + ". Например, чтобы снова включить музыку")
                    : T("The scenario runs " + d + " after the last mouse move or key press. For example, to pause the music or lock the computer",
                        "Сценарий запустится через " + d + " после последнего движения мыши или нажатия клавиши. Например, чтобы поставить музыку на паузу или заблокировать компьютер");
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(Muted(T("I am away after", "Я отошёл, если ничего не трогаю"), 0, 10));
            line.Children.Add(DurationField(() => editing.Idle, v => { editing.Idle = v; update(); }, "10"));
            var when = Choice(() => editing.IdleEvent, v => { editing.IdleEvent = v; update(); },
                new[] { T("Run when I leave", "Запустить, когда я отошёл"), T("Run when I come back", "Запустить, когда я вернулся") }, new[] { "away", "back" }, false);
            update();
            var panel = Stack(line, Gap(when), explain);
            panel.Margin = new Thickness(16, 0, 16, 14);
            return Stack(row, panel);
        }

        sealed class PickItem
        {
            public string Value, Title, Blurb, Glyph, Color;
        }

        static PickItem Item(string value, string title, string blurb, string glyph, string color)
        {
            return new PickItem { Value = value, Title = title, Blurb = blurb, Glyph = glyph, Color = color };
        }

        Grid PickRow(PickItem item)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = Brush("#2E" + item.Color.Substring(1)),
                Child = new TextBlock
                {
                    Text = item.Glyph, FontFamily = new FontFamily(IconFont), FontSize = 16, Foreground = Brush(item.Color),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Title, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(item.Blurb))
                text.Children.Add(Themed(new TextBlock { Text = item.Blurb, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) },
                    TextBlock.ForegroundProperty, "TextSecondary"));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            return grid;
        }

        UIElement PickerCard(List<KeyValuePair<string, PickItem[]>> groups, Func<string> get, Action<string> set)
        {
            var all = groups.SelectMany(g => g.Value).ToList();
            var current = all.FirstOrDefault(x => x.Value == get()) ?? all[0];
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(PickRow(current));
            var change = Themed(new TextBlock { Text = T("Change", "Изменить") + "  \uE70D", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) },
                TextBlock.ForegroundProperty, "TextSecondary");
            change.Inlines.Clear();
            change.Inlines.Add(new System.Windows.Documents.Run(T("Change", "Изменить") + "  "));
            change.Inlines.Add(new System.Windows.Documents.Run("\uE70D") { FontFamily = new FontFamily(IconFont), FontSize = 10 });
            Grid.SetColumn(change, 1);
            content.Children.Add(change);
            var button = new Button { Style = St("ListBtn"), Content = content };
            var frame = Themed(new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Child = button },
                Border.BackgroundProperty, "ControlBg", Border.BorderBrushProperty, "ControlBorder");

            var popup = new Popup
            {
                AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PlacementTarget = frame,
                PopupAnimation = PopupAnimation.Fade
            };
            var list = new StackPanel();
            foreach (var g in groups)
            {
                list.Children.Add(new TextBlock
                {
                    Text = g.Key, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush(g.Value[0].Color),
                    Margin = new Thickness(10, list.Children.Count == 0 ? 4 : 14, 0, 4)
                });
                var wrap = new WrapPanel();
                foreach (var it in g.Value)
                {
                    var choice = it;
                    var b = new Button { Style = St("ListBtn"), Width = 284, Content = PickRow(it) };
                    if (it.Value == current.Value) b.SetResourceReference(Control.BackgroundProperty, "ControlHover");
                    b.Click += delegate
                    {
                        popup.IsOpen = false;
                        set(choice.Value);
                    };
                    wrap.Children.Add(b);
                }
                list.Children.Add(wrap);
            }
            var scroller = new ScrollViewer { MaxHeight = 470, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
            new SmoothScroll(scroller, null);
            popup.Child = Themed(new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(6),
                Margin = new Thickness(0, 4, 14, 14), Width = 610,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 },
                Child = scroller
            }, Border.BackgroundProperty, "PopupBg", Border.BorderBrushProperty, "PopupBorder");
            button.Click += delegate { popup.IsOpen = true; };
            return Stack(frame, popup);
        }

        static List<KeyValuePair<string, PickItem[]>> EventGroups()
        {
            const string blue = "#4C9AFF", teal = "#3CC4B4", amber = "#E3A93B", red = "#EE7470";
            return new List<KeyValuePair<string, PickItem[]>>
            {
                new KeyValuePair<string, PickItem[]>(T("Devices", "Устройства"), new[]
                {
                    Item("device-in", EventTitle("device-in"), T("Headphones, a flash drive, a mouse, a speaker", "Наушники, флешка, мышь, колонка"), "\uE88E", blue),
                    Item("device-out", EventTitle("device-out"), T("A device was unplugged or turned off", "Устройство отсоединили или выключили"), "\uE88E", blue),
                    Item("sound", EventTitle("sound"), T("Sound went to another device", "Звук пошёл в другое устройство"), "\uE767", blue),
                }),
                new KeyValuePair<string, PickItem[]>(T("Network", "Сеть"), new[]
                {
                    Item("wifi-on", EventTitle("wifi-on"), T("Joined a network", "Подключились к сети"), "\uE701", teal),
                    Item("wifi-off", EventTitle("wifi-off"), T("Left a network", "Отключились от сети"), "\uE701", teal),
                    Item("net-on", EventTitle("net-on"), T("The connection is back", "Связь снова есть"), "\uE774", teal),
                    Item("net-off", EventTitle("net-off"), T("The connection is gone", "Связь пропала"), "\uE774", teal),
                }),
                new KeyValuePair<string, PickItem[]>(T("Power", "Питание"), new[]
                {
                    Item("ac-on", EventTitle("ac-on"), T("The laptop is charging", "Ноутбук поставили на зарядку"), "\uE83E", amber),
                    Item("ac-off", EventTitle("ac-off"), T("The laptop runs on battery", "Ноутбук работает от батареи"), "\uE850", amber),
                    Item("battery-low", EventTitle("battery-low"), T("Charge fell below the level you set", "Заряд опустился ниже порога"), "\uE850", amber),
                    Item("wake", EventTitle("wake"), T("The computer woke up", "Компьютер проснулся"), "\uE708", amber),
                }),
                new KeyValuePair<string, PickItem[]>(T("Computer", "Компьютер"), new[]
                {
                    Item("lock", EventTitle("lock"), T("Win+L or automatic lock", "Win+L или автоматическая блокировка"), "\uE72E", red),
                    Item("unlock", EventTitle("unlock"), T("You signed back in", "Вы снова вошли в систему"), "\uE785", red),
                    Item("display-on", EventTitle("display-on"), T("A second screen was plugged in", "Подключили ещё один экран"), "\uE7F4", red),
                    Item("display-off", EventTitle("display-off"), T("A screen was unplugged", "Экран отключили"), "\uE7F4", red),
                }),
            };
        }

        static string EventArgCaption(string kind)
        {
            switch (kind)
            {
                case "device-in": case "device-out": return T("Which device", "Какое устройство");
                case "sound": return T("Which sound device", "На какое устройство звука");
                case "wifi-on": case "wifi-off": return T("Which network", "Какая сеть");
                case "battery-low": return T("At what charge", "При каком заряде");
            }
            return null;
        }

        static readonly string[] EventKinds =
        {
            "device-in", "device-out", "sound", "wifi-on", "wifi-off", "net-on", "net-off", "ac-on", "ac-off", "battery-low", "lock", "unlock", "wake", "display-on", "display-off"
        };

        static string EventTitle(string kind)
        {
            switch (kind)
            {
                case "device-in": return T("Device connected", "Устройство подключено");
                case "device-out": return T("Device disconnected", "Устройство отключено");
                case "sound": return T("Sound output changed", "Звук переключился");
                case "wifi-on": return T("Wi-Fi connected", "Wi-Fi подключился");
                case "wifi-off": return T("Wi-Fi disconnected", "Wi-Fi отключился");
                case "net-on": return T("Internet is back", "Интернет появился");
                case "net-off": return T("Internet is gone", "Интернет пропал");
                case "ac-on": return T("Charger plugged in", "Зарядка подключена");
                case "ac-off": return T("Charger unplugged", "Зарядка отключена");
                case "battery-low": return T("Battery is low", "Заряд батареи низкий");
                case "lock": return T("Computer locked", "Компьютер заблокирован");
                case "unlock": return T("Computer unlocked", "Компьютер разблокирован");
                case "wake": return T("Woke from sleep", "Выход из сна");
                case "display-on": return T("Screen connected", "Монитор подключён");
                case "display-off": return T("Screen disconnected", "Монитор отключён");
            }
            return kind;
        }

        UIElement EventRow()
        {
            var toggle = new CheckBox { Style = St("Toggle"), IsChecked = eventOn };
            toggle.Click += delegate
            {
                eventOn = toggle.IsChecked == true;
                if (eventOn && editing.Event.Length == 0) editing.Event = "device-in";
                RenderEditor();
            };
            var row = Row(T("On a system event", "При событии в системе"),
                T("A device, headphones or a Bluetooth speaker connects, Wi-Fi changes, the charger is plugged in, the computer is locked and more",
                  "Подключили устройство, наушники или Bluetooth-колонку, сменился Wi-Fi, подключили зарядку, заблокировали компьютер и другое"), toggle);
            if (!eventOn) return row;
            var picker = PickerCard(EventGroups(), () => editing.Event, v =>
            {
                editing.Event = v;
                editing.EventArg = v == "battery-low" ? "20" : "";
                Window.Dispatcher.BeginInvoke(new Action(RenderEditor));
            });
            var panel = Stack(picker);
            var arg = EventArgControl(editing.Event, () => editing.EventArg, v => editing.EventArg = v);
            if (arg != null)
            {
                var caption = Txt(EventArgCaption(editing.Event), "RowTitle");
                caption.Margin = new Thickness(2, 14, 0, 6);
                panel.Children.Add(caption);
                panel.Children.Add(arg);
            }
            panel.Margin = new Thickness(16, 0, 16, 14);
            return Stack(row, panel);
        }

        UIElement EventArgControl(string kind, Func<string> get, Action<string> set)
        {
            switch (kind)
            {
                case "device-in":
                case "device-out":
                case "device":
                    var device = Field(get(), set, kind == "device" ? T("Part of the device name", "Часть названия устройства") : T("Empty: any device. Or part of the name, for example Headphones", "Пусто: любое устройство. Или часть названия, например Headphones"));
                    var pick = new Button { Style = St("Btn"), Content = T("Choose…", "Выбрать…"), Margin = new Thickness(8, 0, 0, 0) };
                    var popup = TextPopup(pick, T("Search devices", "Поиск устройства"), () => SystemState.Devices().Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(), n =>
                    {
                        set(n);
                        RerenderAll();
                    });
                    pick.Click += delegate { popup.IsOpen = true; };
                    var line = Line(device, pick);
                    popup.PlacementTarget = line;
                    return Stack(line, popup, Desc(T("Headphones and Bluetooth speakers show up here too while they are connected", "Наушники и Bluetooth-колонки тоже есть в этом списке, пока они подключены"), 2, 0, true));
                case "sound":
                    return CurrentValueLine(get, set, T("Empty: any device. Or part of the name", "Пусто: любое устройство. Или часть названия"), T("Current", "Текущее"), SystemState.SoundOutput);
                case "wifi-on":
                case "wifi-off":
                case "wifi":
                    return CurrentValueLine(get, set, T("Empty: any network. Or the network name", "Пусто: любая сеть. Или название сети"), T("Current network", "Текущая сеть"), SystemState.WifiName);
                case "battery-low":
                    var percent = Field(get(), set, "20");
                    percent.Width = 90;
                    var unit = Themed(new TextBlock { Text = T("% or less, on battery", "% и меньше, при работе от батареи"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                                      TextBlock.ForegroundProperty, "TextSecondary");
                    return Line(percent, unit, false);
            }
            return null;
        }

        UIElement CurrentValueLine(Func<string> get, Action<string> set, string placeholder, string button, Func<string> current)
        {
            var field = Field(get(), set, placeholder);
            var take = new Button { Style = St("Btn"), Content = button, Margin = new Thickness(8, 0, 0, 0) };
            take.Click += async delegate
            {
                string value = await System.Threading.Tasks.Task.Run(current);
                if (string.IsNullOrEmpty(value)) return;
                set(value);
                RerenderAll();
            };
            return Line(field, take);
        }

        void RerenderAll()
        {
            if (editing == null) return;
            RenderEditor();
        }

        Popup TextPopup(Button anchor, string searchHint, Func<List<string>> load, Action<string> onPick)
        {
            var popup = new Popup
            {
                AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PlacementTarget = anchor,
                PopupAnimation = PopupAnimation.Fade
            };
            var search = new TextBox { Style = St("Input"), FontSize = 13 };
            var hint = Placeholder(searchHint);
            var list = new StackPanel();
            var scroller = new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Margin = new Thickness(0, 6, 0, 0) };
            new SmoothScroll(scroller, null);
            List<string> all = null;
            Action fill = () =>
            {
                list.Children.Clear();
                if (all == null)
                {
                    list.Children.Add(Desc(T("Loading…", "Загружаю…"), 10, 8));
                    return;
                }
                string q = search.Text.Trim();
                foreach (string item in all)
                {
                    if (q.Length > 0 && item.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                    string value = item;
                    var b = new Button { Style = St("ListBtn"), Content = new TextBlock { Text = item, TextTrimming = TextTrimming.CharacterEllipsis } };
                    b.Click += delegate
                    {
                        popup.IsOpen = false;
                        onPick(value);
                    };
                    list.Children.Add(b);
                }
                if (list.Children.Count == 0) list.Children.Add(Desc(T("Nothing found", "Ничего не нашлось"), 10, 8));
            };
            search.TextChanged += delegate
            {
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                fill();
            };
            popup.Opened += async delegate
            {
                search.Focus();
                all = null;
                fill();
                try { all = await System.Threading.Tasks.Task.Run(load); }
                catch (Exception) { all = new List<string>(); }
                fill();
            };
            popup.Child = Themed(new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 14, 14), Width = 420,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 },
                Child = Stack(Overlay(search, hint, new Thickness(0)), scroller)
            }, Border.BackgroundProperty, "PopupBg", Border.BorderBrushProperty, "PopupBorder");
            return popup;
        }

        UIElement DurationField(Func<string> get, Action<string> set, string placeholder)
        {
            string number, unit;
            Durations.Split(get(), out number, out unit);
            string[] state = { number, unit };
            var field = Field(number, v =>
            {
                state[0] = v;
                set(Durations.Join(state[0], state[1]));
            }, placeholder);
            field.Width = 90;
            var units = (FrameworkElement)Choice(() => state[1], v =>
            {
                state[1] = v;
                set(Durations.Join(state[0], state[1]));
            }, new[] { T("seconds", "секунд"), T("minutes", "минут"), T("hours", "часов") }, new[] { "s", "m", "h" }, false);
            units.Margin = new Thickness(10, 0, 0, 0);
            units.VerticalAlignment = VerticalAlignment.Center;
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(field);
            line.Children.Add(units);
            return line;
        }

        static string DurationText(string value)
        {
            string number, unit;
            Durations.Split(value, out number, out unit);
            return number + (unit == "h" ? T(" h", " ч") : unit == "m" ? T(" min", " мин") : T(" s", " с"));
        }

        static readonly string[] ConditionKinds = { "running", "active", "time", "days", "wifi", "internet", "battery", "device", "sound", "screens" };

        static string ConditionTitle(string kind)
        {
            switch (kind)
            {
                case "running": return T("A program is running", "Запущена программа");
                case "active": return T("A program is in front", "Программа на переднем плане");
                case "time": return T("Time of day", "Время");
                case "days": return T("Day of the week", "День недели");
                case "wifi": return T("Wi-Fi network", "Сеть Wi-Fi");
                case "internet": return T("There is internet", "Есть интернет");
                case "battery": return T("On battery", "Работает от батареи");
                case "device": return T("A device is connected", "Подключено устройство");
                case "sound": return T("Sound goes to", "Звук идёт в устройство");
                case "screens": return T("Number of screens", "Мониторов не меньше");
                case "text": return T("Compare text", "Сравнить текст");
            }
            return kind;
        }

        static string ConditionDefault(string kind)
        {
            switch (kind)
            {
                case "time": return "09:00-18:00";
                case "days": return "1111100";
                case "screens": return "2";
                case "text": return "clipboard\ncontains\n";
            }
            return "";
        }

        static List<KeyValuePair<string, PickItem[]>> ConditionGroups()
        {
            const string blue = "#4C9AFF", green = "#7DC46A", teal = "#3CC4B4", amber = "#E3A93B";
            return new List<KeyValuePair<string, PickItem[]>>
            {
                new KeyValuePair<string, PickItem[]>(T("Programs", "Программы"), new[]
                {
                    Item("running", ConditionTitle("running"), T("The program is open", "Программа открыта"), "\uE7AC", blue),
                    Item("active", ConditionTitle("active"), T("Its window is the active one", "Её окно сейчас активно"), "\uE8A7", blue),
                }),
                new KeyValuePair<string, PickItem[]>(T("Time", "Время"), new[]
                {
                    Item("time", ConditionTitle("time"), T("Now is within a time range", "Сейчас в промежутке времени"), "\uE823", green),
                    Item("days", ConditionTitle("days"), T("Today is one of the days", "Сегодня отмеченный день"), "\uE787", green),
                }),
                new KeyValuePair<string, PickItem[]>(T("Network", "Сеть"), new[]
                {
                    Item("wifi", ConditionTitle("wifi"), T("Connected to a network", "Подключено к сети"), "\uE701", teal),
                    Item("internet", ConditionTitle("internet"), T("There is a connection", "Связь есть"), "\uE774", teal),
                }),
                new KeyValuePair<string, PickItem[]>(T("Devices and power", "Устройства и питание"), new[]
                {
                    Item("device", ConditionTitle("device"), T("The device is plugged in", "Устройство подключено"), "\uE88E", amber),
                    Item("sound", ConditionTitle("sound"), T("Sound plays through it", "Звук идёт через него"), "\uE767", amber),
                    Item("screens", ConditionTitle("screens"), T("Screens plugged in", "Сколько подключено экранов"), "\uE7F4", amber),
                    Item("battery", ConditionTitle("battery"), T("The laptop is not charging", "Ноутбук не на зарядке"), "\uE850", amber),
                }),
                new KeyValuePair<string, PickItem[]>(T("Text", "Текст"), new[]
                {
                    Item("text", ConditionTitle("text"), T("Compare the clipboard or the scenario buffer", "Сравнить буфер обмена или буфер сценария"), "\uE8D2", "#E879A6"),
                }),
            };
        }

        UIElement ConditionEditor(Func<string> getKind, Action<string> setKind, Func<bool> getNeg, Action<bool> setNeg, Func<string> getArg, Action<string> setArg, string yes, string no)
        {
            var mode = Choice(() => getNeg() ? "not" : "yes", v => setNeg(v == "not"), new[] { yes, no }, new[] { "yes", "not" }, false);
            var picker = PickerCard(ConditionGroups(), getKind, v =>
            {
                setKind(v);
                setArg(ConditionDefault(v));
                Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
            });
            UIElement arg = null;
            switch (getKind())
            {
                case "running":
                case "active":
                    arg = ProgramChips(getArg, setArg, null, () => Window.Dispatcher.BeginInvoke(new Action(RenderSteps)));
                    break;
                case "time":
                    string[] range = (getArg().Length > 0 ? getArg() : "09:00-18:00").Split('-');
                    string[] times = { range[0].Trim(), range.Length > 1 ? range[1].Trim() : "" };
                    var from = Field(times[0], v => { times[0] = v.Trim(); setArg(times[0] + "-" + times[1]); }, "09:00");
                    var to = Field(times[1], v => { times[1] = v.Trim(); setArg(times[0] + "-" + times[1]); }, "18:00");
                    from.Width = to.Width = 90;
                    var timeLine = new StackPanel { Orientation = Orientation.Horizontal };
                    timeLine.Children.Add(Muted(T("from", "с"), 0, 10));
                    timeLine.Children.Add(from);
                    timeLine.Children.Add(Muted(T("to", "до"), 10, 10));
                    timeLine.Children.Add(to);
                    arg = timeLine;
                    break;
                case "days":
                    if (getArg().Length != 7) setArg("1111100");
                    arg = DayChips(getArg, setArg);
                    break;
                case "wifi":
                    arg = EventArgControl("wifi", getArg, setArg);
                    break;
                case "device":
                    arg = EventArgControl("device", getArg, setArg);
                    break;
                case "sound":
                    arg = CurrentValueLine(getArg, setArg, T("Part of the device name, for example Headphones", "Часть названия, например Headphones"), T("Current", "Текущее"), SystemState.SoundOutput);
                    break;
                case "text":
                    string[] tp = (getArg() ?? "").Split(new[] { '\n' }, 3);
                    string[] tparts = { tp.Length > 0 && tp[0].Length > 0 ? tp[0] : "clipboard", tp.Length > 1 && tp[1].Length > 0 ? tp[1] : "contains", tp.Length > 2 ? tp[2] : "" };
                    Action tsave = () => setArg(tparts[0] + "\n" + tparts[1] + "\n" + tparts[2]);
                    var tsource = Choice(() => tparts[0], v => { tparts[0] = v; tsave(); },
                        new[] { T("Windows clipboard", "Буфер обмена Windows"), T("Scenario buffer", "Буфер сценария") }, new[] { "clipboard", "buffer" }, false);
                    var tmode = Choice(() => tparts[1], v => { tparts[1] = v; tsave(); Window.Dispatcher.BeginInvoke(new Action(RenderSteps)); },
                        new[] { T("Contains", "Содержит"), T("Same, any case", "Совпадает, регистр не важен"), T("Exactly the same", "Совпадает точно"), T("Is empty", "Пустой") },
                        new[] { "contains", "nocase", "exact", "empty" }, false);
                    var tpanel = Stack(tsource, Gap(tmode));
                    if (tparts[1] != "empty")
                    {
                        var tbox = new TextBox { Style = St("Input"), FontSize = 13, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = tparts[2].Replace("\n", Environment.NewLine) };
                        tbox.TextChanged += delegate { tparts[2] = tbox.Text.Replace("\r\n", "\n"); tsave(); };
                        tpanel.Children.Add(Gap(tbox));
                        tpanel.Children.Add(Desc(tparts[1] == "exact" ? T("Letters, case and spaces must all match", "Должно совпасть всё: буквы, регистр и пробелы")
                            : tparts[1] == "nocase" ? T("Big and small letters count as the same, spaces at the edges are ignored", "Большие и маленькие буквы считаются одинаковыми, пробелы по краям не важны")
                            : T("The text is somewhere inside, case does not matter", "Текст есть где-то внутри, регистр не важен"), 2, 0, true));
                    }
                    arg = tpanel;
                    break;
                case "screens":
                    var count = Field(getArg(), v => setArg(v.Trim()), "2");
                    count.Width = 90;
                    arg = count;
                    break;
            }
            var panel = Stack(mode, Gap(picker));
            if (arg != null) panel.Children.Add(Gap(arg));
            return panel;
        }

        UIElement ConditionControls(ScenarioStep st)
        {
            string kind;
            bool negate;
            int skip;
            ScenarioCondition.Parse(st.Option, out kind, out negate, out skip);
            Action save = () => st.Option = ScenarioCondition.Format(kind, negate, skip);
            var editor = ConditionEditor(() => kind, v => { kind = v; save(); }, () => negate, v => { negate = v; save(); },
                () => st.Value, v => st.Value = v, T("If", "Если"), T("If not", "Если не"));

            var otherwise = Choice(() => skip > 0 ? "skip" : "stop", v =>
            {
                skip = v == "skip" ? Math.Max(1, skip) : 0;
                save();
                Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
            }, new[] { T("Otherwise stop the scenario", "Иначе остановить сценарий"), T("Otherwise skip steps", "Иначе пропустить шаги") }, new[] { "stop", "skip" }, false);
            var panel = Stack(editor);
            panel.Children.Add(Gap(otherwise));
            if (skip > 0)
            {
                var n = Field(skip.ToString(), v =>
                {
                    int value;
                    skip = int.TryParse(v.Trim(), out value) && value > 0 ? value : 1;
                    save();
                }, "1");
                n.Width = 90;
                var skipLine = new StackPanel { Orientation = Orientation.Horizontal };
                skipLine.Children.Add(Muted(T("how many of the next steps to skip", "сколько следующих шагов пропустить"), 0, 10));
                skipLine.Children.Add(n);
                panel.Children.Add(Gap(skipLine));
            }
            return panel;
        }

        TextBlock Muted(string text, double left, double right)
        {
            return Themed(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, right, 0) },
                          TextBlock.ForegroundProperty, "TextSecondary");
        }

        Button PickProgramButton(Action<string> onPick)
        {
            var pick = new Button { Style = St("Btn"), Content = T("Choose…", "Выбрать…"), Margin = new Thickness(8, 0, 0, 0) };
            pick.Click += delegate
            {
                OpenPicker(pick, T("Choose a program:", "Выберите программу:"), n =>
                {
                    onPick(n);
                    RenderSteps();
                }, n => true);
            };
            return pick;
        }

        UIElement DayChips(Func<string> get, Action<string> set)
        {
            var days = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            string[] names = Loc.Ru ? new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" } : new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int i = 0; i < 7; i++)
            {
                int day = i;
                var chip = new ToggleButton { Style = St("FeatureChip"), Content = names[i], IsChecked = get()[i] == '1', Margin = new Thickness(0, 0, 6, 0) };
                RoutedEventHandler changed = delegate
                {
                    var d = get().ToCharArray();
                    d[day] = chip.IsChecked == true ? '1' : '0';
                    set(new string(d));
                };
                chip.Checked += changed;
                chip.Unchecked += changed;
                days.Children.Add(chip);
            }
            return days;
        }

        static int ParseCount(string text)
        {
            int n;
            return int.TryParse(text.Trim(), out n) && n >= 0 ? n : -1;
        }

        static string WindowActionShort(string option)
        {
            switch (option)
            {
                case "max": return T("maximize the window", "развернуть окно");
                case "min": return T("minimize the window", "свернуть окно");
                case "restore": return T("restore the window", "обычный размер окна");
                case "left": return T("window to the left", "окно влево");
                case "right": return T("window to the right", "окно вправо");
                case "top": return T("window to the top", "окно наверх");
                case "bottom": return T("window to the bottom", "окно вниз");
                case "center": return T("center the window", "окно по центру");
                case "next": return T("window to the next screen", "окно на другой экран");
                case "front": return T("bring the window forward", "окно на передний план");
            }
            return T("arrange the window", "расставить окно");
        }

        static string VolumeShort(ScenarioStep st)
        {
            switch (st.Option)
            {
                case "set": return T("volume ", "громкость ") + st.Value.Trim() + "%";
                case "up": return T("louder by ", "громче на ") + st.Value.Trim() + "%";
                case "down": return T("quieter by ", "тише на ") + st.Value.Trim() + "%";
                case "mute": return T("mute", "выключить звук");
                case "unmute": return T("unmute", "включить звук");
                case "togglemute": return T("switch the sound", "переключить звук");
                case "playpause": return T("play or pause", "пауза или воспроизведение");
                case "next": return T("next track", "следующий трек");
                case "prev": return T("previous track", "предыдущий трек");
            }
            return T("sound", "звук");
        }

        UIElement AddStepButton() { return AddStepButton(-1, null); }

        UIElement AddStepButton(int at, string text)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = "\uE710", FontFamily = new FontFamily(IconFont), FontSize = at < 0 ? 12 : 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, at < 0 ? 10 : 8, 0) });
            label.Children.Add(new TextBlock { Text = text ?? T("Add a step", "Добавить шаг"), VerticalAlignment = VerticalAlignment.Center, FontSize = at < 0 ? 14 : 12.5 });
            var button = new Button { Style = St(at < 0 ? "Btn" : "Link"), Content = label, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8, 18, 8), Margin = new Thickness(0, 4, 0, 0) };

            var popup = new Popup
            {
                AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PlacementTarget = button,
                PopupAnimation = PopupAnimation.Fade
            };
            var groups = new[]
            {
                new KeyValuePair<string, string[]>(T("Programs and sites", "Программы и сайты"), new[] { "site", "run", "explorer", "command" }),
                new KeyValuePair<string, string[]>(T("Windows", "Окна"), new[] { "waitwindow", "waitclose", "window", "close" }),
                new KeyValuePair<string, string[]>(T("Mouse and keyboard", "Мышь и клавиатура"), new[] { "click", "type", "keys" }),
                new KeyValuePair<string, string[]>(T("Text and buffer", "Текст и буфер"), new[] { "buffer", "fetch", "clipboard" }),
                new KeyValuePair<string, string[]>(T("Time, conditions and repeat", "Время, условия и повтор"), new[] { "if", "repeat", "wait", "stop" }),
                new KeyValuePair<string, string[]>(T("Computer", "Компьютер"), new[] { "volume", "lock", "screenoff", "power" }),
                new KeyValuePair<string, string[]>("DeskHand", new[] { "setting", "awake", "notice" }),
            };
            var list = new StackPanel();
            foreach (var g in groups)
            {
                var header = new TextBlock
                {
                    Text = g.Key, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush(StepColor(g.Value[0])),
                    Margin = new Thickness(10, list.Children.Count == 0 ? 4 : 14, 0, 4)
                };
                list.Children.Add(header);
                var grid = new WrapPanel();
                foreach (string kind in g.Value)
                {
                    string k = kind;
                    var item = new Button { Style = St("ListBtn"), Width = 284, Content = StepCard(k) };
                    item.Click += delegate
                    {
                        popup.IsOpen = false;
                        int where = at < 0 ? editing.Steps.Count : at;
                        Mutate(() =>
                        {
                            if (k == "if")
                                editing.Steps.InsertRange(where, new[] { NewStep("if"), new ScenarioStep { Kind = "else" }, new ScenarioStep { Kind = "endif" } });
                            else editing.Steps.Insert(where, NewStep(k));
                        });
                        if (at < 0)
                        {
                            var last = stepsPanel.Children[stepsPanel.Children.Count - 1] as FrameworkElement;
                            if (last != null) Window.Dispatcher.BeginInvoke(new Action(() => last.BringIntoView()), DispatcherPriority.Loaded);
                        }
                    };
                    grid.Children.Add(item);
                }
                list.Children.Add(grid);
            }
            var scroller = new ScrollViewer { MaxHeight = 470, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
            var frame = Themed(new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(6),
                Margin = new Thickness(0, 4, 14, 14), Width = 610,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 },
                Child = scroller
            }, Border.BackgroundProperty, "PopupBg", Border.BorderBrushProperty, "PopupBorder");
            new SmoothScroll(scroller, null);
            popup.Child = frame;
            button.Click += delegate { popup.IsOpen = true; };
            return Stack(button, popup);
        }

        UIElement StepCard(string kind)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = Brush("#2E" + StepColor(kind).Substring(1)),
                Child = new TextBlock
                {
                    Text = StepGlyph(kind), FontFamily = new FontFamily(IconFont), FontSize = 16, Foreground = Brush(StepColor(kind)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            grid.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = StepTitle(kind), FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Themed(new TextBlock { Text = StepBlurb(kind), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) },
                TextBlock.ForegroundProperty, "TextSecondary"));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            return grid;
        }

        static string StepGlyph(string kind)
        {
            switch (kind)
            {
                case "site": return "\uE774";
                case "run": return "\uE7AC";
                case "explorer": return "\uE838";
                case "close": return "\uE711";
                case "wait": return "\uE916";
                case "type": return "\uE8D2";
                case "keys": return "\uE765";
                case "awake": return "\uE708";
                case "pause": return "\uE769";
                case "layout": return "\uE8C1";
                case "notice": return "\uE8BD";
                case "lock": return "\uE72E";
                case "screenoff": return "\uE7F4";
                case "power": return "\uE7E8";
                case "setting": return "\uE713";
                case "command": return "\uE756";
                case "waitwindow": return "\uE823";
                case "waitclose": return "\uE8BB";
                case "condition": return "\uE9D5";
                case "if": return "\uE9D5";
                case "else": return "\uE8AB";
                case "endif": return "\uE73E";
                case "stop": return "\uE71A";
                case "click": return "\uE962";
                case "buffer": return "\uE70B";
                case "fetch": return "\uE896";
                case "window": return "\uE740";
                case "repeat": return "\uE8EE";
                case "clipboard": return "\uE77F";
                case "volume": return "\uE767";
            }
            return "\uE710";
        }

        static string StepColor(string kind)
        {
            switch (kind)
            {
                case "site": case "run": case "explorer": case "command": return "#4C9AFF";
                case "waitwindow": case "waitclose": case "window": case "close": return "#3CC4B4";
                case "type": case "keys": case "click": return "#E3A93B";
                case "buffer": case "fetch": case "clipboard": return "#E879A6";
                case "wait": case "repeat": case "condition": case "if": case "else": case "endif": case "stop": return "#7DC46A";
                case "awake": case "pause": case "setting": case "layout": case "notice": return "#A98BF5";
            }
            return "#EE7470";
        }

        static string StepBlurb(string kind)
        {
            switch (kind)
            {
                case "site": return T("The address opens in the browser", "Адрес откроется в браузере");
                case "run": return T("Any program, with parameters", "Любая программа с параметрами");
                case "explorer": return T("An Explorer window, a folder or a file", "Окно проводника, папка или файл");
                case "close": return T("A program, a window or a tab", "Программу, окно или вкладку");
                case "wait": return T("A pause between steps", "Пауза между шагами");
                case "type": return T("Text into the active window", "Текст в активное окно");
                case "keys": return T("A combination like Ctrl+W", "Сочетание, например Ctrl+W");
                case "awake": return T("The computer will not fall asleep", "Компьютер не уснёт");
                case "pause": return T("Turn the text fixes off or on", "Выключить или включить исправления");
                case "setting": return T("Turn any feature on or off", "Включить или выключить любую функцию");
                case "layout": return T("Russian, English or the next one", "Русская, английская или следующая");
                case "notice": return T("A short note near the mouse", "Короткая надпись у курсора");
                case "lock": return T("Like Win+L", "Как Win+L");
                case "screenoff": return T("The computer keeps working", "Компьютер продолжит работать");
                case "power": return T("Or restart, can ask first", "Или перезагрузка, может спросить");
                case "command": return T("cmd or PowerShell, no window", "cmd или PowerShell без окна");
                case "waitwindow": return T("Until the program opens", "Пока программа не откроется");
                case "waitclose": return T("Until the program is closed", "Пока программа не закроется");
                case "condition": return T("Go on only if something is true", "Продолжить, только если…");
                case "if": return T("Do one thing or another", "Сделать одно или другое");
                case "stop": return T("Nothing after it runs", "Дальше ничего не выполнится");
                case "click": return T("Click a spot in a window", "Нажать в нужном месте окна");
                case "buffer": return T("Your own text while it runs", "Свой текст на время сценария");
                case "fetch": return T("Find text on a page", "Найти текст на странице");
                case "window": return T("Left, right, full screen, other screen", "Слева, справа, на весь экран");
                case "repeat": return T("Run the steps above again", "Ещё раз выполнить шаги выше");
                case "clipboard": return T("Put text into the clipboard", "Положить текст в буфер обмена");
                case "volume": return T("Volume, mute, next track", "Громкость, без звука, трек");
            }
            return "";
        }

        UIElement ScenarioHotkeyRow()
        {
            var box = new TextBox
            {
                Style = St("Input"), FontSize = 13, Width = 170, IsReadOnly = true,
                IsReadOnlyCaretVisible = false, TextAlignment = TextAlignment.Center
            };
            var error = Desc("", 16, 12);
            error.Foreground = Brush("#E5534B");
            error.Visibility = Visibility.Collapsed;
            Action show = () => box.Text = editing.Hotkey.Length == 0 ? T("None", "Нет") : editing.Hotkey;
            show();
            box.PreviewKeyDown += (s, e) =>
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Tab) return;
                e.Handled = true;
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                    || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;
                error.Visibility = Visibility.Collapsed;
                var mods = Keyboard.Modifiers;
                if (mods == ModifierKeys.None && key == Key.Escape) { Keyboard.ClearFocus(); return; }
                if (mods == ModifierKeys.None && (key == Key.Back || key == Key.Delete))
                {
                    editing.Hotkey = "";
                    show();
                    return;
                }
                string text = Hotkey.Format((mods & ModifierKeys.Control) != 0, (mods & ModifierKeys.Alt) != 0,
                    (mods & ModifierKeys.Shift) != 0, (mods & ModifierKeys.Windows) != 0, KeyInterop.VirtualKeyFromKey(key));
                string message = text == null ? T("Use Ctrl, Alt or Win with a letter, a digit or an F key", "Нужен Ctrl, Alt или Win вместе с буквой, цифрой или клавишей F")
                    : HotkeyUsedElsewhere(text) || (text != editingHotkey && !HotkeyFree(text)) ? T("This combination is already taken", "Это сочетание уже занято")
                    : null;
                if (message != null)
                {
                    error.Text = message;
                    error.Visibility = Visibility.Visible;
                    return;
                }
                editing.Hotkey = text;
                show();
            };
            return Stack(Row(T("Hotkey", "Горячая клавиша"), T("Click the field and press a combination. Backspace removes it", "Нажмите на поле и затем сочетание клавиш. Backspace убирает его"), box), error);
        }

        bool HotkeyUsedElsewhere(string text)
        {
            Func<string, bool> same = h => string.Equals(h, text, StringComparison.OrdinalIgnoreCase);
            if (same(cfg.PauseHotkey) || same(cfg.PinHotkey) || same(cfg.PlainPasteHotkey) || same(cfg.KeepAwakeHotkey)) return true;
            return cfg.Scenarios.Any(sc => sc.Name != editingName && same(sc.Hotkey));
        }

        void RenderSteps()
        {
            stepsPanel.Children.Clear();
            if (editing.Steps.Count == 0)
            {
                stepsPanel.Children.Add(Padded(Desc(T("No steps yet. Press “Add a step” below.", "Шагов пока нет. Нажмите «Добавить шаг» ниже."), 0, 0)));
                return;
            }
            var depth = Blocks.Depths(editing.Steps);
            Dictionary<int, int> elseOf, endOf, elseEnd;
            Blocks.Match(editing.Steps, out elseOf, out endOf, out elseEnd);
            for (int i = 0; i < editing.Steps.Count; i++)
            {
                string kind = editing.Steps[i].Kind;
                if (kind == "else" || kind == "endif")
                {
                    bool elseBranch = kind == "endif" && elseEnd.ContainsValue(i);
                    string label = kind == "else" ? T("Add a step for “yes”", "Добавить шаг, если условие выполнено")
                        : elseBranch ? T("Add a step for “no”", "Добавить шаг, если условие не выполнено")
                        : T("Add a step inside “If”", "Добавить шаг внутрь «Если»");
                    stepsPanel.Children.Add(Indented(new Border { Padding = new Thickness(14, 2, 12, 8), Child = AddStepButton(i, label) }, depth[i] + 1));
                }
                if (i > 0) stepsPanel.Children.Add(new Border { Style = St("Divider") });
                stepsPanel.Children.Add(Indented((FrameworkElement)(kind == "else" || kind == "endif" ? MarkerRow(i) : StepRow(i)), depth[i]));
            }
        }

        static FrameworkElement Indented(FrameworkElement row, int depth)
        {
            if (depth <= 0) return row;
            return new Border
            {
                Margin = new Thickness(18 + (depth - 1) * 22, 0, 0, 0), BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = Brush("#667DC46A"), Child = row
            };
        }

        UIElement MarkerRow(int index)
        {
            var step = editing.Steps[index];
            var grid = new Grid { Margin = new Thickness(14, 10, 12, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(Themed(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = (index + 1).ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            }, Border.BackgroundProperty, "ChipBg"));
            var markerIcon = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = Brush("#2E7DC46A"),
                Child = new TextBlock
                {
                    Text = StepGlyph(step.Kind), FontFamily = new FontFamily(IconFont), FontSize = 13, Foreground = Brush("#7DC46A"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(markerIcon, 1);
            grid.Children.Add(markerIcon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Txt(StepTitle(step.Kind), "RowTitle"));
            text.Children.Add(Desc(step.Kind == "else"
                ? T("The steps below run if the condition is not met", "Шаги ниже выполнятся, если условие не выполнено")
                : T("After this the scenario goes on as usual", "Дальше сценарий идёт как обычно"), 0, 0, true));
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            return grid;
        }

        void Mutate(Action change)
        {
            var before = editing.Steps.ToList();
            change();
            var after = editing.Steps;
            ShiftRefs(i => i >= 0 && i < before.Count ? after.IndexOf(before[i]) : -1);
            RenderSteps();
        }

        int BlockEnd(int start)
        {
            Dictionary<int, int> elseOf, endOf, elseEnd;
            Blocks.Match(editing.Steps, out elseOf, out endOf, out elseEnd);
            int end;
            return endOf.TryGetValue(start, out end) ? end : start;
        }

        int BlockStart(int end)
        {
            Dictionary<int, int> elseOf, endOf, elseEnd;
            Blocks.Match(editing.Steps, out elseOf, out endOf, out elseEnd);
            foreach (var pair in endOf) if (pair.Value == end) return pair.Key;
            return end;
        }

        void MoveBlock(int start, int delta)
        {
            int end = BlockEnd(start);
            Mutate(() =>
            {
                var list = editing.Steps;
                var block = list.GetRange(start, end - start + 1);
                if (delta < 0)
                {
                    if (start == 0) return;
                    int prevStart = list[start - 1].Kind == "endif" ? BlockStart(start - 1) : start - 1;
                    list.RemoveRange(start, block.Count);
                    list.InsertRange(prevStart, block);
                }
                else
                {
                    if (end >= list.Count - 1) return;
                    int nextEnd = list[end + 1].Kind == "if" ? BlockEnd(end + 1) : end + 1;
                    int count = nextEnd - end;
                    var next = list.GetRange(end + 1, count);
                    list.RemoveRange(end + 1, count);
                    list.InsertRange(start, next);
                }
            });
        }

        void DeleteBlock(int start)
        {
            Dictionary<int, int> elseOf, endOf, elseEnd;
            Blocks.Match(editing.Steps, out elseOf, out endOf, out elseEnd);
            var markers = new List<ScenarioStep> { editing.Steps[start] };
            int e;
            if (elseOf.TryGetValue(start, out e)) markers.Add(editing.Steps[e]);
            if (endOf.TryGetValue(start, out e) && editing.Steps[e].Kind == "endif") markers.Add(editing.Steps[e]);
            Mutate(() => editing.Steps.RemoveAll(markers.Contains));
        }

        UIElement StepRow(int index)
        {
            var step = editing.Steps[index];
            var grid = new Grid { Margin = new Thickness(14, 12, 12, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var number = Themed(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = (index + 1).ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            }, Border.BackgroundProperty, "ChipBg");
            grid.Children.Add(number);

            var body = new StackPanel();
            var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            titleLine.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, -2, 10, -2), VerticalAlignment = VerticalAlignment.Center,
                Background = Brush("#2E" + StepColor(step.Kind).Substring(1)),
                Child = new TextBlock
                {
                    Text = StepGlyph(step.Kind), FontFamily = new FontFamily(IconFont), FontSize = 13, Foreground = Brush(StepColor(step.Kind)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
            titleLine.Children.Add(Txt(StepTitle(step.Kind), "RowTitle"));
            body.Children.Add(titleLine);
            string hint = StepHint(step);
            if (hint != null) body.Children.Add(Desc(hint, 0, 0, true));
            var controls = StepControls(step);
            if (controls != null)
            {
                var holder = new Border { Margin = new Thickness(0, 8, 0, 0), Child = controls };
                body.Children.Add(holder);
            }
            if (step.Kind == "run" || step.Kind == "site" || step.Kind == "explorer")
            {
                var waitToggle = new CheckBox { Style = St("Toggle"), IsChecked = step.Has("wait") };
                waitToggle.Click += delegate { step.Set("wait", waitToggle.IsChecked == true); };
                var waitRow = Row(T("Wait until it opens", "Дождаться, пока откроется"),
                    T("The next step starts only when the window has appeared, but no longer than a minute",
                      "Следующий шаг начнётся, только когда появится окно, но не дольше минуты"), waitToggle);
                waitRow.Margin = new Thickness(0, 10, 0, 0);
                body.Children.Add(waitRow);
            }
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);

            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            bool block = step.Kind == "if";
            var up = GlyphButton("\uE70E", T("Move up", "Выше"));
            up.IsEnabled = index > 0;
            up.Click += delegate { if (block) MoveBlock(index, -1); else Move(index, -1); };
            var down = GlyphButton("\uE70D", T("Move down", "Ниже"));
            down.IsEnabled = (block ? BlockEnd(index) : index) < editing.Steps.Count - 1;
            down.Margin = new Thickness(4, 0, 6, 0);
            down.Click += delegate { if (block) MoveBlock(index, 1); else Move(index, 1); };
            var del = new Button
            {
                Style = St("IconBtn"), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = block ? T("Remove “If”. The steps inside stay", "Убрать «Если». Шаги внутри останутся") : T("Delete the step", "Удалить шаг")
            };
            del.Click += delegate
            {
                if (block) DeleteBlock(index);
                else Mutate(() => editing.Steps.RemoveAt(index));
            };
            tools.Children.Add(up);
            tools.Children.Add(down);
            tools.Children.Add(del);
            Grid.SetColumn(tools, 2);
            grid.Children.Add(tools);
            return grid;
        }

        void Move(int index, int delta)
        {
            int to = index + delta;
            if (to < 0 || to >= editing.Steps.Count) return;
            Mutate(() =>
            {
                var st = editing.Steps[index];
                editing.Steps.RemoveAt(index);
                editing.Steps.Insert(to, st);
            });
        }

        static string StepHint(ScenarioStep st)
        {
            switch (st.Kind)
            {
                case "site": return st.Option == "window"
                    ? T("Opens in a separate browser window, so a later “Close” step can close just that window",
                        "Откроется отдельным окном браузера, тогда шаг «Закрыть» сможет закрыть только его")
                    : T("Opens as a new tab in the browser you use by default", "Откроется новой вкладкой в браузере, который стоит по умолчанию");
                case "run": return T("Pick an installed app from the list or point to an .exe file. Parameters are optional",
                                     "Выберите приложение из списка установленных или укажите файл .exe. Параметры запуска можно не писать");
                case "command": return T("Runs without a window. The scenario waits until the command finishes",
                                         "Выполняется без окна. Сценарий ждёт, пока команда закончится");
                case "waitclose": return T("The scenario waits until the program is closed completely. For example, run a game, wait until you close it, then bring the settings back",
                                           "Сценарий ждёт, пока программа полностью закроется. Например, запустить игру, дождаться, пока вы её закроете, и вернуть всё как было");
                case "condition": return T("Checked at the moment the scenario reaches this step", "Проверяется в тот момент, когда сценарий доходит до этого шага");
                case "if": return T("If the condition is met, the steps under “If” run. If not, the steps under “Otherwise”. Then the scenario goes on after “End”",
                                    "Если условие выполнено, идут шаги под «Если». Если нет, то шаги под «Иначе». Потом сценарий продолжается после «Конец»");
                case "click": return T("Press “Record”, the settings window hides. Click the spot you need. That click does not reach the program, it is only remembered",
                                       "Нажмите «Записать», окно настроек свернётся. Кликните в нужном месте: этот клик до программы не дойдёт, он только запомнится");
                case "buffer": return T("Each run has its own buffer. Put it into any text with {buffer}. Spaces at the start and end are kept",
                                        "У каждого запуска свой буфер. Его можно вставить в любой текст через {buffer}. Пробелы в начале и в конце сохраняются");
                case "fetch": return T("What is found goes into the scenario buffer. Works with ordinary pages. Sites that build the page with scripts or block bots, like 2ip.io, will not do. For your IP use ifconfig.me/ip",
                                       "Найденное попадёт в буфер сценария. Работает с обычными страницами. Сайты, которые собирают страницу скриптами или закрыты от ботов, как 2ip.io, не подойдут. Для IP подойдёт ifconfig.me/ip");
                case "stop": return T("The scenario ends here. Handy inside “If”", "Сценарий на этом закончится. Удобно внутри «Если»");
                case "waitwindow": return T("The scenario waits until the program has a window. Leave the name empty to wait for the program from the step above. If the window does not appear in time, the scenario stops",
                                            "Сценарий подождёт, пока у программы появится окно. Оставьте имя пустым, чтобы ждать программу из шага выше. Если окно не появится за это время, сценарий остановится");
                case "window": return T("Pick which window to move and where. Windows opened by the steps above are listed by name", "Выберите, какое окно и куда поставить. Окна, которые открыли шаги выше, есть в списке по названию");
                case "repeat": return T("Runs the chosen steps above this one again. After the last repeat the scenario goes on with the next step. A scenario that repeats forever can be stopped from the tray menu",
                                        "Снова выполняет выбранные шаги, которые стоят выше. После последнего круга сценарий идёт дальше. Бесконечный повтор можно остановить в меню в трее");
                case "setting": return T("Changes the setting just like the switch in this window", "Меняет настройку так же, как переключатель в этом окне");
                case "clipboard": return T("You can use {date}, {time} and {clipboard}", "Можно вставить {date}, {time} и {clipboard}");
                case "explorer": return st.Option == "select"
                    ? T("Opens the folder and highlights the file in it", "Откроет папку и выделит в ней файл")
                    : T("A folder opens in Explorer, a file opens in its own program. Leave the path empty for a plain Explorer window",
                        "Папка откроется в проводнике, файл откроется в своей программе. Оставьте путь пустым, чтобы открыть просто окно проводника");
                case "close":
                    switch (st.Option)
                    {
                        case "opened": return T("The window that appeared after the last “Open” or “Run” step", "Окно, которое появилось после последнего шага «Открыть» или «Запустить»");
                        case "active": return T("The window you are working in at that moment", "Окно, в котором вы сейчас работаете");
                        case "tab": return T("Presses Ctrl+W in the window the scenario opened", "Нажимает Ctrl+W в окне, которое открыл сценарий");
                    }
                    return T("Closes every window of the program, like clicking the cross. Leave the name empty to close the program the scenario opened, for example the browser",
                             "Закрывает все окна программы, как будто нажали крестик. Оставьте имя пустым, чтобы закрыть программу, которую открыл сценарий, например браузер");
                case "type": return T("Typed into the active window. Line breaks are kept", "Печатается в активное окно. Переносы строк сохраняются");
                case "keys": return T("Click the field and press the keys. For combinations with Win, like Win+D, use the Win button",
                                      "Нажмите на поле и затем клавиши. Для сочетаний с Win, например Win+D, нажмите кнопку «Win»");
                case "awake": return T("For the time chosen in Tools", "На время, выбранное в разделе «Инструменты»");
                case "pause": return T("Turns the text fixes off or on", "Выключает или включает исправление текста");
                case "layout": return T("In the active window", "В активном окне");
                case "notice": return T("For example, “Done”. You can use {date} and {time}", "Например, «Готово». Можно вставить {date} и {time}");
                case "lock": return T("Like Win+L", "Как Win+L");
                case "screenoff": return T("The computer keeps working. Move the mouse to turn the screen back on", "Компьютер продолжит работать. Пошевелите мышью, чтобы экран включился");
            }
            return null;
        }

        UIElement StepControls(ScenarioStep st)
        {
            switch (st.Kind)
            {
                case "site":
                    return Stack(Field(st.Value, v => st.Value = v, "https://"),
                        Gap(Segments(st, new[] { T("New tab", "Новая вкладка"), T("New window", "Новое окно") }, new[] { "tab", "window" }, true)));
                case "run":
                    var path = Field(st.Value, v => st.Value = v, T("Path to the program", "Путь к программе"));
                    var apps = new Button { Style = St("Btn"), Content = T("Apps…", "Приложения…"), Margin = new Thickness(8, 0, 0, 0) };
                    var appsPopup = AppsPopup(apps, app =>
                    {
                        st.Value = app.Target;
                        RenderSteps();
                    });
                    apps.Click += delegate { appsPopup.IsOpen = true; };
                    var browse = new Button { Style = St("Btn"), Content = T("Browse…", "Обзор…"), Margin = new Thickness(6, 0, 0, 0) };
                    browse.Click += delegate
                    {
                        var dlg = new OpenFileDialog { Filter = T("Programs (*.exe)|*.exe|All files|*.*", "Программы (*.exe)|*.exe|Все файлы|*.*") };
                        if (dlg.ShowDialog(Window) != true) return;
                        st.Value = dlg.FileName;
                        RenderSteps();
                    };
                    var runButtons = new StackPanel { Orientation = Orientation.Horizontal };
                    runButtons.Children.Add(apps);
                    runButtons.Children.Add(browse);
                    var runLine = Line(path, runButtons);
                    appsPopup.PlacementTarget = runLine;
                    var runPanel = Stack(runLine, appsPopup);
                    if (st.Value.StartsWith(InstalledApps.ShellPrefix, StringComparison.OrdinalIgnoreCase))
                        runPanel.Children.Add(Desc(T("App: ", "Приложение: ") + InstalledApps.NameOf(st.Value), 2, 0, true));
                    runPanel.Children.Add(Gap(Field(st.Option, v => st.Option = v, T("Parameters, optional", "Параметры запуска, необязательно"))));
                    return runPanel;
                case "explorer": return ExplorerControls(st);
                case "wait": return DurationField(() => st.Value, v => st.Value = v, "5");
                case "condition": return ConditionControls(st);
                case "click": return ClickControls(st);
                case "buffer":
                    var bufferChoice = Choice(() => st.Option, v =>
                    {
                        st.Option = v;
                        Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
                    }, new[] { T("Write text", "Записать текст"), T("Add to the end", "Добавить в конец"), T("Add to the start", "Добавить в начало"),
                                T("Take from the clipboard", "Взять из буфера обмена"), T("Put into the clipboard", "Положить в буфер обмена"), T("Clear", "Очистить") },
                        new[] { "set", "append", "prepend", "fromclip", "toclip", "clear" }, false);
                    if (st.Option != "set" && st.Option != "append" && st.Option != "prepend") return bufferChoice;
                    return Stack(bufferChoice, Gap(TextWithTokens(st)));
                case "fetch": return FetchControls(st);
                case "if":
                    string ik;
                    bool ineg;
                    int iskip;
                    ScenarioCondition.Parse(st.Option, out ik, out ineg, out iskip);
                    Action saveBlock = () => st.Option = ik + (ineg ? "!" : "");
                    return ConditionEditor(() => ik, v => { ik = v; saveBlock(); }, () => ineg, v => { ineg = v; saveBlock(); },
                        () => st.Value, v => st.Value = v, T("If", "Если"), T("If not", "Если не"));
                case "waitclose":
                    var closeLimit = new StackPanel { Orientation = Orientation.Horizontal };
                    closeLimit.Children.Add(Muted(T("but no longer than", "но не дольше"), 0, 10));
                    closeLimit.Children.Add(DurationField(() => st.Option, v => st.Option = v, T("no limit", "без предела")));
                    return Stack(ProgramChips(() => st.Value, v => st.Value = v, T("Empty: the program from the step above. With several programs the scenario waits until all of them are closed", "Пусто: программа из шага выше. Если программ несколько, сценарий ждёт, пока закроются все"), RenderSteps), Gap(closeLimit));
                case "close":
                    var modes = Segments(st, new[] { T("Program", "Программу"), T("Scenario window", "Окно сценария"), T("Tab", "Вкладку"), T("Active window", "Активное окно") },
                        new[] { "program", "opened", "tab", "active" }, true, st.Option == "kill" ? "program" : null);
                    if (st.Option != "program" && st.Option != "kill") return modes;
                    var force = new CheckBox { Style = St("Toggle"), IsChecked = st.Option == "kill" };
                    force.Click += delegate { st.Option = force.IsChecked == true ? "kill" : "program"; };
                    var forceRow = Row(T("Force", "Принудительно"),
                        T("Ends the program at once, even if it asks to save something. Unsaved changes are lost",
                          "Завершает программу сразу, даже если она спрашивает про сохранение. Несохранённое пропадёт"), force);
                    forceRow.Margin = new Thickness(0, 10, 0, 0);
                    return Stack(modes, Gap(ProgramChips(() => st.Value, v => st.Value = v, T("Empty: the program the scenario opened", "Пусто: программа, которую открыл сценарий"), RenderSteps)), forceRow);
                case "type":
                case "clipboard": return TextWithTokens(st);
                case "keys": return KeysCapture(st);
                case "command":
                    var cmdBox = new TextBox { Style = St("Input"), FontSize = 13, AcceptsReturn = false, Text = st.Value, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                    cmdBox.TextChanged += delegate { st.Value = cmdBox.Text; };
                    return Stack(Segments(st, new[] { T("Command prompt (cmd)", "Командная строка (cmd)"), "PowerShell" }, new[] { "cmd", "ps" }, false), Gap(cmdBox));
                case "waitwindow":
                    var limitLine = new StackPanel { Orientation = Orientation.Horizontal };
                    limitLine.Children.Add(Muted(T("no longer than", "не дольше"), 0, 10));
                    limitLine.Children.Add(DurationField(() => st.Option, v => st.Option = v, "30"));
                    return Stack(ProgramChips(() => st.Value, v => st.Value = v, T("Empty: the program from the step above. With several programs, any of them will do", "Пусто: программа из шага выше. Если программ несколько, подойдёт любая"), RenderSteps), Gap(limitLine));
                case "window":
                    int me = editing.Steps.IndexOf(st);
                    var targetLabels = new List<string>();
                    var targetValues = new List<string>();
                    for (int j = 0; j < me; j++)
                    {
                        var prev = editing.Steps[j];
                        if (prev.Kind != "run" && prev.Kind != "site" && prev.Kind != "explorer") continue;
                        targetLabels.Add(T("Step ", "Шаг ") + (j + 1) + ": " + StepTargetName(prev));
                        targetValues.Add("step:" + (j + 1));
                    }
                    targetLabels.Add(T("Last opened", "Последнее открытое"));
                    targetValues.Add("opened");
                    targetLabels.Add(T("Active window", "Активное окно"));
                    targetValues.Add("active");
                    targetLabels.Add(T("Another program", "Другая программа"));
                    targetValues.Add("program");
                    string target = st.Value.StartsWith("program:", StringComparison.OrdinalIgnoreCase) ? "program" : st.Value.Length == 0 ? "opened" : st.Value;
                    var windowPanel = Stack(Caption(T("Which window", "Какое окно")),
                        Choice(() => target, v =>
                        {
                            st.Value = v == "program" ? "program:" : v;
                            Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
                        }, targetLabels.ToArray(), targetValues.ToArray(), false));
                    if (target == "program")
                        windowPanel.Children.Add(Gap(ProgramChips(() => st.Value.Substring(8), v => st.Value = "program:" + v, null, RenderSteps)));
                    windowPanel.Children.Add(Caption(T("Where to put it", "Куда поставить")));
                    windowPanel.Children.Add(Choice(() => st.Option, v => st.Option = v,
                        new[] { T("Left half", "Слева"), T("Right half", "Справа"), T("Top half", "Сверху"), T("Bottom half", "Снизу"), T("Full screen", "На весь экран"),
                                T("Center", "По центру"), T("Other screen", "На другой экран"), T("Minimize", "Свернуть"), T("Normal size", "Обычный размер"), T("Bring forward", "На передний план") },
                        new[] { "left", "right", "top", "bottom", "max", "center", "next", "min", "restore", "front" }, false));
                    return windowPanel;
                case "setting":
                    return Stack(PickerCard(ToggleGroups(), () => st.Option, v =>
                    {
                        st.Option = v;
                        Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
                    }), Gap(Choice(() => st.Value, v => st.Value = v,
                        new[] { T("Turn on", "Включить"), T("Turn off", "Выключить"), T("Switch", "Переключить") }, new[] { "on", "off", "toggle" }, false)));
                case "repeat":
                    int self = editing.Steps.IndexOf(st);
                    string repeatMode = RepeatSpec.Mode(st.Option);
                    var modeChoice = Choice(() => repeatMode, v =>
                    {
                        st.Option = v == "if" ? RepeatSpec.FormatIf("running", false, "") : v == "forever" ? "0" : "2";
                        Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
                    }, new[] { T("A number of times", "Сколько раз"), T("Forever", "Без конца"), T("While something is true", "Пока выполняется условие") }, new[] { "count", "forever", "if" }, false);
                    var repeatPanel = Stack(modeChoice);
                    if (repeatMode == "count")
                    {
                        var count = Field(st.Option, v => st.Option = v.Trim(), "2");
                        count.Width = 90;
                        var countLine = new StackPanel { Orientation = Orientation.Horizontal };
                        countLine.Children.Add(count);
                        countLine.Children.Add(Muted(T("times in total", "раз всего"), 10, 0));
                        repeatPanel.Children.Add(Gap(countLine));
                    }
                    if (repeatMode == "if")
                    {
                        string rk, ra;
                        bool rn;
                        RepeatSpec.ParseIf(st.Option, out rk, out rn, out ra);
                        Action saveIf = () => st.Option = RepeatSpec.FormatIf(rk, rn, ra);
                        saveIf();
                        repeatPanel.Children.Add(Gap(ConditionEditor(() => rk, v => { rk = v; saveIf(); }, () => rn, v => { rn = v; saveIf(); },
                            () => ra, v => { ra = v; saveIf(); }, T("While", "Пока"), T("While not", "Пока не"))));
                    }
                    var range = Desc("", 2, 0, true);
                    Action showRange = () =>
                    {
                        int f, t;
                        if (!int.TryParse(st.Value.Trim(), out f)) f = 0;
                        if (!int.TryParse(st.FlagValue("to"), out t)) t = self;
                        range.Text = self <= 0 ? T("Put this step below the steps to repeat", "Поставьте этот шаг ниже тех, что нужно повторять")
                            : f < 1 || t > self || f > t ? T("The steps must be from 1 to ", "Номера шагов должны быть от 1 до ") + self + T(", the first not after the last", ", и первый не позже последнего")
                            : f == t ? T("Step ", "Повторяется шаг ") + f
                            : T("Steps ", "Повторяются шаги с ") + f + T(" to ", " по ") + t;
                    };
                    var fromStep = Field(st.Value, v => { st.Value = v; showRange(); }, "1");
                    fromStep.Width = 70;
                    string toText = st.FlagValue("to");
                    var toStep = Field(toText.Length > 0 ? toText : (self > 0 ? self.ToString() : ""), v =>
                    {
                        int t;
                        st.SetFlagValue("to", int.TryParse(v.Trim(), out t) && t != self ? t.ToString() : "");
                        showRange();
                    }, self > 0 ? self.ToString() : "");
                    toStep.Width = 70;
                    var fromLine = new StackPanel { Orientation = Orientation.Horizontal };
                    fromLine.Children.Add(Muted(T("starting from step", "начиная с шага"), 0, 10));
                    fromLine.Children.Add(fromStep);
                    fromLine.Children.Add(Muted(T("and ending with step", "и заканчивая шагом"), 14, 10));
                    fromLine.Children.Add(toStep);
                    repeatPanel.Children.Add(Gap(fromLine));
                    showRange();
                    repeatPanel.Children.Add(range);
                    return repeatPanel;
                case "volume":
                    var volumeChoice = Choice(() => st.Option, v => st.Option = v,
                        new[] { T("Set volume", "Громкость"), T("Louder", "Громче"), T("Quieter", "Тише"), T("Mute", "Без звука"), T("Unmute", "Со звуком"),
                                T("Switch sound", "Переключить звук"), T("Play or pause", "Пауза и плей"), T("Next track", "Следующий трек"), T("Previous track", "Предыдущий трек") },
                        new[] { "set", "up", "down", "mute", "unmute", "togglemute", "playpause", "next", "prev" }, true);
                    if (st.Option != "set" && st.Option != "up" && st.Option != "down") return volumeChoice;
                    var percent = Field(st.Value, v => st.Value = v, "30");
                    percent.Width = 90;
                    var percentUnit = Themed(new TextBlock { Text = "%", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                                             TextBlock.ForegroundProperty, "TextSecondary");
                    return Stack(volumeChoice, Gap(Line(percent, percentUnit, false)));
                case "awake":
                    return Segments(st, new[] { T("Turn on", "Включить"), T("Turn off", "Выключить"), T("Switch", "Переключить") }, new[] { "on", "off", "toggle" }, false);
                case "pause":
                    return Segments(st, new[] { T("Pause", "Поставить на паузу"), T("Resume", "Снять с паузы"), T("Switch", "Переключить") }, new[] { "pause", "resume", "toggle" }, false);
                case "layout":
                    return Segments(st, new[] { T("Russian", "Русская"), T("English", "Английская"), T("Next", "Следующая") }, new[] { "ru", "en", "next" }, false);
                case "notice":
                    return Field(st.Value, v => st.Value = v, T("Text of the note", "Текст подписи"));
                case "power":
                    var ask = new CheckBox { Style = St("Toggle"), IsChecked = st.Value != "now" };
                    ask.Click += delegate { st.Value = ask.IsChecked == true ? "ask" : "now"; };
                    var askRow = Row(T("Ask first", "Спросить перед этим"), T("A window asks for confirmation, so a wrong press does nothing", "Появится окно с вопросом, чтобы случайное нажатие ничего не сделало"), ask);
                    askRow.Margin = new Thickness(0, 10, 0, 0);
                    return Stack(Segments(st, new[] { T("Sleep", "Сон"), T("Shut down", "Выключение"), T("Restart", "Перезагрузка") }, new[] { "sleep", "shutdown", "restart" }, false), askRow);
            }
            return null;
        }

        Popup AppsPopup(Button anchor, Action<InstalledApp> onPick)
        {
            var popup = new Popup
            {
                AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, PlacementTarget = anchor,
                PopupAnimation = PopupAnimation.Fade
            };
            var search = new TextBox { Style = St("Input"), FontSize = 13 };
            var hint = Placeholder(T("Search", "Поиск"));
            var list = new StackPanel();
            var status = Desc(T("Loading the list of apps…", "Загружаю список приложений…"), 10, 8);
            status.Margin = new Thickness(10, 10, 0, 10);
            var scroller = new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Margin = new Thickness(0, 6, 0, 0) };
            new SmoothScroll(scroller, null);
            List<InstalledApp> all = null;

            Action fill = () =>
            {
                list.Children.Clear();
                if (all == null)
                {
                    list.Children.Add(status);
                    return;
                }
                string q = search.Text.Trim();
                foreach (var app in all)
                {
                    if (q.Length > 0 && app.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                    var item = app;
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(new Image { Source = app.Icon, Width = 24, Height = 24, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
                    row.Children.Add(new TextBlock { Text = app.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                    var b = new Button { Style = St("ListBtn"), Content = row };
                    b.Click += delegate
                    {
                        popup.IsOpen = false;
                        InstalledApps.Remember(item);
                        onPick(item);
                    };
                    list.Children.Add(b);
                }
                if (list.Children.Count == 0) list.Children.Add(Desc(T("Nothing found", "Ничего не нашлось"), 10, 8));
            };
            search.TextChanged += delegate
            {
                hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                fill();
            };
            popup.Opened += async delegate
            {
                search.Focus();
                fill();
                if (all != null) return;
                try { all = await InstalledApps.LoadAsync(); }
                catch (Exception) { all = new List<InstalledApp>(); }
                fill();
            };

            popup.Child = Themed(new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 14, 14), Width = 400,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 },
                Child = Stack(Overlay(search, hint, new Thickness(0)), scroller)
            }, Border.BackgroundProperty, "PopupBg", Border.BorderBrushProperty, "PopupBorder");
            return popup;
        }

        TextBlock Caption(string text)
        {
            var t = Txt(text, "RowDesc");
            t.Margin = new Thickness(2, 10, 0, 6);
            return t;
        }

        static string StepTargetName(ScenarioStep st)
        {
            string v = st.Value.Trim().Trim('"');
            switch (st.Kind)
            {
                case "run": return v.Length == 0 ? T("program", "программа") : InstalledApps.NameOf(v);
                case "site":
                    int scheme = v.IndexOf("://", StringComparison.Ordinal);
                    string host = (scheme >= 0 ? v.Substring(scheme + 3) : v).Split('/')[0];
                    return host.Length == 0 ? T("site", "сайт") : host;
                case "explorer":
                    string leaf = System.IO.Path.GetFileName(v.TrimEnd('\\'));
                    return leaf.Length > 0 ? leaf : T("Explorer", "Проводник");
            }
            return st.Kind;
        }

        static List<KeyValuePair<string, PickItem[]>> ToggleGroups()
        {
            const string purple = "#A98BF5";
            Func<string[], PickItem[]> items = keys => keys.Select(k => Item(k, ToggleNames.Title(k), null, "\uE713", purple)).ToArray();
            return new List<KeyValuePair<string, PickItem[]>>
            {
                new KeyValuePair<string, PickItem[]>(T("Corrections", "Исправления"), items(ToggleNames.Corrections)),
                new KeyValuePair<string, PickItem[]>(T("Capital letters", "Заглавные буквы"), items(ToggleNames.Capitals)),
                new KeyValuePair<string, PickItem[]>(T("Keyboard layout", "Раскладка"), items(ToggleNames.Layout)),
                new KeyValuePair<string, PickItem[]>(T("Other", "Прочее"), items(ToggleNames.Other)),
            };
        }

        void ShiftRefs(Func<int, int> map)
        {
            foreach (var st in editing.Steps)
            {
                int n;
                if (st.Kind == "window" && st.Value.StartsWith("step:", StringComparison.OrdinalIgnoreCase) && int.TryParse(st.Value.Substring(5), out n))
                {
                    int m = map(n - 1);
                    st.Value = m < 0 ? "opened" : "step:" + (m + 1);
                }
                if (st.Kind != "repeat") continue;
                if (int.TryParse(st.Value.Trim(), out n) && map(n - 1) >= 0) st.Value = (map(n - 1) + 1).ToString();
                if (int.TryParse(st.FlagValue("to"), out n) && map(n - 1) >= 0) st.SetFlagValue("to", (map(n - 1) + 1).ToString());
            }
        }

        static string FetchPart(ScenarioStep st, int index)
        {
            string[] parts = (st.Value ?? "").Split(new[] { '\n' }, 3);
            return index < parts.Length ? parts[index] : "";
        }

        static void SetFetchPart(ScenarioStep st, int index, string value)
        {
            string[] parts = (st.Value ?? "").Split(new[] { '\n' }, 3);
            var list = new List<string>(parts);
            while (list.Count < 3) list.Add("");
            list[index] = (value ?? "").Replace("\r", "").Replace("\n", " ");
            st.Value = string.Join("\n", list.ToArray()).TrimEnd('\n');
        }

        UIElement FetchControls(ScenarioStep st)
        {
            string[] opt = st.Option.Split(':');
            string[] state = { opt[0].Length > 0 ? opt[0] : "all", opt.Length > 1 ? opt[1] : "set" };
            Action saveOpt = () => st.Option = state[0] + ":" + state[1];
            var url = Field(FetchPart(st, 0), v => SetFetchPart(st, 0, v.Trim()), "https://ifconfig.me/ip");
            var mode = Choice(() => state[0], v => { state[0] = v; saveOpt(); Window.Dispatcher.BeginInvoke(new Action(RenderSteps)); },
                new[] { T("All text of the page", "Весь текст страницы"), T("Text between", "Текст между"), T("By a pattern", "По шаблону") }, new[] { "all", "between", "regex" }, false);
            var panel = Stack(url, Gap(mode));
            if (state[0] == "between")
            {
                var before = Field(FetchPart(st, 1), v => SetFetchPart(st, 1, v), T("After this text, for example “Your IP:”", "После текста, например «Ваш IP:»"));
                var after = Field(FetchPart(st, 2), v => SetFetchPart(st, 2, v), T("Up to this text. Empty: to the end of the line", "До текста. Пусто: до конца строки"));
                panel.Children.Add(Gap(before));
                panel.Children.Add(Gap(after));
            }
            if (state[0] == "regex")
            {
                var pattern = Field(FetchPart(st, 1), v => SetFetchPart(st, 1, v), T("A regular expression", "Регулярное выражение"));
                var presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var pr in new[] { new[] { T("IP address", "IP-адрес"), @"\b\d{1,3}(?:\.\d{1,3}){3}\b" }, new[] { T("Number", "Число"), @"-?\d+(?:[.,]\d+)?" }, new[] { T("E-mail", "Почта"), @"[\w.+-]+@[\w-]+\.[\w.]+" } })
                {
                    string value = pr[1];
                    var b = new Button { Style = St("Btn"), Content = pr[0], Margin = new Thickness(0, 0, 6, 0) };
                    b.Click += delegate
                    {
                        SetFetchPart(st, 1, value);
                        RenderSteps();
                    };
                    presets.Children.Add(b);
                }
                panel.Children.Add(Gap(pattern));
                panel.Children.Add(presets);
            }
            panel.Children.Add(Gap(Choice(() => state[1], v => { state[1] = v; saveOpt(); },
                new[] { T("Write into the buffer", "Записать в буфер"), T("Add to the buffer", "Добавить к буферу") }, new[] { "set", "append" }, false)));
            var result = Desc("", 2, 0, true);
            var check = new Button { Style = St("Btn"), Content = T("Check", "Проверить"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            check.Click += async delegate
            {
                string address = FetchPart(st, 0).Trim();
                if (address.Length == 0) return;
                if (address.IndexOf("://", StringComparison.Ordinal) < 0) address = "https://" + address;
                string m = state[0], a = FetchPart(st, 1), b2 = FetchPart(st, 2);
                result.Text = T("Opening the site…", "Открываю сайт…");
                check.IsEnabled = false;
                string text;
                try
                {
                    text = await System.Threading.Tasks.Task.Run(() => WebText.Extract(WebText.Download(address), m, a, b2));
                    text = text == null ? T("Nothing found", "Ничего не нашлось") : T("Found: ", "Нашлось: ") + (text.Length > 300 ? text.Substring(0, 300) + "…" : text);
                }
                catch (Exception ex) { text = T("The site did not open: ", "Сайт не открылся: ") + ex.Message; }
                check.IsEnabled = true;
                result.Text = text;
            };
            panel.Children.Add(check);
            panel.Children.Add(result);
            return panel;
        }

        UIElement ClickControls(ScenarioStep st)
        {
            var spot = ClickSpot.Parse(st.Value);
            string what = !spot.Recorded ? T("The click is not recorded yet", "Клик ещё не записан")
                : spot.Mode == "window" ? T("Window of ", "Окно ") + spot.Program + ": " + Math.Round(spot.X * 100) + T("% across, ", "% по ширине, ") + Math.Round(spot.Y * 100) + T("% down", "% по высоте")
                : T("Point on the screen: ", "Точка на экране: ") + spot.ScreenX + ", " + spot.ScreenY;
            var record = new Button { Style = St("AccentBtn"), Content = spot.Recorded ? T("Record again", "Записать заново") : T("Record a click", "Записать клик"), Padding = new Thickness(16, 6, 16, 6) };
            record.Click += delegate
            {
                ClickRecorder.Start(Window, s2 =>
                {
                    st.Value = s2.ToString();
                    RenderSteps();
                });
            };
            record.HorizontalAlignment = HorizontalAlignment.Left;
            var panel = Stack(record, Desc(what, 2, 0, true));
            if (spot.Recorded && spot.Program.Length > 0)
                panel.Children.Add(Gap(Choice(() => spot.Mode, v => { spot.Mode = v; st.Value = spot.ToString(); Window.Dispatcher.BeginInvoke(new Action(RenderSteps)); },
                    new[] { T("Tied to the program window", "Привязать к окну программы"), T("Fixed point on the screen", "Точка на экране") }, new[] { "window", "screen" }, false)));
            if (spot.Recorded)
                panel.Children.Add(Desc(spot.Mode == "window"
                    ? T("The spot moves with the window and follows its size, so it works even if the window is moved or resized", "Точка двигается вместе с окном и подстраивается под его размер, поэтому сработает, даже если окно сдвинули или растянули")
                    : T("Always the same place on the screen, wherever the windows are", "Всегда одно и то же место на экране, где бы ни были окна"), 2, 0, true));
            panel.Children.Add(Gap(Choice(() => st.Option, v => st.Option = v,
                new[] { T("Left click", "Левый клик"), T("Right click", "Правый клик"), T("Double click", "Двойной клик") }, new[] { "left", "right", "double" }, false)));
            var back = new CheckBox { Style = St("Toggle"), IsChecked = !st.Has("stay") };
            back.Click += delegate { st.Set("stay", back.IsChecked != true); };
            var backRow = Row(T("Put the cursor back", "Вернуть курсор на место"), T("After the click the mouse pointer returns where it was", "После клика указатель мыши вернётся туда, где был"), back);
            backRow.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(backRow);
            return panel;
        }

        UIElement ExplorerControls(ScenarioStep st)
        {
            var path = Field(st.Value, v => st.Value = v, T("Empty: just an Explorer window", "Пусто: просто окно проводника"));
            var file = new Button { Style = St("Btn"), Content = T("File…", "Файл…"), Margin = new Thickness(8, 0, 0, 0) };
            file.Click += delegate
            {
                var dlg = new OpenFileDialog { Filter = T("All files|*.*", "Все файлы|*.*") };
                if (dlg.ShowDialog(Window) != true) return;
                st.Value = dlg.FileName;
                RenderSteps();
            };
            var folder = new Button { Style = St("Btn"), Content = T("Folder…", "Папка…"), Margin = new Thickness(6, 0, 0, 0) };
            folder.Click += delegate
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog { ShowNewFolderButton = false })
                {
                    if (st.Value.Trim().Length > 0 && System.IO.Directory.Exists(st.Value.Trim())) dlg.SelectedPath = st.Value.Trim();
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    st.Value = dlg.SelectedPath;
                }
                RenderSteps();
            };
            var pickers = new StackPanel { Orientation = Orientation.Horizontal };
            pickers.Children.Add(file);
            pickers.Children.Add(folder);
            return Stack(Line(path, pickers),
                Gap(Segments(st, new[] { T("Open", "Открыть"), T("Show in folder", "Показать в папке") }, new[] { "open", "select" }, true)));
        }

        UIElement TextWithTokens(ScenarioStep st)
        {
            var box = new TextBox { Style = St("Input"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, Text = st.Value.Replace("\n", Environment.NewLine) };
            box.TextChanged += delegate { st.Value = box.Text.Replace("\r\n", "\n"); };
            var tokens = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var t in new[] { new[] { T("+ Date", "+ Дата"), "{date}" }, new[] { T("+ Time", "+ Время"), "{time}" }, new[] { T("+ Clipboard", "+ Буфер обмена"), "{clipboard}" }, new[] { T("+ Scenario buffer", "+ Буфер сценария"), "{buffer}" } })
            {
                string token = t[1];
                var b = new Button { Style = St("Btn"), Content = t[0], Margin = new Thickness(0, 0, 6, 0), ToolTip = token };
                b.Click += delegate
                {
                    box.SelectedText = token;
                    box.CaretIndex = box.SelectionStart + token.Length;
                    box.Focus();
                };
                tokens.Children.Add(b);
            }
            return Stack(box, tokens);
        }

        UIElement Choice(Func<string> get, Action<string> set, string[] labels, string[] values, bool rerender)
        {
            string group = "choice" + (segmentGroup++);
            string current = get();
            var panel = new WrapPanel();
            for (int i = 0; i < labels.Length; i++)
            {
                string value = values[i];
                var rb = new RadioButton { Style = St("Segment"), GroupName = group, Content = labels[i], IsChecked = value == current, Margin = new Thickness(0, 0, 2, 2) };
                rb.Checked += delegate
                {
                    if (get() == value) return;
                    set(value);
                    if (rerender) Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
                };
                panel.Children.Add(rb);
            }
            var box = SegmentBox(panel);
            box.HorizontalAlignment = HorizontalAlignment.Left;
            return box;
        }

        UIElement KeysCapture(ScenarioStep st)
        {
            var box = new TextBox
            {
                Style = St("Input"), FontSize = 13, Width = 220, IsReadOnly = true,
                IsReadOnlyCaretVisible = false, TextAlignment = TextAlignment.Center
            };
            Action show = () => box.Text = st.Value.Length == 0 ? T("Press the keys", "Нажмите клавиши") : st.Value;
            show();
            box.PreviewKeyDown += (s, e) =>
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                e.Handled = true;
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                    || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;
                var mods = Keyboard.Modifiers;
                bool win = st.Value.StartsWith("Win+", StringComparison.OrdinalIgnoreCase) || (mods & ModifierKeys.Windows) != 0;
                string text = KeyNames.Format((mods & ModifierKeys.Control) != 0, (mods & ModifierKeys.Alt) != 0,
                    (mods & ModifierKeys.Shift) != 0, win, KeyInterop.VirtualKeyFromKey(key));
                if (text == null) return;
                st.Value = text;
                show();
            };
            var winButton = new Button { Style = St("Btn"), Content = "Win", Margin = new Thickness(8, 0, 0, 0), ToolTip = T("Add or remove Win", "Добавить или убрать Win") };
            winButton.Click += delegate
            {
                if (st.Value.StartsWith("Win+", StringComparison.OrdinalIgnoreCase)) st.Value = st.Value.Substring(4);
                else if (st.Value.Length > 0) st.Value = "Win+" + st.Value;
                show();
            };
            var clear = new Button { Style = St("Btn"), Content = T("Clear", "Очистить"), Margin = new Thickness(8, 0, 0, 0) };
            clear.Click += delegate
            {
                st.Value = "";
                show();
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(box);
            line.Children.Add(winButton);
            line.Children.Add(clear);
            return line;
        }

        UIElement Segments(ScenarioStep st, string[] labels, string[] values, bool rerender, string selected = null)
        {
            string group = "step" + (segmentGroup++);
            string current = selected ?? st.Option;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < labels.Length; i++)
            {
                string value = values[i];
                var rb = new RadioButton { Style = St("Segment"), GroupName = group, Content = labels[i], IsChecked = value == current };
                rb.Checked += delegate
                {
                    if (st.Option == value || (value == "program" && st.Option == "kill")) return;
                    st.Option = value;
                    if (rerender) Window.Dispatcher.BeginInvoke(new Action(RenderSteps));
                };
                panel.Children.Add(rb);
            }
            var box = SegmentBox(panel);
            box.HorizontalAlignment = HorizontalAlignment.Left;
            return box;
        }

        Grid Field(string value, Action<string> set, string placeholder)
        {
            var box = new TextBox { Style = St("Input"), FontSize = 13, Text = value };
            var hint = Placeholder(placeholder);
            hint.TextWrapping = TextWrapping.NoWrap;
            hint.TextTrimming = TextTrimming.CharacterEllipsis;
            hint.Visibility = value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += delegate
            {
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                set(box.Text);
            };
            return Overlay(box, hint, new Thickness(0));
        }

        static FrameworkElement Gap(UIElement content)
        {
            return new Border { Margin = new Thickness(0, 8, 0, 0), Child = content };
        }

        static Grid Line(FrameworkElement main, FrameworkElement side, bool stretch = true)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (!stretch) grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(main);
            Grid.SetColumn(side, 1);
            grid.Children.Add(side);
            return grid;
        }

        void SaveScenario(bool run)
        {
            string name = editing.Name.Trim();
            string error = null;
            if (name.Length == 0) error = T("Give the scenario a name", "Дайте сценарию название");
            else if (cfg.Scenarios.Any(sc => sc.Name != editingName && string.Equals(sc.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                error = T("A scenario with this name already exists", "Сценарий с таким названием уже есть");
            else if (editing.Steps.Count == 0) error = T("Add at least one step", "Добавьте хотя бы один шаг");
            if (!scheduleOn) editing.Time = "";
            else if (error == null)
            {
                TimeSpan clock;
                if (!TimeSpan.TryParseExact(editing.Time.Trim(), new[] { "h\\:mm", "hh\\:mm" }, System.Globalization.CultureInfo.InvariantCulture, out clock) || clock.TotalHours >= 24)
                    error = T("Type the time like 09:00", "Напишите время так: 09:00");
                else if (editing.Days.IndexOf('1') < 0) error = T("Pick at least one day", "Отметьте хотя бы один день");
                else editing.Time = clock.ToString("hh\\:mm");
            }
            if (!appTriggerOn) editing.WhenOpened = "";
            else if (error == null && editing.WhenOpened.Trim().Trim(',').Length == 0)
                error = T("Type the program to watch for", "Напишите, при открытии какой программы запускать");
            if (!closeTriggerOn) editing.WhenClosed = "";
            else if (error == null && editing.WhenClosed.Trim().Trim(',').Length == 0)
                error = T("Type the program whose closing starts the scenario", "Напишите, при закрытии какой программы запускать");
            if (!idleOn) editing.Idle = "";
            else if (error == null && Durations.Parse(editing.Idle) <= 0)
                error = T("Type how long without activity", "Напишите, сколько времени без действий");
            if (!eventOn)
            {
                editing.Event = "";
                editing.EventArg = "";
            }
            else if (error == null && editing.Event == "battery-low")
            {
                int level;
                if (!int.TryParse(editing.EventArg.Trim(), out level) || level < 1 || level > 100) error = T("Type the battery level from 1 to 100", "Напишите уровень заряда от 1 до 100");
            }
            for (int i = 0; error == null && i < editing.Steps.Count; i++)
            {
                string problem = StepProblem(editing.Steps[i], i);
                if (problem != null) error = T("Step ", "Шаг ") + (i + 1) + ": " + problem;
            }
            if (error != null)
            {
                scenarioError.Text = error;
                scenarioError.Visibility = Visibility.Visible;
                return;
            }

            editing.Name = name;
            int at = editingName == null ? -1 : cfg.Scenarios.FindIndex(sc => sc.Name == editingName);
            if (at >= 0) cfg.Scenarios[at] = editing;
            else cfg.Scenarios.Add(editing);
            CloseEditor();
            Save();
            if (run) Later(400, () => RunScenario(name));
        }

        static string ConditionProblem(string ck, string arg)
        {
            arg = arg ?? "";
            if ((ck == "running" || ck == "active" || ck == "device" || ck == "sound") && arg.Trim().Length == 0) return T("fill in what to check", "заполните, что проверять");
            TimeSpan t1, t2;
            string[] r = arg.Split('-');
            if (ck == "time" && (r.Length != 2 || !ScenarioCondition.TryTime(r[0], out t1) || !ScenarioCondition.TryTime(r[1], out t2))) return T("type the time like 09:00 and 18:00", "напишите время так: 09:00 и 18:00");
            if (ck == "days" && arg.IndexOf('1') < 0) return T("pick at least one day", "отметьте хотя бы один день");
            int screens;
            if (ck == "screens" && !(int.TryParse(arg.Trim(), out screens) && screens > 0)) return T("type the number of screens", "напишите число мониторов");
            return null;
        }

        static string StepProblem(ScenarioStep st, int index)
        {
            switch (st.Kind)
            {
                case "site":
                    string url = st.Value.Trim();
                    return url.Length == 0 || url == "https://" || url == "http://" ? T("type the site address", "напишите адрес сайта") : null;
                case "run": return st.Value.Trim().Length == 0 ? T("choose what to run", "выберите, что запустить") : null;
                case "type": return st.Value.Length == 0 ? T("type the text", "напишите текст") : null;
                case "keys":
                    uint mods;
                    int vk;
                    return KeyNames.TryParse(st.Value, out mods, out vk) ? null : T("press the keys", "нажмите клавиши");
                case "notice": return st.Value.Trim().Length == 0 ? T("type the note text", "напишите текст подписи") : null;
                case "clipboard": return st.Value.Length == 0 ? T("type the text", "напишите текст") : null;
                case "command": return st.Value.Trim().Length == 0 ? T("type the command", "напишите команду") : null;
                case "waitwindow": return Durations.Parse(st.Option) > 0 ? null : T("type how long to wait at most", "напишите, сколько ждать");
                case "wait": return st.Seconds <= 0 ? T("type how long to wait", "напишите, сколько ждать") : null;
                case "waitclose":
                    return st.Option.Trim().Length > 0 && Durations.Parse(st.Option) <= 0 ? T("type a time limit or leave it empty", "напишите предел или оставьте поле пустым") : null;
                case "if":
                case "condition":
                    string ck;
                    bool cn;
                    int cs;
                    ScenarioCondition.Parse(st.Option, out ck, out cn, out cs);
                    return ConditionProblem(ck, st.Value);
                case "click": return ClickSpot.Parse(st.Value).Recorded ? null : T("record the click", "запишите клик");
                case "fetch":
                    string furl = FetchPart(st, 0).Trim();
                    if (furl.Length == 0 || furl == "https://" || furl == "http://") return T("type the site address", "напишите адрес сайта");
                    string fmode = st.Option.Split(':')[0];
                    if (fmode == "between" && FetchPart(st, 1).Length == 0) return T("type the text that comes before the one you need", "напишите текст, после которого идёт нужный");
                    if (fmode == "regex")
                    {
                        if (FetchPart(st, 1).Length == 0) return T("type the pattern", "напишите шаблон");
                        try { new System.Text.RegularExpressions.Regex(FetchPart(st, 1)); }
                        catch (ArgumentException) { return T("the pattern has a mistake", "в шаблоне ошибка"); }
                    }
                    return null;
                case "window":
                    if (st.Value.StartsWith("program:", StringComparison.OrdinalIgnoreCase) && st.Value.Substring(8).Trim().Length == 0) return T("type the program", "напишите программу");
                    int ref1;
                    if (st.Value.StartsWith("step:", StringComparison.OrdinalIgnoreCase) && (!int.TryParse(st.Value.Substring(5), out ref1) || ref1 < 1 || ref1 > index))
                        return T("pick the window again", "выберите окно заново");
                    return null;
                case "repeat":
                    int from, to;
                    if (!int.TryParse(st.FlagValue("to"), out to)) to = index;
                    if (!int.TryParse(st.Value.Trim(), out from) || from < 1 || to > index || from > to)
                        return index == 0 ? T("put it below the steps to repeat", "поставьте его ниже шагов, которые нужно повторять") : T("step numbers must be from 1 to ", "номера шагов должны быть от 1 до ") + index;
                    if (RepeatSpec.Mode(st.Option) == "if")
                    {
                        string rk, ra;
                        bool rn;
                        RepeatSpec.ParseIf(st.Option, out rk, out rn, out ra);
                        return ConditionProblem(rk, ra);
                    }
                    if (ParseCount(st.Option) < 0 || ParseCount(st.Option) == 1) return T("type how many times, 2 or more", "напишите, сколько раз, от 2");
                    return int.TryParse(st.Value.Trim(), out from) && from >= 1 ? null : T("type the step number to start from", "напишите номер шага, с которого начать");
                case "volume":
                    if (st.Option != "set" && st.Option != "up" && st.Option != "down") return null;
                    int percent;
                    return int.TryParse(st.Value.Trim(), out percent) && percent >= 0 && percent <= 100 ? null : T("type a number from 0 to 100", "напишите число от 0 до 100");
            }
            return null;
        }
    }
}
