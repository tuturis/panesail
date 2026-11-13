namespace WindowPalette.Core;

public enum ResizeMode
{
    MatchScreenProportions,
    KeepWindowShape,
}

public sealed record PaletteSettings
{
    public bool Enabled { get; init; }

    public ResizeMode Mode { get; init; } = ResizeMode.MatchScreenProportions;

    public bool StartWithWindows { get; init; } = true;
}
