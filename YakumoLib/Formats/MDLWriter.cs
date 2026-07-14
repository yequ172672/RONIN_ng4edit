using System.Numerics;

namespace YakumoLib.Formats;

public static class MDLWriter
{
    private const uint MagicNumber = 4998221;
    private const int HeaderSize = 200;

    public static byte[] Write(MDLFullData data) => WriteFresh(data);

    private static byte[] WriteFresh(MDLFullData data)
    {
        if (data.Groups is null || data.Groups.Length == 0)
            throw new InvalidOperationException("No vertex group data.");

        int groups = data.Groups.Length;
        uint mainStride = data.Groups[0].MainStride > 0 ? data.Groups[0].MainStride : 36u;
        uint exStride = data.Groups[0].ExStride > 0 ? data.Groups[0].ExStride : 8u;
        uint mainPoolSize = 0, exPoolSize = 0, totalIndices = 0;
        foreach (var g in data.Groups)
        {
            mainPoolSize += (uint)(g.Positions.Length * mainStride);
            exPoolSize += (uint)(g.UVs.Length * exStride);
            if (g.Indices != null) totalIndices += (uint)g.Indices.Length;
        }

        uint vtxSizesOff = (uint)HeaderSize;
        uint vtxHeaderStart = vtxSizesOff + (uint)(groups * 4); // 200 + 4 = 204 for 1 group
        uint vtxPoolOffOff = vtxHeaderStart + 8; // 212
        uint mainPoolOff = vtxPoolOffOff + 8; // 220
        uint exPoolOff = mainPoolOff + mainPoolSize;
        uint idxOff = exPoolOff + exPoolSize;
        int batchCount = data.Batches?.Length ?? 0;
        uint batchOff = idxOff + totalIndices * 2;
        uint batchSz = (uint)(batchCount * 28);
        uint nameTblOff = batchOff + batchSz;
        uint vtxPropCnt = 7;
        uint vtxPropDataOff = nameTblOff + 12;
        uint vtxPropNameOff = vtxPropDataOff + vtxPropCnt * 10;
        uint strDataOff = vtxPropNameOff + vtxPropCnt * 4;

        using var ms = new MemoryStream((int)(strDataOff + 512));
        using var w = new BinaryWriter(ms);

        // header up to 200
        w.BaseStream.Seek(0, SeekOrigin.Begin);
        w.Write(MagicNumber); w.Write(0u); w.Write(0u);
        w.Write(3u); w.Write(3u); w.Write(0u); w.Write(0u);
        w.Write(128u); w.Write(356u); w.Write(1u);
        w.Write((uint)1); w.Write((uint)200); w.Write(nameTblOff);
        w.Write(1u); w.Write(0u);
        w.Write((uint)(data.MeshNames?.Length ?? 0));
        w.Write(nameTblOff + 8);
        w.Write((uint)batchCount); w.Write(batchOff);
        w.Write(vtxPropCnt); w.Write(vtxPropDataOff); w.Write(vtxPropNameOff);
        w.Write(2u);
        w.Write(vtxHeaderStart); // vertex header (strides)
        w.Write((uint)groups); w.Write(vtxSizesOff); w.Write(vtxPoolOffOff);
        w.Write(totalIndices); w.Write(idxOff);
        w.BaseStream.Seek(HeaderSize, SeekOrigin.Begin);
        w.Write(new byte[Math.Max(0, HeaderSize - (int)w.BaseStream.Position)]);

        // strides (must be written BEFORE pool offsets)
        w.BaseStream.Seek(vtxHeaderStart, SeekOrigin.Begin);
        w.Write(mainStride); w.Write(exStride);

        // vtx sizes
        w.BaseStream.Seek(vtxSizesOff, SeekOrigin.Begin);
        foreach (var g in data.Groups) w.Write((uint)g.Positions.Length);

        // pool offsets
        w.BaseStream.Seek(vtxPoolOffOff, SeekOrigin.Begin);
        w.Write(mainPoolOff); w.Write(exPoolOff);

        // main pool
        w.BaseStream.Seek(mainPoolOff, SeekOrigin.Begin);
        foreach (var g in data.Groups) for (int v = 0; v < g.Positions.Length; v++)
        {
            w.Write(g.Positions[v].X); w.Write(g.Positions[v].Y); w.Write(g.Positions[v].Z); w.Write(1f);
            w.Write((Half)g.Normals[v].X); w.Write((Half)g.Normals[v].Y); w.Write((Half)g.Normals[v].Z);
            var t = g.Tangents != null && v < g.Tangents.Length ? g.Tangents[v] : new Vector4(0, 0, 0, 1);
            w.Write((byte)(sbyte)(t.X * 127)); w.Write((byte)(sbyte)(t.Y * 127));
            w.Write((byte)(sbyte)(t.Z * 127)); w.Write((byte)(sbyte)(t.W * 127));
            if (g.BlendIndices != null && v < g.BlendIndices.Length && g.BlendIndices[v] != null)
                { var bi = g.BlendIndices[v]; for (int k = 0; k < 4; k++) w.Write(k < bi.Length ? bi[k] : (byte)0); }
            else w.Write(0u);
            if (g.BlendWeights != null && v < g.BlendWeights.Length && g.BlendWeights[v] != null)
                { var bw = g.BlendWeights[v]; for (int k = 0; k < 4; k++) w.Write(k < bw.Length ? bw[k] : (byte)0); }
            else w.Write(0u);
        }

        // ex pool
        w.BaseStream.Seek(exPoolOff, SeekOrigin.Begin);
        foreach (var g in data.Groups) for (int v = 0; v < g.UVs.Length; v++)
        {
            w.Write((Half)g.UVs[v].X); w.Write((Half)g.UVs[v].Y);
            if (g.VertexColors != null && v < g.VertexColors.Length && g.VertexColors[v] != null)
                { var c = g.VertexColors[v]; for (int k = 0; k < 4; k++) w.Write(k < c.Length ? c[k] : (byte)255); }
            else w.Write(0xFFFFFFFFu);
        }

        // indices
        w.BaseStream.Seek(idxOff, SeekOrigin.Begin);
        uint off = 0;
        foreach (var g in data.Groups) { if (g.Indices != null) { foreach (var idx in g.Indices) w.Write((ushort)(idx + off)); off += (uint)g.Positions.Length; } }

        // batches
        w.BaseStream.Seek(batchOff, SeekOrigin.Begin);
        if (data.Batches != null) foreach (var b in data.Batches)
        { w.Write(b.meshGroupID); w.Write(b.materialID); w.Write(b.boneMapID); w.Write(b.vertexGroupID); w.Write(b.indiceCount); w.Write(b.indiceStart); w.Write(b.vertexCount); }

        // name ptr table
        w.BaseStream.Seek(nameTblOff, SeekOrigin.Begin);
        uint baseStr = strDataOff;
        w.Write(baseStr); w.Write(baseStr + 5); w.Write(baseStr + 12);

        // vtx property table
        w.BaseStream.Seek(vtxPropDataOff, SeekOrigin.Begin);
        WriteProp(w, 0, 6, 0, 0); WriteProp(w, 2, 1, 16, 0); WriteProp(w, 2, 0, 24, 7);
        WriteProp(w, 5, 0, 28, 0); WriteProp(w, 4, 0, 32, 0); WriteProp(w, 3, 0, 0, 0); WriteProp(w, 8, 6, 4, 0);

        // vtx prop name ptrs
        w.BaseStream.Seek(vtxPropNameOff, SeekOrigin.Begin);
        uint pn = strDataOff + 64; // after bone data
        w.Write(pn); w.Write(pn + 4); w.Write(pn + 8); w.Write(pn + 12); w.Write(pn + 16); w.Write(pn + 21); w.Write(pn + 26);

        // string data
        w.BaseStream.Seek(strDataOff, SeekOrigin.Begin);
        WriteStr(w, data.LODName ?? "LOD1");
        WriteStr(w, data.MeshNames?.Length > 0 ? data.MeshNames[0] : "Mesh");
        WriteStr(w, "Parts");
        w.Write(1u); w.Write((ushort)2);
        if (data.Bones != null) foreach (var b in data.Bones) WriteStr(w, b.Name);
        else WriteStr(w, "root");
        // go to prop name area
        w.BaseStream.Seek(pn, SeekOrigin.Begin);
        WriteStr(w, "POS"); WriteStr(w, "NML"); WriteStr(w, "TGT"); WriteStr(w, "BIX");
        WriteStr(w, "BWT"); WriteStr(w, "map1"); WriteStr(w, "color");

        return ms.ToArray();
    }

    private static void WriteProp(BinaryWriter w, ushort sem, ushort fmt, uint off, ushort unk)
    { w.Write(sem); w.Write(fmt); w.Write(off); w.Write(unk); }

    private static void WriteStr(BinaryWriter w, string s)
    { byte[] b = System.Text.Encoding.UTF8.GetBytes(s); w.Write(b); w.Write((byte)0); }
}
