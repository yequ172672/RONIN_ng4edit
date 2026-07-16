using System.Text;
using YakumoLib.Formats;
using YakumoLib.Assets;

namespace YakumoLib.Modding;

/// <summary>Builds verified patch CSV/DAT pairs without modifying original image archives.</summary>
public static class PatchGenerator
{
    private const string Header = "header,2,0,";
    private static readonly object WriteLock = new();

    public static string GeneratePatch(
        string gameRoot,
        IReadOnlyList<ModifiedAssetEntry> modifiedAssets,
        bool compressData)
    {
        if (modifiedAssets is null || modifiedAssets.Count == 0)
            throw new InvalidOperationException("No modified assets to patch.");
        if (compressData || modifiedAssets.Any(asset => asset.Compress))
            throw new NotSupportedException("GDeflate compression is unavailable. Disable compression before generating a patch.");

        // The API receives the game Assets root, not the installation root. Keep this
        // explicit so callers do not accidentally create Backups beside the executable.
        string assetsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if (!Directory.Exists(assetsDirectory))
            throw new DirectoryNotFoundException($"Patch destination does not exist: '{assetsDirectory}'.");

        List<ParentPatch> patches = BuildParentPatches(modifiedAssets);

        lock (WriteLock)
        {
            string patchId = GetLatestPatchArchiveId(assetsDirectory);
            string csvPath = Path.Combine(assetsDirectory, $"@{patchId}.csv");
            string datPath = Path.Combine(assetsDirectory, $"@{patchId}.dat");
            AppendToPatchArchive(csvPath, datPath, patches, new BackupManager(assetsDirectory));
            return patchId;
        }
    }

    private static List<ParentPatch> BuildParentPatches(IReadOnlyList<ModifiedAssetEntry> modifications)
    {
        var result = new List<ParentPatch>();
        foreach (var group in modifications.GroupBy(item => item.ParentEntry.AssetID))
        {
            ModifiedAssetEntry first = group.First();
            AssetEntry parent = first.ParentEntry;
            if (group.Any(item => !string.Equals(item.ParentEntry.Path, parent.Path, StringComparison.Ordinal)))
                throw new InvalidDataException("Modifications with the same asset ID disagree on the parent path.");

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
                    byte[] textureParent = BuildTextureParent(parent, splitTexture, out IReadOnlyList<RebuiltSubEntry> textureSubEntries);
                    result.Add(new ParentPatch(parent, textureParent, textureSubEntries));
                    continue;
                }

                if (parent.SubEntries?.Any(sub => sub.IsGlobal) == true)
                    throw new NotSupportedException($"Global subfiles for '{parent.Path}' have not been format-verified and are rejected.");

                result.Add(new ParentPatch(parent, wholeParent.ModifiedData.ToArray(), BuildWholeParentSubEntries(parent, wholeParent.ModifiedData.LongLength)));
                continue;
            }

            IReadOnlyList<SubAssetEntry> subEntries = parent.SubEntries ?? [];
            if (subEntries.Count == 0)
                throw new InvalidOperationException($"Asset '{parent.Path}' has no local subfiles to replace.");
            if (subEntries.Any(sub => sub.IsGlobal) || group.Any(item => item.SubEntry!.IsGlobal))
                throw new NotSupportedException($"Global subfiles for '{parent.Path}' have not been format-verified and are rejected.");

