using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Text;
using YakumoLib;
using YakumoLib.Assets;
using YakumoLib.Formats;
using YakumoLib.Modding;

internal static class Ng4LodParamTests
{
    public static void Run()
    {
        Type parserType = Type.GetType("YakumoLib.Formats.Ng4LodParam, YakumoLib")
            ?? throw new InvalidOperationException("Ng4LodParam parser is not implemented.");
        byte[] source = BuildFixture();
        MethodInfo parse = parserType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(byte[])])
            ?? throw new InvalidOperationException("Ng4LodParam.Parse(byte[]) is missing.");
        MethodInfo disable = parserType.GetMethod("DisableChildLods", BindingFlags.Public | BindingFlags.Static, [typeof(byte[])])
            ?? throw new InvalidOperationException("Ng4LodParam.DisableChildLods(byte[]) is missing.");

        var entries = ((System.Collections.IEnumerable)(parse.Invoke(null, [source])
            ?? throw new InvalidOperationException("LodParam parser returned null."))).Cast<object>().ToArray();
        Assert(entries.Length == 4, "LodParam parser did not preserve all records.");
        Assert(ReadProperty(entries[0], "Name") == "LOD0" && Nearly(ReadFloat(entries[0], "ScreenCoverage"), 1f),
            "LOD0 threshold was not parsed.");
        Assert(ReadProperty(entries[1], "Name") == "LOD1" && Nearly(ReadFloat(entries[1], "ScreenCoverage"), 0.5f),
            "LOD1 threshold was not parsed.");
        Assert(ReadProperty(entries[2], "Name") == "LOD2" && Nearly(ReadFloat(entries[2], "ScreenCoverage"), 0.2f),
            "LOD2 threshold was not parsed.");
        Assert(ReadProperty(entries[3], "Name") == "Force Hide", "Non-LOD record was not preserved.");

        byte[] disabled = (byte[])(disable.Invoke(null, [source])
            ?? throw new InvalidOperationException("DisableChildLods returned null."));
        Assert(disabled.Length == source.Length, "Disabling child LODs changed the LodParam size.");
        var disabledEntries = ((System.Collections.IEnumerable)(parse.Invoke(null, [disabled])
            ?? throw new InvalidOperationException("Disabled LodParam did not parse."))).Cast<object>().ToArray();
        Assert(Nearly(ReadFloat(disabledEntries[0], "ScreenCoverage"), 1f), "LOD0 threshold changed unexpectedly.");
        Assert(Nearly(ReadFloat(disabledEntries[1], "ScreenCoverage"), 0f) &&
               Nearly(ReadFloat(disabledEntries[2], "ScreenCoverage"), 0f),
            "Child LOD thresholds were not disabled.");
        Assert(Nearly(ReadFloat(disabledEntries[3], "ScreenCoverage"), 0f), "Force Hide threshold changed unexpectedly.");

        Assert(disabled.SequenceEqual(BuildFixture(0f, 0f)),
            "Disabling child LODs changed bytes outside the two child threshold fields.");
        AssertThrows<InvalidDataException>(() => parse.Invoke(null, [source[..^1]]), "truncated LodParam");
        ModelReplacementIncludesEveryLodAndDisablesChildThresholds();
        Console.WriteLine("NG4 LodParam focused tests passed.");
    }

    private static void ModelReplacementIncludesEveryLodAndDisablesChildThresholds()
    {
        string root = Path.Combine(Path.GetTempPath(), "ronin-lod-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] lodParam = BuildFixture();
            byte[] archive = [1, 2, 3, 4, 5, 6, .. lodParam];
            File.WriteAllBytes(Path.Combine(root, "synthetic.dat"), archive);
            SubAssetEntry[] subEntries =
            [
                Sub("modeldata.mdl", 0, 2),
                Sub("LOD1.mdl", 2, 2),
                Sub("LOD2.mdl", 4, 2),
                Sub("LodParam.bin", 6, lodParam.Length)
            ];
            var model = new AssetEntry
            {
                Path = "Assets/Character/PL/TestModel",
                Type = AssetType.SkeletalMesh,
                AssetID = UUIDParser.Parse("00000001-00000002-00000003-00000004"),
                SubEntries = subEntries
            };
            byte[] replacement = [9, 8, 7];

            IReadOnlyList<ModifiedAssetEntry> changes = ModelLodReplacementService.CreateChanges(model, replacement);

            Assert(changes.Select(change => change.SubEntry!.FileName).SequenceEqual(
                ["modeldata.mdl", "LOD1.mdl", "LOD2.mdl", "LodParam.bin"]),
                "Model replacement plan omitted or reordered LOD subfiles.");
            Assert(changes.Take(3).All(change => change.ModifiedData.SequenceEqual(replacement)),
                "Replacement MDL was not applied to every model LOD.");
            IReadOnlyList<Ng4LodParamEntry> disabledEntries = Ng4LodParam.Parse(changes[3].ModifiedData);
            Assert(Nearly(disabledEntries.Single(entry => entry.Name == "LOD0").ScreenCoverage, 1f),
                "Model replacement plan changed LOD0 coverage.");
            Assert(disabledEntries.Where(entry => entry.Name is "LOD1" or "LOD2").All(entry => Nearly(entry.ScreenCoverage, 0f)),
                "Model replacement plan did not disable child LOD thresholds.");

            SubAssetEntry Sub(string fileName, int offset, int size) => new()
            {
                FileName = fileName,
                Size = size,
                CompressedSize = 0,
                Offset = offset,
                SourceArchive = "synthetic",
                ContentDirectory = root,
                IsCompressed = false,
                IsGlobal = true
            };
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static byte[] BuildFixture(float lod1Threshold = 0.5f, float lod2Threshold = 0.2f)
    {
        byte[][] records =
        [
            BuildRecord("LOD0", 0, 1f),
            BuildRecord("LOD1", 1, lod1Threshold),
            BuildRecord("LOD2", 2, lod2Threshold),
            BuildRecord("Force Hide", 100, 0f)
        ];
        int listHeader = 8 + records.Length * 4;
        int listLength = listHeader + records.Sum(record => record.Length);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0u);
        long rootStart = output.Position;
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(16u);
        writer.Write((uint)records.Length);
        writer.Write((uint)listLength);
        int recordOffset = listHeader;
        foreach (byte[] record in records)
        {
            writer.Write((uint)recordOffset);
            recordOffset += record.Length;
        }
        foreach (byte[] record in records) writer.Write(record);
        long end = output.Position;
        output.Position = rootStart;
        writer.Write(checked((uint)(end - rootStart)));
        return output.ToArray();
    }

    private static byte[] BuildRecord(string name, uint index, float threshold)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        const int fieldTableSize = 8 + 3 * 8;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0u);
        writer.Write(3u);
        int nameOffset = fieldTableSize;
        int indexOffset = nameOffset + 4 + nameBytes.Length;
        int thresholdOffset = indexOffset + 4;
        writer.Write(0u); writer.Write((uint)nameOffset);
        writer.Write(1u); writer.Write((uint)indexOffset);
        writer.Write(2u); writer.Write((uint)thresholdOffset);
        writer.Write((uint)nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(index);
        writer.Write(threshold);
        long end = output.Position;
        output.Position = 0;
        writer.Write(checked((uint)end));
        return output.ToArray();
    }

    private static string ReadProperty(object value, string name) =>
        value.GetType().GetProperty(name)?.GetValue(value)?.ToString() ?? "";

    private static float ReadFloat(object value, string name) =>
        Convert.ToSingle(value.GetType().GetProperty(name)?.GetValue(value));

    private static bool Nearly(float actual, float expected) => MathF.Abs(actual - expected) < 0.0001f;

    private static void AssertThrows<T>(Action action, string scenario) where T : Exception
    {
        try { action(); }
        catch (TargetInvocationException error) when (error.InnerException is T) { return; }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} for {scenario}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
