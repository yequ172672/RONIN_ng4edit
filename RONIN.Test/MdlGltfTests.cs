using YakumoLib.Formats;
using SharpGLTF.Schema2;
using System.Numerics;
using System.Text.Json.Nodes;

internal static class MdlGltfTests
{
    public static void Run()
    {
        string? fixturePath = Environment.GetEnvironmentVariable("RONIN_NG4_MDL_FIXTURE");
        if (string.IsNullOrWhiteSpace(fixturePath))
        {
            Console.WriteLine("MDL/glTF real-fixture tests skipped; set RONIN_NG4_MDL_FIXTURE to a modeldata.mdl file to enable them.");
            return;
        }

        fixturePath = Path.GetFullPath(fixturePath);
        if (!File.Exists(fixturePath))
        {
            Console.WriteLine($"MDL/glTF real-fixture tests skipped; fixture not found: {fixturePath}");
            return;
        }

        MDLFullData model = MDLParserExtended.Parse(File.ReadAllBytes(fixturePath));
        var unweightedGroup = new VertexGroup
        {
            Positions = [Vector3.Zero],
            BlendIndexLayers = [[new byte[4]], [new byte[4]]],
            BlendWeightLayers = [[new byte[4]], [new byte[4]]]
        };
        Assert(MdlGltfConversion.GetSkinBindings(unweightedGroup, 0).Length == 0,
            "Zero-weight vertices must remain unweighted instead of binding to joint zero.");
        Assert(model.Bones.Length == 346, "Expected the complete 346-bone skeleton.");
        Assert(model.Batches.Length == 13, "Expected 13 real-model batches.");
        Assert(model.Groups.Length == 3, "Expected three shared vertex groups.");
        Assert(model.Indices.Length == 524445, "Expected one authoritative global index buffer.");

        int[] expectedGroupSizes = [65385, 54487, 24759];
        for (int groupIndex = 0; groupIndex < model.Groups.Length; groupIndex++)
        {
            VertexGroup group = model.Groups[groupIndex];
            Assert(group.Positions.Length == expectedGroupSizes[groupIndex], $"Unexpected group {groupIndex} size.");
            Assert(group.BlendIndexLayers.Length == 2, $"Group {groupIndex} lost a BIX layer.");
            Assert(group.BlendWeightLayers.Length == 2, $"Group {groupIndex} lost a BWT layer.");
            Assert(group.UVLayers.Length == 3, $"Group {groupIndex} lost a UV layer.");
            Assert(group.RawVertexAttributes.Count == model.VertexDeclarations.Length,
                $"Group {groupIndex} did not preserve every declaration as raw bytes.");

            for (int vertex = 0; vertex < group.Positions.Length; vertex++)
            {
                int combinedWeight = group.BlendWeightLayers.Sum(layer => layer[vertex].Sum(value => (int)value));
                Assert(combinedWeight == 255,
                    $"Group {groupIndex} vertex {vertex} combined weight is {combinedWeight}, expected 255.");
            }
        }

        MDLBatch first = model.Batches[0];
        ReadOnlySpan<ushort> firstIndices = model.GetBatchIndices(0);
        Assert(firstIndices.Length == first.indiceCount, "Batch slice length does not match indiceCount.");
        Assert(first.vertexCount == first.indiceCount / 3, "MDL batch vertexCount field is the triangle count.");
        Assert(firstIndices.ToArray().Max() == 17304, "Unexpected first-batch group-local index range.");

        TestRealExport(model);

        Console.WriteLine("MDL/glTF focused tests passed.");
    }

