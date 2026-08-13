using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;
using YakumoLib.Assets;
using YakumoLib.Formats;

namespace YakumoLib.Modding;

public sealed record ModelTextureCandidate(AssetEntry Asset, string UsageHint);
public sealed record ModelTextureSetEntry(string AssetId, string LogicalPath, string? StoragePath, string DdsFile, string UsageHint, int Width, int Height, int DxgiFormat, int MipCount, string OriginalSha256);
public sealed record ModelTextureSetManifest(
    int SchemaVersion,
    string ModelAssetId,
    string ModelPath,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<ModelTextureSetEntry> Textures,
    IReadOnlyList<string>? UnknownTextureAssetIds = null,
    bool PreserveTextureDimensions = true,
    string OriginalGlbSha256 = "");
public sealed record ModelTextureImportPlan(IReadOnlyList<ModifiedAssetEntry> Changes, int UnchangedCount);

public static class ModelTextureSetService
{
    public const string ManifestFileName = "ronin-texture-set.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<ModelTextureCandidate> DiscoverCandidates(AssetEntry model, IEnumerable<AssetEntry> assets)
    {
        AssetEntry[] assetArray = assets.ToArray();
        string root = Normalize(Path.GetDirectoryName(model.Path) ?? "").TrimEnd('/') + "/Texture/";
        string stem = Path.GetFileName(model.Path);
        var seen = new HashSet<UUID>();
        IEnumerable<AssetEntry> pathCandidates = assetArray.Where(a => a.Type == AssetType.Texture)
            .Where(a => Normalize(a.Path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Where(a => a.FileName.StartsWith(stem, StringComparison.OrdinalIgnoreCase));
        IEnumerable<AssetEntry> materialCandidates = Ng4MaterialResolver.ResolveTextureDependencies(model, assetArray);
        return pathCandidates.Concat(materialCandidates)
            .OrderBy(a => Normalize(a.Path), StringComparer.Ordinal)
            .Where(a => seen.Add(a.AssetID))
            .Select(a => new ModelTextureCandidate(a, ClassifyUsage(a.FileName)))
            .ToArray();
    }

    public static string ClassifyUsage(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string[] tokens = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(t => t.Equals("ColorMask", StringComparison.OrdinalIgnoreCase))) return "ColorMask";
        if (tokens.Any(t => t.Equals("BaseCol", StringComparison.OrdinalIgnoreCase) ||
                            t.Equals("BaseColor", StringComparison.OrdinalIgnoreCase) ||
                            t.Equals("Albedo", StringComparison.OrdinalIgnoreCase) ||
                            t.Equals("Diffuse", StringComparison.OrdinalIgnoreCase))) return "BaseCol";
        if (tokens.Any(t => t.Equals("Normal", StringComparison.OrdinalIgnoreCase))) return "Normal";
        if (tokens.Any(t => t.StartsWith("MRO", StringComparison.OrdinalIgnoreCase))) return "MRO";
        if (tokens.Any(t => t.Equals("Mask", StringComparison.OrdinalIgnoreCase) || t.EndsWith("Mask", StringComparison.OrdinalIgnoreCase))) return "Mask";
        return "Unknown";
    }

    public static ModelTextureSetEntry ToManifestEntry(ModelTextureCandidate c) => new(c.Asset.StringAssetID, c.Asset.Path, c.Asset.StoragePath, c.Asset.FileName, c.UsageHint, 0, 0, 0, 0, "");
    public static string SerializeManifest(ModelTextureSetManifest manifest) => JsonSerializer.Serialize(manifest, JsonOptions);
    public static ModelTextureSetManifest DeserializeManifest(string json) => JsonSerializer.Deserialize<ModelTextureSetManifest>(json, JsonOptions) ?? throw new InvalidDataException("Invalid texture set manifest.");

    public static ModelTextureSetManifest Export(AssetEntry model, IEnumerable<AssetEntry> assets, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Export destination '{destination}' already exists.");

        AssetEntry[] assetArray = assets.ToArray();
        ModelTextureCandidate[] candidates = DiscoverCandidates(model, assetArray).ToArray();
        var unknownIds = ResolveUnknownTextureIds(model, assetArray).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (candidates.Length == 0)
            throw new InvalidOperationException($"No candidate textures were found for model '{model.Path}'.");

        var planned = candidates.Select(candidate => (Candidate: candidate, FileName: GetSafeDdsFileName(candidate.Asset.FileName))).ToArray();
        string? collision = planned.GroupBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (collision is not null)
            throw new InvalidOperationException($"Multiple candidate textures map to DDS filename '{collision}'.");

        string parent = Path.GetDirectoryName(destination) ?? throw new IOException("Export destination must have a parent directory.");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException($"Export destination parent '{parent}' does not exist.");

        var prepared = new List<(string FileName, byte[] DdsBytes, ModelTextureSetEntry ManifestEntry)>(planned.Length);
        foreach (var item in planned)
        {
            TexturePackageDdsData extracted;
            try
            {
                string contentDirectory = item.Candidate.Asset.ContentDirectory
                    ?? item.Candidate.Asset.SubEntries?.FirstOrDefault(sub => sub.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase))?.ContentDirectory
                    ?? item.Candidate.Asset.SubEntries?.Select(sub => sub.ContentDirectory).FirstOrDefault(directory => !string.IsNullOrWhiteSpace(directory))
                    ?? throw new InvalidDataException($"Texture '{item.Candidate.Asset.Path}' has no content directory.");
                extracted = TexturePackageDds.ExtractRootMip(item.Candidate.Asset, contentDirectory);
            }
            catch (Exception ex)
            {
                if (unknownIds.Contains(item.Candidate.Asset.StringAssetID)) continue;
                throw new InvalidDataException($"Failed to extract texture '{item.Candidate.Asset.Path}'.", ex);
            }
            ParseDdsMetadata(extracted.DdsBytes, out int width, out int height, out int dxgiFormat, out _);
            prepared.Add((
                item.FileName,
                extracted.DdsBytes,
                new ModelTextureSetEntry(
                    item.Candidate.Asset.StringAssetID,
                    item.Candidate.Asset.Path,
                    item.Candidate.Asset.StoragePath,
                    item.FileName,
                    item.Candidate.UsageHint,
                    width,
                    height,
                    dxgiFormat,
                    extracted.MipCount,
                    Convert.ToHexString(SHA256.HashData(extracted.DdsBytes)).ToLowerInvariant())));
        }

        string temporary = destination + $".ronin-{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(temporary);
            foreach (var item in prepared)
            {
                string outputPath = Path.Combine(temporary, item.FileName);
                File.WriteAllBytes(outputPath, item.DdsBytes);
            }

            var manifest = new ModelTextureSetManifest(1, model.StringAssetID, model.Path, DateTimeOffset.UtcNow,
                prepared.Select(item => item.ManifestEntry).ToArray(),
                unknownIds.Where(id => prepared.Any(item => item.ManifestEntry.AssetId.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray());
            File.WriteAllText(Path.Combine(temporary, ManifestFileName), SerializeManifest(manifest));
            Directory.Move(temporary, destination);
            return manifest;
        }
        catch
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            throw;
        }
    }

