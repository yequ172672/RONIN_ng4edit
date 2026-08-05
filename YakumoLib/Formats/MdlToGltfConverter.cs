using System.Numerics;
using System.Text.Json.Nodes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace YakumoLib.Formats;

public static class MdlToGltfConverter
{
    public static void WriteGlb(MDLFullData model, string outputPath)
        => BuildScene(model).ToGltf2().SaveGLB(outputPath);

    public static void WriteGltf(MDLFullData model, string outputPath)
        => BuildScene(model).ToGltf2().SaveGLTF(outputPath);

    private static SceneBuilder BuildScene(MDLFullData model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ValidateInterchangeLayout(model);
        var scene = new SceneBuilder("RONIN MDL");
        var skeletonRoot = new NodeBuilder("RONIN_Skeleton")
        {
            Extras = new JsonObject { ["roninSkeletonRoot"] = true }
        };
        scene.AddNode(skeletonRoot);

        NodeBuilder[] boneNodes = BuildSkeleton(model, skeletonRoot);
        List<MaterialBuilder> materials = BuildMaterials(model);

        for (int batchIndex = 0; batchIndex < model.Batches.Length; batchIndex++)
            AddBatch(scene, model, batchIndex, boneNodes, materials);

        return scene;
    }

    private static NodeBuilder[] BuildSkeleton(MDLFullData model, NodeBuilder root)
    {
        if (model.Bones.Length == 0)
        {
            var staticRoot = new NodeBuilder("RONIN_StaticRoot")
            {
                Extras = new JsonObject { ["roninStaticRoot"] = true }
            };
            root.AddNode(staticRoot);
            return [staticRoot];
        }
        var nodes = new NodeBuilder[model.Bones.Length];
        var worldTransforms = new Matrix4x4[model.Bones.Length];

        for (int i = 0; i < model.Bones.Length; i++)
        {
            MDLBoneData bone = model.Bones[i];
            var node = new NodeBuilder(UniqueBoneName(model.Bones, i));
            node.Extras = new JsonObject
            {
                ["roninBoneIndex"] = i,
                ["roninOriginalName"] = bone.Name,
                ["roninRotationX"] = bone.Rotation.X,
                ["roninRotationY"] = bone.Rotation.Y,
                ["roninRotationZ"] = bone.Rotation.Z
            };

            Matrix4x4 world = MdlGltfConversion.ToGltfBoneWorldTransform(bone);
            nodes[i] = node;
            worldTransforms[i] = world;
        }

        for (int i = 0; i < model.Bones.Length; i++)
        {
            MDLBoneData bone = model.Bones[i];
            NodeBuilder parent = bone.ParentIndex < model.Bones.Length && bone.ParentIndex != i
                ? nodes[bone.ParentIndex]
                : root;
            parent.AddNode(nodes[i]);
            Matrix4x4 local = worldTransforms[i];
            if (bone.ParentIndex < model.Bones.Length && bone.ParentIndex != i)
            {
                if (!Matrix4x4.Invert(worldTransforms[bone.ParentIndex], out Matrix4x4 inverseParent))
                    throw new InvalidDataException($"Bone {i} '{bone.Name}' has a non-invertible parent transform.");
                local = MdlGltfConversion.SanitizeAffine(worldTransforms[i] * inverseParent);
            }
            nodes[i].LocalTransform = local;
        }

        return nodes;
    }

    private static void AddBatch(SceneBuilder scene, MDLFullData model, int batchIndex,
        NodeBuilder[] boneNodes, IReadOnlyList<MaterialBuilder> materials)
    {
        MDLBatch batch = model.Batches[batchIndex];
        if (batch.vertexGroupID >= model.Groups.Length)
            throw new InvalidDataException($"Batch {batchIndex} references missing vertex group {batch.vertexGroupID}.");
        if (model.Bones.Length != 0 && batch.boneMapID >= model.BoneRemapTables.Length)
            throw new InvalidDataException($"Batch {batchIndex} references missing bone remap {batch.boneMapID}.");

        VertexGroup group = model.Groups[batch.vertexGroupID];
        ReadOnlySpan<ushort> indices = model.GetBatchIndices(batchIndex);
        string baseName = batch.meshGroupID < model.MeshNames.Length && !string.IsNullOrWhiteSpace(model.MeshNames[batch.meshGroupID])
            ? model.MeshNames[batch.meshGroupID]
            : $"Mesh_{batch.meshGroupID}";
        string name = $"{baseName}__RONIN_B{batchIndex}_G{batch.meshGroupID}_M{batch.materialID}_R{batch.boneMapID}_V{batch.vertexGroupID}";
        var mesh = new MeshBuilder<VertexPositionNormalTangent, VertexColor1Texture4, VertexJoints8>(name)
        {
            Extras = BatchExtras(batchIndex, batch)
        };
        MaterialBuilder material = batch.materialID < materials.Count ? materials[(int)batch.materialID] : materials[0];
        PrimitiveBuilder<MaterialBuilder, VertexPositionNormalTangent, VertexColor1Texture4, VertexJoints8> primitive = mesh.UsePrimitive(material);

        for (int triangle = 0; triangle + 2 < indices.Length; triangle += 3)
        {
            ushort[] remap = model.Bones.Length == 0 ? [0] : model.BoneRemapTables[batch.boneMapID];
            VertexBuilder<VertexPositionNormalTangent, VertexColor1Texture4, VertexJoints8> a = CreateVertex(group, indices[triangle], remap);
            VertexBuilder<VertexPositionNormalTangent, VertexColor1Texture4, VertexJoints8> b = CreateVertex(group, indices[triangle + 1], remap);
            VertexBuilder<VertexPositionNormalTangent, VertexColor1Texture4, VertexJoints8> c = CreateVertex(group, indices[triangle + 2], remap);
            primitive.AddTriangle(a, c, b);
        }

        scene.AddSkinnedMesh(mesh, Matrix4x4.Identity, boneNodes).WithExtras(BatchExtras(batchIndex, batch));
    }

