namespace WindowPalette.Core;

/// <summary>
/// Evidence collected by the native monitor for one gesture. Positive facts start
/// false, so absent or incomplete observations cannot authorize a window change.
/// </summary>
public sealed record GestureEvidence
{
    public bool CaptionHit { get; init; }
    public bool StartedNormalResizable { get; init; }
    public bool MoveStarted { get; init; }
    public bool MoveEnded { get; init; }
    public bool MouseReleased { get; init; }
    public bool IdentityUnchanged { get; init; }
    public bool TopologyUnchanged { get; init; }
    public bool DestinationSafeInterior { get; init; }
    public bool PlacementStable { get; init; }
    public bool Cancelled { get; init; }
    public bool ShiftUsed { get; init; }
    public bool SnapObserved { get; init; }
    public bool SameMonitor { get; init; }
}

public enum GestureDecision
{
    Apply,
    UnrecognizedCaption,
    UnsupportedWindow,
    IncompleteGesture,
    Cancelled,
    ShiftBypass,
    SnapObserved,
    WindowChanged,
    DisplayChanged,
    SameMonitor,
    UnsafeDestination,
    PlacementChanged,
}

public static class GesturePolicy
{
    public static GestureDecision Evaluate(GestureEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Cancelled) return GestureDecision.Cancelled;
        if (evidence.ShiftUsed) return GestureDecision.ShiftBypass;
        if (evidence.SnapObserved) return GestureDecision.SnapObserved;
        if (!evidence.CaptionHit) return GestureDecision.UnrecognizedCaption;
        if (!evidence.StartedNormalResizable) return GestureDecision.UnsupportedWindow;
        if (!evidence.MoveStarted || !evidence.MoveEnded || !evidence.MouseReleased) return GestureDecision.IncompleteGesture;
        if (!evidence.IdentityUnchanged) return GestureDecision.WindowChanged;
        if (!evidence.TopologyUnchanged) return GestureDecision.DisplayChanged;
        if (evidence.SameMonitor) return GestureDecision.SameMonitor;
        if (!evidence.DestinationSafeInterior) return GestureDecision.UnsafeDestination;
        if (!evidence.PlacementStable) return GestureDecision.PlacementChanged;
        return GestureDecision.Apply;
    }
}
