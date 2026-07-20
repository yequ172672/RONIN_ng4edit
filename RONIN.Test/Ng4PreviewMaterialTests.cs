using System.Buffers.Binary;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using YakumoLib;
using YakumoLib.Assets;
using YakumoLib.Formats;

internal static class Ng4PreviewMaterialTests
{
    public static void Run()
    {
        TestMaterialMapSlots();
        TestMaterialMapSlotsFollowModelMaterialTable();
        TestBaseColorInheritance();
        TestBoundedMipDecode();
        Console.WriteLine("NG4 preview material tests passed.");
    }

    private static void TestMaterialMapSlotsFollowModelMaterialTable()
    {
        int remapped = Ng4MaterialResolver.ResolveModelMaterialSlot("Body", 2, ["M_Decal", "M_Cloth", "M_Body"]);
        if (remapped != 2)
            throw new Exception("Material map slot remapping did not follow the model material table.");
        int fallback = Ng4MaterialResolver.ResolveModelMaterialSlot("Missing", 7, ["M_Decal"]);
        if (fallback != 7)
            throw new Exception("Unknown material names should preserve their map fallback slot.");
    }

    private static void TestMaterialMapSlots()
    {
        UUID first = Id(1), second = Id(2);
        byte[] data = BuildMaterialMap(BuildNamedUuidRecord("Body", first), BuildNamedUuidRecord("Eyes", second));
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x14, 4)) != 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x18, 4)) != data.Length - 0x14 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x1c, 4)) < 16)
            throw new Exception("Synthetic materialmap fixture does not match the observed NG4 layout.");
        IReadOnlyList<Ng4MaterialMapEntry> entries = Ng4MaterialMapReader.Read(data);
        if (entries.Count != 2 || entries[0].Slot != 0 || entries[0].Name != "Body" || !entries[0].MaterialAssetId.Equals(first) ||
            entries[1].Slot != 1 || entries[1].Name != "Eyes" || !entries[1].MaterialAssetId.Equals(second))
            throw new Exception("materialmap.bin slot parsing failed.");
    }

    private static void TestBaseColorInheritance()
    {
        UUID parent = Id(10), inheritedTexture = Id(20), childTexture = Id(21);
        UUID inheritedMro = Id(31), inheritedNormal = Id(30), childMro = Id(32), childNormal = Id(33), inheritedDetail = Id(40), childDetail = Id(41);
        Ng4MaterialInstanceData parentData = Ng4MaterialInstanceReader.Read(BuildInstance(null,
            BuildNamedUuidRecord("BaseColorMap", inheritedTexture), BuildNamedUuidRecord("MaskMap", inheritedMro),
            BuildNamedUuidRecord("NormalMap", inheritedNormal), BuildNamedUuidRecord("DetailTex", inheritedDetail)));
        byte[] childBytes = BuildInstance(parent);
        if (BinaryPrimitives.ReadUInt32LittleEndian(childBytes.AsSpan(8, 4)) != 7 ||
            BinaryPrimitives.ReadUInt32LittleEndian(childBytes.AsSpan(16, 4)) + 4 != 0x44 ||
            !childBytes.AsSpan(0x44, 16).SequenceEqual(ToBytes(parent)))
            throw new Exception("Synthetic Instance.dat fixture does not match the observed NG4 layout.");
        Ng4MaterialInstanceData childWithoutOverride = Ng4MaterialInstanceReader.Read(childBytes);
        Ng4MaterialInstanceData childWithOverride = Ng4MaterialInstanceReader.Read(BuildInstance(parent,
            BuildNamedUuidRecord("BaseColorMap", childTexture), BuildNamedUuidRecord("MaskMap", childMro),
            BuildNamedUuidRecord("NormalMap", childNormal), BuildNamedUuidRecord("DetailTex", childDetail)));

        UUID? inherited = Ng4MaterialResolver.ResolveBaseColor(childWithoutOverride, id => id.Equals(parent) ? parentData : null);
        UUID? overridden = Ng4MaterialResolver.ResolveBaseColor(childWithOverride, id => id.Equals(parent) ? parentData : null);
        UUID? inheritedResolvedMro = Ng4MaterialResolver.ResolveMro(childWithoutOverride, id => id.Equals(parent) ? parentData : null);
        UUID? inheritedResolvedNormal = Ng4MaterialResolver.ResolveNormal(childWithoutOverride, id => id.Equals(parent) ? parentData : null);
        UUID? overriddenMro = Ng4MaterialResolver.ResolveMro(childWithOverride, id => id.Equals(parent) ? parentData : null);
        UUID? overriddenNormal = Ng4MaterialResolver.ResolveNormal(childWithOverride, id => id.Equals(parent) ? parentData : null);
        IReadOnlyList<Ng4UnknownTexture> unknown = Ng4MaterialResolver.ResolveUnknownTextures(childWithoutOverride, id => id.Equals(parent) ? parentData : null);
        IReadOnlyList<Ng4UnknownTexture> overriddenUnknown = Ng4MaterialResolver.ResolveUnknownTextures(childWithOverride, id => id.Equals(parent) ? parentData : null);
        if (inherited is null || !inherited.Equals(inheritedTexture) || overridden is null || !overridden.Equals(childTexture) ||
            inheritedResolvedMro is null || !inheritedResolvedMro.Equals(inheritedMro) ||
            inheritedResolvedNormal is null || !inheritedResolvedNormal.Equals(inheritedNormal) ||
            overriddenMro is null || !overriddenMro.Equals(childMro) ||
            overriddenNormal is null || !overriddenNormal.Equals(childNormal) || unknown.Count != 1 || !unknown[0].AssetId.Equals(inheritedDetail) ||
            overriddenUnknown.Count != 1 || !overriddenUnknown[0].AssetId.Equals(childDetail))
            throw new Exception("MaterialInstance BaseColorMap inheritance failed.");
    }

    private static void TestBoundedMipDecode()
    {
        byte[] red = Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 64).SelectMany(pixel => pixel).ToArray();
        byte[] green = Enumerable.Repeat(new byte[] { 0, 255, 0, 255 }, 16).SelectMany(pixel => pixel).ToArray();
        byte[] blue = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 4).SelectMany(pixel => pixel).ToArray();
        var encoder = new BcEncoder(CompressionFormat.Bc1);
        byte[][] payloads =
        [
            encoder.EncodeToRawBytes(red, 8, 8, PixelFormat.Rgba32)[0],
            encoder.EncodeToRawBytes(green, 4, 4, PixelFormat.Rgba32)[0],
            encoder.EncodeToRawBytes(blue, 2, 2, PixelFormat.Rgba32)[0]
        ];
        byte[] header = BuildTextureHeader(8, 8, 3, 71, payloads);
        TexturePreviewRgba decoded = TexturePreviewDecoder.Decode(header, mip => payloads[mip], 4);
        if (decoded.Width != 4 || decoded.Height != 4 || decoded.Mip != 1 || decoded.Rgba.Length != 64 ||
            decoded.Rgba[0] > 8 || decoded.Rgba[1] < 247 || decoded.Rgba[2] > 8 || decoded.Rgba[3] != 255)
            throw new Exception("Bounded low-mip RGBA decode failed.");
    }

    private static byte[] BuildInstance(UUID? parent, params byte[][] textureRecords)
    {
        byte[] parentSection = new byte[16];
        if (parent is not null) WriteId(parentSection, 0, parent);
        byte[] textures = BuildContainer(textureRecords);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(7u);
        int tableStart = checked((int)stream.Position);
        for (int i = 0; i < 7; i++) { writer.Write(i); writer.Write(0u); }
        int parentOffset = checked((int)stream.Position);
        writer.Write(parentSection);
        int textureOffset = checked((int)stream.Position);
        writer.Write(textures);
        long end = stream.Position;
        stream.Position = 4; writer.Write(checked((uint)(end - 4)));
        stream.Position = tableStart + 4;
        foreach (long offset in new[] { parentOffset, parentOffset, parentOffset, textureOffset, end, end, end })
        {
            writer.Write(checked((uint)(offset - 4)));
            stream.Position += 4;
        }
        return stream.ToArray();
    }

    private static byte[] BuildMaterialMap(params byte[][] records)
    {
        byte[] section = BuildContainer(records);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0u); writer.Write(0u); writer.Write(1u); writer.Write(0u); writer.Write(16u); writer.Write(section);
        long end = stream.Position; stream.Position = 4; writer.Write(checked((uint)(end - 4)));
        return stream.ToArray();
    }

    private static byte[] BuildContainer(params byte[][] records)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((uint)records.Length));
        writer.Write(0u);
        long offsetsStart = stream.Position;
        foreach (byte[] _ in records) writer.Write(0u);
        long baseOffset = 0;
        for (int i = 0; i < records.Length; i++)
        {
            long recordOffset = stream.Position;
            long restore = stream.Position;
            stream.Position = offsetsStart + i * 4;
            writer.Write(checked((uint)(recordOffset - baseOffset)));
            stream.Position = restore;
            writer.Write(records[i]);
        }
        long end = stream.Position;
        stream.Position = 4;
        writer.Write(checked((uint)end));
        return stream.ToArray();
    }

    private static byte[] BuildNamedUuidRecord(string name, UUID id)
    {
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int size = checked(28 + nameBytes.Length + 16);
        writer.Write(size); writer.Write(2); writer.Write(0); writer.Write(24); writer.Write(1);
        writer.Write(size - 16); writer.Write(nameBytes.Length); writer.Write(nameBytes);
        WriteId(writer, id);
        return stream.ToArray();
    }

    private static byte[] BuildTextureHeader(int width, int height, int mipCount, int dxgiFormat, IReadOnlyList<byte[]> payloads)
    {
        byte[] dds = new byte[148];
        "DDS "u8.CopyTo(dds); BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(8), 0x000A1007);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(12), checked((uint)height));
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16), checked((uint)width));
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(20), checked((uint)payloads[0].Length));
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(28), checked((uint)mipCount));
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(76), 32); BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(80), 4);
        "DX10"u8.CopyTo(dds.AsSpan(84)); BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(108), 0x00401008);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(128), checked((uint)dxgiFormat));
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(132), 3); BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(140), 1);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((uint)(148 + payloads.Sum(payload => payload.Length)))); writer.Write(checked((uint)mipCount));
        int offset = 0; foreach (byte[] payload in payloads) { writer.Write(offset); writer.Write(payload.Length); offset += payload.Length; }
        writer.Write(dds); return stream.ToArray();
    }

    private static UUID Id(uint value) => new() { a = value, b = value + 1, c = value + 2, d = value + 3 };
    private static byte[] ToBytes(UUID id) { byte[] bytes = new byte[16]; WriteId(bytes, 0, id); return bytes; }
    private static void WriteId(BinaryWriter writer, UUID id) { writer.Write(id.a); writer.Write(id.b); writer.Write(id.c); writer.Write(id.d); }
    private static void WriteId(byte[] buffer, int offset, UUID id) { BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), id.a); BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 4), id.b); BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 8), id.c); BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 12), id.d); }
}
