using System.Numerics;

namespace YakumoLib.Formats;

public static class MdlGltfConversion
{
    public static Vector3 ToGltfPosition(Vector3 value) => value;
    public static Vector3 ToMdlPosition(Vector3 value) => value;
    public static Vector3 ToGltfNormal(Vector3 value) => NormalizeOrZero(-value);
    public static Vector3 ToMdlNormal(Vector3 value) => NormalizeOrZero(-value);
    public static Vector4 ToGltfTangent(Vector4 value)
    {
        Vector3 xyz = NormalizeOrZero(ToGltfPosition(new Vector3(value.X, value.Y, value.Z)));
        if (xyz == Vector3.Zero) xyz = Vector3.UnitX;
        return new Vector4(xyz, value.W < 0 ? -1f : 1f);
    }

    public static Matrix4x4 ToGltfBoneWorldTransform(MDLBoneData bone)
    {
        Matrix4x4 mdlWorld = Matrix4x4.CreateScale(bone.Scale) *
            Matrix4x4.CreateFromYawPitchRoll(bone.Rotation.Y, bone.Rotation.X, bone.Rotation.Z) *
            Matrix4x4.CreateTranslation(bone.Translation);
        return SanitizeAffine(mdlWorld);
    }

    public static Matrix4x4 ToMdlBoneWorldTransform(Matrix4x4 gltfWorld)
    {
        return SanitizeAffine(gltfWorld);
    }

    public static Vector3 QuaternionToMdlEuler(Quaternion value)
    {
        value = Quaternion.Normalize(value);
        float pitch = MathF.Asin(Math.Clamp(2f * (value.W * value.X - value.Y * value.Z), -1f, 1f));
        float yaw = MathF.Atan2(2f * (value.W * value.Y + value.Z * value.X),
            1f - 2f * (value.X * value.X + value.Y * value.Y));
        float roll = MathF.Atan2(2f * (value.W * value.Z + value.X * value.Y),
            1f - 2f * (value.X * value.X + value.Z * value.Z));
        return new Vector3(pitch, yaw, roll);
    }

    public static Matrix4x4 SanitizeAffine(Matrix4x4 value)
    {
        value.M14 = 0; value.M24 = 0; value.M34 = 0; value.M44 = 1;
        return value;
    }

    public static (int JointIndex, float Weight)[] GetSkinBindings(VertexGroup group, int vertex)
    {
        if ((uint)vertex >= (uint)group.Positions.Length) throw new ArgumentOutOfRangeException(nameof(vertex));
        var bindings = new List<(int JointIndex, float Weight)>(8);
        int layerCount = Math.Min(group.BlendIndexLayers.Length, group.BlendWeightLayers.Length);
        float total = 0;
        for (int layer = 0; layer < layerCount; layer++)
        {
            byte[] indices = group.BlendIndexLayers[layer][vertex];
            byte[] weights = group.BlendWeightLayers[layer][vertex];
            for (int i = 0; i < Math.Min(indices.Length, weights.Length); i++)
            {
                if (weights[i] == 0) continue;
                float weight = weights[i] / 255f;
                bindings.Add((indices[i], weight));
                total += weight;
            }
        }

        if (total <= 0) return [];
        for (int i = 0; i < bindings.Count; i++)
            bindings[i] = (bindings[i].JointIndex, bindings[i].Weight / total);
        return bindings.ToArray();
    }

    private static Vector3 NormalizeOrZero(Vector3 value) => value.LengthSquared() > 1e-20f
        ? Vector3.Normalize(value)
        : Vector3.Zero;
}
