using YakumoLib.Assets;
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

public sealed record Ng4ModWorkspacePackageResult(string PackagePath, int ChangedModelCount, int ChangedTextureCount, int BatchCount, int GroupCount);

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
        => ExportTga(model, assets, exportRoot);

    public static Ng4ModWorkspaceExportResult ExportPng(AssetEntry model, IEnumerable<AssetEntry> assets, string exportRoot)
        => ExportCore(model, assets, exportRoot, ModelTextureSetService.ExportPng);

    public static Ng4ModWorkspaceExportResult ExportTga(AssetEntry model, IEnumerable<AssetEntry> assets, string exportRoot)
        => ExportCore(model, assets, exportRoot, ModelTextureSetService.ExportTga);

    private static Ng4ModWorkspaceExportResult ExportCore(
        AssetEntry model,
        IEnumerable<AssetEntry> assets,
        string exportRoot,
        Func<AssetEntry, IEnumerable<AssetEntry>, string, ModelTextureSetManifest> exportTextures)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(exportTextures);
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
            string textureDirectory = Path.Combine(temporary, "textures");
            ModelTextureSetManifest manifest = exportTextures(model, assets, textureDirectory) with
            {
                OriginalGlbSha256 = Sha256File(glbPath)
            };
            File.WriteAllText(Path.Combine(textureDirectory, ModelTextureSetService.ManifestFileName), ModelTextureSetService.SerializeManifest(manifest));
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

    public static Ng4ModWorkspacePackageResult PackageWorkspace(string selectedGlbPath, AssetLibrary library, Ng4ModWorkspacePackageRequest request)
        => PackageMany(DiscoverWorkspaceModels(selectedGlbPath), library, request);

    public static IReadOnlyList<Ng4ModWorkspaceModelInput> DiscoverWorkspaceModels(string selectedGlbPath)
    {
        string selected = Path.GetFullPath(selectedGlbPath);
        string selectedDirectory = Path.GetDirectoryName(selected) ?? throw new InvalidDataException("Selected GLB has no directory.");
        string selectedManifestPath = Path.Combine(selectedDirectory, "textures", ModelTextureSetService.ManifestFileName);
        if (!File.Exists(selectedManifestPath)) throw new FileNotFoundException("RONIN texture-set manifest was not found beside the selected GLB.", selectedManifestPath);
        ModelTextureSetManifest selectedManifest = ModelTextureSetService.DeserializeManifest(File.ReadAllText(selectedManifestPath));
        string root = selectedDirectory;
        foreach (string _ in selectedManifest.ModelPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            root = Path.GetDirectoryName(root) ?? throw new InvalidDataException("Workspace path is shallower than its model logical path.");

        var discovered = new List<Ng4ModWorkspaceModelInput>();
        foreach (string manifestPath in Directory.EnumerateFiles(root, ModelTextureSetService.ManifestFileName, SearchOption.AllDirectories))
        {
            if (!string.Equals(Path.GetFileName(Path.GetDirectoryName(manifestPath)), "textures", StringComparison.OrdinalIgnoreCase)) continue;
            ModelTextureSetManifest manifest = ModelTextureSetService.DeserializeManifest(File.ReadAllText(manifestPath));
            string modelDirectory = Path.GetDirectoryName(Path.GetDirectoryName(manifestPath)!)!;
            if (!MapWorkspacePath(root, manifest.ModelPath).Equals(Path.GetFullPath(modelDirectory), StringComparison.OrdinalIgnoreCase)) continue;
            string[] glbs = Directory.GetFiles(modelDirectory, "*.glb", SearchOption.TopDirectoryOnly);
            string chosenGlb;
            if (Path.GetFullPath(modelDirectory).Equals(selectedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                chosenGlb = selected;
            }
            else
            {
                string canonicalName = Path.GetFileName(manifest.ModelPath.Replace('\\', '/')) + ".glb";
                string[] canonical = glbs.Where(path => Path.GetFileName(path).Equals(canonicalName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (canonical.Length == 1) chosenGlb = canonical[0];
                else if (glbs.Length == 1) chosenGlb = glbs[0];
                else throw new InvalidDataException($"Workspace model '{manifest.ModelPath}' has multiple GLBs and no unique canonical file named '{canonicalName}'.");
            }
            discovered.Add(new Ng4ModWorkspaceModelInput(chosenGlb));
        }
        if (discovered.Count == 0) throw new InvalidDataException("No valid model workspaces were found under the selected workspace root.");
        return discovered.OrderBy(item => item.GlbPath, StringComparer.OrdinalIgnoreCase).ToArray();
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
        int changedModelCount = 0;
        int batchCount = 0;
        int groupCount = 0;
        foreach ((string glb, string textureDirectory, ModelTextureSetManifest manifest) in prepared)
        {
            AssetEntry model = library.All.SingleOrDefault(entry =>
                entry.StringAssetID.Equals(manifest.ModelAssetId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Workspace model asset '{manifest.ModelAssetId}' is not present in the configured NG4 library.");
            ModelTextureImportPlan texturePlan = ModelTextureSetService.PlanImport(textureDirectory, library.All, compress: false);
            bool modelChanged = string.IsNullOrWhiteSpace(manifest.OriginalGlbSha256) || !Sha256File(glb).Equals(manifest.OriginalGlbSha256, StringComparison.OrdinalIgnoreCase);
            if (modelChanged)
            {
                SubAssetEntry mdl = FindMdl(model);
                byte[] originalMdl = AssetExtractor.GetSubBlob(mdl, model, mdl.ContentDirectory);
                byte[] replacementMdl = GltfToMdlConverter.Convert(glb, originalMdl);
                MDLFullData verified = MDLParserExtended.Parse(replacementMdl);
                changes.AddRange(ModelLodReplacementService.CreateChanges(model, replacementMdl));
                changedModelCount++;
                batchCount += verified.Batches.Length;
                groupCount += verified.Groups.Length;
            }
            changes.AddRange(texturePlan.Changes);
            changedTextureCount += texturePlan.Changes.Count;
        }

        changes = MergeDuplicateChanges(changes);
        changedTextureCount = changes.Count(change => change.ParentEntry.Type == AssetType.Texture);
        if (changes.Count == 0) throw new InvalidOperationException("No changed models or textures were found in the workspace.");
        string packagePath = Ng4ModPackageExporter.Export(new Ng4ModExportRequest
        {
            ModId = request.ModId, Name = request.Name, Version = request.Version, Author = request.Author,
            Description = request.Description, Dependencies = request.Dependencies,
            CoverPath = request.CoverPath, IconPath = request.IconPath,
            AssetDatabaseSha256 = request.AssetDatabaseSha256, Changes = changes, DestinationPath = request.DestinationPath
        });
        Ng4ModPackageExporter.VerifyPackage(packagePath);
        return new Ng4ModWorkspacePackageResult(packagePath, changedModelCount, changedTextureCount, batchCount, groupCount);
    }

    private static SubAssetEntry FindMdl(AssetEntry model) => model.SubEntries?.FirstOrDefault(s => s.FileName.Equals("modeldata.mdl", StringComparison.OrdinalIgnoreCase))
        ?? model.SubEntries?.FirstOrDefault(s => s.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Model '{model.Path}' has no MDL subfile.");

    private static string Sha256File(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static List<ModifiedAssetEntry> MergeDuplicateChanges(IEnumerable<ModifiedAssetEntry> changes)
    {
        var merged = new List<ModifiedAssetEntry>();
        foreach (IGrouping<string, ModifiedAssetEntry> group in changes.GroupBy(
            change => change.ParentEntry.StringAssetID + "|" + (change.SubEntry?.FileName ?? "<parent>"),
            StringComparer.OrdinalIgnoreCase))
        {
            ModifiedAssetEntry first = group.First();
            if (group.Skip(1).Any(change =>
                change.AllowTextureLayoutChange != first.AllowTextureLayoutChange ||
                !change.ModifiedData.AsSpan().SequenceEqual(first.ModifiedData)))
                throw new InvalidDataException($"Workspace contains conflicting replacements for '{first.DisplayLabel}'.");
            merged.Add(first);
        }
        return merged;
    }
}
