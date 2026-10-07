using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlightenInk.Core
{
    public enum ToolbarLayout
    {
        Horizontal,
        Vertical
    }

    public enum ScreenshotMode
    {
        /// <summary>Drag a region (Snagit-style); a click takes the whole monitor.</summary>
        Region,
        FullScreen
    }

    /// <summary>Everything AlightenInk remembers between runs. Plain data; see <see cref="SettingsStore"/>.</summary>
    public sealed class AppSettings
    {
        public static readonly string[] DefaultSwatches =
        {
            "#F09030", // Pumpkin
            "#F5F5F5", // White
            "#FF4757", // Red
            "#26DE81", // Green
            "#00C8FF", // Cyan
            "#FFD400", // Yellow
            "#1C2A39", // Navy
        };

        public static readonly double[] FadeChoices = { 2, 3, 5, 8 };

        /// <summary>User-added colours, shown after the defaults ("#RRGGBB").</summary>
        public List<string> CustomSwatches { get; set; } = new List<string>();

        /// <summary>Current ink colour ("#RRGGBB").</summary>
        public string Color { get; set; } = "#F09030";

        /// <summary>0 = thin, 1 = medium, 2 = thick.</summary>
        public int WidthIndex { get; set; } = 1;

        public bool FadeEnabled { get; set; }

        /// <summary>Seconds a fading item stays fully visible before it fades out.</summary>
        public double FadeSeconds { get; set; } = 3;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ToolbarLayout Layout { get; set; } = ToolbarLayout.Horizontal;

        /// <summary>Toolbar top-left in physical screen pixels; null = default placement.</summary>
        public double? ToolbarX { get; set; }
        public double? ToolbarY { get; set; }

        public bool Collapsed { get; set; }

        /// <summary>What the screenshot button / Ctrl+Shift+S does (right-click is always full screen).</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ScreenshotMode ScreenshotMode { get; set; } = ScreenshotMode.Region;

        /// <summary>All swatches in display order (defaults then custom).</summary>
        [JsonIgnore]
        public IEnumerable<string> AllSwatches => DefaultSwatches.Concat(CustomSwatches);

        /// <summary>Repairs anything out of range so the UI can trust the values.</summary>
        public AppSettings Normalize()
        {
            CustomSwatches = (CustomSwatches ?? new List<string>())
                .Select(ColorText.Normalize)
                .Where(c => c != null)
                .Select(c => c!)
                .Where(c => !DefaultSwatches.Contains(c, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
            Color = ColorText.Normalize(Color) ?? DefaultSwatches[0];
            if (WidthIndex < 0 || WidthIndex > 2) WidthIndex = 1;
            if (double.IsNaN(FadeSeconds) || FadeSeconds < 0.5 || FadeSeconds > 60) FadeSeconds = 3;
            if (!Enum.IsDefined(typeof(ToolbarLayout), Layout)) Layout = ToolbarLayout.Horizontal;
            if (!Enum.IsDefined(typeof(ScreenshotMode), ScreenshotMode)) ScreenshotMode = ScreenshotMode.Region;
            if (ToolbarX.HasValue && (double.IsNaN(ToolbarX.Value) || double.IsInfinity(ToolbarX.Value))) ToolbarX = null;
            if (ToolbarY.HasValue && (double.IsNaN(ToolbarY.Value) || double.IsInfinity(ToolbarY.Value))) ToolbarY = null;
            return this;
        }
    }

    public static class ColorText
    {
        /// <summary>Returns "#RRGGBB" upper-case for "#rgb", "#rrggbb" or "#aarrggbb" (alpha dropped); null if invalid.</summary>
        public static string? Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
            if (!s.All(Uri.IsHexDigit)) return null;
            if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
            else if (s.Length == 8) s = s.Substring(2);
            else if (s.Length != 6) return null;
            return "#" + s.ToUpperInvariant();
        }

        public static (byte R, byte G, byte B) Parse(string text)
        {
            string n = Normalize(text) ?? throw new FormatException("Not a colour: " + text);
            return (byte.Parse(n.Substring(1, 2), NumberStyles.HexNumber),
                    byte.Parse(n.Substring(3, 2), NumberStyles.HexNumber),
                    byte.Parse(n.Substring(5, 2), NumberStyles.HexNumber));
        }

        public static string Format(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Loads/saves <see cref="AppSettings"/> as JSON. Never throws on load.</summary>
    public sealed class SettingsStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public string Path { get; }

        /// <summary>Set when the last <see cref="Load"/> found an unreadable file (it is backed up as .bad).</summary>
        public bool LastLoadWasCorrupt { get; private set; }

        public SettingsStore(string path) => Path = path;

        public static string DefaultPath =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlightenInk", "settings.json");

        public AppSettings Load()
        {
            LastLoadWasCorrupt = false;
            try
            {
                if (!File.Exists(Path)) return new AppSettings();
                string json = File.ReadAllText(Path);
                var s = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (s == null) throw new JsonException("null document");
                return s.Normalize();
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                LastLoadWasCorrupt = ex is JsonException;
                if (LastLoadWasCorrupt)
                {
                    try { File.Copy(Path, Path + ".bad", true); } catch { /* best effort */ }
                }
                return new AppSettings();
            }
        }

        /// <summary>Atomic write (temp file + replace). Returns false instead of throwing.</summary>
        public bool Save(AppSettings settings)
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
                File.Move(tmp, Path, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
