namespace DeskHand
{
    static class ToggleNames
    {
        public static readonly string[] Corrections = { "Capitalize", "DoubleSpacePeriod", "AutoSpace", "AutoHyphen", "SmartQuotes", "AutoBrackets", "CapsLockFix", "Typos", "EnglishI", "Snippets" };
        public static readonly string[] Capitals = { "CapitalizeAfterEnter", "CapitalizeOnWindowSwitch", "CapitalizeAfterClick", "CapitalizeAfterEllipsis", "CapitalizeInBrackets" };
        public static readonly string[] Layout = { "LayoutFix", "LayoutPerApp", "LayoutIndicator", "LangRussian", "LangEnglish", "LangOther" };
        public static readonly string[] Other = { "PinOnTitleBar", "KeepAwakeScreen", "SkipFullscreen", "IgnoreInjected" };

        public static string Title(string key)
        {
            switch (key)
            {
                case "Capitalize": return Loc.T("Capitalize sentences", "Заглавная в начале предложения");
                case "DoubleSpacePeriod": return Loc.T("Period on double space", "Точка по двойному пробелу");
                case "AutoSpace": return Loc.T("Spaces around punctuation", "Пробелы у знаков препинания");
                case "AutoHyphen": return Loc.T("Hyphens in words", "Дефис в словах");
                case "SmartQuotes": return Loc.T("Russian quotes «»", "Кавычки-ёлочки");
                case "AutoBrackets": return Loc.T("Auto-close brackets", "Автоматические скобки");
                case "CapsLockFix": return Loc.T("Accidental Caps Lock", "Случайный Caps Lock");
                case "Typos": return Loc.T("Common mistakes", "Частые ошибки");
                case "EnglishI": return Loc.T("“i” → “I” in English", "«i» → «I» в английском");
                case "Snippets": return Loc.T("Text shortcuts", "Быстрые вставки");
                case "CapitalizeAfterEnter": return Loc.T("Capital after Enter", "Заглавная после Enter");
                case "CapitalizeOnWindowSwitch": return Loc.T("Capital after switching windows", "Заглавная после перехода в окно");
                case "CapitalizeAfterClick": return Loc.T("Capital after a click", "Заглавная после клика");
                case "CapitalizeAfterEllipsis": return Loc.T("Capital after an ellipsis", "Заглавная после многоточия");
                case "CapitalizeInBrackets": return Loc.T("Capital in brackets", "Заглавная в скобках");
                case "LayoutFix": return Loc.T("Fix the keyboard layout", "Исправлять раскладку");
                case "LayoutPerApp": return Loc.T("Layout for each app", "Раскладка для каждой программы");
                case "LayoutIndicator": return Loc.T("Layout indicator", "Индикатор раскладки");
                case "LangRussian": return Loc.T("Work in Russian", "Работать в русской раскладке");
                case "LangEnglish": return Loc.T("Work in English", "Работать в английской раскладке");
                case "LangOther": return Loc.T("Work in other layouts", "Работать в других раскладках");
                case "PinOnTitleBar": return Loc.T("Keep on top button", "Кнопка «Поверх всех окон»");
                case "KeepAwakeScreen": return Loc.T("Keep the screen on", "Не гасить экран");
                case "SkipFullscreen": return Loc.T("Stay out of full-screen apps", "Не мешать в играх и полноэкранных");
                case "IgnoreInjected": return Loc.T("Ignore input from other programs", "Не реагировать на ввод других программ");
            }
            return key;
        }
    }
}