    private static void TestRealExport(MDLFullData model)
    {
        string glbPath = Path.Combine(Path.GetTempPath(), $"ronin-mdl-gltf-{Guid.NewGuid():N}.glb");
        try
        {
            MdlToGltfConverter.WriteGlb(model, glbPath);
            ModelRoot gltf = ModelRoot.Load(glbPath);

            Assert(gltf.LogicalMeshes.Count == 13, "Every MDL batch must remain an independently identifiable glTF mesh.");
            Assert(gltf.LogicalSkins.Count == 13, "Every exported batch must retain its ordered bone remap skin.");
            Assert(gltf.LogicalSkins.All(s => s.Joints.Count == 346),
                "Each real-model skin must expose the complete skeleton to the GLB consumer.");
            Assert(gltf.LogicalNodes.Count(n => n.Name == "p_Floor") == 1,
                "Skin-unused bones must remain in the complete 346-bone hierarchy.");
            for (int batchIndex = 0; batchIndex < model.Batches.Length; batchIndex++)
                Assert(gltf.LogicalMeshes[batchIndex].Primitives[0].GetIndexAccessor()!.Count == model.Batches[batchIndex].indiceCount,
                    $"Batch {batchIndex} lost degenerate/source-distinct triangles during glTF construction.");

            Node spine = gltf.LogicalNodes.Single(n => n.Name == "j_Spine_001");
            Vector3 spineWorld = spine.WorldMatrix.Translation;
            Assert(Vector3.Distance(spineWorld, new Vector3(0, 0.97455055f, 0)) < 0.0001f,
                $"Bone translation was accumulated as local data: {spineWorld}.");
            Matrix4x4 armWorld = gltf.LogicalNodes.Single(n => n.Name == "j_Arm_l").WorldMatrix;
            float armOffDiagonal = MathF.Abs(armWorld.M12) + MathF.Abs(armWorld.M13) + MathF.Abs(armWorld.M21) +
                                   MathF.Abs(armWorld.M23) + MathF.Abs(armWorld.M31) + MathF.Abs(armWorld.M32);
            Assert(armOffDiagonal > 0.5f, "MDL bone rotation was discarded from the glTF skeleton.");

            MeshPrimitive primitive = gltf.LogicalMeshes[0].Primitives[0];
            Assert(primitive.GetVertexAccessor("TEXCOORD_1") != null, "The second UV layer was not exported.");
            Assert(primitive.GetVertexAccessor("TEXCOORD_2") != null, "The third UV layer was not exported.");
            Assert(primitive.GetVertexAccessor("COLOR_0") != null, "The vertex color layer was not exported.");
            Assert(primitive.GetVertexAccessor("TANGENT") != null, "The MDL tangent layer was not exported.");
            Assert(primitive.GetVertexAccessor("JOINTS_1") != null, "The second four MDL influences were not exported.");
            Assert(primitive.GetVertexAccessor("WEIGHTS_1") != null, "The second four MDL weights were not exported.");

            IReadOnlyList<Vector3> exportedNormals = primitive.GetVertexAccessor("NORMAL")!.AsVector3Array();
            Vector3 exportedNormal = exportedNormals[0];
            ushort sourceVertex = model.GetBatchIndices(0)[0];
            Vector3 sourceNormal = model.Groups[model.Batches[0].vertexGroupID].Normals[sourceVertex];
            Vector3 expectedNormal = Vector3.Normalize(-sourceNormal);
            Assert(Vector3.Distance(exportedNormal, expectedNormal) < 0.002f,
                $"Exported normal {exportedNormal} does not match reference conversion {expectedNormal}.");

            IReadOnlyList<Vector3> positions = primitive.GetVertexAccessor("POSITION")!.AsVector3Array();
            IReadOnlyList<uint> triangleIndices = primitive.GetIndexAccessor()!.AsIndicesArray();
            Vector3 geometricNormal = Vector3.Normalize(Vector3.Cross(
                positions[(int)triangleIndices[1]] - positions[(int)triangleIndices[0]],
                positions[(int)triangleIndices[2]] - positions[(int)triangleIndices[0]]));
            float windingDot = Vector3.Dot(geometricNormal, exportedNormals[(int)triangleIndices[0]]);
            Assert(windingDot > 0f,
                $"Triangle winding and exported normals diverge (dot={windingDot}).");

            byte[] templateBytes = model.OriginalBytes;
            byte[] replacedBytes = GltfToMdlConverter.Convert(glbPath, templateBytes);
            MDLFullData replaced = MDLParserExtended.Parse(replacedBytes);
            Assert(replaced.Bones.Length == model.Bones.Length, "Template import dropped skeleton bones.");
            Assert(replaced.Batches.Length == model.Batches.Length, "Template import changed the batch count.");
            Assert(replaced.Groups.Length == model.Groups.Length, "Template import collapsed shared vertex groups.");
            Assert(replaced.Groups.All(g => g.Positions.Length <= ushort.MaxValue),
                "Template import produced an unrepresentable vertex group.");
            Assert(replaced.Groups.All(g => g.BlendWeightLayers.Length == 2),
                "Template import dropped the second skin layer.");
            Assert(replaced.Header.Unknown08 == model.Header.Unknown08 &&
                   replaced.Header.Unknown14 == model.Header.Unknown14 &&
                   replaced.Header.Unknown58 == model.Header.Unknown58,
                "Template-owned header metadata changed.");
            Assert(replacedBytes.AsSpan(128, 128).SequenceEqual(templateBytes.AsSpan(128, 128)),
                "Template shadow writer did not preserve original opaque bytes.");
            Assert(replaced.LODs.Length == model.LODs.Length && replaced.LODs[0].RawBytes.SequenceEqual(model.LODs[0].RawBytes),
                "Template LOD metadata changed.");
            Assert(ReadSingleStringPointer(templateBytes, model.Header.LODNameOffset) == model.LODName,
                "Template LOD name pointer is not readable.");
            Assert(ReadSingleStringPointer(replacedBytes, replaced.Header.LODNameOffset) == model.LODName,
                "Template replacement LOD name pointer changed.");
            Assert(replaced.MeshNames.SequenceEqual(model.MeshNames) && replaced.MaterialNames.SequenceEqual(model.MaterialNames),
                "Template mesh or material names changed.");
            for (int batchIndex = 0; batchIndex < model.Batches.Length; batchIndex++)
            {
                MDLBatch expectedBatch = model.Batches[batchIndex];
                MDLBatch actualBatch = replaced.Batches[batchIndex];
                Assert(actualBatch.meshGroupID == expectedBatch.meshGroupID && actualBatch.materialID == expectedBatch.materialID &&
                       actualBatch.boneMapID == expectedBatch.boneMapID && actualBatch.vertexGroupID == expectedBatch.vertexGroupID &&
                       actualBatch.indiceCount == expectedBatch.indiceCount,
                    $"Batch {batchIndex} identity or topology changed during unchanged roundtrip.");
            }
            for (int boneIndex = 0; boneIndex < model.Bones.Length; boneIndex++)
            {
                Assert(Vector3.Distance(replaced.Bones[boneIndex].Translation, model.Bones[boneIndex].Translation) < 0.0001f,
                    $"Bone {boneIndex} translation changed during unchanged roundtrip.");
                Matrix4x4 expectedRotation = Matrix4x4.CreateFromYawPitchRoll(model.Bones[boneIndex].Rotation.Y,
                    model.Bones[boneIndex].Rotation.X, model.Bones[boneIndex].Rotation.Z);
                Matrix4x4 actualRotation = Matrix4x4.CreateFromYawPitchRoll(replaced.Bones[boneIndex].Rotation.Y,
                    replaced.Bones[boneIndex].Rotation.X, replaced.Bones[boneIndex].Rotation.Z);
                Assert(MatrixDistance(expectedRotation, actualRotation) < 0.0005f &&
                       Vector3.Distance(replaced.Bones[boneIndex].Scale, model.Bones[boneIndex].Scale) < 0.0001f,
                    $"Bone {boneIndex} rotation or scale changed during unchanged roundtrip.");
            }

            TestReplacementTopologyDoesNotRestoreUnavailableDuplicates(gltf, model);
            TestMissingOptionalGlbAttributes(gltf, model);
            TestMultiplePrimitivesPerBatch(gltf, model);
            TestMaterialOverride(gltf, model);
            TestUnchangedLayoutDoesNotCreateVertexGroups(glbPath, model);
            TestOverflowingSharedGroupCreatesAdditionalGroup(glbPath, model);
            TestSingleBatchOverVertexLimitIsRejected(glbPath, model);
            TestNativeMultipleGroupsAreNotRepacked(glbPath, model);
        }
        finally
        {
            if (File.Exists(glbPath)) File.Delete(glbPath);
        }
    }

