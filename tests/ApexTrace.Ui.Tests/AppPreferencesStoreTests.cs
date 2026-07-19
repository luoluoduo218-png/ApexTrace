using ApexTrace.App;

namespace ApexTrace.Ui.Tests;

public sealed class AppPreferencesStoreTests
{
    [Fact]
    public void EnglishLocalizationTranslatesStaticAndDynamicText()
    {
        try
        {
            LocalizationManager.SetLanguage(LocalizationManager.EnglishLanguage);

            Assert.Equal("Settings", LocalizationManager.Translate("设置"));
            Assert.Equal("Lap 12", LocalizationManager.Translate("圈 12"));
            Assert.Equal("Version 1.2.3", LocalizationManager.Translate("版本 1.2.3"));
        }
        finally
        {
            LocalizationManager.SetLanguage(LocalizationManager.ChineseLanguage);
        }
    }

    [Fact]
    public void GermanLocalizationUsesGermanLabels()
    {
        try
        {
            LocalizationManager.SetLanguage(LocalizationManager.GermanLanguage);

            Assert.Equal("Einstellungen", LocalizationManager.Translate("设置"));
            Assert.Equal("Runde 12", LocalizationManager.Translate("圈 12"));
        }
        finally
        {
            LocalizationManager.SetLanguage(LocalizationManager.ChineseLanguage);
        }
    }

    [Fact]
    public void RepeatedLanguageSwitchesAlwaysTranslateFromChineseSource()
    {
        try
        {
            LocalizationManager.SetLanguage(LocalizationManager.ChineseLanguage);
            Assert.Equal("设置", LocalizationManager.Translate("设置"));
            LocalizationManager.SetLanguage(LocalizationManager.EnglishLanguage);
            Assert.Equal("Settings", LocalizationManager.Translate("设置"));
            LocalizationManager.SetLanguage(LocalizationManager.GermanLanguage);
            Assert.Equal("Einstellungen", LocalizationManager.Translate("设置"));
            LocalizationManager.SetLanguage(LocalizationManager.ChineseLanguage);
            Assert.Equal("设置", LocalizationManager.Translate("设置"));
        }
        finally
        {
            LocalizationManager.SetLanguage(LocalizationManager.ChineseLanguage);
        }
    }

    [Fact]
    public void BuiltInLanguagePacksAreRegistered()
    {
        Assert.Contains(LocalizationManager.AvailableLanguagePacks, pack => pack.LanguageCode == "zh-CN");
        Assert.Contains(LocalizationManager.AvailableLanguagePacks, pack => pack.LanguageCode == "en-US");
        Assert.Contains(LocalizationManager.AvailableLanguagePacks, pack => pack.LanguageCode == "de-DE");
    }

    [Fact]
    public void DisplayAndPanelPreferencesRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ApexTrace.Ui.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new AppPreferencesStore(path);
            store.Save(new AppPreferences(true, 0.43, 0.67, @"E:\Games\Le Mans Ultimate", "en-US"));

            var loaded = store.Load();
            Assert.True(loaded.UseAxisPedalDisplay);
            Assert.Equal(0.43, loaded.RealtimeTrackPanelRatio, 3);
            Assert.Equal(0.67, loaded.ReplayTrackPanelRatio, 3);
            Assert.Equal(@"E:\Games\Le Mans Ultimate", loaded.LmuInstallationPath);
            Assert.Equal("en-US", loaded.Language);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void MalformedPreferenceFileFallsBackToClassicDisplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ApexTrace.Ui.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{not-json");

            Assert.False(new AppPreferencesStore(path).Load().UseAxisPedalDisplay);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void LegacyPreferenceFileUsesDefaultPanelRatios()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ApexTrace.Ui.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"useAxisPedalDisplay\":true}");

            var loaded = new AppPreferencesStore(path).Load();

            Assert.True(loaded.UseAxisPedalDisplay);
            Assert.Equal(0.54, loaded.RealtimeTrackPanelRatio, 3);
            Assert.Equal(0.60, loaded.ReplayTrackPanelRatio, 3);
            Assert.Null(loaded.LmuInstallationPath);
            Assert.Equal("zh-CN", loaded.Language);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
