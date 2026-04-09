using System.Numerics;
using MHUpkManager.BackupManager;
using MHUpkManager.MeshImporter;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Types;
using UpkManager.Repository;

namespace MHUpkManager.Retargeting;

internal sealed class MeshReplacer
{
    private const int MaxUe3VertexCount = ushort.MaxValue + 1;

    public async Task<RetargetMesh> BuildReplacementRoundTripMeshAsync(
        string upkPath,
        string skeletalMeshExportPath,
        RetargetMesh retargetedMesh,
        int lodIndex,
        Action<string> log = null)
    {
        (MeshImportContext context, UE3LodModel newLod, _) = await BuildReplacementLodWithExportAsync(
            upkPath,
            skeletalMeshExportPath,
            retargetedMesh,
            lodIndex).ConfigureAwait(false);
        LogBuiltLodDiagnostics(retargetedMesh, context, newLod, log);

        USkeletalMesh syntheticMesh = new()
        {
            RefSkeleton = context.SkeletalMesh.RefSkeleton,
            LODModels = [newLod.Inner]
        };

        return new MhoSkeletalMeshConverter().Convert(
            syntheticMesh,
            $"{skeletalMeshExportPath}#rebuilt",
            0,
            log);
    }

    public async Task<string> ReplaceMeshInUpkAsync(
        string upkPath,
        string skeletalMeshExportPath,
        RetargetMesh retargetedMesh,
        int lodIndex,
        bool replaceAllLods,
        Action<string> log = null)
    {
        if (string.IsNullOrWhiteSpace(upkPath))
            throw new ArgumentException("UPK path is required.", nameof(upkPath));
        if (string.IsNullOrWhiteSpace(skeletalMeshExportPath))
            throw new ArgumentException("SkeletalMesh export path is required.", nameof(skeletalMeshExportPath));
        if (retargetedMesh == null)
            throw new ArgumentNullException(nameof(retargetedMesh));

        (MeshImportContext context, UE3LodModel newLod, UnrealExportTableEntry export) = await BuildReplacementLodWithExportAsync(
            upkPath,
            skeletalMeshExportPath,
            retargetedMesh,
            lodIndex).ConfigureAwait(false);

        string directory = Path.GetDirectoryName(upkPath) ?? Environment.CurrentDirectory;
        string tempOutputPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(upkPath) + "_retarget_tmp.upk");
        string backupPath = null;

        try
        {
            UpkSkeletalMeshInjector injector = new();
            if (!replaceAllLods)
            {
                await injector.InjectAsync(upkPath, export, context, newLod, tempOutputPath).ConfigureAwait(false);
            }
            else
            {
                string currentInput = upkPath;
                string currentOutput = tempOutputPath;
                List<string> tempOutputs = [];
                for (int currentLod = lodIndex; currentLod < context.SkeletalMesh.LODModels.Count; currentLod++)
                {
                    (MeshImportContext currentContext, UE3LodModel currentLodModel, UnrealExportTableEntry currentExport) = await BuildReplacementLodWithExportAsync(
                        currentInput,
                        skeletalMeshExportPath,
                        retargetedMesh,
                        currentLod).ConfigureAwait(false);
                    await injector.InjectAsync(currentInput, currentExport, currentContext, currentLodModel, currentOutput).ConfigureAwait(false);

                    if (currentLod < context.SkeletalMesh.LODModels.Count - 1)
                    {
                        tempOutputs.Add(currentOutput);
                        currentInput = currentOutput;
                        currentOutput = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(tempOutputPath)}.lod{currentLod + 1}.tmp{Path.GetExtension(tempOutputPath)}");
                    }
                }

                foreach (string path in tempOutputs.Where(path => !string.Equals(path, tempOutputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path)))
                    File.Delete(path);
            }

