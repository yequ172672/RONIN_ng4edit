using System.Numerics;
using System.Text;

namespace YakumoLib.Formats;

/// <summary>
/// Converts MDLFullData model data to ASCII FBX format (version 7400).
/// Supports static meshes and skinned meshes with bone weights.
/// </summary>
public static class MdlToFbxConverter
{
    private const string IndentString = "    ";

    public static string ConvertToFbx(MDLFullData model)
    {
        var sb = new StringBuilder(1024 * 1024);

        // ── Precompute batch data and allocate FBX node IDs ──
        int batchCount = model.Batches.Length;
        var batchData = new BatchMeshData[batchCount];

        // Deduplicate bone names for model nodes
        var boneNameSet = new HashSet<string>();
        if (model.Bones != null)
        {
            foreach (var b in model.Bones)
                if (!string.IsNullOrEmpty(b.Name))
                    boneNameSet.Add(b.Name);
        }
        string[] uniqueBoneNames = [.. boneNameSet];

        // ID allocation
        long nextId = 0;
        const long rootId = 0;
        nextId = 1;

        long[] modelIds = new long[batchCount];
        long[] nodeAttrIds = new long[batchCount];
        long[] geometryIds = new long[batchCount];
        long[] deformerIds = new long[batchCount];
        long[][] clusterIds = new long[batchCount][];

        for (int i = 0; i < batchCount; i++)
        {
            modelIds[i] = nextId++;
            nodeAttrIds[i] = nextId++;
            geometryIds[i] = nextId++;
            deformerIds[i] = nextId++;
        }

        // Bone model IDs
        var boneModelIds = new Dictionary<string, long>(uniqueBoneNames.Length);
        foreach (string boneName in uniqueBoneNames)
        {
            boneModelIds[boneName] = nextId++;
        }

        // Local bone name array for easier access
        string[] boneNames = model.Bones?.Select(b => b.Name ?? "").ToArray() ?? [];

        // Prepare batch data and allocate cluster IDs
        for (int i = 0; i < batchCount; i++)
        {
            batchData[i] = PrepareBatch(model, i);
            int boneCount = batchData[i].BoneInfluenceMap?.Count ?? 0;
            clusterIds[i] = new long[boneCount];
            for (int j = 0; j < boneCount; j++)
                clusterIds[i][j] = nextId++;
        }

        // ═══════════════════════════════════════════════════════
        // 1. HEADER
        // ═══════════════════════════════════════════════════════
        sb.AppendLine("; FBX 7.4.0 project file");
        sb.AppendLine("FBXHeaderExtension:  {");
        sb.AppendLine(Indent(1) + "FBXHeaderVersion: 1003");
        sb.AppendLine(Indent(1) + "FBXVersion: 7400");
        sb.AppendLine(Indent(1) + "CurrentCameraResolution:  {");
        sb.AppendLine(Indent(2) + "CameraName: \"Model View\"");
        sb.AppendLine(Indent(2) + "CameraResolutionMode: \"Window Size\"");
        sb.AppendLine(Indent(2) + "CameraResolutionW: 1280");
        sb.AppendLine(Indent(2) + "CameraResolutionH: 720");
        sb.AppendLine(Indent(1) + "}");
        sb.AppendLine(Indent(1) + "CreationTimeStamp:  {");
        sb.AppendLine(Indent(2) + "Version: 1000");
        sb.AppendLine(Indent(2) + "Year: 2024");
        sb.AppendLine(Indent(2) + "Month: 7");
        sb.AppendLine(Indent(2) + "Day: 14");
        sb.AppendLine(Indent(2) + "Hour: 0");
        sb.AppendLine(Indent(2) + "Minute: 0");
        sb.AppendLine(Indent(2) + "Second: 0");
        sb.AppendLine(Indent(1) + "}");
        sb.AppendLine("}");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════
        // 2. GLOBAL SETTINGS
        // ═══════════════════════════════════════════════════════
        sb.AppendLine("GlobalSettings:  {");
        sb.AppendLine(Indent(1) + "Version: 1000");
        sb.AppendLine(Indent(1) + "Properties70:  {");
        WriteGlobalProperty(sb, 2, "UpAxis", "int", "Integer", "", 1);
        WriteGlobalProperty(sb, 2, "UpAxisSign", "int", "Integer", "", 1);
        WriteGlobalProperty(sb, 2, "FrontAxis", "int", "Integer", "", 2);
        WriteGlobalProperty(sb, 2, "FrontAxisSign", "int", "Integer", "", 1);
        WriteGlobalProperty(sb, 2, "CoordAxis", "int", "Integer", "", 0);
        WriteGlobalProperty(sb, 2, "CoordAxisSign", "int", "Integer", "", 1);
        WriteGlobalProperty(sb, 2, "OriginalUpAxis", "int", "Integer", "", 1);
        WriteGlobalProperty(sb, 2, "OriginalUpAxisSign", "int", "Integer", "", 1);
        WriteGlobalProperty(sb, 2, "UnitScaleFactor", "double", "Number", "", 1.0);
        WriteGlobalProperty(sb, 2, "OriginalUnitScaleFactor", "double", "Number", "", 1.0);
        WriteGlobalProperty(sb, 2, "AmbientColor", "ColorRGB", "Color", "", "0, 0, 0");
        WriteGlobalProperty(sb, 2, "DefaultCamera", "KString", "", "", "Producer Perspective");
        WriteGlobalProperty(sb, 2, "TimeMode", "enum", "", "", 6);
        WriteGlobalProperty(sb, 2, "TimeProtocol", "enum", "", "", 2);
        WriteGlobalProperty(sb, 2, "SnapOnFrameMode", "enum", "", "", 0);
        WriteGlobalProperty(sb, 2, "TimeSpanStart", "KTime", "KTime", "", 0);
        WriteGlobalProperty(sb, 2, "TimeSpanStop", "KTime", "KTime", "", 0);
        WriteGlobalProperty(sb, 2, "CustomFrameRate", "double", "Number", "", 30.0);
        sb.AppendLine(Indent(1) + "}");
        sb.AppendLine("}");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════
        // 3. DOCUMENTS
        // ═══════════════════════════════════════════════════════
        sb.AppendLine("Documents:  {");
        sb.AppendLine(Indent(1) + "Count: 1");
        sb.AppendLine(Indent(1) + "Document: 1, \"\", \"Scene\" {");
        sb.AppendLine(Indent(2) + "Properties70:  {");
        sb.AppendLine(Indent(3) + "P: \"SourceGlobalCamera\", \"object\", \"\", \"\"");
        sb.AppendLine(Indent(3) + "P: \"SourceGlobalTime\", \"object\", \"\", \"\"");
        sb.AppendLine(Indent(2) + "}");
        sb.AppendLine(Indent(2) + "RootNode: 0");
        sb.AppendLine(Indent(1) + "}");
        sb.AppendLine("}");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════
        // 4. OBJECTS
        // ═══════════════════════════════════════════════════════
        sb.AppendLine("Objects:  {");

        // 4a. Root model
        WriteModelNode(sb, rootId, "RootNode", "Null", 1);

        // 4b. Bone model nodes
        foreach (string boneName in uniqueBoneNames)
        {
            WriteModelNode(sb, boneModelIds[boneName], boneName, "LimbNode", 1);
        }

        // 4c. Batch model nodes, geometry, node attributes, materials, deformers
        for (int i = 0; i < batchCount; i++)
        {
            string meshName = GetMeshName(model, i);
            BatchMeshData data = batchData[i];

            // Model node
            WriteModelNode(sb, modelIds[i], meshName, "Mesh", 1);

            // NodeAttribute
            WriteNodeAttribute(sb, nodeAttrIds[i], meshName, 1);

            // Geometry
            WriteGeometry(sb, geometryIds[i], meshName, data, 1);

            // Material (empty/placeholder per batch)
            WriteMaterial(sb, modelIds[i] + 10000, meshName, 1);

            // Skin Deformer + Clusters
            if (data.HasSkinning && data.BoneInfluenceMap != null && data.BoneInfluenceMap.Count > 0)
            {
                WriteSkinDeformer(sb, deformerIds[i], meshName, boneNames, data, clusterIds[i], boneModelIds, 1);
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════
        // 5. CONNECTIONS
        // ═══════════════════════════════════════════════════════
        sb.AppendLine("Connections:  {");

        // Bone models -> root
        foreach (string boneName in uniqueBoneNames)
        {
            WriteConnection(sb, boneModelIds[boneName], rootId, 1);
        }

        for (int i = 0; i < batchCount; i++)
        {
            string meshName = GetMeshName(model, i);
            BatchMeshData data = batchData[i];

            // Model -> root
            WriteConnection(sb, modelIds[i], rootId, 1);

            // NodeAttribute -> Model
            WriteConnection(sb, nodeAttrIds[i], modelIds[i], 1);

            // Geometry -> Model
            WriteConnection(sb, geometryIds[i], modelIds[i], 1);

            // Material -> Model
            WriteConnection(sb, modelIds[i] + 10000, modelIds[i], 1);

            // Skin deformer -> Geometry + Clusters -> Deformer
            if (data.HasSkinning && data.BoneInfluenceMap != null && data.BoneInfluenceMap.Count > 0)
            {
                WriteConnection(sb, deformerIds[i], geometryIds[i], 1);

                int clusterIdx = 0;
                foreach (int boneIdx in data.BoneInfluenceMap.Keys)
                {
                    string boneName = boneIdx >= 0 && boneNames != null && boneIdx < boneNames.Length
                        ? boneNames[boneIdx]
                        : $"Bone{boneIdx}";

                    long boneNodeId = boneModelIds.TryGetValue(boneName, out long bid) ? bid : 0;

                    // Cluster -> Deformer
                    WriteConnection(sb, clusterIds[i][clusterIdx], deformerIds[i], 1);
                    // Bone node -> Cluster (bone link)
                    WriteConnection(sb, boneNodeId, clusterIds[i][clusterIdx], 1);

                    clusterIdx++;
                }
            }
        }

        sb.AppendLine("}");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════
        // 6. TAKES (empty)
        // ═══════════════════════════════════════════════════════
        sb.AppendLine("Takes:  {");
        sb.AppendLine(Indent(1) + "Current: \"\"");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════
    //  WRITE HELPERS
    // ═══════════════════════════════════════════════════════════

    private static void WriteModelNode(StringBuilder sb, long id, string name, string type, int indent)
    {
        string line = Indent(indent) + $"Model: {id}, \"{EscapeFbxString(name)}\", \"{type}\" {{";
        sb.AppendLine(line);
        sb.AppendLine(Indent(indent + 1) + "Version: 232");
        sb.AppendLine(Indent(indent + 1) + "Properties70:  {");

        if (type == "Mesh")
        {
            WriteGlobalProperty(sb, indent + 2, "RotationActive", "bool", "", "", 1);
            WriteGlobalProperty(sb, indent + 2, "InheritType", "enum", "", "", 1);
            WriteGlobalProperty(sb, indent + 2, "ScalingMax", "Vector3D", "Vector", "", "0, 0, 0");
            WriteGlobalProperty(sb, indent + 2, "DefaultAttributeIndex", "int", "Integer", "", 0);
        }
        else
        {
            WriteGlobalProperty(sb, indent + 2, "RotationActive", "bool", "", "", 1);
            WriteGlobalProperty(sb, indent + 2, "InheritType", "enum", "", "", 1);
            WriteGlobalProperty(sb, indent + 2, "ScalingMax", "Vector3D", "Vector", "", "0, 0, 0");
            WriteGlobalProperty(sb, indent + 2, "DefaultAttributeIndex", "int", "Integer", "", 0);
            WriteGlobalProperty(sb, indent + 2, "Lcl Rotation", "Vector3D", "Vector", "", "0, 0, 0");
            WriteGlobalProperty(sb, indent + 2, "Lcl Translation", "Vector3D", "Vector", "", "0, 0, 0");
            WriteGlobalProperty(sb, indent + 2, "Lcl Scaling", "Vector3D", "Vector", "", "1, 1, 1");
        }

        sb.AppendLine(Indent(indent + 1) + "}");
        sb.AppendLine(Indent(indent + 1) + "Shading: Y");
        sb.AppendLine(Indent(indent + 1) + "Culling: \"CullingOff\"");
        sb.AppendLine(Indent(indent) + "}");
    }

    private static void WriteNodeAttribute(StringBuilder sb, long id, string meshName, int indent)
    {
        sb.AppendLine(Indent(indent) + $"NodeAttribute: {id}, \"NodeAttribute::{EscapeFbxString(meshName)}\", \"NodeAttribute::Mesh\" {{");
        sb.AppendLine(Indent(indent + 1) + "AttributeType: \"Mesh\"");
        sb.AppendLine(Indent(indent + 1) + "ShapeCount: 0");
        sb.AppendLine(Indent(indent) + "}");
    }

    private static void WriteGeometry(StringBuilder sb, long id, string meshName, BatchMeshData data, int indent)
    {
        int vertexCount = data.Positions.Length;
        int polyVertexCount = data.Indices.Length;
        int triangleCount = polyVertexCount / 3;

        sb.AppendLine(Indent(indent) + $"Geometry: {id}, \"Geometry::{EscapeFbxString(meshName)}\", \"Mesh\" {{");

        // Vertices
        WriteFloatArray(sb, "Vertices", data.Positions, v => new[] { v.X, v.Y, v.Z }, indent + 1);

        // PolygonVertexIndex
        WritePolygonVertexIndex(sb, data.Indices, indent + 1);

        // LayerElementNormal (ByPolygonVertex)
        WriteLayerElementFloatArray(sb, "LayerElementNormal", "Normals", 0,
            data.Normals, data.Indices, v => new[] { v.X, v.Y, v.Z }, indent + 1);

        // LayerElementUV
        WriteLayerElementFloatArray(sb, "LayerElementUV", "UV", 0,
            data.UVs, data.Indices, v => new[] { v.X, v.Y }, indent + 1);

        // LayerElementTangent (if present)
        if (data.HasTangents)
        {
            WriteLayerElementFloatArray(sb, "LayerElementTangent", "Tangents", 0,
                data.Tangents, data.Indices, v => new[] { v.X, v.Y, v.Z }, indent + 1);
        }

        // LayerElementMaterial (AllSame)
        sb.AppendLine(Indent(indent + 1) + "LayerElementMaterial: 0 {");
        sb.AppendLine(Indent(indent + 2) + "Version: 101");
        sb.AppendLine(Indent(indent + 2) + "Name: \"\"");
        sb.AppendLine(Indent(indent + 2) + "MappingInformationType: \"AllSame\"");
        sb.AppendLine(Indent(indent + 2) + "ReferenceInformationType: \"Direct\"");
        sb.AppendLine(Indent(indent + 1) + "}");

        // Layer
        sb.AppendLine(Indent(indent + 1) + "Layer: 0 {");
        sb.AppendLine(Indent(indent + 2) + "Version: 100");
        WriteLayerElementRef(sb, "LayerElementNormal", 0, indent + 2);
        WriteLayerElementRef(sb, "LayerElementUV", 0, indent + 2);
        if (data.HasTangents)
            WriteLayerElementRef(sb, "LayerElementTangent", 0, indent + 2);
        WriteLayerElementRef(sb, "LayerElementMaterial", 0, indent + 2);
        sb.AppendLine(Indent(indent + 1) + "}");

        sb.AppendLine(Indent(indent) + "}");
    }

    private static void WriteMaterial(StringBuilder sb, long id, string meshName, int indent)
    {
        sb.AppendLine(Indent(indent) + $"Material: {id}, \"Material::{EscapeFbxString(meshName)}\", \"\" {{");
        sb.AppendLine(Indent(indent + 1) + "Version: 102");
        sb.AppendLine(Indent(indent + 1) + "ShadingModel: \"phong\"");
        sb.AppendLine(Indent(indent + 1) + "Properties70:  {");
        WriteGlobalProperty(sb, indent + 2, "AmbientColor", "ColorRGB", "Color", "", "0.2, 0.2, 0.2");
        WriteGlobalProperty(sb, indent + 2, "DiffuseColor", "ColorRGB", "Color", "", "0.8, 0.8, 0.8");
        WriteGlobalProperty(sb, indent + 2, "SpecularColor", "ColorRGB", "Color", "", "0, 0, 0");
        WriteGlobalProperty(sb, indent + 2, "Shininess", "double", "Number", "", 0.2);
        sb.AppendLine(Indent(indent + 1) + "}");
        sb.AppendLine(Indent(indent) + "}");
    }

    private static void WriteSkinDeformer(StringBuilder sb, long deformerId, string meshName,
        string[] boneNames, BatchMeshData data, long[] clusterIds,
        Dictionary<string, long> boneModelIds, int indent)
    {
        int boneCount = data.BoneInfluenceMap!.Count;

        sb.AppendLine(Indent(indent) + $"Deformer: {deformerId}, \"Deformer::Skin\", \"Skin\" {{");
        sb.AppendLine(Indent(indent + 1) + "Version: 101");
        sb.AppendLine(Indent(indent + 1) + "Type: \"Skin\"");
        sb.AppendLine(Indent(indent + 1) + "MultiLayer: 0");
        sb.AppendLine(Indent(indent) + "}");

        int clusterIdx = 0;
        foreach (var kvp in data.BoneInfluenceMap)
        {
            int boneIdx = kvp.Key;
            string boneName = boneIdx >= 0 && boneIdx < boneNames.Length
                ? boneNames[boneIdx]
                : $"Bone{boneIdx}";
            long clusterId = clusterIds[clusterIdx];

            var influences = kvp.Value; // List<(int cpt, float weight)>

            sb.AppendLine(Indent(indent) + $"SubDeformer: {clusterId}, \"SubDeformer::{EscapeFbxString(boneName)}\", \"Cluster\" {{");
            sb.AppendLine(Indent(indent + 1) + "Version: 101");
            sb.AppendLine(Indent(indent + 1) + "UserData: \"\"");

            // Indexes (control point indices)
            int[] cptIndices = new int[influences.Count];
            double[] weights = new double[influences.Count];
            for (int k = 0; k < influences.Count; k++)
            {
                cptIndices[k] = influences[k].cpt;
                weights[k] = influences[k].weight;
            }
            WriteIntArray(sb, "Indexes", cptIndices, indent + 1);
            WriteDoubleArray(sb, "Weights", weights, indent + 1);

            // Identity transform matrices (we lack bone transform data)
            sb.AppendLine(Indent(indent + 1) + "Transform: 1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1");
            sb.AppendLine(Indent(indent + 1) + "TransformLink: 1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1");
            sb.AppendLine(Indent(indent) + "}");

            clusterIdx++;
        }
    }

    private static void WriteConnection(StringBuilder sb, long childId, long parentId, int indent)
    {
        sb.AppendLine(Indent(indent) + $"C: \"OO\",{childId},{parentId}");
    }

    // ═══════════════════════════════════════════════════════════
    //  ARRAY WRITERS
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Writes an FBX float array from a source array with a value expander.
    /// </summary>
    private static void WriteFloatArray<T>(StringBuilder sb, string propertyName,
        T[] srcArray, Func<T, float[]> expander, int indent)
    {
        if (srcArray == null || srcArray.Length == 0)
        {
            sb.AppendLine(Indent(indent) + $"{propertyName}: *0 {{");
            sb.AppendLine(Indent(indent + 1) + "a: ");
            sb.AppendLine(Indent(indent) + "}");
            return;
        }
        int components = expander(srcArray[0]).Length;
        int count = srcArray.Length * components;

        sb.AppendLine(Indent(indent) + $"{propertyName}: *{count} {{");
        sb.Append(Indent(indent + 1) + "a: ");
        for (int i = 0; i < srcArray.Length; i++)
        {
            float[] vals = expander(srcArray[i]);
            for (int c = 0; c < components; c++)
            {
                if (i > 0 || c > 0) sb.Append(", ");
                sb.AppendFormat(null, "{0:0.000000}", vals[c]);
            }
        }
        sb.AppendLine();
        sb.AppendLine(Indent(indent) + "}");
    }

    /// <summary>
    /// Writes an FBX float array (e.g. Vertices: *N { a: f1, f2, ... })
    /// </summary>
    private static void WriteFloatArray(StringBuilder sb, string propertyName, float[] values, int indent)
    {
        if (values == null || values.Length == 0)
        {
            sb.AppendLine(Indent(indent) + $"{propertyName}: *0 {{");
            sb.AppendLine(Indent(indent + 1) + "a: ");
            sb.AppendLine(Indent(indent) + "}");
            return;
        }

        sb.AppendLine(Indent(indent) + $"{propertyName}: *{values.Length} {{");
        sb.Append(Indent(indent + 1) + "a: ");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.AppendFormat(null, "{0:0.000000}", values[i]);
        }
        sb.AppendLine();
        sb.AppendLine(Indent(indent) + "}");
    }

    /// <summary>
    /// Writes an FBX integer array (e.g. Indexes: *N { a: i1, i2, ... })
    /// </summary>
    private static void WriteIntArray(StringBuilder sb, string propertyName, int[] values, int indent)
    {
        int count = values.Length;
        if (count == 0)
        {
            sb.AppendLine(Indent(indent) + $"{propertyName}: *0 {{");
            sb.AppendLine(Indent(indent + 1) + "a: ");
            sb.AppendLine(Indent(indent) + "}");
            return;
        }

        sb.AppendLine(Indent(indent) + $"{propertyName}: *{count} {{");
        sb.Append(Indent(indent + 1) + "a: ");
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(values[i]);
        }
        sb.AppendLine();
        sb.AppendLine(Indent(indent) + "}");
    }

    /// <summary>
    /// Writes an FBX double array (for cluster weights).
    /// </summary>
    private static void WriteDoubleArray(StringBuilder sb, string propertyName, double[] values, int indent)
    {
        int count = values.Length;
        if (count == 0)
        {
            sb.AppendLine(Indent(indent) + $"{propertyName}: *0 {{");
            sb.AppendLine(Indent(indent + 1) + "a: ");
            sb.AppendLine(Indent(indent) + "}");
            return;
        }

        sb.AppendLine(Indent(indent) + $"{propertyName}: *{count} {{");
        sb.Append(Indent(indent + 1) + "a: ");
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.AppendFormat(null, "{0:G}", values[i]);
        }
        sb.AppendLine();
        sb.AppendLine(Indent(indent) + "}");
    }

