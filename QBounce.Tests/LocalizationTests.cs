using Xunit;

namespace QBounce.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("auto", "zh", (int)DisplayLanguage.SimplifiedChinese)]
    [InlineData("auto", "zh-CN", (int)DisplayLanguage.SimplifiedChinese)]
    [InlineData("auto", "zh-Hant", (int)DisplayLanguage.TraditionalChinese)]
    [InlineData("auto", "zh_TW", (int)DisplayLanguage.TraditionalChinese)]
    [InlineData("auto", "tw", (int)DisplayLanguage.TraditionalChinese)]
    [InlineData("auto", "en", (int)DisplayLanguage.English)]
    [InlineData("auto", "ja", (int)DisplayLanguage.English)]
    [InlineData("auto", null, (int)DisplayLanguage.English)]
    [InlineData("zh-TW", "en", (int)DisplayLanguage.TraditionalChinese)]
    [InlineData("en", "zh-CN", (int)DisplayLanguage.English)]
    [InlineData("zh-CN", "en", (int)DisplayLanguage.SimplifiedChinese)]
    public void LanguageSelectionFollowsHostOrManualOverride(string preference, string? host, int expected) =>
        Assert.Equal((DisplayLanguage)expected, Localization.Resolve(preference, host));

    [Fact]
    public void EveryLanguageHasAllMessagesAndValidCountPlaceholders()
    {
        foreach (var language in Enum.GetValues<DisplayLanguage>())
        {
            foreach (var key in Enum.GetValues<TextId>()) Assert.False(string.IsNullOrWhiteSpace(Localization.Get(key, language)));
            var count = Localization.Format(TextId.Counts, language, 17, 23, 2);
            Assert.Contains("17", count); Assert.Contains("23", count); Assert.DoesNotContain("{", count);
            Assert.Contains("Codex", Localization.Get(TextId.Credits, language));
        }
    }

    [Fact]
    public void InvalidOrLegacyConfigurationUsesAutomaticLanguageWithoutChangingBounceSettings()
    {
        var config = new Configuration { UiLanguage = "unknown", Amplitude = .55f, Period = .8f };
        config.Normalize();
        Assert.Equal("auto", config.UiLanguage); Assert.Equal(.55f, config.Amplitude); Assert.Equal(.8f, config.Period);
        config.UiLanguage = " ZH-tw "; config.Normalize(); Assert.Equal("zh-TW", config.UiLanguage);
        Assert.Equal("auto", new Configuration().UiLanguage);
    }

    [Fact]
    public void MusicStatusCanChangeLanguageWithoutRestartingPlayback()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        File.WriteAllBytes(path, []);
        var plays = 0;
        try
        {
            using var service = new AudioService(_ => new Playback(() => plays++), error => throw error);
            service.Start(path, .35f, false);
            Assert.Equal("Looping", service.GetStatus(DisplayLanguage.English));
            Assert.Equal("循環播放", service.GetStatus(DisplayLanguage.TraditionalChinese));
            Assert.Equal(1, plays);
            service.Stop(); Assert.Equal("Stopped", service.GetStatus(DisplayLanguage.English));
        }
        finally { File.Delete(path); }
    }

    private sealed class Playback(Action play) : IAudioPlayback
    {
        public float Volume { set { } }
        public void Play() => play();
        public void Stop() { }
        public void Dispose() { }
    }
}
