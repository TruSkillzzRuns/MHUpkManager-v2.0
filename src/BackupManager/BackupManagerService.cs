namespace MHUpkManager.BackupManager;

internal sealed class BackupManagerService
{
    public List<BackupEntry> ScanBackupFiles(string rootFolder, bool recursive)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            throw new DirectoryNotFoundException("Select a valid folder first.");

        SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(rootFolder, "*.bak", searchOption)
            .Select(CreateEntry)
            .OrderByDescending(static entry => entry.LastWriteTimeUtc)
            .ThenBy(static entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void RestoreBackup(BackupEntry entry)
    {
        if (entry == null)
            throw new InvalidOperationException("Select a backup entry first.");

        if (!File.Exists(entry.BackupPath))
            throw new FileNotFoundException("The backup file no longer exists.", entry.BackupPath);

        string directory = Path.GetDirectoryName(entry.OriginalPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.Copy(entry.BackupPath, entry.OriginalPath, overwrite: true);
    }

    private static BackupEntry CreateEntry(string backupPath)
    {
        FileInfo backupInfo = new(backupPath);
        string originalPath = backupPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
            ? backupPath[..^4]
            : backupPath;
        FileInfo originalInfo = new(originalPath);

        return new BackupEntry
        {
            BackupPath = backupPath,
            OriginalPath = originalPath,
            LastWriteTimeUtc = backupInfo.LastWriteTimeUtc,
            BackupSizeBytes = backupInfo.Exists ? backupInfo.Length : 0,
            OriginalExists = originalInfo.Exists,
            OriginalSizeBytes = originalInfo.Exists ? originalInfo.Length : null
        };
    }
}
