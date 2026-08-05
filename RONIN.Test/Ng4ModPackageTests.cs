using System.Text.Json;
using System.Text.Json.Nodes;
using YakumoLib.Modding;

internal static class Ng4ModPackageTests
{
    public static void Run()
    {
        RejectsMissingOrInvalidDiscriminators();
        RejectsNullRequiredGraphMembers();
        FactoryCreatesNinjaGaiden4Manifest();
        RejectsInvalidGameId();
        RoundTripsManifestWithInt64OffsetsAndSnakeCaseNames();
        RejectsInvalidInputAndUnsupportedContracts();
        Console.WriteLine("NG4MOD package focused tests passed.");
    }

    private static void RejectsMissingOrInvalidDiscriminators()
    {
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("{}"), "missing discriminators");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("""
            { "schema_version": 2 }
            """), "missing format");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("""
            { "format": "ng4mod" }
            """), "missing schema_version");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("""
            { "Format": "other", "Schema_Version": 2 }
            """), "miscased discriminator names");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("""
            { "format": 1, "schema_version": 2 }
            """), "non-string format");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("""
            { "format": "ng4mod", "schema_version": "1" }
            """), "non-number schema_version");
    }

    private static void RejectsNullRequiredGraphMembers()
    {
        Ng4ModManifest valid = CreateValidManifest();
        Ng4ModAsset asset = valid.Assets.Single();
        Ng4ModAssetIntent intent = asset.Intent;

        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Game = null! }), "null game during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Assets = null! }), "null assets during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Assets = [null!] }), "null asset during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Dependencies = null! }), "null dependencies during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Dependencies = [null!] }), "null dependency during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Assets = [asset with { SubEntries = null! }] }), "null sub_entries during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Assets = [asset with { SubEntries = [null!] }] }), "null sub-entry during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Assets = [asset with { Intent = null! }] }), "null intent during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Assets = [asset with { Intent = intent with { ReplacedSubEntries = null! } }] }), "null replaced_sub_entries during serialization");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Assets = [asset with { Intent = intent with { ReplacedSubEntries = [null!] } }] }), "null replaced sub-entry during serialization");

        string json = Ng4ModPackageJson.Serialize(valid);
        AssertJsonMutationRejected(json, root => root["game"] = null, "null game during deserialization");
        AssertJsonMutationRejected(json, root => root["assets"] = null, "null assets during deserialization");
        AssertJsonMutationRejected(json, root => root["assets"]!.AsArray()[0] = null, "null asset during deserialization");
        AssertJsonMutationRejected(json, root => root["dependencies"] = null, "null dependencies during deserialization");
        AssertJsonMutationRejected(json, root => root["dependencies"]!.AsArray()[0] = null, "null dependency during deserialization");
        AssertJsonMutationRejected(json, root => root["assets"]![0]!["sub_entries"] = null, "null sub_entries during deserialization");
        AssertJsonMutationRejected(json, root => root["assets"]![0]!["sub_entries"]!.AsArray()[0] = null, "null sub-entry during deserialization");
        AssertJsonMutationRejected(json, root => root["assets"]![0]!["intent"] = null, "null intent during deserialization");
        AssertJsonMutationRejected(json, root => root["assets"]![0]!["intent"]!["replaced_sub_entries"] = null, "null replaced_sub_entries during deserialization");
        AssertJsonMutationRejected(json, root => root["assets"]![0]!["intent"]!["replaced_sub_entries"]!.AsArray()[0] = null, "null replaced sub-entry during deserialization");
    }

    private static void FactoryCreatesNinjaGaiden4Manifest()
    {
        Ng4ModManifest manifest = Ng4ModManifest.Create("factory.test", "Factory Test", "1.0.0");
        Assert(manifest.Game.Id == "ninja-gaiden-4", "Factory did not target Ninja Gaiden 4.");
    }

    private static void RejectsInvalidGameId()
    {
        Ng4ModManifest valid = CreateValidManifest();
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Game = new Ng4ModGame { Id = "" } }), "empty game id");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Game = new Ng4ModGame { Id = "other-game" } }), "wrong game id");

        string json = Ng4ModPackageJson.Serialize(valid);
        AssertJsonMutationRejected(json, root => root["game"]!["id"] = "", "empty game id during deserialization");
        AssertJsonMutationRejected(json, root => root["game"]!["id"] = "other-game", "wrong game id during deserialization");
    }

    private static void RoundTripsManifestWithInt64OffsetsAndSnakeCaseNames()
    {
        const long payloadSize = 5_396_740_137L;
        const long subEntryOffset = 5_374_366_877L;
        const long subEntrySize = 5_000_000_123L;
        const long compressedSize = 4_000_000_321L;

        Ng4ModManifest manifest = Ng4ModManifest.Create("example.large-asset", "Large Asset", "1.2.3") with
        {
            Author = "RONIN Test",
            Description = "Exercises the NG4MOD v1 package contract.",
            Icon = "icon.png",
            Dependencies = ["base.contract@1"],
            Game = new Ng4ModGame
            {
                Id = "ninja-gaiden-4",
                AssetDatabaseSha256 = "database-sha256"
            },
            Assets =
            [
                new Ng4ModAsset
                {
                    AssetId = "75d246be-4ac8e807-63d20583-6eb1ac97",
                    LogicalPath = "Assets/Character/Test/LargeAsset",
                    AssetType = "SkeletalMesh",
                    StoragePath = "Assets/Files/7/5/d/75d246be-4ac8e807-63d20583-6eb1ac97/asset.bin",
                    CsvUnknown = "2",
                    CsvMetadata = "0,",
                    PayloadPath = "payloads/large-asset.bin",
                    PayloadSize = payloadSize,
                    PayloadSha256 = "payload-sha256",
                    GameCompression = "gdeflate",
                    SubEntries =
                    [
                        new Ng4ModSubEntry
                        {
                            Name = "model.mdl",
                            Addressing = "archive_offset",
                            Offset = subEntryOffset,
                            Size = subEntrySize,
                            CompressedSize = compressedSize
                        }
                    ],
                    Intent = new Ng4ModAssetIntent
                    {
                        ReplacedSubEntries = ["model.mdl"],
                        SourceArchive = "@image4",
                        SourceFingerprint = "source-fingerprint"
                    }
                }
            ]
        };

        string json = Ng4ModPackageJson.Serialize(manifest);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement asset = root.GetProperty("assets")[0];
        JsonElement subEntry = asset.GetProperty("sub_entries")[0];
        JsonElement intent = asset.GetProperty("intent");

        Assert(root.GetProperty("format").GetString() == "ng4mod", "format was not serialized as ng4mod.");
        Assert(root.GetProperty("schema_version").GetInt32() == 2, "schema_version was not serialized as 2.");
        Assert(root.TryGetProperty("mod_id", out _), "mod_id snake_case token is missing.");
        Assert(root.TryGetProperty("name", out _), "name token is missing.");
        Assert(root.TryGetProperty("version", out _), "version token is missing.");
        Assert(root.TryGetProperty("author", out _), "author token is missing.");
        Assert(root.TryGetProperty("description", out _), "description token is missing.");
        Assert(root.TryGetProperty("icon", out _), "icon token is missing.");
        Assert(root.TryGetProperty("dependencies", out _), "dependencies token is missing.");
        JsonElement game = root.GetProperty("game");
        Assert(game.TryGetProperty("id", out _), "game id token is missing.");
        Assert(game.TryGetProperty("asset_database_sha256", out _), "asset_database_sha256 snake_case token is missing.");
        Assert(asset.TryGetProperty("asset_id", out _), "asset_id snake_case token is missing.");
        Assert(asset.TryGetProperty("logical_path", out _), "logical_path snake_case token is missing.");
        Assert(asset.TryGetProperty("asset_type", out _), "asset_type snake_case token is missing.");
        Assert(asset.TryGetProperty("storage_path", out _), "storage_path snake_case token is missing.");
        Assert(asset.TryGetProperty("csv_unknown", out _), "csv_unknown snake_case token is missing.");
        Assert(asset.TryGetProperty("csv_metadata", out _), "csv_metadata snake_case token is missing.");
        Assert(asset.TryGetProperty("payload_path", out _), "payload_path snake_case token is missing.");
        Assert(asset.GetProperty("payload_size").GetInt64() == payloadSize, "payload_size was not serialized as Int64.");
        Assert(asset.TryGetProperty("payload_sha256", out _), "payload_sha256 snake_case token is missing.");
        Assert(asset.TryGetProperty("game_compression", out _), "game_compression snake_case token is missing.");
        Assert(asset.TryGetProperty("sub_entries", out _), "sub_entries snake_case token is missing.");
        Assert(subEntry.TryGetProperty("name", out _), "sub-entry name token is missing.");
        Assert(subEntry.TryGetProperty("addressing", out _), "sub-entry addressing token is missing.");
        Assert(subEntry.GetProperty("offset").GetInt64() == subEntryOffset, "offset was not serialized as Int64.");
        Assert(subEntry.GetProperty("size").GetInt64() == subEntrySize, "size was not serialized as Int64.");
        Assert(subEntry.GetProperty("compressed_size").GetInt64() == compressedSize, "compressed_size was not serialized as Int64.");
        Assert(asset.TryGetProperty("intent", out _), "intent token is missing.");
        Assert(intent.TryGetProperty("replaced_sub_entries", out _), "replaced_sub_entries snake_case token is missing.");
        Assert(intent.TryGetProperty("source_archive", out _), "source_archive snake_case token is missing.");
        Assert(intent.TryGetProperty("source_fingerprint", out _), "source_fingerprint snake_case token is missing.");

        Ng4ModManifest roundTrip = Ng4ModPackageJson.Deserialize(json);
        Assert(roundTrip.Format == "ng4mod", "format changed during round-trip.");
        Assert(roundTrip.SchemaVersion == 2, "schema_version changed during round-trip.");
        Assert(roundTrip.ModId == manifest.ModId, "mod_id changed during round-trip.");
        Assert(roundTrip.Assets.Single().PayloadSize == payloadSize, "payload_size lost Int64 precision during round-trip.");
        Assert(roundTrip.Assets.Single().SubEntries.Single().Offset == subEntryOffset, "sub-entry offset lost Int64 precision during round-trip.");
        Assert(roundTrip.Assets.Single().SubEntries.Single().Size == subEntrySize, "sub-entry size lost Int64 precision during round-trip.");
        Assert(roundTrip.Assets.Single().SubEntries.Single().CompressedSize == compressedSize, "sub-entry compressed_size lost Int64 precision during round-trip.");
        Assert(roundTrip.Assets.Single().StoragePath == manifest.Assets.Single().StoragePath, "storage_path changed during round-trip.");
    }

    private static void RejectsInvalidInputAndUnsupportedContracts()
    {
        Ng4ModManifest valid = Ng4ModManifest.Create("validation.test", "Validation Test", "1.0.0");

        AssertThrows<ArgumentNullException>(() => Ng4ModPackageJson.Serialize(null!), "null manifest");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { Format = "other" }), "wrong manifest format");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Serialize(valid with { SchemaVersion = 1 }), "unsupported manifest schema");
        AssertThrows<ArgumentNullException>(() => Ng4ModPackageJson.Deserialize(null!), "null JSON");
        AssertThrows<ArgumentException>(() => Ng4ModPackageJson.Deserialize("  "), "empty JSON");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("""
            { "format": "other", "schema_version": 2 }
            """), "wrong format");
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize("""
            { "format": "ng4mod", "schema_version": 1 }
            """), "unsupported schema");
    }

    private static Ng4ModManifest CreateValidManifest()
    {
        return Ng4ModManifest.Create("validation.test", "Validation Test", "1.0.0") with
        {
            Dependencies = ["base.contract@1"],
            Game = new Ng4ModGame { Id = "ninja-gaiden-4", AssetDatabaseSha256 = "database-sha256" },
            Assets =
            [
                new Ng4ModAsset
                {
                    AssetId = "75d246be-4ac8e807-63d20583-6eb1ac97",
                    SubEntries = [new Ng4ModSubEntry { Name = "model.mdl" }],
                    Intent = new Ng4ModAssetIntent { ReplacedSubEntries = ["model.mdl"] }
                }
            ]
        };
    }

    private static void AssertJsonMutationRejected(string json, Action<JsonObject> mutation, string scenario)
    {
        JsonObject root = JsonNode.Parse(json)?.AsObject() ?? throw new Exception("Valid manifest JSON was not an object.");
        mutation(root);
        AssertThrows<InvalidDataException>(() => Ng4ModPackageJson.Deserialize(root.ToJsonString()), scenario);
    }

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
}
