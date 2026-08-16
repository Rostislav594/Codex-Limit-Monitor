using System.Windows;
using CodexLimitMonitor.App.Services;

namespace CodexLimitMonitor.App.Tests;

public sealed class WindowPlacementServiceTests
{
    [Fact]
    public void MissingPositionCentersWindow()
    {
        var position = WindowPlacementService.EnsureVisible(null, null, 300, 200, new Rect(0, 0, 1920, 1080));

        Assert.Equal(new Point(810, 440), position);
    }

    [Fact]
    public void OffscreenPositionIsClampedInsideDesktop()
    {
        var position = WindowPlacementService.EnsureVisible(2500, -400, 300, 200, new Rect(-1280, 0, 3200, 1080));

        Assert.Equal(new Point(1620, 0), position);
    }

    [Fact]
    public void ValidMultiMonitorPositionIsPreserved()
    {
        var position = WindowPlacementService.EnsureVisible(-900, 220, 300, 200, new Rect(-1280, 0, 3200, 1080));

        Assert.Equal(new Point(-900, 220), position);
    }

    [Fact]
    public void PositionInGapBetweenDisplaysMovesToNearestWorkingArea()
    {
        Rect[] areas =
        [
            new(0, 0, 1920, 1040),
            new(1920, 300, 1280, 720),
        ];

        var position = WindowPlacementService.EnsureVisible(2100, 40, 300, 200, areas);

        Assert.Equal(new Point(2100, 300), position);
    }

    [Fact]
    public void RemovedDisplayMovesWindowToRemainingPrimaryDisplay()
    {
        Rect[] areas = [new(0, 0, 1920, 1040)];

        var position = WindowPlacementService.EnsureVisible(-1700, 120, 300, 200, areas);

        Assert.Equal(new Point(0, 120), position);
    }
}
