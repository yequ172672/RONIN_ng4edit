using System.Text;
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

        IReadOnlyList<ParentPayload> patches = ParentPayloadBuilder.BuildAll(modifiedAssets);

        lock (WriteLock)
        {
            string patchId = GetLatestPatchArchiveId(assetsDirectory);
            string csvPath = Path.Combine(assetsDirectory, $"@{patchId}.csv");
            string datPath = Path.Combine(assetsDirectory, $"@{patchId}.dat");
            AppendToPatchArchive(csvPath, datPath, patches, new BackupManager(assetsDirectory));
            return patchId;
        }
    }

    private static void AppendToPatchArchive(string csvPath, string datPath, IReadOnlyList<ParentPayload> patches,
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
        var appended = new List<(ParentPayload Patch, long Offset)>();
        try
        {
            using (var dat = new FileStream(datPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                dat.Position = dat.Length;
                foreach (ParentPayload patch in patches)
                {
                    if (string.IsNullOrWhiteSpace(patch.Parent.StoragePath))
                        throw new InvalidDataException($"Asset '{patch.Parent.Path}' has no game CSV storage path.");
                    long offset = dat.Position;
                    dat.Write(patch.Data.Span);
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

    private static string BuildCsvRow(ParentPayload patch, long offset)
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
        int localFileCount = patch.SubEntries.Count(sub => !sub.IsGlobal);
        return $"{patch.Parent.StoragePath},{offset},{patch.Parent.CsvUnknown ?? "0"},{patch.Data.Length},0," +
               $"{localFileCount},{patch.Parent.CsvMetadata ?? "0"},{subEntries}";
    }

    private static void VerifyAppendedArchive(string csvPath, string datPath,
        IReadOnlyList<(ParentPayload Patch, long Offset)> expected)
    {
        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length == 0 || lines[0] != Header)
            throw new InvalidDataException("Patch CSV could not be reloaded with the game-native header.");
        using var dat = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach ((ParentPayload patch, long expectedOffset) in expected)
        {
            string expectedPath = patch.Parent.StoragePath!;
            string line = lines.LastOrDefault(value => value.StartsWith(expectedPath + ",", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Patch CSV is missing '{expectedPath}'.");
            string[] columns = line.Split(',', 8);
            if (columns.Length != 8 || columns[0] != expectedPath ||
                !long.TryParse(columns[1], out long offset) ||
                !long.TryParse(columns[3], out long size) ||
                !long.TryParse(columns[4], out long compressedSize) ||
                offset != expectedOffset || size != patch.Data.Length || compressedSize != 0)
                throw new InvalidDataException($"Patch CSV row for '{expectedPath}' failed reload validation.");

            dat.Position = offset;
            byte[] extracted = new byte[patch.Data.Length];
            dat.ReadExactly(extracted);
            if (!extracted.AsSpan().SequenceEqual(patch.Data.Span))
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
}
