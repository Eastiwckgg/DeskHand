namespace DeskHand
{
    static class Loc
    {
        public static bool Ru;

        public static void Apply(Config cfg) { Ru = cfg.UiLanguage == "ru"; }

        public static string T(string en, string ru) { return Ru ? ru : en; }
    }
}
