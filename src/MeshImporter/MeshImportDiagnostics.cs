using System.Text;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;

namespace MHUpkManager.MeshImporter;

internal static class MeshImportDiagnostics
{
    public static string WriteException(Exception ex)
    {
        string path = Path.Combine(GetDirectory(), $"fbx-import-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(path, ex.ToString());
        return path;
    }

    public static string WriteImportSummary(MeshImportContext context, NeutralMesh neutralMesh, UE3LodModel rebuiltLod)
    {
        string path = Path.Combine(GetDirectory(), $"fbx-import-summary-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(path, BuildImportSummary(context, neutralMesh, rebuiltLod.Inner));
        return path;
    }

    private static string GetDirectory()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "MHUpkManager_ImportLogs");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string BuildImportSummary(MeshImportContext context, NeutralMesh neutralMesh, FStaticLODModel rebuiltLod)
    {
        StringBuilder sb = new();
        FStaticLODModel originalLod = context.OriginalLod;
        FSkeletalMeshVertexBuffer originalBuffer = originalLod.VertexBufferGPUSkin;
        FSkeletalMeshVertexBuffer rebuiltBuffer = rebuiltLod.VertexBufferGPUSkin;

        sb.AppendLine("FBX Import Summary");
        sb.AppendLine();
        sb.AppendLine($"Export: {context.ExportEntry.GetPathName()}");
        sb.AppendLine($"LOD Index: {context.LodIndex}");
        sb.AppendLine($"Original LOD Byte Length: {context.LodDataSize}");
        sb.AppendLine();
        AppendNeutralMeshSummary(sb, neutralMesh);
        sb.AppendLine();
        AppendLayoutSummary(sb, neutralMesh);
        sb.AppendLine();
        AppendSplitSummary(sb, neutralMesh);
        sb.AppendLine();
        sb.AppendLine($"Original NumTexCoords: {originalLod.NumTexCoords}");
        sb.AppendLine($"Rebuilt NumTexCoords: {rebuiltLod.NumTexCoords}");
        sb.AppendLine($"Original NumVertices: {originalLod.NumVertices}");
        sb.AppendLine($"Rebuilt NumVertices: {rebuiltLod.NumVertices}");
        sb.AppendLine($"Original Sections: {originalLod.Sections.Count}");
        sb.AppendLine($"Rebuilt Sections: {rebuiltLod.Sections.Count}");
        sb.AppendLine($"Original Chunks: {originalLod.Chunks.Count}");
        sb.AppendLine($"Rebuilt Chunks: {rebuiltLod.Chunks.Count}");
        sb.AppendLine();
        AppendOriginalSectionSummary(sb, originalLod);
        sb.AppendLine();
        AppendChunkBoneMapSummary(sb, originalLod, rebuiltLod);
        sb.AppendLine();
        sb.AppendLine($"Original VB NumTexCoords: {originalBuffer.NumTexCoords}");
        sb.AppendLine($"Rebuilt VB NumTexCoords: {rebuiltBuffer.NumTexCoords}");
        sb.AppendLine($"Original VB UseFullPrecisionUVs: {originalBuffer.bUseFullPrecisionUVs}");
        sb.AppendLine($"Rebuilt VB UseFullPrecisionUVs: {rebuiltBuffer.bUseFullPrecisionUVs}");
        sb.AppendLine($"Original VB UsePackedPosition: {originalBuffer.bUsePackedPosition}");
        sb.AppendLine($"Rebuilt VB UsePackedPosition: {rebuiltBuffer.bUsePackedPosition}");
        sb.AppendLine($"Original VB ElementSize: {GetGpuVertexElementSize(originalBuffer)}");
        sb.AppendLine($"Rebuilt VB ElementSize: {GetGpuVertexElementSize(rebuiltBuffer)}");
        sb.AppendLine($"Original VB VertexCount: {GetGpuVertexCount(originalBuffer)}");
        sb.AppendLine($"Rebuilt VB VertexCount: {GetGpuVertexCount(rebuiltBuffer)}");
        sb.AppendLine($"Original MeshOrigin: {originalBuffer.MeshOrigin.Format}");
        sb.AppendLine($"Rebuilt MeshOrigin: {rebuiltBuffer.MeshOrigin.Format}");
        sb.AppendLine($"Original MeshExtension: {originalBuffer.MeshExtension.Format}");
        sb.AppendLine($"Rebuilt MeshExtension: {rebuiltBuffer.MeshExtension.Format}");
        sb.AppendLine();
        sb.AppendLine($"Original RawPointIndices Length: {originalLod.RawPointIndices?.Length ?? -1}");
        sb.AppendLine($"Rebuilt RawPointIndices Length: {rebuiltLod.RawPointIndices?.Length ?? -1}");
        sb.AppendLine($"Original RequiredBones Length: {originalLod.RequiredBones?.Length ?? -1}");
        sb.AppendLine($"Rebuilt RequiredBones Length: {rebuiltLod.RequiredBones?.Length ?? -1}");

        return sb.ToString();
    }

    private static void AppendNeutralMeshSummary(StringBuilder sb, NeutralMesh neutralMesh)
    {
        int importedVertexCount = neutralMesh.Sections.Sum(static s => s.ImportedVertexCount);
        int exactWeldedVertexCount = neutralMesh.Sections.Sum(static s => s.ImportedExactWeldVertexCount);
        int mergedVertexCount = neutralMesh.Sections.Sum(static s => s.ImportedMergedVertexCount);
        int importedTriangleCount = neutralMesh.Sections.Sum(static s => s.ImportedTriangleCount);
        int importedUniquePositionCount = neutralMesh.Sections.Sum(static s => s.ImportedUniquePositionCount);
        int importedUniqueFullVertexCount = neutralMesh.Sections.Sum(static s => s.ImportedUniqueFullVertexCount);
        int importedUniqueVerticesIgnoringSurfaceVectors = neutralMesh.Sections.Sum(static s => s.ImportedUniqueVerticesIgnoringSurfaceVectors);
        int importedMergeableVerticesIgnoringSurfaceVectors = neutralMesh.Sections.Sum(static s => s.ImportedMergeableVerticesIgnoringSurfaceVectors);
        int splitPositionGroupCount = neutralMesh.Sections.Sum(static s => s.ImportedSplitPositionGroupCount);
        int splitVerticesFromPositionGroups = neutralMesh.Sections.Sum(static s => s.ImportedSplitVerticesFromPositionGroups);
        int positionGroupsWithUvSplits = neutralMesh.Sections.Sum(static s => s.ImportedPositionGroupsWithUvSplits);
        int splitVerticesFromUvSplits = neutralMesh.Sections.Sum(static s => s.ImportedSplitVerticesFromUvSplits);
        int positionGroupsWithNormalSplits = neutralMesh.Sections.Sum(static s => s.ImportedPositionGroupsWithNormalSplits);
        int splitVerticesFromNormalSplits = neutralMesh.Sections.Sum(static s => s.ImportedSplitVerticesFromNormalSplits);
        int positionGroupsWithWeightSplits = neutralMesh.Sections.Sum(static s => s.ImportedPositionGroupsWithWeightSplits);
        int splitVerticesFromWeightSplits = neutralMesh.Sections.Sum(static s => s.ImportedSplitVerticesFromWeightSplits);
        int positionGroupsWithUvOnlySplits = neutralMesh.Sections.Sum(static s => s.ImportedPositionGroupsWithUvOnlySplits);
        int splitVerticesFromUvOnlySplits = neutralMesh.Sections.Sum(static s => s.ImportedSplitVerticesFromUvOnlySplits);
        int positionGroupsWithNormalOnlySplits = neutralMesh.Sections.Sum(static s => s.ImportedPositionGroupsWithNormalOnlySplits);
        int splitVerticesFromNormalOnlySplits = neutralMesh.Sections.Sum(static s => s.ImportedSplitVerticesFromNormalOnlySplits);
        int positionGroupsWithUvAndNormalSplits = neutralMesh.Sections.Sum(static s => s.ImportedPositionGroupsWithUvAndNormalSplits);
        int splitVerticesFromUvAndNormalSplits = neutralMesh.Sections.Sum(static s => s.ImportedSplitVerticesFromUvAndNormalSplits);
        int maxVerticesPerPosition = neutralMesh.Sections.Count == 0 ? 0 : neutralMesh.Sections.Max(static s => s.ImportedMaxVerticesPerPosition);

        sb.AppendLine("Imported FBX Mesh");
        sb.AppendLine($"Imported Sections: {neutralMesh.Sections.Count}");
        sb.AppendLine($"Imported TriangleCount: {importedTriangleCount}");
        sb.AppendLine($"Imported VertexCount Before Exact Weld: {importedVertexCount}");
        sb.AppendLine($"Imported VertexCount After Exact Weld: {exactWeldedVertexCount}");
        sb.AppendLine($"Imported VertexCount After Surface Merge: {mergedVertexCount}");
        sb.AppendLine($"Imported Exact Duplicate Vertices Removed: {importedVertexCount - exactWeldedVertexCount}");
        sb.AppendLine($"Imported Surface-Vector Merge Vertices Removed: {exactWeldedVertexCount - mergedVertexCount}");
        sb.AppendLine($"Imported Unique Positions: {importedUniquePositionCount}");
        sb.AppendLine($"Imported Unique Full Vertices: {importedUniqueFullVertexCount}");
        sb.AppendLine($"Imported Unique Vertices Ignoring Normals/Tangents: {importedUniqueVerticesIgnoringSurfaceVectors}");
        sb.AppendLine($"Imported Mergeable Vertices Ignoring Normals/Tangents: {importedMergeableVerticesIgnoringSurfaceVectors}");
        sb.AppendLine($"Position Groups With Splits: {splitPositionGroupCount}");
        sb.AppendLine($"Split Vertices From Position Groups: {splitVerticesFromPositionGroups}");
        sb.AppendLine($"Position Groups With UV Splits: {positionGroupsWithUvSplits}");
        sb.AppendLine($"Split Vertices From UV Splits: {splitVerticesFromUvSplits}");
        sb.AppendLine($"Position Groups With Normal/Tangent Splits: {positionGroupsWithNormalSplits}");
        sb.AppendLine($"Split Vertices From Normal/Tangent Splits: {splitVerticesFromNormalSplits}");
        sb.AppendLine($"Position Groups With Weight Splits: {positionGroupsWithWeightSplits}");
        sb.AppendLine($"Split Vertices From Weight Splits: {splitVerticesFromWeightSplits}");
        sb.AppendLine($"Position Groups With UV-Only Splits: {positionGroupsWithUvOnlySplits}");
        sb.AppendLine($"Split Vertices From UV-Only Splits: {splitVerticesFromUvOnlySplits}");
        sb.AppendLine($"Position Groups With Normal-Only Splits: {positionGroupsWithNormalOnlySplits}");
        sb.AppendLine($"Split Vertices From Normal-Only Splits: {splitVerticesFromNormalOnlySplits}");
        sb.AppendLine($"Position Groups With UV+Normal Splits: {positionGroupsWithUvAndNormalSplits}");
        sb.AppendLine($"Split Vertices From UV+Normal Splits: {splitVerticesFromUvAndNormalSplits}");
        sb.AppendLine($"Max Vertices Per Position: {maxVerticesPerPosition}");
        sb.AppendLine($"Imported Max UV Sets: {neutralMesh.MaxUvSets}");

        for (int i = 0; i < neutralMesh.Sections.Count; i++)
        {
            NeutralSection section = neutralMesh.Sections[i];
            sb.AppendLine();
            sb.AppendLine($"Section {i}: {section.Name}");
            sb.AppendLine($"Material: {section.MaterialName}");
            sb.AppendLine($"ImportedMaterialIndex: {section.ImportedMaterialIndex}");
            sb.AppendLine($"TriangleCount: {section.ImportedTriangleCount}");
            sb.AppendLine($"VertexCount Before Exact Weld: {section.ImportedVertexCount}");
            sb.AppendLine($"VertexCount After Exact Weld: {section.ImportedExactWeldVertexCount}");
            sb.AppendLine($"VertexCount After Surface Merge: {section.ImportedMergedVertexCount}");
            sb.AppendLine($"Exact Duplicate Vertices Removed: {section.ImportedVertexCount - section.ImportedExactWeldVertexCount}");
            sb.AppendLine($"Surface-Vector Merge Vertices Removed: {section.ImportedExactWeldVertexCount - section.ImportedMergedVertexCount}");
            sb.AppendLine($"Unique Positions: {section.ImportedUniquePositionCount}");
            sb.AppendLine($"Unique Full Vertices: {section.ImportedUniqueFullVertexCount}");
            sb.AppendLine($"Unique Vertices Ignoring Normals/Tangents: {section.ImportedUniqueVerticesIgnoringSurfaceVectors}");
            sb.AppendLine($"Mergeable Vertices Ignoring Normals/Tangents: {section.ImportedMergeableVerticesIgnoringSurfaceVectors}");
            sb.AppendLine($"Position Groups With Splits: {section.ImportedSplitPositionGroupCount}");
            sb.AppendLine($"Split Vertices From Position Groups: {section.ImportedSplitVerticesFromPositionGroups}");
            sb.AppendLine($"Position Groups With UV Splits: {section.ImportedPositionGroupsWithUvSplits}");
            sb.AppendLine($"Split Vertices From UV Splits: {section.ImportedSplitVerticesFromUvSplits}");
            sb.AppendLine($"Position Groups With Normal/Tangent Splits: {section.ImportedPositionGroupsWithNormalSplits}");
            sb.AppendLine($"Split Vertices From Normal/Tangent Splits: {section.ImportedSplitVerticesFromNormalSplits}");
            sb.AppendLine($"Position Groups With Weight Splits: {section.ImportedPositionGroupsWithWeightSplits}");
            sb.AppendLine($"Split Vertices From Weight Splits: {section.ImportedSplitVerticesFromWeightSplits}");
            sb.AppendLine($"Position Groups With UV-Only Splits: {section.ImportedPositionGroupsWithUvOnlySplits}");
            sb.AppendLine($"Split Vertices From UV-Only Splits: {section.ImportedSplitVerticesFromUvOnlySplits}");
            sb.AppendLine($"Position Groups With Normal-Only Splits: {section.ImportedPositionGroupsWithNormalOnlySplits}");
            sb.AppendLine($"Split Vertices From Normal-Only Splits: {section.ImportedSplitVerticesFromNormalOnlySplits}");
            sb.AppendLine($"Position Groups With UV+Normal Splits: {section.ImportedPositionGroupsWithUvAndNormalSplits}");
            sb.AppendLine($"Split Vertices From UV+Normal Splits: {section.ImportedSplitVerticesFromUvAndNormalSplits}");
            sb.AppendLine($"Max Vertices Per Position: {section.ImportedMaxVerticesPerPosition}");
            sb.AppendLine($"Max UV Sets: {GetSectionMaxUvSets(section)}");
        }
    }

    private static void AppendSplitSummary(StringBuilder sb, NeutralMesh neutralMesh)
    {
        sb.AppendLine("Section Reconstruction");
        sb.AppendLine($"Used Single Section Split: {neutralMesh.UsedSingleSectionSplit}");
        if (!neutralMesh.UsedSingleSectionSplit)
            return;

        sb.AppendLine($"Split Strategy: {neutralMesh.SplitStrategy}");
        sb.AppendLine($"Split Source Vertex Count: {neutralMesh.SplitSourceVertexCount}");
        sb.AppendLine($"Split Output Vertex Count: {neutralMesh.SplitOutputVertexCount}");
        sb.AppendLine($"Split Duplicated Vertex Count: {neutralMesh.SplitDuplicatedVertexCount}");
        sb.AppendLine($"Split Shared Source Vertex Count: {neutralMesh.SplitSharedSourceVertexCount}");
    }

    private static void AppendLayoutSummary(StringBuilder sb, NeutralMesh neutralMesh)
    {
        sb.AppendLine("Layout Selection");
        sb.AppendLine($"Layout Strategy: {neutralMesh.LayoutStrategy}");

        for (int i = 0; i < neutralMesh.LayoutSections.Count; i++)
        {
            LayoutSectionDiagnostic section = neutralMesh.LayoutSections[i];
            sb.AppendLine($"Layout Section {i}: OriginalSection={section.OriginalSectionIndex}, PreserveOriginal={section.PreserveOriginal}, ImportedVertices={section.ImportedVertexCount}, ImportedTriangles={section.ImportedTriangleCount}");
        }
    }

    private static void AppendOriginalSectionSummary(StringBuilder sb, FStaticLODModel originalLod)
    {
        sb.AppendLine("Original Section Layout");
        for (int i = 0; i < originalLod.Sections.Count; i++)
        {
            FSkelMeshSection section = originalLod.Sections[i];
            sb.AppendLine(
                $"Original Section {i}: MaterialIndex={section.MaterialIndex}, ChunkIndex={section.ChunkIndex}, " +
                $"Triangles={section.NumTriangles}, BaseIndex={section.BaseIndex}");
        }
    }

    private static void AppendChunkBoneMapSummary(StringBuilder sb, FStaticLODModel originalLod, FStaticLODModel rebuiltLod)
    {
        sb.AppendLine("Chunk Bone Maps");
        int chunkCount = Math.Max(originalLod.Chunks.Count, rebuiltLod.Chunks.Count);
        for (int i = 0; i < chunkCount; i++)
        {
            string originalBones = i < originalLod.Chunks.Count
                ? string.Join(",", originalLod.Chunks[i].BoneMap.Select(static bone => bone.ToString()))
                : "<missing>";
            string rebuiltBones = i < rebuiltLod.Chunks.Count
                ? string.Join(",", rebuiltLod.Chunks[i].BoneMap.Select(static bone => bone.ToString()))
                : "<missing>";

            sb.AppendLine($"Chunk {i}: Original=[{originalBones}] Rebuilt=[{rebuiltBones}]");
        }
    }

    private static int GetSectionMaxUvSets(NeutralSection section)
    {
        return section.Vertices.Count == 0 ? 1 : Math.Max(1, section.Vertices.Max(static v => v.UVs.Count));
    }

    private static int GetGpuVertexCount(FSkeletalMeshVertexBuffer buffer)
    {
        if (buffer.VertsF16 != null)
            return buffer.VertsF16.Count;
        if (buffer.VertsF16UV32 != null)
            return buffer.VertsF16UV32.Count;
        if (buffer.VertsF32 != null)
            return buffer.VertsF32.Count;
        if (buffer.VertsF32UV32 != null)
            return buffer.VertsF32UV32.Count;

        return 0;
    }

    private static int GetGpuVertexElementSize(FSkeletalMeshVertexBuffer buffer)
    {
        int texCoords = checked((int)buffer.NumTexCoords);
        if (buffer.bUseFullPrecisionUVs)
            return buffer.bUsePackedPosition ? 16 + 4 + (4 * 2 * texCoords) : 16 + 12 + (4 * 2 * texCoords);

        return buffer.bUsePackedPosition ? 16 + 4 + (2 * 2 * texCoords) : 16 + 12 + (2 * 2 * texCoords);
    }
}
