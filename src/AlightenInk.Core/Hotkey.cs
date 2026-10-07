using System;
using System.Collections.Generic;
using System.Text;

namespace AlightenInk.Core
{
    [Flags]
    public enum HotkeyModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
    }

    /// <summary>A global hotkey as RegisterHotKey wants it (modifiers + virtual-key code).</summary>
    public readonly struct Hotkey : IEquatable<Hotkey>
    {
        public Hotkey(HotkeyModifiers modifiers, uint virtualKey)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }

        public HotkeyModifiers Modifiers { get; }
        public uint VirtualKey { get; }

        private const uint VkEscape = 0x1B;

        /// <summary>Parses "Ctrl+Shift+D", "ctrl + alt + F5", "Win+1". Throws <see cref="FormatException"/>.</summary>
        public static Hotkey Parse(string text)
        {
            if (!TryParse(text, out var hk, out string? error)) throw new FormatException(error);
            return hk;
        }

        public static bool TryParse(string? text, out Hotkey hotkey) => TryParse(text, out hotkey, out _);

        public static bool TryParse(string? text, out Hotkey hotkey, out string? error)
        {
            hotkey = default;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "Empty hotkey"; return false; }

            var mods = HotkeyModifiers.None;
            uint? vk = null;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Length == 0) { error = "Empty part in '" + text + "'"; return false; }
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        mods |= HotkeyModifiers.Control; continue;
                    case "SHIFT":
                        mods |= HotkeyModifiers.Shift; continue;
                    case "ALT":
                        mods |= HotkeyModifiers.Alt; continue;
                    case "WIN":
                    case "WINDOWS":
                        mods |= HotkeyModifiers.Win; continue;
                }
                if (vk.HasValue) { error = "More than one key in '" + text + "'"; return false; }
                uint? k = KeyCode(part);
                if (!k.HasValue) { error = "Unknown key '" + part + "'"; return false; }
                vk = k;
            }

            if (!vk.HasValue) { error = "No key in '" + text + "'"; return false; }
            if (mods == HotkeyModifiers.None) { error = "A global hotkey needs a modifier"; return false; }
            if (vk.Value == VkEscape) { error = "Esc is never registered globally"; return false; }
            hotkey = new Hotkey(mods, vk.Value);
            return true;
        }

        private static uint? KeyCode(string part)
        {
            string p = part.ToUpperInvariant();
            if (p.Length == 1)
            {
                char c = p[0];
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return c;
                return null;
            }
            if (p[0] == 'F' && int.TryParse(p.Substring(1), out int f) && f >= 1 && f <= 24) return (uint)(0x70 + f - 1);
            return p switch
            {
                "ESC" or "ESCAPE" => VkEscape,
                "SPACE" => 0x20,
                "TAB" => 0x09,
                "ENTER" or "RETURN" => 0x0D,
                "HOME" => 0x24,
                "END" => 0x23,
                "INSERT" or "INS" => 0x2D,
                "DELETE" or "DEL" => 0x2E,
                "PAGEUP" or "PGUP" => 0x21,
                "PAGEDOWN" or "PGDN" => 0x22,
                _ => null
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append("Ctrl+");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt+");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift+");
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) sb.Append("Win+");
            sb.Append(KeyName(VirtualKey));
            return sb.ToString();
        }

        private static string KeyName(uint vk)
        {
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            return vk switch
            {
                0x20 => "Space",
                0x09 => "Tab",
                0x0D => "Enter",
                0x24 => "Home",
                0x23 => "End",
                0x2D => "Insert",
                0x2E => "Delete",
                0x21 => "PageUp",
                0x22 => "PageDown",
                _ => "0x" + vk.ToString("X2")
            };
        }

        public bool Equals(Hotkey other) => Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
        public override bool Equals(object? obj) => obj is Hotkey h && Equals(h);
        public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
    }

    /// <summary>The app's global commands and their default bindings.</summary>
    public enum HotkeyCommand
    {
        ToggleDraw,
        ToggleFade,
        CopyRegion,
        Screenshot,
        Undo,
        Redo,
        Clear,
        Board
    }

    public static class DefaultHotkeys
    {
        public static readonly IReadOnlyDictionary<HotkeyCommand, string> Bindings = new Dictionary<HotkeyCommand, string>
        {
            [HotkeyCommand.ToggleDraw] = "Ctrl+Shift+D",
            [HotkeyCommand.ToggleFade] = "Ctrl+Shift+F",
            [HotkeyCommand.CopyRegion] = "Ctrl+Shift+C",
            [HotkeyCommand.Screenshot] = "Ctrl+Shift+S",
            [HotkeyCommand.Undo] = "Ctrl+Shift+Z",
            [HotkeyCommand.Redo] = "Ctrl+Shift+Y",
            [HotkeyCommand.Clear] = "Ctrl+Shift+X",
            [HotkeyCommand.Board] = "Ctrl+Shift+W",
        };
    }
}
