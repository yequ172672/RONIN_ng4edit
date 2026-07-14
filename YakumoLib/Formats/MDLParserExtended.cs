using System.Numerics;

namespace YakumoLib.Formats;

public static class MDLParserExtended
{
    private const uint MagicNumber = 4998221;

    public static MDLFullData Parse(byte[] data)
    {
        var reader = new BinaryReader(new MemoryStream(data));
        uint magic = reader.ReadUInt32();
        if (magic != MagicNumber)
            throw new InvalidDataException($"Not a valid MDL file (magic: 0x{magic:X8})");

        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        int headerSize = (int)Math.Min(data.Length, 200);
        byte[] originalHeader = new byte[headerSize];
        Array.Copy(data, originalHeader, headerSize);

        reader.BaseStream.Seek(40, SeekOrigin.Begin);
        uint lodCount = reader.ReadUInt32();
        uint lodDataOffset = reader.ReadUInt32();
        uint lodNameOffset = reader.ReadUInt32();

        reader.BaseStream.Seek(60, SeekOrigin.Begin);
        uint meshNameCount = reader.ReadUInt32();
        uint meshNameOffset = reader.ReadUInt32();
        uint batchCount = reader.ReadUInt32();
        uint batchOffset = reader.ReadUInt32();
        uint vertexPropertyCount = reader.ReadUInt32();
        uint vertexPropertyDataOffset = reader.ReadUInt32();
        uint vertexPropertyNameOffset = reader.ReadUInt32();
        reader.ReadUInt32();
        uint vertexHeaderStart = reader.ReadUInt32();
        uint vertexGroupCount = reader.ReadUInt32();
        uint vertexGroupSizesOffset = reader.ReadUInt32();
        uint vertexPoolOffsetOffsets = reader.ReadUInt32();
        uint indiceCount = reader.ReadUInt32();
        uint indiceOffset = reader.ReadUInt32();

        reader.BaseStream.Seek(vertexGroupSizesOffset, SeekOrigin.Begin);
        uint[] vtxPoolSizes = new uint[vertexGroupCount];
        for (int i = 0; i < vertexGroupCount; i++) vtxPoolSizes[i] = reader.ReadUInt32();

        reader.BaseStream.Seek(vertexPoolOffsetOffsets, SeekOrigin.Begin);
        uint mainPoolOffset = reader.ReadUInt32();
        uint exPoolOffset = reader.ReadUInt32();

        reader.BaseStream.Seek(vertexHeaderStart, SeekOrigin.Begin);
        uint mainStride = reader.ReadUInt32();
        uint exStride = reader.ReadUInt32();

        var groups = new List<VertexGroup>();
        reader.BaseStream.Seek(mainPoolOffset, SeekOrigin.Begin);
        for (int g = 0; g < vertexGroupCount; g++)
        {
            uint count = vtxPoolSizes[g];
            var vg = new VertexGroup
            {
                Positions = new Vector3[count],
                Normals = new Vector3[count],
                Tangents = new Vector4[count],
                BlendIndices = new byte[count][],
                BlendWeights = new byte[count][],
                UVs = new Vector2[count],
                VertexColors = new byte[count][],
                MainStride = mainStride,
                ExStride = exStride
            };
            for (int v = 0; v < count; v++)
            {
                vg.Positions[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                reader.ReadSingle();
                vg.Normals[v] = new Vector3((float)reader.ReadHalf(), (float)reader.ReadHalf(), (float)reader.ReadHalf());
                vg.Tangents[v] = new Vector4(
                    (sbyte)reader.ReadByte() / 127f, (sbyte)reader.ReadByte() / 127f,
                    (sbyte)reader.ReadByte() / 127f, (sbyte)reader.ReadByte() / 127f);
                vg.BlendIndices[v] = new byte[] { reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte() };
                vg.BlendWeights[v] = new byte[] { reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte() };
            }
            groups.Add(vg);
        }

        reader.BaseStream.Seek(exPoolOffset, SeekOrigin.Begin);
        for (int g = 0; g < Math.Min(vtxPoolSizes.Length, groups.Count); g++)
        {
            uint count = vtxPoolSizes[g];
            for (int v = 0; v < count; v++)
            {
                groups[g].UVs[v] = new Vector2((float)reader.ReadHalf(), (float)reader.ReadHalf());
                groups[g].VertexColors[v] = new byte[] { reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte() };
            }
        }

        ushort[] indices = [];
        if (indiceCount > 0 && indiceOffset > 0)
        {
            indices = new ushort[indiceCount];
            reader.BaseStream.Seek(indiceOffset, SeekOrigin.Begin);
            for (int i = 0; i < indiceCount; i++) indices[i] = reader.ReadUInt16();
        }

        reader.BaseStream.Seek(batchOffset, SeekOrigin.Begin);
        var batches = new List<MDLBatch>();
        for (int i = 0; i < batchCount; i++)
        {
            batches.Add(new MDLBatch
            {
                meshGroupID = reader.ReadUInt32(),
                materialID = reader.ReadUInt32(),
                boneMapID = reader.ReadUInt32(),
                vertexGroupID = reader.ReadUInt32(),
                indiceCount = reader.ReadUInt32(),
                indiceStart = reader.ReadUInt32(),
                vertexCount = reader.ReadUInt32()
            });
        }

        // Assign shared index buffer to groups
        if (groups.Count > 0) groups[0].Indices = indices;

        // Extract LOD name
        string lodName = "LOD1";
        if (lodNameOffset > 0 && lodNameOffset < data.Length)
        {
            int end = (int)lodNameOffset;
            while (end < data.Length && data[end] != 0) end++;
            if (end > (int)lodNameOffset)
                lodName = System.Text.Encoding.UTF8.GetString(data, (int)lodNameOffset, end - (int)lodNameOffset);
        }

        return new MDLFullData
        {
            Groups = groups.ToArray(),
            Batches = batches.ToArray(),
            MeshNames = [],
            LODName = lodName,
            OriginalHeader = originalHeader
        };
    }
}
