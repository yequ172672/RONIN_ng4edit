using YakumoLib.Assets;
using YakumoLib.Modding;

/// <summary>Minimal command-line surface for the model texture-set prototype.</summary>
public static class TextureSetCli
{
    public sealed record ExportResult(string ModelPath, string Destination, int TextureCount);
    public sealed record ImportResult(string TextureSetDirectory, string? PatchId, int ChangedCount, int UnchangedCount);

    public static ExportResult Export(string assetsDirectory, string exactModelLogicalPath, string destinationDirectory)
    {
        AssetLibrary library = AssetLibrary.Load(Path.GetFullPath(assetsDirectory));
        AssetEntry model = FindExactModel(library, exactModelLogicalPath);
        ModelTextureSetManifest manifest = ModelTextureSetService.Export(model, library.All, destinationDirectory);
        return new ExportResult(model.Path, Path.GetFullPath(destinationDirectory), manifest.Textures.Count);
    }

    public static ExportResult ExportPng(string assetsDirectory, string exactModelLogicalPath, string destinationDirectory)
    {
        AssetLibrary library = AssetLibrary.Load(Path.GetFullPath(assetsDirectory));
        AssetEntry model = FindExactModel(library, exactModelLogicalPath);
        ModelTextureSetManifest manifest = ModelTextureSetService.ExportPng(model, library.All, destinationDirectory);
        return new ExportResult(model.Path, Path.GetFullPath(destinationDirectory), manifest.Textures.Count);
    }

    public static ImportResult Import(string assetsDirectory, string textureSetDirectory)
    {
        string assets = Path.GetFullPath(assetsDirectory);
        AssetLibrary library = AssetLibrary.Load(assets);
        ModelTextureImportPlan plan = ModelTextureSetService.PlanImport(textureSetDirectory, library.All);
        if (plan.Changes.Count == 0)
            return new ImportResult(Path.GetFullPath(textureSetDirectory), null, 0, plan.UnchangedCount);

        string patchId = PatchGenerator.GeneratePatch(assets, plan.Changes, compressData: false);
        return new ImportResult(Path.GetFullPath(textureSetDirectory), patchId, plan.Changes.Count, plan.UnchangedCount);
    }

    private static AssetEntry FindExactModel(AssetLibrary library, string logicalPath)
    {
        if (string.IsNullOrWhiteSpace(logicalPath))
            throw new ArgumentException("An exact model logical path is required.", nameof(logicalPath));
        AssetEntry[] matches = library.All.Where(entry =>
            entry.Type == AssetType.SkeletalMesh &&
            entry.Path.Equals(logicalPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No skeletal model matched exact logical path '{logicalPath}'."),
            _ => throw new InvalidOperationException($"Exact logical path '{logicalPath}' matched {matches.Length} skeletal models.")
        };
    }
}
