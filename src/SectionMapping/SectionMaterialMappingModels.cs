namespace MHUpkManager.SectionMapping;

internal sealed class SectionMaterialMappingResult
{
    public string UpkPath { get; init; } = string.Empty;
    public string SkeletalMeshExportPath { get; init; } = string.Empty;
    public string FbxPath { get; init; } = string.Empty;
    public int LodIndex { get; init; }
    public string LayoutStrategy { get; init; } = "Unknown";
    public bool UsedSingleSectionSplit { get; init; }
    public string SplitStrategy { get; init; } = "None";
    public int ImportedSectionCount { get; init; }
    public IReadOnlyList<SectionMaterialMappingEntry> Entries { get; init; } = Array.Empty<SectionMaterialMappingEntry>();
}

internal sealed class SectionMaterialMappingEntry
{
    public int OriginalSectionIndex { get; init; }
    public int OriginalTriangleCount { get; init; }
    public int FinalMaterialIndex { get; init; }
    public string Behavior { get; init; } = "Unknown";
    public bool PreserveOriginal { get; init; }
    public int ImportedVertexCount { get; init; }
    public int ImportedTriangleCount { get; init; }
    public IReadOnlyList<int> SourceImportedSectionIndices { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> SourceImportedSectionNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceImportedMaterialNames { get; init; } = Array.Empty<string>();
}
