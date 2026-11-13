using WindowPalette.Core;

namespace WindowPalette.Core.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static readonly PixelRect Landscape = new(0, 0, 2000, 1000);
    private static readonly PixelRect Portrait = new(-1000, 0, 1000, 2000);
    private static readonly PixelRect WideWindow = new(100, 100, 1600, 600);
    private static readonly ResizeRequest Example = new(WideWindow, Landscape, Portrait, new(-500, 300), 0.5, 20);
    private static readonly GestureEvidence FreeDrop = new()
    {
        CaptionHit = true,
        StartedNormalResizable = true,
        MoveStarted = true,
        MoveEnded = true,
        MouseReleased = true,
        IdentityUnchanged = true,
        TopologyUnchanged = true,
        DestinationSafeInterior = true,
        PlacementStable = true,
    };

    public static int Main()
    {
        Run("settings begin disabled with proportional mode and startup preference", () =>
        {
            PaletteSettings settings = new();
            Equal(false, settings.Enabled);
            Equal(ResizeMode.MatchScreenProportions, settings.Mode);
            Equal(true, settings.StartWithWindows);
        });
        Run("released window smaller on both axes fits destination work area", () =>
            Equal(true, ResizeGeometry.FitsWithinWorkArea(new(-900, 100, 999, 1999), Portrait)));
        Run("a window exactly as large as the work area fits", () =>
            Equal(true, ResizeGeometry.FitsWithinWorkArea(new(400, -200, 1000, 2000), Portrait)));
        Run("one pixel too wide does not fit even when height fits", () =>
            Equal(false, ResizeGeometry.FitsWithinWorkArea(new(-900, 100, 1001, 1999), Portrait)));
        Run("one pixel too tall does not fit even when width fits", () =>
            Equal(false, ResizeGeometry.FitsWithinWorkArea(new(-900, 100, 999, 2001), Portrait)));
        Run("a window too large on both axes does not fit", () =>
            Equal(false, ResizeGeometry.FitsWithinWorkArea(new(-900, 100, 1001, 2001), Portrait)));
        Run("fit depends on dimensions, not whether the drop overlaps the work area", () =>
        {
            PixelRect offsetWorkArea = new(-1080, -350, 1080, 1830);
            Equal(true, ResizeGeometry.FitsWithinWorkArea(new(-2000, -1000, 900, 1000), offsetWorkArea));
            Equal(true, ResizeGeometry.FitsWithinWorkArea(new(-1300, 100, 900, 1000), offsetWorkArea));
            Equal(true, ResizeGeometry.FitsWithinWorkArea(new(-1000, 1300, 900, 1000), offsetWorkArea));
        });
        Run("work-area offsets account for reserved screen space", () =>
        {
            PixelRect fullScreen = new(-1080, -400, 1080, 1920);
            PixelRect usableArea = new(-1080, -360, 1080, 1840);
            PixelRect released = new(-1000, -300, 1080, 1841);
            Equal(true, ResizeGeometry.FitsWithinWorkArea(released, fullScreen));
            Equal(false, ResizeGeometry.FitsWithinWorkArea(released, usableArea));
        });
        Run("fit is determined from release dimensions after a DPI transition", () =>
        {
            PixelRect source = new(0, 0, 1200, 1900);
            PixelRect releasedSmaller = new(-900, 100, 900, 1800);
            Equal(false, ResizeGeometry.FitsWithinWorkArea(source, Portrait));
            Equal(true, ResizeGeometry.FitsWithinWorkArea(releasedSmaller, Portrait));
            PixelRect releasedLarger = new(-900, 100, 1001, 1900);
            Equal(false, ResizeGeometry.FitsWithinWorkArea(releasedLarger, Portrait));
        });
        Run("fit rejects invalid window and destination rectangles", () =>
        {
            foreach (PixelRect invalid in new[] { new PixelRect(0, 0, 0, 100), new PixelRect(0, 0, 100, -1), new PixelRect(int.MaxValue, 0, 1, 1), new PixelRect(0, int.MaxValue, 1, 1) })
            {
                Throws<ArgumentException>(() => ResizeGeometry.FitsWithinWorkArea(invalid, Portrait));
                Throws<ArgumentException>(() => ResizeGeometry.FitsWithinWorkArea(WideWindow, invalid));
            }
        });
        Run("landscape to portrait preserves each screen percentage", () =>
            Equal(new PixelRect(-900, 280, 800, 1200), ResizeGeometry.Calculate(Example, ResizeMode.MatchScreenProportions)));
        Run("portrait to landscape restores screen percentages", () =>
            Equal(new PixelRect(200, 130, 1600, 600), ResizeGeometry.Calculate(
                new(new(-900, 280, 800, 1200), Portrait, Landscape, new(1000, 150), 0.5, 20), ResizeMode.MatchScreenProportions)));
        Run("keep shape fits a wide window to a narrow monitor", () =>
            Equal(new PixelRect(-1000, 280, 1000, 375), ResizeGeometry.Calculate(Example, ResizeMode.KeepWindowShape)));
        Run("keep shape fits a tall window to a short monitor", () =>
            Equal(new PixelRect(700, 0, 500, 1000), ResizeGeometry.Calculate(
                new(new(-800, 100, 600, 1200), Portrait, Landscape, new(950, 100), 0.5, 20), ResizeMode.KeepWindowShape)));
        Run("keep shape never enlarges an already fitting window", () =>
            Equal(new PixelRect(750, 280, 500, 300), ResizeGeometry.Calculate(
                new(new(-900, 100, 500, 300), Portrait, Landscape, new(1000, 300), 0.5, 20), ResizeMode.KeepWindowShape)));
        Run("keep shape remains small when moved back", () =>
        {
            PixelRect small = ResizeGeometry.Calculate(Example, ResizeMode.KeepWindowShape);
            PixelRect back = ResizeGeometry.Calculate(new(small, Portrait, Landscape, new(1000, 300), 0.5, 20), ResizeMode.KeepWindowShape);
            Equal(1000, back.Width);
            Equal(375, back.Height);
        });
        Run("proportional mode grows both dimensions on a larger monitor", () =>
        {
            PixelRect result = ResizeGeometry.Calculate(new(new(50, 50, 400, 300), new(0, 0, 1000, 800), new(1000, 0, 2000, 1600), new(2000, 400), 0.5, 20), ResizeMode.MatchScreenProportions);
            Equal(new PixelRect(1600, 380, 800, 600), result);
        });
        Run("proportional mode shrinks both dimensions on a smaller monitor", () =>
            Equal(new PixelRect(100, 130, 800, 300), ResizeGeometry.Calculate(
                new(WideWindow, Landscape, new(0, 0, 1000, 500), new(500, 150), 0.5, 20), ResizeMode.MatchScreenProportions)));
        Run("taskbar offsets and negative desktop coordinates are respected", () =>
            Equal(new PixelRect(-745, 180, 490, 950), ResizeGeometry.Calculate(
                new(new(100, 100, 1000, 500), Landscape, new(-980, 40, 980, 1900), new(-500, 200), 0.5, 20), ResizeMode.MatchScreenProportions)));
        Run("grab fraction preserves an off-center horizontal anchor", () =>
            Equal(new PixelRect(-900, 280, 800, 1200), ResizeGeometry.Calculate(Example with { ReleasePoint = new(-700, 300), HorizontalGrabFraction = 0.25 }, ResizeMode.MatchScreenProportions)));
        Run("left and top drops are clamped inside usable area", () =>
            Equal(new PixelRect(-1000, 0, 800, 1200), ResizeGeometry.Calculate(Example with { ReleasePoint = new(-1000, 1) }, ResizeMode.MatchScreenProportions)));
        Run("right and bottom drops are clamped inside usable area", () =>
            Equal(new PixelRect(-800, 800, 800, 1200), ResizeGeometry.Calculate(Example with { ReleasePoint = new(-1, 1999) }, ResizeMode.MatchScreenProportions)));
        Run("source overflow is capped to destination work area", () =>
            Equal(Portrait, ResizeGeometry.Calculate(Example with { SourceWindow = new(-500, -500, 4000, 3000) }, ResizeMode.MatchScreenProportions)));
        Run("shape mode caps both overflows using one scale", () =>
            Equal(new PixelRect(-1000, 280, 1000, 1000), ResizeGeometry.Calculate(Example with { SourceWindow = new(-500, -500, 4000, 4000) }, ResizeMode.KeepWindowShape)));
        Run("half-pixel dimensions round to the nearest larger integer", () =>
        {
            PixelRect result = ResizeGeometry.Calculate(new(new(0, 0, 1, 1), new(0, 0, 2, 2), new(0, 0, 5, 5), new(2, 2), 0, 0), ResizeMode.MatchScreenProportions);
            Equal(new PixelRect(2, 2, 3, 3), result);
        });
        Run("tiny scaled windows retain at least one pixel", () =>
            Equal(new PixelRect(0, 0, 1, 1), ResizeGeometry.Calculate(new(new(0, 0, 1, 1), Landscape, new(0, 0, 1, 1), new(0, 0), 0.5, 20), ResizeMode.MatchScreenProportions)));
        Run("finite outlying grab offsets are safely clamped", () =>
        {
            Equal(new PixelRect(-1000, 300, 800, 1200), ResizeGeometry.Calculate(Example with { HorizontalGrabFraction = 9, VerticalGrabOffset = -10 }, ResizeMode.MatchScreenProportions));
            Equal(new PixelRect(-800, 0, 800, 1200), ResizeGeometry.Calculate(Example with { HorizontalGrabFraction = -9, VerticalGrabOffset = 9000 }, ResizeMode.MatchScreenProportions));
        });
        Run("dimension arithmetic does not overflow before clamping", () =>
        {
            PixelRect huge = new(0, 0, int.MaxValue, int.MaxValue);
            Equal(huge, ResizeGeometry.Calculate(new(huge, new(0, 0, 1, 1), huge, new(int.MinValue, int.MinValue), 1, 0), ResizeMode.MatchScreenProportions));
        });
        Run("rectangles use exclusive right and bottom edges", () =>
        {
            PixelRect rectangle = new(-100, -50, 100, 50);
            Equal(true, rectangle.Contains(new(-100, -50)));
            Equal(true, rectangle.Contains(new(-1, -1)));
            Equal(false, rectangle.Contains(new(0, -1)));
            Equal(false, rectangle.Contains(new(-1, 0)));
        });
        Run("invalid window and work-area dimensions are rejected", () =>
        {
            foreach (PixelRect invalid in new[] { new PixelRect(0, 0, 0, 100), new PixelRect(0, 0, 100, -1), new PixelRect(int.MaxValue, 0, 1, 1), new PixelRect(0, int.MaxValue, 1, 1) })
            {
                Equal(false, invalid.IsValid);
                Throws<ArgumentException>(() => ResizeGeometry.Calculate(Example with { SourceWindow = invalid }, ResizeMode.MatchScreenProportions));
                Throws<ArgumentException>(() => ResizeGeometry.Calculate(Example with { SourceWorkArea = invalid }, ResizeMode.MatchScreenProportions));
                Throws<ArgumentException>(() => ResizeGeometry.Calculate(Example with { DestinationWorkArea = invalid }, ResizeMode.MatchScreenProportions));
            }
        });
        Run("non-finite offsets are rejected", () =>
        {
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Throws<ArgumentException>(() => ResizeGeometry.Calculate(Example with { HorizontalGrabFraction = invalid }, ResizeMode.MatchScreenProportions));
                Throws<ArgumentException>(() => ResizeGeometry.Calculate(Example with { VerticalGrabOffset = invalid }, ResizeMode.MatchScreenProportions));
            }
        });
        Run("null requests and unknown modes are rejected", () =>
        {
            Throws<ArgumentNullException>(() => ResizeGeometry.Calculate(null!, ResizeMode.MatchScreenProportions));
            Throws<ArgumentOutOfRangeException>(() => ResizeGeometry.Calculate(Example, (ResizeMode)99));
        });
        Run("integer-ratio repeated transfers preserve the original dimensions", () =>
        {
            PixelRect current = WideWindow;
            for (int iteration = 0; iteration < 100; iteration++)
            {
                current = ResizeGeometry.Calculate(new(current, Landscape, Portrait, new(-500, 300), 0.5, 20), ResizeMode.MatchScreenProportions);
                current = ResizeGeometry.Calculate(new(current, Portrait, Landscape, new(1000, 200), 0.5, 20), ResizeMode.MatchScreenProportions);
            }
            Equal(1600, current.Width);
            Equal(600, current.Height);
        });
        Run("fractional repeated transfers settle without progressive rounding drift", () =>
        {
            PixelRect source = new(0, 0, 1920, 1040);
            PixelRect target = new(-1080, -300, 1080, 1870);
            PixelRect current = new(100, 100, 1333, 711);
            for (int iteration = 0; iteration < 100; iteration++)
            {
                current = ResizeGeometry.Calculate(new(current, source, target, new(-540, 100), 0.5, 20), ResizeMode.MatchScreenProportions);
                current = ResizeGeometry.Calculate(new(current, target, source, new(960, 100), 0.5, 20), ResizeMode.MatchScreenProportions);
                InRange(current.Width, 1332, 1334);
                InRange(current.Height, 710, 712);
            }
        });
        Run("size and placement invariants hold across varied monitor arrangements", VerifyGeometryGrid);

        Run("complete free drop is accepted", () => Equal(GestureDecision.Apply, GesturePolicy.Evaluate(FreeDrop)));
        Run("missing caption evidence rejects resize-border and custom-client gestures", () => Equal(GestureDecision.UnrecognizedCaption, GesturePolicy.Evaluate(FreeDrop with { CaptionHit = false })));
        Run("unsupported initial window state is rejected", () => Equal(GestureDecision.UnsupportedWindow, GesturePolicy.Evaluate(FreeDrop with { StartedNormalResizable = false })));
        Run("missing movement start is rejected", () => Equal(GestureDecision.IncompleteGesture, GesturePolicy.Evaluate(FreeDrop with { MoveStarted = false })));
        Run("missing movement end is rejected", () => Equal(GestureDecision.IncompleteGesture, GesturePolicy.Evaluate(FreeDrop with { MoveEnded = false })));
        Run("movement completion before mouse release cannot apply", () => Equal(GestureDecision.IncompleteGesture, GesturePolicy.Evaluate(FreeDrop with { MouseReleased = false })));
        Run("Escape cancellation wins over completion", () => Equal(GestureDecision.Cancelled, GesturePolicy.Evaluate(FreeDrop with { Cancelled = true })));
        Run("Shift used anywhere in a completed gesture bypasses resizing", () => Equal(GestureDecision.ShiftBypass, GesturePolicy.Evaluate(FreeDrop with { ShiftUsed = true })));
        Run("observed snap preview vetoes a later free-looking drop", () => Equal(GestureDecision.SnapObserved, GesturePolicy.Evaluate(FreeDrop with { SnapObserved = true })));
        Run("destroyed or replaced window identity is rejected", () => Equal(GestureDecision.WindowChanged, GesturePolicy.Evaluate(FreeDrop with { IdentityUnchanged = false })));
        Run("changed display configuration is rejected", () => Equal(GestureDecision.DisplayChanged, GesturePolicy.Evaluate(FreeDrop with { TopologyUnchanged = false })));
        Run("returning to the original monitor is a no-op", () => Equal(GestureDecision.SameMonitor, GesturePolicy.Evaluate(FreeDrop with { SameMonitor = true })));
        Run("snap-edge release is rejected", () => Equal(GestureDecision.UnsafeDestination, GesturePolicy.Evaluate(FreeDrop with { DestinationSafeInterior = false })));
        Run("competing placement after release is rejected", () => Equal(GestureDecision.PlacementChanged, GesturePolicy.Evaluate(FreeDrop with { PlacementStable = false })));
        Run("absent evidence fails closed", () => Equal(GestureDecision.UnrecognizedCaption, GesturePolicy.Evaluate(new())));
        Run("null evidence is rejected", () => Throws<ArgumentNullException>(() => GesturePolicy.Evaluate(null!)));

        Console.WriteLine($"Core checks: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void VerifyGeometryGrid()
    {
        PixelRect[] destinations = [new(0, 0, 1920, 1040), new(-1080, -400, 1080, 1870), new(1920, 80, 3840, 2080), new(-1440, 40, 1440, 860)];
        int observations = 0;
        foreach (PixelRect destination in destinations)
        foreach (ResizeMode mode in Enum.GetValues<ResizeMode>())
        foreach (int width in new[] { 1, 333, 1000, 1601, 2500 })
        foreach (int height in new[] { 1, 187, 600, 1001, 2500 })
        foreach (double fraction in new[] { 0.0, 0.25, 0.5, 1.0 })
        {
            PixelRect window = new(100, 100, width, height);
            PixelRect result = ResizeGeometry.Calculate(new(window, Landscape, destination,
                new(destination.Left + destination.Width / 2, destination.Bottom - 1), fraction, 20), mode);
            Equal(true, result.IsValid);
            Equal(true, result.Left >= destination.Left && result.Top >= destination.Top
                && result.Right <= destination.Right && result.Bottom <= destination.Bottom);
            if (mode == ResizeMode.KeepWindowShape)
            {
                Equal(true, result.Width <= width && result.Height <= height);
                // Each dimension rounds independently to an integer pixel. The
                // cross-product error stays inside the resulting half-pixel bound.
                double error = Math.Abs((double)result.Width * height - (double)result.Height * width);
                Equal(true, error <= width + height);
            }
            observations++;
        }
        Equal(800, observations);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}: {exception.Message}");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
        }
    }

    private static void InRange(int actual, int minimum, int maximum)
    {
        if (actual < minimum || actual > maximum)
        {
            throw new InvalidOperationException($"Expected {minimum}..{maximum}; got {actual}.");
        }
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
