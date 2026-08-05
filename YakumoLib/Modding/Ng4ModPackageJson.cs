using System.Text.Json;

namespace YakumoLib.Modding;

public static class Ng4ModPackageJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(Ng4ModManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateContract(manifest);
        return JsonSerializer.Serialize(manifest, Options);
    }

    public static Ng4ModManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ValidateDiscriminators(json);
        Ng4ModManifest manifest = JsonSerializer.Deserialize<Ng4ModManifest>(json, Options)
            ?? throw new InvalidDataException("Invalid NG4MOD manifest.");
        ValidateContract(manifest);
        return manifest;
    }

    private static void ValidateDiscriminators(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("NG4MOD manifest root must be an object.");

        if (!root.TryGetProperty("format", out JsonElement format) ||
            format.ValueKind != JsonValueKind.String ||
            !string.Equals(format.GetString(), "ng4mod", StringComparison.Ordinal))
            throw new InvalidDataException("NG4MOD manifest must contain exact string discriminator 'format' with value 'ng4mod'.");

        if (!root.TryGetProperty("schema_version", out JsonElement schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out int version) ||
            version != 2)
            throw new InvalidDataException("NG4MOD manifest must contain exact numeric discriminator 'schema_version' with value 2.");
    }

    private static void ValidateContract(Ng4ModManifest manifest)
    {
        if (!string.Equals(manifest.Format, "ng4mod", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported NG4MOD format '{manifest.Format}'.");
        if (manifest.SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported NG4MOD schema version {manifest.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.ModId))
            throw new InvalidDataException("NG4MOD mod_id is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("NG4MOD name is required.");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("NG4MOD version is required.");
        if (manifest.Author is null || manifest.Description is null || manifest.Icon is null)
            throw new InvalidDataException("NG4MOD author, description, and icon must be strings.");
        if (manifest.Cover is not null &&
            (!string.Equals(manifest.Cover.MediaType, "image/png", StringComparison.Ordinal) ||
             manifest.Cover.Offset < 0 || manifest.Cover.Length <= 0 || !IsSha256(manifest.Cover.Sha256)))
            throw new InvalidDataException("NG4MOD cover metadata is invalid.");
        if (manifest.Game is null)
            throw new InvalidDataException("NG4MOD game is required.");
        if (!string.Equals(manifest.Game.Id, Ng4ModManifest.SupportedGameId, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported NG4MOD game id '{manifest.Game.Id}'.");
        if (manifest.Dependencies is null)
            throw new InvalidDataException("NG4MOD dependencies are required.");
        if (manifest.Dependencies.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("NG4MOD dependencies cannot contain null or empty elements.");
        if (manifest.Assets is null)
            throw new InvalidDataException("NG4MOD assets are required.");

        foreach (Ng4ModAsset asset in manifest.Assets)
        {
            if (asset is null)
                throw new InvalidDataException("NG4MOD assets cannot contain null elements.");
            if (asset.SubEntries is null)
                throw new InvalidDataException("NG4MOD asset sub_entries are required.");
            if (asset.SubEntries.Any(subEntry => subEntry is null))
                throw new InvalidDataException("NG4MOD asset sub_entries cannot contain null elements.");
            if (asset.Intent is null)
                throw new InvalidDataException("NG4MOD asset intent is required.");
            if (asset.Intent.ReplacedSubEntries is null)
                throw new InvalidDataException("NG4MOD intent replaced_sub_entries are required.");
            if (asset.Intent.ReplacedSubEntries.Any(name => name is null))
                throw new InvalidDataException("NG4MOD intent replaced_sub_entries cannot contain null elements.");
        }
    }

    private static bool IsSha256(string? value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
}
