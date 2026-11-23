using Setu.Core;
using Setu.Core.Screen;
using Setu.Mac;

namespace Setu.Mac.Tests;

public class ScreenCapturePlanTests
{
    private static readonly (double, double, double, double) Union = (0, 0, 1512, 982);

    [Fact]
    public void Screen_UsesUnionRect() =>
        Assert.Equal(["-x", "-t", "png", "-R", "0,0,1512,982", "/d/out.png"],
            ScreenCapturePlan.Args(new ScreenTarget(), Union, null, "/d/out.png"));

    [Fact]
    public void Screen_NegativeOriginFromSecondDisplay() =>
        Assert.Equal(["-x", "-t", "png", "-R", "-1920,0,3432,1080", "/o"],
            ScreenCapturePlan.Args(new ScreenTarget(), (-1920, 0, 3432, 1080), null, "/o"));

    [Fact]
    public void Window_UsesL_AndNoShadow()
    {
        var w = new MacWindow(4711, 200, "TextEdit", "Untitled", 0, 10, 20, 300, 400, true);
        Assert.Equal(["-x", "-t", "png", "-o", "-l", "4711", "/o"],
            ScreenCapturePlan.Args(new WindowTarget(4711), Union, w, "/o"));
    }

    [Fact]
    public void Window_Unknown_ThrowsInputError()
    {
        var ex = Assert.Throws<ToolInputException>(() =>
            ScreenCapturePlan.Args(new WindowTarget(99), Union, null, "/o"));
        Assert.Equal("No window with handle 99; get a current handle from list_windows", ex.Message);
    }

    [Fact]
    public void Region_PassesThrough() =>
        Assert.Equal(["-x", "-t", "png", "-R", "5,-6,70,80", "/o"],
            ScreenCapturePlan.Args(new RegionTarget(5, -6, 70, 80), Union, null, "/o"));

    [Fact]
    public void Scale_Retina_Is2() => Assert.Equal((2.0, (int?)null), ScreenCapturePlan.Scale(3024, 1512, null));

    [Fact]
    public void MaxWidth_Downscales() =>
        Assert.Equal((1000 / 1512.0, (int?)1000), ScreenCapturePlan.Scale(3024, 1512, 1000));

    [Fact]
    public void CapturePlan_MaxWidthLargerThanImage_NoResample() =>
        Assert.Equal((2.0, (int?)null), ScreenCapturePlan.Scale(3024, 1512, 5000));

    [Fact]
    public void MaxWidth_EqualToImage_NoResample() =>
        Assert.Equal((2.0, (int?)null), ScreenCapturePlan.Scale(3024, 1512, 3024));
}
