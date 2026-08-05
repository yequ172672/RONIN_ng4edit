using YakumoLib.Assets;
using YakumoLib.Formats;

namespace YakumoLib.Modding;

/// <summary>A complete uncompressed parent payload ready for NG4MOD v2 serialization.</summary>
public sealed record ParentPayload
{
    public ParentPayload(
        AssetEntry parent,
        ReadOnlyMemory<byte> data,
        IEnumerable<NormalizedSubEntry> subEntries)
        : this(
            parent,
            data.ToArray(),
            subEntries?.ToArray() ?? throw new ArgumentNullException(nameof(subEntries)))
    {
    }

    private ParentPayload(AssetEntry parent, byte[] ownedData, NormalizedSubEntry[] ownedSubEntries)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Data = ownedData ?? throw new ArgumentNullException(nameof(ownedData));
        ArgumentNullException.ThrowIfNull(ownedSubEntries);
        SubEntries = Array.AsReadOnly(ownedSubEntries);
    }

    internal static ParentPayload TakeOwnership(
        AssetEntry parent,
        byte[] ownedData,
        NormalizedSubEntry[] ownedSubEntries) => new(parent, ownedData, ownedSubEntries);

    public AssetEntry Parent { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public IReadOnlyList<NormalizedSubEntry> SubEntries { get; }
}

/// <summary>A sub-entry offset normalized relative to its containing parent payload.</summary>
public sealed record NormalizedSubEntry(
    string FileName,
    long Offset,
    long Size,
    bool IsGlobal = false);

