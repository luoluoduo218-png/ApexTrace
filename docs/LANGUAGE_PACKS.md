# ApexTrace language packs

ApexTrace loads optional language-pack plug-ins from the `Languages` folder beside the application executable.
Each plug-in is a .NET assembly that references `ApexTrace.App.dll` and exposes a public, parameterless class implementing `ILanguagePack`:

```csharp
using ApexTrace.App;

public sealed class ExampleLanguagePack : ILanguagePack
{
    public string LanguageCode => "fr-FR";
    public string DisplayName => "Français";
    public string Translate(string sourceText) => sourceText switch
    {
        "设置" => "Paramètres",
        _ => sourceText
    };
}
```

Copy the compiled DLL to `Languages`. It is discovered at the next application start and appears automatically in Settings > Display > Language. A plug-in with the same language code replaces the built-in pack for that code.
