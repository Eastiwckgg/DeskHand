using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DeskHand
{
    static class EngineTests
    {
        static int failed, total;

        static int Main()
        {
            Dictionaries.LoadNow();
            var cfg = Fresh();

            Check(cfg, "привет. как дела? норм! ок", "Привет. Как дела? Норм! Ок");
            Check(cfg, "hello world  next", "Hello world. Next");
            Check(cfg, "т.е. так. и т.д. Далее", "Т.е. так. И т.д. Далее");
            Check(cfg, "а. с. пушкин", "А. с. Пушкин");
            Check(cfg, "в 2020 г. было", "В 2020 г. было");
            Check(cfg, "ну... ладно", "Ну... ладно");
            Check(cfg, "строка\nновая", "Строка\nНовая");
            Check(cfg, "\bпривет", "привет");
            Check(cfg, "п\bпривет", "привет");
            Check(cfg, "i said no. then", "I said no. Then");

            Check(cfg, "один  два", "Один. Два");
            Check(cfg, "один ~ два", "Один  два");
            Check(cfg, "конец.  дальше", "Конец.  Дальше");
            Check(cfg, "один  \b", "Один  ");

            Check(cfg, "привет ,как дела", "Привет, как дела");
            Check(cfg, "привет,как", "Привет, как");
            Check(cfg, "конец.начало", "Конец. Начало");
            Check(cfg, "привет!как дела", "Привет! Как дела");
            Check(cfg, "это site.com тут", "Это site.com тут");
            Check(cfg, "т.е.так", "Т.е.так");
            Check(cfg, "число 3,5 и 10:30", "Число 3,5 и 10:30");
            Check(cfg, "смайл :)", "Смайл :)");
            Check(cfg, "ссылка example.org/a?b,c", "Ссылка example.org/a?b,c");
            Check(cfg, "привет,к\b", "Привет,к");

            Check(cfg, "он сказал \"привет\" и ушёл", "Он сказал «привет» и ушёл");
            Check(cfg, "\"а \"б\" в\"", "«А „б“ в»");
            Check(cfg, "\"\b", "\"");
            CheckLang(cfg, Lang.En, "he said \"hi\"", "He said \"hi\"");

            CheckCaps(cfg, "пРИВЕТ ", "Привет ");
            CheckCaps(cfg, "ПРИВЕТ ", "ПРИВЕТ ");
            CheckCapsMid(cfg, "Привет ", "пРИВЕТ ", "Привет привет ");
            CheckCapsMid(cfg, "Конец. ", "пРИВЕТ ", "Конец. Привет ");

            Check(cfg, "слово(пояснение)", "Слово (пояснение)");
            Check(cfg, "слово (пояснение)", "Слово (пояснение)");
            Check(cfg, "жаль((", "Жаль ((");
            Check(cfg, "смайл :(", "Смайл :(");
            Check(cfg, "ссылка site.com/a(b)", "Ссылка site.com/a(b)");
            Check(cfg, "слово(\b", "Слово(");
            Check(cfg, "ну извене.", "Ну извини.");

            Check(cfg, "вообщем, да", "В общем, да");
            Check(cfg, "ну извени.", "Ну извини.");
            Check(cfg, "teh end", "The end");
            Check(cfg, "ok i think", "Ok I think");
            Check(cfg, "ok i'm fine", "Ok I'm fine");
            Check(cfg, "ok it is", "Ok it is");
            Check(cfg, "ну вообщем \b", "Ну вообщем ");

            Check(cfg, "кое как сделал", "Кое-как сделал");
            Check(cfg, "кое с кем поговорил", "Кое с кем поговорил");
            Check(cfg, "когда нибудь приеду", "Когда-нибудь приеду");
            Check(cfg, "либо да, либо нет", "Либо да, либо нет");
            Check(cfg, "всё таки пришёл", "Всё-таки пришёл");
            Check(cfg, "он таки пришёл", "Он таки пришёл");
            Check(cfg, "точь в точь как", "Точь-в-точь как");
            Check(cfg, "по моему, да", "По-моему, да");
            Check(cfg, "по моему совету", "По моему совету");
            Check(cfg, "сделай по другому не выйдет", "Сделай по-другому не выйдет");
            Check(cfg, "кое как \b", "Кое как ");
            Check(cfg, "какието слова", "Какие-то слова");
            Check(cfg, "какойнибудь вариант", "Какой-нибудь вариант");
            Check(cfg, "ктото пришёл", "Кто-то пришёл");
            Check(cfg, "Чтолибо, да", "Что-либо, да");
            Check(cfg, "коекак сделал", "Кое-как сделал");
            Check(cfg, "изза тебя", "Из-за тебя");
            Check(cfg, "помоему да", "По-моему да");
            Check(cfg, "вопервых да", "Во-первых да");
            Check(cfg, "точьвточь как", "Точь-в-точь как");
            Check(cfg, "дайка мне", "Дай-ка мне");
            Check(cfg, "вообще то да", "Вообще-то да");
            Check(cfg, "вообщето да", "Вообще-то да");
            Check(cfg, "какието \b", "Какието ");
            Check(cfg, "какое то", "Какое то");
            Check(cfg, "поиски собака то", "Поиски собака то");
            Check(cfg, "койка кое", "Койка кое");
            Check(cfg, "что нибудь\nок", "Что-нибудь\nОк");
            Check(cfg, "кое как  ок", "Кое-как. Ок");

            CheckLang(cfg, Lang.En, "ghbdtn rfr ltkf", "Привет как дела");
            CheckLang(cfg, Lang.Ru, "руддщ цщкдв", "Hello world");
            CheckLang(cfg, Lang.En, "hello world", "Hello world");
            CheckLang(cfg, Lang.En, "lf ", "Да ");
            CheckLang(cfg, Lang.En, "ghbdtn? rfr", "Привет, как");
            CheckLang(cfg, Lang.En, "ghbdtn \b", "Ghbdtn ");
            CheckLang(cfg, Lang.En, "iphone 15", "Iphone 15");
            CheckLang(cfg, Lang.En, "test@mail.ru ", "Test@mail.ru ");

            Check(cfg, "спс ", "Спасибо ");
            Check(cfg, "ну спс.", "Ну спасибо.");
            Check(cfg, "спс \b", "Спс ");
            var withMail = Fresh();
            withMail.SnippetList[";почта"] = "me@mail.ru";
            withMail.SnippetList["подп"] = "С уважением,\nРуди";
            Check(withMail, "пиши ;почта ", "Пиши me@mail.ru ");
            Check(withMail, "подп\nпс", "С уважением,\nРуди\nПс");
            string iniPath = Path.Combine(Path.GetTempPath(), "bw-snip-" + Guid.NewGuid().ToString("N") + ".ini");
            withMail.Save(iniPath);
            var reloaded = Config.Load(iniPath);
            File.Delete(iniPath);
            Expect(reloaded.SnippetList.ContainsKey(";почта") && reloaded.SnippetList["подп"] == "С уважением,\nРуди", "вставки после сохранения");

            var custom = Fresh();
            custom.CustomTypos["превед"] = "привет";
            custom.CustomTypos["cnj"] = "Сто";
            custom.LayoutIgnore.Add("ghbdtn");
            Check(custom, "ну превед ", "Ну привет ");
            Check(custom, "превед ", "Привет ");
            Check(custom, "ну превед \b", "Ну превед ");
            CheckLang(custom, Lang.En, "ghbdtn ", "Ghbdtn ");
            CheckLang(custom, Lang.En, "ok cnj ", "Ok Сто ");
            CheckLang(cfg, Lang.En, "ok cnj ", "Ok сто ");
            string customPath = Path.Combine(Path.GetTempPath(), "bw-custom-" + Guid.NewGuid().ToString("N") + ".ini");
            custom.PauseHotkey = "";
            custom.Save(customPath);
            var customLoaded = Config.Load(customPath);
            File.Delete(customPath);
            Expect(customLoaded.CustomTypos["превед"] == "привет" && customLoaded.LayoutIgnore.Contains("ghbdtn") && customLoaded.PauseHotkey == "", "custom corrections survive saving");

            uint mods, vk;
            Expect(Hotkey.TryParse("Ctrl+Alt+B", out mods, out vk) && mods == (Hotkey.ModControl | Hotkey.ModAlt) && vk == 0x42, "parse Ctrl+Alt+B");
            Expect(Hotkey.TryParse("Win+F12", out mods, out vk) && mods == Hotkey.ModWin && vk == 0x7B, "parse Win+F12");
            Expect(!Hotkey.TryParse("B", out mods, out vk) && !Hotkey.TryParse("Shift+B", out mods, out vk) && !Hotkey.TryParse("", out mods, out vk), "reject hotkeys without Ctrl, Alt or Win");
            Expect(Hotkey.Format(true, true, false, false, 0x42) == "Ctrl+Alt+B" && Hotkey.Format(false, false, true, false, 0x42) == null && Hotkey.Format(true, false, false, false, 0x20) == null, "format hotkeys");

            var defaults = Fresh();
            Expect(!defaults.AutoBrackets && !defaults.CapitalizeInBrackets && defaults.BracketExit == "hold" && defaults.IndicatorSize == "small" && !defaults.LayoutPerApp && defaults.Theme == "dark", "new defaults");

            var twoSpaces = Fresh();
            twoSpaces.AutoBrackets = true;
            twoSpaces.CapitalizeInBrackets = true;
            twoSpaces.BracketExit = "double";
            Check(twoSpaces, "(привет  ", "(Привет)");
            Check(twoSpaces, "(привет  и", "(Привет)и");
            Check(twoSpaces, "слово (привет) и", "Слово (Привет) и");
            Check(twoSpaces, "слово [да  ", "Слово [Да]");
            Check(twoSpaces, "слово {да}", "Слово {Да}");
            Check(twoSpaces, "(а (б  в  ", "(А (Б)в)");
            Check(twoSpaces, "(\bа", "А");
            Check(twoSpaces, "ну \"да  ", "Ну «да»");
            CheckLang(twoSpaces, Lang.En, "say \"hi  ", "Say \"hi\"");
            Check(twoSpaces, "ссылка a.com/b(c", "Ссылка a.com/b(c");
            Check(twoSpaces, "\"(\", \"{\" и", "«()», «{}» и");
            Check(twoSpaces, "жаль((", "Жаль (()");

            var holdExit = Fresh();
            holdExit.AutoBrackets = true;
            holdExit.BracketExit = "hold";
            Check(holdExit, "ну (да ␣␣␣", "Ну (да)");
            Check(holdExit, "ну (да ␣␣␣ и", "Ну (да) и");
            Check(holdExit, "ну (да  и", "Ну (да. И)");
            Check(holdExit, "ну (да ^␣␣ и", "Ну (да) и");
            Check(holdExit, "ну (да ^и", "Ну (да)и");
            Check(holdExit, "ну да ^и", "Ну да и");
            Check(holdExit, "ну (^и", "Ну (и)");
            Check(holdExit, "ну да ␣␣", "Ну да   ");

            var noPairs = Fresh();
            Check(noPairs, "\"(\", \"{\" и", "«(», «{» и");
            Check(noPairs, "\"а \"б\" в\"", "«А „б“ в»");
            Check(noPairs, "слово (да)", "Слово (да)");
            var vars = Fresh();
            vars.SnippetList["имя"] = "{date:yyyy}, {clipboard}, {нет}";
            var varEngine = new Engine(vars) { Now = () => new DateTime(2026, 11, 2), ClipboardText = () => "буфер" };
            varEngine.Reset(false);
            Verify(varEngine, vars, "", "имя ", "2026, буфер, {нет} ", "snippet variables");

            var awake = Fresh();
            var clock = new DateTime(2026, 11, 2, 12, 0, 0);
            Expect(!awake.KeepAwakeActive(clock), "stay awake is off by default");
            awake.KeepAwakeHours = 3;
            awake.StartKeepAwake(clock);
            Expect(awake.KeepAwakeActive(clock.AddHours(2.9)) && !awake.KeepAwakeActive(clock.AddHours(3.1)), "stay awake ends on time");
            awake.KeepAwakeHours = 0;
            awake.StartKeepAwake(clock);
            Expect(awake.KeepAwakeActive(clock.AddYears(1)), "stay awake until turned off");
            string awakePath = Path.Combine(Path.GetTempPath(), "bw-awake-" + Guid.NewGuid().ToString("N") + ".ini");
            awake.KeepAwakeHotkey = "Ctrl+Alt+K";
            awake.Save(awakePath);
            var awakeLoaded = Config.Load(awakePath);
            File.Delete(awakePath);
            Expect(awakeLoaded.KeepAwakeUntil == "forever" && awakeLoaded.KeepAwakeHotkey == "Ctrl+Alt+K" && awakeLoaded.KeepAwakeHours == 0, "stay awake survives saving");
            string oldIni = Path.Combine(Path.GetTempPath(), "bw-old-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(oldIni, "[Main]\nCapitalize=0\nAutoBrackets=1\nLayoutPerApp=1\nBracketExit=tab\n[Variables]\nenabled = 0\n", Encoding.UTF8);
            var oldLoaded = Config.Load(oldIni);
            Expect(!oldLoaded.Capitalize && oldLoaded.Enabled && oldLoaded.Snippets && !oldLoaded.AutoBrackets && !oldLoaded.LayoutPerApp && oldLoaded.BracketExit == "hold", "an older settings file is upgraded");
            Expect(File.Exists(oldIni + ".v1.bak") && File.ReadAllText(oldIni).Contains("SettingsVersion=" + Config.CurrentVersion), "the upgrade keeps a backup and marks the version");
            File.Delete(oldIni + ".v1.bak");
            var upgraded = Config.Load(oldIni);
            upgraded.AutoBrackets = true;
            upgraded.Save(oldIni);
            Expect(Config.Load(oldIni).AutoBrackets && !File.Exists(oldIni + ".v1.bak"), "an upgraded file is not upgraded again");
            File.Delete(oldIni);
            string newerIni = Path.Combine(Path.GetTempPath(), "bw-new-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(newerIni, "[Main]\nSettingsVersion=99\nCapitalize=0\nFutureOption=1\n", Encoding.UTF8);
            File.WriteAllText(newerIni + ".v1.bak", "old", Encoding.UTF8);
            var newerLoaded = Config.Load(newerIni);
            Expect(!newerLoaded.Capitalize && File.Exists(newerIni + ".v99.bak") && File.ReadAllText(newerIni).Contains("FutureOption") && !File.Exists(newerIni + ".v1.bak"), "a file from a newer version is backed up, left alone, and only the last backup stays");
            File.Delete(newerIni);
            File.Delete(newerIni + ".v99.bak");
            var noEnglish = Fresh();
            noEnglish.LangEnglish = false;
            CheckLang(noEnglish, Lang.En, "hello  world", "hello  world");
            CheckLang(noEnglish, Lang.Ru, "привет  мир", "Привет. Мир");

            CheckContext(cfg, "Привет", true, "ка", "Приветка");
            CheckContext(cfg, "Привет ", true, "как", "Привет как");
            CheckContext(cfg, "Привет. ", true, "как", "Привет. Как");
            CheckContext(cfg, "", true, "как", "Как");
            CheckContext(cfg, "Первая строка\nВторая. ", true, "и", "Первая строка\nВторая. И");
            CheckContext(cfg, "Режим чата\n\uFFFC", false, "привет", "Режим чата\n\uFFFCПривет");
            CheckPassword(cfg);

            var rules = Fresh();
            rules.AppRules["Telegram"] = Config.List("Capitalize, DoubleSpacePeriod");
            Expect(!rules.ForApp("telegram").Capitalize && rules.ForApp("telegram").AutoHyphen, "правило для Telegram");
            Expect(rules.ForApp("chrome").Capitalize, "без правила всё включено");
            Check(rules.ForApp("Telegram"), "привет  мир", "привет  мир");

            var withScenario = Fresh();
            var sc = new Scenario { Name = "Работа | тест", Hotkey = "Ctrl+Alt+I", InTray = false, OnStartup = true };
            sc.Steps.Add(new ScenarioStep { Kind = "site", Option = "window", Value = "https://example.com/?a=1|2" });
            sc.Steps.Add(new ScenarioStep { Kind = "wait", Value = "10" });
            sc.Steps.Add(new ScenarioStep { Kind = "type", Value = "строка\nвторая \\ конец" });
            sc.Steps.Add(new ScenarioStep { Kind = "close", Option = "program" });
            withScenario.Scenarios.Add(sc);
            withScenario.Scenarios.Add(new Scenario { Name = "Второй" });
            string scenarioPath = Path.Combine(Path.GetTempPath(), "bw-sc-" + Guid.NewGuid().ToString("N") + ".ini");
            withScenario.Save(scenarioPath);
            var scLoaded = Config.Load(scenarioPath);
            File.Delete(scenarioPath);
            Expect(scLoaded.Scenarios.Count == 2 && scLoaded.Scenarios[1].Name == "Второй" && scLoaded.Scenarios[1].InTray, "scenarios survive saving");
            var first = scLoaded.Scenarios[0];
            Expect(first.Name == "Работа | тест" && first.Hotkey == "Ctrl+Alt+I" && !first.InTray && first.OnStartup && first.Steps.Count == 4, "scenario fields survive saving");
            Expect(first.Steps[0].Kind == "site" && first.Steps[0].Option == "window" && first.Steps[0].Value == "https://example.com/?a=1|2", "a site step survives saving");
            Expect(first.Steps[1].Seconds == 10 && first.Steps[2].Value == "строка\nвторая \\ конец" && first.Steps[3].Option == "program", "steps keep their values");
            Expect(new ScenarioStep { Kind = "wait", Value = "0,5" }.Seconds == 0.5 && new ScenarioStep { Kind = "wait", Value = "abc" }.Seconds == 0, "wait seconds are parsed");
            Expect(ScenarioStep.Parse("unknown | x | y") == null, "an unknown step is skipped");
            uint keyMods;
            int keyVk;
            Expect(KeyNames.TryParse("Ctrl+W", out keyMods, out keyVk) && keyMods == Hotkey.ModControl && keyVk == 0x57, "Ctrl+W is parsed");
            Expect(KeyNames.TryParse("Enter", out keyMods, out keyVk) && keyMods == 0 && keyVk == 0x0D, "a key without modifiers is parsed");
            Expect(KeyNames.TryParse("Win+D", out keyMods, out keyVk) && keyMods == Hotkey.ModWin && keyVk == 0x44, "Win+D is parsed");
            Expect(KeyNames.Format(true, false, true, true, 0x25) == "Win+Ctrl+Shift+Left" && !KeyNames.TryParse("Ctrl+W+Q", out keyMods, out keyVk), "key names are formatted and checked");

            var timed = Fresh();
            var morning = new Scenario { Name = "Утро", Time = "09:05", Days = "1111100", WhenOpened = "Discord, Telegram.exe" };
            morning.Steps.Add(new ScenarioStep { Kind = "repeat", Option = "0", Value = "1" });
            morning.Steps.Add(new ScenarioStep { Kind = "volume", Option = "set", Value = "30" });
            timed.Scenarios.Add(morning);
            string timedPath = Path.Combine(Path.GetTempPath(), "bw-time-" + Guid.NewGuid().ToString("N") + ".ini");
            timed.Save(timedPath);
            var timedLoaded = Config.Load(timedPath).Scenarios[0];
            File.Delete(timedPath);
            Expect(timedLoaded.Time == "09:05" && timedLoaded.Days == "1111100" && timedLoaded.WhenOpened == "Discord, Telegram.exe" && timedLoaded.Steps.Count == 2, "triggers survive saving");
            Expect(timedLoaded.DueAt(new DateTime(2026, 11, 9, 9, 5, 30)) && !timedLoaded.DueAt(new DateTime(2026, 11, 9, 9, 6, 0)), "a schedule fires only in its minute");
            Expect(!timedLoaded.DueAt(new DateTime(2026, 11, 8, 9, 5, 0)), "a schedule skips unchecked days");
            Expect(new Scenario { Time = "7:30" }.DueAt(new DateTime(2026, 11, 9, 7, 30, 0)), "a one digit hour works");
            Expect(timedLoaded.OpensWith("discord") && timedLoaded.OpensWith("Telegram") && !timedLoaded.OpensWith("chrome"), "the program trigger matches names");
            string rawPath = WriteTemp("[Scenario]\nName = x\nDays = 12\n");
            Expect(Config.Load(rawPath).Scenarios[0].Days == "1111111", "broken days fall back to every day");
            File.Delete(rawPath);
            File.Delete(rawPath + ".v1.bak");

            Expect(Durations.Parse("5") == 5 && Durations.Parse("5s") == 5 && Durations.Parse("2m") == 120 && Durations.Parse("1,5h") == 5400 && Durations.Parse("") == 0 && Durations.Parse("x") == 0, "durations are parsed");
            string dn, du;
            Durations.Split("15m", out dn, out du);
            Expect(dn == "15" && du == "m" && Durations.Join("15", "m") == "15m" && Durations.Join("7", "s") == "7" && Durations.Join("", "h") == "", "durations split and join");
            Expect(new ScenarioStep { Kind = "wait", Value = "2m" }.Seconds == 120, "a wait step understands minutes");
            string ck;
            bool cneg;
            int cskip;
            ScenarioCondition.Parse("running!:skip3", out ck, out cneg, out cskip);
            Expect(ck == "running" && cneg && cskip == 3 && ScenarioCondition.Format(ck, cneg, cskip) == "running!:skip3", "a condition is parsed and written back");
            ScenarioCondition.Parse("wifi:stop", out ck, out cneg, out cskip);
            Expect(ck == "wifi" && !cneg && cskip == 0, "a stopping condition is parsed");
            Expect(ScenarioCondition.InTimeRange("09:00-18:00", new DateTime(2026, 1, 1, 12, 0, 0)) && !ScenarioCondition.InTimeRange("09:00-18:00", new DateTime(2026, 1, 1, 18, 0, 0)), "a time range works");
            Expect(ScenarioCondition.InTimeRange("22:00-06:00", new DateTime(2026, 1, 1, 23, 30, 0)) && ScenarioCondition.InTimeRange("22:00-06:00", new DateTime(2026, 1, 1, 5, 0, 0)) && !ScenarioCondition.InTimeRange("22:00-06:00", new DateTime(2026, 1, 1, 12, 0, 0)), "a range over midnight works");
            Expect(ScenarioCondition.OnDay("1111100", new DateTime(2026, 11, 9)) && !ScenarioCondition.OnDay("1111100", new DateTime(2026, 11, 8)), "day of the week works");
            var full = Fresh();
            var game = new Scenario { Name = "Игра", Ask = true, WhenClosed = "Game", Idle = "15m", IdleEvent = "back", Event = "device-in", EventArg = "Headphones | 2" };
            game.Steps.Add(new ScenarioStep { Kind = "condition", Option = "running!:skip2", Value = "Steam" });
            game.Steps.Add(new ScenarioStep { Kind = "waitclose", Option = "1h", Value = "Game" });
            game.Steps.Add(new ScenarioStep { Kind = "repeat", Option = "while:Game", Value = "1" });
            full.Scenarios.Add(game);
            string fullPath = Path.Combine(Path.GetTempPath(), "bw-full-" + Guid.NewGuid().ToString("N") + ".ini");
            full.Save(fullPath);
            var g = Config.Load(fullPath).Scenarios[0];
            File.Delete(fullPath);
            Expect(g.Ask && g.WhenClosed == "Game" && g.Idle == "15m" && g.IdleEvent == "back" && g.Event == "device-in" && g.EventArg == "Headphones | 2", "new triggers survive saving");
            Expect(g.Steps.Count == 3 && g.Steps[0].Option == "running!:skip2" && g.Steps[1].Option == "1h" && g.Steps[2].Option == "while:Game", "new steps survive saving");
            Expect(g.ClosesWith("game") && !g.OpensWith("game"), "the close trigger matches names");

            var flagged = new ScenarioStep { Kind = "run", Option = "-x", Value = "C:\\a|b.exe" };
            flagged.Set("wait", true);
            Expect(flagged.Has("wait") && flagged.Flags == "wait", "a step flag is set");
            var withFlags = Fresh();
            var fs = new Scenario { Name = "F" };
            fs.Steps.Add(flagged);
            fs.Steps.Add(new ScenarioStep { Kind = "type", Value = "~не флаг | текст" });
            fs.Steps.Add(new ScenarioStep { Kind = "setting", Option = "LayoutIndicator", Value = "off" });
            fs.Steps.Add(new ScenarioStep { Kind = "window", Option = "left", Value = "step:1" });
            withFlags.Scenarios.Add(fs);
            string flagPath = Path.Combine(Path.GetTempPath(), "bw-flag-" + Guid.NewGuid().ToString("N") + ".ini");
            withFlags.Save(flagPath);
            var fl = Config.Load(flagPath).Scenarios[0];
            File.Delete(flagPath);
            Expect(fl.Steps[0].Has("wait") && fl.Steps[0].Option == "-x" && fl.Steps[0].Value == "C:\\a|b.exe", "flags survive saving and the value keeps its bar");
            Expect(fl.Steps[1].Value == "~не флаг | текст" && fl.Steps[1].Flags == "", "a value that starts with a tilde is not a flag");
            Expect(fl.Steps[2].Option == "LayoutIndicator" && fl.Steps[2].Value == "off" && fl.Steps[3].Value == "step:1", "setting and window steps survive saving");
            Expect(ScenarioStep.Parse("run | | C:\\x.exe").Flags == "", "an old step has no flags");
            Expect(RepeatSpec.Mode("3") == "count" && RepeatSpec.Mode("0") == "forever" && RepeatSpec.Mode("while:Game") == "if" && RepeatSpec.Mode("if:time:09:00-18:00") == "if", "repeat modes are recognized");
            string rk, ra;
            bool rn;
            RepeatSpec.ParseIf("while:Game", out rk, out rn, out ra);
            Expect(rk == "running" && !rn && ra == "Game", "the old while form still works");
            RepeatSpec.ParseIf(RepeatSpec.FormatIf("time", true, "09:00-18:00"), out rk, out rn, out ra);
            Expect(rk == "time" && rn && ra == "09:00-18:00", "a repeat condition with colons in the argument survives");
            var toggles = Fresh();
            bool allKnown = true;
            foreach (string key in Config.Toggles)
            {
                bool? before = toggles.GetToggle(key);
                if (before == null) { allKnown = false; continue; }
                toggles.SetToggle(key, !before.Value);
                if (toggles.GetToggle(key) != !before.Value) allKnown = false;
            }
            Expect(allKnown && toggles.GetToggle("Nope") == null, "every setting can be read and switched");

            var ranged = new ScenarioStep { Kind = "repeat", Option = "3", Value = "1" };
            ranged.Set("wait", true);
            ranged.SetFlagValue("to", "2");
            Expect(ranged.FlagValue("to") == "2" && ranged.Has("wait"), "a flag with a value lives next to a plain flag");
            ranged.SetFlagValue("to", "");
            Expect(ranged.FlagValue("to") == "" && ranged.Flags == "wait", "an empty flag value removes it");
            Expect(Scenario.Names("Discord, Telegram.exe, ,steam").SequenceEqual(new[] { "Discord", "Telegram", "steam" }), "a program list is split");

            var blockSteps = new System.Collections.Generic.List<ScenarioStep>();
            foreach (string k in new[] { "if", "wait", "if", "wait", "else", "wait", "endif", "else", "wait", "endif", "wait" })
                blockSteps.Add(new ScenarioStep { Kind = k });
            System.Collections.Generic.Dictionary<int, int> elseOf, endOf, elseEnd;
            Blocks.Match(blockSteps, out elseOf, out endOf, out elseEnd);
            Expect(elseOf[0] == 7 && endOf[0] == 9 && elseOf[2] == 4 && endOf[2] == 6 && elseEnd[7] == 9 && elseEnd[4] == 6, "nested blocks are matched");
            Expect(Blocks.Depths(blockSteps).SequenceEqual(new[] { 0, 1, 1, 2, 1, 2, 1, 0, 1, 0, 0 }), "nested blocks get their depth");
            var broken = new System.Collections.Generic.List<ScenarioStep> { new ScenarioStep { Kind = "if" }, new ScenarioStep { Kind = "wait" } };
            Blocks.Match(broken, out elseOf, out endOf, out elseEnd);
            Expect(endOf[0] == 1 && !elseOf.ContainsKey(0), "an unclosed block ends with the scenario");

            var spaced = Fresh();
            var bufScenario = new Scenario { Name = "Buf" };
            bufScenario.Steps.Add(new ScenarioStep { Kind = "buffer", Option = "set", Value = "Сейчас у меня " });
            bufScenario.Steps.Add(new ScenarioStep { Kind = "buffer", Option = "append", Value = " айпишник" });
            bufScenario.Steps.Add(new ScenarioStep { Kind = "type", Value = "  " });
            bufScenario.Steps.Add(new ScenarioStep { Kind = "fetch", Option = "between:append", Value = "https://example.com\nВаш IP: \n" });
            spaced.Scenarios.Add(bufScenario);
            string spacedPath = Path.Combine(Path.GetTempPath(), "bw-space-" + Guid.NewGuid().ToString("N") + ".ini");
            spaced.Save(spacedPath);
            var sp = Config.Load(spacedPath).Scenarios[0];
            File.Delete(spacedPath);
            Expect(sp.Steps[0].Value == "Сейчас у меня " && sp.Steps[1].Value == " айпишник" && sp.Steps[2].Value == "  ", "spaces at the edges of a value are kept");
            Expect(sp.Steps[3].Value == "https://example.com\nВаш IP: \n" && sp.Steps[3].Option == "between:append", "a multi-line fetch value is kept");
            Expect(Config.Unescape("a\\sb") == "a b" && Config.Escape("a\\sb") == "a\\\\sb", "a real backslash-s survives");

            Expect(ScenarioCondition.TextMatches("clipboard\ncontains\nIP", "мой ip такой", "") && !ScenarioCondition.TextMatches("clipboard\ncontains\nIPv6", "мой ip", ""), "contains ignores case");
            Expect(ScenarioCondition.TextMatches("buffer\nnocase\nПривет", "", "  привет ") && !ScenarioCondition.TextMatches("buffer\nexact\nПривет", "", "привет"), "same text with and without case");
            Expect(ScenarioCondition.TextMatches("buffer\nexact\nA b", "x", "A b") && ScenarioCondition.TextMatches("clipboard\nempty\n", " ", "x"), "exact and empty comparisons");

            string page = "<html><head><title>T</title><style>p{}</style><script>var ip='0.0.0.0'</script></head><body><p>Ваш IP: 10.20.30.40</p><div>Город:&nbsp;Москва</div></body></html>";
            Expect(WebText.Strip(page).Contains("Ваш IP: 10.20.30.40") && !WebText.Strip(page).Contains("0.0.0.0") && WebText.Strip(page).Contains("Город: Москва"), "a page is turned into plain text");
            Expect(WebText.Extract(page, "between", "ваш ip:", "") == "10.20.30.40", "text between a marker and the line end");
            Expect(WebText.Extract(page, "between", "Город:", "Нет такого") == null && WebText.Extract(page, "between", "Город:", "") == "Москва", "between needs its end marker");
            Expect(WebText.Extract(page, "regex", @"\b\d{1,3}(?:\.\d{1,3}){3}\b", "") == "10.20.30.40", "a pattern finds the address");
            Expect(WebText.Extract(page, "regex", @"Город: (\w+)", "") == "Москва" && WebText.Extract(page, "regex", "(", "") == null, "a pattern group is used and a broken pattern gives nothing");
            Expect(WebText.Extract("203.0.113.5\n", "all", "", "") == "203.0.113.5", "a plain text answer is taken as is");

            var spot = ClickSpot.Parse("0.25;0.75;window;Telegram;100;200");
            Expect(spot.X == 0.25 && spot.Y == 0.75 && spot.Mode == "window" && spot.Program == "Telegram" && spot.ScreenX == 100 && spot.Recorded, "a click spot is parsed");
            Expect(ClickSpot.Parse(spot.ToString()).ToString() == spot.ToString() && !ClickSpot.Parse("").Recorded && ClickSpot.Parse("0;0;screen;;5;6").Recorded, "a click spot is written back");

            Console.WriteLine(failed == 0 ? "OK: all " + total + " tests passed" : "FAILED: " + failed + " of " + total);
            return failed;
        }

        static string WriteTemp(string text)
        {
            string path = Path.Combine(Path.GetTempPath(), "bw-raw-" + Guid.NewGuid().ToString("N") + ".ini");
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }

        static Config Fresh()
        {
            string ini = Path.Combine(Path.GetTempPath(), "bw-test-" + Guid.NewGuid().ToString("N") + ".ini");
            var cfg = Config.Load(ini);
            File.Delete(ini);
            return cfg;
        }

        static void Expect(bool ok, string label)
        {
            total++;
            if (ok) return;
            failed++;
            Console.WriteLine("FAIL: " + label);
        }

        static void Check(Config cfg, string typed, string expected) { CheckLang(cfg, Lang.Ru, typed, expected); }

        static void CheckLang(Config cfg, Lang lang, string typed, string expected)
        {
            var engine = new Engine(cfg) { Lang = lang };
            engine.Reset(true);
            Verify(engine, cfg, "", typed, expected, typed);
        }

        static void CheckCaps(Config cfg, string typed, string expected)
        {
            var engine = new Engine(cfg) { CapsLock = true };
            engine.Reset(true);
            Verify(engine, cfg, "", typed, expected, "[caps] " + typed);
        }

        static void CheckCapsMid(Config cfg, string before, string typed, string expected)
        {
            var engine = new Engine(cfg);
            engine.SetContext(before, true, false, true);
            engine.CapsLock = true;
            Verify(engine, cfg, before, typed, expected, "[" + before + "][caps] " + typed);
        }

        static void CheckContext(Config cfg, string before, bool atStart, string typed, string expected)
        {
            var engine = new Engine(cfg);
            engine.SetContext(before, atStart, false, true);
            Verify(engine, cfg, before, typed, expected, "[" + before + "]" + typed);
        }

        static void CheckPassword(Config cfg)
        {
            var engine = new Engine(cfg);
            engine.SetContext("", true, true, true);
            Verify(engine, cfg, "", "secret  кое как", "secret  кое как", "password field");
        }

        static void Verify(Engine engine, Config cfg, string initial, string typed, string expected, string label)
        {
            total++;
            var screen = new StringBuilder(initial);
            int caret = initial.Length;
            long now = 1000;
            Lang startLang = engine.Lang;
            foreach (char key in typed)
            {
                char c = key;
                if (engine.Lang != startLang && key != '\b' && key != '\n' && key != '~')
                {
                    string conv = Layouts.Convert(key.ToString(), startLang);
                    if (conv != null) c = conv[0];
                }
                if (c == '~') { now += 5000; continue; }
                if (c == '^')
                {
                    var leave = engine.OnSpaceHeld();
                    Apply(screen, ref caret, leave);
                    continue;
                }
                bool held = c == '␣';
                if (held) c = ' ';
                now += 100;
                EngineAction act;
                if (c == '\b')
                {
                    act = engine.OnBackspace();
                    if (!act.Replace)
                    {
                        if (caret > 0) { screen.Remove(caret - 1, 1); caret--; }
                        continue;
                    }
                }
                else if (c == '\n')
                {
                    act = engine.OnEnter();
                    Apply(screen, ref caret, act);
                    screen.Insert(caret++, '\n');
                    engine.Reset(cfg.CapitalizeAfterEnter);
                    continue;
                }
                else
                {
                    act = engine.OnChar(c, now, held, true);
                    if (!act.Replace) { screen.Insert(caret++, c); continue; }
                }
                Apply(screen, ref caret, act);
            }
            string got = screen.ToString();
            if (got == expected) return;
            failed++;
            Console.WriteLine("FAIL: \"" + Show(label) + "\"\n  expected: \"" + Show(expected) + "\"\n  got:      \"" + Show(got) + "\"");
        }

        static void Apply(StringBuilder screen, ref int caret, EngineAction act)
        {
            if (!act.Replace) return;
            int back = Math.Min(act.Backspaces, caret);
            screen.Remove(caret - back, back);
            caret -= back;
            screen.Insert(caret, act.Text);
            caret += act.Text.Length;
            caret = Math.Max(0, caret - act.CaretLeft);
            caret = Math.Min(screen.Length, caret + act.CaretRight);
            if (act.DeleteRight > 0) screen.Remove(caret, Math.Min(act.DeleteRight, screen.Length - caret));
        }

        static string Show(string s) { return s.Replace("\n", "\\n").Replace("\b", "\\b"); }
    }
}
