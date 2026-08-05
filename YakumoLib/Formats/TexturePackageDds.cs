using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YakumoLib.Assets;

namespace YakumoLib.Formats;

public sealed record TexturePackageDdsData(
    byte[] DdsBytes,
    int Width,
    int Height,
    int MipCount,
    IReadOnlyList<SubAssetEntry> MipEntries,
    int HeaderLength,
    int TotalSize);

public static class TexturePackageDds
{
    private const int DdsMagicLength = 4;
    private const int DdsHeaderLength = 124;
    private const int DdsHeaderWithMagicLength = DdsMagicLength + DdsHeaderLength;
    private const int DdsDx10HeaderLength = 20;
    private const int DdsPixelFormatOffset = 76;
    private const int DdsCapsOffset = 108;
    private const int DdsCaps2Offset = 112;
    private const uint DdsMagic = 0x20534444;
    private const uint DdsHeaderSize = 124;
    private const uint DdsPixelFormatSize = 32;
    private const uint DdsRequiredFlags = 0x00001007; // CAPS|HEIGHT|WIDTH|PIXELFORMAT
    private const uint DdsPitchFlag = 0x8;
    private const uint DdsLinearSizeFlag = 0x80000;
    private const uint DdsRequiredCaps = 0x1000;
    private const int TexturePackageHeaderLength = 8;
    private const int TexturePackageMipInfoLength = 8;

    public static TexturePackageDdsData Extract(AssetEntry entry, string archiveRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRootDirectory);

        if (entry.Type != AssetType.Texture)
            throw new InvalidOperationException($"Asset '{entry.Path}' is not a texture package.");

        if (entry.SubEntries is null || entry.SubEntries.Count == 0)
            throw new InvalidDataException($"Texture '{entry.Path}' has no subfiles.");

        var imageHeaderEntry = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Texture '{entry.Path}' is missing Image.img.");

