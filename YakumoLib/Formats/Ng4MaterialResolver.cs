using System.Buffers.Binary;
using System.Text;
using YakumoLib.Assets;

namespace YakumoLib.Formats;

public sealed record Ng4MaterialMapEntry(int Slot, string Name, UUID MaterialAssetId);
public sealed record Ng4MaterialInstanceData(UUID? ParentAssetId, IReadOnlyDictionary<string, UUID> Textures);
public sealed record Ng4UnknownTexture(string Parameter, UUID AssetId);
public sealed record Ng4ResolvedMaterial(
    int Slot,
    string Name,
    AssetEntry? BaseColorTexture,
    AssetEntry? MroTexture,
    AssetEntry? NormalTexture,
    IReadOnlyList<Ng4UnknownTexture> UnknownTextures);

public static class Ng4MaterialMapReader
{
    public static IReadOnlyList<Ng4MaterialMapEntry> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 28 || BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4)) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4)) != 0)
            throw new InvalidDataException("materialmap.bin header is invalid.");
        int materialSection = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4)) + 4);
        if (materialSection != 0x14)
            throw new InvalidDataException($"materialmap.bin material section begins at unexpected offset 0x{materialSection:X}.");
        IReadOnlyList<NamedUuidRecord> records = NamedUuidRecord.ReadContainer(data, materialSection, "materialmap.bin material section");
        return records.Select((record, slot) => new Ng4MaterialMapEntry(slot, record.Name, record.AssetId)).ToArray();
    }
}

public static class Ng4MaterialInstanceReader
{
    public static Ng4MaterialInstanceData Read(ReadOnlySpan<byte> data)
    {
        Ng4MaterialInstanceDocument document = Ng4MaterialInstanceDocument.Parse(data);
        var textures = new Dictionary<string, UUID>(StringComparer.OrdinalIgnoreCase);
        foreach (Ng4MaterialParameter parameter in document.Parameters)
            if (parameter.Type == Ng4MaterialParameterType.TextureUuid && parameter.Value is UUID texture)
                textures[parameter.Name] = texture;
        return new Ng4MaterialInstanceData(document.ParentAssetId, textures);
    }
}

public static class Ng4MaterialResolver
{
    private static readonly string[] BaseColorNames = ["BaseColorMap", "BaseColor", "AlbedoMap", "DiffuseMap"];
    private static readonly string[] MroNames = ["MaskMap", "MROMap", "ORMMap", "OrmMap", "MetallicRoughnessOcclusionMap"];
    private static readonly string[] NormalNames = ["NormalMap", "Normal"];
    private static readonly HashSet<string> RecognizedNames = new(
        BaseColorNames.Concat(MroNames).Concat(NormalNames), StringComparer.OrdinalIgnoreCase);

    public static int ResolveModelMaterialSlot(string materialName, int fallbackSlot, IReadOnlyList<string> modelMaterialNames)
    {
        int slot = modelMaterialNames
            .Select((name, index) => (name, index))
            .FirstOrDefault(item => item.Item1.Equals(materialName, StringComparison.OrdinalIgnoreCase), ("", -1)).Item2;
        return slot >= 0 ? slot : fallbackSlot;
    }

    public static UUID? ResolveBaseColor(Ng4MaterialInstanceData instance, Func<UUID, Ng4MaterialInstanceData?> loadParent)
        => ResolveTexture(instance, loadParent, BaseColorNames);

    public static UUID? ResolveMro(Ng4MaterialInstanceData instance, Func<UUID, Ng4MaterialInstanceData?> loadParent)
        => ResolveTexture(instance, loadParent, MroNames);

    public static UUID? ResolveNormal(Ng4MaterialInstanceData instance, Func<UUID, Ng4MaterialInstanceData?> loadParent)
        => ResolveTexture(instance, loadParent, NormalNames);