    private static void TestReplacementTopologyDoesNotRestoreUnavailableDuplicates(ModelRoot gltf, MDLFullData model)
    {
        const int targetBatch = 5;
        const int replacementBatch = 12;
        MeshPrimitive target = gltf.LogicalMeshes[targetBatch].Primitives[0];
        MeshPrimitive replacement = gltf.LogicalMeshes[replacementBatch].Primitives[0];
        Dictionary<string, Accessor> original = target.VertexAccessors.ToDictionary(pair => pair.Key, pair => pair.Value);
        Accessor? originalIndices = target.GetIndexAccessor();
        string path = Path.Combine(Path.GetTempPath(), $"ronin-mdl-gltf-replacement-topology-{Guid.NewGuid():N}.glb");
        try
        {
            foreach ((string semantic, Accessor accessor) in replacement.VertexAccessors)
                target.SetVertexAccessor(semantic, accessor);
            target.SetIndexAccessor(replacement.GetIndexAccessor());
            gltf.SaveGLB(path);
            MDLFullData replaced = MDLParserExtended.Parse(GltfToMdlConverter.Convert(path, model.OriginalBytes));
            Assert(replaced.Batches[targetBatch].indiceCount == replacement.GetIndexAccessor()!.Count,
                "Intentional replacement topology was changed by template duplicate restoration.");
        }
        finally
        {
            foreach ((string semantic, Accessor accessor) in original)
                target.SetVertexAccessor(semantic, accessor);
            target.SetIndexAccessor(originalIndices);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestMissingOptionalGlbAttributes(ModelRoot gltf, MDLFullData model)
    {
        const int batchIndex = 12;
        MeshPrimitive primitive = gltf.LogicalMeshes[batchIndex].Primitives[0];
        Accessor tangent = primitive.GetVertexAccessor("TANGENT")!;
        Accessor color = primitive.GetVertexAccessor("COLOR_0")!;
        string path = Path.Combine(Path.GetTempPath(), $"ronin-mdl-gltf-generated-attrs-{Guid.NewGuid():N}.glb");
        try
        {
            primitive.SetVertexAccessor("TANGENT", null!);
            primitive.SetVertexAccessor("COLOR_0", null!);
            gltf.SaveGLB(path);
            MDLFullData replaced = MDLParserExtended.Parse(GltfToMdlConverter.Convert(path, model.OriginalBytes));
            VertexGroup group = replaced.Groups[replaced.Batches[batchIndex].vertexGroupID];
            int count = primitive.GetVertexAccessor("POSITION")!.Count;
            Assert(group.Tangents.TakeLast(count).All(value => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
                                                                float.IsFinite(value.Z) && float.IsFinite(value.W)),
                "Generated MDL tangents contain non-finite values.");
            Assert(group.VertexColorLayers[0].TakeLast(count).All(value => value.SequenceEqual(new byte[] { 255, 255, 255, 255 })),
                "Missing GLB vertex colors must default to opaque white.");
        }
        finally
        {
            primitive.SetVertexAccessor("TANGENT", tangent);
            primitive.SetVertexAccessor("COLOR_0", color);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestMultiplePrimitivesPerBatch(ModelRoot gltf, MDLFullData model)
    {
        const int batchIndex = 12;
        MeshPrimitive source = gltf.LogicalMeshes[batchIndex].Primitives[0];
        MeshPrimitive duplicate = gltf.LogicalMeshes[batchIndex].CreatePrimitive();
        foreach ((string semantic, Accessor accessor) in source.VertexAccessors)
            duplicate.SetVertexAccessor(semantic, accessor);
        duplicate.SetIndexAccessor(source.GetIndexAccessor());
        duplicate.Material = source.Material;

        string path = Path.Combine(Path.GetTempPath(), $"ronin-mdl-gltf-multiprim-{Guid.NewGuid():N}.glb");
        try
        {
            gltf.SaveGLB(path);
            MDLFullData replaced = MDLParserExtended.Parse(GltfToMdlConverter.Convert(path, model.OriginalBytes));
            Assert(replaced.Batches[batchIndex].indiceCount == model.Batches[batchIndex].indiceCount * 2,
                "Multiple glTF primitives belonging to one MDL batch were not merged.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestMaterialOverride(ModelRoot gltf, MDLFullData model)
    {
        const int batchIndex = 7;
        const int replacementMaterial = 5;
        Node node = gltf.LogicalNodes.Single(item => item.Mesh == gltf.LogicalMeshes[batchIndex]);
        JsonNode? originalExtras = node.Extras?.DeepClone();
        string path = Path.Combine(Path.GetTempPath(), $"ronin-mdl-gltf-material-override-{Guid.NewGuid():N}.glb");
        try
        {
            JsonObject extras = node.Extras as JsonObject ?? [];
            extras["roninMaterialId"] = replacementMaterial;
            node.Extras = extras;
            gltf.SaveGLB(path);
            MDLFullData replaced = MDLParserExtended.Parse(GltfToMdlConverter.Convert(path, model.OriginalBytes));
            Assert(replaced.Batches[batchIndex].materialID == replacementMaterial,
                "Explicit glTF material override was not written to the MDL batch.");
        }
        finally
        {
            node.Extras = originalExtras;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static float MatrixDistance(Matrix4x4 a, Matrix4x4 b) =>
        MathF.Abs(a.M11 - b.M11) + MathF.Abs(a.M12 - b.M12) + MathF.Abs(a.M13 - b.M13) +
        MathF.Abs(a.M21 - b.M21) + MathF.Abs(a.M22 - b.M22) + MathF.Abs(a.M23 - b.M23) +
        MathF.Abs(a.M31 - b.M31) + MathF.Abs(a.M32 - b.M32) + MathF.Abs(a.M33 - b.M33);

    private static string ReadSingleStringPointer(byte[] data, uint tableOffset)
    {
        if ((ulong)tableOffset + 4 > (ulong)data.Length)
            throw new InvalidDataException("String pointer field is outside the file.");
        uint stringOffset = BitConverter.ToUInt32(data, checked((int)tableOffset));
        if (stringOffset >= data.Length)
            throw new InvalidDataException("String pointer target is outside the file.");
        int end = checked((int)stringOffset);
        while (end < data.Length && data[end] != 0) end++;
        if (end == data.Length) throw new InvalidDataException("String pointer target is unterminated.");
        return System.Text.Encoding.UTF8.GetString(data, checked((int)stringOffset), end - checked((int)stringOffset));
    }

    private static void TestUnchangedLayoutDoesNotCreateVertexGroups(string sourceGlbPath, MDLFullData model)
    {
        MDLFullData replaced = ConvertCopy(sourceGlbPath, model, "unchanged-groups");
        Assert(replaced.Groups.Length == model.Groups.Length,
            "An unchanged model must retain the template vertex-group count.");
        Assert(replaced.Batches.Select(batch => batch.vertexGroupID)
                .SequenceEqual(model.Batches.Select(batch => batch.vertexGroupID)),
            "An unchanged model must retain every template batch-to-vertex-group mapping.");
    }

    private static void TestOverflowingSharedGroupCreatesAdditionalGroup(string sourceGlbPath, MDLFullData model)
    {
        ModelRoot edited = ModelRoot.Load(sourceGlbPath);
        int sourceGroup = Enumerable.Range(0, model.Groups.Length)
            .Where(group => model.Batches.Count(batch => batch.vertexGroupID == group) >= 2)
            .OrderByDescending(group => model.Groups[group].Positions.Length)
            .First();
        int[] groupBatches = Enumerable.Range(0, model.Batches.Length)
            .Where(batch => model.Batches[batch].vertexGroupID == sourceGroup)
            .ToArray();
        int targetBatch = groupBatches
            .Where(batch => edited.LogicalMeshes[batch].Primitives[0].GetVertexAccessor("POSITION")!.Count * 2 <= ushort.MaxValue)
            .OrderBy(batch => edited.LogicalMeshes[batch].Primitives[0].GetVertexAccessor("POSITION")!.Count)
            .First();
        MeshPrimitive source = edited.LogicalMeshes[targetBatch].Primitives[0];
        int originalTargetIndexCount = source.GetIndexAccessor()!.Count;
        int rebuiltSourceGroupVertices = groupBatches.Sum(batch =>
            edited.LogicalMeshes[batch].Primitives.Sum(primitive => primitive.GetVertexAccessor("POSITION")!.Count));
        int addedVertices = source.GetVertexAccessor("POSITION")!.Count;
        Assert(rebuiltSourceGroupVertices <= ushort.MaxValue && rebuiltSourceGroupVertices + addedVertices > ushort.MaxValue,
            "The real fixture no longer provides a bounded shared-group overflow scenario.");
        DuplicatePrimitive(edited.LogicalMeshes[targetBatch], source);

        byte[] firstBytes = ConvertTemporaryBytes(edited, model, "split-overflow-a");
        byte[] secondBytes = ConvertTemporaryBytes(edited, model, "split-overflow-b");
        Assert(firstBytes.AsSpan().SequenceEqual(secondBytes),
            "Dynamic vertex-group packing must produce byte-identical MDL output for identical input.");
        MDLFullData replaced = MDLParserExtended.Parse(firstBytes);
        Assert(replaced.Groups.Length == model.Groups.Length + 1,
            "A representable shared-group overflow must create exactly one additional vertex group.");
        Assert(replaced.Groups.All(group => group.Positions.Length is > 0 and <= ushort.MaxValue),
            "Every rebuilt vertex group must remain representable by 16-bit local indices.");
        for (int batchIndex = 0; batchIndex < model.Batches.Length; batchIndex++)
        {
            MDLBatch expected = model.Batches[batchIndex];
            MDLBatch actual = replaced.Batches[batchIndex];
            Assert(actual.meshGroupID == expected.meshGroupID && actual.materialID == expected.materialID &&
                   actual.boneMapID == expected.boneMapID,
                $"Dynamic vertex-group packing changed batch {batchIndex} identity.");
            uint expectedIndexCount = batchIndex == targetBatch
                ? checked((uint)(originalTargetIndexCount * 2))
                : expected.indiceCount;
            Assert(actual.indiceCount == expectedIndexCount && actual.vertexCount == expectedIndexCount / 3,
                $"Dynamic vertex-group packing changed batch {batchIndex} topology.");
            Assert(replaced.GetBatchIndices(batchIndex).ToArray()
                    .All(index => index < replaced.Groups[actual.vertexGroupID].Positions.Length),
                $"Dynamic vertex-group packing wrote an invalid local index for batch {batchIndex}.");
        }
    }

    private static void TestSingleBatchOverVertexLimitIsRejected(string sourceGlbPath, MDLFullData model)
    {
        ModelRoot edited = ModelRoot.Load(sourceGlbPath);
        int targetBatch = Enumerable.Range(0, model.Batches.Length)
            .Where(batch => edited.LogicalMeshes[batch].Primitives[0].GetVertexAccessor("POSITION")!.Count > 0)
            .OrderByDescending(batch => edited.LogicalMeshes[batch].Primitives[0].GetVertexAccessor("POSITION")!.Count)
            .First();
        MeshPrimitive source = edited.LogicalMeshes[targetBatch].Primitives[0];
        int vertexCount = source.GetVertexAccessor("POSITION")!.Count;
        int primitiveCount = ushort.MaxValue / vertexCount + 1;
        for (int primitive = 1; primitive < primitiveCount; primitive++)
            DuplicatePrimitive(edited.LogicalMeshes[targetBatch], source);

        try
        {
            _ = ConvertTemporary(edited, model, "oversized-batch");
            throw new InvalidOperationException("A single batch over 65535 vertices was accepted.");
        }
        catch (NotSupportedException exception)
        {
            Assert(exception.Message.Contains("batch", StringComparison.OrdinalIgnoreCase) &&
                   exception.Message.Contains("65535", StringComparison.Ordinal),
                $"Single-batch overflow must report the batch-local 65535 limit, got: {exception.Message}");
        }
    }

    private static void TestNativeMultipleGroupsAreNotRepacked(string sourceGlbPath, MDLFullData model)
    {
        Assert(model.Groups.Length > 1, "The native multi-group regression requires more than one template group.");
        MDLFullData replaced = ConvertCopy(sourceGlbPath, model, "native-multigroup");
        for (int batchIndex = 0; batchIndex < model.Batches.Length; batchIndex++)
            Assert(replaced.Batches[batchIndex].vertexGroupID == model.Batches[batchIndex].vertexGroupID,
                $"Native multi-group batch {batchIndex} was needlessly repacked.");
    }

    private static void DuplicatePrimitive(Mesh mesh, MeshPrimitive source)
    {
        MeshPrimitive duplicate = mesh.CreatePrimitive();
        foreach ((string semantic, Accessor accessor) in source.VertexAccessors)
            duplicate.SetVertexAccessor(semantic, accessor);
        duplicate.SetIndexAccessor(source.GetIndexAccessor());
        duplicate.Material = source.Material;
    }

    private static MDLFullData ConvertCopy(string sourceGlbPath, MDLFullData model, string suffix) =>
        ConvertTemporary(ModelRoot.Load(sourceGlbPath), model, suffix);

    private static MDLFullData ConvertTemporary(ModelRoot gltf, MDLFullData model, string suffix)
        => MDLParserExtended.Parse(ConvertTemporaryBytes(gltf, model, suffix));

    private static byte[] ConvertTemporaryBytes(ModelRoot gltf, MDLFullData model, string suffix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ronin-mdl-gltf-{suffix}-{Guid.NewGuid():N}.glb");
        try
        {
            gltf.SaveGLB(path);
            return GltfToMdlConverter.Convert(path, model.OriginalBytes);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
