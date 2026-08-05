using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YakumoLib;
using YakumoLib.Assets;
using YakumoLib.Modding;

internal static class Ng4ModExporterTests
{
    public static string GenerateGoldenFixture(string destination)
    {
        string fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        byte[] payload = [10, 20, 30, 40, 50, 60];
        ModifiedAssetEntry change = CreateWholeParentChange(payload);
        string assetId = change.ParentEntry.AssetID.GetString();
        change = change with
        {
            ParentEntry = change.ParentEntry with
            {
                StoragePath = $"Assets/Files/7/5/d/{assetId}/asset.bin",
                CsvUnknown = "2",
                CsvMetadata = "fixture"
            }
        };
        string result = Ng4ModPackageExporter.Export(new Ng4ModExportRequest
        {
            ModId = "ronin.golden-fixture",
            Name = "RONIN Golden Fixture",
            Version = "1.0.0",
            Author = "RONIN Test",
            Description = "Synthetic cross-repository NG4MOD fixture.",
            Dependencies = [],
            Changes = [change],
            DestinationPath = fullPath
        });
        Ng4ModPackageExporter.VerifyPackage(result);
        return result;
    }

    public static void Run()
    {
        ExportsVerifiedPackageWithExactManifestAndPayload();
        ExportsAndVerifiesPngCoverOutsideDatPrefix();
        MultiAssetWriterBindsPayloadsByAssetIdRegardlessOfInputOrder();
        ExportsEmptyCsvMetadataAsExactEmptyStrings();
        BuildManifestNormalizesNonTextureSubEntryAddressing();
        ExportDoesNotTouchSourceArchives();
        RejectsInvalidRequestsBeforeCreatingOutput();
        RejectsDuplicateAssetsAndCorruptV2Containers();
        FailedExportPreservesExistingDestinationAndCleansTemporaryFile();
        ManifestSizeFieldsRemainInt64();
        GoldenFixtureUsesStrictConsumerContract();
        Console.WriteLine("NG4MOD exporter focused tests passed.");
    }

    private static void ExportsAndVerifiesPngCoverOutsideDatPrefix()
    {
        using var temp = new TemporaryDirectory();
        string coverPath = Path.Combine(temp.Path, "cover.png");
        byte[] coverBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(coverPath, coverBytes);
        string destination = Path.Combine(temp.Path, "covered.ng4mod");

        _ = Ng4ModPackageExporter.Export(CreateRequest(destination, [CreateWholeParentChange([1, 2, 3, 4, 5])]) with
        {
            CoverPath = coverPath
        });
        Ng4ModPackageExporter.VerifyPackage(destination);

        using var file = File.OpenRead(destination);
        file.Position = file.Length - 160;
        byte[] trailer = new byte[160];
        file.ReadExactly(trailer);
        long payloadLength = BitConverter.ToInt64(trailer, 24);
        long metadataOffset = BitConverter.ToInt64(trailer, 32);
        Assert(metadataOffset - payloadLength == coverBytes.LongLength, "Cover was not placed between DAT and metadata.");
        file.Position = payloadLength;
        byte[] storedCover = new byte[coverBytes.Length];
        file.ReadExactly(storedCover);
        Assert(storedCover.AsSpan().SequenceEqual(coverBytes), "Cover bytes changed during export.");

        using JsonDocument manifest = ReadManifest(destination);
        JsonElement cover = manifest.RootElement.GetProperty("cover");
        Assert(manifest.RootElement.GetProperty("icon").GetString() == "", "Legacy icon field must remain empty.");
        Assert(cover.GetProperty("media_type").GetString() == "image/png", "Cover media_type is wrong.");
        Assert(cover.GetProperty("offset").GetInt64() == payloadLength, "Cover offset is wrong.");
        Assert(cover.GetProperty("length").GetInt64() == coverBytes.LongLength, "Cover length is wrong.");
        Assert(cover.GetProperty("sha256").GetString() == Sha256(coverBytes), "Cover SHA-256 is wrong.");

        string mutatedPackage = Path.Combine(temp.Path, "mutated-cover.ng4mod");
        File.Copy(destination, mutatedPackage);
        using (var mutated = new FileStream(mutatedPackage, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            mutated.Position = payloadLength + 40;
            int original = mutated.ReadByte();
            mutated.Position--;
            mutated.WriteByte((byte)(original ^ 1));
        }
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.VerifyPackage(mutatedPackage), "mutated package cover");

        string corruptCover = Path.Combine(temp.Path, "corrupt.png");
        byte[] corruptBytes = coverBytes.ToArray();
        corruptBytes[corruptBytes.Length - 5] ^= 1;
        File.WriteAllBytes(corruptCover, corruptBytes);
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.Export(
            CreateRequest(Path.Combine(temp.Path, "corrupt-cover.ng4mod"), [CreateWholeParentChange([1, 2, 3])]) with
            { CoverPath = corruptCover }), "cover CRC corruption");

