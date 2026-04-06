namespace MHUpkManager.BackupManager;

internal sealed class BackupEntry
{
    public string BackupPath { get; init; } = string.Empty;
    public string OriginalPath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(OriginalPath);
    public string BackupFileName => Path.GetFileName(BackupPath);
    public DateTime LastWriteTimeUtc { get; init; }
    public long BackupSizeBytes { get; init; }
    public bool OriginalExists { get; init; }
    public long? OriginalSizeBytes { get; init; }
}