            var replacements = new Dictionary<string, ModifiedAssetEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ModifiedAssetEntry modification in group)
            {
                SubAssetEntry sub = modification.SubEntry!;
                if (!subEntries.Any(candidate => ReferenceEquals(candidate, sub) ||
                        string.Equals(candidate.FileName, sub.FileName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Subfile '{sub.FileName}' does not belong to '{parent.Path}'.");
                replacements[sub.FileName] = modification;
            }

            string sourceDirectory = subEntries.Select(sub => sub.ContentDirectory)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                ?? parent.ContentDirectory
                ?? throw new InvalidOperationException($"No archive directory is known for '{parent.Path}'.");
            byte[] originalParent = AssetExtractor.GetParentBlob(parent, sourceDirectory);
            result.Add(RebuildParent(parent, subEntries, replacements, originalParent));
        }
        return result;
    }

    private static ParentPatch RebuildParent(
        AssetEntry parent,
        IReadOnlyList<SubAssetEntry> subEntries,
        IReadOnlyDictionary<string, ModifiedAssetEntry> replacements,
        byte[] originalParent)
    {
        using var rebuilt = new MemoryStream();
        var rebuiltSubEntries = new List<RebuiltSubEntry>(subEntries.Count);
        foreach (SubAssetEntry sub in subEntries)
        {
            if (sub.Offset < 0 || sub.Size < 0 || sub.Offset > originalParent.LongLength || sub.Size > originalParent.LongLength - sub.Offset)
                throw new InvalidDataException($"Subfile '{sub.FileName}' exceeds parent '{parent.Path}' bounds.");

            long newOffset = rebuilt.Position;
            byte[] bytes = replacements.TryGetValue(sub.FileName, out ModifiedAssetEntry? replacement)
                ? replacement.ModifiedData
                : originalParent.AsSpan(checked((int)sub.Offset), checked((int)sub.Size)).ToArray();
            rebuilt.Write(bytes);
            rebuiltSubEntries.Add(new RebuiltSubEntry(sub.FileName, newOffset, bytes.LongLength));
        }

        return new ParentPatch(parent, rebuilt.ToArray(), rebuiltSubEntries);
    }

    private static IReadOnlyList<RebuiltSubEntry> BuildWholeParentSubEntries(AssetEntry parent, long dataLength)
    {
        if (parent.SubEntries is null || parent.SubEntries.Count == 0)
            return [new RebuiltSubEntry(parent.FileName, 0, dataLength)];

        foreach (SubAssetEntry sub in parent.SubEntries)
        {
            if (sub.Offset < 0 || sub.Size < 0 || sub.Offset > dataLength || sub.Size > dataLength - sub.Offset)
                throw new InvalidDataException($"Replacement parent '{parent.Path}' does not contain subfile '{sub.FileName}'.");
        }
        return parent.SubEntries.Select(sub => new RebuiltSubEntry(sub.FileName, sub.Offset, sub.Size)).ToList();
    }

    private static byte[] BuildTextureParent(
        AssetEntry parent,
        TexturePackageImportResult splitTexture,
        out IReadOnlyList<RebuiltSubEntry> subEntries)
    {
        if (parent.SubEntries is null || parent.SubEntries.Count == 0)
            throw new InvalidOperationException($"Texture '{parent.Path}' has no subfiles.");

        using var rebuilt = new MemoryStream();
        var entries = new List<RebuiltSubEntry>(parent.SubEntries.Count);
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
            entries.Add(new RebuiltSubEntry(sub.FileName, offset, bytes.LongLength,
                sub.FileName.StartsWith("mip", StringComparison.OrdinalIgnoreCase)));
        }

        subEntries = entries;
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

    private static void AppendToPatchArchive(string csvPath, string datPath, IReadOnlyList<ParentPatch> patches,
        BackupManager backupManager)
    {
        if (!File.Exists(csvPath) || !File.Exists(datPath))
            throw new FileNotFoundException("The game patch archive requires both CSV and DAT files.");
        byte[] originalCsv = File.ReadAllBytes(csvPath);
        string[] existingLines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (existingLines.Length == 0 || existingLines[0] != Header)
            throw new InvalidDataException($"Patch archive '{csvPath}' has an unexpected header.");
        long originalDatLength = new FileInfo(datPath).Length;
        string temporaryCsv = csvPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool csvCommitted = false;
        var appended = new List<(ParentPatch Patch, long Offset)>();
        try
        {
            using (var dat = new FileStream(datPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                dat.Position = dat.Length;
                foreach (ParentPatch patch in patches)
                {
                    if (string.IsNullOrWhiteSpace(patch.Parent.StoragePath))
                        throw new InvalidDataException($"Asset '{patch.Parent.Path}' has no game CSV storage path.");
                    long offset = dat.Position;
                    dat.Write(patch.Data);
                    appended.Add((patch, offset));
                }
                dat.Flush(flushToDisk: true);
            }

            HashSet<string> replacedPaths = appended.Select(item => item.Patch.Parent.StoragePath!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outputLines = existingLines.Skip(1)
                .Where(line => !replacedPaths.Contains(line.Split(',', 2)[0]))
                .Prepend(Header).ToList();
            outputLines.AddRange(appended.Select(item => BuildCsvRow(item.Patch, item.Offset)));
            using (var csv = new StreamWriter(new FileStream(temporaryCsv, FileMode.CreateNew, FileAccess.Write, FileShare.None),
                       new UTF8Encoding(false)))
            {
                foreach (string line in outputLines) csv.WriteLine(line);
                csv.Flush();
                ((FileStream)csv.BaseStream).Flush(flushToDisk: true);
            }
            VerifyAppendedArchive(temporaryCsv, datPath, appended);

            BackupAndVerifyIfPresent(backupManager, csvPath);
            File.Move(temporaryCsv, csvPath, overwrite: true);
            csvCommitted = true;
            VerifyAppendedArchive(csvPath, datPath, appended);
        }
        catch
        {
            using (var dat = new FileStream(datPath, FileMode.Open, FileAccess.Write, FileShare.None))
                dat.SetLength(originalDatLength);
            if (csvCommitted) File.WriteAllBytes(csvPath, originalCsv);
            File.Delete(temporaryCsv);
            throw;
        }
    }

    private static string BuildCsvRow(ParentPatch patch, long offset)
    {
        string subEntries = string.Join('/', patch.SubEntries.SelectMany(sub =>
            new[]
            {
                sub.FileName,
                sub.IsGlobal ? checked(offset + sub.Offset).ToString() : "0",
                sub.IsGlobal ? "0" : sub.Offset.ToString(),
                sub.Size.ToString(),
                "0"
            }));
        return $"{patch.Parent.StoragePath},{offset},{patch.Parent.CsvUnknown ?? "0"},{patch.Data.LongLength},0," +
               $"{patch.SubEntries.Count},{patch.Parent.CsvMetadata ?? "0"},{subEntries}";
    }

    private static void VerifyAppendedArchive(string csvPath, string datPath,
        IReadOnlyList<(ParentPatch Patch, long Offset)> expected)
    {
        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length == 0 || lines[0] != Header)
            throw new InvalidDataException("Patch CSV could not be reloaded with the game-native header.");
        using var dat = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach ((ParentPatch patch, long expectedOffset) in expected)
        {
            string expectedPath = patch.Parent.StoragePath!;
            string line = lines.LastOrDefault(value => value.StartsWith(expectedPath + ",", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Patch CSV is missing '{expectedPath}'.");
            string[] columns = line.Split(',', 8);
            if (columns.Length != 8 || columns[0] != expectedPath ||
                !long.TryParse(columns[1], out long offset) ||
                !long.TryParse(columns[3], out long size) ||
                !long.TryParse(columns[4], out long compressedSize) ||
                offset != expectedOffset || size != patch.Data.LongLength || compressedSize != 0)
                throw new InvalidDataException($"Patch CSV row for '{expectedPath}' failed reload validation.");

            dat.Position = offset;
            byte[] extracted = new byte[patch.Data.Length];
            dat.ReadExactly(extracted);
            if (!extracted.AsSpan().SequenceEqual(patch.Data))
                throw new InvalidDataException($"Byte verification failed for '{patch.Parent.Path}'.");
        }
    }

    private static void BackupAndVerifyIfPresent(BackupManager manager, string path)
    {
        if (!File.Exists(path))
            return;
        string backupPath = manager.Backup(path) ?? throw new IOException($"Backup failed for '{path}'.");
        if (!string.Equals(BackupManager.ComputeSha256(path), BackupManager.ComputeSha256(backupPath), StringComparison.Ordinal))
            throw new IOException($"Backup SHA-256 verification failed for '{path}'.");
    }

    private static string GetLatestPatchArchiveId(string assetsDirectory)
    {
        int[] indices = Directory.EnumerateFiles(assetsDirectory, "@patch_image*.csv")
            .Select(path => Path.GetFileNameWithoutExtension(path)["@patch_image".Length..])
            .Select(text => int.TryParse(text, out int index) ? index : -1).Where(index => index >= 0).ToArray();
        if (indices.Length == 0)
            throw new FileNotFoundException("No game @patch_imageN.csv archive exists in the Assets directory.");
        return $"patch_image{indices.Max()}";
    }

    private sealed record ParentPatch(AssetEntry Parent, byte[] Data, IReadOnlyList<RebuiltSubEntry> SubEntries);
    private sealed record RebuiltSubEntry(string FileName, long Offset, long Size, bool IsGlobal = false);
}
