using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Serilog;

namespace ApexTrace.App;

/// <summary>A self-contained language plug-in. External packs may implement this interface in a DLL.</summary>
public interface ILanguagePack
{
    string LanguageCode { get; }
    string DisplayName { get; }
    string Translate(string sourceText);
}

internal sealed class BuiltInLanguagePack(string languageCode, string displayName) : ILanguagePack
{
    public string LanguageCode { get; } = languageCode;
    public string DisplayName { get; } = displayName;

    public string Translate(string sourceText)
    {
        if (LanguageCode == LocalizationManager.ChineseLanguage) return sourceText;
        return LocalizationManager.TranslateBuiltIn(sourceText);
    }
}

public static class LanguagePackRegistry
{
    private static readonly List<ILanguagePack> MutablePacks =
    [
        new BuiltInLanguagePack(LocalizationManager.ChineseLanguage, "中文"),
        new BuiltInLanguagePack(LocalizationManager.EnglishLanguage, "English"),
        new BuiltInLanguagePack(LocalizationManager.GermanLanguage, "Deutsch")
    ];

    static LanguagePackRegistry() => LoadExternalPacks();

    public static IReadOnlyList<ILanguagePack> Packs => MutablePacks;

    public static ILanguagePack Resolve(string? languageCode) => MutablePacks.FirstOrDefault(pack =>
        string.Equals(pack.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase)) ?? MutablePacks[0];

    private static void LoadExternalPacks()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Languages");
        if (!Directory.Exists(directory)) return;

        foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
                foreach (var type in assembly.GetTypes().Where(type => typeof(ILanguagePack).IsAssignableFrom(type)
                    && type is { IsAbstract: false, IsInterface: false } && type.GetConstructor(Type.EmptyTypes) is not null))
                {
                    if (Activator.CreateInstance(type) is not ILanguagePack pack) continue;
                    MutablePacks.RemoveAll(existing => string.Equals(existing.LanguageCode, pack.LanguageCode, StringComparison.OrdinalIgnoreCase));
                    MutablePacks.Add(pack);
                }
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Unable to load language pack {LanguagePackPath}", path);
            }
        }
    }
}
