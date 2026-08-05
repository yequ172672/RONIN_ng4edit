using YakumoLib.Assets;
using YakumoLib.Formats;

namespace YakumoLib.Modding;

/// <summary>Builds the complete model replacement set used by both NG4MOD exporters.</summary>
public static class ModelLodReplacementService
{
    public static IReadOnlyList<ModifiedAssetEntry> CreateChanges(AssetEntry model, byte[] replacementMdl)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(replacementMdl);

        IReadOnlyList<SubAssetEntry> mdlEntries = (model.SubEntries ?? [])
            .Where(sub => sub.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(sub => sub.FileName.Equals("modeldata.mdl", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(sub => sub.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mdlEntries.Count == 0)
            throw new InvalidDataException($"Asset '{model.Path}' contains no MDL subfiles.");

        var changes = mdlEntries.Select(sub => CreateChange(model, sub, replacementMdl)).ToList();
        SubAssetEntry? lodParam = (model.SubEntries ?? []).FirstOrDefault(sub =>
            sub.FileName.Equals("LodParam.bin", StringComparison.OrdinalIgnoreCase));
        if (lodParam is not null)
        {
            byte[] original = AssetExtractor.GetSubBlob(lodParam, model, lodParam.ContentDirectory);
            byte[] modified = Ng4LodParam.DisableChildLods(original);
            changes.Add(CreateChange(model, lodParam, modified, original));
        }
        return changes;
    }

    private static ModifiedAssetEntry CreateChange(AssetEntry model, SubAssetEntry sub, byte[] modified)
    {
        byte[] original = AssetExtractor.GetSubBlob(sub, model, sub.ContentDirectory);
        return CreateChange(model, sub, modified, original);
    }

    private static ModifiedAssetEntry CreateChange(AssetEntry model, SubAssetEntry sub, byte[] modified, byte[] original) => new()
    {
        ParentEntry = model,
        SubEntry = sub,
        ModifiedData = modified,
        OriginalData = original,
        Compress = false
    };
}
