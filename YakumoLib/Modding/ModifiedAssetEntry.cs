using YakumoLib.Assets;

namespace YakumoLib.Modding;

/// <summary>
/// Tracks an asset that has been modified by the user during a modding session.
/// </summary>
public sealed record ModifiedAssetEntry
{
    /// <summary>The containing asset entry (package) in the library.</summary>
    public required AssetEntry ParentEntry { get; init; }

    /// <summary>The specific subfile that was modified, or null if the whole package was replaced.</summary>
    public SubAssetEntry? SubEntry { get; init; }

    /// <summary>The raw modified bytes to place in the exported package.</summary>
    public required byte[] ModifiedData { get; init; }

    /// <summary>The original raw bytes, used to validate the replacement against the source asset.</summary>
    public required byte[] OriginalData { get; init; }

    /// <summary>Human-readable label shown in the UI list.</summary>
    public string DisplayLabel => SubEntry is not null
        ? $"{ParentEntry.FileName}/{SubEntry.FileName}"
        : ParentEntry.FileName;

    /// <summary>Whether the game payload should request compression. NG4MOD v2 currently requires this to be false.</summary>
    public bool Compress { get; init; }

    /// <summary>Whether a texture replacement may change its DDS dimensions or mip layout.</summary>
    public bool AllowTextureLayoutChange { get; init; }
}
