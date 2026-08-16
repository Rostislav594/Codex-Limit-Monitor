using System.Windows;

namespace CodexLimitMonitor.App.Services;

internal static class WindowPlacementService
{
    public static Point EnsureVisible(
        double? requestedLeft,
        double? requestedTop,
        double windowWidth,
        double windowHeight,
        Rect desktopBounds)
        => EnsureVisible(requestedLeft, requestedTop, windowWidth, windowHeight, [desktopBounds]);

    public static Point EnsureVisible(
        double? requestedLeft,
        double? requestedTop,
        double windowWidth,
        double windowHeight,
        IReadOnlyList<Rect> workingAreas)
    {
        var areas = workingAreas
            .Where(area => !area.IsEmpty && area.Width > 0 && area.Height > 0)
            .ToArray();
        if (areas.Length == 0)
        {
            return new Point(0, 0);
        }

        if (requestedLeft is not { } left || requestedTop is not { } top ||
            !double.IsFinite(left) || !double.IsFinite(top))
        {
            return Center(windowWidth, windowHeight, areas[0]);
        }

        var requested = new Rect(left, top, Math.Max(1, windowWidth), Math.Max(1, windowHeight));
        var target = areas
            .OrderByDescending(area => IntersectionArea(requested, area))
            .ThenBy(area => DistanceSquared(requested, area))
            .First();
        var maximumLeft = Math.Max(target.Left, target.Right - windowWidth);
        var maximumTop = Math.Max(target.Top, target.Bottom - windowHeight);
        return new Point(
            Math.Clamp(left, target.Left, maximumLeft),
            Math.Clamp(top, target.Top, maximumTop));
    }

    private static double IntersectionArea(Rect first, Rect second)
    {
        var intersection = Rect.Intersect(first, second);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }

    private static double DistanceSquared(Rect first, Rect second)
    {
        var dx = first.Left + (first.Width / 2) - (second.Left + (second.Width / 2));
        var dy = first.Top + (first.Height / 2) - (second.Top + (second.Height / 2));
        return (dx * dx) + (dy * dy);
    }

    private static Point Center(double width, double height, Rect bounds) =>
        new(
            bounds.Left + Math.Max(0, (bounds.Width - width) / 2),
            bounds.Top + Math.Max(0, (bounds.Height - height) / 2));
}
