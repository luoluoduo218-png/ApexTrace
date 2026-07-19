namespace ApexTrace.App;

public enum PageKind
{
    Home,
    Realtime,
    MultiLap,
    Replay,
    Compare,
    Setup,
    Library,
    Settings
}

public sealed record NavigationItem(string Glyph, string Title, PageKind Page);
