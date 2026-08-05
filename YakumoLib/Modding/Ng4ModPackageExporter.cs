using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace YakumoLib.Modding;

public sealed record Ng4ModExportRequest
{
    public string ModId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string? CoverPath { get; init; }
    public string? IconPath { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public string GameId { get; init; } = Ng4ModManifest.SupportedGameId;
    public string AssetDatabaseSha256 { get; init; } = "";
    public IReadOnlyList<ModifiedAssetEntry> Changes { get; init; } = [];
    public string DestinationPath { get; init; } = "";
}

public static class Ng4ModPackageExporter
{
    private const int TrailerSize = 160;
    private const int MaxMetadataSize = 1024 * 1024;
    private const int MaxCoverSize = 8 * 1024 * 1024;
    private const long ParentAlignment = 16;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("NG4MOD2\0");
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public static string Export(Ng4ModExportRequest request)
    {
        ValidateRequest(request);
        IReadOnlyList<ParentPayload> payloads = ParentPayloadBuilder.BuildAll(request.Changes);
        if (payloads.Count == 0)
            throw new InvalidDataException("NG4MOD export produced no parent payloads.");

        string destination = Path.GetFullPath(request.DestinationPath);
        string destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("NG4MOD destination has no parent directory.", nameof(request));
        Directory.CreateDirectory(destinationDirectory);
        string temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            Ng4ModManifest manifest = BuildManifest(request, payloads);
            WritePackage(temporaryPath, manifest, payloads, ResolveCoverPath(request));
            VerifyPackage(temporaryPath);
            File.Move(temporaryPath, destination, overwrite: true);
            return destination;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public static void VerifyPackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < TrailerSize) throw new InvalidDataException("NG4MOD v2 package is truncated.");
        stream.Position = stream.Length - TrailerSize;
        byte[] trailer = new byte[TrailerSize];
        stream.ReadExactly(trailer);
        if (!trailer.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("NG4MOD v2 magic is invalid.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(trailer.AsSpan(8, 4)) != TrailerSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(trailer.AsSpan(12, 4)) != 2)
            throw new InvalidDataException("NG4MOD v2 trailer version is invalid.");
        if (!trailer.AsSpan(16, 8).SequenceEqual(new byte[8]) ||
            !trailer.AsSpan(48, 16).SequenceEqual(new byte[16]))
            throw new InvalidDataException("NG4MOD v2 reserved trailer bytes must be zero.");

        long payloadLength = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(24, 8));
        long metadataOffset = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(32, 8));
        long metadataLength = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(40, 8));
        long expectedLength;
        try { expectedLength = checked(metadataOffset + metadataLength + TrailerSize); }
        catch (OverflowException ex) { throw new InvalidDataException("NG4MOD v2 trailer bounds overflowed.", ex); }
        if (payloadLength < 0 || metadataOffset < payloadLength || metadataLength < 0 || metadataLength > MaxMetadataSize
            || metadataOffset > stream.Length - TrailerSize || expectedLength != stream.Length)
            throw new InvalidDataException("NG4MOD v2 trailer bounds are invalid.");
        byte[] trailerCopy = (byte[])trailer.Clone();
        Array.Clear(trailerCopy, 128, 32);
        if (!SHA256.HashData(trailerCopy).AsSpan().SequenceEqual(trailer.AsSpan(128, 32)))
            throw new InvalidDataException("NG4MOD v2 trailer hash does not match.");
        stream.Position = 0;
        if (!HashRange(stream, payloadLength).AsSpan().SequenceEqual(trailer.AsSpan(64, 32)))
            throw new InvalidDataException("NG4MOD v2 DAT prefix hash does not match.");
        stream.Position = metadataOffset;
        byte[] metadata = new byte[checked((int)metadataLength)];
        stream.ReadExactly(metadata);
        if (!SHA256.HashData(metadata).AsSpan().SequenceEqual(trailer.AsSpan(96, 32)))
            throw new InvalidDataException("NG4MOD v2 metadata hash does not match.");
        Ng4ModManifest manifest;
        try { manifest = Ng4ModPackageJson.Deserialize(Utf8NoBom.GetString(metadata)); }
        catch (Exception ex) when (ex is DecoderFallbackException or System.Text.Json.JsonException or ArgumentException)
        {
            throw new InvalidDataException("NG4MOD v2 metadata is not valid UTF-8 JSON.", ex);
        }
        ValidateCover(stream, manifest.Cover, payloadLength, metadataOffset);

        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var storagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var payloadPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long previousEnd = 0;
        foreach (Ng4ModAsset asset in manifest.Assets.OrderBy(asset => asset.PayloadOffset))
        {
            if (!assetIds.Add(RequireValue(asset.AssetId, "asset_id")))
                throw new InvalidDataException($"NG4MOD contains duplicate asset id '{asset.AssetId}'.");
            if (!IsAssetId(asset.AssetId))
                throw new InvalidDataException($"NG4MOD asset id '{asset.AssetId}' is not canonical.");
            ValidateLogicalPath(asset.LogicalPath, "logical_path");
            ValidateLogicalPath(asset.StoragePath, "storage_path");
            ValidateStoragePath(asset.StoragePath, asset.AssetId);
            if (!storagePaths.Add(asset.StoragePath))
                throw new InvalidDataException($"NG4MOD contains duplicate storage path '{asset.StoragePath}'.");
            string payloadPath = NormalizePackagePath(asset.PayloadPath);
            if (!string.Equals(payloadPath, $"payload/{asset.AssetId}.bin", StringComparison.Ordinal) ||
                !payloadPaths.Add(payloadPath))
                throw new InvalidDataException($"NG4MOD asset '{asset.AssetId}' has an invalid or duplicate payload_path.");
            RequireValue(asset.AssetType, "asset_type");
            ValidateOpaqueCsvField(asset.CsvUnknown, "csv_unknown");
            ValidateOpaqueCsvField(asset.CsvMetadata, "csv_metadata");
            if (!string.Equals(asset.GameCompression, "none", StringComparison.Ordinal))
                throw new InvalidDataException($"NG4MOD asset '{asset.AssetId}' has unsupported game_compression '{asset.GameCompression}'.");

            long expectedOffset = Align(previousEnd, ParentAlignment);
            if (asset.PayloadOffset != expectedOffset || asset.PayloadOffset % ParentAlignment != 0 || asset.PayloadSize <= 0
                || asset.PayloadOffset > payloadLength || asset.PayloadSize > payloadLength - asset.PayloadOffset)
                throw new InvalidDataException($"NG4MOD asset '{asset.AssetId}' has an invalid DAT range.");
            ValidateZeroRange(stream, previousEnd, asset.PayloadOffset - previousEnd);
            stream.Position = asset.PayloadOffset;
            string actualHash = Convert.ToHexString(HashRange(stream, asset.PayloadSize)).ToLowerInvariant();
            if (!string.Equals(actualHash, asset.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"NG4MOD payload '{asset.AssetId}' SHA-256 does not match its manifest.");
            previousEnd = checked(asset.PayloadOffset + asset.PayloadSize);

            ValidateSubEntries(asset);
        }
        if (manifest.Assets.Count == 0) throw new InvalidDataException("NG4MOD v2 metadata has no assets.");
        if (previousEnd != payloadLength) throw new InvalidDataException("NG4MOD v2 DAT prefix has unreferenced trailing bytes.");
    }

