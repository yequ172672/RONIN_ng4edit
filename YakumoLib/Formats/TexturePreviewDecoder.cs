using System.Buffers.Binary;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using YakumoLib.Assets;

namespace YakumoLib.Formats;

public sealed record TexturePreviewRgba(byte[] Rgba, int Width, int Height, int Mip, int SourceWidth, int SourceHeight, bool Srgb);

public static class TexturePreviewDecoder
{
    public static TexturePreviewRgba Decode(AssetEntry texture, int maxSize)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture.Type != AssetType.Texture) throw new ArgumentException("Asset must be a Texture.", nameof(texture));
        SubAssetEntry image = texture.SubEntries?.FirstOrDefault(sub => sub.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Texture '{texture.Path}' is missing Image.img.");
        byte[] header = AssetExtractor.GetSubBlob(image, texture, image.ContentDirectory);
        return Decode(header, mip =>
        {
            SubAssetEntry entry = texture.SubEntries?.FirstOrDefault(sub => sub.FileName.Equals($"mip{mip}.img", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Texture '{texture.Path}' is missing mip{mip}.img.");
            return AssetExtractor.GetSubBlob(entry, texture, entry.ContentDirectory);
        }, maxSize);
    }

    public static TexturePreviewRgba Decode(ReadOnlySpan<byte> imageHeader, Func<int, byte[]> loadMip, int maxSize)
    {
        ArgumentNullException.ThrowIfNull(loadMip);
        if (maxSize is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxSize), "Texture preview max size must be between 1 and 4096.");
        if (imageHeader.Length < 8 + 128) throw new InvalidDataException("Texture Image.img is too small.");
        int mipCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(imageHeader.Slice(4, 4)));
        int descriptorEnd = checked(8 + mipCount * 8);
        if (mipCount <= 0 || descriptorEnd > imageHeader.Length - 128) throw new InvalidDataException("Texture mip descriptor table is invalid.");
        ReadOnlySpan<byte> dds = imageHeader[descriptorEnd..];
        if (!dds[..4].SequenceEqual("DDS "u8)) throw new InvalidDataException("Texture DDS header is invalid.");
        int sourceHeight = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds.Slice(12, 4)));
        int sourceWidth = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds.Slice(16, 4)));
        bool dx10 = dds.Length >= 148 && dds.Slice(84, 4).SequenceEqual("DX10"u8);
        int format = dx10 ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds.Slice(128, 4))) : FourCcFormat(dds.Slice(84, 4));
        int mip = 0, width = sourceWidth, height = sourceHeight;
        while (mip + 1 < mipCount && (width > maxSize || height > maxSize))
        {
            mip++; width = Math.Max(1, sourceWidth >> mip); height = Math.Max(1, sourceHeight >> mip);
        }
        int capacity = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(imageHeader.Slice(8 + mip * 8 + 4, 4)));
        int logicalLength = BlockLength(width, height, format);
        byte[] stored = loadMip(mip);
        if (capacity < logicalLength || stored.Length < logicalLength)
            throw new InvalidDataException($"Texture mip {mip} is smaller than its logical payload.");
        ColorRgba32[] pixels;
        try { pixels = new BcDecoder().DecodeRaw(stored.AsSpan(0, logicalLength).ToArray(), width, height, Compression(format)); }
        catch (Exception ex) { throw new InvalidDataException($"Texture mip {mip} could not be decoded.", ex); }
        byte[] rgba = new byte[checked(pixels.Length * 4)];
        for (int index = 0; index < pixels.Length; index++)
        {
            int target = index * 4; ColorRgba32 pixel = pixels[index];
            rgba[target] = pixel.r; rgba[target + 1] = pixel.g; rgba[target + 2] = pixel.b; rgba[target + 3] = pixel.a;
        }
        return new TexturePreviewRgba(rgba, width, height, mip, sourceWidth, sourceHeight, format is 72 or 78 or 99);
    }

    private static int FourCcFormat(ReadOnlySpan<byte> fourCc) => fourCc.SequenceEqual("DXT1"u8) ? 71 : fourCc.SequenceEqual("DXT3"u8) || fourCc.SequenceEqual("DXT5"u8) ? 77 : fourCc.SequenceEqual("ATI1"u8) || fourCc.SequenceEqual("BC4U"u8) ? 80 : fourCc.SequenceEqual("ATI2"u8) || fourCc.SequenceEqual("BC5U"u8) ? 83 : throw new InvalidDataException("Unsupported DDS texture format.");
    private static CompressionFormat Compression(int format) => format switch { 71 or 72 => CompressionFormat.Bc1, 77 or 78 => CompressionFormat.Bc3, 80 => CompressionFormat.Bc4, 83 => CompressionFormat.Bc5, 98 or 99 => CompressionFormat.Bc7, _ => throw new InvalidDataException($"Unsupported DXGI texture format {format}.") };
    private static int BlockLength(int width, int height, int format) => checked(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * (format is 71 or 72 or 80 ? 8 : 16));
}
