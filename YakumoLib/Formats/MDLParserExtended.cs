using System.Numerics;
using System.Text;

namespace YakumoLib.Formats;

public static class MDLParserExtended
{
    private const uint MagicNumber = 4998221;
    private const int HeaderFieldCount = 32;

    public static MDLFullData Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderFieldCount * 4)
            throw new InvalidDataException("MDL header is truncated.");

        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != MagicNumber)
            throw new InvalidDataException("Not a valid MDL file.");

        var h = ReadHeader(reader);
        if (h.FileSize != 0 && h.FileSize > data.Length)
            throw new InvalidDataException("MDL file size exceeds the available data.");

        string[] boneNames = ReadStringTable(reader, data.Length, h.BoneNameTableOffset, h.BoneCount, "bone names");
        string[] materialNames = ReadStringTable(reader, data.Length, h.MaterialNameTableOffset, h.MaterialCount, "material names");
        string[] meshNames = ReadStringTable(reader, data.Length, h.MeshNameTableOffset, h.MeshNameCount, "mesh names");
        string[] propertyNames = ReadStringTable(reader, data.Length, h.VertexPropertyNameTableOffset, h.VertexPropertyCount, "vertex property names");

        var bones = ReadBones(reader, data.Length, h, boneNames);
        var boneInfos = ReadBoneInfos(reader, data.Length, h);
        var lods = ReadLods(reader, data, h);
        var declarations = ReadDeclarations(reader, data, h, propertyNames);
        var remaps = ReadRemaps(reader, data.Length, h);
        var batches = ReadBatches(reader, data.Length, h);

        RequireRange(data.Length, h.VertexHeaderOffset, 8, "vertex header");
        stream.Position = h.VertexHeaderOffset;
        uint mainStride = reader.ReadUInt32();
        uint exStride = reader.ReadUInt32();
        RequireRange(data.Length, h.VertexGroupSizesOffset, checked(h.VertexGroupCount * 4), "vertex group sizes");
        stream.Position = h.VertexGroupSizesOffset;
        uint[] groupSizes = new uint[checked((int)h.VertexGroupCount)];
        for (int i = 0; i < groupSizes.Length; i++) groupSizes[i] = reader.ReadUInt32();

        RequireRange(data.Length, h.VertexPoolOffsetsOffset, 8, "vertex pool offsets");
        stream.Position = h.VertexPoolOffsetsOffset;
        uint mainBase = reader.ReadUInt32();
        uint exBase = reader.ReadUInt32();
        uint mainOffset = mainBase, exOffset = exBase;
        var groups = new VertexGroup[groupSizes.Length];
        int blendIndexLayerCount = SemanticLayerCount(declarations, "BIX");
        int blendWeightLayerCount = SemanticLayerCount(declarations, "BWT");
        int uvLayerCount = declarations.Where(d => d.Name.StartsWith("map", StringComparison.OrdinalIgnoreCase))
            .Select(d => (int)d.MapIndex + 1).DefaultIfEmpty(0).Max();
        int colorLayerCount = declarations.Where(d => d.Name.StartsWith("color", StringComparison.OrdinalIgnoreCase))
            .Select(d => (int)d.MapIndex + 1).DefaultIfEmpty(0).Max();
        for (int g = 0; g < groups.Length; g++)
        {
            int count = checked((int)groupSizes[g]);
            RequireRange(data.Length, mainOffset, checked((uint)(count * mainStride)), $"main vertex group {g}");
            if (exStride != 0) RequireRange(data.Length, exOffset, checked((uint)(count * exStride)), $"extended vertex group {g}");
            var group = new VertexGroup
            {
                Positions = new Vector3[count], Normals = new Vector3[count], Tangents = new Vector4[count],
                BlendIndexLayers = NewByteArrayLayers(blendIndexLayerCount, count),
                BlendWeightLayers = NewByteArrayLayers(blendWeightLayerCount, count),
                UVLayers = NewVector2Layers(uvLayerCount, count),
                VertexColorLayers = NewByteArrayLayers(colorLayerCount, count),
                MainStride = mainStride, ExStride = exStride,
                MainDataOffset = mainOffset, ExDataOffset = exOffset
            };
            for (int declarationIndex = 0; declarationIndex < declarations.Length; declarationIndex++)
            {
                MDLVertexDeclaration declaration = declarations[declarationIndex];
                uint stride = declaration.Pool == 0 ? mainStride : exStride;
                int width = DeclarationStorageWidth(declarations, declarationIndex, stride);
                ReadDeclaration(reader, group, declaration, declarationIndex, width, count,
                    declaration.Pool == 0 ? mainOffset : exOffset, stride);
            }
            groups[g] = group;
            mainOffset = checked(mainOffset + (uint)(count * mainStride));
            exOffset = checked(exOffset + (uint)(count * exStride));
        }

        RequireRange(data.Length, h.IndexOffset, checked(h.IndexCount * 2), "index buffer");
        stream.Position = h.IndexOffset;
        ushort[] indices = new ushort[checked((int)h.IndexCount)];
        for (int i = 0; i < indices.Length; i++) indices[i] = reader.ReadUInt16();
        foreach (var group in groups) group.Indices = indices;

        return new MDLFullData
        {
            Header = h, LODs = lods, Groups = groups, Batches = batches, MeshNames = meshNames,
            MaterialNames = materialNames, Bones = bones, BoneInfos = boneInfos,
            VertexDeclarations = declarations, BoneRemapTables = remaps, Indices = indices,
            OriginalBytes = (byte[])data.Clone()
        };
    }

    private static MDLHeader ReadHeader(BinaryReader r) => new()
    {
        FileSize = r.ReadUInt32(), Unknown08 = r.ReadUInt32(), Version = r.ReadUInt32(), BoneCount = r.ReadUInt32(),
        Unknown14 = r.ReadUInt32(), BoneDataOffset = r.ReadUInt32(), BoneNameTableOffset = r.ReadUInt32(),
        BoneInfoCount = r.ReadUInt32(), BoneInfoOffset = r.ReadUInt32(), LODCount = r.ReadUInt32(), LODOffset = r.ReadUInt32(),
        LODNameOffset = r.ReadUInt32(), MaterialCount = r.ReadUInt32(), MaterialNameTableOffset = r.ReadUInt32(),
        MeshNameCount = r.ReadUInt32(), MeshNameTableOffset = r.ReadUInt32(), BatchCount = r.ReadUInt32(), BatchOffset = r.ReadUInt32(),
        VertexPropertyCount = r.ReadUInt32(), VertexPropertyOffset = r.ReadUInt32(), VertexPropertyNameTableOffset = r.ReadUInt32(),
        Unknown58 = r.ReadUInt32(), VertexHeaderOffset = r.ReadUInt32(), VertexGroupCount = r.ReadUInt32(),
        VertexGroupSizesOffset = r.ReadUInt32(), VertexPoolOffsetsOffset = r.ReadUInt32(), IndexCount = r.ReadUInt32(),
        IndexOffset = r.ReadUInt32(), RemapTableCount = r.ReadUInt32(), RemapCountOffset = r.ReadUInt32(), RemapTableOffset = r.ReadUInt32()
    };

    private static MDLBoneData[] ReadBones(BinaryReader r, int length, MDLHeader h, string[] names)
    {
        const uint size = 76;
        RequireRange(length, h.BoneDataOffset, checked(h.BoneCount * size), "bones");
        r.BaseStream.Position = h.BoneDataOffset;
        var result = new MDLBoneData[checked((int)h.BoneCount)];
        for (int i = 0; i < result.Length; i++) result[i] = new MDLBoneData
        {
            Name = i < names.Length ? names[i] : $"Bone{i}", ParentIndex = r.ReadUInt32(), UnknownA = ReadVector3(r),
            UnknownB = ReadVector3(r), UnknownC = ReadVector3(r), Translation = ReadVector3(r), Rotation = ReadVector3(r), Scale = ReadVector3(r)
        };
        return result;
    }

    private static MDLBoneInfo[] ReadBoneInfos(BinaryReader r, int length, MDLHeader h)
    {
        RequireRange(length, h.BoneInfoOffset, checked(h.BoneInfoCount * 28), "bone info");
        r.BaseStream.Position = h.BoneInfoOffset;
        var result = new MDLBoneInfo[checked((int)h.BoneInfoCount)];
        for (int i = 0; i < result.Length; i++) result[i] = new MDLBoneInfo { UnknownA = ReadVector3(r), UnknownB = ReadVector3(r), RelatedBoneIndex = r.ReadUInt32() };
        return result;
    }

    private static MDLLOD[] ReadLods(BinaryReader r, byte[] data, MDLHeader h)
    {
        const int size = 44;
        RequireRange(data.Length, h.LODOffset, checked(h.LODCount * size), "LODs");
        var result = new MDLLOD[checked((int)h.LODCount)];
        for (int i = 0; i < result.Length; i++)
        {
            uint offset = h.LODOffset + (uint)(i * size); r.BaseStream.Position = offset;
            uint u0 = r.ReadUInt32(); string name = Encoding.ASCII.GetString(r.ReadBytes(4)).TrimEnd('\0');
            uint[] values = new uint[8]; for (int j = 0; j < values.Length; j++) values[j] = r.ReadUInt32();
            uint batchCount = r.ReadUInt32(); byte[] raw = data.AsSpan((int)offset, size).ToArray();
            result[i] = new MDLLOD { Unknown00 = u0, Name = name, UnknownValues = values, BatchCount = batchCount, RawBytes = raw };
        }
        return result;
    }

    private static MDLVertexDeclaration[] ReadDeclarations(BinaryReader r, byte[] data, MDLHeader h, string[] names)
    {
        RequireRange(data.Length, h.VertexPropertyOffset, checked(h.VertexPropertyCount * 10), "vertex declarations");
        r.BaseStream.Position = h.VertexPropertyOffset;
        var result = new MDLVertexDeclaration[checked((int)h.VertexPropertyCount)];
        for (int i = 0; i < result.Length; i++)
        {
            long start = r.BaseStream.Position;
            ushort pool = r.ReadUInt16(), format = r.ReadUInt16(), slot = r.ReadUInt16(), map = r.ReadUInt16(), offset = r.ReadUInt16();
            result[i] = new MDLVertexDeclaration { Pool = pool, Format = format, Slot = slot, MapIndex = map, ByteOffset = offset,
                Name = i < names.Length ? names[i] : $"ATTR{i}", RawBytes = data.AsSpan((int)start, 10).ToArray() };
        }
        return result;
    }

    private static ushort[][] ReadRemaps(BinaryReader r, int length, MDLHeader h)
    {
        if (h.RemapTableCount == 0) return [];
        RequireRange(length, h.RemapCountOffset, checked(h.RemapTableCount * 4), "remap counts");
        r.BaseStream.Position = h.RemapCountOffset;
        uint[] counts = new uint[checked((int)h.RemapTableCount)]; for (int i = 0; i < counts.Length; i++) counts[i] = r.ReadUInt32();
        uint total = 0; foreach (uint count in counts) total = checked(total + count);
        RequireRange(length, h.RemapTableOffset, checked(total * 2), "remap tables"); r.BaseStream.Position = h.RemapTableOffset;
        var result = new ushort[counts.Length][];
        for (int i = 0; i < result.Length; i++) { result[i] = new ushort[counts[i]]; for (int j = 0; j < result[i].Length; j++) result[i][j] = r.ReadUInt16(); }
        return result;
    }

    private static MDLBatch[] ReadBatches(BinaryReader r, int length, MDLHeader h)
    {
        RequireRange(length, h.BatchOffset, checked(h.BatchCount * 28), "batches"); r.BaseStream.Position = h.BatchOffset;
        var result = new MDLBatch[checked((int)h.BatchCount)];
        for (int i = 0; i < result.Length; i++) result[i] = new MDLBatch { meshGroupID = r.ReadUInt32(), materialID = r.ReadUInt32(), boneMapID = r.ReadUInt32(), vertexGroupID = r.ReadUInt32(), indiceCount = r.ReadUInt32(), indiceStart = r.ReadUInt32(), vertexCount = r.ReadUInt32() };
        return result;
    }

    private static void ReadDeclaration(BinaryReader r, VertexGroup g, MDLVertexDeclaration d, int declarationIndex,
        int width, int count, uint baseOffset, uint stride)
    {
        if (stride == 0 || d.ByteOffset >= stride) throw new InvalidDataException($"Vertex declaration {d.Name} lies outside its stride.");
        var rawValues = new byte[count][];
        g.RawVertexAttributes[declarationIndex] = rawValues;
        for (int i = 0; i < count; i++)
        {
            r.BaseStream.Position = baseOffset + (long)i * stride + d.ByteOffset;
            rawValues[i] = r.ReadBytes(width);
            r.BaseStream.Position = baseOffset + (long)i * stride + d.ByteOffset;
            switch (d.Name.ToUpperInvariant())
            {
                case "POS": g.Positions[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); break;
                case "NML": g.Normals[i] = ReadVector3ByFormat(r, d.Format); break;
                case "TGT": g.Tangents[i] = ReadVector4ByFormat(r, d.Format, true); break;
                case "BIX": g.BlendIndexLayers[d.MapIndex][i] = r.ReadBytes(4); break;
                case "BWT": g.BlendWeightLayers[d.MapIndex][i] = r.ReadBytes(4); break;
                case "COLOR": g.VertexColorLayers[d.MapIndex][i] = r.ReadBytes(4); break;
                default:
                    if (d.Name.StartsWith("color", StringComparison.OrdinalIgnoreCase))
                        g.VertexColorLayers[d.MapIndex][i] = r.ReadBytes(4);
                    else if (d.Name.StartsWith("map", StringComparison.OrdinalIgnoreCase))
                        g.UVLayers[d.MapIndex][i] = new Vector2((float)r.ReadHalf(), (float)r.ReadHalf());
                    break;
            }
        }
    }

    private static int DeclarationStorageWidth(MDLVertexDeclaration[] declarations, int index, uint stride)
    {
        MDLVertexDeclaration current = declarations[index];
        uint end = stride;
        foreach (MDLVertexDeclaration candidate in declarations)
            if (candidate.Pool == current.Pool && candidate.ByteOffset > current.ByteOffset)
                end = Math.Min(end, candidate.ByteOffset);
        if (end <= current.ByteOffset) throw new InvalidDataException($"Vertex declaration {index} has no storage width.");
        return checked((int)(end - current.ByteOffset));
    }

    private static Vector3 ReadVector3ByFormat(BinaryReader r, ushort format) => format switch
    {
        2 => new((float)r.ReadHalf(), (float)r.ReadHalf(), (float)r.ReadHalf()),
        6 => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
        _ => throw new InvalidDataException($"Unsupported vector3 vertex format {format}.")
    };

    private static Vector4 ReadVector4ByFormat(BinaryReader r, ushort format, bool signed) => format switch
    {
        8 when signed => new((sbyte)r.ReadByte() / 127f, (sbyte)r.ReadByte() / 127f, (sbyte)r.ReadByte() / 127f, (sbyte)r.ReadByte() / 127f),
        8 => new(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f),
        _ => throw new InvalidDataException($"Unsupported vector4 vertex format {format}.")
    };

    private static string[] ReadStringTable(BinaryReader r, int length, uint tableOffset, uint count, string label)
    {
        if (count == 0) return [];
        RequireRange(length, tableOffset, checked(count * 4), label);
        var result = new string[checked((int)count)];
        for (int i = 0; i < result.Length; i++) { r.BaseStream.Position = tableOffset + i * 4L; uint offset = r.ReadUInt32(); result[i] = ReadCString(r, length, offset, label); }
        return result;
    }

    private static string ReadCString(BinaryReader r, int length, uint offset, string label)
    {
        RequireRange(length, offset, 1, label); r.BaseStream.Position = offset; var bytes = new List<byte>();
        while (r.BaseStream.Position < length) { byte b = r.ReadByte(); if (b == 0) return Encoding.UTF8.GetString(bytes.ToArray()); bytes.Add(b); }
        throw new InvalidDataException($"Unterminated {label} string.");
    }

    private static int SemanticLayerCount(MDLVertexDeclaration[] declarations, string name) => declarations
        .Where(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(d => (int)d.MapIndex + 1).DefaultIfEmpty(0).Max();
    private static byte[][] NewByteArrays(int count) { var result = new byte[count][]; for (int i = 0; i < count; i++) result[i] = new byte[4]; return result; }
    private static byte[][][] NewByteArrayLayers(int layers, int count) => Enumerable.Range(0, layers).Select(_ => NewByteArrays(count)).ToArray();
    private static Vector2[][] NewVector2Layers(int layers, int count) => Enumerable.Range(0, layers).Select(_ => new Vector2[count]).ToArray();
    private static Vector3 ReadVector3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static void RequireRange(int length, uint offset, uint size, string label) { if ((ulong)offset + size > (ulong)length) throw new InvalidDataException($"MDL {label} range is outside the file."); }
}