        byte[] headerBlob = AssetExtractor.GetSubBlob(imageHeaderEntry, entry, imageHeaderEntry.ContentDirectory);
        return Assemble(entry, headerBlob, archiveRootDirectory);
    }

    public static TexturePackageDdsData Assemble(AssetEntry entry, byte[] headerBlob, string archiveRootDirectory)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(headerBlob);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRootDirectory);

        using var ms = new MemoryStream(headerBlob, writable: false);
        using var reader = new BinaryReader(ms);

        if (headerBlob.Length < TexturePackageHeaderLength + DdsHeaderWithMagicLength)
            throw new InvalidDataException($"Texture header blob too small ({headerBlob.Length} bytes).");

        uint totalSize = reader.ReadUInt32();
        uint mipMapCount = reader.ReadUInt32();
        if (mipMapCount == 0)
            throw new InvalidDataException("Texture package declared zero mip levels.");

        long mipTableLength = checked((long)mipMapCount * TexturePackageMipInfoLength);
        long requiredHeaderBytes = TexturePackageHeaderLength + mipTableLength + DdsHeaderWithMagicLength;
        if (headerBlob.Length < requiredHeaderBytes)
            throw new InvalidDataException($"Texture header blob too small for {mipMapCount} mip descriptors.");

        var expectedMipCapacities = new int[mipMapCount];
        uint expectedOffset = 0;
        for (int i = 0; i < mipMapCount; i++)
        {
            uint mipOffset = reader.ReadUInt32();
            uint mipCapacity = reader.ReadUInt32();
            if (mipOffset != expectedOffset)
                throw new InvalidDataException($"Texture mip descriptor {i} offset mismatch. Expected {expectedOffset}, got {mipOffset}.");
            if (mipCapacity == 0)
                throw new InvalidDataException($"Texture mip descriptor {i} has zero size.");
            expectedMipCapacities[i] = checked((int)mipCapacity);
            expectedOffset = checked(mipOffset + mipCapacity);
        }

        int ddsHeaderLength = GetDdsHeaderLength(headerBlob.AsSpan(checked((int)reader.BaseStream.Position)));
        byte[] ddsHeader = reader.ReadBytes(ddsHeaderLength);
        if (ddsHeader.Length != ddsHeaderLength)
            throw new InvalidDataException("Texture package is missing the full DDS header.");

        ValidateDdsHeader(ddsHeader, (uint)totalSize, mipMapCount, out int width, out int height, out int ddsMipCount, false,
            allowMissingPitchMetadata: true);
        if (ddsMipCount != mipMapCount)
            throw new InvalidDataException($"DDS mip count mismatch. Package={mipMapCount}, DDS={ddsMipCount}.");
        int[] logicalMipLengths = BuildBlockCompressedMipLengths(ddsHeader, width, height, ddsMipCount);
        int logicalTotalSize = checked(ddsHeaderLength + logicalMipLengths.Sum());
        if (logicalTotalSize != totalSize)
            throw new InvalidDataException($"Texture logical DDS size mismatch. Package={totalSize}, calculated={logicalTotalSize}.");
        uint ddsFlags = BinaryPrimitives.ReadUInt32LittleEndian(ddsHeader.AsSpan(8, 4));
        uint pitchOrLinearSize = BinaryPrimitives.ReadUInt32LittleEndian(ddsHeader.AsSpan(20, 4));
        if ((ddsFlags & (DdsPitchFlag | DdsLinearSizeFlag)) == 0 && pitchOrLinearSize == 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(ddsHeader.AsSpan(8, 4), ddsFlags | DdsLinearSizeFlag);
            BinaryPrimitives.WriteUInt32LittleEndian(ddsHeader.AsSpan(20, 4), checked((uint)logicalMipLengths[0]));
        }

        var mipEntries = new List<SubAssetEntry>(checked((int)mipMapCount));
        using var output = new MemoryStream(checked((int)totalSize));
        output.Write(ddsHeader);

        for (int i = 0; i < mipMapCount; i++)
        {
            var mipEntry = entry.SubEntries?.FirstOrDefault(s => s.FileName.Equals($"mip{i}.img", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Texture '{entry.Path}' is missing mip{i}.img.");

            byte[] mipBytes = AssetExtractor.GetSubBlob(mipEntry, entry, mipEntry.ContentDirectory);
            if (mipBytes.Length != expectedMipCapacities[i])
                throw new InvalidDataException($"Texture '{entry.Path}' mip{i}.img capacity mismatch. Expected {expectedMipCapacities[i]}, got {mipBytes.Length}.");
            if (logicalMipLengths[i] > mipBytes.Length)
                throw new InvalidDataException($"Texture '{entry.Path}' mip{i}.img is smaller than its logical DDS payload. Required {logicalMipLengths[i]}, got {mipBytes.Length}.");

            output.Write(mipBytes, 0, logicalMipLengths[i]);
            mipEntries.Add(mipEntry);
        }

        byte[] ddsBytes = output.ToArray();

        return new TexturePackageDdsData(ddsBytes, width, height, ddsMipCount, mipEntries, ddsHeaderLength, checked((int)totalSize));
    }

    public static byte[] CreateHeaderBlob(byte[] ddsBytes)
    {
        var split = SplitForImport(ddsBytes);
        return split.HeaderBlob;
    }

    public static TexturePackageDdsData ExtractRootMip(AssetEntry entry, string archiveRootDirectory)
    {
        TexturePackageDdsData full = Extract(entry, archiveRootDirectory);
        byte[] bytes = full.DdsBytes;
        int headerLength = full.HeaderLength;
        int logicalMipLength = BuildBlockCompressedMipLengths(bytes, full.Width, full.Height, full.MipCount)[0];
        if (logicalMipLength <= 0 || headerLength + logicalMipLength > bytes.Length)
            throw new InvalidDataException("DDS root mip exceeds the extracted payload.");
        byte[] root = bytes.AsSpan(0, headerLength + logicalMipLength).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(28, 4), 1);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(8, 4)) & ~0x00020000u;
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(8, 4), flags);
        uint caps = BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(108, 4)) & ~(0x8u | 0x400000u);
        BinaryPrimitives.WriteUInt32LittleEndian(root.AsSpan(108, 4), caps);
        return new TexturePackageDdsData(root, full.Width, full.Height, full.MipCount, full.MipEntries, headerLength, root.Length);
    }

    public static IReadOnlyList<byte[]> SplitMipPayloads(byte[] ddsBytes)
    {
        var split = SplitForImport(ddsBytes);
        return split.MipPayloads;
    }

    public static TexturePackageImportResult SplitForImport(byte[] ddsBytes)
    {
        ArgumentNullException.ThrowIfNull(ddsBytes);
        ValidateDdsHeader(ddsBytes, (uint)ddsBytes.Length, null, out _, out _, out int mipCount, true);

        int headerLength = GetDdsHeaderLength(ddsBytes);
        if (ddsBytes.Length <= headerLength)
            throw new InvalidDataException("DDS payload does not contain mip data.");

        int payloadLength = ddsBytes.Length - headerLength;
        if (mipCount <= 0)
            throw new InvalidDataException("DDS declared zero mip levels.");
        if (payloadLength < mipCount)
            throw new InvalidDataException($"DDS payload too small for {mipCount} mip levels.");

        int width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(16, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(12, 4)));
        int[] mipLengths = BuildBlockCompressedMipLengths(ddsBytes, width, height, mipCount);
        if (mipLengths.Sum() != payloadLength)
            throw new InvalidDataException($"DDS payload length mismatch. Expected {mipLengths.Sum()}, got {payloadLength}.");
        var mipPayloads = new List<byte[]>(mipCount);
        int cursor = headerLength;

        for (int i = 0; i < mipCount; i++)
        {
            int mipLength = mipLengths[i];
            var mipBytes = new byte[mipLength];
            Buffer.BlockCopy(ddsBytes, cursor, mipBytes, 0, mipLength);
            mipPayloads.Add(mipBytes);
            cursor += mipLength;
        }

        using var header = new MemoryStream(TexturePackageHeaderLength + (mipCount * TexturePackageMipInfoLength) + headerLength);
        using var writer = new BinaryWriter(header);
        writer.Write(ddsBytes.Length);
        writer.Write(mipCount);
        int storageOffset = 0;
        for (int i = 0; i < mipCount; i++)
        {
            writer.Write(storageOffset);
            writer.Write(mipLengths[i]);
            storageOffset = checked(storageOffset + mipLengths[i]);
        }
        writer.Write(ddsBytes, 0, headerLength);

        return new TexturePackageImportResult(header.ToArray(), mipPayloads);
    }

    public static void ValidateImportAgainstAsset(byte[] ddsBytes, AssetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(ddsBytes);
        ArgumentNullException.ThrowIfNull(entry);

        var split = SplitForImport(ddsBytes);
        if (entry.SubEntries is null || entry.SubEntries.Count == 0)
            throw new InvalidDataException($"Texture '{entry.Path}' has no subfiles to replace.");

        var imageHeaderEntry = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Texture '{entry.Path}' is missing Image.img.");

        // The header blob may be smaller than the original Image.img because the game
        // stores extra metadata bytes after the DDS header. The extra bytes are preserved
        // during import by only overwriting the first HeaderBlob.Length bytes of Image.img.
        if (split.HeaderBlob.Length > imageHeaderEntry.Size)
            throw new InvalidDataException($"Imported DDS header blob length {split.HeaderBlob.Length} exceeds Image.img capacity {imageHeaderEntry.Size}.");

        if (split.MipPayloads.Count != entry.SubEntries.Count(s => s.FileName.StartsWith("mip", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Imported DDS mip count does not match the existing texture package layout.");

        for (int i = 0; i < split.MipPayloads.Count; i++)
        {
            var mipEntry = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals($"mip{i}.img", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Texture '{entry.Path}' is missing mip{i}.img.");
            if (split.MipPayloads[i].Length > mipEntry.Size)
                throw new InvalidDataException($"Imported DDS mip{i} payload {split.MipPayloads[i].Length} exceeds template capacity {mipEntry.Size}.");
        }
    }

    public static TexturePackageImportResult CreateTemplateImport(byte[] ddsBytes, AssetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(ddsBytes);
        ArgumentNullException.ThrowIfNull(entry);
        ValidateImportAgainstAsset(ddsBytes, entry);
        TexturePackageImportResult logical = SplitForImport(ddsBytes);
        IReadOnlyList<SubAssetEntry> subEntries = entry.SubEntries!;
        SubAssetEntry imageEntry = subEntries.Single(sub => sub.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase));
        string contentDirectory = imageEntry.ContentDirectory ?? entry.ContentDirectory
            ?? throw new InvalidDataException($"Texture '{entry.Path}' has no content directory.");
        byte[] originalImage = AssetExtractor.GetSubBlob(imageEntry, entry, contentDirectory);
        int mipCount = logical.MipPayloads.Count;
        int ddsHeaderLength = GetDdsHeaderLength(ddsBytes);
        int descriptorEnd = checked(TexturePackageHeaderLength + mipCount * TexturePackageMipInfoLength);
        if (originalImage.Length < descriptorEnd + ddsHeaderLength)
            throw new InvalidDataException($"Texture '{entry.Path}' Image.img is too small for its template layout.");

        byte[] header = originalImage.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), checked((uint)ddsBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), checked((uint)mipCount));
        ddsBytes.AsSpan(0, ddsHeaderLength).CopyTo(header.AsSpan(descriptorEnd, ddsHeaderLength));

        var padded = new List<byte[]>(mipCount);
        for (int i = 0; i < mipCount; i++)
        {
            SubAssetEntry mipEntry = subEntries.Single(sub => sub.FileName.Equals($"mip{i}.img", StringComparison.OrdinalIgnoreCase));
            byte[] originalMip = AssetExtractor.GetSubBlob(mipEntry, entry, mipEntry.ContentDirectory ?? contentDirectory);
            byte[] replacement = originalMip.ToArray();
            logical.MipPayloads[i].CopyTo(replacement, 0);
            padded.Add(replacement);
        }
        return new TexturePackageImportResult(header, padded);
    }

    private static int[] BuildBlockCompressedMipLengths(ReadOnlySpan<byte> ddsBytes, int width, int height, int mipCount)
    {
        int maximumMipCount = 1;
        for (int extent = Math.Max(width, height); extent > 1; extent >>= 1)
            maximumMipCount++;
        if (mipCount <= 0 || mipCount > maximumMipCount)
            throw new InvalidDataException(
                $"DDS mip count {mipCount} exceeds the maximum {maximumMipCount} levels for {width}x{height}.");

        int blockBytes = GetBlockBytes(ddsBytes);
        var mipLengths = new int[mipCount];
        for (int i = 0; i < mipCount; i++)
        {
            int mipWidth = Math.Max(1, width >> i);
            int mipHeight = Math.Max(1, height >> i);
            int blocksWide = Math.Max(1, (mipWidth + 3) / 4);
            int blocksHigh = Math.Max(1, (mipHeight + 3) / 4);
            mipLengths[i] = checked(blocksWide * blocksHigh * blockBytes);
        }
        return mipLengths;
    }

    private static int GetDdsHeaderLength(ReadOnlySpan<byte> ddsBytes)
    {
        if (ddsBytes.Length < DdsHeaderWithMagicLength)
            throw new InvalidDataException($"DDS data too small ({ddsBytes.Length} bytes).");
        bool isDx10 = ddsBytes.Slice(84, 4).SequenceEqual("DX10"u8);
        int length = DdsHeaderWithMagicLength + (isDx10 ? DdsDx10HeaderLength : 0);
        if (ddsBytes.Length < length)
            throw new InvalidDataException("DDS data is missing the DX10 extension header.");
        return length;
    }

    private static int GetBlockBytes(ReadOnlySpan<byte> ddsBytes)
    {
        ReadOnlySpan<byte> fourCc = ddsBytes.Slice(84, 4);
        if (fourCc.SequenceEqual("DX10"u8))
        {
            uint format = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.Slice(128, 4));
            return format switch
            {
                71 or 72 or 80 => 8,
                77 or 78 or 83 or 98 or 99 => 16,
                _ => throw new InvalidDataException($"Unsupported block-compressed DXGI format {format}.")
            };
        }
        if (fourCc.SequenceEqual("DXT1"u8) || fourCc.SequenceEqual("ATI1"u8) || fourCc.SequenceEqual("BC4U"u8)) return 8;
        if (fourCc.SequenceEqual("DXT3"u8) || fourCc.SequenceEqual("DXT5"u8) || fourCc.SequenceEqual("ATI2"u8) || fourCc.SequenceEqual("BC5U"u8)) return 16;
        throw new InvalidDataException($"Unsupported DDS FourCC '{System.Text.Encoding.ASCII.GetString(fourCc)}'.");
    }

    private static void ValidateDdsHeader(
        byte[] ddsBytes,
        uint expectedTotalSize,
        uint? expectedMipCount,
        out int width,
        out int height,
        out int mipCount,
        bool validateExactSize,
        bool allowMissingPitchMetadata = false)
    {
        int ddsHeaderLength = GetDdsHeaderLength(ddsBytes);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(0, 4));
        if (magic != DdsMagic)
            throw new InvalidDataException("DDS magic mismatch.");

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(4, 4));
        if (headerSize != DdsHeaderSize)
            throw new InvalidDataException($"Unexpected DDS header size {headerSize}.");

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(8, 4));
        if ((flags & DdsRequiredFlags) != DdsRequiredFlags)
            throw new InvalidDataException($"DDS header missing required flags 0x{DdsRequiredFlags:X8}.");
        uint declaredLinearSize = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(20, 4));
        bool missingPitchMetadata = (flags & (DdsPitchFlag | DdsLinearSizeFlag)) == 0 && declaredLinearSize == 0;
        if ((flags & (DdsPitchFlag | DdsLinearSizeFlag)) == 0 && !(allowMissingPitchMetadata && missingPitchMetadata))
            throw new InvalidDataException("DDS header requires PITCH (0x8) or LINEARSIZE (0x80000) flag.");

        height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(12, 4)));
        width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(16, 4)));
        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Invalid DDS dimensions {width}x{height}.");

        if (declaredLinearSize == 0 && !(allowMissingPitchMetadata && missingPitchMetadata))
            throw new InvalidDataException("DDS pitch/linear size is zero.");

        uint declaredMipCount = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(28, 4));
        mipCount = checked((int)(declaredMipCount == 0 ? 1 : declaredMipCount));
        if (expectedMipCount.HasValue && mipCount != expectedMipCount.Value)
            throw new InvalidDataException($"DDS header mip count mismatch. Expected {expectedMipCount.Value}, got {mipCount}.");

        uint pixelFormatSize = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(DdsPixelFormatOffset, 4));
        if (pixelFormatSize != DdsPixelFormatSize)
            throw new InvalidDataException($"Unexpected DDS pixel format size {pixelFormatSize}.");

        uint caps = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(DdsCapsOffset, 4));
        if ((caps & DdsRequiredCaps) != DdsRequiredCaps)
            throw new InvalidDataException($"DDS header missing required caps 0x{DdsRequiredCaps:X8}.");

        uint caps2 = BinaryPrimitives.ReadUInt32LittleEndian(ddsBytes.AsSpan(DdsCaps2Offset, 4));
        if (caps2 != 0)
            throw new InvalidDataException($"Unsupported DDS caps2 flags 0x{caps2:X8}.");

        if (validateExactSize && ddsBytes.Length != expectedTotalSize)
            throw new InvalidDataException($"DDS size mismatch. Expected {expectedTotalSize}, got {ddsBytes.Length}.");

        _ = ddsHeaderLength;
    }
}

public sealed record TexturePackageImportResult(byte[] HeaderBlob, IReadOnlyList<byte[]> MipPayloads);