    /// <summary>
    /// Writes the PolygonVertexIndex array with the last index of each triangle negated-1.
    /// </summary>
    private static void WritePolygonVertexIndex(StringBuilder sb, ushort[] indices, int indent)
    {
        int count = indices.Length;
        int triCount = count / 3;

        sb.AppendLine(Indent(indent) + $"PolygonVertexIndex: *{count} {{");
        sb.Append(Indent(indent + 1) + "a: ");
        for (int i = 0; i < triCount; i++)
        {
            int baseIdx = i * 3;
            for (int j = 0; j < 3; j++)
            {
                if (baseIdx + j > 0) sb.Append(", ");
                int value = indices[baseIdx + j];
                if (j == 2) value = -(value + 1); // Negate-1 for last vertex of polygon
                sb.Append(value);
            }
        }
        sb.AppendLine();
        sb.AppendLine(Indent(indent) + "}");
    }

    /// <summary>
    /// Writes a LayerElement that contains an expanded array (ByPolygonVertex, Direct).
    /// Expands srcArray using the indices buffer to create per-polygon-vertex data.
    /// </summary>
    private static void WriteLayerElementFloatArray<T>(
        StringBuilder sb, string elementType, string arrayName, int layerIndex,
        T[] srcArray, ushort[] indices, Func<T, float[]> expander, int indent)
    {
        // Safeguard: if source or index array is empty, write an empty element
        if (srcArray == null || srcArray.Length == 0 || indices.Length == 0)
        {
            sb.AppendLine(Indent(indent) + $"{elementType}: {layerIndex} {{");
            sb.AppendLine(Indent(indent + 1) + "Version: 101");
            sb.AppendLine(Indent(indent + 1) + "Name: \"\"");
            sb.AppendLine(Indent(indent + 1) + "MappingInformationType: \"ByPolygonVertex\"");
            sb.AppendLine(Indent(indent + 1) + "ReferenceInformationType: \"Direct\"");
            sb.AppendLine(Indent(indent + 1) + $"{arrayName}: *0 {{");
            sb.AppendLine(Indent(indent + 2) + "a: ");
            sb.AppendLine(Indent(indent + 1) + "}");
            sb.AppendLine(Indent(indent) + "}");
            return;
        }

        int polyVtxCount = indices.Length;
        int components = expander(srcArray[0]).Length;
        var expanded = new float[polyVtxCount * components];

        for (int i = 0; i < polyVtxCount; i++)
        {
            ushort srcIdx = indices[i];
            if (srcIdx >= srcArray.Length) continue;
            T src = srcArray[srcIdx];
            float[] vals = expander(src);
            for (int c = 0; c < components; c++)
            {
                expanded[i * components + c] = vals[c];
            }
        }

        sb.AppendLine(Indent(indent) + $"{elementType}: {layerIndex} {{");
        sb.AppendLine(Indent(indent + 1) + "Version: 101");
        sb.AppendLine(Indent(indent + 1) + "Name: \"\"");
        sb.AppendLine(Indent(indent + 1) + "MappingInformationType: \"ByPolygonVertex\"");
        sb.AppendLine(Indent(indent + 1) + "ReferenceInformationType: \"Direct\"");
        WriteFloatArray(sb, arrayName, expanded, indent + 1);
        sb.AppendLine(Indent(indent) + "}");
    }

