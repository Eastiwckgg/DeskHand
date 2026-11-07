using System;
using System.Collections.Generic;

namespace DeskHand
{
    static class Hotkey
    {
        public const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModWin = 8, ModNoRepeat = 0x4000;

        public static bool TryParse(string text, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= ModControl;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= ModAlt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= ModShift;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= ModWin;
                else
                {
                    if (vk != 0) return false;
                    vk = KeyCode(part);
                    if (vk == 0) return false;
                }
            }
            return vk != 0 && (mods & (ModControl | ModAlt | ModWin)) != 0;
        }

        public static string Format(bool ctrl, bool alt, bool shift, bool win, int vk)
        {
            string key = KeyName(vk);
            if (key == null || !(ctrl || alt || win)) return null;
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            parts.Add(key);
            return string.Join("+", parts.ToArray());
        }

        static string KeyName(int vk)
        {
            if ((vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39)) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            if (vk == 0x13) return "Pause";
            return null;
        }

        static uint KeyCode(string name)
        {
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
                return 0;
            }
            if (name.Equals("Pause", StringComparison.OrdinalIgnoreCase)) return 0x13;
            int n;
            if ((name[0] == 'F' || name[0] == 'f') && int.TryParse(name.Substring(1), out n) && n >= 1 && n <= 24)
                return (uint)(0x6F + n);
            return 0;
        }
    }

    static class KeyNames
    {
        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 0x08, "Backspace" }, { 0x09, "Tab" }, { 0x0D, "Enter" }, { 0x13, "Pause" }, { 0x1B, "Esc" }, { 0x20, "Space" },
            { 0x21, "PageUp" }, { 0x22, "PageDown" }, { 0x23, "End" }, { 0x24, "Home" },
            { 0x25, "Left" }, { 0x26, "Up" }, { 0x27, "Right" }, { 0x28, "Down" },
            { 0x2C, "PrintScreen" }, { 0x2D, "Insert" }, { 0x2E, "Delete" }, { 0x5D, "Menu" },
            { 0xAD, "VolumeMute" }, { 0xAE, "VolumeDown" }, { 0xAF, "VolumeUp" },
            { 0xB0, "MediaNext" }, { 0xB1, "MediaPrevious" }, { 0xB2, "MediaStop" }, { 0xB3, "MediaPlayPause" },
        };

        public static string KeyName(int vk)
        {
            if ((vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39)) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
            string name;
            return Names.TryGetValue(vk, out name) ? name : null;
        }

        public static string Format(bool ctrl, bool alt, bool shift, bool win, int vk)
        {
            string key = KeyName(vk);
            if (key == null) return null;
            var parts = new List<string>();
            if (win) parts.Add("Win");
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            parts.Add(key);
            return string.Join("+", parts.ToArray());
        }

        public static bool TryParse(string text, out uint mods, out int vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= Hotkey.ModControl;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= Hotkey.ModAlt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= Hotkey.ModShift;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= Hotkey.ModWin;
                else
                {
                    if (vk != 0) return false;
                    vk = KeyCode(part);
                    if (vk == 0) return false;
                }
            }
            return vk != 0;
        }

        static int KeyCode(string name)
        {
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : 0;
            }
            int n;
            if ((name[0] == 'F' || name[0] == 'f') && int.TryParse(name.Substring(1), out n) && n >= 1 && n <= 24) return 0x6F + n;
            foreach (var pair in Names)
                if (pair.Value.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Key;
            return 0;
        }
    }
}
