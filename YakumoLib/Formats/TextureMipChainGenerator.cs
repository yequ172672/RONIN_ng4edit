using System.Buffers.Binary;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;

namespace YakumoLib.Formats;

public static class TextureMipChainGenerator
{
    public static byte[] DecodeRootRgba(byte[] dds, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(dds);
        if (dds.Length < 128 || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8))
            throw new InvalidDataException("A valid DDS root mip is required.");
        width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(16, 4)));
        height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(12, 4)));
        try
        {
            using var input = new MemoryStream(dds, writable: false);
            ColorRgba32[] pixels = new BcDecoder().Decode(DdsFile.Load(input));
            if (pixels.Length != checked(width * height))
                throw new InvalidDataException($"DDS decoded {pixels.Length} pixels, expected {width * height}.");
            return ToBytes(pixels);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) { throw new InvalidDataException("DDS root mip could not be decoded.", ex); }
    }

    public static byte[] EncodeRootRgba(byte[] rgba, int width, int height, byte[] templateDds)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentNullException.ThrowIfNull(templateDds);
        if (rgba.Length != checked(width * height * 4))
            throw new InvalidDataException("RGBA pixel buffer length does not match its dimensions.");
        if (templateDds.Length < 148 || !templateDds.AsSpan(0, 4).SequenceEqual("DDS "u8) ||
            !templateDds.AsSpan(84, 4).SequenceEqual("DX10"u8))
            throw new InvalidDataException("A DX10 DDS template is required.");
        if (width != checked((int)BinaryPrimitives.ReadUInt32LittleEndian(templateDds.AsSpan(16, 4))) ||
            height != checked((int)BinaryPrimitives.ReadUInt32LittleEndian(templateDds.AsSpan(12, 4))))
            throw new InvalidDataException("RGBA dimensions differ from the texture template.");

        int formatCode = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(templateDds.AsSpan(128, 4)));
        var encoder = new BcEncoder(GetCompressionFormat(formatCode));
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        byte[] payload = encoder.EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32)[0];
        byte[] result = new byte[148 + payload.Length];
        templateDds.AsSpan(0, 148).CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28, 4), 1);
        payload.CopyTo(result, 148);
        return result;
    }

    public static byte[] TranscodeRoot(byte[] editedDds, byte[] templateDds)
    {
        ArgumentNullException.ThrowIfNull(editedDds);
        ArgumentNullException.ThrowIfNull(templateDds);
        if (editedDds.Length < 128 || templateDds.Length < 148 ||
            !editedDds.AsSpan(0, 4).SequenceEqual("DDS "u8) ||
            !templateDds.AsSpan(0, 4).SequenceEqual("DDS "u8) ||
            !templateDds.AsSpan(84, 4).SequenceEqual("DX10"u8))
            throw new InvalidDataException("Edited and template root textures must be valid DDS files, with a DX10 template.");

        int width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(templateDds.AsSpan(16, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(templateDds.AsSpan(12, 4)));
        if (width != checked((int)BinaryPrimitives.ReadUInt32LittleEndian(editedDds.AsSpan(16, 4))) ||
            height != checked((int)BinaryPrimitives.ReadUInt32LittleEndian(editedDds.AsSpan(12, 4))))
            throw new InvalidDataException("Edited DDS dimensions differ from the texture template.");

        int formatCode = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(templateDds.AsSpan(128, 4)));
        CompressionFormat format = GetCompressionFormat(formatCode);
        ColorRgba32[] pixels;
        try
        {
            using var input = new MemoryStream(editedDds, writable: false);
            pixels = new BcDecoder().Decode(DdsFile.Load(input));
        }
        catch (Exception ex) { throw new InvalidDataException("Edited DDS root mip could not be decoded.", ex); }
        if (pixels.Length != checked(width * height))
            throw new InvalidDataException($"Edited DDS decoded {pixels.Length} pixels, expected {width * height}.");

        var encoder = new BcEncoder(format);
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        byte[] payload = encoder.EncodeToRawBytes(ToBytes(pixels), width, height, PixelFormat.Rgba32)[0];
        if (payload.Length != BlockLength(width, height, formatCode))
            throw new InvalidDataException("Transcoded DDS root mip has an unexpected payload length.");
        byte[] result = new byte[148 + payload.Length];
        templateDds.AsSpan(0, 148).CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28, 4), 1);
        payload.CopyTo(result, 148);
        return result;
    }

    public static byte[] Generate(byte[] rootDds, int targetMipCount)
    {
        ArgumentNullException.ThrowIfNull(rootDds);
        if (rootDds.Length < 148 || !rootDds.AsSpan(0, 4).SequenceEqual("DDS "u8) ||
            !rootDds.AsSpan(84, 4).SequenceEqual("DX10"u8))
            throw new InvalidDataException("A DX10 DDS root mip is required.");
        int width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(rootDds.AsSpan(16, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(rootDds.AsSpan(12, 4)));
        int formatCode = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(rootDds.AsSpan(128, 4)));
        CompressionFormat format = GetCompressionFormat(formatCode);
        int maximumMipCount = 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));
        if (targetMipCount <= 0 || targetMipCount > maximumMipCount)
            throw new InvalidDataException($"Mip count {targetMipCount} is invalid for {width}x{height}.");

        int rootLength = BlockLength(width, height, formatCode);
        if (rootDds.Length != 148 + rootLength)
            throw new InvalidDataException($"Root DDS payload length mismatch. Expected {rootLength}, got {rootDds.Length - 148}.");

        ColorRgba32[] pixels;
        try { pixels = new BcDecoder().DecodeRaw(rootDds.AsSpan(148).ToArray(), width, height, format); }
        catch (Exception ex) { throw new InvalidDataException("DDS root mip could not be decoded.", ex); }

        byte[] header = rootDds.AsSpan(0, 148).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28, 4), checked((uint)targetMipCount));
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
        flags = targetMipCount > 1 ? flags | 0x00020000u : flags & ~0x00020000u;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), flags);
        uint caps = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(108, 4));
        caps = targetMipCount > 1 ? caps | 0x00400008u : caps & ~0x00400008u;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(108, 4), caps);

        using var output = new MemoryStream();
        output.Write(header);
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        int mipWidth = width, mipHeight = height;
        for (int level = 0; level < targetMipCount; level++)
        {
            byte[] rgba = ToBytes(pixels);
            byte[] encoded;
            try
            {
                encoded = encoder.EncodeToRawBytes(rgba, mipWidth, mipHeight, PixelFormat.Rgba32)[0];
            }
            catch (Exception ex) { throw new InvalidDataException($"DDS mip {level} could not be encoded.", ex); }
            if (encoded.Length != BlockLength(mipWidth, mipHeight, formatCode))
                throw new InvalidDataException($"Encoded mip {level} has unexpected length {encoded.Length}.");
            output.Write(encoded);
            if (level + 1 < targetMipCount)
            {
                pixels = Downsample(pixels, mipWidth, mipHeight, formatCode);
                mipWidth = Math.Max(1, mipWidth / 2);
                mipHeight = Math.Max(1, mipHeight / 2);
            }
        }
        return output.ToArray();
    }

    private static ColorRgba32[] Downsample(ColorRgba32[] source, int width, int height, int formatCode)
    {
        int nextWidth = Math.Max(1, width / 2), nextHeight = Math.Max(1, height / 2);
        var result = new ColorRgba32[nextWidth * nextHeight];
        bool srgb = formatCode is 72 or 78;
        for (int y = 0; y < nextHeight; y++)
        for (int x = 0; x < nextWidth; x++)
        {
            int x0 = x * 2, x1 = Math.Min(width - 1, x0 + 1);
            int y0 = y * 2, y1 = Math.Min(height - 1, y0 + 1);
            ColorRgba32 a = source[y0 * width + x0], b = source[y0 * width + x1];
            ColorRgba32 c = source[y1 * width + x0], d = source[y1 * width + x1];
            result[y * nextWidth + x] = formatCode == 83 ? AverageNormal(a, b, c, d) : AverageColor(a, b, c, d, srgb);
        }
        return result;
    }

    private static ColorRgba32 AverageColor(ColorRgba32 p0, ColorRgba32 p1, ColorRgba32 p2, ColorRgba32 p3, bool srgb)
    {
        double red = AverageChannel(p0.r, p1.r, p2.r, p3.r, srgb);
        double green = AverageChannel(p0.g, p1.g, p2.g, p3.g, srgb);
        double blue = AverageChannel(p0.b, p1.b, p2.b, p3.b, srgb);
        double alpha = (p0.a + p1.a + p2.a + p3.a) / (4d * 255d);
        return new ColorRgba32(ToByte(red), ToByte(green), ToByte(blue), ToByte(alpha));
    }

    private static ColorRgba32 AverageNormal(ColorRgba32 p0, ColorRgba32 p1, ColorRgba32 p2, ColorRgba32 p3)
    {
        double x = 0, y = 0, z = 0;
        AddNormal(p0, ref x, ref y, ref z); AddNormal(p1, ref x, ref y, ref z);
        AddNormal(p2, ref x, ref y, ref z); AddNormal(p3, ref x, ref y, ref z);
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length > 1e-12) { x /= length; y /= length; }
        return new ColorRgba32(ToByte(x * 0.5 + 0.5), ToByte(y * 0.5 + 0.5), 0, 255);
    }

    private static double AverageChannel(byte a, byte b, byte c, byte d, bool srgb)
    {
        double result = srgb
            ? (SrgbToLinear(a / 255d) + SrgbToLinear(b / 255d) + SrgbToLinear(c / 255d) + SrgbToLinear(d / 255d)) / 4
            : (a + b + c + d) / (4d * 255d);
        return srgb ? LinearToSrgb(result) : result;
    }

    private static void AddNormal(ColorRgba32 value, ref double x, ref double y, ref double z)
    {
        double nx = value.r / 127.5 - 1, ny = value.g / 127.5 - 1;
        x += nx; y += ny; z += Math.Sqrt(Math.Max(0, 1 - nx * nx - ny * ny));
    }

    private static byte[] ToBytes(IReadOnlyList<ColorRgba32> pixels)
    {
        var bytes = new byte[checked(pixels.Count * 4)];
        for (int i = 0; i < pixels.Count; i++)
        {
            ColorRgba32 pixel = pixels[i];
            int offset = i * 4;
            bytes[offset] = pixel.r; bytes[offset + 1] = pixel.g; bytes[offset + 2] = pixel.b; bytes[offset + 3] = pixel.a;
        }
        return bytes;
    }
    private static int BlockLength(int width, int height, int format) => checked(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * (format is 71 or 72 or 80 ? 8 : 16));
    private static CompressionFormat GetCompressionFormat(int formatCode) => formatCode switch
    {
        71 or 72 => CompressionFormat.Bc1,
        77 or 78 => CompressionFormat.Bc3,
        80 => CompressionFormat.Bc4,
        83 => CompressionFormat.Bc5,
        _ => throw new NotSupportedException($"DXGI format {formatCode} is not supported for texture encoding.")
    };
    private static double SrgbToLinear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    private static double LinearToSrgb(double value) => value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
    private static byte ToByte(double value) => checked((byte)Math.Clamp((int)Math.Round(value * 255), 0, 255));
}