    public static IReadOnlyList<Ng4UnknownTexture> ResolveUnknownTextures(
        Ng4MaterialInstanceData instance, Func<UUID, Ng4MaterialInstanceData?> loadParent)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(loadParent);
        var resolved = new Dictionary<string, UUID>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<UUID>();
        Ng4MaterialInstanceData? current = instance;
        for (int depth = 0; current is not null && depth < 16; depth++)
        {
            foreach ((string parameter, UUID texture) in current.Textures)
                if (!RecognizedNames.Contains(parameter) && !resolved.ContainsKey(parameter))
                    resolved[parameter] = texture;
            if (current.ParentAssetId is null) break;
            if (!visited.Add(current.ParentAssetId))
                throw new InvalidDataException("MaterialInstance parent inheritance contains a cycle.");
            current = loadParent(current.ParentAssetId);
        }
        if (current is not null && current.ParentAssetId is not null)
            throw new InvalidDataException("MaterialInstance parent inheritance exceeds 16 levels.");
        return resolved.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new Ng4UnknownTexture(item.Key, item.Value)).ToArray();
    }

    private static UUID? ResolveTexture(
        Ng4MaterialInstanceData instance,
        Func<UUID, Ng4MaterialInstanceData?> loadParent,
        IReadOnlyList<string> parameterNames)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(loadParent);
        var visited = new HashSet<UUID>();
        Ng4MaterialInstanceData? current = instance;
        for (int depth = 0; current is not null && depth < 16; depth++)
        {
            foreach (string name in parameterNames)
                if (current.Textures.TryGetValue(name, out UUID? texture)) return texture;
            if (current.ParentAssetId is null) return null;
            if (!visited.Add(current.ParentAssetId))
                throw new InvalidDataException("MaterialInstance parent inheritance contains a cycle.");
            current = loadParent(current.ParentAssetId);
        }
        if (current is not null) throw new InvalidDataException("MaterialInstance parent inheritance exceeds 16 levels.");
        return null;
    }

    public static IReadOnlyList<Ng4ResolvedMaterial> Resolve(AssetEntry model, IReadOnlyCollection<AssetEntry> assets)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(assets);
        SubAssetEntry materialMap = model.SubEntries?.FirstOrDefault(sub => sub.FileName.Equals("materialmap.bin", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Model '{model.Path}' is missing materialmap.bin.");
        byte[] mapBytes = AssetExtractor.GetSubBlob(materialMap, model, materialMap.ContentDirectory);
        SubAssetEntry mdlSub = model.SubEntries?.FirstOrDefault(sub => sub.FileName.Equals("modeldata.mdl", StringComparison.OrdinalIgnoreCase))
            ?? model.SubEntries?.FirstOrDefault(sub => sub.FileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Model '{model.Path}' is missing modeldata.mdl.");
        MDLFullData modelData = MDLParserExtended.Parse(AssetExtractor.GetSubBlob(mdlSub, model, mdlSub.ContentDirectory));
        var byId = assets.GroupBy(asset => asset.AssetID).ToDictionary(group => group.Key, group => group.Single());
        var instanceCache = new Dictionary<UUID, Ng4MaterialInstanceData?>();

        Ng4MaterialInstanceData? LoadInstance(UUID id)
        {
            if (instanceCache.TryGetValue(id, out Ng4MaterialInstanceData? cached)) return cached;
            if (!byId.TryGetValue(id, out AssetEntry? asset) || asset.Type != AssetType.MaterialInstance)
                return instanceCache[id] = null;
            SubAssetEntry? instance = asset.SubEntries?.FirstOrDefault(sub => sub.FileName.Equals("Instance.dat", StringComparison.OrdinalIgnoreCase));
            return instanceCache[id] = instance is null ? null : Ng4MaterialInstanceReader.Read(AssetExtractor.GetSubBlob(instance, asset, instance.ContentDirectory));
        }

        return Ng4MaterialMapReader.Read(mapBytes).Select(material =>
        {
            Ng4MaterialInstanceData? instance = LoadInstance(material.MaterialAssetId);
            UUID? baseColorId = instance is null ? null : ResolveBaseColor(instance, LoadInstance);
            UUID? mroId = instance is null ? null : ResolveMro(instance, LoadInstance);
            UUID? normalId = instance is null ? null : ResolveNormal(instance, LoadInstance);
            IReadOnlyList<Ng4UnknownTexture> unknown = instance is null ? [] : ResolveUnknownTextures(instance, LoadInstance);
            AssetEntry? FindTexture(UUID? id) => id is not null && byId.TryGetValue(id, out AssetEntry? candidate) && candidate.Type == AssetType.Texture ? candidate : null;
            int modelSlot = ResolveModelMaterialSlot(material.Name, material.Slot, modelData.MaterialNames);
            return new Ng4ResolvedMaterial(modelSlot, material.Name, FindTexture(baseColorId), FindTexture(mroId), FindTexture(normalId),
                unknown.Where(item => FindTexture(item.AssetId) is not null).ToArray());
        }).ToArray();
    }

    public static IReadOnlyList<AssetEntry> ResolveTextureDependencies(
        AssetEntry model, IReadOnlyCollection<AssetEntry> assets)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assets);
        SubAssetEntry? materialMap = model.SubEntries?.FirstOrDefault(sub =>
            sub.FileName.Equals("materialmap.bin", StringComparison.OrdinalIgnoreCase));
        if (materialMap is null) return [];
        var byId = assets.GroupBy(asset => asset.AssetID).ToDictionary(group => group.Key, group => group.Single());
        var textureIds = new HashSet<UUID>();
        var visitedMaterials = new HashSet<UUID>();

        void VisitMaterial(UUID id, int depth)
        {
            if (depth >= 16 || !visitedMaterials.Add(id) || !byId.TryGetValue(id, out AssetEntry? material) ||
                material.Type != AssetType.MaterialInstance) return;
            SubAssetEntry? instanceSub = material.SubEntries?.FirstOrDefault(sub =>
                sub.FileName.Equals("Instance.dat", StringComparison.OrdinalIgnoreCase));
            if (instanceSub is null) return;
            Ng4MaterialInstanceData instance = Ng4MaterialInstanceReader.Read(
                AssetExtractor.GetSubBlob(instanceSub, material, instanceSub.ContentDirectory));
            foreach (UUID texture in instance.Textures.Values) textureIds.Add(texture);
            if (instance.ParentAssetId is UUID parent) VisitMaterial(parent, depth + 1);
        }

        byte[] mapBytes = AssetExtractor.GetSubBlob(materialMap, model, materialMap.ContentDirectory);
        foreach (Ng4MaterialMapEntry entry in Ng4MaterialMapReader.Read(mapBytes))
            VisitMaterial(entry.MaterialAssetId, 0);
        return textureIds
            .Select(id => byId.TryGetValue(id, out AssetEntry? asset) && asset.Type == AssetType.Texture ? asset : null)
            .Where(asset => asset is not null)
            .Cast<AssetEntry>()
            .OrderBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

}

