using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace YakumoLib.Formats;

/// <summary>
/// ASCII FBX importer with full support for:
/// - Mesh geometry (positions, normals, UVs, tangents)
/// - Skinning (Deformer/Cluster → BoneIndices + BoneWeights)
/// - Bone hierarchy
/// - Multiple meshes
/// </summary>
public static class FbxToMdlConverter
{
    public static byte[] Convert(string fbxPath, byte[] originalMdlData)
    {
        var fbx = File.ReadAllText(fbxPath);
        var model = ParseFbx(fbx);
        if (originalMdlData != null && originalMdlData.Length >= 200)
        {
            model.OriginalHeader = new byte[200];
            Array.Copy(originalMdlData, model.OriginalHeader, 200);
        }
        return MDLWriter.Write(model);
    }

    public static MDLFullData ParseFbx(string fbx)
    {
        // ── Step 1: Parse connections ──
        var conns = ParseConnections(fbx);

        // ── Step 2: Extract geometry blocks ──
        var geoBlocks = Regex.Matches(fbx, @"Geometry:\s*\d+,\s*""[^""]*"",\s*""Mesh""\s*\{", RegexOptions.Singleline);
        var sections = Regex.Split(fbx, @"Geometry:\s*\d+,\s*""[^""]*"",\s*""Mesh""\s*\{", RegexOptions.Singleline)
            .Where(s => s.Contains("Vertices:")).ToList();

        if (geoBlocks.Count == 0 || sections.Count == 0)
            throw new InvalidDataException("No mesh geometry found in FBX.");

        // Extract geometry IDs from the matches
        var geoIds = geoBlocks.Select(m =>
        {
            var idMatch = Regex.Match(m.Value, @"\d+");
            return idMatch.Success ? long.Parse(idMatch.Value) : 0L;
        }).ToArray();

        // ── Step 3: Parse bone names first (needed for skinning resolution) ──
        var boneNames = ExtractBoneNames(fbx, conns);

        // ── Step 4: Parse skinning data with known bone names ──
        var skinData = ParseSkinningData(fbx, conns, boneNames);

        // ── Step 5: Process each mesh ──
        var allPos = new List<Vector3>();
        var allNml = new List<Vector3>();
        var allUv = new List<Vector2>();
        var allTan = new List<Vector4>();
        var allIdx = new List<ushort>();
        var allBi = new List<byte[]>();  // blend indices
        var allBw = new List<byte[]>();  // blend weights
        var batches = new List<MDLBatch>();
        int vertexOffset = 0;

        for (int si = 0; si < sections.Count; si++)
        {
            string section = sections[si];
            long geoId = si < geoIds.Length ? geoIds[si] : 0;

            // Vertices
            var vm = Regex.Match(section, @"Vertices:\s*\*\d+\s*\{\s*a:\s*([^}]+)\}");
            if (!vm.Success) continue;
            var verts = ParseFloats(vm.Groups[1].Value);
            int vc = verts.Count / 3;
            if (vc == 0) continue;

            // PolygonVertexIndex
            var pm = Regex.Match(section, @"PolygonVertexIndex:\s*\*\d+\s*\{\s*a:\s*([^}]+)\}");
            if (!pm.Success) continue;
            var rawIdx = ParseInts(pm.Groups[1].Value);

            // Triangulate
            var indices = Triangulate(rawIdx, vertexOffset);

            // Normals (ByPolygonVertex → per-vertex)
            var normals = ReadLayerVector3(section, "Normals", rawIdx, vc);

            // UVs (ByPolygonVertex → per-vertex)
            var uvs = ReadLayerVector2(section, "UV", rawIdx, vc, true);

            // Tangents (if present)
            var tangents = ReadLayerVector4(section, "Tangents", rawIdx, vc);

            // Build position array
            var positions = new List<Vector3>();
            for (int i = 0; i < vc; i++)
                positions.Add(new Vector3(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]));

            // Skinning data for this geometry
            var blendIndices = new byte[vc][];
            var blendWeights = new byte[vc][];
            for (int v = 0; v < vc; v++)
            {
                blendIndices[v] = new byte[4];
                blendWeights[v] = new byte[4];
                // Default: first bone with full weight (rigid binding)
                blendWeights[v][0] = 255;
            }

            // Apply skinning from parsed data
            if (skinData.TryGetValue(geoId, out var geoClusters) && geoClusters.Count > 0 && boneNames.Count > 0)
            {
                foreach (var cluster in geoClusters)
                {
                    int boneIdx = cluster.BoneIndex;
                    if (boneIdx >= boneNames.Count) continue;

                    for (int i = 0; i < cluster.ControlPoints.Length && i < cluster.Weights.Length; i++)
                    {
                        int cp = cluster.ControlPoints[i];
                        if (cp < 0 || cp >= vc) continue;

                        float weight = cluster.Weights[i];
                        // Find slot and insert (keep highest weights, max 4)
                        int slot = -1;
                        float minW = float.MaxValue;
                        for (int k = 0; k < 4; k++)
                        {
                            float existingW = blendWeights[cp][k] / 255f;
                            if (existingW < minW) { minW = existingW; slot = k; }
                            if (blendIndices[cp][k] == boneIdx) { slot = k; break; }
                        }
                        if (slot >= 0 && weight > minW)
                        {
                            blendIndices[cp][slot] = (byte)boneIdx;
                            blendWeights[cp][slot] = (byte)Math.Clamp((int)(weight * 255), 0, 255);
                        }
                    }
                }
                // Normalize weights per vertex (sum to 255)
                for (int v = 0; v < vc; v++)
                {
                    int sum = blendWeights[v][0] + blendWeights[v][1] + blendWeights[v][2] + blendWeights[v][3];
                    if (sum > 0)
                    {
                        float scale = 255f / sum;
                        for (int k = 0; k < 4; k++)
                            blendWeights[v][k] = (byte)Math.Clamp((int)(blendWeights[v][k] * scale), 0, 255);
                    }
                }
            }

            allPos.AddRange(positions);
            allNml.AddRange(normals);
            allUv.AddRange(uvs);
            allTan.AddRange(tangents);
            allIdx.AddRange(indices);
            allBi.AddRange(blendIndices);
            allBw.AddRange(blendWeights);

            batches.Add(new MDLBatch
            {
                meshGroupID = (uint)(batches.Count),
                materialID = 0,
                boneMapID = 0,
                vertexGroupID = 0,
                indiceCount = (uint)indices.Count,
                indiceStart = (uint)(allIdx.Count - indices.Count),
                vertexCount = (uint)vc
            });
            vertexOffset += vc;
        }

