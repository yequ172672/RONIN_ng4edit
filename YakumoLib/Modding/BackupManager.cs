using System.Security.Cryptography;

namespace YakumoLib.Modding;

/// <summary>Creates and restores SHA-256-verified snapshots under a configured game root.</summary>
public sealed class BackupManager
{
    private readonly string _gameRoot;
    private readonly string _backupRoot;
    private readonly HashSet<string> _backedUpFiles = new(StringComparer.OrdinalIgnoreCase);

    public BackupManager(string gameRoot)
        : this(gameRoot, snapshotPath: null)
    {
    }

    private BackupManager(string gameRoot, string? snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new ArgumentException("Game root is required.", nameof(gameRoot));

        _gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if (!Directory.Exists(_gameRoot))
            throw new DirectoryNotFoundException($"Game root does not exist: '{_gameRoot}'.");

        string backupsDirectory = Path.Combine(_gameRoot, "Backups");
        if (snapshotPath is null)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss_fff");
            _backupRoot = Path.Combine(backupsDirectory, timestamp);
            Directory.CreateDirectory(_backupRoot);
        }
        else
        {
            _backupRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(snapshotPath));
            EnsureWithinRoot(backupsDirectory, _backupRoot, "Snapshot");
            if (!Directory.Exists(_backupRoot))
                throw new DirectoryNotFoundException($"Backup snapshot does not exist: '{_backupRoot}'.");
        }
    }

    public string BackupRoot => _backupRoot;
    public string GameRoot => _gameRoot;

    public static BackupManager OpenSnapshot(string gameRoot, string snapshotPath) => new(gameRoot, snapshotPath);

    public string? Backup(string relativeOrAbsolutePath)
    {
        string fullPath = ResolveGamePath(relativeOrAbsolutePath);
        if (!File.Exists(fullPath))
            return null;

        string relative = Path.GetRelativePath(_gameRoot, fullPath);
        string backupPath = Path.GetFullPath(Path.Combine(_backupRoot, relative));
        EnsureWithinRoot(_backupRoot, backupPath, "Backup destination");

        lock (_backedUpFiles)
        {
            if (_backedUpFiles.Contains(fullPath))
            {
                VerifyEqual(fullPath, backupPath);
                return backupPath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(fullPath, backupPath, overwrite: true);
            VerifyEqual(fullPath, backupPath);
            _backedUpFiles.Add(fullPath);
        }

        return backupPath;
    }

    public int BackupDirectory(string relativeOrAbsolutePath)
    {
        string fullPath = ResolveGamePath(relativeOrAbsolutePath);
        if (!Directory.Exists(fullPath))
            return 0;

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
        {
            if (IsWithinRoot(_backupRoot, file))
                continue;
            if (Backup(file) is not null)
                count++;
        }
        return count;
    }

    public bool Restore(string relativeOrAbsolutePath)
    {
        string fullPath = ResolveGamePath(relativeOrAbsolutePath);
        string relative = Path.GetRelativePath(_gameRoot, fullPath);
        string backupPath = Path.GetFullPath(Path.Combine(_backupRoot, relative));
        EnsureWithinRoot(_backupRoot, backupPath, "Backup source");
        if (!File.Exists(backupPath))
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(backupPath, temporaryPath, overwrite: false);
            VerifyEqual(backupPath, temporaryPath);
            File.Move(temporaryPath, fullPath, overwrite: true);
            VerifyEqual(backupPath, fullPath);
            return true;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public int RestoreAll()
    {
        int count = 0;
        foreach (string backupFile in Directory.EnumerateFiles(_backupRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(_backupRoot, backupFile);
            if (Restore(relative))
                count++;
        }
        return count;
    }

    public IReadOnlyList<string> GetBackedUpFiles() => Directory
        .EnumerateFiles(_backupRoot, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(_backupRoot, file))
        .ToList();

    public static string? GetLatestBackupPath(string gameRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        string backupsDirectory = Path.Combine(root, "Backups");
        if (!Directory.Exists(backupsDirectory))
            return null;

        return Directory.EnumerateDirectories(backupsDirectory)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private string ResolveGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A game file path is required.", nameof(path));

        string fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_gameRoot, path));
        EnsureWithinRoot(_gameRoot, fullPath, "Game path");
        return fullPath;
    }

    private static void VerifyEqual(string source, string copy)
    {
        if (!File.Exists(copy) || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(ComputeSha256(source)),
                Convert.FromHexString(ComputeSha256(copy))))
        {
            throw new IOException($"SHA-256 verification failed for backup '{copy}'.");
        }
    }

    private static bool IsWithinRoot(string root, string candidate)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedCandidate = Path.GetFullPath(candidate);
        return string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureWithinRoot(string root, string candidate, string label)
    {
        if (!IsWithinRoot(root, candidate))
            throw new UnauthorizedAccessException($"{label} is outside the configured root: '{candidate}'.");
    }
}
