using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoninNg4Host;

public sealed record Ng4ModUploadMetadata
{
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

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public static Ng4ModUploadMetadata Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Multipart field 'metadata' is required.");
        Ng4ModUploadMetadata metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<Ng4ModUploadMetadata>(json)
                ?? throw new InvalidDataException("NG4MOD metadata JSON is null.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("NG4MOD metadata JSON is invalid.", error);
        }
        if (string.IsNullOrWhiteSpace(metadata.ModId) || string.IsNullOrWhiteSpace(metadata.Name) ||
            string.IsNullOrWhiteSpace(metadata.Version) || metadata.Dependencies is null ||
            metadata.Dependencies.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("NG4MOD metadata requires mod_id, name, version, and non-empty dependencies.");
        return metadata;
    }
}

public sealed record Ng4ModBatchItem
{
    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = "";

    [JsonPropertyName("glb_field")]
    public string GlbField { get; init; } = "";

    [JsonPropertyName("texture_field")]
    public string TextureField { get; init; } = "";
}

public static class Ng4ModBatchContract
{
    public static IReadOnlyList<Ng4ModBatchItem> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Multipart field 'models' is required.");
        Ng4ModBatchItem[] items;
        try { items = JsonSerializer.Deserialize<Ng4ModBatchItem[]>(json) ?? []; }
        catch (JsonException error) { throw new InvalidDataException("NG4MOD models JSON is invalid.", error); }
        if (items.Length == 0 || items.Length > 64) throw new InvalidDataException("NG4MOD batch must contain 1 to 64 models.");
        foreach (Ng4ModBatchItem item in items)
            if (string.IsNullOrWhiteSpace(item.AssetId) || string.IsNullOrWhiteSpace(item.GlbField) || string.IsNullOrWhiteSpace(item.TextureField))
                throw new InvalidDataException("Each NG4MOD batch model requires asset_id, glb_field, and texture_field.");
        if (items.Select(item => item.AssetId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
            throw new InvalidDataException("NG4MOD batch contains duplicate asset IDs.");
        return items;
    }
}

public sealed class Ng4ModUploadWorkspace : IDisposable
{
    private string? _rootDirectory;

    private Ng4ModUploadWorkspace(string rootDirectory) => _rootDirectory = rootDirectory;

    public string RootDirectory => _rootDirectory ?? throw new ObjectDisposedException(nameof(Ng4ModUploadWorkspace));
    public string GlbPath => Path.Combine(RootDirectory, "model.glb");
    public string TextureSetDirectory => Path.Combine(RootDirectory, "textures");
    public string PackagePath => Path.Combine(RootDirectory, "result.ng4mod");

    public static Ng4ModUploadWorkspace Create()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ronin-ng4mod-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return new Ng4ModUploadWorkspace(root);
    }

    public async Task CopyGlbAsync(Stream input, long maxBytes, CancellationToken cancellationToken)
        => await CopyLimitedAsync(input, GlbPath, maxBytes, cancellationToken);

    public async Task<string> CopyIconAsync(Stream input, string fileName, long maxBytes, CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(Path.GetFileName(fileName));
        if (extension.Length > 16 || extension.Any(character => !char.IsLetterOrDigit(character) && character != '.'))
            extension = ".bin";
        string path = Path.Combine(RootDirectory, "icon" + extension.ToLowerInvariant());
        await CopyLimitedAsync(input, path, maxBytes, cancellationToken);
        return path;
    }

