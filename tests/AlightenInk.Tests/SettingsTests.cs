using System;
using System.IO;
using AlightenInk.Core;
using Xunit;

namespace AlightenInk.Tests
{
    public sealed class SettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "AlightenInkTests_" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(_dir, "sub", "settings.json");

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void RoundTrip_PreservesEverything()
        {
            var store = new SettingsStore(FilePath);
            var s = new AppSettings
            {
                Color = "#00C8FF",
                WidthIndex = 2,
                FadeEnabled = true,
                FadeSeconds = 5,
                Layout = ToolbarLayout.Vertical,
                ToolbarX = -3500.5,
                ToolbarY = 42,
                Collapsed = true,
            };
            s.CustomSwatches.Add("#123456");
            s.CustomSwatches.Add("#abcdef");

            Assert.True(store.Save(s));
            var back = store.Load();

            Assert.False(store.LastLoadWasCorrupt);
            Assert.Equal("#00C8FF", back.Color);
            Assert.Equal(2, back.WidthIndex);
            Assert.True(back.FadeEnabled);
            Assert.Equal(5, back.FadeSeconds);
            Assert.Equal(ToolbarLayout.Vertical, back.Layout);
            Assert.Equal(-3500.5, back.ToolbarX);
            Assert.Equal(42, back.ToolbarY);
            Assert.True(back.Collapsed);
            Assert.Equal(new[] { "#123456", "#ABCDEF" }, back.CustomSwatches);
        }

        [Fact]
        public void NullPosition_RoundTripsAsNull()
        {
            var store = new SettingsStore(FilePath);
            store.Save(new AppSettings());
            var back = store.Load();
            Assert.Null(back.ToolbarX);
            Assert.Null(back.ToolbarY);
        }

        [Fact]
        public void MissingFile_GivesDefaults()
        {
            var store = new SettingsStore(FilePath);
            var s = store.Load();
            Assert.False(store.LastLoadWasCorrupt);
            Assert.Equal("#F09030", s.Color);
            Assert.Equal(1, s.WidthIndex);
            Assert.False(s.FadeEnabled);
            Assert.Equal(3, s.FadeSeconds);
            Assert.Equal(ToolbarLayout.Horizontal, s.Layout);
            Assert.Empty(s.CustomSwatches);
        }

        [Theory]
        [InlineData("{ this is not json")]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("[1,2,3]")]
        [InlineData("{\"layout\":\"Diagonal\"}")]
        public void CorruptFile_GivesDefaultsAndBacksUp(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, content);
            var store = new SettingsStore(FilePath);
            var s = store.Load();
            Assert.True(store.LastLoadWasCorrupt);
            Assert.Equal("#F09030", s.Color);
            Assert.True(File.Exists(FilePath + ".bad"));
        }

        [Fact]
        public void OutOfRangeValues_AreRepaired()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                "{\"color\":\"banana\",\"widthIndex\":9,\"fadeSeconds\":-1,\"customSwatches\":[\"#f09030\",\"nope\",\"#0f0\",\"#00FF00\"],}");
            var s = new SettingsStore(FilePath).Load();
            Assert.Equal("#F09030", s.Color);
            Assert.Equal(1, s.WidthIndex);
            Assert.Equal(3, s.FadeSeconds);
            // Default duplicates and junk dropped, #0f0 expanded and de-duplicated.
            Assert.Equal(new[] { "#00FF00" }, s.CustomSwatches);
        }

        [Fact]
        public void Save_ToUnwritablePath_ReturnsFalse()
        {
            // A directory where the file should be makes the write fail.
            Directory.CreateDirectory(FilePath);
            Assert.False(new SettingsStore(FilePath).Save(new AppSettings()));
        }

        [Theory]
        [InlineData("#f09030", "#F09030")]
        [InlineData("f09030", "#F09030")]
        [InlineData("#abc", "#AABBCC")]
        [InlineData("#80F09030", "#F09030")]
        [InlineData("  #00c8ff ", "#00C8FF")]
        [InlineData("#12345", null)]
        [InlineData("#GGGGGG", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ColorText_Normalize(string? input, string? expected)
        {
            Assert.Equal(expected, ColorText.Normalize(input));
        }

        [Fact]
        public void AllSwatches_DefaultsThenCustom()
        {
            var s = new AppSettings();
            s.CustomSwatches.Add("#010203");
            Assert.Equal(8, System.Linq.Enumerable.Count(s.AllSwatches));
            Assert.Equal("#010203", System.Linq.Enumerable.Last(s.AllSwatches));
        }
    }
}
