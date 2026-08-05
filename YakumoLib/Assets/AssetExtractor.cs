using YakumoLib.DEFLATE;

namespace YakumoLib.Assets;

public static class AssetExtractor
{
    private static byte[] ReadExact(FileStream stream, int size)
    {
        if (size < 0)
            throw new InvalidDataException("A negative asset size was requested.");

        var buffer = new byte[size];
        int totalRead = 0;
        while (totalRead < size)
        {
            int read = stream.Read(buffer, totalRead, size - totalRead);
            if (read == 0)
                throw new EndOfStreamException($"Unexpected EOF after {totalRead} of {size} bytes.");
            totalRead += read;
        }
        return buffer;
    }

    public static byte[] GetParentBlob(AssetEntry parent, string archiveRootDirectory)
    {
        if (parent.Offset is null || parent.Size is null || parent.SourceArchive is null)
            throw new InvalidOperationException($"Asset {parent.Path} has been pruned.");

        string archivePath = ResolveArchivePath(archiveRootDirectory, parent.SourceArchive);
        long compressedSize = parent.CompressedSize ?? 0;
        long storedSize = compressedSize != 0 ? compressedSize : parent.Size.Value;
        ValidateFileRange(archivePath, parent.Offset.Value, storedSize, parent.Path);

        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = parent.Offset.Value;
        byte[] raw = ReadExact(stream, CheckedArrayLength(storedSize, parent.Path));
        if (compressedSize == 0)
            return raw;

        byte[] output = new byte[CheckedArrayLength(parent.Size.Value, parent.Path)];
        Decompress(raw, output, parent.Path);
        return output;
    }

    public static byte[] GetSubBlob(SubAssetEntry sub, AssetEntry parent, string archiveRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(sub.SourceArchive))
            throw new InvalidOperationException($"Asset {sub.FileName} has been pruned.");
        if (sub.Offset < 0 || sub.Size < 0 || sub.CompressedSize < 0)
            throw new InvalidDataException($"Sub-asset '{sub.FileName}' has a negative offset or size.");

        if (!sub.IsGlobal)
        {
            byte[] parentData = GetParentBlob(parent, archiveRootDirectory);
            ValidateBufferRange(parentData.LongLength, sub.Offset, sub.Size, sub.FileName);
            return parentData.AsSpan(CheckedArrayLength(sub.Offset, sub.FileName), CheckedArrayLength(sub.Size, sub.FileName)).ToArray();
        }

        string archivePath = ResolveArchivePath(archiveRootDirectory, sub.SourceArchive);
        long storedSize = sub.CompressedSize != 0 ? sub.CompressedSize : sub.Size;
        ValidateFileRange(archivePath, sub.Offset, storedSize, sub.FileName);

        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = sub.Offset;
        byte[] raw = ReadExact(stream, CheckedArrayLength(storedSize, sub.FileName));
        if (sub.CompressedSize == 0)
            return raw;

        byte[] output = new byte[CheckedArrayLength(sub.Size, sub.FileName)];
        Decompress(raw, output, sub.FileName);
        return output;
    }

    public static void ExtractAll(AssetEntry target, string outputDir)
    {
        string outputRoot = Path.GetFullPath(outputDir);
        string packageRoot = ResolveOutputPath(outputRoot, Path.GetFileNameWithoutExtension(target.FileName));
        Directory.CreateDirectory(packageRoot);

        foreach (SubAssetEntry sub in target.SubEntries ?? [])
        {
            byte[] data = GetSubBlob(sub, target, sub.ContentDirectory ?? string.Empty);
            string outputPath = ResolveOutputPath(packageRoot, sub.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, data);
        }
    }

    private static string ResolveArchivePath(string rootDirectory, string sourceArchive)
    {
        string root = Path.GetFullPath(rootDirectory);
        string candidate = Path.GetFullPath(Path.Combine(root, sourceArchive + ".dat"));
        EnsureWithinRoot(root, candidate, "Archive path");
        return candidate;
    }

    private static string ResolveOutputPath(string rootDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Export path '{relativePath}' must be relative.");

        string candidate = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        EnsureWithinRoot(rootDirectory, candidate, "Export path");
        return candidate;
    }

    private static void EnsureWithinRoot(string rootDirectory, string candidate, string label)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
            return;

        string prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} escapes its configured root: '{candidate}'.");
    }

    private static void ValidateFileRange(string path, long offset, long size, string assetName)
    {
        if (offset < 0 || size < 0)
            throw new InvalidDataException($"Asset '{assetName}' has a negative offset or size.");

        long length = new FileInfo(path).Length;
        ValidateBufferRange(length, offset, size, assetName);
    }

    private static void ValidateBufferRange(long length, long offset, long size, string assetName)
    {
        if (offset < 0 || size < 0 || offset > length || size > length - offset)
            throw new InvalidDataException($"Asset '{assetName}' range [{offset}, {offset + size}) exceeds source length {length}.");
    }

    private static int CheckedArrayLength(long value, string assetName)
    {
        if (value < 0 || value > int.MaxValue)
            throw new InvalidDataException($"Asset '{assetName}' size {value} cannot be represented in memory.");
        return (int)value;
    }

    private static unsafe void Decompress(byte[] raw, byte[] output, string assetName)
    {
        fixed (byte* source = raw)
        fixed (byte* destination = output)
        {
            int result = DeflateSharp.GDeflate_Decompress(
                (IntPtr)source, (nuint)raw.Length,
                (IntPtr)destination, (nuint)output.Length,
                numWorkers: 1);
            if (result != 0)
                throw new InvalidDataException($"GDeflate decompression failed for '{assetName}' (code {result}).");
        }
    }
}
