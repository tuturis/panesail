namespace WindowPalette.Core;

public readonly record struct PixelPoint(int X, int Y);

/// <summary>A physical-pixel rectangle; right and bottom edges are exclusive.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);

    public int Bottom => checked(Top + Height);

    public bool IsValid => Width > 0 && Height > 0
        && (long)Left + Width <= int.MaxValue
        && (long)Top + Height <= int.MaxValue;

    public bool Contains(PixelPoint point) => IsValid
        && point.X >= Left && point.X < Right
        && point.Y >= Top && point.Y < Bottom;
}

public sealed record ResizeRequest(
    PixelRect SourceWindow,
    PixelRect SourceWorkArea,
    PixelRect DestinationWorkArea,
    PixelPoint ReleasePoint,
    double HorizontalGrabFraction,
    double VerticalGrabOffset);

public static class ResizeGeometry
{
    /// <summary>
    /// Tests whether the released visible frame can fit by size within the work area.
    /// Its current position is deliberately ignored.
    /// </summary>
    public static bool FitsWithinWorkArea(PixelRect releasedVisibleBounds, PixelRect destinationWorkArea)
    {
        if (!releasedVisibleBounds.IsValid)
            throw new ArgumentException("The visible window rectangle must have positive, representable dimensions.", nameof(releasedVisibleBounds));
        if (!destinationWorkArea.IsValid)
            throw new ArgumentException("The destination work-area rectangle must have positive, representable dimensions.", nameof(destinationWorkArea));

        return releasedVisibleBounds.Width <= destinationWorkArea.Width
            && releasedVisibleBounds.Height <= destinationWorkArea.Height;
    }

    /// <summary>
    /// Calculates one visible-frame adjustment. Application minimum sizes and
    /// invisible frame margins are native integration concerns, not geometry rules.
    /// </summary>
    public static PixelRect Calculate(ResizeRequest request, ResizeMode mode)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.SourceWindow.IsValid || !request.SourceWorkArea.IsValid
            || !request.DestinationWorkArea.IsValid)
        {
            throw new ArgumentException("Window and work-area rectangles must have positive, representable dimensions.", nameof(request));
        }

        if (!double.IsFinite(request.HorizontalGrabFraction) || !double.IsFinite(request.VerticalGrabOffset))
        {
            throw new ArgumentException("Grab offsets must be finite.", nameof(request));
        }

        PixelRect source = request.SourceWindow;
        PixelRect destination = request.DestinationWorkArea;
        double desiredWidth;
        double desiredHeight;
        switch (mode)
        {
            case ResizeMode.MatchScreenProportions:
                desiredWidth = (double)source.Width / request.SourceWorkArea.Width * destination.Width;
                desiredHeight = (double)source.Height / request.SourceWorkArea.Height * destination.Height;
                break;
            case ResizeMode.KeepWindowShape:
                double scale = Math.Min(1, Math.Min((double)destination.Width / source.Width, (double)destination.Height / source.Height));
                desiredWidth = source.Width * scale;
                desiredHeight = source.Height * scale;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown resize mode.");
        }

        int width = RoundDimension(desiredWidth, destination.Width);
        int height = RoundDimension(desiredHeight, destination.Height);
        double grabFraction = Math.Clamp(request.HorizontalGrabFraction, 0, 1);
        double grabOffset = Math.Clamp(request.VerticalGrabOffset, 0, height - 1);
        int left = (int)Math.Clamp(
            Math.Round(request.ReleasePoint.X - grabFraction * width, MidpointRounding.AwayFromZero),
            destination.Left,
            destination.Right - width);
        int top = (int)Math.Clamp(
            Math.Round(request.ReleasePoint.Y - grabOffset, MidpointRounding.AwayFromZero),
            destination.Top,
            destination.Bottom - height);
        return new PixelRect(left, top, width, height);
    }

    private static int RoundDimension(double dimension, int maximum) =>
        (int)Math.Round(Math.Clamp(dimension, 1, maximum), MidpointRounding.AwayFromZero);
}