    public void ExtractTextureSet(Stream input, long maxEntryBytes, long maxTotalBytes)
    {
        if (maxEntryBytes <= 0 || maxTotalBytes <= 0 || maxEntryBytes > maxTotalBytes)
            throw new ArgumentOutOfRangeException(nameof(maxEntryBytes));
        if (Directory.Exists(TextureSetDirectory)) throw new InvalidOperationException("Texture set was already extracted.");
        Directory.CreateDirectory(TextureSetDirectory);
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string relative = ValidateEntryPath(entry.FullName);
                if (!paths.Add(relative)) throw new InvalidDataException($"Texture ZIP contains duplicate entry '{relative}'.");
                if (entry.Length > maxEntryBytes) throw new InvalidDataException($"Texture ZIP entry '{relative}' exceeds the size limit.");
                total = checked(total + entry.Length);
                if (total > maxTotalBytes) throw new InvalidDataException("Texture ZIP exceeds the total extracted-size limit.");

                string destination = Path.Combine(TextureSetDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                if (relative.EndsWith('/'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using Stream source = entry.Open();
                using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                CopyLimited(source, target, entry.Length, relative);
            }
        }
        catch
        {
            if (Directory.Exists(TextureSetDirectory)) Directory.Delete(TextureSetDirectory, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        string? root = Interlocked.Exchange(ref _rootDirectory, null);
        if (root is not null && Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static string ValidateEntryPath(string path)
    {
        bool directory = path.EndsWith("/", StringComparison.Ordinal);
        string value = directory ? path[..^1] : path;
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.Contains(':') ||
            Path.IsPathRooted(value) || value.StartsWith("/", StringComparison.Ordinal) ||
            value.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException($"Texture ZIP entry path '{path}' is unsafe.");
        return path;
    }

    private static void CopyLimited(Stream source, Stream target, long expectedBytes, string entryName)
    {
        byte[] buffer = new byte[1024 * 1024];
        long written = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            written = checked(written + read);
            if (written > expectedBytes) throw new InvalidDataException($"Texture ZIP entry '{entryName}' expanded beyond its declared size.");
            target.Write(buffer, 0, read);
        }
        if (written != expectedBytes) throw new InvalidDataException($"Texture ZIP entry '{entryName}' did not match its declared size.");
    }

    private static async Task CopyLimitedAsync(Stream source, string destination, long maxBytes, CancellationToken cancellationToken)
    {
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);
        byte[] buffer = new byte[1024 * 1024];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            written = checked(written + read);
            if (written > maxBytes) throw new InvalidDataException("Uploaded file exceeds its size limit.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

public sealed class TemporaryNg4ModPackage : IDisposable
{
    private Ng4ModUploadWorkspace? _workspace;

    public TemporaryNg4ModPackage(Ng4ModUploadWorkspace workspace, int batchCount, int groupCount, int changedTextures, string fileName)
    {
        _workspace = workspace;
        BatchCount = batchCount;
        GroupCount = groupCount;
        ChangedTextures = changedTextures;
        FileName = fileName;
    }

    public string Path => (_workspace ?? throw new ObjectDisposedException(nameof(TemporaryNg4ModPackage))).PackagePath;
    public int BatchCount { get; }
    public int GroupCount { get; }
    public int ChangedTextures { get; }
    public string FileName { get; }

    public void Dispose() => Interlocked.Exchange(ref _workspace, null)?.Dispose();
}

public sealed class TemporaryNg4ModBatchPackage : IDisposable
{
    private IReadOnlyList<Ng4ModUploadWorkspace>? _workspaces;
    public TemporaryNg4ModBatchPackage(IReadOnlyList<Ng4ModUploadWorkspace> workspaces, string packagePath, int batchCount, int groupCount, int changedTextures, string fileName)
    {
        _workspaces = workspaces;
        Path = packagePath;
        BatchCount = batchCount;
        GroupCount = groupCount;
        ChangedTextures = changedTextures;
        FileName = fileName;
    }
    public string Path { get; }
    public int BatchCount { get; }
    public int GroupCount { get; }
    public int ChangedTextures { get; }
    public string FileName { get; }
    public void Dispose()
    {
        IReadOnlyList<Ng4ModUploadWorkspace>? workspaces = Interlocked.Exchange(ref _workspaces, null);
        if (workspaces is null) return;
        foreach (Ng4ModUploadWorkspace workspace in workspaces) workspace.Dispose();
    }
}
