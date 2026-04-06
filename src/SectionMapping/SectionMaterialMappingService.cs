using MHUpkManager.MeshImporter;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager.SectionMapping;

internal sealed class SectionMaterialMappingService
{
    private readonly UpkFileRepository _repository = new();

    public async Task<List<string>> GetSkeletalMeshExportsAsync(string upkPath)
    {
        var header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadTablesAsync(null).ConfigureAwait(true);

        return header.ExportTable
            .Where(static export =>
                string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
            .Select(static export => export.GetPathName())
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SectionMaterialMappingResult> AnalyzeAsync(
        string upkPath,
        string skeletalMeshExportPath,
        string fbxPath,
        int lodIndex,
        bool allowTopologyChange)
    {
        if (!File.Exists(fbxPath))
            throw new FileNotFoundException("FBX file was not found.", fbxPath);

        var header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(entry => string.Equals(entry.GetPathName(), skeletalMeshExportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{skeletalMeshExportPath}'.");

        MeshImportContext context = await MeshImportContext.CreateAsync(header, export, lodIndex).ConfigureAwait(true);
        FbxMeshImporter fbxImporter = new();
        BoneRemapper boneRemapper = new();
        WeightNormalizer weightNormalizer = new();
        SectionRebuilder sectionRebuilder = new();

        NeutralMesh neutralMesh = fbxImporter.Import(fbxPath);
        neutralMesh.AllowTopologyChange = allowTopologyChange;
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalized = BuildAnalysisWeights(neutralMesh, context, boneRemapper, weightNormalizer);
        UE3LodModel rebuilt = sectionRebuilder.RebuildSections(neutralMesh, context, normalized);

        List<SectionMaterialMappingEntry> entries = [];
        for (int i = 0; i < neutralMesh.LayoutSections.Count; i++)
        {
            LayoutSectionDiagnostic layout = neutralMesh.LayoutSections[i];
            FSkelMeshSection originalSection = context.OriginalLod.Sections[layout.OriginalSectionIndex];

            List<string> importedSectionNames = [];
            List<string> importedMaterialNames = [];
            foreach (int importedIndex in layout.SourceImportedSectionIndices)
            {
                if (importedIndex < 0 || importedIndex >= neutralMesh.Sections.Count)
                    continue;

                NeutralSection importedSection = neutralMesh.Sections[importedIndex];
                importedSectionNames.Add(importedSection.Name);
                importedMaterialNames.Add(importedSection.MaterialName);
            }

            entries.Add(new SectionMaterialMappingEntry
            {
                OriginalSectionIndex = layout.OriginalSectionIndex,
                OriginalTriangleCount = checked((int)originalSection.NumTriangles),
                FinalMaterialIndex = layout.FinalMaterialIndex,
                Behavior = layout.Behavior,
                PreserveOriginal = layout.PreserveOriginal,
                ImportedVertexCount = layout.ImportedVertexCount,
                ImportedTriangleCount = layout.ImportedTriangleCount,
                SourceImportedSectionIndices = [.. layout.SourceImportedSectionIndices],
                SourceImportedSectionNames = importedSectionNames,
                SourceImportedMaterialNames = importedMaterialNames
            });
        }

        return new SectionMaterialMappingResult
        {
            UpkPath = upkPath,
            SkeletalMeshExportPath = skeletalMeshExportPath,
            FbxPath = fbxPath,
            LodIndex = lodIndex,
            LayoutStrategy = neutralMesh.LayoutStrategy,
            UsedSingleSectionSplit = neutralMesh.UsedSingleSectionSplit,
            SplitStrategy = neutralMesh.SplitStrategy,
            ImportedSectionCount = neutralMesh.Sections.Count,
            Entries = entries
        };
    }

    private static IReadOnlyList<IReadOnlyList<NormalizedWeight>> BuildAnalysisWeights(
        NeutralMesh mesh,
        MeshImportContext context,
        BoneRemapper boneRemapper,
        WeightNormalizer weightNormalizer)
    {
        int totalVertices = 0;
        int weightedVertices = 0;

        foreach (NeutralSection section in mesh.Sections)
        {
            foreach (NeutralVertex vertex in section.Vertices)
            {
                totalVertices++;
                if (vertex.Weights.Any(static weight => weight.Weight > 0.0f && !string.IsNullOrWhiteSpace(weight.BoneName)))
                    weightedVertices++;
            }
        }

        if (totalVertices == 0)
            throw new InvalidOperationException("The imported FBX did not contain any vertices.");

        if (weightedVertices == 0)
        {
            int fallbackBoneIndex = context.RequiredBones.Count > 0 ? context.RequiredBones[0] : 0;
            return Enumerable.Range(0, totalVertices)
                .Select(_ => (IReadOnlyList<NormalizedWeight>)
                [
                    new NormalizedWeight(fallbackBoneIndex, 255),
                    new NormalizedWeight(0, 0),
                    new NormalizedWeight(0, 0),
                    new NormalizedWeight(0, 0)
                ])
                .ToList();
        }

        if (weightedVertices != totalVertices)
            throw new InvalidOperationException($"The imported FBX only has usable skin weights on {weightedVertices} of {totalVertices} vertices.");

        IReadOnlyList<IReadOnlyList<RemappedWeight>> remapped = boneRemapper.Remap(mesh, context);
        return weightNormalizer.Normalize(remapped);
    }
}