internal sealed record NamedUuidRecord(string Name, UUID AssetId)
{
    public static IReadOnlyList<NamedUuidRecord> ReadContainer(ReadOnlySpan<byte> data, int offset, string label)
    {
        if (offset < 0 || offset > data.Length - 8) throw new InvalidDataException($"{label} is out of bounds.");
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)));
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4)));
        if (count < 0 || count > (data.Length - offset - 8) / 4 || length < 8 || length > data.Length - offset)
            throw new InvalidDataException($"{label} header is invalid.");
        var result = new List<NamedUuidRecord>(count);
        for (int index = 0; index < count; index++)
        {
            int relative = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 8 + index * 4, 4)));
            int recordOffset = checked(offset + relative);
            if (recordOffset < offset || recordOffset > offset + length - 28)
                throw new InvalidDataException($"{label} record {index} offset is invalid.");
            int recordLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(recordOffset, 4)));
            if (recordLength < 44 || recordLength > offset + length - recordOffset)
                throw new InvalidDataException($"{label} record {index} length is invalid.");
            int nameLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(recordOffset + 24, 4)));
            if (nameLength <= 0 || nameLength > recordLength - 44)
                throw new InvalidDataException($"{label} record {index} name is invalid.");
            ReadOnlySpan<byte> rawName = data.Slice(recordOffset + 28, nameLength);
            if (rawName[^1] != 0) throw new InvalidDataException($"{label} record {index} name is not terminated.");
            string name = Encoding.UTF8.GetString(rawName[..^1]);
            int uuidOffset = recordOffset + recordLength - 16;
            UUID uuid = new()
            {
                a = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(uuidOffset, 4)),
                b = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(uuidOffset + 4, 4)),
                c = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(uuidOffset + 8, 4)),
                d = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(uuidOffset + 12, 4))
            };
            result.Add(new NamedUuidRecord(name, uuid));
        }
        return result;
    }
}