    private static VertexBuilder<VertexPositionNormalTangent, VertexColor1Texture4, VertexJoints8> CreateVertex(
        VertexGroup group, int index, ushort[] remap)
    {
        if ((uint)index >= (uint)group.Positions.Length)
            throw new InvalidDataException($"Vertex index {index} is outside its vertex group.");
        Vector3 position = MdlGltfConversion.ToGltfPosition(group.Positions[index]);
        Vector3 normal = index < group.Normals.Length ? MdlGltfConversion.ToGltfNormal(group.Normals[index]) : Vector3.UnitZ;
        Vector4 tangent = index < group.Tangents.Length
            ? MdlGltfConversion.ToGltfTangent(group.Tangents[index])
            : new Vector4(1, 0, 0, 1);
        Vector2[] uv = Enumerable.Range(0, 3).Select(layer => group.UVLayers.Length > layer && index < group.UVLayers[layer].Length
            ? group.UVLayers[layer][index]
            : Vector2.Zero).ToArray();
        byte[] colorBytes = group.VertexColorLayers.Length > 0 && index < group.VertexColorLayers[0].Length
            ? group.VertexColorLayers[0][index]
            : [255, 255, 255, 255];
        Vector4 color = new(
            colorBytes.ElementAtOrDefault(0) / 255f,
            colorBytes.ElementAtOrDefault(1) / 255f,
            colorBytes.ElementAtOrDefault(2) / 255f,
            colorBytes.Length > 3 ? colorBytes[3] / 255f : 1f);
        (int JointIndex, float Weight)[] bindings = MdlGltfConversion.GetSkinBindings(group, index)
            .Select(binding => binding.JointIndex < remap.Length
                ? ((int)remap[binding.JointIndex], binding.Weight)
                : throw new InvalidDataException($"Vertex {index} references remap joint {binding.JointIndex} outside the table."))
            .ToArray();
        if (bindings.Length == 0) bindings = [(0, 1f)];
        var skin = new VertexJoints8(bindings);
        Vector2 sourceVertexIdentity = new(index / 65535f, 0);
        return new VertexBuilder<VertexPositionNormalTangent, VertexColor1Texture4, VertexJoints8>(
            new VertexPositionNormalTangent(position, normal, tangent),
            new VertexColor1Texture4(color, uv[0], uv[1], uv[2], sourceVertexIdentity), skin);
    }

    private static List<MaterialBuilder> BuildMaterials(MDLFullData model)
    {
        var result = model.MaterialNames.Select(name => new MaterialBuilder(name)
            .WithDoubleSide(true).WithMetallicRoughness(0, 1)).ToList();
        if (result.Count == 0)
            result.Add(new MaterialBuilder("Default").WithDoubleSide(true).WithMetallicRoughness(0, 1));
        return result;
    }

    private static JsonObject BatchExtras(int batchIndex, MDLBatch batch) => new()
    {
        ["roninBatchIndex"] = batchIndex,
        ["roninMeshGroupId"] = batch.meshGroupID,
        ["roninMaterialId"] = batch.materialID,
        ["roninBoneMapId"] = batch.boneMapID,
        ["roninVertexGroupId"] = batch.vertexGroupID
    };

    private static string UniqueBoneName(MDLBoneData[] bones, int index)
    {
        string name = string.IsNullOrWhiteSpace(bones[index].Name) ? $"Bone_{index}" : bones[index].Name;
        return bones.Take(index).Any(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? $"{name}_{index}" : name;
    }

    private static void ValidateInterchangeLayout(MDLFullData model)
    {
        int uvLayers = model.VertexDeclarations.Where(d => d.Name.StartsWith("map", StringComparison.OrdinalIgnoreCase))
            .Select(d => (int)d.MapIndex + 1).DefaultIfEmpty(0).Max();
        int colorLayers = model.VertexDeclarations.Where(d => d.Name.StartsWith("color", StringComparison.OrdinalIgnoreCase))
            .Select(d => (int)d.MapIndex + 1).DefaultIfEmpty(0).Max();
        int skinLayers = model.VertexDeclarations.Where(d => d.Name.Equals("BIX", StringComparison.OrdinalIgnoreCase) ||
                                                               d.Name.Equals("BWT", StringComparison.OrdinalIgnoreCase))
            .Select(d => (int)d.MapIndex + 1).DefaultIfEmpty(0).Max();
        if (uvLayers > 3 || colorLayers > 1 || skinLayers > 2)
            throw new NotSupportedException($"MDL requires {uvLayers} UV, {colorLayers} color, and {skinLayers} skin layers; this glTF vertex layout supports at most 3, 1, and 2.");
    }
}