    private static void WriteLayerElementRef(StringBuilder sb, string type, int typedIndex, int indent)
    {
        sb.AppendLine(Indent(indent) + "LayerElement:  {");
        sb.AppendLine(Indent(indent + 1) + $"Type: \"{type}\"");
        sb.AppendLine(Indent(indent + 1) + $"TypedIndex: {typedIndex}");
        sb.AppendLine(Indent(indent) + "}");
    }

    // ═══════════════════════════════════════════════════════════
    //  PROPERTY WRITERS
    // ═══════════════════════════════════════════════════════════

    private static void WriteGlobalProperty(StringBuilder sb, int indent,
        string name, string type, string label, string user, object value)
    {
        string valStr = value switch
        {
            string s => $"\"{s}\"",
            int i => i.ToString(),
            double d => d.ToString("G"),
            _ => value?.ToString() ?? ""
        };
        sb.AppendLine(Indent(indent) + $"P: \"{name}\", \"{type}\", \"{label}\", \"{user}\", {valStr}");
    }

    // ═══════════════════════════════════════════════════════════
    //  BATCH DATA PREPARATION
    // ═══════════════════════════════════════════════════════════

    private sealed class BatchMeshData
    {
        public Vector3[] Positions = [];
        public Vector3[] Normals = [];
        public Vector4[] Tangents = [];
        public Vector2[] UVs = [];
        public ushort[] Indices = [];