    public static ModelTextureSetManifest ExportPng(AssetEntry model, IEnumerable<AssetEntry> assets, string destinationDirectory)
        => ExportImageSet(model, assets, destinationDirectory, 2, ".png", static (image, path) => image.Save(path, new PngEncoder()));

    public static ModelTextureSetManifest ExportTga(AssetEntry model, IEnumerable<AssetEntry> assets, string destinationDirectory)
        => ExportImageSet(model, assets, destinationDirectory, 3, ".tga", SaveTopLeftTga);

    private static void SaveTopLeftTga(Image<Rgba32> image, string path)
    {
        using var encoded = new MemoryStream();
        image.Save(encoded, new TgaEncoder { BitsPerPixel = TgaBitsPerPixel.Pixel32, Compression = TgaCompression.None });
        byte[] tga = NormalizeTgaToTopLeft(encoded.ToArray(), image.Width, image.Height);
        File.WriteAllBytes(path, tga);
    }

    internal static byte[] EncodeTopLeftTga(ReadOnlySpan<byte> rgba, int width, int height)
    {
        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        using var encoded = new MemoryStream();
        image.Save(encoded, new TgaEncoder { BitsPerPixel = TgaBitsPerPixel.Pixel32, Compression = TgaCompression.None });
        return NormalizeTgaToTopLeft(encoded.ToArray(), width, height);
    }

