using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.Modding
{
    /// <summary>
    /// Manages timestamped backups of game files before any read/write operation.
    /// All game-directory file operations must route through BackupManager.
    /// </summary>
    public sealed class BackupManager
    {
        private readonly string _gameRoot;
        private readonly string _backupRoot;
        private readonly HashSet<string> _backedUpFiles = new(StringComparer.OrdinalIgnoreCase);

        public BackupManager(string gameRoot)
        {
            _gameRoot = Path.GetFullPath(gameRoot);
            _backupRoot = Path.Combine(_gameRoot, "Backups", DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            Directory.CreateDirectory(_backupRoot);
        }

        /// <summary>
        /// The root backup directory for this session.
        /// </summary>
        public string BackupRoot => _backupRoot;

        /// <summary>
        /// The game root directory.
        /// </summary>
        public string GameRoot => _gameRoot;

        /// <summary>
        /// Ensure the file at the given path (relative to game root or absolute) is backed up.
        /// Skips if an identical backup already exists.
        /// Returns the backup file path, or null if file doesn't exist.
        /// </summary>
        public string? Backup(string relativeOrAbsolutePath)
        {
            string fullPath = ResolvePath(relativeOrAbsolutePath);

            if (!File.Exists(fullPath))
                return null;

            string relative = GetRelativePath(fullPath);
            string backupPath = Path.Combine(_backupRoot, relative);

            // Skip if we already backed up this exact file this session
            if (_backedUpFiles.Contains(fullPath))
                return backupPath;

            // Skip identical backup (already exists and matches)
            if (File.Exists(backupPath) && FilesAreIdentical(fullPath, backupPath))
                return backupPath;

            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(fullPath, backupPath, overwrite: true);

            lock (_backedUpFiles)
            {
                _backedUpFiles.Add(fullPath);
            }

            return backupPath;
        }

        /// <summary>
        /// Backup all files in a directory recursively.
        /// </summary>
        public int BackupDirectory(string relativeOrAbsolutePath)
        {
            string fullPath = ResolvePath(relativeOrAbsolutePath);
            if (!Directory.Exists(fullPath))
                return 0;

            int count = 0;
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                if (Backup(file) != null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Restore a single file from backup.
        /// </summary>
        public bool Restore(string relativeOrAbsolutePath)
        {
            string fullPath = ResolvePath(relativeOrAbsolutePath);
            string relative = GetRelativePath(fullPath);
            string backupPath = Path.Combine(_backupRoot, relative);

            if (!File.Exists(backupPath))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.Copy(backupPath, fullPath, overwrite: true);
            return true;
        }

        /// <summary>
        /// Restore all files from the most recent backup snapshot.
        /// </summary>
        public int RestoreAll()
        {
            int count = 0;
            foreach (var backupFile in Directory.EnumerateFiles(_backupRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(_backupRoot, backupFile);
                string originalPath = Path.Combine(_gameRoot, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
                File.Copy(backupFile, originalPath, overwrite: true);
                count++;
            }
            return count;
        }

        /// <summary>
        /// List all backed-up files.
        /// </summary>
        public IReadOnlyList<string> GetBackedUpFiles()
        {
            if (!Directory.Exists(_backupRoot))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(_backupRoot, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_backupRoot, f))
                .ToList();
        }

        /// <summary>
        /// Get the latest backup timestamp directory name.
        /// </summary>
        public static string? GetLatestBackupPath(string gameRoot)
        {
            var backupsDir = Path.Combine(gameRoot, "Backups");
            if (!Directory.Exists(backupsDir))
                return null;

            return Directory.EnumerateDirectories(backupsDir)
                .OrderByDescending(d => d)
                .FirstOrDefault();
        }

        private string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path))
                return Path.GetFullPath(path);
            return Path.GetFullPath(Path.Combine(_gameRoot, path));
        }

        private string GetRelativePath(string fullPath)
        {
            string fullGameRoot = Path.GetFullPath(_gameRoot) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(fullGameRoot, StringComparison.OrdinalIgnoreCase))
                return fullPath[fullGameRoot.Length..];
            return Path.GetFileName(fullPath);
        }

        private static bool FilesAreIdentical(string path1, string path2)
        {
            var info1 = new FileInfo(path1);
            var info2 = new FileInfo(path2);

            if (info1.Length != info2.Length)
                return false;

            // Quick check using last write time
            if (info1.LastWriteTimeUtc == info2.LastWriteTimeUtc)
                return true;

            // Full byte comparison
            using var fs1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var fs2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.Read);

            int bufferSize = 8192;
            byte[] buffer1 = new byte[bufferSize];
            byte[] buffer2 = new byte[bufferSize];

            int bytesRead1, bytesRead2;
            while ((bytesRead1 = fs1.Read(buffer1, 0, bufferSize)) > 0)
            {
                bytesRead2 = fs2.Read(buffer2, 0, bufferSize);
                if (bytesRead1 != bytesRead2)
                    return false;

                for (int i = 0; i < bytesRead1; i++)
                {
                    if (buffer1[i] != buffer2[i])
                        return false;
                }
            }

            return true;
        }
    }
}
