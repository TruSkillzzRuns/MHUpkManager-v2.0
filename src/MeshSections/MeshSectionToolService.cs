using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Classes;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager.MeshSections;

internal sealed class MeshSectionToolService
{
    private readonly UpkFileRepository _repository = new();

    public async Task<List<string>> GetSkeletalMeshExportsAsync(string upkPath)
    {
        UnrealHeader header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadTablesAsync(null).ConfigureAwait(true);

        return header.ExportTable
            .Where(static export =>
                string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
            .Select(static export => export.GetPathName())
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MeshSectionToolResult> AnalyzeAsync(string upkPath, string skeletalMeshExportPath, int lodIndex)
    {
        UnrealHeader header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(entry => string.Equals(entry.GetPathName(), skeletalMeshExportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{skeletalMeshExportPath}'.");

        if (export.UnrealObject == null)
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException($"Export '{skeletalMeshExportPath}' is not a SkeletalMesh.");

        if (skeletalMesh.LODModels.Count == 0)
            return new MeshSectionToolResult { UpkPath = upkPath, SkeletalMeshExportPath = skeletalMeshExportPath, LodIndex = 0 };

        int resolvedLodIndex = Math.Clamp(lodIndex, 0, skeletalMesh.LODModels.Count - 1);
        FStaticLODModel lod = skeletalMesh.LODModels[resolvedLodIndex];
        uint[] indices = [.. lod.MultiSizeIndexContainer.IndexBuffer];

        List<MeshSectionInfo> sections = [];
        for (int i = 0; i < lod.Sections.Count; i++)
        {
            FSkelMeshSection section = lod.Sections[i];
            FObject materialObject = section.MaterialIndex >= 0 && section.MaterialIndex < skeletalMesh.Materials.Count
                ? skeletalMesh.Materials[section.MaterialIndex]
                : null;

            int triangleCount = checked((int)section.NumTriangles);
            int indexStart = checked((int)section.BaseIndex);
            int indexCount = triangleCount * 3;
            HashSet<uint> uniqueVertexIndices = [];
            for (int indexOffset = 0; indexOffset < indexCount && indexStart + indexOffset < indices.Length; indexOffset++)
                uniqueVertexIndices.Add(indices[indexStart + indexOffset]);

            sections.Add(new MeshSectionInfo
            {
                SectionIndex = i,
                SectionName = $"Section {i}",
                MaterialIndex = section.MaterialIndex,
                MaterialPath = materialObject?.GetPathName() ?? "<missing>",
                TriangleCount = triangleCount,
                EstimatedVertexCount = uniqueVertexIndices.Count,
                ChunkIndex = section.ChunkIndex
            });
        }

        return new MeshSectionToolResult
        {
            UpkPath = upkPath,
            SkeletalMeshExportPath = skeletalMeshExportPath,
            LodIndex = resolvedLodIndex,
            Sections = sections
        };
    }

    public MeshSectionStripPlan BuildStripPlan(MeshSectionToolResult result, IReadOnlyCollection<int> stripSectionIndices)
    {
        HashSet<int> stripSet = stripSectionIndices == null ? [] : [.. stripSectionIndices];
        List<MeshSectionStripPlanEntry> entries = [];
        int removedTriangles = 0;
        int remainingTriangles = 0;

        foreach (MeshSectionInfo section in result?.Sections ?? [])
        {
            bool strip = stripSet.Contains(section.SectionIndex);
            if (strip)
                removedTriangles += section.TriangleCount;
            else
                remainingTriangles += section.TriangleCount;

            entries.Add(new MeshSectionStripPlanEntry
            {
                SectionIndex = section.SectionIndex,
                Action = strip ? "Strip" : "Keep",
                MaterialIndex = section.MaterialIndex,
                TriangleCount = section.TriangleCount,
                MaterialPath = section.MaterialPath
            });
        }

        return new MeshSectionStripPlan
        {
            KeepSectionCount = entries.Count(static entry => entry.Action == "Keep"),
            StripSectionCount = entries.Count(static entry => entry.Action == "Strip"),
            RemovedTriangleCount = removedTriangles,
            RemainingTriangleCount = remainingTriangles,
            Entries = entries
        };
    }
}