/// <summary>Builds complete parent payloads from staged whole-parent or subfile replacements.</summary>
public static class ParentPayloadBuilder
{
    public static IReadOnlyList<ParentPayload> BuildAll(IReadOnlyList<ModifiedAssetEntry> modifications)
    {
        ArgumentNullException.ThrowIfNull(modifications);

        var result = new List<ParentPayload>();
        foreach (var group in modifications.GroupBy(item => item.ParentEntry.AssetID))
        {
            ModifiedAssetEntry first = group.First();
            AssetEntry parent = first.ParentEntry;
            foreach (ModifiedAssetEntry modification in group.Skip(1))
                ValidateParentIdentity(parent, modification.ParentEntry);

            ModifiedAssetEntry? wholeParent = group.LastOrDefault(item => item.SubEntry is null);
            if (wholeParent is not null && group.Any(item => item.SubEntry is not null))
                throw new InvalidOperationException($"Asset '{parent.Path}' mixes whole-parent and subfile replacements.");

            if (wholeParent is not null)
            {
                if (group.Count(item => item.SubEntry is null) != 1)
                    throw new InvalidOperationException($"Asset '{parent.Path}' has multiple whole-parent replacements.");
                if (parent.Type == AssetType.Texture)
                {
                    TexturePackageImportResult splitTexture = TexturePackageDds.CreateTemplateImport(wholeParent.ModifiedData, parent);
                    byte[] textureParent = BuildTextureParent(parent, splitTexture, out NormalizedSubEntry[] textureSubEntries);
                    result.Add(ParentPayload.TakeOwnership(parent, textureParent, textureSubEntries));
                    continue;
                }

                byte[] ownedWholeParent = wholeParent.ModifiedData.ToArray();
                result.Add(ParentPayload.TakeOwnership(parent, ownedWholeParent,
                    BuildWholeParentSubEntries(parent, ownedWholeParent.LongLength)));
                continue;
            }

            IReadOnlyList<SubAssetEntry> subEntries = parent.SubEntries ?? [];
            if (subEntries.Count == 0)
                throw new InvalidOperationException($"Asset '{parent.Path}' has no local subfiles to replace.");

            var replacements = new Dictionary<string, ModifiedAssetEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ModifiedAssetEntry modification in group)
            {
                SubAssetEntry sub = modification.SubEntry!;
                SubAssetEntry? canonical = subEntries.FirstOrDefault(candidate => ReferenceEquals(candidate, sub))
                    ?? subEntries.FirstOrDefault(candidate =>
                        string.Equals(candidate.FileName, sub.FileName, StringComparison.OrdinalIgnoreCase));
                if (canonical is null)
                    throw new InvalidOperationException($"Subfile '{sub.FileName}' does not belong to '{parent.Path}'.");
                ValidateReplacementSubEntry(parent, canonical, sub);
                if (!replacements.TryAdd(sub.FileName, modification))
                    throw new InvalidOperationException($"Asset '{parent.Path}' contains multiple replacements for subfile '{sub.FileName}'.");
            }

            string sourceDirectory = subEntries.Select(sub => sub.ContentDirectory)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                ?? parent.ContentDirectory
                ?? throw new InvalidOperationException($"No archive directory is known for '{parent.Path}'.");
            byte[] originalParent = AssetExtractor.GetParentBlob(parent, sourceDirectory);
            result.Add(RebuildParent(parent, subEntries, replacements, originalParent));
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private static void ValidateParentIdentity(AssetEntry expected, AssetEntry actual)
    {
        EnsureIdentityField(expected.AssetID.Equals(actual.AssetID), expected, nameof(AssetEntry.AssetID));
        EnsureIdentityField(string.Equals(expected.Path, actual.Path, StringComparison.Ordinal), expected, nameof(AssetEntry.Path));
        EnsureIdentityField(expected.Type == actual.Type, expected, nameof(AssetEntry.Type));
        EnsureIdentityField(expected.Size == actual.Size, expected, nameof(AssetEntry.Size));
        EnsureIdentityField(expected.CompressedSize == actual.CompressedSize, expected, nameof(AssetEntry.CompressedSize));
        EnsureIdentityField(expected.Offset == actual.Offset, expected, nameof(AssetEntry.Offset));
        EnsureIdentityField(string.Equals(expected.StoragePath, actual.StoragePath, StringComparison.Ordinal), expected, nameof(AssetEntry.StoragePath));
        EnsureIdentityField(string.Equals(expected.SourceArchive, actual.SourceArchive, StringComparison.Ordinal), expected, nameof(AssetEntry.SourceArchive));
        EnsureIdentityField(string.Equals(expected.ContentDirectory, actual.ContentDirectory, StringComparison.Ordinal), expected, nameof(AssetEntry.ContentDirectory));
        EnsureIdentityField(string.Equals(expected.CsvUnknown, actual.CsvUnknown, StringComparison.Ordinal), expected, nameof(AssetEntry.CsvUnknown));
        EnsureIdentityField(string.Equals(expected.CsvMetadata, actual.CsvMetadata, StringComparison.Ordinal), expected, nameof(AssetEntry.CsvMetadata));
        EnsureIdentityField(expected.Pruned == actual.Pruned, expected, nameof(AssetEntry.Pruned));

        IReadOnlyList<SubAssetEntry>? expectedSubEntries = expected.SubEntries;
        IReadOnlyList<SubAssetEntry>? actualSubEntries = actual.SubEntries;
        EnsureIdentityField((expectedSubEntries is null) == (actualSubEntries is null), expected, nameof(AssetEntry.SubEntries));
        if (expectedSubEntries is null)
            return;

        EnsureIdentityField(expectedSubEntries.Count == actualSubEntries!.Count, expected, "SubEntries.Count");
        for (int index = 0; index < expectedSubEntries.Count; index++)
        {
            SubAssetEntry expectedSub = expectedSubEntries[index];
            SubAssetEntry actualSub = actualSubEntries[index];
            string prefix = $"SubEntries[{index}]";
            EnsureIdentityField(string.Equals(expectedSub.FileName, actualSub.FileName, StringComparison.Ordinal), expected, prefix + ".FileName");
            EnsureIdentityField(expectedSub.Offset == actualSub.Offset, expected, prefix + ".Offset");
            EnsureIdentityField(expectedSub.Size == actualSub.Size, expected, prefix + ".Size");
            EnsureIdentityField(expectedSub.CompressedSize == actualSub.CompressedSize, expected, prefix + ".CompressedSize");
            EnsureIdentityField(expectedSub.IsCompressed == actualSub.IsCompressed, expected, prefix + ".IsCompressed");
            EnsureIdentityField(expectedSub.IsGlobal == actualSub.IsGlobal, expected, prefix + ".IsGlobal");
            EnsureIdentityField(string.Equals(expectedSub.SourceArchive, actualSub.SourceArchive, StringComparison.Ordinal), expected, prefix + ".SourceArchive");
            EnsureIdentityField(string.Equals(expectedSub.ContentDirectory, actualSub.ContentDirectory, StringComparison.Ordinal), expected, prefix + ".ContentDirectory");
        }
    }

    private static void EnsureIdentityField(bool matches, AssetEntry parent, string field)
    {
        if (!matches)
            throw new InvalidDataException($"Modifications for asset '{parent.Path}' disagree on parent identity field '{field}'.");
    }

    private static void ValidateReplacementSubEntry(AssetEntry parent, SubAssetEntry canonical, SubAssetEntry candidate)
    {
        EnsureCanonicalSubEntryField(string.Equals(canonical.FileName, candidate.FileName, StringComparison.OrdinalIgnoreCase), parent, candidate, nameof(SubAssetEntry.FileName));
        EnsureCanonicalSubEntryField(canonical.Offset == candidate.Offset, parent, candidate, nameof(SubAssetEntry.Offset));
        EnsureCanonicalSubEntryField(canonical.Size == candidate.Size, parent, candidate, nameof(SubAssetEntry.Size));
        EnsureCanonicalSubEntryField(canonical.CompressedSize == candidate.CompressedSize, parent, candidate, nameof(SubAssetEntry.CompressedSize));
        EnsureCanonicalSubEntryField(canonical.IsCompressed == candidate.IsCompressed, parent, candidate, nameof(SubAssetEntry.IsCompressed));
        EnsureCanonicalSubEntryField(canonical.IsGlobal == candidate.IsGlobal, parent, candidate, nameof(SubAssetEntry.IsGlobal));
        EnsureCanonicalSubEntryField(string.Equals(canonical.SourceArchive, candidate.SourceArchive, StringComparison.Ordinal), parent, candidate, nameof(SubAssetEntry.SourceArchive));
        EnsureCanonicalSubEntryField(string.Equals(canonical.ContentDirectory, candidate.ContentDirectory, StringComparison.Ordinal), parent, candidate, nameof(SubAssetEntry.ContentDirectory));
    }

    private static void EnsureCanonicalSubEntryField(bool matches, AssetEntry parent, SubAssetEntry candidate, string field)
    {
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Subfile '{candidate.FileName}' does not match canonical parent subentry for asset '{parent.Path}' field '{field}'.");
        }
    }

