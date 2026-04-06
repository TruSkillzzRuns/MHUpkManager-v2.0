namespace MHUpkManager.MeshSections;

internal sealed class MeshSectionToolResult
{
    public string UpkPath { get; init; } = string.Empty;
    public string SkeletalMeshExportPath { get; init; } = string.Empty;
    public int LodIndex { get; init; }
    public IReadOnlyList<MeshSectionInfo> Sections { get; init; } = Array.Empty<MeshSectionInfo>();
}

internal sealed class MeshSectionInfo
{
    public int SectionIndex { get; init; }
    public string SectionName { get; init; } = string.Empty;
    public int MaterialIndex { get; init; }
    public string MaterialPath { get; init; } = string.Empty;
    public int TriangleCount { get; init; }
    public int EstimatedVertexCount { get; init; }
    public int ChunkIndex { get; init; }
}

internal sealed class MeshSectionStripPlan
{
    public int KeepSectionCount { get; init; }
    public int StripSectionCount { get; init; }
    public int RemovedTriangleCount { get; init; }
    public int RemainingTriangleCount { get; init; }
    public IReadOnlyList<MeshSectionStripPlanEntry> Entries { get; init; } = Array.Empty<MeshSectionStripPlanEntry>();
}

internal sealed class MeshSectionStripPlanEntry
{
    public int SectionIndex { get; init; }
    public string Action { get; init; } = string.Empty;
    public int MaterialIndex { get; init; }
    public int TriangleCount { get; init; }
    public string MaterialPath { get; init; } = string.Empty;
}
