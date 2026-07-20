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

    /// <summary>The raw modified bytes to write into the patch.</summary>
    public required byte[] ModifiedData { get; init; }

    /// <summary>The original raw bytes (before modification), used for differential patching.</summary>
    public required byte[] OriginalData { get; init; }

    /// <summary>Human-readable label shown in the UI list.</summary>
    public string DisplayLabel => SubEntry is not null
        ? $"{ParentEntry.FileName}/{SubEntry.FileName}"
        : ParentEntry.FileName;

    /// <summary>Whether the data should be compressed in the patch .dat file.</summary>
    public bool Compress { get; init; }
}
