using YakumoLib.Assets;
using YakumoLib.Blender;
using YakumoLib.Formats;

namespace YakumoLib.Modding;

public sealed record Ng4ModWorkspaceExportResult(string WorkspacePath, string GlbPath, ModelTextureSetManifest TextureManifest);

public sealed record Ng4ModWorkspacePackageRequest(
    string ModId,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<string> Dependencies,
    string DestinationPath,
    string AssetDatabaseSha256 = "",
    string? CoverPath = null,
    string? IconPath = null);

public sealed record Ng4ModWorkspacePackageResult(string PackagePath, int ChangedTextureCount, int BatchCount, int GroupCount);

/// <summary>
/// One editable model workspace to include in a combined NG4MOD package.
/// The GLB must be beside the workspace's <c>textures</c> directory.
/// </summary>
public sealed record Ng4ModWorkspaceModelInput(string GlbPath);

public static class Ng4ModWorkspaceService
{
    public static string MapWorkspacePath(string exportRoot, string logicalPath)
    {
        if (string.IsNullOrWhiteSpace(exportRoot)) throw new ArgumentException("Export root is required.", nameof(exportRoot));
        string normalized = (logicalPath ?? "").Replace('\\', '/');
        string[] parts = normalized.Split('/', StringSplitOptions.None);
        if (parts.Length == 0 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.Contains(':')) || Path.IsPathRooted(normalized))
            throw new InvalidDataException($"NG4 logical path is unsafe: '{logicalPath}'.");
        string root = Path.GetFullPath(exportRoot);
        string mapped = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!mapped.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"NG4 logical path escapes export root: '{logicalPath}'.");
        return mapped;
    }

    public static Ng4ModWorkspaceExportResult Export(AssetEntry model, IEnumerable<AssetEntry> assets, string exportRoot)
    {
        ArgumentNullException.ThrowIfNull(model);
        string destination = MapWorkspacePath(exportRoot, model.Path);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"NG4 model workspace already exists: '{destination}'.");
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string temporary = destination + $".ronin-{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(temporary);
            string stem = Path.GetFileName(destination);
            SubAssetEntry mdl = FindMdl(model);
            byte[] mdlData = AssetExtractor.GetSubBlob(mdl, model, mdl.ContentDirectory);
            string glbPath = Path.Combine(temporary, stem + ".glb");
            MdlToGltfConverter.WriteGlb(MDLParserExtended.Parse(mdlData), glbPath);
            ModelTextureSetManifest manifest = ModelTextureSetService.ExportTga(model, assets, Path.Combine(temporary, "textures"));
            Directory.Move(temporary, destination);
            return new Ng4ModWorkspaceExportResult(destination, Path.Combine(destination, stem + ".glb"), manifest);
        }
        catch
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            throw;
        }
    }

    public static Ng4ModWorkspacePackageResult Package(string glbPath, AssetLibrary library, Ng4ModWorkspacePackageRequest request)
    {
        return PackageMany([new Ng4ModWorkspaceModelInput(glbPath)], library, request);
    }

    /// <summary>
    /// Converts and aggregates multiple model workspaces into one verified package.
    /// Each workspace contributes its model parent replacement and changed texture
    /// parents. Model identity comes from the workspace manifest, never from the
    /// caller-provided filename, so renamed GLBs remain safe to process.
    /// </summary>
    public static Ng4ModWorkspacePackageResult PackageMany(
        IEnumerable<Ng4ModWorkspaceModelInput> inputs,
        AssetLibrary library,
        Ng4ModWorkspacePackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(request);
        Ng4ModWorkspaceModelInput[] workspaces = inputs.ToArray();
        if (workspaces.Length == 0)
            throw new ArgumentException("At least one model workspace is required.", nameof(inputs));

        var changes = new List<ModifiedAssetEntry>();
        var modelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prepared = new List<(string Glb, string TextureDirectory, ModelTextureSetManifest Manifest)>(workspaces.Length);
        foreach (Ng4ModWorkspaceModelInput input in workspaces)
        {
            string glb = Path.GetFullPath(input.GlbPath);
            if (!File.Exists(glb) || !glb.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("GLB file was not found.", glb);
            string textureDirectory = Path.Combine(Path.GetDirectoryName(glb)!, "textures");
            string manifestPath = Path.Combine(textureDirectory, ModelTextureSetService.ManifestFileName);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("RONIN texture-set manifest was not found beside the GLB.", manifestPath);
            ModelTextureSetManifest manifest = ModelTextureSetService.DeserializeManifest(File.ReadAllText(manifestPath));
            if (!modelIds.Add(manifest.ModelAssetId))
                throw new InvalidDataException($"Model workspace '{manifest.ModelAssetId}' was supplied more than once.");
            prepared.Add((glb, textureDirectory, manifest));
        }

        int changedTextureCount = 0;
        int batchCount = 0;
        int groupCount = 0;
        foreach ((string glb, string textureDirectory, ModelTextureSetManifest manifest) in prepared)
        {
            AssetEntry model = library.All.SingleOrDefault(entry =>
                entry.StringAssetID.Equals(manifest.ModelAssetId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Workspace model asset '{manifest.ModelAssetId}' is not present in the configured NG4 library.");
            SubAssetEntry mdl = FindMdl(model);
            byte[] originalMdl = AssetExtractor.GetSubBlob(mdl, model, mdl.ContentDirectory);
            byte[] replacementMdl = GltfToMdlConverter.Convert(glb, originalMdl);
            MDLFullData verified = MDLParserExtended.Parse(replacementMdl);
            ModelTextureImportPlan texturePlan = ModelTextureSetService.PlanImport(textureDirectory, library.All, compress: false);
            changes.Add(new ModifiedAssetEntry
            {
                ParentEntry = model, SubEntry = mdl, ModifiedData = replacementMdl,
                OriginalData = originalMdl, Compress = false
            });
            changes.AddRange(texturePlan.Changes);
            changedTextureCount += texturePlan.Changes.Count;
            batchCount += verified.Batches.Length;
            groupCount += verified.Groups.Length;
        }

        string packagePath = Ng4ModPackageExporter.Export(new Ng4ModExportRequest
        {
            ModId = request.ModId, Name = request.Name, Version = request.Version, Author = request.Author,
            Description = request.Description, Dependencies = request.Dependencies,
            CoverPath = request.CoverPath, IconPath = request.IconPath,
            AssetDatabaseSha256 = request.AssetDatabaseSha256, Changes = changes, DestinationPath = request.DestinationPath
        });
        Ng4ModPackageExporter.VerifyPackage(packagePath);
        return new Ng4ModWorkspacePackageResult(packagePath, changedTextureCount, batchCount, groupCount);
    }

    private static SubAssetEntry FindMdl(AssetEntry model) => model.SubEntries?.FirstOrDefault(s => s.FileName.Equals("modeldata.mdl", StringComparison.OrdinalIgnoreCase))
        ?? model.SubEntries?.FirstOrDefault(s => s.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Model '{model.Path}' has no MDL subfile.");
}
