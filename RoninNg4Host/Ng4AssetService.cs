using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using YakumoLib;
using YakumoLib.Assets;
using YakumoLib.Blender;
using YakumoLib.Formats;
using YakumoLib.Modding;

namespace RoninNg4Host;

public sealed record AssetTreeItem(string Id, string Path, string FileName, string Type, bool HasModelDataMdl);
public sealed record Ng4BrowserEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("has_preview")] bool HasPreview,
    [property: JsonPropertyName("can_import")] bool CanImport);
public sealed record Ng4BrowserTree(
    [property: JsonPropertyName("protocol_version")] int ProtocolVersion,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("children")] IReadOnlyList<Ng4BrowserEntry> Children);
public sealed record ConversionResult(byte[] Data, int BatchCount, int GroupCount);
public sealed record PreviewTextureReference(
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("name")] string Name);
public sealed record PreviewUnknownTextureReference(
    [property: JsonPropertyName("parameter")] string Parameter,
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("name")] string Name);
public sealed record PreviewMaterial(
    [property: JsonPropertyName("slot")] int Slot,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("base_color")] PreviewTextureReference? BaseColor,
    [property: JsonPropertyName("mro")] PreviewTextureReference? Mro,
    [property: JsonPropertyName("normal")] PreviewTextureReference? Normal,
    [property: JsonPropertyName("unknown_textures")] IReadOnlyList<PreviewUnknownTextureReference> UnknownTextures);
public sealed record PreviewMaterialResponse(
    [property: JsonPropertyName("protocol_version")] int ProtocolVersion,
    [property: JsonPropertyName("materials")] IReadOnlyList<PreviewMaterial> Materials);

public sealed class TemporaryTextureArchive : IDisposable
{
    private string? _path;

    private TemporaryTextureArchive(string path) => _path = path;

    public string Path => _path ?? throw new ObjectDisposedException(nameof(TemporaryTextureArchive));

    public static TemporaryTextureArchive CreateFromDirectory(string sourceDirectory)
    {
        string archivePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ronin-textures-{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(sourceDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return new TemporaryTextureArchive(archivePath);
        }
        catch
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            throw;
        }
    }

    public void Dispose()
    {
        string? path = Interlocked.Exchange(ref _path, null);
        if (path is not null && File.Exists(path)) File.Delete(path);
    }
}

public sealed class Ng4AssetService(HostState state)
{
    private readonly SemaphoreSlim _configureGate = new(1, 1);

