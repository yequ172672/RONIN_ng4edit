using System.IO.Compression;
using System.Text.Json;
using RoninNg4Host;
using YakumoLib.Assets;

internal static class HostNg4ModTests
{
    public static void Run()
    {
        TestMetadataContract();
        TestHostActivityTimeoutContract();
        TestConfigureRequestUsesSnakeCaseAssetsDirectory();
        TestSafeTextureArchiveExtraction();
        TestUnsafeTextureArchivesAreRejected();
        TestBatchModelContract();
        TestModelSubEntrySelectionIncludesIndependentLods();
        Console.WriteLine("Host NG4MOD tests passed.");
    }

    private static void TestHostActivityTimeoutContract()
    {
        var state = new HostState();
        Assert(!state.IsIdle(TimeSpan.FromMinutes(1)), "A newly created Host must not be idle.");
        state.Touch();
        Assert(!state.IsIdle(TimeSpan.FromMinutes(1)), "Host activity touch did not refresh the idle deadline.");
        using (state.BeginRequest())
            Assert(!state.IsIdle(TimeSpan.Zero), "An active Host request must suppress idle shutdown.");
    }

    private static void TestConfigureRequestUsesSnakeCaseAssetsDirectory()
    {
        ConfigureRequest request = JsonSerializer.Deserialize<ConfigureRequest>(
            "{\"assets_directory\":\"D:/NG4/Assets\"}")
            ?? throw new InvalidOperationException("Configure request JSON was null.");
        Assert(request.AssetsDirectory == "D:/NG4/Assets", "assets_directory JSON did not bind to ConfigureRequest.");
    }

    private static void TestMetadataContract()
    {
        Ng4ModUploadMetadata metadata = Ng4ModUploadMetadata.Parse("""
            {"mod_id":"author.example","name":"Example","version":"1.2.3","author":"Author","description":"Desc","dependencies":["base.mod"]}
            """);
        Assert(metadata.ModId == "author.example", "metadata mod_id was not parsed.");
        Assert(metadata.Dependencies.SequenceEqual(["base.mod"]), "metadata dependencies were not parsed.");
        AssertThrows<InvalidDataException>(() => Ng4ModUploadMetadata.Parse("{}"), "missing metadata fields");
        AssertThrows<InvalidDataException>(() => Ng4ModUploadMetadata.Parse("{"), "malformed metadata JSON");
    }

    private static void TestSafeTextureArchiveExtraction()
    {
        byte[] archive = CreateArchive(("ronin-texture-set.json", "{}"), ("nested/", ""), ("nested/body.tga", "pixels"));
        using Ng4ModUploadWorkspace workspace = Ng4ModUploadWorkspace.Create();
        using var input = new MemoryStream(archive);
        workspace.ExtractTextureSet(input, maxEntryBytes: 64, maxTotalBytes: 128);
        Assert(File.ReadAllText(Path.Combine(workspace.TextureSetDirectory, "nested", "body.tga")) == "pixels",
            "safe texture archive content was not extracted.");
        string root = workspace.RootDirectory;
        workspace.Dispose();
        Assert(!Directory.Exists(root), "disposing an NG4MOD upload workspace did not clean it.");
    }

    private static void TestUnsafeTextureArchivesAreRejected()
    {
        AssertArchiveRejected(CreateArchive(("../escape.tga", "bad")), 64, 128, "path traversal");
        AssertArchiveRejected(CreateArchive(("Body.tga", "a"), ("body.tga", "b")), 64, 128, "duplicate entry");
        AssertArchiveRejected(CreateArchive(("large.tga", new string('x', 65))), 64, 128, "oversized entry");
        AssertArchiveRejected(CreateArchive(("a.tga", new string('x', 40)), ("b.tga", new string('y', 40))), 64, 64, "oversized archive");
    }

    private static void TestBatchModelContract()
    {
        IReadOnlyList<Ng4ModBatchItem> items = Ng4ModBatchContract.Parse("""
            [{"asset_id":"asset-a","glb_field":"glb_0","texture_field":"texture_set_0"},{"asset_id":"asset-b","glb_field":"glb_1","texture_field":"texture_set_1"}]
            """);
        Assert(items.Count == 2 && items[1].AssetId == "asset-b", "Batch model contract was not parsed in order.");
        AssertThrows<InvalidDataException>(() => Ng4ModBatchContract.Parse("[]"), "empty batch");
        AssertThrows<InvalidDataException>(() => Ng4ModBatchContract.Parse("[{\"asset_id\":\"asset-a\",\"glb_field\":\"g\",\"texture_field\":\"t\"},{\"asset_id\":\"asset-a\",\"glb_field\":\"g2\",\"texture_field\":\"t2\"}]"), "duplicate batch asset");
    }

    private static void TestModelSubEntrySelectionIncludesIndependentLods()
    {
        SubAssetEntry[] entries =
        [
            CreateSubEntry("LOD2.mdl"),
            CreateSubEntry("materialmap.bin"),
            CreateSubEntry("LOD1.mdl"),
            CreateSubEntry("modeldata.mdl")
        ];
        IReadOnlyList<SubAssetEntry> selected = Ng4AssetService.SelectModelSubEntries(entries);
        Assert(selected.Select(entry => entry.FileName).SequenceEqual(["modeldata.mdl", "LOD1.mdl", "LOD2.mdl"]),
            "Model packaging must include modeldata.mdl and independent LOD MDLs in stable order.");
    }

    private static SubAssetEntry CreateSubEntry(string fileName) => new()
    {
        FileName = fileName, Size = 1, CompressedSize = 1, Offset = 0,
        SourceArchive = "test", ContentDirectory = "test", IsCompressed = false, IsGlobal = false
    };

    private static void AssertArchiveRejected(byte[] archive, long maxEntryBytes, long maxTotalBytes, string scenario)
    {
        string root;
        using (Ng4ModUploadWorkspace workspace = Ng4ModUploadWorkspace.Create())
        {
            root = workspace.RootDirectory;
            using var input = new MemoryStream(archive);
            AssertThrows<InvalidDataException>(() => workspace.ExtractTextureSet(input, maxEntryBytes, maxTotalBytes), scenario);
        }
        Assert(!Directory.Exists(root), $"NG4MOD workspace leaked after {scenario}.");
    }

    private static byte[] CreateArchive(params (string Name, string Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                if (name.EndsWith('/')) continue;
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return output.ToArray();
    }

    private static void AssertThrows<T>(Action action, string scenario) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} for {scenario}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
