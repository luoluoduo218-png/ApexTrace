using ApexTrace.App;

namespace ApexTrace.Ui.Tests;

public sealed class UiContractTests
{
    [Fact]
    public void NavigationContainsAllNineRequiredPages()
    {
        var pages = Enum.GetValues<PageKind>();
        Assert.Equal(9, pages.Length);
        Assert.Equal([PageKind.Home, PageKind.Realtime, PageKind.MultiLap, PageKind.Replay, PageKind.Compare, PageKind.Corners, PageKind.Setup, PageKind.Library, PageKind.Settings], pages);
    }
}
