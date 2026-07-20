using System.Numerics;
using System.Text;

namespace YakumoLib.Formats;

public static class MDLWriter
{
    private const uint MagicNumber = 4998221;

    public static byte[] Write(MDLFullData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.OriginalBytes is null || data.OriginalBytes.Length == 0)
            throw new InvalidOperationException("Template-backed MDL writing requires the complete original MDL bytes.");

        MDLFullData template = MDLParserExtended.Parse(data.OriginalBytes);
        ValidatePatchContract(template, data);
        byte[] output = (byte[])data.OriginalBytes.Clone();
        using var stream = new MemoryStream(output, true);
        using var writer = new BinaryWriter(stream);
        WriteVertexPools(writer, data, template.VertexDeclarations, useLayoutOffsets: true);
        return output;
    }

    public static byte[] Rebuild(MDLFullData data)
        => BuildSections(data, preserveTemplateBytes: false);

    public static byte[] WriteTemplateReplacement(MDLFullData data)
    {
        if (data.OriginalBytes is null || data.OriginalBytes.Length == 0)
            throw new InvalidOperationException("Template replacement requires the complete original MDL bytes.");
        return BuildSections(data, preserveTemplateBytes: true);
    }

    private static byte[] BuildSections(MDLFullData data, bool preserveTemplateBytes)
    {
        ArgumentNullException.ThrowIfNull(data);
        ushort[] globalIndices = BuildGlobalIndexBuffer(data);
        data.Indices = globalIndices;
        ValidateRebuildContract(data);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        if (preserveTemplateBytes)
        {
            writer.Write(data.OriginalBytes);
            Align(writer, 16);
        }
        else writer.Write(new byte[128]);

        MDLHeader header = CloneHeaderScalars(data.Header);
        header.BoneCount = checked((uint)data.Bones.Length);
        header.BoneDataOffset = Position(writer);
        foreach (MDLBoneData bone in data.Bones) WriteBone(writer, bone);

        WriteStringTable(writer, data.Bones.Select(b => b.Name).ToArray(), out header.BoneNameTableOffset);

        header.BoneInfoCount = checked((uint)data.BoneInfos.Length);
        header.BoneInfoOffset = Position(writer);
        foreach (MDLBoneInfo info in data.BoneInfos)
        {
            WriteVector3(writer, info.UnknownA);
            WriteVector3(writer, info.UnknownB);
            writer.Write(info.RelatedBoneIndex);
        }

        Align(writer, 4);
        header.LODCount = checked((uint)data.LODs.Length);
        header.LODOffset = Position(writer);
        foreach (MDLLOD lod in data.LODs) WriteLod(writer, lod);
        WriteCString(writer, data.LODName, out header.LODNameOffset);

        header.MaterialCount = checked((uint)data.MaterialNames.Length);
        WriteStringTable(writer, data.MaterialNames, out header.MaterialNameTableOffset);
        header.MeshNameCount = checked((uint)data.MeshNames.Length);
        WriteStringTable(writer, data.MeshNames, out header.MeshNameTableOffset);

        Align(writer, 4);
        header.BatchCount = checked((uint)data.Batches.Length);
        header.BatchOffset = Position(writer);
        foreach (MDLBatch batch in data.Batches) WriteBatch(writer, batch);

        Align(writer, 2);
        header.VertexPropertyCount = checked((uint)data.VertexDeclarations.Length);
        header.VertexPropertyOffset = Position(writer);
        foreach (MDLVertexDeclaration declaration in data.VertexDeclarations)
        {
            writer.Write(declaration.Pool);
            writer.Write(declaration.Format);
            writer.Write(declaration.Slot);
            writer.Write(declaration.MapIndex);
            writer.Write(declaration.ByteOffset);
        }
        WriteStringTable(writer, data.VertexDeclarations.Select(d => d.Name).ToArray(), out header.VertexPropertyNameTableOffset);

        Align(writer, 4);
        header.VertexHeaderOffset = Position(writer);
        uint mainStride = data.Groups[0].MainStride;
        uint exStride = data.Groups[0].ExStride;
        writer.Write(mainStride);
        writer.Write(exStride);

        header.VertexGroupCount = checked((uint)data.Groups.Length);
        header.VertexGroupSizesOffset = Position(writer);
        foreach (VertexGroup group in data.Groups) writer.Write(checked((uint)group.Positions.Length));

        header.VertexPoolOffsetsOffset = Position(writer);
        long poolOffsetFields = writer.BaseStream.Position;
        writer.Write(0u);
        writer.Write(0u);

        Align(writer, 4);
        uint mainBase = Position(writer);
        WritePool(writer, data, data.VertexDeclarations, pool: 0, mainStride);
        Align(writer, 4);
        uint exBase = Position(writer);
        if (exStride != 0) WritePool(writer, data, data.VertexDeclarations, pool: 1, exStride);

        long endPools = writer.BaseStream.Position;
        writer.BaseStream.Position = poolOffsetFields;
        writer.Write(mainBase);
        writer.Write(exBase);
        writer.BaseStream.Position = endPools;

        Align(writer, 2);
        header.IndexOffset = Position(writer);
        header.IndexCount = checked((uint)globalIndices.Length);
        foreach (ushort index in globalIndices) writer.Write(index);

        Align(writer, 4);
        header.RemapTableCount = checked((uint)data.BoneRemapTables.Length);
        header.RemapCountOffset = Position(writer);
        foreach (ushort[] table in data.BoneRemapTables) writer.Write(checked((uint)table.Length));
        header.RemapTableOffset = Position(writer);
        foreach (ushort[] table in data.BoneRemapTables)
            foreach (ushort bone in table) writer.Write(bone);

        Align(writer, 4);
        header.FileSize = checked((uint)writer.BaseStream.Length);
        writer.BaseStream.Position = 0;
        writer.Write(MagicNumber);
        WriteHeader(writer, header);
        return stream.ToArray();
    }

    private static void ValidateRebuildContract(MDLFullData data)
    {
        if (data.Groups.Length == 0) throw new InvalidDataException("MDL rebuild requires at least one vertex group.");
        if (data.VertexDeclarations.Length == 0) throw new InvalidDataException("MDL rebuild requires verified template vertex declarations.");
        if (data.LODs.Length == 0) throw new InvalidDataException("MDL rebuild requires at least one LOD record.");
        if (data.Groups.Any(g => g.MainStride != data.Groups[0].MainStride || g.ExStride != data.Groups[0].ExStride))
            throw new NotSupportedException("MDL groups with different vertex strides cannot be represented by the verified shared vertex header.");

        foreach (MDLVertexDeclaration declaration in data.VertexDeclarations)
        {
            uint stride = declaration.Pool == 0 ? data.Groups[0].MainStride : data.Groups[0].ExStride;
            int width = DeclarationWidth(declaration);
            if (declaration.Pool > 1 || stride == 0 || declaration.ByteOffset + width > stride)
                throw new NotSupportedException($"Vertex declaration {declaration.Name} has an unverified pool/format/offset layout.");
        }

        for (int groupIndex = 0; groupIndex < data.Groups.Length; groupIndex++)
        {
            VertexGroup group = data.Groups[groupIndex];
            int count = group.Positions.Length;
            if (count == 0 || count > ushort.MaxValue)
                throw new NotSupportedException($"Vertex group {groupIndex} must contain 1..65535 vertices.");
            RequireAttributes(group, data.VertexDeclarations, count, groupIndex);
        }

        for (int i = 0; i < data.Batches.Length; i++)
        {
            MDLBatch batch = data.Batches[i];
            if (batch.vertexGroupID >= data.Groups.Length) throw new InvalidDataException($"Batch {i} references missing vertex group {batch.vertexGroupID}.");
            if (batch.materialID >= data.MaterialNames.Length && data.MaterialNames.Length != 0)
                throw new InvalidDataException($"Batch {i} references missing material {batch.materialID}.");
            if (batch.boneMapID >= data.BoneRemapTables.Length && data.BoneRemapTables.Length != 0)
                throw new InvalidDataException($"Batch {i} references missing bone remap table {batch.boneMapID}.");
            if ((ulong)batch.indiceStart + batch.indiceCount > (ulong)data.Indices.Length)
                throw new InvalidDataException($"Batch {i} index range is outside the global index buffer.");
            if (batch.vertexCount != batch.indiceCount / 3)
                throw new InvalidDataException($"Batch {i} primitive count must equal indiceCount / 3.");
            VertexGroup group = data.Groups[batch.vertexGroupID];
            foreach (ushort index in data.Indices.AsSpan((int)batch.indiceStart, (int)batch.indiceCount))
                if (index >= group.Positions.Length)
                    throw new InvalidDataException($"Batch {i} contains index {index} outside vertex group {batch.vertexGroupID}.");
        }
    }

    private static ushort[] BuildGlobalIndexBuffer(MDLFullData data)
    {
        if (data.Indices.Length > 0) return data.Indices;
        var indices = new List<ushort>();
        for (int i = 0; i < data.Batches.Length; i++)
        {
            MDLBatch batch = data.Batches[i];
            VertexGroup group = data.Groups[batch.vertexGroupID];
            data.Batches[i].indiceStart = checked((uint)indices.Count);
            data.Batches[i].indiceCount = checked((uint)group.Indices.Length);
            data.Batches[i].vertexCount = checked((uint)group.Indices.Length / 3);
            foreach (ushort index in group.Indices)
            {
                if (index >= group.Positions.Length) throw new InvalidDataException($"Batch {i} contains index {index} outside its vertex group.");
                indices.Add(index);
            }
        }
        return indices.ToArray();
    }

    private static void WritePool(BinaryWriter writer, MDLFullData data, MDLVertexDeclaration[] declarations, ushort pool, uint stride)
    {
        foreach (VertexGroup group in data.Groups)
        {
            foreach (int vertex in Enumerable.Range(0, group.Positions.Length))
            {
                long start = writer.BaseStream.Position;
                writer.Write(new byte[checked((int)stride)]);
                foreach (MDLVertexDeclaration declaration in declarations.Where(d => d.Pool == pool))
                {
                    writer.BaseStream.Position = start + declaration.ByteOffset;
                    WriteDeclaration(writer, group, declaration, vertex);
                }
                writer.BaseStream.Position = start + stride;
            }
        }
    }

    private static void WriteVertexPools(BinaryWriter writer, MDLFullData data, MDLVertexDeclaration[] declarations, bool useLayoutOffsets)
    {
        for (int g = 0; g < data.Groups.Length; g++)
        {
            VertexGroup group = data.Groups[g];
            foreach (MDLVertexDeclaration declaration in declarations)
            {
                uint stride = declaration.Pool == 0 ? group.MainStride : group.ExStride;
                uint baseOffset = declaration.Pool == 0 ? group.MainDataOffset : group.ExDataOffset;
                for (int vertex = 0; vertex < group.Positions.Length; vertex++)
                {
                    writer.BaseStream.Position = baseOffset + (long)vertex * stride + declaration.ByteOffset;
                    WriteDeclaration(writer, group, declaration, vertex);
                }
            }
        }
    }

    private static void ValidatePatchContract(MDLFullData template, MDLFullData edited)
    {
        if (edited.Groups.Length != template.Groups.Length) Reject("vertex group count");
        if (edited.Batches.Length != template.Batches.Length) Reject("batch count");
        if (edited.Bones.Length != template.Bones.Length) Reject("bone count");
        for (int g = 0; g < template.Groups.Length; g++)
        {
            VertexGroup a = template.Groups[g], b = edited.Groups[g];
            if (b.Positions.Length != a.Positions.Length) Reject($"vertex count in group {g}");
            if (!b.Indices.AsSpan().SequenceEqual(a.Indices)) Reject($"topology in group {g}");
            RequireAttributes(b, template.VertexDeclarations, a.Positions.Length, g);
        }
    }

    private static void RequireAttributes(VertexGroup group, MDLVertexDeclaration[] declarations, int count, int groupIndex)
    {
        foreach (MDLVertexDeclaration declaration in declarations)
        {
            string name = declaration.Name.ToUpperInvariant();
            if (name == "POS") RequireLength(group.Positions, count, "positions", groupIndex);
            else if (name == "NML") RequireLength(group.Normals, count, "normals", groupIndex);
            else if (name == "TGT") RequireLength(group.Tangents, count, "tangents", groupIndex);
            else if (name == "BIX") RequireLength(group.BlendIndices, count, "blend indices", groupIndex);
            else if (name == "BWT") RequireLength(group.BlendWeights, count, "blend weights", groupIndex);
            else if (name == "COLOR" || name.StartsWith("COLORSET", StringComparison.Ordinal)) RequireLength(group.VertexColors, count, "vertex colors", groupIndex);
            else if (declaration.Name.StartsWith("map", StringComparison.OrdinalIgnoreCase))
                RequireLength(group.UVs, count, "UVs", groupIndex);
            else throw new NotSupportedException($"Cannot rebuild unverified vertex declaration '{declaration.Name}' (format {declaration.Format}).");
        }
    }

    private static int DeclarationWidth(MDLVertexDeclaration declaration)
    {
        string name = declaration.Name.ToUpperInvariant();
        if (name == "POS") return 12;
        if (name == "NML") return declaration.Format == 2 ? 6 : declaration.Format == 6 ? 12 : throw new NotSupportedException($"Unsupported NML format {declaration.Format}.");
        if (name is "TGT" or "BIX" or "BWT" or "COLOR" || name.StartsWith("COLORSET", StringComparison.Ordinal)) return 4;
        if (declaration.Name.StartsWith("map", StringComparison.OrdinalIgnoreCase)) return 4;
        throw new NotSupportedException($"Cannot size unverified vertex declaration '{declaration.Name}'.");
    }

    private static void WriteDeclaration(BinaryWriter writer, VertexGroup group, MDLVertexDeclaration declaration, int vertex)
    {
        switch (declaration.Name.ToUpperInvariant())
        {
            case "POS": WriteVector3(writer, group.Positions[vertex]); break;
            case "NML": WriteVector3(writer, group.Normals[vertex], declaration.Format); break;
            case "TGT": WriteVector4(writer, group.Tangents[vertex], declaration.Format); break;
            case "BIX": WriteBytes4(writer, group.BlendIndexLayers[declaration.MapIndex][vertex]); break;
            case "BWT": WriteBytes4(writer, group.BlendWeightLayers[declaration.MapIndex][vertex]); break;
            case "COLOR": WriteBytes4(writer, group.VertexColorLayers[declaration.MapIndex][vertex]); break;
            default:
                if (declaration.Name.StartsWith("colorSet", StringComparison.OrdinalIgnoreCase))
                    WriteBytes4(writer, group.VertexColorLayers[declaration.MapIndex][vertex]);
                else if (declaration.Name.StartsWith("map", StringComparison.OrdinalIgnoreCase))
                {
                    writer.Write((Half)group.UVLayers[declaration.MapIndex][vertex].X);
                    writer.Write((Half)group.UVLayers[declaration.MapIndex][vertex].Y);
                }
                else throw new NotSupportedException($"Cannot emit unverified declaration '{declaration.Name}'.");
                break;
        }
    }

    private static void WriteHeader(BinaryWriter writer, MDLHeader h)
    {
        uint[] fields = [h.FileSize, h.Unknown08, h.Version, h.BoneCount, h.Unknown14, h.BoneDataOffset, h.BoneNameTableOffset,
            h.BoneInfoCount, h.BoneInfoOffset, h.LODCount, h.LODOffset, h.LODNameOffset, h.MaterialCount, h.MaterialNameTableOffset,
            h.MeshNameCount, h.MeshNameTableOffset, h.BatchCount, h.BatchOffset, h.VertexPropertyCount, h.VertexPropertyOffset,
            h.VertexPropertyNameTableOffset, h.Unknown58, h.VertexHeaderOffset, h.VertexGroupCount, h.VertexGroupSizesOffset,
            h.VertexPoolOffsetsOffset, h.IndexCount, h.IndexOffset, h.RemapTableCount, h.RemapCountOffset, h.RemapTableOffset];
        foreach (uint field in fields) writer.Write(field);
    }

    private static MDLHeader CloneHeaderScalars(MDLHeader h) => new() { Unknown08 = h.Unknown08, Version = h.Version, Unknown14 = h.Unknown14, Unknown58 = h.Unknown58 };
    private static void WriteBone(BinaryWriter w, MDLBoneData b) { w.Write(b.ParentIndex); WriteVector3(w, b.UnknownA); WriteVector3(w, b.UnknownB); WriteVector3(w, b.UnknownC); WriteVector3(w, b.Translation); WriteVector3(w, b.Rotation); WriteVector3(w, b.Scale); }
    private static void WriteBatch(BinaryWriter w, MDLBatch b) { w.Write(b.meshGroupID); w.Write(b.materialID); w.Write(b.boneMapID); w.Write(b.vertexGroupID); w.Write(b.indiceCount); w.Write(b.indiceStart); w.Write(b.vertexCount); }
    private static void WriteLod(BinaryWriter w, MDLLOD lod) { w.Write(lod.Unknown00); byte[] name = new byte[4]; Encoding.ASCII.GetBytes(lod.Name.AsSpan(0, Math.Min(4, lod.Name.Length)), name); w.Write(name); for (int i = 0; i < 8; i++) w.Write(i < lod.UnknownValues.Length ? lod.UnknownValues[i] : 0u); w.Write(lod.BatchCount); }
    private static void WriteStringTable(BinaryWriter w, string[] values, out uint tableOffset) { Align(w, 4); tableOffset = Position(w); long fields = w.BaseStream.Position; foreach (string _ in values) w.Write(0u); for (int i = 0; i < values.Length; i++) { uint offset = Position(w); long end; WriteCString(w, values[i], out _); end = w.BaseStream.Position; w.BaseStream.Position = fields + i * 4L; w.Write(offset); w.BaseStream.Position = end; } }
    private static void WriteCString(BinaryWriter w, string value, out uint offset) { offset = Position(w); w.Write(Encoding.UTF8.GetBytes(value ?? string.Empty)); w.Write((byte)0); }
    private static void Align(BinaryWriter w, int alignment) { while (w.BaseStream.Position % alignment != 0) w.Write((byte)0); }
    private static uint Position(BinaryWriter w) => checked((uint)w.BaseStream.Position);
    private static void WriteVector3(BinaryWriter w, Vector3 value) { w.Write(value.X); w.Write(value.Y); w.Write(value.Z); }
    private static void WriteVector3(BinaryWriter w, Vector3 value, ushort format) { if (format == 2) { w.Write((Half)value.X); w.Write((Half)value.Y); w.Write((Half)value.Z); } else if (format == 6) WriteVector3(w, value); else throw new NotSupportedException($"Unsupported vector3 format {format}."); }
    private static void WriteVector4(BinaryWriter w, Vector4 value, ushort format) { if (format != 8) throw new NotSupportedException($"Unsupported vector4 format {format}."); w.Write(unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(value.X * 127f), -127, 127))); w.Write(unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(value.Y * 127f), -127, 127))); w.Write(unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(value.Z * 127f), -127, 127))); w.Write(unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(value.W * 127f), -127, 127))); }
    private static void WriteBytes4(BinaryWriter w, byte[] value) { if (value is null || value.Length < 4) throw new InvalidDataException("Four-byte vertex attribute is incomplete."); w.Write(value, 0, 4); }
    private static void RequireLength<T>(T[] values, int expected, string name, int group) { if (values.Length != expected) throw new InvalidDataException($"MDL {name} count in group {group} must be {expected}."); }
    private static void Reject(string structure) => throw new NotSupportedException($"Template-safe MDL replacement does not support changing {structure}; use MDLWriter.Rebuild for verified topology rebuilds.");
}