    internal static byte[] NormalizeTgaToTopLeft(byte[] tga, int width, int height)
    {
        int pixelOffset = 18 + tga[0];
        int rowLength = checked(width * 4);
        int pixelLength = checked(rowLength * height);
        if (tga.Length < pixelOffset + pixelLength || tga[1] != 0 || tga[2] != 2 || tga[16] != 32 || (tga[17] & 0x0F) != 8 || (tga[17] & 0x10) != 0)
            throw new InvalidDataException("ImageSharp produced an unsupported TGA layout.");

        if ((tga[17] & 0x20) == 0)
        {
            byte[] row = new byte[rowLength];
            for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
            {
                Span<byte> topRow = tga.AsSpan(pixelOffset + top * rowLength, rowLength);
                Span<byte> bottomRow = tga.AsSpan(pixelOffset + bottom * rowLength, rowLength);
                topRow.CopyTo(row);
                bottomRow.CopyTo(topRow);
                row.CopyTo(bottomRow);
            }
        }
        tga[17] = (byte)((tga[17] & ~0x30) | 0x20 | 8);
        return tga;
    }

    private static ModelTextureSetManifest ExportImageSet(
        AssetEntry model,
        IEnumerable<AssetEntry> assets,
        string destinationDirectory,
        int schemaVersion,
        string extension,
        Action<Image<Rgba32>, string> saveImage)
    {
        string destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException($"Export destination '{destination}' already exists.");
        AssetEntry[] assetArray = assets.ToArray();
        var candidates = DiscoverCandidates(model, assetArray).ToArray();
        var unknownIds = ResolveUnknownTextureIds(model, assetArray).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (candidates.Length == 0) throw new InvalidOperationException($"No candidate textures were found for model '{model.Path}'.");
        var planned = candidates.Select(candidate => (Candidate: candidate, FileName: Path.GetFileName(candidate.Asset.FileName) + extension)).ToArray();
        string? collision = planned.GroupBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (collision is not null) throw new InvalidOperationException($"Multiple candidate textures map to image filename '{collision}'.");
        string parent = Path.GetDirectoryName(destination) ?? throw new IOException("Export destination must have a parent directory.");
        Directory.CreateDirectory(parent);
        string temp = destination + $".ronin-{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(temp);
            var entries = new List<ModelTextureSetEntry>();
            foreach (var plannedItem in planned)
            {
                ModelTextureCandidate candidate = plannedItem.Candidate;
                string file = plannedItem.FileName;
                try
                {
                    string content = candidate.Asset.ContentDirectory ?? candidate.Asset.SubEntries?.FirstOrDefault(s => s.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase))?.ContentDirectory ?? throw new InvalidDataException($"Texture '{candidate.Asset.Path}' has no content directory.");
                    var extracted = TexturePackageDds.ExtractRootMip(candidate.Asset, content);
                    byte[] rgba = TextureMipChainGenerator.DecodeRootRgba(extracted.DdsBytes, out int width, out int height);
                    using (Image<Rgba32> image = Image.LoadPixelData<Rgba32>(rgba, width, height))
                        saveImage(image, Path.Combine(temp, file));
                    entries.Add(new ModelTextureSetEntry(candidate.Asset.StringAssetID, candidate.Asset.Path, candidate.Asset.StoragePath, file, candidate.UsageHint, width, height, ReadDxgi(extracted.DdsBytes), extracted.MipCount, Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant()));
                }
                catch when (unknownIds.Contains(candidate.Asset.StringAssetID))
                {
                    string partial = Path.Combine(temp, file);
                    if (File.Exists(partial)) File.Delete(partial);
                }
            }
            var manifest = new ModelTextureSetManifest(schemaVersion, model.StringAssetID, model.Path, DateTimeOffset.UtcNow,
                entries, unknownIds.Where(id => entries.Any(entry => entry.AssetId.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray());
            File.WriteAllText(Path.Combine(temp, ManifestFileName), SerializeManifest(manifest));
            Directory.Move(temp, destination);
            return manifest;
        }
        catch { if (Directory.Exists(temp)) Directory.Delete(temp, true); throw; }
    }

