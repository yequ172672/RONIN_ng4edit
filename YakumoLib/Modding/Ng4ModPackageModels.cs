using System.Text.Json.Serialization;

namespace YakumoLib.Modding;

public sealed record Ng4ModManifest
{
    public const string SupportedGameId = "ninja-gaiden-4";

    [JsonPropertyName("format")]
    public string Format { get; init; } = "ng4mod";

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("mod_id")]
    public string ModId { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("author")]
    public string Author { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "";

    [JsonPropertyName("cover")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Ng4ModCover? Cover { get; init; }

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    [JsonPropertyName("game")]
    public Ng4ModGame Game { get; init; } = new();

    [JsonPropertyName("assets")]
    public IReadOnlyList<Ng4ModAsset> Assets { get; init; } = [];

    public static Ng4ModManifest Create(string modId, string name, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        return new Ng4ModManifest
        {
            ModId = modId,
            Name = name,
            Version = version,
            Game = new Ng4ModGame { Id = SupportedGameId }
        };
    }
}

public sealed record Ng4ModCover
{
    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "";

    [JsonPropertyName("offset")]
    public long Offset { get; init; }

    [JsonPropertyName("length")]
    public long Length { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
}

public sealed record Ng4ModGame
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("asset_database_sha256")]
    public string AssetDatabaseSha256 { get; init; } = "";
}

public sealed record Ng4ModAsset
{
    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = "";

    [JsonPropertyName("logical_path")]
    public string LogicalPath { get; init; } = "";

    [JsonPropertyName("asset_type")]
    public string AssetType { get; init; } = "";

    [JsonPropertyName("storage_path")]
    public string StoragePath { get; init; } = "";

    [JsonPropertyName("csv_unknown")]
    public string CsvUnknown { get; init; } = "";

    [JsonPropertyName("csv_metadata")]
    public string CsvMetadata { get; init; } = "";

    [JsonPropertyName("payload_path")]
    public string PayloadPath { get; init; } = "";

    [JsonPropertyName("payload_size")]
    public long PayloadSize { get; init; }

    [JsonPropertyName("payload_sha256")]
    public string PayloadSha256 { get; init; } = "";

    [JsonPropertyName("payload_offset")]
    public long PayloadOffset { get; init; }

    [JsonPropertyName("game_compression")]
    public string GameCompression { get; init; } = "";

    [JsonPropertyName("sub_entries")]
    public IReadOnlyList<Ng4ModSubEntry> SubEntries { get; init; } = [];

    [JsonPropertyName("intent")]
    public Ng4ModAssetIntent Intent { get; init; } = new();
}

public sealed record Ng4ModSubEntry
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("addressing")]
    public string Addressing { get; init; } = "";

    [JsonPropertyName("offset")]
    public long Offset { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("compressed_size")]
    public long CompressedSize { get; init; }
}

public sealed record Ng4ModAssetIntent
{
    [JsonPropertyName("replaced_sub_entries")]
    public IReadOnlyList<string> ReplacedSubEntries { get; init; } = [];

    [JsonPropertyName("source_archive")]
    public string SourceArchive { get; init; } = "";

    [JsonPropertyName("source_fingerprint")]
    public string SourceFingerprint { get; init; } = "";
}
