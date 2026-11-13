using WindowPalette.Core;

namespace WindowPalette.Windows;

internal enum ResizeOperationResult { AlreadyFits, Applied, Rejected }

/// <summary>Applies at most one window operation after the gesture has been validated.</summary>
internal static class WindowResizeOperation
{
    internal static ResizeOperationResult Apply(PixelRect releasedBounds, ResizeRequest request, ResizeMode mode,
        Func<PixelRect, bool> apply, out PixelRect desired)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(apply);
        desired = default;
        if (ResizeGeometry.FitsWithinWorkArea(releasedBounds, request.DestinationWorkArea))
            return ResizeOperationResult.AlreadyFits;

        desired = ResizeGeometry.Calculate(request, mode);
        return apply(desired) ? ResizeOperationResult.Applied : ResizeOperationResult.Rejected;
    }
}
