using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;
using MHUpkManager.BackupManager;

namespace MHUpkManager.MeshImporter;

internal sealed class MeshImportProgress
{
    public int Value { get; init; }
    public int Maximum { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal static class MeshPreProcessor
{
    public static async Task<string> ProcessAndReplaceMesh(
        string upkPath,
        string meshName,
        string fbxPath,
        int lodIndex,
        bool replaceAllLods,
        IProgress<MeshImportProgress> progress = null,
        Action<string> log = null)
    {
        string directory = Path.GetDirectoryName(upkPath) ?? Environment.CurrentDirectory;
        string tempOutputPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(upkPath) + "_meshimport_tmp.upk");
        string backupPath = null;

        try
        {
            await ProcessAndInjectMesh(
                upkPath,
                meshName,
                fbxPath,
                lodIndex,
                tempOutputPath,
                replaceAllLods,
                progress,
                log).ConfigureAwait(false);

            backupPath = BackupFileHelper.CreateBackup(upkPath);
            File.Copy(tempOutputPath, upkPath, true);
            log?.Invoke($"Backup written: {backupPath}");
            log?.Invoke($"Replaced original UPK: {upkPath}");
            return backupPath;
        }
        finally
        {
            if (File.Exists(tempOutputPath))
                File.Delete(tempOutputPath);
        }
    }

    public static Task<string> ProcessAndInjectMesh(
        string upkPath,
        string meshName,
        string fbxPath,
        int lodIndex,
        string outputPath)
    {
        return ProcessAndInjectMesh(upkPath, meshName, fbxPath, lodIndex, outputPath, replaceAllLods: false);
    }

    public static async Task<string> ProcessAndInjectMesh(
        string upkPath,
        string meshName,
        string fbxPath,
        int lodIndex,
        string outputPath,
        bool replaceAllLods,
        IProgress<MeshImportProgress> progress = null,
        Action<string> log = null)
    {
        UpkFileRepository repository = new();
        log?.Invoke($"Loading UPK: {upkPath}");
        progress?.Report(new MeshImportProgress { Value = 1, Maximum = 6, Message = "Loading UPK..." });

        var header = await repository.LoadUpkFile(upkPath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);

        UnrealExportTableEntry exportEntry = header.ExportTable
            .FirstOrDefault(export =>
                string.Equals(export.GetPathName(), meshName, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{meshName}'.");

        if (exportEntry.UnrealObject == null)
            await exportEntry.ParseUnrealObject(false, false).ConfigureAwait(false);

        if (exportEntry.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException($"Export '{meshName}' is not a SkeletalMesh.");

        int startLod = Math.Clamp(lodIndex, 0, Math.Max(0, skeletalMesh.LODModels.Count - 1));
        int endLod = replaceAllLods ? skeletalMesh.LODModels.Count - 1 : startLod;

        List<string> tempOutputs = [];
        try
        {
            string currentInput = upkPath;
            string currentOutput = outputPath;
            for (int currentLod = startLod; currentLod <= endLod; currentLod++)
            {
                log?.Invoke($"Processing LOD{currentLod} for {meshName}");
                progress?.Report(new MeshImportProgress
                {
                    Value = 2 + (currentLod - startLod),
                    Maximum = Math.Max(3, 2 + (endLod - startLod) + 1),
                    Message = $"Replacing LOD{currentLod}..."
                });

                await ImportAsync(currentInput, meshName, fbxPath, currentOutput, currentLod, log).ConfigureAwait(false);

                if (currentLod < endLod)
                {
                    tempOutputs.Add(currentOutput);
                    currentInput = currentOutput;
                    currentOutput = Path.Combine(
                        Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory,
                        Path.GetFileNameWithoutExtension(outputPath) + $".lod{currentLod + 1}.tmp" + Path.GetExtension(outputPath));
                }
            }
        }
        finally
        {
            foreach (string tempOutput in tempOutputs)
            {
                if (!string.Equals(tempOutput, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(tempOutput))
                    File.Delete(tempOutput);
            }
        }

        log?.Invoke($"UPK injection complete: {outputPath}");
        progress?.Report(new MeshImportProgress { Value = 6, Maximum = 6, Message = "Complete" });
        return outputPath;
    }

    private static async Task ImportAsync(
        string upkPath,
        string exportPath,
        string fbxPath,
        string outputUpkPath,
        int lodIndex,
        Action<string> log)
    {
        UpkFileRepository repository = new();
        var header = await repository.LoadUpkFile(upkPath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(e => string.Equals(e.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{exportPath}' in '{upkPath}'.");

        MeshImportContext context = await MeshImportContext.CreateAsync(header, export, lodIndex).ConfigureAwait(false);
        FbxMeshImporter fbxImporter = new();
        BoneRemapper boneRemapper = new();
        WeightNormalizer weightNormalizer = new();
        SectionRebuilder sectionRebuilder = new();
        UpkSkeletalMeshInjector injector = new();

        log?.Invoke($"Importing FBX: {fbxPath}");
        NeutralMesh neutralMesh = fbxImporter.Import(fbxPath);
        ValidateImportedSkinWeights(neutralMesh, log);

        log?.Invoke("Remapping FBX bones onto the original UE3 skeleton");
        IReadOnlyList<IReadOnlyList<RemappedWeight>> remapped = boneRemapper.Remap(neutralMesh, context);

        log?.Invoke("Normalizing weights to UE3 4-influence format");
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalized = weightNormalizer.Normalize(remapped);

        log?.Invoke("Rebuilding replacement UE3 LOD");
        UE3LodModel newLod = sectionRebuilder.RebuildSections(neutralMesh, context, normalized);

        string summaryPath = MeshImportDiagnostics.WriteImportSummary(context, neutralMesh, newLod);
        log?.Invoke($"Import summary written: {summaryPath}");

        log?.Invoke("Injecting rebuilt LOD into the UPK");
        await injector.InjectAsync(upkPath, export, context, newLod, outputUpkPath).ConfigureAwait(false);
    }

    private static void ValidateImportedSkinWeights(NeutralMesh mesh, Action<string> log)
    {
        int totalVertices = 0;
        int weightedVertices = 0;
        int totalInfluences = 0;

        foreach (NeutralSection section in mesh.Sections)
        {
            foreach (NeutralVertex vertex in section.Vertices)
            {
                totalVertices++;
                int positiveWeights = vertex.Weights.Count(static weight => weight.Weight > 0.0f && !string.IsNullOrWhiteSpace(weight.BoneName));
                if (positiveWeights > 0)
                {
                    weightedVertices++;
                    totalInfluences += positiveWeights;
                }
            }
        }

        log?.Invoke($"Imported skin weights: weighted vertices {weightedVertices}/{totalVertices}, influences {totalInfluences}.");

        if (totalVertices == 0)
            throw new InvalidOperationException("The imported FBX did not contain any vertices.");

        if (weightedVertices == 0)
        {
            throw new InvalidOperationException(
                "The imported FBX does not contain any usable skin weights. Export the mesh with its armature/skeleton and vertex weights, then try again.");
        }

        if (weightedVertices != totalVertices)
        {
            throw new InvalidOperationException(
                $"The imported FBX only has skin weights on {weightedVertices} of {totalVertices} vertices. Refusing to import a partially unweighted skeletal mesh.");
        }
    }
}
