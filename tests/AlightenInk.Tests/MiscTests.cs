using System;
using System.Linq;
using AlightenInk.Core;
using Xunit;

namespace AlightenInk.Tests
{
    public class FadeMathTests
    {
        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(2.99, 1.0)]
        [InlineData(3.0, 1.0)]
        [InlineData(3.8, 0.0)]
        [InlineData(10, 0.0)]
        public void Opacity_HoldThenFade(double elapsed, double expected)
        {
            Assert.Equal(expected, FadeMath.Opacity(elapsed, hold: 3), 6);
        }

        [Fact]
        public void Opacity_MidFadeIsHalf_AndMonotonic()
        {
            Assert.Equal(0.5, FadeMath.Opacity(3.4, 3), 6);
            double prev = 1;
            for (double t = 3; t <= 3.8; t += 0.01)
            {
                double o = FadeMath.Opacity(t, 3);
                Assert.InRange(o, 0, 1);
                Assert.True(o <= prev + 1e-12);
                prev = o;
            }
        }

        [Fact]
        public void IsExpired_AtHoldPlusFade()
        {
            Assert.False(FadeMath.IsExpired(3.79, 3));
            Assert.True(FadeMath.IsExpired(3.8, 3));
            Assert.Equal(0.8, FadeMath.FadeOutSeconds);
        }

        [Fact]
        public void ZeroFade_DropsImmediatelyAfterHold()
        {
            Assert.Equal(1, FadeMath.Opacity(2, 2, 0));
            Assert.Equal(0, FadeMath.Opacity(2.0001, 2, 0));
        }

        [Theory]
        [InlineData(2, 3)]
        [InlineData(3, 5)]
        [InlineData(5, 8)]
        [InlineData(8, 2)]
        [InlineData(4, 5)]
        [InlineData(100, 2)]
        public void NextHold_Cycles(double current, double next)
        {
            Assert.Equal(next, FadeMath.NextHold(current, AppSettings.FadeChoices));
        }
    }

    public class HotkeyTests
    {
        [Theory]
        [InlineData("Ctrl+Shift+D", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'D')]
        [InlineData("ctrl + shift + z", HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Z')]
        [InlineData("Alt+1", HotkeyModifiers.Alt, '1')]
        [InlineData("Win+Control+W", HotkeyModifiers.Win | HotkeyModifiers.Control, 'W')]
        public void Parse_Valid(string text, HotkeyModifiers mods, char key)
        {
            var hk = Hotkey.Parse(text);
            Assert.Equal(mods, hk.Modifiers);
            Assert.Equal((uint)key, hk.VirtualKey);
        }

        [Fact]
        public void Parse_FunctionKeys()
        {
            Assert.Equal(0x70u, Hotkey.Parse("Ctrl+F1").VirtualKey);
            Assert.Equal(0x87u, Hotkey.Parse("Ctrl+F24").VirtualKey);
        }

        [Theory]
        [InlineData("")]
        [InlineData("D")]                 // no modifier
        [InlineData("Ctrl+Shift")]        // no key
        [InlineData("Ctrl+D+E")]          // two keys
        [InlineData("Ctrl+Esc")]          // Esc is never global
        [InlineData("Ctrl+Shift+Banana")]
        [InlineData("Ctrl++D")]
        [InlineData("Ctrl+F25")]
        public void Parse_Invalid(string text)
        {
            Assert.False(Hotkey.TryParse(text, out _));
            Assert.Throws<FormatException>(() => Hotkey.Parse(text));
        }

        [Fact]
        public void ToString_RoundTrips()
        {
            foreach (var text in DefaultHotkeys.Bindings.Values)
            {
                var hk = Hotkey.Parse(text);
                Assert.Equal(text, hk.ToString());
                Assert.Equal(hk, Hotkey.Parse(hk.ToString()));
            }
        }

        [Fact]
        public void Defaults_AreAllValidAndDistinct()
        {
            var parsed = DefaultHotkeys.Bindings.Values.Select(Hotkey.Parse).ToList();
            Assert.Equal(8, parsed.Count);
            Assert.Equal(parsed.Count, parsed.Distinct().Count());
            Assert.All(parsed, h => Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, h.Modifiers));
            Assert.Equal(Enum.GetValues(typeof(HotkeyCommand)).Length, DefaultHotkeys.Bindings.Count);
        }
    }

    public class ColorMathTests
    {
        [Theory]
        [InlineData("#F09030")]
        [InlineData("#1C2A39")]
        [InlineData("#FF4757")]
        [InlineData("#26DE81")]
        [InlineData("#000000")]
        [InlineData("#FFFFFF")]
        [InlineData("#808080")]
        public void RgbHsvRoundTrip(string hex)
        {
            var (r, g, b) = ColorText.Parse(hex);
            var (h, s, v) = ColorMath.RgbToHsv(r, g, b);
            var back = ColorMath.HsvToRgb(h, s, v);
            Assert.Equal(hex, ColorText.Format(back.R, back.G, back.B));
        }

        [Fact]
        public void PrimaryHues()
        {
            Assert.Equal(((byte)255, (byte)0, (byte)0), ColorMath.HsvToRgb(0, 1, 1));
            Assert.Equal(((byte)0, (byte)255, (byte)0), ColorMath.HsvToRgb(120, 1, 1));
            Assert.Equal(((byte)0, (byte)0, (byte)255), ColorMath.HsvToRgb(240, 1, 1));
            Assert.Equal(((byte)255, (byte)0, (byte)0), ColorMath.HsvToRgb(360, 1, 1));
        }
    }
}

namespace AlightenInk.Tests
{
    using System.Collections.Generic;
    using AlightenInk.Core;
    using Xunit;

    /// <summary>
    /// Regression: in Text mode a click on the toolbar's pass-through button reached the overlay and
    /// opened a text box. Presses inside the toolbar (or its popups) must be ignored by overlays.
    /// </summary>
    public class InputGuardTests
    {
        private static readonly List<PixelRect> Toolbar = new List<PixelRect>
        {
            new PixelRect(171, 4, 1988, 137),     // toolbar window (incl. shadow margin)
            new PixelRect(1500, 130, 1900, 600),  // open "more" menu popup
        };

        [Theory]
        [InlineData(220, 71, true)]     // pass-through button
        [InlineData(171, 4, true)]      // top-left corner is inside
        [InlineData(1987, 136, true)]   // last pixel inside
        [InlineData(1988, 71, false)]   // right edge is exclusive
        [InlineData(500, 600, false)]   // open canvas
        [InlineData(1700, 400, true)]   // inside the popup
        [InlineData(-3000, 50, false)]  // another monitor
        public void PressOverToolbar_IsBlocked(int x, int y, bool blocked)
        {
            Assert.Equal(blocked, InputGuard.IsOverToolbar(x, y, Toolbar));
        }

        [Fact]
        public void HiddenToolbar_BlocksNothing()
        {
            Assert.False(InputGuard.IsOverToolbar(220, 71, new List<PixelRect>()));
            Assert.False(InputGuard.IsOverToolbar(5, 5, new[] { new PixelRect(10, 10, 10, 10) }));
        }
    }
}