    public async Task<int> ConfigureAsync(string assetsDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetsDirectory) || !Path.IsPathFullyQualified(assetsDirectory))
            throw new ArgumentException("Assets directory must be an absolute path.", nameof(assetsDirectory));
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetsDirectory));
        if (!Directory.Exists(fullPath) || !File.Exists(Path.Combine(fullPath, "AssetDatabase.dat")))
            throw new DirectoryNotFoundException($"NG4 Assets directory is invalid: '{fullPath}'.");

        await _configureGate.WaitAsync(cancellationToken);
        try
        {
            state.SetLoading(fullPath);
            AssetLibrary library = await Task.Run(() => AssetLibrary.Load(fullPath), cancellationToken);
            state.SetReady(fullPath, library);
            return library.Count;
        }
        catch (Exception exception)
        {
            state.SetError(exception.Message);
            throw;
        }
        finally { _configureGate.Release(); }
    }

    public IReadOnlyList<AssetTreeItem> GetTree(string? prefix)
    {
        AssetLibrary library = RequireLibrary();
        string normalizedPrefix = (prefix ?? string.Empty).Replace('\\', '/').Trim('/');
        return library.All
            .Where(IsModel)
            .Where(entry => normalizedPrefix.Length == 0 || entry.Path.Replace('\\', '/').StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.StringAssetID, StringComparer.Ordinal)
            .Select(entry => new AssetTreeItem(entry.StringAssetID, entry.Path, entry.FileName, entry.Type.ToString(), FindModelSubEntry(entry) is not null))
            .ToArray();
    }

    public Ng4BrowserTree GetBrowserTree(string? path)
    {
        string canonicalPath = NormalizeBrowserPath(path);
        string[] currentSegments = canonicalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var folders = new Dictionary<string, Ng4BrowserEntry>(StringComparer.OrdinalIgnoreCase);
        var assets = new List<Ng4BrowserEntry>();

        foreach (AssetEntry entry in RequireLibrary().All.Where(IsBrowserAsset))
        {
            string[] assetSegments = entry.Path.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (assetSegments.Length <= currentSegments.Length || !HasPrefix(assetSegments, currentSegments)) continue;

            int remaining = assetSegments.Length - currentSegments.Length;
            string childName = assetSegments[currentSegments.Length];
            if (remaining > 1)
            {
                string folderPath = "/" + string.Join('/', assetSegments.Take(currentSegments.Length + 1));
                var folder = new Ng4BrowserEntry(folderPath, folderPath, childName, "Folder", false, false);
                if (!folders.TryGetValue(childName, out Ng4BrowserEntry? existing) ||
                    string.CompareOrdinal(folder.Path, existing.Path) < 0)
                    folders[childName] = folder;
                continue;
            }

            bool isModel = IsModel(entry);
            bool hasModelPayload = FindModelSubEntry(entry) is not null;
            string assetPath = "/" + string.Join('/', assetSegments);
            assets.Add(new Ng4BrowserEntry(entry.StringAssetID, assetPath, childName, entry.Type.ToString(), isModel && hasModelPayload, isModel && hasModelPayload));
        }

        Ng4BrowserEntry[] children = folders.Values
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .Concat(assets
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal))
            .ToArray();
        return new Ng4BrowserTree(1, canonicalPath, children);
    }

    public byte[] BuildRngp(string id)
    {
        MDLFullData model = ReadModel(ResolveModel(id));
        var vertices = new List<RngpPreviewVertex>();
        var indices = new List<uint>();
        var sections = new List<RngpPreviewSection>();

        for (int batchIndex = 0; batchIndex < model.Batches.Length; batchIndex++)
        {
            MDLBatch batch = model.Batches[batchIndex];
            if (batch.vertexGroupID >= model.Groups.Length)
                throw new InvalidDataException($"Batch {batchIndex} references missing vertex group {batch.vertexGroupID}.");
            VertexGroup group = model.Groups[batch.vertexGroupID];
            ReadOnlySpan<ushort> sourceIndices = model.GetBatchIndices(batchIndex);
            uint firstIndex = checked((uint)indices.Count);
            var localToPreview = new Dictionary<ushort, uint>();
            for (int triangle = 0; triangle < sourceIndices.Length; triangle += 3)
            {
                indices.Add(GetPreviewIndex(sourceIndices[triangle]));
                indices.Add(GetPreviewIndex(sourceIndices[triangle + 2]));
                indices.Add(GetPreviewIndex(sourceIndices[triangle + 1]));
            }
            sections.Add(new RngpPreviewSection(batch.materialID, firstIndex, checked((uint)sourceIndices.Length / 3)));

            uint GetPreviewIndex(ushort sourceIndex)
            {
                if (sourceIndex >= group.Positions.Length)
                    throw new InvalidDataException($"Batch {batchIndex} references missing vertex {sourceIndex}.");
                if (localToPreview.TryGetValue(sourceIndex, out uint existing)) return existing;
                Vector2 uv = group.UVLayers.Length > 0 && sourceIndex < group.UVLayers[0].Length
                    ? group.UVLayers[0][sourceIndex]
                    : Vector2.Zero;
                uint created = checked((uint)vertices.Count);
                localToPreview.Add(sourceIndex, created);
                vertices.Add(new RngpPreviewVertex(MdlGltfConversion.ToGltfPosition(group.Positions[sourceIndex]), uv));
                return created;
            }
        }

        if (vertices.Count == 0) throw new InvalidDataException("Model contains no previewable vertices.");
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        foreach (RngpPreviewVertex vertex in vertices)
        {
            minimum = Vector3.Min(minimum, vertex.Position);
            maximum = Vector3.Max(maximum, vertex.Position);
        }
        return RngpPreviewSerializer.Serialize(new RngpPreviewPayload(0, vertices, indices, new RngpBounds(minimum, maximum), sections));
    }

    public byte[] BuildGlb(string id)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"ronin-{Guid.NewGuid():N}.glb");
        try
        {
            MdlToGltfConverter.WriteGlb(ReadModel(ResolveModel(id)), tempPath);
            return File.ReadAllBytes(tempPath);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    public byte[] GetTemplateMdl(string id) => ReadModelBytes(ResolveModel(id));

    public PreviewMaterialResponse GetPreviewMaterials(string id)
    {
        IReadOnlyList<PreviewMaterial> materials = Ng4MaterialResolver.Resolve(ResolveModel(id), RequireLibrary().All)
            .Select(material => new PreviewMaterial(material.Slot, material.Name,
                ToTextureReference(material.BaseColorTexture),
                ToTextureReference(material.MroTexture),
                ToTextureReference(material.NormalTexture),
                material.UnknownTextures.Select(texture =>
                {
                    AssetEntry asset = RequireLibrary().GetByUUID(texture.AssetId)
                        ?? throw new InvalidDataException($"Texture '{texture.AssetId.GetString()}' was not found.");
                    return new PreviewUnknownTextureReference(texture.Parameter, asset.StringAssetID, asset.FileName);
                }).ToArray()))
            .ToArray();
        return new PreviewMaterialResponse(1, materials);
    }

    private static PreviewTextureReference? ToTextureReference(AssetEntry? texture) =>
        texture is null ? null : new PreviewTextureReference(texture.StringAssetID, texture.FileName);

    public TexturePreviewRgba GetRawTexture(string id, int maxSize)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Texture asset id is required.", nameof(id));
        if (!UUIDParser.TryParse(id, out UUID textureId)) throw new ArgumentException("Texture asset id must be a UUID.", nameof(id));
        AssetEntry texture = RequireLibrary().GetByUUID(textureId)
            ?? throw new KeyNotFoundException($"Texture asset '{id}' was not found.");
        if (texture.Type != AssetType.Texture) throw new ArgumentException($"Asset '{id}' is not a texture.", nameof(id));
        return TexturePreviewDecoder.Decode(texture, maxSize);
    }

    public async Task<ConversionResult> ConvertGlbAsync(string id, Stream glb, CancellationToken cancellationToken)
    {
        AssetEntry asset = ResolveModel(id);
        string tempPath = Path.Combine(Path.GetTempPath(), $"ronin-{Guid.NewGuid():N}.glb");
        try
        {
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
                await glb.CopyToAsync(output, cancellationToken);
            byte[] header = new byte[4];
            await using (var input = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4, useAsync: true))
                if (await input.ReadAsync(header, cancellationToken) != 4 || !header.AsSpan().SequenceEqual("glTF"u8))
                    throw new InvalidDataException("Uploaded file is not a binary glTF file.");
            byte[] replacement = GltfToMdlConverter.Convert(tempPath, ReadModelBytes(asset));
            MDLFullData verified = MDLParserExtended.Parse(replacement);
            return new ConversionResult(replacement, verified.Batches.Length, verified.Groups.Length);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    public TemporaryTextureArchive ExportTextureSetZip(string id, string format)
    {
        AssetEntry asset = ResolveModel(id);
        AssetLibrary library = RequireLibrary();
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"ronin-textures-{Guid.NewGuid():N}");
        try
        {
            if (format.Equals("png", StringComparison.OrdinalIgnoreCase))
                ModelTextureSetService.ExportPng(asset, library.All, tempDirectory);
            else if (format.Equals("tga", StringComparison.OrdinalIgnoreCase))
                ModelTextureSetService.ExportTga(asset, library.All, tempDirectory);
            else
                throw new ArgumentException("Texture format must be 'png' or 'tga'.", nameof(format));

            return TemporaryTextureArchive.CreateFromDirectory(tempDirectory);
        }
        finally { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true); }
    }

    public async Task<TemporaryNg4ModPackage> ExportNg4ModAsync(
        string id, Stream glb, Stream textureSetZip, Ng4ModUploadMetadata metadata,
        Stream? icon, string? iconFileName, CancellationToken cancellationToken)
    {
        AssetEntry model = ResolveModel(id);
        AssetLibrary library = RequireLibrary();
        RequireModelSubEntry(model);
        Ng4ModUploadWorkspace workspace = Ng4ModUploadWorkspace.Create();
        try
        {
            await workspace.CopyGlbAsync(glb, 128L * 1024 * 1024, cancellationToken);
            workspace.ExtractTextureSet(textureSetZip, 256L * 1024 * 1024, 1024L * 1024 * 1024);
            string? iconPath = icon is null ? null
                : await workspace.CopyIconAsync(icon, iconFileName ?? "cover.png", 8L * 1024 * 1024, cancellationToken);

            byte[] originalMdl = ReadModelBytes(model);
            byte[] replacementMdl = GltfToMdlConverter.Convert(workspace.GlbPath, originalMdl);
            MDLFullData verified = MDLParserExtended.Parse(replacementMdl);
            var changes = CreateModelLodChanges(model, replacementMdl).ToList();
            ModelTextureImportPlan texturePlan = ModelTextureSetService.PlanImport(workspace.TextureSetDirectory, library.All);
            changes.AddRange(texturePlan.Changes);

            string assetsDirectory = state.Snapshot().AssetsDirectory
                ?? throw new InvalidOperationException("Host is not configured with an NG4 Assets directory.");
            string databaseHash;
            using (FileStream database = File.OpenRead(Path.Combine(assetsDirectory, "AssetDatabase.dat")))
                databaseHash = Convert.ToHexString(SHA256.HashData(database)).ToLowerInvariant();

            Ng4ModPackageExporter.Export(new Ng4ModExportRequest
            {
                ModId = metadata.ModId, Name = metadata.Name, Version = metadata.Version,
                Author = metadata.Author, Description = metadata.Description, Dependencies = metadata.Dependencies,
                CoverPath = iconPath, AssetDatabaseSha256 = databaseHash, Changes = changes,
                DestinationPath = workspace.PackagePath
            });
            Ng4ModPackageExporter.VerifyPackage(workspace.PackagePath);
            return new TemporaryNg4ModPackage(workspace, verified.Batches.Length, verified.Groups.Length,
                texturePlan.Changes.Count, GetSafePackageFileName(metadata.ModId));
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    public async Task<TemporaryNg4ModBatchPackage> ExportNg4ModBatchAsync(
        IReadOnlyList<(string AssetId, Stream Glb, Stream TextureSet)> uploads,
        Ng4ModUploadMetadata metadata, Stream? icon, string? iconFileName, CancellationToken cancellationToken)
    {
        if (uploads.Count == 0 || uploads.Count > 64) throw new InvalidDataException("NG4MOD batch must contain 1 to 64 models.");
        AssetLibrary library = RequireLibrary();
        var workspaces = new List<Ng4ModUploadWorkspace>();
        try
        {
            var changes = new Dictionary<string, ModifiedAssetEntry>(StringComparer.OrdinalIgnoreCase);
            var textureChangeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int batchCount = 0;
            int groupCount = 0;
            void AddChange(string key, ModifiedAssetEntry change)
            {
                if (!changes.TryGetValue(key, out ModifiedAssetEntry? existing))
                {
                    changes.Add(key, change);
                    return;
                }
                if (existing.ModifiedData.AsSpan().SequenceEqual(change.ModifiedData)) return;
                throw new InvalidDataException($"Selected models produce conflicting replacements for '{key}'.");
            }
            foreach ((string assetId, Stream glb, Stream textureSet) in uploads)
            {
                AssetEntry model = ResolveModel(assetId);
                RequireModelSubEntry(model);
                Ng4ModUploadWorkspace workspace = Ng4ModUploadWorkspace.Create();
                workspaces.Add(workspace);
                await workspace.CopyGlbAsync(glb, 128L * 1024 * 1024, cancellationToken);
                workspace.ExtractTextureSet(textureSet, 256L * 1024 * 1024, 1024L * 1024 * 1024);
                byte[] originalMdl = ReadModelBytes(model);
                byte[] replacementMdl = GltfToMdlConverter.Convert(workspace.GlbPath, originalMdl);
                MDLFullData verified = MDLParserExtended.Parse(replacementMdl);
                batchCount += verified.Batches.Length;
                groupCount += verified.Groups.Length;
                foreach (ModifiedAssetEntry modelChange in CreateModelLodChanges(model, replacementMdl))
                    AddChange($"{model.StringAssetID}/{modelChange.SubEntry!.FileName}", modelChange);
                ModelTextureImportPlan texturePlan = ModelTextureSetService.PlanImport(workspace.TextureSetDirectory, library.All);
                foreach (ModifiedAssetEntry change in texturePlan.Changes)
                {
                    string key = $"{change.ParentEntry.StringAssetID}/{change.SubEntry?.FileName ?? ""}";
                    AddChange(key, change);
                    textureChangeKeys.Add(key);
                }
            }
            string assetsDirectory = state.Snapshot().AssetsDirectory ?? throw new InvalidOperationException("Host is not configured with an NG4 Assets directory.");
            string databaseHash;
            using (FileStream database = File.OpenRead(Path.Combine(assetsDirectory, "AssetDatabase.dat")))
                databaseHash = Convert.ToHexString(SHA256.HashData(database)).ToLowerInvariant();
            string? iconPath = null;
            if (icon is not null)
            {
                iconPath = await workspaces[0].CopyIconAsync(icon, iconFileName ?? "cover.png", 8L * 1024 * 1024, cancellationToken);
            }
            string packagePath = workspaces[0].PackagePath;
            Ng4ModPackageExporter.Export(new Ng4ModExportRequest
            {
                ModId = metadata.ModId, Name = metadata.Name, Version = metadata.Version,
                Author = metadata.Author, Description = metadata.Description, Dependencies = metadata.Dependencies,
                CoverPath = iconPath, AssetDatabaseSha256 = databaseHash, Changes = changes.Values.ToArray(),
                DestinationPath = packagePath
            });
            Ng4ModPackageExporter.VerifyPackage(packagePath);
            return new TemporaryNg4ModBatchPackage(workspaces, packagePath, batchCount, groupCount, textureChangeKeys.Count, GetSafePackageFileName(metadata.ModId));
        }
        catch
        {
            foreach (Ng4ModUploadWorkspace workspace in workspaces) workspace.Dispose();
            throw;
        }
    }

    private AssetLibrary RequireLibrary() => state.Snapshot().Library
        ?? throw new InvalidOperationException("Host is not configured with an NG4 Assets directory.");

    private static string NormalizeBrowserPath(string? path)
    {
        string normalized = string.IsNullOrWhiteSpace(path) ? "/Assets" : "/" + path.Replace('\\', '/').Trim('/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("Browser path must be rooted at '/Assets' and cannot contain traversal segments.", nameof(path));
        return "/Assets" + (segments.Length > 1 ? "/" + string.Join('/', segments.Skip(1)) : string.Empty);
    }

    private static bool HasPrefix(IReadOnlyList<string> value, IReadOnlyList<string> prefix)
    {
        for (int index = 0; index < prefix.Count; index++)
            if (!value[index].Equals(prefix[index], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private AssetEntry ResolveModel(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Asset id is required.", nameof(id));
        AssetEntry[] matches = RequireLibrary().All.Where(entry => IsModel(entry) &&
            (entry.StringAssetID.Equals(id, StringComparison.OrdinalIgnoreCase) || entry.Path.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();
        return matches.Length == 1 ? matches[0] : throw new KeyNotFoundException($"Model asset '{id}' matched {matches.Length} entries.");
    }

    private static bool IsModel(AssetEntry entry) => entry.Type is AssetType.SkeletalMesh or AssetType.StaticMesh;

    private static bool IsBrowserAsset(AssetEntry entry) => entry.Type == AssetType.Texture || IsModel(entry);

    public static IReadOnlyList<SubAssetEntry> SelectModelSubEntries(IReadOnlyList<SubAssetEntry>? entries)
        => (entries ?? [])
            .Where(sub => sub.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(sub => sub.FileName.Equals("modeldata.mdl", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(sub => sub.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static SubAssetEntry? FindModelSubEntry(AssetEntry asset)
        => SelectModelSubEntries(asset.SubEntries).FirstOrDefault();

    private static IReadOnlyList<ModifiedAssetEntry> CreateModelLodChanges(AssetEntry model, byte[] replacementMdl)
    {
        IReadOnlyList<SubAssetEntry> modelEntries = SelectModelSubEntries(model.SubEntries);
        if (modelEntries.Count == 0) throw new InvalidDataException($"Asset '{model.Path}' contains no MDL subfiles.");
        return modelEntries.Select(sub => new ModifiedAssetEntry
        {
            ParentEntry = model,
            SubEntry = sub,
            ModifiedData = replacementMdl,
            OriginalData = AssetExtractor.GetSubBlob(sub, model, sub.ContentDirectory),
            Compress = false
        }).ToArray();
    }

    private static SubAssetEntry RequireModelSubEntry(AssetEntry asset)
    {
        SubAssetEntry? sub = FindModelSubEntry(asset);
        if (sub is not null) return sub;
        if (asset.Size is null || asset.Offset is null || string.IsNullOrWhiteSpace(asset.SourceArchive))
            throw new InvalidDataException(
                $"Asset '{asset.Path}' is a pruned database entry: the installed game archives contain no model payload or subfiles to import.");
        throw new InvalidDataException($"Asset '{asset.Path}' contains data but has no supported MDL subfile.");
    }

    private static byte[] ReadModelBytes(AssetEntry asset)
    {
        SubAssetEntry sub = RequireModelSubEntry(asset);
        return AssetExtractor.GetSubBlob(sub, asset, sub.ContentDirectory);
    }

    private static MDLFullData ReadModel(AssetEntry asset) => MDLParserExtended.Parse(ReadModelBytes(asset));

    private static string GetSafePackageFileName(string modId)
    {
        string safe = string.Concat(modId.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_')).Trim('.');
        return (safe.Length == 0 ? "mod" : safe) + ".ng4mod";
    }
}
