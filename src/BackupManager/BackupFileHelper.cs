namespace MHUpkManager.BackupManager;

internal static class BackupFileHelper
{
    public static string CreateBackup(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));

        string primaryBackupPath = sourcePath + ".bak";
        string backupPath = primaryBackupPath;

        if (File.Exists(primaryBackupPath))
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            backupPath = $"{sourcePath}.bak.{timestamp}";
            int counter = 1;
            while (File.Exists(backupPath))
            {
                backupPath = $"{sourcePath}.bak.{timestamp}_{counter++}";
            }
        }

        File.Copy(sourcePath, backupPath, overwrite: false);
        return backupPath;
    }

    public static string ResolveOriginalPath(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            return backupPath;

        int markerIndex = backupPath.IndexOf(".bak", StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0 ? backupPath[..markerIndex] : backupPath;
    }
}
