using ApexTrace.App;

namespace ApexTrace.Ui.Tests;

public sealed class UiContractTests
{
    [Fact]
    public void NavigationContainsEightRequiredPagesWithoutCornerAnalysis()
    {
        var pages = Enum.GetValues<PageKind>();
        Assert.Equal(8, pages.Length);
        Assert.Equal([PageKind.Home, PageKind.Realtime, PageKind.MultiLap, PageKind.Replay, PageKind.Compare, PageKind.Setup, PageKind.Library, PageKind.Settings], pages);
    }
}
