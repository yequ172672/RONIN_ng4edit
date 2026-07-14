using System.Numerics;

namespace YakumoLib.Formats;

public sealed class MDLFullData
{
    public VertexGroup[] Groups { get; set; } = [];
    public MDLBatch[] Batches { get; set; } = [];
    public string[] MeshNames { get; set; } = [];
    public MDLBoneData[]? Bones { get; set; }
    public string[]? VertexProperties { get; set; }
    public string LODName { get; set; } = "LOD1";
    public byte[]? OriginalHeader { get; set; }
}

public sealed class VertexGroup
{
    public Vector3[] Positions { get; set; } = [];
    public Vector3[] Normals { get; set; } = [];
    public Vector4[] Tangents { get; set; } = [];
    public byte[][] BlendIndices { get; set; } = [];
    public byte[][] BlendWeights { get; set; } = [];
    public Vector2[] UVs { get; set; } = [];
    public byte[][] VertexColors { get; set; } = [];
    public ushort[] Indices { get; set; } = [];
    public uint MainStride { get; set; }
    public uint ExStride { get; set; }
}

public struct MDLBatch
{
    public uint meshGroupID, materialID, boneMapID, vertexGroupID, indiceCount, indiceStart, vertexCount;
}

public sealed class MDLBoneData
{
    public required string Name { get; init; }
    public int ParentIndex { get; init; }
    public Matrix4x4 LocalTransform { get; init; }
    public Matrix4x4 InverseBindPose { get; init; }
}