    private static ParentPayload RebuildParent(
        AssetEntry parent,
        IReadOnlyList<SubAssetEntry> subEntries,
        IReadOnlyDictionary<string, ModifiedAssetEntry> replacements,
        byte[] originalParent)
    {
        using var rebuilt = new MemoryStream();
        var rebuiltSubEntries = new List<NormalizedSubEntry>(subEntries.Count);
        foreach (SubAssetEntry sub in subEntries)
        {
            if (!sub.IsGlobal &&
                (sub.Offset < 0 || sub.Size < 0 || sub.Offset > originalParent.LongLength ||
                 sub.Size > originalParent.LongLength - sub.Offset))
                throw new InvalidDataException($"Subfile '{sub.FileName}' exceeds parent '{parent.Path}' bounds.");

            long newOffset = rebuilt.Position;
            byte[] bytes;
            if (replacements.TryGetValue(sub.FileName, out ModifiedAssetEntry? replacement))
            {
                bytes = replacement.ModifiedData;
            }
            else if (sub.IsGlobal)
            {
                bytes = AssetExtractor.GetSubBlob(sub, parent, sub.ContentDirectory);
            }
            else
            {
                bytes = originalParent.AsSpan(checked((int)sub.Offset), checked((int)sub.Size)).ToArray();
            }

            rebuilt.Write(bytes);
            rebuiltSubEntries.Add(new NormalizedSubEntry(sub.FileName, newOffset, bytes.LongLength, sub.IsGlobal));
        }

        return ParentPayload.TakeOwnership(parent, rebuilt.ToArray(), rebuiltSubEntries.ToArray());
    }

