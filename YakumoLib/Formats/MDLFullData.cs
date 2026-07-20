using System.Numerics;

namespace YakumoLib.Formats;

public sealed class MDLFullData
{
    public required MDLHeader Header { get; init; }
    public MDLLOD[] LODs { get; set; } = [];
    public VertexGroup[] Groups { get; set; } = [];
    public MDLBatch[] Batches { get; set; } = [];
    public string[] MeshNames { get; set; } = [];
    public string[] MaterialNames { get; set; } = [];
    public MDLBoneData[] Bones { get; set; } = [];
    public MDLBoneInfo[] BoneInfos { get; set; } = [];
    public MDLVertexDeclaration[] VertexDeclarations { get; set; } = [];
    public ushort[][] BoneRemapTables { get; set; } = [];
    public ushort[] Indices { get; set; } = [];
    public string LODName => LODs.Length == 0 ? "LOD1" : LODs[0].Name;
    public required byte[] OriginalBytes { get; init; }

    public ReadOnlySpan<ushort> GetBatchIndices(int batchIndex)
    {
        if ((uint)batchIndex >= (uint)Batches.Length) throw new ArgumentOutOfRangeException(nameof(batchIndex));
        MDLBatch batch = Batches[batchIndex];
        if ((ulong)batch.indiceStart + batch.indiceCount > (ulong)Indices.Length)
            throw new InvalidDataException($"Batch {batchIndex} index range is outside the global index buffer.");
        return Indices.AsSpan(checked((int)batch.indiceStart), checked((int)batch.indiceCount));
    }
}

public sealed class MDLHeader
{
    public uint FileSize, Unknown08, Version, BoneCount, Unknown14, BoneDataOffset, BoneNameTableOffset;
    public uint BoneInfoCount, BoneInfoOffset, LODCount, LODOffset, LODNameOffset, MaterialCount, MaterialNameTableOffset;
    public uint MeshNameCount, MeshNameTableOffset, BatchCount, BatchOffset, VertexPropertyCount;
    public uint VertexPropertyOffset, VertexPropertyNameTableOffset, Unknown58, VertexHeaderOffset, VertexGroupCount;
    public uint VertexGroupSizesOffset, VertexPoolOffsetsOffset, IndexCount, IndexOffset, RemapTableCount;
    public uint RemapCountOffset, RemapTableOffset;
}

public sealed class MDLLOD
{
    public uint Unknown00 { get; init; }
    public string Name { get; init; } = "LOD1";
    public uint[] UnknownValues { get; init; } = new uint[8];
    public uint BatchCount { get; init; }
    public byte[] RawBytes { get; init; } = [];
}

public sealed class VertexGroup
{
    public Dictionary<int, byte[][]> RawVertexAttributes { get; set; } = [];
    public Vector3[] Positions { get; set; } = [];
    public Vector3[] Normals { get; set; } = [];
    public Vector4[] Tangents { get; set; } = [];
    public byte[][][] BlendIndexLayers { get; set; } = [];
    public byte[][][] BlendWeightLayers { get; set; } = [];
    public byte[][] BlendIndices
    {
        get => BlendIndexLayers.Length == 0 ? [] : BlendIndexLayers[0];
        set => BlendIndexLayers = ReplaceFirstLayer(BlendIndexLayers, value);
    }
    public byte[][] BlendWeights
    {
        get => BlendWeightLayers.Length == 0 ? [] : BlendWeightLayers[0];
        set => BlendWeightLayers = ReplaceFirstLayer(BlendWeightLayers, value);
    }
    public string[] ImportedBonePalette { get; set; } = [];
    public Vector2[][] UVLayers { get; set; } = [];
    public byte[][][] VertexColorLayers { get; set; } = [];
    public Vector2[] UVs
    {
        get => UVLayers.Length == 0 ? [] : UVLayers[0];
        set => UVLayers = ReplaceFirstLayer(UVLayers, value);
    }
    public byte[][] VertexColors
    {
        get => VertexColorLayers.Length == 0 ? [] : VertexColorLayers[0];
        set => VertexColorLayers = ReplaceFirstLayer(VertexColorLayers, value);
    }
    public ushort[] Indices { get; set; } = [];
    public uint MainStride { get; init; }
    public uint ExStride { get; init; }
    public uint MainDataOffset { get; init; }
    public uint ExDataOffset { get; init; }

    private static T[][] ReplaceFirstLayer<T>(T[][] layers, T[] value)
    {
        if (layers.Length == 0) return [value];
        layers[0] = value;
        return layers;
    }
}

public struct MDLBatch
{
    public uint meshGroupID, materialID, boneMapID, vertexGroupID, indiceCount, indiceStart, vertexCount;
}

public sealed class MDLBoneData
{
    public string Name { get; init; } = "";
    public uint ParentIndex { get; init; }
    public Vector3 UnknownA { get; init; }
    public Vector3 UnknownB { get; init; }
    public Vector3 UnknownC { get; init; }
    public Vector3 Translation { get; init; }
    public Vector3 Rotation { get; init; }
    public Vector3 Scale { get; init; }
    public Matrix4x4 LocalTransform => Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromYawPitchRoll(Rotation.Y, Rotation.X, Rotation.Z) * Matrix4x4.CreateTranslation(Translation);
}

public sealed class MDLBoneInfo
{
    public Vector3 UnknownA { get; init; }
    public Vector3 UnknownB { get; init; }
    public uint RelatedBoneIndex { get; init; }
}

public sealed class MDLVertexDeclaration
{
    public ushort Pool { get; init; }
    public ushort Format { get; init; }
    public ushort Slot { get; init; }
    public ushort MapIndex { get; init; }
    public ushort ByteOffset { get; init; }
    public string Name { get; init; } = "";
    public byte[] RawBytes { get; init; } = [];
}