    internal static Ng4ModManifest BuildManifest(Ng4ModExportRequest request, IReadOnlyList<ParentPayload> payloads)
    {
        var assets = new List<Ng4ModAsset>(payloads.Count);
        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var payloadPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long payloadOffset = 0;
        foreach (ParentPayload payload in payloads.OrderBy(payload => payload.Parent.AssetID.GetString(), StringComparer.Ordinal))
        {
            string assetId = payload.Parent.AssetID.GetString();
            if (!assetIds.Add(assetId))
                throw new InvalidDataException($"NG4MOD export contains duplicate asset id '{assetId}'.");
            ValidateLogicalPath(payload.Parent.Path, "logical_path");
            ValidateLogicalPath(payload.Parent.StoragePath, "storage_path");
            string payloadPath = NormalizePackagePath("payload/" + assetId + ".bin");
            if (!payloadPaths.Add(payloadPath))
                throw new InvalidDataException($"NG4MOD export contains duplicate payload path '{payloadPath}'.");

            HashSet<string> explicitlyReplaced = request.Changes
                .Where(change => change.ParentEntry.AssetID.Equals(payload.Parent.AssetID) && change.SubEntry is not null)
                .Select(change => change.SubEntry!.FileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool wholeParentReplaced = request.Changes.Any(change =>
                change.ParentEntry.AssetID.Equals(payload.Parent.AssetID) && change.SubEntry is null);
            string[] replacedSubEntries = payload.SubEntries
                .Where(subEntry => wholeParentReplaced || explicitlyReplaced.Contains(subEntry.FileName))
                .Select(subEntry => subEntry.FileName)
                .ToArray();

            assets.Add(new Ng4ModAsset
            {
                AssetId = assetId,
                LogicalPath = payload.Parent.Path,
                AssetType = payload.Parent.Type.ToString(),
                StoragePath = payload.Parent.StoragePath!,
                CsvUnknown = payload.Parent.CsvUnknown ?? "",
                CsvMetadata = payload.Parent.CsvMetadata ?? "",
                PayloadPath = payloadPath,
                PayloadSize = payload.Data.Length,
                PayloadSha256 = Sha256(payload.Data.Span),
                PayloadOffset = payloadOffset = Align(payloadOffset, ParentAlignment),
                GameCompression = "none",
                SubEntries = payload.SubEntries.Select(subEntry => new Ng4ModSubEntry
                {
                    Name = subEntry.FileName,
                    Addressing = payload.Parent.Type == YakumoLib.Assets.AssetType.Texture && subEntry.IsGlobal
                        ? "global"
                        : "local",
                    Offset = subEntry.Offset,
                    Size = subEntry.Size,
                    CompressedSize = 0
                }).ToArray(),
                Intent = new Ng4ModAssetIntent
                {
                    ReplacedSubEntries = replacedSubEntries,
                    SourceArchive = payload.Parent.SourceArchive ?? "",
                    SourceFingerprint = ""
                }
            });
            payloadOffset = checked(payloadOffset + payload.Data.Length);
        }

        return Ng4ModManifest.Create(request.ModId, request.Name, request.Version) with
        {
            Author = request.Author ?? "",
            Description = request.Description ?? "",
            Icon = "",
            Dependencies = request.Dependencies.ToArray(),
            Game = new Ng4ModGame
            {
                Id = request.GameId,
                AssetDatabaseSha256 = request.AssetDatabaseSha256 ?? ""
            },
            Assets = assets.ToArray()
        };
    }

    internal static void WritePackage(
        string temporaryPath,
        Ng4ModManifest manifest,
        IReadOnlyList<ParentPayload> payloads,
        string? iconSourcePath)
    {
        var payloadsByAssetId = new Dictionary<string, ParentPayload>(StringComparer.OrdinalIgnoreCase);
        foreach (ParentPayload payload in payloads)
        {
            string assetId = payload.Parent.AssetID.GetString();
            if (!payloadsByAssetId.TryAdd(assetId, payload))
                throw new InvalidDataException($"NG4MOD package build received duplicate asset id '{assetId}'.");
        }

        var manifestAssetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Ng4ModAsset asset in manifest.Assets)
        {
            if (!manifestAssetIds.Add(asset.AssetId))
                throw new InvalidDataException($"NG4MOD manifest contains duplicate asset id '{asset.AssetId}'.");
            if (!payloadsByAssetId.ContainsKey(asset.AssetId))
                throw new InvalidDataException($"NG4MOD manifest has no source payload for asset '{asset.AssetId}'.");
        }
        if (manifestAssetIds.Count != payloadsByAssetId.Count)
            throw new InvalidDataException("NG4MOD package build has payloads missing from the manifest.");

        using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            foreach (Ng4ModAsset asset in manifest.Assets.OrderBy(asset => asset.PayloadOffset))
            {
                if (!payloadsByAssetId.TryGetValue(asset.AssetId, out ParentPayload? payload))
                    throw new InvalidDataException($"NG4MOD package build cannot bind asset '{asset.AssetId}'.");
                WritePadding(output, asset.PayloadOffset - output.Position);
                output.Write(payload.Data.Span);
            }
            long payloadLength = output.Position;
            byte[]? coverBytes = null;
            if (!string.IsNullOrWhiteSpace(iconSourcePath))
            {
                var coverInfo = new FileInfo(iconSourcePath);
                if (coverInfo.Length <= 0 || coverInfo.Length > MaxCoverSize)
                    throw new InvalidDataException("NG4MOD v2 cover must be between 1 byte and 8 MiB.");
                coverBytes = File.ReadAllBytes(iconSourcePath);
                ValidatePng(coverBytes);
                manifest = manifest with
                {
                    Icon = "",
                    Cover = new Ng4ModCover
                    {
                        MediaType = "image/png",
                        Offset = payloadLength,
                        Length = coverBytes.LongLength,
                        Sha256 = Sha256(coverBytes)
                    }
                };
                output.Write(coverBytes);
            }
            else
            {
                manifest = manifest with { Icon = "", Cover = null };
            }

            long metadataOffset = output.Position;
            byte[] metadata = Utf8NoBom.GetBytes(Ng4ModPackageJson.Serialize(manifest));
            if (metadata.Length > MaxMetadataSize) throw new InvalidDataException("NG4MOD v2 metadata exceeds the 1 MiB limit.");
            output.Write(metadata);
            byte[] trailer = new byte[TrailerSize];
            Magic.CopyTo(trailer, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(8, 4), TrailerSize);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(12, 4), 2);
            BinaryPrimitives.WriteInt64LittleEndian(trailer.AsSpan(24, 8), payloadLength);
            BinaryPrimitives.WriteInt64LittleEndian(trailer.AsSpan(32, 8), metadataOffset);
            BinaryPrimitives.WriteInt64LittleEndian(trailer.AsSpan(40, 8), metadata.LongLength);
            output.Flush();
            output.Position = 0;
            HashRange(output, payloadLength).CopyTo(trailer, 64);
            SHA256.HashData(metadata).CopyTo(trailer, 96);
            SHA256.HashData(trailer).CopyTo(trailer, 128);
            output.Position = checked(metadataOffset + metadata.Length);
            output.Write(trailer);
            output.Flush(true);
        }
    }

    private static void ValidateRequest(Ng4ModExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireArgument(request.ModId, nameof(request.ModId));
        RequireArgument(request.Name, nameof(request.Name));
        RequireArgument(request.Version, nameof(request.Version));
        RequireArgument(request.DestinationPath, nameof(request.DestinationPath));
        if (!string.Equals(Path.GetExtension(request.DestinationPath), ".ng4mod", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("NG4MOD destination must use the .ng4mod extension.", nameof(request.DestinationPath));
        if (Directory.Exists(request.DestinationPath))
            throw new ArgumentException("NG4MOD destination cannot be a directory.", nameof(request.DestinationPath));
        if (!string.Equals(request.GameId, Ng4ModManifest.SupportedGameId, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported NG4MOD game id '{request.GameId}'.");
        if (request.Dependencies is null || request.Dependencies.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("NG4MOD dependencies cannot contain empty values.", nameof(request.Dependencies));
        if (!string.IsNullOrWhiteSpace(request.AssetDatabaseSha256) && !IsSha256(request.AssetDatabaseSha256))
            throw new ArgumentException("asset_database_sha256 must be an empty value or a 64-character hexadecimal SHA-256.", nameof(request.AssetDatabaseSha256));
        if (request.Changes is null || request.Changes.Count == 0)
            throw new ArgumentException("NG4MOD export requires at least one change.", nameof(request.Changes));
        if (request.Changes.Any(change => change is null))
            throw new ArgumentException("NG4MOD changes cannot contain null entries.", nameof(request.Changes));
        if (request.Changes.Any(change => change.Compress))
            throw new NotSupportedException("NG4MOD v2 stores uncompressed game payloads only.");
        foreach (ModifiedAssetEntry change in request.Changes)
        {
            if (change.ParentEntry is null)
                throw new InvalidDataException("NG4MOD change is missing its parent asset.");
            ValidateLogicalPath(change.ParentEntry.StoragePath, "storage_path");
        }
        string? coverPath = ResolveCoverPath(request);
        if (!string.IsNullOrWhiteSpace(coverPath))
        {
            if (!string.Equals(Path.GetExtension(coverPath), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("NG4MOD v2 cover must use the .png extension.", nameof(request.CoverPath));
            if (!File.Exists(coverPath))
                throw new FileNotFoundException("NG4MOD cover file was not found.", coverPath);
        }
    }

    private static void ValidateSubEntries(Ng4ModAsset asset)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool isTexture = string.Equals(asset.AssetType, "Texture", StringComparison.OrdinalIgnoreCase);
        foreach (Ng4ModSubEntry subEntry in asset.SubEntries)
        {
            if (!names.Add(RequireValue(subEntry.Name, "sub_entries.name")))
                throw new InvalidDataException($"NG4MOD asset '{asset.AssetId}' contains duplicate sub-entry '{subEntry.Name}'.");
            if (subEntry.Name.Contains('/'))
                throw new InvalidDataException($"NG4MOD sub-entry '{subEntry.Name}' contains a path separator.");
            if (subEntry.Addressing is not ("local" or "global"))
                throw new InvalidDataException($"NG4MOD sub-entry '{subEntry.Name}' has invalid addressing.");
            if (!isTexture && subEntry.Addressing == "global")
                throw new InvalidDataException($"NG4MOD non-texture asset '{asset.AssetId}' contains a global sub-entry.");
            if (subEntry.Offset < 0 || subEntry.Size < 0 || subEntry.CompressedSize != 0 ||
                subEntry.Offset > asset.PayloadSize || subEntry.Size > asset.PayloadSize - subEntry.Offset)
                throw new InvalidDataException($"NG4MOD sub-entry '{subEntry.Name}' is outside its payload.");
        }
        var replacedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string replaced in asset.Intent.ReplacedSubEntries)
        {
            if (!replacedNames.Add(RequireValue(replaced, "intent.replaced_sub_entries")) || !names.Contains(replaced))
                throw new InvalidDataException($"NG4MOD intent references unknown sub-entry '{replaced}'.");
        }
    }

    private static string? ResolveCoverPath(Ng4ModExportRequest request)
    {
        string? coverPath = string.IsNullOrWhiteSpace(request.CoverPath) ? null : request.CoverPath;
        string? legacyPath = string.IsNullOrWhiteSpace(request.IconPath) ? null : request.IconPath;
        if (coverPath is not null && legacyPath is not null &&
            !string.Equals(Path.GetFullPath(coverPath), Path.GetFullPath(legacyPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("CoverPath and IconPath cannot refer to different files.", nameof(request));
        return coverPath ?? legacyPath;
    }

    private static void ValidateCover(Stream stream, Ng4ModCover? cover, long payloadLength, long metadataOffset)
    {
        if (cover is null)
        {
            if (metadataOffset != payloadLength)
                throw new InvalidDataException("NG4MOD v2 has unreferenced bytes between DAT and metadata.");
            return;
        }

        long coverEnd;
        try { coverEnd = checked(cover.Offset + cover.Length); }
        catch (OverflowException ex) { throw new InvalidDataException("NG4MOD v2 cover range overflowed.", ex); }
        if (!string.Equals(cover.MediaType, "image/png", StringComparison.Ordinal) ||
            cover.Offset != payloadLength || cover.Length <= 0 || cover.Length > MaxCoverSize || coverEnd != metadataOffset)
            throw new InvalidDataException("NG4MOD v2 cover range is invalid.");
        if (!IsSha256(cover.Sha256))
            throw new InvalidDataException("NG4MOD v2 cover SHA-256 is invalid.");

        stream.Position = cover.Offset;
        byte[] bytes = new byte[checked((int)cover.Length)];
        stream.ReadExactly(bytes);
        ValidatePng(bytes);
        if (!string.Equals(Sha256(bytes), cover.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NG4MOD v2 cover SHA-256 does not match.");
    }

    private static void ValidatePng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes[..PngSignature.Length].SequenceEqual(PngSignature))
            throw new InvalidDataException("NG4MOD v2 cover is not a PNG stream.");

        int position = PngSignature.Length;
        bool sawIhdr = false;
        bool sawIdat = false;
        bool sawIend = false;
        while (position < bytes.Length)
        {
            if (bytes.Length - position < 12)
                throw new InvalidDataException("NG4MOD v2 cover chunk is truncated.");
            uint rawLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(position, 4));
            if (rawLength > int.MaxValue)
                throw new InvalidDataException("NG4MOD v2 cover chunk is too large.");
            int length = (int)rawLength;
            int typeOffset = checked(position + 4);
            int dataOffset = checked(position + 8);
            if (length > bytes.Length - dataOffset - 4)
                throw new InvalidDataException("NG4MOD v2 cover chunk exceeds the stream.");
            int crcOffset = checked(dataOffset + length);
            ReadOnlySpan<byte> type = bytes.Slice(typeOffset, 4);
            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawIhdr || position != PngSignature.Length || length != 13)
                    throw new InvalidDataException("NG4MOD v2 cover IHDR placement is invalid.");
                uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(dataOffset, 4));
                uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(dataOffset + 4, 4));
                byte bitDepth = bytes[dataOffset + 8];
                byte colorType = bytes[dataOffset + 9];
                if (width == 0 || height == 0 || width > 4096 || height > 4096 ||
                    (ulong)width * height > 16_777_216UL ||
                    !IsValidPngBitDepth(bitDepth, colorType) || bytes[dataOffset + 10] != 0 ||
                    bytes[dataOffset + 11] != 0 || bytes[dataOffset + 12] != 0)
                    throw new InvalidDataException("NG4MOD v2 cover IHDR fields are invalid.");
                sawIhdr = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawIhdr || sawIend)
                    throw new InvalidDataException("NG4MOD v2 cover IDAT placement is invalid.");
                sawIdat = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!sawIhdr || !sawIdat || sawIend || length != 0 || crcOffset + 4 != bytes.Length)
                    throw new InvalidDataException("NG4MOD v2 cover IEND is invalid.");
                sawIend = true;
            }
            else if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
            {
                throw new InvalidDataException("NG4MOD v2 cover APNG is not supported.");
            }

            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(crcOffset, 4));
            if (Crc32(bytes.Slice(typeOffset, 4 + length)) != expectedCrc)
                throw new InvalidDataException("NG4MOD v2 cover chunk CRC does not match.");
            position = checked(crcOffset + 4);
        }
        if (!sawIhdr || !sawIdat || !sawIend)
            throw new InvalidDataException("NG4MOD v2 cover is missing required PNG chunks.");
    }

    private static bool IsValidPngBitDepth(byte depth, byte colorType) => colorType switch
    {
        0 => depth is 1 or 2 or 4 or 8 or 16,
        2 => depth is 8 or 16,
        3 => depth is 1 or 2 or 4 or 8,
        4 or 6 => depth is 8 or 16,
        _ => false
    };

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : 0xedb88320U ^ (crc >> 1);
        }
        return crc ^ uint.MaxValue;
    }

    private static void ValidateZeroRange(Stream stream, long offset, long length)
    {
        if (length <= 0) return;
        stream.Position = offset;
        Span<byte> buffer = stackalloc byte[16];
        while (length > 0)
        {
            int count = (int)Math.Min(buffer.Length, length);
            stream.ReadExactly(buffer[..count]);
            if (buffer[..count].IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidDataException("NG4MOD v2 DAT alignment padding must contain zero bytes.");
            length -= count;
        }
    }

    private static void ValidateStoragePath(string path, string assetId)
    {
        string[] parts = path.Split('/');
        if (parts.Length < 5 || parts[0] != "Assets" || parts[1] != "Files" ||
            !string.Equals(parts[^2], assetId, StringComparison.OrdinalIgnoreCase) || parts[^1] != "asset.bin")
            throw new InvalidDataException("NG4MOD storage_path must be Assets/Files/.../<asset_id>/asset.bin.");
    }

    private static void ValidateOpaqueCsvField(string? value, string field)
    {
        if (value is null || value.IndexOfAny([',', '\r', '\n']) >= 0)
            throw new InvalidDataException($"NG4MOD {field} contains a CSV delimiter.");
    }

    private static bool IsAssetId(string value)
    {
        string[] parts = value.Split('-');
        return parts.Length == 4 && parts.All(part => part.Length == 8 && part.All(Uri.IsHexDigit));
    }

    private static string NormalizePackagePath(string path)
    {
        string value = RequireValue(path, "ZIP entry path");
        if (value.Contains('\\') || value.Contains(':') || Path.IsPathRooted(value) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            value.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException($"Unsafe NG4MOD ZIP entry path '{path}'.");
        return value;
    }

    private static void ValidateLogicalPath(string? path, string field)
    {
        string value = RequireValue(path, field);
        if (value.Contains('\\') || value.Contains(':') || Path.IsPathRooted(value) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            value.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException($"NG4MOD {field} must be a safe relative path.");
    }

    private static string RequireValue(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"NG4MOD {field} is required.");
        return value;
    }

    private static void RequireArgument(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => Uri.IsHexDigit(character));

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static long Align(long value, long alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static void WritePadding(Stream stream, long count)
    {
        if (count < 0) throw new InvalidDataException("NG4MOD v2 payload offsets are not monotonic.");
        Span<byte> zeros = stackalloc byte[16];
        while (count > 0) { int chunk = (int)Math.Min(zeros.Length, count); stream.Write(zeros[..chunk]); count -= chunk; }
    }

    private static byte[] HashRange(Stream stream, long length)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) throw new InvalidDataException("NG4MOD v2 payload range is truncated.");
            sha.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return sha.GetHashAndReset();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