        string wrongExtension = Path.Combine(temp.Path, "cover.jpg");
        File.WriteAllBytes(wrongExtension, coverBytes);
        AssertThrows<ArgumentException>(() => Ng4ModPackageExporter.Export(
            CreateRequest(Path.Combine(temp.Path, "wrong-cover.ng4mod"), [CreateWholeParentChange([1, 2, 3])]) with
            { CoverPath = wrongExtension }), "non-PNG cover extension");
    }

    private static void GoldenFixtureUsesStrictConsumerContract()
    {
        using var temp = new TemporaryDirectory();
        string path = GenerateGoldenFixture(Path.Combine(temp.Path, "valid.ng4mod"));
        using JsonDocument document = ReadManifest(path);
        JsonElement asset = document.RootElement.GetProperty("assets")[0];
        string assetId = asset.GetProperty("asset_id").GetString()!;
        Assert(asset.GetProperty("storage_path").GetString() == $"Assets/Files/7/5/d/{assetId}/asset.bin",
            "Golden fixture storage_path is not canonical.");
        Assert(!asset.GetProperty("csv_unknown").GetString()!.Contains(','), "Golden fixture csv_unknown contains a delimiter.");
        Assert(!asset.GetProperty("csv_metadata").GetString()!.Contains(','), "Golden fixture csv_metadata contains a delimiter.");
        Ng4ModPackageExporter.VerifyPackage(path);
    }

    private static void ExportsVerifiedPackageWithExactManifestAndPayload()
    {
        using var temp = new TemporaryDirectory();
        string destination = Path.Combine(temp.Path, "example.ng4mod");
        byte[] payload = [10, 20, 30, 40, 50, 60];
        ModifiedAssetEntry change = CreateWholeParentChange(payload);

        string result = Ng4ModPackageExporter.Export(new Ng4ModExportRequest
        {
            ModId = "example.local-parent",
            Name = "Local Parent",
            Version = "1.2.3",
            Author = "RONIN Test",
            Description = "Synthetic package export.",
            Dependencies = ["base.contract@1"],
            AssetDatabaseSha256 = new string('a', 64),
            Changes = [change],
            DestinationPath = destination
        });

        Assert(result == Path.GetFullPath(destination), "Exporter returned the wrong destination path.");
        Assert(File.Exists(destination), "Exporter did not create the package.");
        Ng4ModPackageExporter.VerifyPackage(destination);

        using JsonDocument document = ReadManifest(destination);
        JsonElement root = document.RootElement;
        JsonElement asset = root.GetProperty("assets")[0];
        JsonElement firstSubEntry = asset.GetProperty("sub_entries")[0];
        JsonElement secondSubEntry = asset.GetProperty("sub_entries")[1];

        Assert(root.GetProperty("format").GetString() == "ng4mod", "Manifest format is wrong.");
        Assert(root.GetProperty("schema_version").GetInt32() == 2, "Manifest schema is wrong.");
        Assert(root.GetProperty("mod_id").GetString() == "example.local-parent", "Manifest mod_id is wrong.");
        Assert(root.GetProperty("author").GetString() == "RONIN Test", "Manifest author is wrong.");
        Assert(root.GetProperty("description").GetString() == "Synthetic package export.", "Manifest description is wrong.");
        Assert(root.GetProperty("icon").GetString() == "", "Manifest icon should be empty when no icon is provided.");
        Assert(root.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "base.contract@1" }),
            "Manifest dependencies are wrong.");
        Assert(root.GetProperty("game").GetProperty("id").GetString() == "ninja-gaiden-4", "Manifest game id is wrong.");
        Assert(root.GetProperty("game").GetProperty("asset_database_sha256").GetString() == new string('a', 64),
            "Manifest asset_database_sha256 is wrong.");
        Assert(asset.GetProperty("asset_id").GetString() == "75d246be-4ac8e807-63d20583-6eb1ac97", "Manifest asset_id is wrong.");
        Assert(asset.GetProperty("logical_path").GetString() == "Assets/Character/Test/LocalParent", "Manifest logical_path is wrong.");
        Assert(asset.GetProperty("asset_type").GetString() == "SkeletalMesh", "Manifest asset_type is wrong.");
        Assert(asset.GetProperty("storage_path").GetString() == "Assets/Files/7/5/d/75d246be-4ac8e807-63d20583-6eb1ac97/asset.bin", "Manifest storage_path is wrong.");
        Assert(asset.GetProperty("csv_unknown").GetString() == "2", "Manifest csv_unknown is wrong.");
        Assert(asset.GetProperty("csv_metadata").GetString() == "fixture", "Manifest csv_metadata is wrong.");
        Assert(asset.GetProperty("payload_path").GetString() == "payload/75d246be-4ac8e807-63d20583-6eb1ac97.bin", "Manifest payload_path is wrong.");
        Assert(asset.GetProperty("payload_size").GetInt64() == payload.LongLength, "Manifest payload_size is wrong.");
        Assert(asset.GetProperty("payload_sha256").GetString() == Sha256(payload), "Manifest payload SHA-256 is wrong.");
        Assert(asset.GetProperty("game_compression").GetString() == "none", "Manifest game_compression is wrong.");
        Assert(firstSubEntry.GetProperty("name").GetString() == "header.bin", "First sub-entry name is wrong.");
        Assert(firstSubEntry.GetProperty("addressing").GetString() == "local", "Local sub-entry addressing is wrong.");
        Assert(firstSubEntry.GetProperty("offset").GetInt64() == 0, "First sub-entry offset is wrong.");
        Assert(firstSubEntry.GetProperty("size").GetInt64() == 2, "First sub-entry size is wrong.");
        Assert(firstSubEntry.GetProperty("compressed_size").GetInt64() == 0, "First sub-entry compressed size is wrong.");
        Assert(secondSubEntry.GetProperty("offset").GetInt64() == 2, "Second sub-entry offset is wrong.");
        string[] replaced = asset.GetProperty("intent").GetProperty("replaced_sub_entries")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert(replaced.SequenceEqual(new[] { "header.bin", "modeldata.mdl" }), "Whole-parent intent did not list normalized sub-entries.");
        Assert(asset.GetProperty("intent").GetProperty("source_archive").GetString() == "@image4", "Source archive was not preserved.");
        Assert(asset.GetProperty("intent").GetProperty("source_fingerprint").GetString() == "", "Source fingerprint should be explicitly empty.");

        Assert(ReadPayload(destination, asset).AsSpan().SequenceEqual(payload), "DAT payload bytes changed during export.");
    }

    private static void MultiAssetWriterBindsPayloadsByAssetIdRegardlessOfInputOrder()
    {
        using var temp = new TemporaryDirectory();
        string destination = Path.Combine(temp.Path, "multi-asset.ng4mod");
        byte[] firstBytes = [1, 2, 3, 4, 5, 6];
        byte[] secondBytes = [11, 12, 13, 14, 15, 16];
        ModifiedAssetEntry first = CreateWholeParentChange(firstBytes);
        ModifiedAssetEntry secondBase = CreateWholeParentChange(secondBytes);
        ModifiedAssetEntry second = secondBase with
        {
            ParentEntry = secondBase.ParentEntry with
            {
                AssetID = UUIDParser.Parse("10b2c3d4-20b2c3d4-30b2c3d4-40b2c3d4"),
                Path = "Assets/Character/Test/SecondParent",
                StoragePath = "Assets/Files/1/0/b/10b2c3d4-20b2c3d4-30b2c3d4-40b2c3d4/asset.bin",
                SourceArchive = "@image5"
            }
        };
        ModifiedAssetEntry[] changes = [first, second];
        IReadOnlyList<ParentPayload> payloads = ParentPayloadBuilder.BuildAll(changes);
        Ng4ModExportRequest request = CreateRequest(destination, changes);
        Ng4ModManifest manifest = Ng4ModPackageExporter.BuildManifest(request, payloads);

        Ng4ModPackageExporter.WritePackage(destination, manifest, payloads.Reverse().ToArray(), iconSourcePath: null);
        Ng4ModPackageExporter.VerifyPackage(destination);

        var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [first.ParentEntry.AssetID.GetString()] = firstBytes,
            [second.ParentEntry.AssetID.GetString()] = secondBytes
        };
        foreach (Ng4ModAsset asset in manifest.Assets)
        {
            byte[] expectedBytes = expected[asset.AssetId];
            Assert(ReadPayload(destination, asset).AsSpan().SequenceEqual(expectedBytes), $"Payload bytes were bound to the wrong asset '{asset.AssetId}'.");
            Assert(asset.PayloadSha256 == Sha256(expectedBytes), $"Payload SHA was bound to the wrong asset '{asset.AssetId}'.");
        }
    }

    private static void ExportsEmptyCsvMetadataAsExactEmptyStrings()
    {
        using var temp = new TemporaryDirectory();
        string destination = Path.Combine(temp.Path, "empty-metadata.ng4mod");
        ModifiedAssetEntry change = CreateWholeParentChange([1, 2, 3, 4]);
        change = change with
        {
            ParentEntry = change.ParentEntry with { CsvUnknown = null, CsvMetadata = null }
        };

        _ = Ng4ModPackageExporter.Export(CreateRequest(destination, [change]));
        Ng4ModPackageExporter.VerifyPackage(destination);

        using JsonDocument document = ReadManifest(destination);
        JsonElement asset = document.RootElement.GetProperty("assets")[0];
        Assert(asset.GetProperty("csv_unknown").GetString() == "", "Null csv_unknown was not normalized to an empty string.");
        Assert(asset.GetProperty("csv_metadata").GetString() == "", "Null csv_metadata was not normalized to an empty string.");
    }

    private static void BuildManifestNormalizesNonTextureSubEntryAddressing()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        ModifiedAssetEntry change = CreateWholeParentChange(data);
        var payload = new ParentPayload(
            change.ParentEntry,
            data,
            [
                new NormalizedSubEntry("Image.img", 0L, 3L, IsGlobal: false),
                new NormalizedSubEntry("mip0.img", 3L, 5L, IsGlobal: true)
            ]);
        Ng4ModExportRequest request = CreateRequest("synthetic.ng4mod", [change]);

        Ng4ModManifest manifest = Ng4ModPackageExporter.BuildManifest(request, [payload]);

        Ng4ModSubEntry local = manifest.Assets.Single().SubEntries[0];
        Ng4ModSubEntry normalized = manifest.Assets.Single().SubEntries[1];
        Assert(local.Addressing == "local", "Local synthetic sub-entry addressing is wrong.");
        Assert(normalized.Addressing == "local", "NG4MOD v2 non-texture sub-entry was not normalized to local addressing.");
        Assert(normalized.Offset == 3L, "Normalized synthetic sub-entry offset is wrong.");
        Assert(normalized.Size == 5L, "Normalized synthetic sub-entry size is wrong.");
        Assert(normalized.CompressedSize == 0L, "Normalized synthetic sub-entry compressed size is wrong.");
        Assert(normalized.Offset.GetType() == typeof(long), "Normalized synthetic sub-entry offset is not Int64.");
        Assert(normalized.Size.GetType() == typeof(long), "Normalized synthetic sub-entry size is not Int64.");
    }

    private static void ExportDoesNotTouchSourceArchives()
    {
        using var temp = new TemporaryDirectory();
        string csv = Path.Combine(temp.Path, "@image1.csv");
        string dat = Path.Combine(temp.Path, "@image1.dat");
        File.WriteAllBytes(csv, [1, 2, 3]);
        File.WriteAllBytes(dat, [4, 5, 6, 7]);
        string csvHash = Sha256(File.ReadAllBytes(csv));
        string datHash = Sha256(File.ReadAllBytes(dat));
        string[] filesBefore = Directory.GetFiles(temp.Path, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _ = Ng4ModPackageExporter.Export(CreateRequest(
            Path.Combine(temp.Path, "isolated.ng4mod"),
            [CreateWholeParentChange([9, 8, 7, 6])]));

        Assert(Sha256(File.ReadAllBytes(csv)) == csvHash, "Exporter modified the source CSV.");
        Assert(Sha256(File.ReadAllBytes(dat)) == datHash, "Exporter modified the source DAT.");
        string[] filesAfter = Directory.GetFiles(temp.Path, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert(filesAfter.Except(filesBefore, StringComparer.OrdinalIgnoreCase).SequenceEqual(["isolated.ng4mod"]),
            "Exporter created an unexpected source archive file.");
    }

    private static void RejectsInvalidRequestsBeforeCreatingOutput()
    {
        using var temp = new TemporaryDirectory();
        string destination = Path.Combine(temp.Path, "invalid.ng4mod");
        ModifiedAssetEntry valid = CreateWholeParentChange([1, 2, 3]);

        AssertThrows<ArgumentException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination, [valid]) with { ModId = "" }), "empty mod id");
        AssertThrows<ArgumentException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination, [valid]) with { Name = "" }), "empty name");
        AssertThrows<ArgumentException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination, [valid]) with { Version = "" }), "empty version");
        AssertThrows<ArgumentException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination, [])), "no changes");
        AssertThrows<NotSupportedException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination, [valid with { Compress = true }])), "game compression request");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination,
            [valid with { ParentEntry = valid.ParentEntry with { StoragePath = "" } }])), "missing storage path");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination,
            [valid with { ParentEntry = valid.ParentEntry with { StoragePath = "../escape.bin" } }])), "unsafe storage path");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination,
            [valid with { ParentEntry = valid.ParentEntry with { StoragePath = "Assets\\Files\\fixture.bin" } }])), "backslash storage path");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination,
            [valid with { ParentEntry = valid.ParentEntry with { StoragePath = "Assets/C:/fixture.bin" } }])), "colon storage path");
        Assert(!File.Exists(destination), "Invalid request created an output package.");
    }

    private static void RejectsDuplicateAssetsAndCorruptV2Containers()
    {
        using var temp = new TemporaryDirectory();
        string destination = Path.Combine(temp.Path, "duplicate.ng4mod");
        ModifiedAssetEntry first = CreateWholeParentChange([1, 2, 3]);
        ModifiedAssetEntry second = first with
        {
            ParentEntry = first.ParentEntry with { Path = "Assets/Character/Test/DuplicateIdentity" },
            ModifiedData = [4, 5, 6]
        };
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.Export(CreateRequest(destination, [first, second])), "duplicate asset id");

        string valid = Ng4ModPackageExporter.Export(CreateRequest(Path.Combine(temp.Path, "valid.ng4mod"), [first]));
        string corruptPayload = Path.Combine(temp.Path, "corrupt-payload.ng4mod");
        File.Copy(valid, corruptPayload);
        using (var file = new FileStream(corruptPayload, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            file.Position = 0;
            file.WriteByte((byte)(file.ReadByte() ^ 0xff));
        }
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.VerifyPackage(corruptPayload), "corrupt DAT prefix");

        string corruptTrailer = Path.Combine(temp.Path, "corrupt-trailer.ng4mod");
        File.Copy(valid, corruptTrailer);
        using (var file = new FileStream(corruptTrailer, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            file.Position = file.Length - 1;
            file.WriteByte(1);
        }
        AssertThrows<InvalidDataException>(() => Ng4ModPackageExporter.VerifyPackage(corruptTrailer), "corrupt trailer hash");
    }

    private static void FailedExportPreservesExistingDestinationAndCleansTemporaryFile()
    {
        using var temp = new TemporaryDirectory();
        string destination = Path.Combine(temp.Path, "existing.ng4mod");
        byte[] original = [42, 43, 44];
        File.WriteAllBytes(destination, original);
        using (FileStream lockedDestination = new(destination, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AssertThrows<UnauthorizedAccessException>(() => Ng4ModPackageExporter.Export(CreateRequest(
                destination, [CreateWholeParentChange([1, 2, 3])])), "locked destination replacement");
        }

        Assert(File.ReadAllBytes(destination).AsSpan().SequenceEqual(original), "Failed export replaced the existing destination.");
        Assert(Directory.GetFiles(temp.Path, "existing.ng4mod.tmp-*", SearchOption.TopDirectoryOnly).Length == 0,
            "Failed export left a sibling temporary file.");
    }

    private static void ManifestSizeFieldsRemainInt64()
    {
        Assert(typeof(Ng4ModAsset).GetProperty(nameof(Ng4ModAsset.PayloadSize))!.PropertyType == typeof(long), "payload_size is not Int64.");
        Assert(typeof(Ng4ModAsset).GetProperty(nameof(Ng4ModAsset.PayloadOffset))!.PropertyType == typeof(long), "payload_offset is not Int64.");
        Assert(typeof(Ng4ModSubEntry).GetProperty(nameof(Ng4ModSubEntry.Offset))!.PropertyType == typeof(long), "sub-entry offset is not Int64.");
        Assert(typeof(Ng4ModSubEntry).GetProperty(nameof(Ng4ModSubEntry.Size))!.PropertyType == typeof(long), "sub-entry size is not Int64.");
        Assert(typeof(Ng4ModSubEntry).GetProperty(nameof(Ng4ModSubEntry.CompressedSize))!.PropertyType == typeof(long), "compressed_size is not Int64.");
        Assert(typeof(Ng4ModCover).GetProperty(nameof(Ng4ModCover.Offset))!.PropertyType == typeof(long), "cover offset is not Int64.");
        Assert(typeof(Ng4ModCover).GetProperty(nameof(Ng4ModCover.Length))!.PropertyType == typeof(long), "cover length is not Int64.");
    }

    private static Ng4ModExportRequest CreateRequest(string destination, IReadOnlyList<ModifiedAssetEntry> changes) => new()
    {
        ModId = "export.test",
        Name = "Export Test",
        Version = "1.0.0",
        Changes = changes,
        DestinationPath = destination
    };

    private static ModifiedAssetEntry CreateWholeParentChange(byte[] payload)
    {
        var parent = new AssetEntry
        {
            Path = "Assets/Character/Test/LocalParent",
            Type = AssetType.SkeletalMesh,
            AssetID = UUIDParser.Parse("75d246be-4ac8e807-63d20583-6eb1ac97"),
            Size = payload.LongLength,
            CompressedSize = 0,
            Offset = 123,
            SourceArchive = "@image4",
            StoragePath = "Assets/Files/7/5/d/75d246be-4ac8e807-63d20583-6eb1ac97/asset.bin",
            CsvUnknown = "2",
            CsvMetadata = "fixture",
            ContentDirectory = "unused-synthetic-directory",
            Pruned = false,
            SubEntries =
            [
                new SubAssetEntry
                {
                    FileName = "header.bin", Offset = 0, Size = 2, CompressedSize = 0,
                    SourceArchive = "@image4", ContentDirectory = "unused-synthetic-directory",
                    IsCompressed = false, IsGlobal = false
                },
                new SubAssetEntry
                {
                    FileName = "modeldata.mdl", Offset = 2, Size = payload.LongLength - 2, CompressedSize = 0,
                    SourceArchive = "@image4", ContentDirectory = "unused-synthetic-directory",
                    IsCompressed = false, IsGlobal = false
                }
            ]
        };
        return new ModifiedAssetEntry
        {
            ParentEntry = parent,
            ModifiedData = payload,
            OriginalData = payload.ToArray(),
            Compress = false
        };
    }

    private static JsonDocument ReadManifest(string path)
    {
        using var file = File.OpenRead(path);
        file.Position = file.Length - 160;
        byte[] trailer = new byte[160];
        file.ReadExactly(trailer);
        long metadataOffset = BitConverter.ToInt64(trailer, 32);
        int metadataLength = checked((int)BitConverter.ToInt64(trailer, 40));
        file.Position = metadataOffset;
        byte[] metadata = new byte[metadataLength];
        file.ReadExactly(metadata);
        return JsonDocument.Parse(metadata);
    }

    private static byte[] ReadPayload(string path, JsonElement asset)
    {
        using var file = File.OpenRead(path);
        file.Position = asset.GetProperty("payload_offset").GetInt64();
        byte[] result = new byte[checked((int)asset.GetProperty("payload_size").GetInt64())];
        file.ReadExactly(result);
        return result;
    }

    private static byte[] ReadPayload(string path, Ng4ModAsset asset)
    {
        using var file = File.OpenRead(path);
        file.Position = asset.PayloadOffset;
        byte[] result = new byte[checked((int)asset.PayloadSize)];
        file.ReadExactly(result);
        return result;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertThrows<TException>(Action action, string scenario) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name} for {scenario}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ronin-ng4-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
