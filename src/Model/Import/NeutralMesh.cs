using System.Numerics;

namespace MHUpkManager.Model.Import;

internal sealed class NeutralMesh
{
    public List<NeutralSection> Sections { get; } = [];
    public string LayoutStrategy { get; set; } = "Unknown";
    public List<LayoutSectionDiagnostic> LayoutSections { get; } = [];
    public string SplitStrategy { get; set; } = "None";
    public bool UsedSingleSectionSplit { get; set; }
    public int SplitSourceVertexCount { get; set; }
    public int SplitOutputVertexCount { get; set; }
    public int SplitDuplicatedVertexCount { get; set; }
    public int SplitSharedSourceVertexCount { get; set; }
    public int MaxUvSets => Sections.Count == 0 ? 1 : Math.Max(1, Sections.Max(static s => s.Vertices.Count == 0 ? 1 : s.Vertices.Max(v => v.UVs.Count)));
    public int VertexCount => Sections.Sum(static s => s.Vertices.Count);
    public int TriangleCount => Sections.Sum(static s => s.Indices.Count / 3);
}

internal sealed class LayoutSectionDiagnostic
{
    public int OriginalSectionIndex { get; init; }
    public int ImportedVertexCount { get; init; }
    public int ImportedTriangleCount { get; init; }
    public bool PreserveOriginal { get; init; }
}

internal sealed class NeutralSection
{
    public string Name { get; init; } = string.Empty;
    public string MaterialName { get; init; } = string.Empty;
    public int ImportedMaterialIndex { get; init; }
    public int ImportedVertexCount { get; set; }
    public int ImportedExactWeldVertexCount { get; set; }
    public int ImportedMergedVertexCount { get; set; }
    public int ImportedTriangleCount { get; set; }
    public int ImportedUniquePositionCount { get; set; }
    public int ImportedUniqueFullVertexCount { get; set; }
    public int ImportedUniqueVerticesIgnoringSurfaceVectors { get; set; }
    public int ImportedMergeableVerticesIgnoringSurfaceVectors { get; set; }
    public int ImportedSplitPositionGroupCount { get; set; }
    public int ImportedSplitVerticesFromPositionGroups { get; set; }
    public int ImportedPositionGroupsWithUvSplits { get; set; }
    public int ImportedSplitVerticesFromUvSplits { get; set; }
    public int ImportedPositionGroupsWithNormalSplits { get; set; }
    public int ImportedSplitVerticesFromNormalSplits { get; set; }
    public int ImportedPositionGroupsWithWeightSplits { get; set; }
    public int ImportedSplitVerticesFromWeightSplits { get; set; }
    public int ImportedPositionGroupsWithUvOnlySplits { get; set; }
    public int ImportedSplitVerticesFromUvOnlySplits { get; set; }
    public int ImportedPositionGroupsWithNormalOnlySplits { get; set; }
    public int ImportedSplitVerticesFromNormalOnlySplits { get; set; }
    public int ImportedPositionGroupsWithUvAndNormalSplits { get; set; }
    public int ImportedSplitVerticesFromUvAndNormalSplits { get; set; }
    public int ImportedMaxVerticesPerPosition { get; set; }
    public List<NeutralVertex> Vertices { get; } = [];
    public List<int> Indices { get; } = [];
}

internal sealed class NeutralVertex
{
    public Vector3 Position { get; init; }
    public Vector3 Normal { get; init; }
    public Vector3 Tangent { get; init; }
    public Vector3 Bitangent { get; init; }
    public List<Vector2> UVs { get; init; } = [];
    public List<VertexWeight> Weights { get; init; } = [];
}

internal sealed record VertexWeight(string BoneName, float Weight);
internal sealed record RemappedWeight(int BoneIndex, float Weight);
internal sealed record NormalizedWeight(int BoneIndex, byte Weight);
