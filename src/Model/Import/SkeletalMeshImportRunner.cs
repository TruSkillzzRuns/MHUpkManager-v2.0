using MHUpkManager.BackupManager;

namespace MHUpkManager.Model.Import;

internal static class SkeletalMeshImportRunner
{
    public static async Task<string> ImportAndReplaceAsync(string upkPath, string exportPath, string fbxPath, int lodIndex = 0)
    {
        string directory = Path.GetDirectoryName(upkPath) ?? Environment.CurrentDirectory;
        string tempOutputPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(upkPath) + "_fbximport_tmp.upk");
        string backupPath = null;

        SkeletalMeshImportPipeline pipeline = new();
        await pipeline.ImportAsync(upkPath, exportPath, fbxPath, tempOutputPath, lodIndex).ConfigureAwait(false);

        backupPath = BackupFileHelper.CreateBackup(upkPath);
        File.Copy(tempOutputPath, upkPath, true);
        File.Delete(tempOutputPath);

        return backupPath;
    }
}