        if (allPos.Count == 0)
            throw new InvalidDataException("No mesh geometry found in FBX.");

        return new MDLFullData
        {
            Groups = [new VertexGroup
            {
                Positions = allPos.ToArray(),
                Normals = allNml.ToArray(),
                UVs = allUv.ToArray(),
                Tangents = allTan.ToArray(),
                Indices = allIdx.ToArray(),
                BlendIndices = allBi.ToArray(),
                BlendWeights = allBw.ToArray(),
                VertexColors = [],
                MainStride = 36,
                ExStride = 8
            }],
            Batches = batches.ToArray(),
            MeshNames = batches.Select((_, i) => $"Mesh_{i}").ToArray(),
            Bones = boneNames.Select(n => new MDLBoneData { Name = n }).ToArray(),
            LODName = "LOD0"
        };
    }

    // ── Skinning helpers ──

    private class FbxCluster
    {
        public string BoneName = "";
        public int BoneIndex = -1;
        public int[] ControlPoints = [];
        public float[] Weights = [];
    }

    /// <summary>Parse all Deformer/SubDeformer(Cluster) blocks from FBX text</summary>
    private static Dictionary<long, List<FbxCluster>> ParseSkinningData(string fbx,
        Dictionary<(long Child, long Parent, string Type), string> connections,
        List<string> boneNames)
    {
        var result = new Dictionary<long, List<FbxCluster>>();
        var nameToBoneIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < boneNames.Count; i++)
            nameToBoneIdx[boneNames[i]] = i;
        var deformerToGeo = new Dictionary<long, long>();

        // Build deformer→geometry map from connections
        foreach (var kvp in connections)
        {
            var (child, parent, type) = kvp.Key;
            if (type == "OO")
            {
                // A Deformer (Skin) connected to something might be child of a Geometry
                if (parent != 0) deformerToGeo[child] = parent;
            }
        }

        // Parse all Deformer blocks
        var deformerMatches = Regex.Matches(fbx, @"Deformer:\s*(\d+),\s*""[^""]*"",\s*""Skin""\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);

        foreach (Match dm in deformerMatches)
        {
            long deformerId = long.Parse(dm.Groups[1].Value);
            string deformerBody = dm.Groups[2].Value;
            if (!deformerToGeo.TryGetValue(deformerId, out var geometryId) || geometryId == 0) continue;

            // Parse SubDeformer (Cluster) blocks
            var clusterMatches = Regex.Matches(deformerBody,
                @"SubDeformer:\s*(\d+),\s*""SubDeformer::([^""]*)"",\s*""Cluster""\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);

            foreach (Match cm in clusterMatches)
            {
                string boneName = cm.Groups[2].Value;
                string clusterBody = cm.Groups[3].Value;

                var idxMatch = Regex.Match(clusterBody, @"Indexes:\s*\*\d+\s*\{\s*a:\s*([^}]+)\}");
                var wMatch = Regex.Match(clusterBody, @"Weights:\s*\*\d+\s*\{\s*a:\s*([^}]+)\}");
                if (!idxMatch.Success || !wMatch.Success) continue;

                var cluster = new FbxCluster
                {
                    BoneName = boneName,
                    BoneIndex = nameToBoneIdx.TryGetValue(boneName, out int bi) ? bi : -1,
                    ControlPoints = ParseInts(idxMatch.Groups[1].Value).ToArray(),
                    Weights = ParseFloats(wMatch.Groups[1].Value).ToArray()
                };

                if (!result.TryGetValue(geometryId, out var list))
                { list = []; result[geometryId] = list; }
                list.Add(cluster);
            }
        }
        return result;
    }

    /// <summary>
    /// Extract bone names from the FBX scene by finding Model nodes with "LimbNode" type.
    /// </summary>
    private static List<string> ExtractBoneNames(string fbx,
        Dictionary<(long Child, long Parent, string Type), string> connections)
    {
        var boneNames = new List<string>();
        var allBoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find all Model nodes of type "LimbNode"
        var modelMatches = Regex.Matches(fbx,
            @"Model:\s*(\d+),\s*""([^""]*)"",\s*""LimbNode""\s*\{", RegexOptions.Singleline);

        foreach (Match m in modelMatches)
        {
            string name = m.Groups[2].Value;
            if (!string.IsNullOrEmpty(name) && !name.StartsWith("SubDeformer") && allBoneNames.Add(name))
                boneNames.Add(name);
        }

        // If no explicit LimbNodes found, extract from SubDeformer:: names
        if (boneNames.Count == 0)
        {
            var sdMatches = Regex.Matches(fbx,
                @"SubDeformer:\s*\d+,\s*""SubDeformer::([^""]*)""", RegexOptions.Singleline);
            foreach (Match m in sdMatches)
            {
                string name = m.Groups[1].Value;
                if (!string.IsNullOrEmpty(name) && allBoneNames.Add(name))
                    boneNames.Add(name);
            }
        }

        return boneNames;
    }

    // ── Connection parsing ──

    private static Dictionary<(long Child, long Parent, string Type), string> ParseConnections(string fbx)
    {
        var conns = new Dictionary<(long, long, string), string>();
        var connSection = Regex.Match(fbx, @"Connections:\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);
        if (!connSection.Success) return conns;

        string body = connSection.Groups[1].Value;
        var matches = Regex.Matches(body, @"C:\s*""(OO|OP|OO|)"",\s*(\d+),\s*(\d+)");
        foreach (Match m in matches)
        {
            string type = m.Groups[1].Value;
            long child = long.Parse(m.Groups[2].Value);
            long parent = long.Parse(m.Groups[3].Value);
            conns[(child, parent, type)] = type;
        }
        return conns;
    }

    // ── Geometry helpers ──

    private static List<ushort> Triangulate(List<int> rawIdx, int vertexOffset)
    {
        var tris = new List<ushort>();
        for (int i = 0; i < rawIdx.Count;)
        {
            int start = i;
            while (i < rawIdx.Count && rawIdx[i] >= 0) i++;
            if (i >= rawIdx.Count) break;
            int faceVerts = i - start + 1;
            if (faceVerts < 3) { i++; continue; }
            var face = new List<int>();
            for (int j = start; j < i; j++) face.Add(rawIdx[j]);
            face.Add(~rawIdx[i]);
            for (int j = 1; j < faceVerts - 1; j++)
            {
                tris.Add((ushort)(face[0] + vertexOffset));
                tris.Add((ushort)(face[j] + vertexOffset));
                tris.Add((ushort)(face[j + 1] + vertexOffset));
            }
            i++;
        }
        return tris;
    }

    private static Vector3[] ReadLayerVector3(string section, string elementName,
        List<int> rawIdx, int vc)
    {
        var m = Regex.Match(section, $@"{Regex.Escape(elementName)}:\s*\*\d+\s*\{{\s*a:\s*([^}}]+)");
        var vals = m.Success ? ParseFloats(m.Groups[1].Value) : [];
        var result = new Vector3[vc];
        int idx = 0;
        for (int pi = 0; pi < rawIdx.Count && idx * 3 + 2 < vals.Count; pi++)
        {
            int posVert = rawIdx[pi] >= 0 ? rawIdx[pi] : ~rawIdx[pi];
            if (posVert < vc)
                result[posVert] = new Vector3(vals[idx * 3], vals[idx * 3 + 1], vals[idx * 3 + 2]);
            idx++;
        }
        return result;
    }

    private static Vector2[] ReadLayerVector2(string section, string elementName,
        List<int> rawIdx, int vc, bool optional)
    {
        var m = Regex.Match(section, $@"{Regex.Escape(elementName)}:\s*\*\d+\s*\{{\s*a:\s*([^}}]+)");
        var vals = m.Success ? ParseFloats(m.Groups[1].Value) : [];
        var result = new Vector2[vc];
        int idx = 0;
        for (int pi = 0; pi < rawIdx.Count && idx * 2 + 1 < vals.Count; pi++)
        {
            int posVert = rawIdx[pi] >= 0 ? rawIdx[pi] : ~rawIdx[pi];
            if (posVert < vc)
                result[posVert] = new Vector2(vals[idx * 2], vals[idx * 2 + 1]);
            idx++;
        }
        return result;
    }

    private static Vector4[] ReadLayerVector4(string section, string elementName,
        List<int> rawIdx, int vc)
    {
        var m = Regex.Match(section, $@"{Regex.Escape(elementName)}:\s*\*\d+\s*\{{\s*a:\s*([^}}]+)");
        var vals = m.Success ? ParseFloats(m.Groups[1].Value) : [];
        var result = new Vector4[vc];
        int idx = 0;
        for (int pi = 0; pi < rawIdx.Count && idx * 4 + 3 < vals.Count; pi++)
        {
            int posVert = rawIdx[pi] >= 0 ? rawIdx[pi] : ~rawIdx[pi];
            if (posVert < vc)
                result[posVert] = new Vector4(vals[idx * 4], vals[idx * 4 + 1], vals[idx * 4 + 2], vals[idx * 4 + 3]);
            idx++;
        }
        return result;
    }

    // ── Number parsers ──

    private static List<float> ParseFloats(string t)
    {
        var r = new List<float>();
        var ni = CultureInfo.InvariantCulture;
        foreach (var p in t.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            if (float.TryParse(p.Trim(), NumberStyles.Float, ni, out var v)) r.Add(v);
        return r;
    }

    private static List<int> ParseInts(string t)
    {
        var r = new List<int>();
        foreach (var p in t.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(p.Trim(), out var v)) r.Add(v);
        return r;
    }
}