    private static NormalizedSubEntry[] BuildWholeParentSubEntries(AssetEntry parent, long dataLength)
    {
        if (parent.SubEntries is null || parent.SubEntries.Count == 0)
            return [new NormalizedSubEntry(parent.FileName, 0, dataLength)];

        foreach (SubAssetEntry sub in parent.SubEntries)
        {
            long normalizedOffset = sub.IsGlobal
                ? checked(sub.Offset - (parent.Offset ?? throw new InvalidDataException(
                    $"Global subfile '{sub.FileName}' has no parent DAT offset.")))
                : sub.Offset;
            if (normalizedOffset < 0 || sub.Size < 0 || normalizedOffset > dataLength ||
                sub.Size > dataLength - normalizedOffset)
                throw new InvalidDataException($"Replacement parent '{parent.Path}' does not contain subfile '{sub.FileName}'.");
        }

        return parent.SubEntries
            .Select(sub => new NormalizedSubEntry(
                sub.FileName,
                sub.IsGlobal
                    ? checked(sub.Offset - (parent.Offset ?? throw new InvalidDataException(
                        $"Global subfile '{sub.FileName}' has no parent DAT offset.")))
                    : sub.Offset,
                sub.Size,
                sub.IsGlobal))
            .ToArray();
    }

    private static byte[] BuildTextureParent(
        AssetEntry parent,
        TexturePackageImportResult splitTexture,
        out NormalizedSubEntry[] subEntries)
    {
        if (parent.SubEntries is null || parent.SubEntries.Count == 0)
            throw new InvalidOperationException($"Texture '{parent.Path}' has no subfiles.");

        using var rebuilt = new MemoryStream();
        var entries = new List<NormalizedSubEntry>(parent.SubEntries.Count);
        foreach (SubAssetEntry sub in parent.SubEntries)
        {
            byte[] bytes;
            if (sub.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase))
            {
                bytes = splitTexture.HeaderBlob;
            }
            else if (sub.FileName.StartsWith("mip", StringComparison.OrdinalIgnoreCase))
            {
                int mipIndex = ParseMipIndex(sub.FileName);
                if (mipIndex >= splitTexture.MipPayloads.Count)
                    throw new InvalidDataException($"Texture '{parent.Path}' is missing mip payload {mipIndex} in imported DDS.");
                bytes = splitTexture.MipPayloads[mipIndex];
            }
            else
            {
                bytes = AssetExtractor.GetSubBlob(sub, parent, sub.ContentDirectory);
            }

            long offset = rebuilt.Position;
            rebuilt.Write(bytes);
            entries.Add(new NormalizedSubEntry(sub.FileName, offset, bytes.LongLength,
                sub.FileName.StartsWith("mip", StringComparison.OrdinalIgnoreCase)));
        }

        subEntries = entries.ToArray();
        return rebuilt.ToArray();
    }

    private static int ParseMipIndex(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (!stem.StartsWith("mip", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(stem[3..], out int mipIndex) ||
            mipIndex < 0)
        {
            throw new InvalidDataException($"Unable to parse mip index from '{fileName}'.");
        }

        return mipIndex;
    }
}