    private static int ReadDxgi(ReadOnlySpan<byte> dds) => dds.Length >= 132 && dds.Slice(84, 4).SequenceEqual("DX10"u8) ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds.Slice(128, 4))) : 0;

    private static IReadOnlyList<string> ResolveUnknownTextureIds(AssetEntry model, IEnumerable<AssetEntry> assets)
    {
        try
        {
            return Ng4MaterialResolver.Resolve(model, assets.ToArray())
                .SelectMany(material => material.UnknownTextures)
                .Select(texture => texture.AssetId.GetString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (InvalidDataException)
        {
            // Texture-only fixtures and legacy models may not carry materialmap.bin.
            return [];
        }
    }

    public static ModelTextureImportPlan PlanImport(string directory, IEnumerable<AssetEntry> assets, bool compress = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(assets);
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        string manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath)) throw new InvalidDataException($"Texture set is missing '{ManifestFileName}'.");

        ModelTextureSetManifest manifest;
        try { manifest = DeserializeManifest(File.ReadAllText(manifestPath)); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { throw new InvalidDataException("Texture set manifest is invalid.", ex); }
        if (manifest.SchemaVersion is not (1 or 2 or 3) || manifest.Textures is null)
            throw new InvalidDataException($"Unsupported texture set manifest schema {manifest.SchemaVersion}.");

        AssetEntry[] assetArray = assets.ToArray();
        if (assetArray.GroupBy(asset => asset.AssetID).Any(group => group.Count() > 1))
            throw new InvalidDataException("Available texture assets contain duplicate UUIDs.");
        var assetsById = assetArray.ToDictionary(asset => asset.StringAssetID, StringComparer.OrdinalIgnoreCase);
        if (manifest.Textures.GroupBy(entry => entry.AssetId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Texture set manifest contains duplicate UUIDs.");
        if (manifest.Textures.GroupBy(entry => entry.DdsFile, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("Texture set manifest contains duplicate texture filenames.");

        var prepared = new List<(ModelTextureSetEntry Manifest, AssetEntry Asset, byte[] Root, byte[] OriginalFull, int OriginalWidth, int OriginalHeight, int OriginalMipCount, bool Changed)>();
        foreach (ModelTextureSetEntry item in manifest.Textures)
        {
            ValidateManifestEntry(item);
            if (!assetsById.TryGetValue(item.AssetId, out AssetEntry? asset) || asset.Type != AssetType.Texture)
                throw new InvalidDataException($"Texture UUID '{item.AssetId}' was not found.");
            if (!Normalize(asset.Path).Equals(Normalize(item.LogicalPath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(asset.StoragePath, item.StoragePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Texture UUID '{item.AssetId}' does not match manifest path metadata.");
            string ddsPath = Path.Combine(root, item.DdsFile);
            if (!File.Exists(ddsPath)) throw new InvalidDataException($"Texture set is missing texture '{item.DdsFile}'.");
            string contentDirectory = asset.ContentDirectory
                ?? asset.SubEntries?.FirstOrDefault(sub => sub.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase))?.ContentDirectory
                ?? throw new InvalidDataException($"Texture '{asset.Path}' has no content directory.");
            TexturePackageDdsData original = TexturePackageDds.Extract(asset, contentDirectory);
            byte[] rootDds;
            int format;
            int inputWidth;
            int inputHeight;
            bool changed;
            if (manifest.SchemaVersion is 2 or 3)
            {
                string formatName = manifest.SchemaVersion == 2 ? "PNG" : "TGA";
                string expectedExtension = manifest.SchemaVersion == 2 ? ".png" : ".tga";
                if (!item.DdsFile.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{formatName} texture set entry '{item.DdsFile}' has the wrong file extension.");
                using Image<Rgba32> image = manifest.SchemaVersion == 3
                    ? LoadTga(File.ReadAllBytes(ddsPath), item)
                    : Image.Load<Rgba32>(ddsPath);
                inputWidth = image.Width;
                inputHeight = image.Height;
                byte[] rgba = new byte[checked(image.Width * image.Height * 4)];
                image.CopyPixelDataTo(rgba);
                string hash = Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant();
                changed = !hash.Equals(item.OriginalSha256, StringComparison.OrdinalIgnoreCase) || inputWidth != original.Width || inputHeight != original.Height;
                if (!changed)
                {
                    rootDds = TexturePackageDds.ExtractRootMip(asset, contentDirectory).DdsBytes;
                    ParseDdsMetadata(rootDds, out int width, out int height, out format, out int mipCount);
                }
                else
                {
                    rootDds = TextureMipChainGenerator.EncodeRootRgba(rgba, image.Width, image.Height, original.DdsBytes, allowDimensionChange: true);
                    ParseDdsMetadata(rootDds, out int width, out int height, out format, out int mipCount);
                }
            }
            else
            {
                rootDds = File.ReadAllBytes(ddsPath);
                ParseDdsMetadata(rootDds, out inputWidth, out inputHeight, out format, out int mipCount);
                if (mipCount != 1) throw new InvalidDataException($"DDS '{item.DdsFile}' must contain only its root mip.");
                changed = !Convert.ToHexString(SHA256.HashData(rootDds)).Equals(item.OriginalSha256, StringComparison.OrdinalIgnoreCase) || inputWidth != original.Width || inputHeight != original.Height;
            }
            if (original.Width != item.Width || original.Height != item.Height || original.MipCount != item.MipCount)
                throw new InvalidDataException($"Texture '{asset.Path}' no longer matches its export manifest.");
            if (format != item.DxgiFormat || !rootDds.AsSpan(84, 4).SequenceEqual("DX10"u8))
                rootDds = TextureMipChainGenerator.TranscodeRoot(rootDds, original.DdsBytes, allowDimensionChange: true);
            prepared.Add((item, asset, rootDds, original.DdsBytes, original.Width, original.Height, original.MipCount, changed));
        }

        var changes = new List<ModifiedAssetEntry>();
        foreach (var item in prepared.Where(item => item.Changed))
        {
            ParseDdsMetadata(item.Root, out int width, out int height, out _, out _);
            int targetMipCount = Math.Min(item.Manifest.MipCount, TextureMipChainGenerator.MaximumMipCount(width, height));
            byte[] regenerated = TextureMipChainGenerator.Generate(item.Root, targetMipCount);
            bool layoutChange = width != item.OriginalWidth || height != item.OriginalHeight || targetMipCount != item.OriginalMipCount;
            TexturePackageDds.ValidateImportAgainstAsset(regenerated, item.Asset, layoutChange);
            changes.Add(new ModifiedAssetEntry { ParentEntry = item.Asset, SubEntry = null, ModifiedData = regenerated, OriginalData = item.OriginalFull, Compress = compress, AllowTextureLayoutChange = layoutChange });
        }
        return new ModelTextureImportPlan(changes, prepared.Count - changes.Count);
    }

    private static void ValidateManifestEntry(ModelTextureSetEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.AssetId) || entry.AssetId.Length != 35 ||
            !UUIDParser.TryParse(entry.AssetId, out UUID? parsedId) ||
            !parsedId.GetString().Equals(entry.AssetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Manifest texture UUID '{entry.AssetId}' is invalid.");
        if (string.IsNullOrWhiteSpace(entry.LogicalPath) || Path.IsPathRooted(entry.LogicalPath) || entry.LogicalPath.Split('/', '\\').Any(part => part is ".." or "."))
            throw new InvalidDataException($"Manifest texture path '{entry.LogicalPath}' is unsafe.");
        if (string.IsNullOrWhiteSpace(entry.DdsFile) || entry.DdsFile != Path.GetFileName(entry.DdsFile) ||
            entry.DdsFile is "." or ".." || entry.DdsFile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !(entry.DdsFile.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || entry.DdsFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || entry.DdsFile.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Manifest texture filename '{entry.DdsFile}' is unsafe.");
        if (entry.Width <= 0 || entry.Height <= 0 || entry.MipCount <= 0 || entry.DxgiFormat is not (71 or 72 or 77 or 78 or 80 or 83 or 98 or 99))
            throw new InvalidDataException($"Manifest DDS metadata for '{entry.DdsFile}' is unsupported.");
        if (string.IsNullOrWhiteSpace(entry.OriginalSha256) || entry.OriginalSha256.Length != 64 || entry.OriginalSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException($"Manifest SHA-256 for '{entry.DdsFile}' is invalid.");
    }

    private static Image<Rgba32> LoadTga(byte[] tga, ModelTextureSetEntry entry)
    {
        try
        {
            var detectedFormat = Image.DetectFormat(tga);
            if (detectedFormat is null || !detectedFormat.Name.Equals(TgaFormat.Instance.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Texture '{entry.DdsFile}' is not a valid TGA image.");

            Image<Rgba32> image = Image.Load<Rgba32>(tga);
            return image;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException($"TGA '{entry.DdsFile}' could not be decoded: {exception.Message}", exception);
        }
    }

    private static string GetSafeDdsFileName(string assetFileName)
    {
        if (string.IsNullOrWhiteSpace(assetFileName) || assetFileName is "." or ".." ||
            Path.GetFileName(assetFileName) != assetFileName || assetFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Texture filename '{assetFileName}' is unsafe for export.");
        return assetFileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? assetFileName : assetFileName + ".dds";
    }

    private static void ParseDdsMetadata(ReadOnlySpan<byte> dds, out int width, out int height, out int dxgiFormat, out int mipCount)
    {
        if (dds.Length < 128 || !dds[..4].SequenceEqual("DDS "u8) || BinaryPrimitives.ReadUInt32LittleEndian(dds[4..8]) != 124)
            throw new InvalidDataException("Extracted texture does not contain a valid DDS header.");

        height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds[12..16]));
        width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds[16..20]));
        mipCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds[28..32]));
        if (mipCount == 0) mipCount = 1;
        if (width <= 0 || height <= 0 || mipCount <= 0)
            throw new InvalidDataException("Extracted DDS has invalid dimensions or mip count.");

        uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(dds[84..88]);
        dxgiFormat = fourCc switch
        {
            0x30315844 when dds.Length >= 148 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds[128..132])), // DX10
            0x31545844 => 71, // BC1_UNORM
            0x33545844 => 74, // BC2_UNORM
            0x35545844 => 77, // BC3_UNORM
            0x31495441 or 0x55344342 => 80, // BC4_UNORM
            0x32495441 or 0x55354342 => 83, // BC5_UNORM
            0x53344342 => 81, // BC4_SNORM
            0x53354342 => 84, // BC5_SNORM
            _ => 0
        };
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