        public bool HasTangents;
        public bool HasSkinning;

        /// <summary>
        /// Map of bone index (in global BoneNames) -> list of (controlPointIndex, normalizedWeight).
        /// </summary>
        public Dictionary<int, List<(int cpt, float weight)>>? BoneInfluenceMap;
    }

    private static BatchMeshData PrepareBatch(MDLFullData model, int batchIndex)
    {
        if (batchIndex < 0 || batchIndex >= model.Batches.Length)
            return new BatchMeshData { Positions = [], Normals = [], UVs = [], Indices = [], HasTangents = false, HasSkinning = false };

        var batch = model.Batches[batchIndex];
        if (batch.vertexGroupID >= model.Groups.Length)
            return new BatchMeshData { Positions = [], Normals = [], UVs = [], Indices = [], HasTangents = false, HasSkinning = false };
        var group = model.Groups[batch.vertexGroupID];

        int start = (int)batch.indiceStart;
        int count = (int)batch.indiceCount;

        // Validate: group.Indices must exist and batch ranges must be valid
        if (group.Indices == null || group.Indices.Length == 0)
        {
            // No index data — return empty batch
            return new BatchMeshData
            {
                Positions = [], Normals = [], UVs = [], Indices = [],
                HasTangents = false, HasSkinning = false
            };
        }

        // Clamp to valid range
        if (start >= group.Indices.Length) start = 0;
        if (start + count > group.Indices.Length) count = group.Indices.Length - start;
        if (count <= 0) return new BatchMeshData
        {
            Positions = [], Normals = [], UVs = [], Indices = [],
            HasTangents = false, HasSkinning = false
        };

        // Extract batch indices from the group's index buffer
        var batchIndices = new ushort[count];
        Array.Copy(group.Indices, start, batchIndices, 0, count);

        // Find vertex range used by this batch
        ushort minIdx = ushort.MaxValue;
        ushort maxIdx = ushort.MinValue;
        foreach (ushort idx in batchIndices)
        {
            if (idx < minIdx) minIdx = idx;
            if (idx > maxIdx) maxIdx = idx;
        }

        int localVertexCount = maxIdx - minIdx + 1;

        // Validate that all vertex data arrays are large enough
        int positionsLen = group.Positions?.Length ?? 0;
        int normalsLen = group.Normals?.Length ?? 0;
        int uvsLen = group.UVs?.Length ?? 0;
        int maxAvail = Math.Max(0, Math.Min(positionsLen, Math.Min(normalsLen, uvsLen)) - minIdx);
        if (maxAvail <= 0) return new BatchMeshData
        {
            Positions = [], Normals = [], UVs = [], Indices = [],
            HasTangents = false, HasSkinning = false
        };

        // Copy vertex data for the full local range
        var result = new BatchMeshData
        {
            Positions = SliceArraySafe(group.Positions!, minIdx, localVertexCount),
            Normals = SliceArraySafe(group.Normals!, minIdx, localVertexCount),
            UVs = SliceArraySafe(group.UVs!, minIdx, localVertexCount),
        };

        // Tangents (may be empty)
        if (group.Tangents != null && group.Tangents.Length > 0 && minIdx < group.Tangents.Length)
        {
            result.Tangents = SliceArraySafe(group.Tangents, minIdx, localVertexCount);
            result.HasTangents = true;
        }

        // Remap indices to be 0-based locally
        result.Indices = new ushort[count];
        for (int i = 0; i < count; i++)
            result.Indices[i] = (ushort)(batchIndices[i] - minIdx);

        // Skinning data
        bool hasValidSkinData = group.BlendIndices != null && group.BlendIndices.Length > 0 &&
            group.BlendWeights != null && group.BlendWeights.Length > 0 &&
            model.Bones != null && model.Bones.Length > 0;
        if (hasValidSkinData && minIdx < group.BlendIndices!.Length && minIdx < group.BlendWeights!.Length)
        {
            // Also verify the full index range is within blend arrays
            int blendEndIdx = minIdx + localVertexCount - 1;
            if (blendEndIdx < group.BlendIndices.Length && blendEndIdx < group.BlendWeights.Length)
            {
                result.HasSkinning = true;
                var influenceMap = new Dictionary<int, List<(int cpt, float weight)>>();

                for (int v = 0; v < localVertexCount; v++)
                {
                    int globalVtxIdx = minIdx + v;

                    byte[] indices = group.BlendIndices[globalVtxIdx];
                    byte[] weights = group.BlendWeights[globalVtxIdx];

                    int maxInfs = Math.Min(Math.Min(indices.Length, weights.Length), 4);
                    for (int b = 0; b < maxInfs; b++)
                    {
                        int boneIdx = indices[b];
                        float weight = weights[b] / 255f;

                        if (weight > 0.001f && boneIdx >= 0 && boneIdx < model.Bones!.Length)
                        {
                            if (!influenceMap.TryGetValue(boneIdx, out var list))
                            {
                                list = [];
                                influenceMap[boneIdx] = list;
                            }
                            list.Add((v, weight));
                        }
                    }
                }

                result.BoneInfluenceMap = influenceMap;
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════════════════════════

    private static string GetMeshName(MDLFullData model, int batchIndex)
    {
        var batch = model.Batches[batchIndex];
        if (model.MeshNames != null && batch.meshGroupID < model.MeshNames.Length)
        {
            string name = model.MeshNames[batch.meshGroupID];
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        return $"Mesh_{batchIndex}";
    }

    private static T[] SliceArraySafe<T>(T[] src, int start, int length)
    {
        if (src == null || src.Length == 0 || start >= src.Length || length <= 0)
            return [];
        int actualLength = Math.Min(length, src.Length - start);
        var result = new T[actualLength];
        Array.Copy(src, start, result, 0, actualLength);
        return result;
    }

    private static string EscapeFbxString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string Indent(int level)
    {
        return level switch
        {
            0 => "",
            1 => IndentString,
            2 => IndentString + IndentString,
            3 => IndentString + IndentString + IndentString,
            4 => IndentString + IndentString + IndentString + IndentString,
            _ => new string(' ', level * 4)
        };
    }
}
