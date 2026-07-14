using System.Text;
using YakumoLib.Assets;
using YakumoLib.DEFLATE;

namespace YakumoLib.Modding;

/// <summary>
/// Generates @patch_*.csv and @patch_*.dat files in the game directory
/// representing only the changed assets, without modifying original archives.
/// </summary>
public static class PatchGenerator
{
    private static readonly object _writeLock = new();

    /// <summary>
    /// Generate patch CSV and DAT files for all modified assets.
    /// </summary>
    /// <param name="gameRoot">Absolute path to the game's Assets directory.</param>
    /// <param name="modifiedAssets">List of assets with modified data.</param>
    /// <param name="compressData">If true, compress the modified data using GDeflate.</param>
    /// <returns>The patch identifier string (e.g., "patch_1").</returns>
    public static string GeneratePatch(
        string gameRoot,
        IReadOnlyList<ModifiedAssetEntry> modifiedAssets,
        bool compressData)
    {
        if (modifiedAssets is null || modifiedAssets.Count == 0)
            throw new InvalidOperationException("No modified assets to patch.");

        string assetsDir = Path.GetFullPath(gameRoot);
        int patchIndex = GetNextPatchIndex(assetsDir);
        string patchId = $"patch_{patchIndex}";

        string csvPath = Path.Combine(assetsDir, $"@{patchId}.csv");
        string datPath = Path.Combine(assetsDir, $"@{patchId}.dat");

        // Build the CSV and DAT content in memory
        var csvLines = new List<string> { "Path,Offset,Unknown,Size,CompressedSize,FileCount,Unknown2,SubEntries" };
        var dataBlocks = new List<(long Offset, long Size, long CompressedSize, string SubEntryCsv)>();

        using var datStream = new MemoryStream();

        foreach (var modified in modifiedAssets)
        {
            string path = modified.ParentEntry.Path;
            byte[] data = modified.ModifiedData;
            byte[] compressedData;

            if (compressData)
            {
                compressedData = CompressData(data);
            }
            else
            {
                compressedData = Array.Empty<byte>();
            }

            long offset = datStream.Position;
            long size = data.Length;
            long compressedSize = compressData ? compressedData.Length : 0;

            // Write raw (or compressed) data to the DAT stream
            if (compressData && compressedData.Length > 0)
            {
                datStream.Write(compressedData, 0, compressedData.Length);
            }
            else
            {
                datStream.Write(data, 0, data.Length);
            }

            // Build sub-entry CSV string
            string subEntryCsv = BuildSubEntryCsv(modified, data);

            csvLines.Add(
                $"{path}," +
                $"{offset}," +
                $"0," +
                $"{size}," +
                $"{compressedSize}," +
                $"{(modified.ParentEntry.SubEntries?.Count ?? 1)}," +
                $"0," +
                $"{subEntryCsv}");
        }

        // Write files
        lock (_writeLock)
        {
            // Write CSV
            File.WriteAllLines(csvPath, csvLines, Encoding.UTF8);

            // Write DAT
            var datArray = datStream.ToArray();
            File.WriteAllBytes(datPath, datArray);
        }

        return patchId;
    }

    private static int GetNextPatchIndex(string assetsDir)
    {
        int maxIndex = 0;
        if (!Directory.Exists(assetsDir))
            return 1;

        foreach (var file in Directory.EnumerateFiles(assetsDir, "@patch_*.csv"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            // name is like "@patch_1"
            string indexPart = name.Replace("@patch_", "");
            if (int.TryParse(indexPart, out int idx) && idx > maxIndex)
                maxIndex = idx;
        }

        return maxIndex + 1;
    }

    private static string BuildSubEntryCsv(ModifiedAssetEntry modified, byte[] data)
    {
        var sb = new StringBuilder();
        var parent = modified.ParentEntry;

        if (parent.SubEntries is not null && parent.SubEntries.Count > 0)
        {
            foreach (var sub in parent.SubEntries)
            {
                if (modified.SubEntry is not null &&
                    !string.Equals(sub.FileName, modified.SubEntry.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    // Unmodified subfile — keep original offset/size
                    long subOffset = sub.IsGlobal ? sub.Offset : 0;
                    long subLocalOffset = sub.IsGlobal ? 0 : sub.Offset;
                    sb.Append($"{sub.FileName}/{subOffset}/{subLocalOffset}/{sub.Size}/{sub.CompressedSize}/");
                }
                else
                {
                    // This subfile was modified
                    long newOffset = modified.SubEntry?.IsGlobal == true ? 0 : 0;
                    // For patch files, modified data is stored at the parent offset in the new .dat
                    // The offset within the patch .dat is determined by the parent's offset
                    sb.Append($"{sub.FileName}/0/0/{data.Length}/0/");
                }
            }
        }
        else
        {
            sb.Append($"{parent.FileName}/0/0/{data.Length}/0/");
        }

        return sb.ToString().TrimEnd('/');
    }

    private static byte[] CompressData(byte[] data)
    {
        try
        {
            int maxCompressedSize = data.Length + 1024;
            var compressed = new byte[maxCompressedSize];

            unsafe
            {
                fixed (byte* srcPtr = data)
                fixed (byte* dstPtr = compressed)
                {
                    int result = DeflateSharp.GDeflate_Compress(
                        (IntPtr)srcPtr, (nuint)data.Length,
                        (IntPtr)dstPtr, (nuint)maxCompressedSize,
                        numWorkers: 1);

                    if (result > 0 && result < maxCompressedSize)
                    {
                        var resultArray = new byte[result];
                        Array.Copy(compressed, resultArray, result);
                        return resultArray;
                    }
                }
            }

            return Array.Empty<byte>();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