            backupPath = BackupFileHelper.CreateBackup(upkPath);
            File.Copy(tempOutputPath, upkPath, true);
            log?.Invoke($"Backup written: {backupPath}");
            log?.Invoke($"Replaced SkeletalMesh export '{skeletalMeshExportPath}' in {upkPath}.");
            return backupPath;
        }
        finally
        {
            if (File.Exists(tempOutputPath))
                File.Delete(tempOutputPath);
        }
    }

    private static async Task<(MeshImportContext Context, UE3LodModel Lod, UnrealExportTableEntry Export)> BuildReplacementLodWithExportAsync(
        string upkPath,
        string skeletalMeshExportPath,
        RetargetMesh retargetedMesh,
        int lodIndex)
    {
        UpkFileRepository repository = new();
        var header = await repository.LoadUpkFile(upkPath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(entry => string.Equals(entry.GetPathName(), skeletalMeshExportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{skeletalMeshExportPath}'.");

        MeshImportContext context = await MeshImportContext.CreateAsync(header, export, Math.Clamp(lodIndex, 0, int.MaxValue)).ConfigureAwait(false);
        NeutralMesh neutralMesh = ConvertToNeutralMesh(retargetedMesh);
        if (neutralMesh.VertexCount > MaxUe3VertexCount)
        {
            throw new InvalidOperationException(
                $"UE3 skeletal mesh import supports at most {MaxUe3VertexCount} vertices per LOD, but the processed mesh has {neutralMesh.VertexCount}. " +
                "Reduce mesh complexity before replacing the UPK mesh.");
        }

        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights = BuildNormalizedWeights(neutralMesh, context);
        SectionRebuilder sectionRebuilder = new();
        UE3LodModel newLod = sectionRebuilder.RebuildSections(neutralMesh, context, normalizedWeights);
        return (context, newLod, export);
    }

    private static NeutralMesh ConvertToNeutralMesh(RetargetMesh retargetedMesh)
    {
        NeutralMesh neutralMesh = new()
        {
            AllowTopologyChange = true
        };
        foreach (RetargetSection sourceSection in retargetedMesh.Sections)
        {
            NeutralSection section = new()
            {
                Name = sourceSection.Name,
                MaterialName = sourceSection.MaterialName,
                ImportedMaterialIndex = sourceSection.MaterialIndex,
                ImportedVertexCount = sourceSection.Vertices.Count,
                ImportedTriangleCount = sourceSection.Indices.Count / 3
            };

            foreach (RetargetVertex sourceVertex in sourceSection.Vertices)
            {
                section.Vertices.Add(new NeutralVertex
                {
                    Position = ConvertToUe3Position(sourceVertex.Position),
                    Normal = ConvertToUe3Direction(sourceVertex.Normal),
                    Tangent = ConvertToUe3Direction(sourceVertex.Tangent),
                    Bitangent = ConvertToUe3Direction(sourceVertex.Bitangent),
                    UVs = [.. sourceVertex.UVs],
                    Weights = [.. sourceVertex.Weights.Select(static weight => new VertexWeight(weight.BoneName, weight.Weight))]
                });
            }

            section.Indices.AddRange(sourceSection.Indices);
            neutralMesh.Sections.Add(section);
        }

        return neutralMesh;
    }

    private static IReadOnlyList<IReadOnlyList<NormalizedWeight>> BuildNormalizedWeights(NeutralMesh neutralMesh, MeshImportContext context)
    {
        List<IReadOnlyList<NormalizedWeight>> normalized = [];
        foreach (NeutralSection section in neutralMesh.Sections)
        {
            foreach (NeutralVertex vertex in section.Vertices)
            {
                List<RemappedWeight> remapped = [];
                foreach (VertexWeight weight in vertex.Weights)
                {
                    if (weight.Weight <= 0.0f || string.IsNullOrWhiteSpace(weight.BoneName))
                        continue;

                    remapped.Add(new RemappedWeight(context.ResolveBoneIndex(weight.BoneName), weight.Weight));
                }

                normalized.Add(new MHUpkManager.MeshImporter.WeightNormalizer().Normalize([remapped])[0]);
            }
        }

        return normalized;
    }

    private static Vector3 ConvertToUe3Position(Vector3 value) => new(value.X, value.Z, value.Y);

    private static Vector3 ConvertToUe3Direction(Vector3 value) => new(value.X, value.Z, value.Y);

    private static void LogBuiltLodDiagnostics(
        RetargetMesh expectedMesh,
        MeshImportContext context,
        UE3LodModel lodModel,
        Action<string> log)
    {
        if (expectedMesh == null || context == null || lodModel?.Inner == null || log == null)
            return;

        FStaticLODModel lod = lodModel.Inner;
        FSkeletalMeshVertexBuffer buffer = lod.VertexBufferGPUSkin;
        IReadOnlyList<FGPUSkinVertexBase> vertices = [.. buffer.VertexData];
        log(
            $"Replacement build UE3 buffer: packed={buffer.bUsePackedPosition}, fullPrecisionUvs={buffer.bUseFullPrecisionUVs}, " +
            $"numTexCoords={buffer.NumTexCoords}, vertexCount={vertices.Count}, meshOrigin={buffer.MeshOrigin.Format}, meshExtension={buffer.MeshExtension.Format}.");

        for (int chunkIndex = 0; chunkIndex < lod.Chunks.Count; chunkIndex++)
        {
            FSkelMeshChunk chunk = lod.Chunks[chunkIndex];
            if (chunk == null)
                continue;

            string boneMap = string.Join(", ", chunk.BoneMap.Select(static boneIndex => boneIndex).Select(boneIndex =>
            {
                string boneName = boneIndex >= 0 && boneIndex < context.SkeletalMesh.RefSkeleton.Count
                    ? context.SkeletalMesh.RefSkeleton[boneIndex].Name?.Name ?? $"Bone_{boneIndex}"
                    : $"Bone_{boneIndex}";
                return $"{boneName}({boneIndex})";
            }));

            log(
                $"Replacement build chunk {chunkIndex}: baseVertex={chunk.BaseVertexIndex}, rigid={chunk.NumRigidVertices}, soft={chunk.NumSoftVertices}, " +
                $"boneMap=[{boneMap}].");
        }

        RetargetRegion[] sampleRegions =
        [
            RetargetRegion.Head,
            RetargetRegion.Chest,
            RetargetRegion.Pelvis,
            RetargetRegion.LeftHand,
            RetargetRegion.RightHand,
            RetargetRegion.LeftFoot,
            RetargetRegion.RightFoot
        ];

        List<RegionAnchor> anchors = RetargetRegions.BuildAnchors(expectedMesh.Bones);
        foreach (RetargetRegion region in sampleRegions)
        {
            Vector3 anchor = RetargetRegions.GetAnchorCenter(RetargetRegions.GetRegionBoneNames(region), anchors);
            if (!float.IsFinite(anchor.X))
                continue;

            int bestVertexIndex = -1;
            float bestDistance = float.PositiveInfinity;
            Vector3 bestDecoded = Vector3.Zero;
            Vector3 bestRaw = Vector3.Zero;

            for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
            {
                FGPUSkinVertexBase vertex = vertices[vertexIndex];
                Vector3 decoded = buffer.GetVertexPosition(vertex);
                float distance = Vector3.DistanceSquared(decoded, anchor);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestVertexIndex = vertexIndex;
                bestDecoded = decoded;
                bestRaw = vertex.GetVector3();
            }

            if (bestVertexIndex < 0)
                continue;

            FSkelMeshChunk owningChunk = FindChunkForVertexIndex(lod, bestVertexIndex);
            FGPUSkinVertexBase bestVertex = vertices[bestVertexIndex];
            string resolvedWeights = FormatResolvedWeights(bestVertex, owningChunk, context);
            string localBones = string.Join(", ", bestVertex.InfluenceBones.Select(static b => b.ToString()));
            string localWeights = string.Join(", ", bestVertex.InfluenceWeights.Select(static w => w.ToString()));

            log(
                $"Replacement build UE3 region {region}: vertexIndex={bestVertexIndex}, decodedPos=({bestDecoded.X:0.##},{bestDecoded.Y:0.##},{bestDecoded.Z:0.##}), " +
                $"rawPos=({bestRaw.X:0.###},{bestRaw.Y:0.###},{bestRaw.Z:0.###}), localBones=[{localBones}], localWeights=[{localWeights}], resolvedWeights=[{resolvedWeights}].");
        }
    }

    private static FSkelMeshChunk FindChunkForVertexIndex(FStaticLODModel lod, int vertexIndex)
    {
        foreach (FSkelMeshChunk chunk in lod.Chunks)
        {
            if (chunk == null)
                continue;

            int start = checked((int)chunk.BaseVertexIndex);
            int end = start + chunk.NumRigidVertices + chunk.NumSoftVertices;
            if (vertexIndex >= start && vertexIndex < end)
                return chunk;
        }

        return null;
    }

    private static string FormatResolvedWeights(FGPUSkinVertexBase vertex, FSkelMeshChunk chunk, MeshImportContext context)
    {
        if (vertex == null || chunk == null || context == null)
            return "<unresolved>";

        List<string> entries = [];
        for (int i = 0; i < 4; i++)
        {
            byte weight = vertex.InfluenceWeights[i];
            if (weight == 0)
                continue;

            byte localBone = vertex.InfluenceBones[i];
            int globalBone = localBone < chunk.BoneMap.Count ? chunk.BoneMap[localBone] : -1;
            string boneName = globalBone >= 0 && globalBone < context.SkeletalMesh.RefSkeleton.Count
                ? context.SkeletalMesh.RefSkeleton[globalBone].Name?.Name ?? $"Bone_{globalBone}"
                : $"Bone_{globalBone}";
            entries.Add($"{boneName}:{(weight / 255.0f):0.##}");
        }

        return entries.Count == 0 ? "<none>" : string.Join(", ", entries);
    }
}
