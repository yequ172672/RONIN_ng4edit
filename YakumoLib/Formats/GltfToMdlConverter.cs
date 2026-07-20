using System.Numerics;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

namespace YakumoLib.Formats;

public static partial class GltfToMdlConverter
{
    public static byte[] Convert(string gltfPath, byte[] templateMdl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gltfPath);
        ArgumentNullException.ThrowIfNull(templateMdl);
        ModelRoot gltf = ModelRoot.Load(gltfPath, new ReadSettings { Validation = ValidationMode.TryFix });
        MDLFullData template = MDLParserExtended.Parse(templateMdl);
        return BuildTemplateReplacement(gltf, template);
    }

    private static byte[] BuildTemplateReplacement(ModelRoot gltf, MDLFullData template)
    {
        Dictionary<string, int> boneByName = BuildBoneNameMap(template.Bones);
        int uvLayerCount = LayerCount(template.VertexDeclarations, d => d.Name.StartsWith("map", StringComparison.OrdinalIgnoreCase));
        int colorLayerCount = LayerCount(template.VertexDeclarations, d => d.Name.StartsWith("color", StringComparison.OrdinalIgnoreCase));
        int skinLayerCount = Math.Max(LayerCount(template.VertexDeclarations, d => d.Name.Equals("BIX", StringComparison.OrdinalIgnoreCase)),
            LayerCount(template.VertexDeclarations, d => d.Name.Equals("BWT", StringComparison.OrdinalIgnoreCase)));
        if (uvLayerCount > 3 || colorLayerCount > 1 || skinLayerCount > 2)
            throw new NotSupportedException($"Template requires {uvLayerCount} UV, {colorLayerCount} color, and {skinLayerCount} skin layers; the validated Blender layout supports at most 3 game UVs plus one identity UV, 1 color, and 2 skin layers.");
        ImportedBatch?[] imported = new ImportedBatch[template.Batches.Length];

        foreach (Node node in gltf.LogicalNodes.Where(n => n.Mesh != null))
        {
            int batchIndex = ResolveBatchIndex(node, template);
            if (imported[batchIndex] != null)
                throw new InvalidDataException($"glTF contains more than one mesh for MDL batch {batchIndex}.");
            imported[batchIndex] = ReadBatch(node, node.Mesh!.Primitives, template, batchIndex, boneByName,
                uvLayerCount, colorLayerCount, skinLayerCount);
        }

        for (int i = 0; i < imported.Length; i++)
            if (imported[i] == null) throw new InvalidDataException($"glTF is missing required MDL batch {i}.");

        ushort[][] remaps = template.BoneRemapTables.Select(table => table.ToArray()).ToArray();
        ExtendRemaps(imported!, template.Batches, remaps);
        BuildGroups(imported!, template, remaps, uvLayerCount, colorLayerCount, skinLayerCount,
            out VertexGroup[] groups, out MDLBatch[] batches, out ushort[] indices);
        ApplyMaterialOverrides(gltf, template, batches);

        var result = new MDLFullData
        {
            Header = template.Header,
            OriginalBytes = template.OriginalBytes,
            LODs = template.LODs,
            Groups = groups,
            Batches = batches,
            Indices = indices,
            MeshNames = template.MeshNames,
            MaterialNames = template.MaterialNames,
            Bones = ReadEditedBonePositions(gltf, template.Bones),
            BoneInfos = template.BoneInfos,
            VertexDeclarations = template.VertexDeclarations,
            BoneRemapTables = remaps
        };
        return MDLWriter.WriteTemplateReplacement(result);
    }

    private static void ApplyMaterialOverrides(ModelRoot gltf, MDLFullData template, MDLBatch[] batches)
    {
        foreach (Node node in gltf.LogicalNodes.Where(item => item.Mesh != null))
        {
            JsonNode? materialNode = (node.Extras as JsonObject)?["roninMaterialId"] ??
                                     (node.Mesh?.Extras as JsonObject)?["roninMaterialId"];
            if (materialNode == null) continue;
            int batchIndex = ResolveBatchIndex(node, template);
            int materialId = materialNode.GetValue<int>();
            if ((uint)materialId >= (uint)template.MaterialNames.Length)
                throw new InvalidDataException($"Batch {batchIndex} material override {materialId} is outside the MDL material table.");
            batches[batchIndex].materialID = checked((uint)materialId);
        }
    }

    private static ImportedBatch ReadBatch(Node node, IReadOnlyList<MeshPrimitive> primitives, MDLFullData template,
        int batchIndex, IReadOnlyDictionary<string, int> boneByName, int uvLayerCount, int colorLayerCount, int skinLayerCount)
    {
        if (primitives.Count == 0)
            throw new InvalidDataException($"MDL batch {batchIndex} has no glTF primitives.");

        ImportedBatch[] parts = primitives.Select(primitive => ReadPrimitive(node, primitive, batchIndex, boneByName,
            uvLayerCount, colorLayerCount, skinLayerCount)).ToArray();
        int vertexCount = parts.Sum(part => part.Positions.Length);
        int indexCount = parts.Sum(part => part.Indices.Length);
        var result = new ImportedBatch(vertexCount, new int[indexCount], uvLayerCount, colorLayerCount, skinLayerCount);
        int vertexBase = 0;
        int indexBase = 0;
        foreach (ImportedBatch part in parts)
        {
            part.Positions.CopyTo(result.Positions, vertexBase);
            part.Normals.CopyTo(result.Normals, vertexBase);
            part.Tangents.CopyTo(result.Tangents, vertexBase);
            for (int layer = 0; layer < uvLayerCount; layer++) part.UVs[layer].CopyTo(result.UVs[layer], vertexBase);
            for (int layer = 0; layer < colorLayerCount; layer++) part.Colors[layer].CopyTo(result.Colors[layer], vertexBase);
            part.Influences.CopyTo(result.Influences, vertexBase);
            part.SourceVertexIds.CopyTo(result.SourceVertexIds, vertexBase);
            for (int index = 0; index < part.Indices.Length; index++)
                result.Indices[indexBase + index] = checked(vertexBase + part.Indices[index]);
            vertexBase += part.Positions.Length;
            indexBase += part.Indices.Length;
        }
        RestoreTemplateDuplicateTriangles(result, template, batchIndex);
        return result;
    }

    private static ImportedBatch ReadPrimitive(Node node, MeshPrimitive primitive,
        int batchIndex, IReadOnlyDictionary<string, int> boneByName, int uvLayerCount, int colorLayerCount, int skinLayerCount)
    {
        Accessor positionsAccessor = primitive.GetVertexAccessor("POSITION")
            ?? throw new InvalidDataException($"Batch {batchIndex} has no POSITION accessor.");
        int count = positionsAccessor.Count;
        IReadOnlyList<Vector3> sourcePositions = positionsAccessor.AsVector3Array();
        IReadOnlyList<Vector3> sourceNormals = RequireVector3(primitive, "NORMAL", count, batchIndex);
        IReadOnlyList<Vector2>[] sourceUvs = Enumerable.Range(0, uvLayerCount)
            .Select(layer => RequireVector2(primitive, $"TEXCOORD_{layer}", count, batchIndex)).ToArray();
        IReadOnlyList<Vector4>?[] sourceColors = Enumerable.Range(0, colorLayerCount)
            .Select(layer => OptionalColor(primitive, $"COLOR_{layer}", count, batchIndex)).ToArray();
        IReadOnlyList<Vector2>? sourceVertexIds = primitive.GetVertexAccessor("TEXCOORD_3")?.AsVector2Array();
        Accessor? indexAccessor = primitive.GetIndexAccessor();
        IReadOnlyList<uint> sourceIndices = indexAccessor != null
            ? indexAccessor.AsIndicesArray().ToArray()
            : Enumerable.Range(0, count).Select(i => (uint)i).ToArray();
        if (sourceIndices.Count % 3 != 0) throw new InvalidDataException($"Batch {batchIndex} index count is not triangular.");
        if (NormalsFollowWinding(sourcePositions, sourceNormals, sourceIndices))
            sourceNormals = sourceNormals.Select(Vector3.Negate).ToArray();
        IReadOnlyList<Vector4>? tangentAccessor = OptionalVector4(primitive, "TANGENT", count, batchIndex);
        IReadOnlyList<Vector4> sourceTangents = tangentAccessor
            ?? GenerateTangents(sourcePositions, sourceNormals, sourceUvs.FirstOrDefault(), sourceIndices, batchIndex)
                .Select(value => new Vector4(-value.X, -value.Y, -value.Z, -value.W))
                .ToArray();

        Matrix4x4 world = node.WorldMatrix;
        if (!Matrix4x4.Invert(world, out Matrix4x4 inverseWorld))
            throw new InvalidDataException($"Batch {batchIndex} has a non-invertible node transform.");
        Matrix4x4 normalMatrix = Matrix4x4.Transpose(inverseWorld);

        var result = new ImportedBatch(count, sourceIndices.Select(v => checked((int)v)).ToArray(),
            uvLayerCount, colorLayerCount, skinLayerCount);
        for (int vertex = 0; vertex < count; vertex++)
        {
            Vector3 gltfPosition = Vector3.Transform(sourcePositions[vertex], world);
            Vector3 gltfNormal = Vector3.Normalize(Vector3.TransformNormal(sourceNormals[vertex], normalMatrix));
            Vector3 gltfTangent = Vector3.Normalize(Vector3.TransformNormal(
                new Vector3(sourceTangents[vertex].X, sourceTangents[vertex].Y, sourceTangents[vertex].Z), world));
            result.Positions[vertex] = MdlGltfConversion.ToMdlPosition(gltfPosition);
            if (sourceVertexIds != null)
                result.SourceVertexIds[vertex] = Math.Clamp((int)MathF.Round(sourceVertexIds[vertex].X * 65535f), 0, ushort.MaxValue);
            result.Normals[vertex] = MdlGltfConversion.ToMdlNormal(gltfNormal);
            Vector3 mdlTangent = MdlGltfConversion.ToMdlPosition(gltfTangent);
            result.Tangents[vertex] = new Vector4(Vector3.Normalize(mdlTangent), sourceTangents[vertex].W);
            for (int layer = 0; layer < result.UVs.Length; layer++)
                result.UVs[layer][vertex] = sourceUvs[layer][vertex];
            for (int layer = 0; layer < colorLayerCount; layer++)
            {
                Vector4 color = sourceColors[layer]?[vertex] ?? Vector4.One;
                result.Colors[layer][vertex] = [ToByte(color.X), ToByte(color.Y), ToByte(color.Z), ToByte(color.W)];
            }
        }

        ReadSkin(node, primitive, result, batchIndex, boneByName, skinLayerCount);
        return result;
    }

    private static void RestoreTemplateDuplicateTriangles(ImportedBatch result, MDLFullData template, int batchIndex)
    {
        MDLBatch batch = template.Batches[batchIndex];
        if (result.Indices.Length >= batch.indiceCount) return;
        var importedBySource = new Dictionary<int, int>();
        for (int vertex = 0; vertex < result.SourceVertexIds.Length; vertex++)
            if (result.SourceVertexIds[vertex] is int sourceId) importedBySource.TryAdd(sourceId, vertex);
        if (importedBySource.Count == 0) return;

        ReadOnlySpan<ushort> source = template.GetBatchIndices(batchIndex);
        if (source.ToArray().Distinct().Any(sourceId => !importedBySource.ContainsKey(sourceId)))
            return;

        var restored = result.Indices.ToList();
        var importedCounts = new Dictionary<(ushort A, ushort B, ushort C), int>();
        for (int triangle = 0; triangle < result.Indices.Length; triangle += 3)
        {
            int? a = result.SourceVertexIds[result.Indices[triangle]];
            int? b = result.SourceVertexIds[result.Indices[triangle + 1]];
            int? c = result.SourceVertexIds[result.Indices[triangle + 2]];
            if (a is null || b is null || c is null) continue;
            var key = SortedTriangle((ushort)a, (ushort)b, (ushort)c);
            importedCounts[key] = importedCounts.GetValueOrDefault(key) + 1;
        }

        var sourceTriangles = new Dictionary<(ushort A, ushort B, ushort C), List<(ushort A, ushort B, ushort C)>>();
        for (int triangle = 0; triangle < source.Length; triangle += 3)
        {
            ushort a = source[triangle], b = source[triangle + 1], c = source[triangle + 2];
            var key = SortedTriangle(a, b, c);
            if (!sourceTriangles.TryGetValue(key, out var list)) sourceTriangles[key] = list = [];
            list.Add((a, b, c));
        }
        foreach (var pair in sourceTriangles.Where(pair => pair.Value.Count > 1))
        {
            int missing = pair.Value.Count - importedCounts.GetValueOrDefault(pair.Key);
            if (missing <= 0) continue;
            foreach ((ushort a, ushort b, ushort c) in pair.Value.TakeLast(missing))
            {
                if (!importedBySource.TryGetValue(a, out int ia) || !importedBySource.TryGetValue(b, out int ib) ||
                    !importedBySource.TryGetValue(c, out int ic))
                    throw new InvalidDataException($"Batch {batchIndex} cannot restore a template duplicate triangle because source vertex identity was removed.");
                restored.Add(ia); restored.Add(ic); restored.Add(ib);
            }
        }
        if (restored.Count > batch.indiceCount)
            throw new InvalidDataException($"Batch {batchIndex} duplicate restoration exceeded the template index count.");
        result.Indices = restored.ToArray();
    }

    private static (ushort A, ushort B, ushort C) SortedTriangle(ushort a, ushort b, ushort c)
    {
        ushort[] values = [a, b, c];
        Array.Sort(values);
        return (values[0], values[1], values[2]);
    }

    private static void ReadSkin(Node node, MeshPrimitive primitive, ImportedBatch result, int batchIndex,
        IReadOnlyDictionary<string, int> boneByName, int skinLayerCount)
    {
        Skin skin = node.Skin ?? throw new InvalidDataException($"Batch {batchIndex} has no skin.");
        for (int set = 0; set < skinLayerCount; set++)
        {
            Accessor? jointsAccessor = primitive.GetVertexAccessor($"JOINTS_{set}");
            Accessor? weightsAccessor = primitive.GetVertexAccessor($"WEIGHTS_{set}");
            if (jointsAccessor == null && weightsAccessor == null) continue;
            if (jointsAccessor == null || weightsAccessor == null || jointsAccessor.Count != result.Positions.Length || weightsAccessor.Count != result.Positions.Length)
                throw new InvalidDataException($"Batch {batchIndex} has incomplete JOINTS/WEIGHTS_{set} data.");
            IReadOnlyList<Vector4> joints = jointsAccessor.AsVector4Array();
            IReadOnlyList<Vector4> weights = weightsAccessor.AsVector4Array();
            for (int vertex = 0; vertex < result.Positions.Length; vertex++)
            {
                for (int lane = 0; lane < 4; lane++)
                {
                    float weight = weights[vertex][lane];
                    if (!float.IsFinite(weight) || weight <= 0) continue;
                    int skinJoint = checked((int)MathF.Round(joints[vertex][lane]));
                    if ((uint)skinJoint >= (uint)skin.Joints.Count)
                        throw new InvalidDataException($"Batch {batchIndex} vertex {vertex} references skin joint {skinJoint} outside the skin.");
                    string name = skin.Joints[skinJoint].Name ?? throw new InvalidDataException($"Batch {batchIndex} skin joint {skinJoint} has no name.");
                    if (!boneByName.TryGetValue(name, out int globalBone))
                        throw new InvalidDataException($"Batch {batchIndex} skin joint '{name}' is not present in the MDL template.");
                    result.Influences[vertex].Add((globalBone, weight));
                }
            }
        }

        for (int vertex = 0; vertex < result.Influences.Length; vertex++)
        {
            var merged = result.Influences[vertex].GroupBy(x => x.Bone)
                .Select(group => (Bone: group.Key, Weight: group.Sum(x => x.Weight)))
                .Where(x => x.Weight > 0).OrderByDescending(x => x.Weight).ThenBy(x => x.Bone).ToList();
            if (merged.Count > skinLayerCount * 4)
                throw new NotSupportedException($"Batch {batchIndex} vertex {vertex} has {merged.Count} influences but the template stores {skinLayerCount * 4}.");
            float total = merged.Sum(x => x.Weight);
            if (total <= 0)
            {
                result.Influences[vertex].Clear();
                continue;
            }
            result.Influences[vertex] = merged.Select(x => (x.Bone, x.Weight / total)).ToList();
        }
    }

    private static void ExtendRemaps(ImportedBatch?[] imported, MDLBatch[] batches, ushort[][] remaps)
    {
        for (int batchIndex = 0; batchIndex < imported.Length; batchIndex++)
        {
            int remapIndex = checked((int)batches[batchIndex].boneMapID);
            if ((uint)remapIndex >= (uint)remaps.Length) throw new InvalidDataException($"Batch {batchIndex} references missing remap {remapIndex}.");
            var values = remaps[remapIndex].ToList();
            foreach (int bone in imported[batchIndex]!.Influences.SelectMany(v => v).Select(x => x.Bone).Distinct().Order())
            {
                ushort encodedBone = checked((ushort)bone);
                if (!values.Contains(encodedBone)) values.Add(encodedBone);
            }
            if (values.Count > 256) throw new NotSupportedException($"Bone remap {remapIndex} requires {values.Count} entries; MDL supports 256.");
            remaps[remapIndex] = values.ToArray();
        }
    }

    private static void BuildGroups(ImportedBatch?[] imported, MDLFullData template, ushort[][] remaps,
        int uvLayerCount, int colorLayerCount, int skinLayerCount,
        out VertexGroup[] groups, out MDLBatch[] batches, out ushort[] indices)
    {
        groups = new VertexGroup[template.Groups.Length];
        batches = template.Batches.ToArray();
        var globalIndices = new List<ushort>();

        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvLayers = Enumerable.Range(0, uvLayerCount).Select(_ => new List<Vector2>()).ToArray();
            var colorLayers = Enumerable.Range(0, colorLayerCount).Select(_ => new List<byte[]>()).ToArray();
            var bixLayers = Enumerable.Range(0, skinLayerCount).Select(_ => new List<byte[]>()).ToArray();
            var bwtLayers = Enumerable.Range(0, skinLayerCount).Select(_ => new List<byte[]>()).ToArray();

            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                MDLBatch batch = batches[batchIndex];
                if (batch.vertexGroupID != groupIndex) continue;
                ImportedBatch source = imported[batchIndex]!;
                int vertexBase = positions.Count;
                if (vertexBase + source.Positions.Length > ushort.MaxValue)
                    throw new NotSupportedException($"Rebuilt vertex group {groupIndex} exceeds 65535 vertices.");
                positions.AddRange(source.Positions); normals.AddRange(source.Normals); tangents.AddRange(source.Tangents);
                for (int layer = 0; layer < uvLayerCount; layer++) uvLayers[layer].AddRange(source.UVs[layer]);
                for (int layer = 0; layer < colorLayerCount; layer++) colorLayers[layer].AddRange(source.Colors[layer]);

                ushort[] remap = remaps[batch.boneMapID];
                var localByGlobal = remap.Select((global, local) => (global, local)).ToDictionary(x => (int)x.global, x => x.local);
                for (int vertex = 0; vertex < source.Positions.Length; vertex++)
                {
                    (byte[] localIndices, byte[] localWeights) = EncodeInfluences(source.Influences[vertex], localByGlobal, skinLayerCount * 4);
                    for (int layer = 0; layer < skinLayerCount; layer++)
                    {
                        bixLayers[layer].Add(localIndices.AsSpan(layer * 4, 4).ToArray());
                        bwtLayers[layer].Add(localWeights.AsSpan(layer * 4, 4).ToArray());
                    }
                }

                batches[batchIndex].indiceStart = checked((uint)globalIndices.Count);
                for (int triangle = 0; triangle < source.Indices.Length; triangle += 3)
                {
                    globalIndices.Add(checked((ushort)(vertexBase + source.Indices[triangle])));
                    globalIndices.Add(checked((ushort)(vertexBase + source.Indices[triangle + 2])));
                    globalIndices.Add(checked((ushort)(vertexBase + source.Indices[triangle + 1])));
                }
                batches[batchIndex].indiceCount = checked((uint)source.Indices.Length);
                batches[batchIndex].vertexCount = checked((uint)source.Indices.Length / 3);
            }

            VertexGroup layout = template.Groups[groupIndex];
            groups[groupIndex] = new VertexGroup
            {
                Positions = positions.ToArray(), Normals = normals.ToArray(), Tangents = tangents.ToArray(),
                UVLayers = uvLayers.Select(x => x.ToArray()).ToArray(),
                VertexColorLayers = colorLayers.Select(x => x.ToArray()).ToArray(),
                BlendIndexLayers = bixLayers.Select(x => x.ToArray()).ToArray(),
                BlendWeightLayers = bwtLayers.Select(x => x.ToArray()).ToArray(),
                MainStride = layout.MainStride, ExStride = layout.ExStride
            };
        }

        indices = globalIndices.ToArray();
        foreach (VertexGroup group in groups) group.Indices = indices;
    }

    private static (byte[] Indices, byte[] Weights) EncodeInfluences(
        IReadOnlyList<(int Bone, float Weight)> influences, IReadOnlyDictionary<int, int> localByGlobal, int capacity)
    {
        if (influences.Count > capacity)
            throw new NotSupportedException($"Vertex has {influences.Count} influences but the template stores {capacity}.");
        var indices = new byte[capacity];
        var weights = new byte[capacity];
        if (influences.Count == 0) return (indices, weights);
        float[] exact = influences.Select(x => x.Weight * 255f).ToArray();
        int[] quantized = exact.Select(x => (int)MathF.Floor(x)).ToArray();
        int remainder = 255 - quantized.Sum();
        foreach (int index in exact.Select((value, index) => (Fraction: value - MathF.Floor(value), index))
                     .OrderByDescending(x => x.Fraction).ThenBy(x => x.index).Take(remainder).Select(x => x.index))
            quantized[index]++;
        for (int i = 0; i < influences.Count; i++)
        {
            if (!localByGlobal.TryGetValue(influences[i].Bone, out int local))
                throw new InvalidDataException($"Bone {influences[i].Bone} is missing from the rebuilt remap.");
            indices[i] = checked((byte)local);
            weights[i] = checked((byte)quantized[i]);
        }
        return (indices, weights);
    }

    private static MDLBoneData[] ReadEditedBonePositions(ModelRoot gltf, MDLBoneData[] templateBones)
    {
        var nodesByIndex = new Dictionary<int, Node>();
        foreach (Node node in gltf.LogicalNodes)
        {
            if (node.Extras is not JsonObject extras || extras["roninBoneIndex"] is not JsonNode indexNode) continue;
            int index = indexNode.GetValue<int>();
            if (!nodesByIndex.TryAdd(index, node)) throw new InvalidDataException($"glTF contains duplicate roninBoneIndex {index}.");
        }
        Dictionary<string, Node> nodes = gltf.LogicalNodes.Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Name!, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        return templateBones.Select((bone, boneIndex) =>
        {
            Node? node = nodesByIndex.GetValueOrDefault(boneIndex) ?? nodes.GetValueOrDefault(bone.Name);
            if (node == null) throw new InvalidDataException($"glTF is missing required template bone {boneIndex} '{bone.Name}'.");
            Matrix4x4 mdlWorld = MdlGltfConversion.ToMdlBoneWorldTransform(node.WorldMatrix);
            if (!Matrix4x4.Decompose(mdlWorld, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
                throw new InvalidDataException($"Bone {boneIndex} '{bone.Name}' has a non-decomposable glTF transform.");
            return new MDLBoneData
            {
                Name = bone.Name, ParentIndex = bone.ParentIndex, UnknownA = bone.UnknownA, UnknownB = bone.UnknownB,
                UnknownC = bone.UnknownC, Translation = translation,
                Rotation = MdlGltfConversion.QuaternionToMdlEuler(rotation), Scale = scale
            };
        }).ToArray();
    }

    private static int ResolveBatchIndex(Node node, MDLFullData template)
    {
        JsonNode? batchNode = (node.Extras as JsonObject)?["roninBatchIndex"] ??
                              (node.Mesh?.Extras as JsonObject)?["roninBatchIndex"];
        if (batchNode != null)
        {
            int metadataIndex = batchNode.GetValue<int>();
            if ((uint)metadataIndex >= (uint)template.Batches.Length)
                throw new InvalidDataException($"Mesh metadata references missing batch {metadataIndex}.");
            return metadataIndex;
        }
        string identity = node.Name ?? node.Mesh?.Name ?? string.Empty;
        Match match = BatchNamePattern().Match(identity);
        if (!match.Success) throw new InvalidDataException($"Mesh '{identity}' does not contain stable RONIN batch identity.");
        int index = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if ((uint)index >= (uint)template.Batches.Length) throw new InvalidDataException($"Mesh '{identity}' references missing batch {index}.");
        return index;
    }

    private static Dictionary<string, int> BuildBoneNameMap(MDLBoneData[] bones)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < bones.Length; i++)
            if (!result.TryAdd(bones[i].Name, i)) throw new InvalidDataException($"Template contains duplicate bone name '{bones[i].Name}'.");
        return result;
    }

    private static IReadOnlyList<Vector2> RequireVector2(MeshPrimitive primitive, string semantic, int count, int batch)
    {
        Accessor accessor = primitive.GetVertexAccessor(semantic) ?? throw new InvalidDataException($"Batch {batch} has no {semantic} accessor.");
        if (accessor.Count != count) throw new InvalidDataException($"Batch {batch} {semantic} count mismatch.");
        return accessor.AsVector2Array();
    }

    private static IReadOnlyList<Vector3> RequireVector3(MeshPrimitive primitive, string semantic, int count, int batch)
    {
        Accessor accessor = primitive.GetVertexAccessor(semantic) ?? throw new InvalidDataException($"Batch {batch} has no {semantic} accessor.");
        if (accessor.Count != count) throw new InvalidDataException($"Batch {batch} {semantic} count mismatch.");
        return accessor.AsVector3Array();
    }

    private static IReadOnlyList<Vector4> RequireVector4(MeshPrimitive primitive, string semantic, int count, int batch)
    {
        Accessor accessor = primitive.GetVertexAccessor(semantic) ?? throw new InvalidDataException($"Batch {batch} has no {semantic} accessor.");
        if (accessor.Count != count) throw new InvalidDataException($"Batch {batch} {semantic} count mismatch.");
        return accessor.AsVector4Array();
    }

    private static IReadOnlyList<Vector4>? OptionalVector4(MeshPrimitive primitive, string semantic, int count, int batch)
    {
        Accessor? accessor = primitive.GetVertexAccessor(semantic);
        if (accessor == null) return null;
        if (accessor.Count != count) throw new InvalidDataException($"Batch {batch} {semantic} count mismatch.");
        return accessor.AsVector4Array();
    }

    private static IReadOnlyList<Vector4>? OptionalColor(MeshPrimitive primitive, string semantic, int count, int batch)
    {
        Accessor? accessor = primitive.GetVertexAccessor(semantic);
        if (accessor == null) return null;
        if (accessor.Count != count) throw new InvalidDataException($"Batch {batch} {semantic} count mismatch.");
        return accessor.Dimensions switch
        {
            DimensionType.VEC3 => accessor.AsVector3Array().Select(value => new Vector4(value, 1f)).ToArray(),
            DimensionType.VEC4 => accessor.AsVector4Array(),
            _ => throw new InvalidDataException($"Batch {batch} {semantic} must be VEC3 or VEC4.")
        };
    }

    private static Vector4[] GenerateTangents(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2>? uvs, IReadOnlyList<uint> indices, int batch)
    {
        var tangent = new Vector3[positions.Count];
        var bitangent = new Vector3[positions.Count];
        if (indices.Any(index => index >= positions.Count))
            throw new InvalidDataException($"Batch {batch} contains an index outside its POSITION accessor.");
        if (uvs != null)
        {
            for (int i = 0; i < indices.Count; i += 3)
            {
                int i0 = checked((int)indices[i]);
                int i1 = checked((int)indices[i + 1]);
                int i2 = checked((int)indices[i + 2]);
                Vector3 edge1 = positions[i1] - positions[i0];
                Vector3 edge2 = positions[i2] - positions[i0];
                Vector2 uv1 = uvs[i1] - uvs[i0];
                Vector2 uv2 = uvs[i2] - uvs[i0];
                float determinant = uv1.X * uv2.Y - uv1.Y * uv2.X;
                if (MathF.Abs(determinant) < 1e-12f) continue;
                float reciprocal = 1f / determinant;
                Vector3 s = (edge1 * uv2.Y - edge2 * uv1.Y) * reciprocal;
                Vector3 t = (edge2 * uv1.X - edge1 * uv2.X) * reciprocal;
                tangent[i0] += s; tangent[i1] += s; tangent[i2] += s;
                bitangent[i0] += t; bitangent[i1] += t; bitangent[i2] += t;
            }
        }

        var result = new Vector4[positions.Count];
        for (int i = 0; i < result.Length; i++)
        {
            Vector3 normal = normals[i].LengthSquared() > 1e-20f ? Vector3.Normalize(normals[i]) : Vector3.UnitZ;
            Vector3 value = tangent[i] - normal * Vector3.Dot(normal, tangent[i]);
            if (value.LengthSquared() < 1e-12f)
            {
                Vector3 axis = MathF.Abs(normal.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitY;
                value = Vector3.Cross(axis, normal);
            }
            value = Vector3.Normalize(value);
            float handedness = Vector3.Dot(Vector3.Cross(normal, value), bitangent[i]) < 0f ? -1f : 1f;
            result[i] = new Vector4(value, handedness);
        }
        return result;
    }

    private static bool NormalsFollowWinding(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> normals,
        IReadOnlyList<uint> indices)
    {
        int positive = 0, negative = 0;
        for (int i = 0; i < indices.Count; i += 3)
        {
            int i0 = checked((int)indices[i]);
            int i1 = checked((int)indices[i + 1]);
            int i2 = checked((int)indices[i + 2]);
            Vector3 face = Vector3.Cross(positions[i1] - positions[i0], positions[i2] - positions[i0]);
            if (face.LengthSquared() < 1e-12f) continue;
            Vector3 normal = normals[i0] + normals[i1] + normals[i2];
            if (normal.LengthSquared() < 1e-12f) continue;
            if (Vector3.Dot(face, normal) > 0) positive++; else negative++;
        }

        return positive >= 8 && positive > negative * 2;
    }

    private static byte ToByte(float value) => checked((byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255));
    private static int LayerCount(MDLVertexDeclaration[] declarations, Func<MDLVertexDeclaration, bool> predicate) => declarations
        .Where(predicate).Select(d => (int)d.MapIndex + 1).DefaultIfEmpty(0).Max();

    [GeneratedRegex(@"__RONIN_B(\d+)_G\d+_M\d+_R\d+_V\d+", RegexOptions.CultureInvariant)]
    private static partial Regex BatchNamePattern();

    private sealed class ImportedBatch
    {
        public ImportedBatch(int count, int[] indices, int uvLayerCount, int colorLayerCount, int skinLayerCount)
        {
            Positions = new Vector3[count]; Normals = new Vector3[count]; Tangents = new Vector4[count];
            UVs = Enumerable.Range(0, uvLayerCount).Select(_ => new Vector2[count]).ToArray();
            Colors = Enumerable.Range(0, colorLayerCount).Select(_ =>
            {
                var values = new byte[count][];
                for (int i = 0; i < count; i++) values[i] = new byte[4];
                return values;
            }).ToArray();
            Influences = Enumerable.Range(0, count).Select(_ => new List<(int Bone, float Weight)>()).ToArray();
            SourceVertexIds = new int?[count];
            Indices = indices;
        }

        public Vector3[] Positions { get; }
        public Vector3[] Normals { get; }
        public Vector4[] Tangents { get; }
        public Vector2[][] UVs { get; }
        public byte[][][] Colors { get; }
        public List<(int Bone, float Weight)>[] Influences { get; set; }
        public int?[] SourceVertexIds { get; }
        public int[] Indices { get; set; }
    }
}
