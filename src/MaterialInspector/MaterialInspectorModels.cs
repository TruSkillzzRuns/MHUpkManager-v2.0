using System.Numerics;
using UpkManager.Models.UpkFile.Engine.Material;

namespace MHUpkManager.MaterialInspector;

internal sealed class MaterialInspectorResult
{
    public string UpkPath { get; init; } = string.Empty;
    public string SkeletalMeshExportPath { get; init; } = string.Empty;
    public IReadOnlyList<MaterialInspectorSectionInfo> Sections { get; init; } = Array.Empty<MaterialInspectorSectionInfo>();
}

internal sealed class MaterialInspectorSectionInfo
{
    public int SectionIndex { get; init; }
    public int MaterialIndex { get; init; }
    public string MaterialPath { get; init; } = string.Empty;
    public string MaterialType { get; init; } = string.Empty;
    public IReadOnlyList<MaterialInspectorMaterialNode> MaterialChain { get; init; } = Array.Empty<MaterialInspectorMaterialNode>();
}

internal sealed class MaterialInspectorMaterialNode
{
    public string Path { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public EBlendMode? BlendMode { get; init; }
    public bool? TwoSided { get; init; }
    public IReadOnlyList<MaterialInspectorTextureParameter> TextureParameters { get; init; } = Array.Empty<MaterialInspectorTextureParameter>();
    public IReadOnlyList<MaterialInspectorScalarParameter> ScalarParameters { get; init; } = Array.Empty<MaterialInspectorScalarParameter>();
    public IReadOnlyList<MaterialInspectorVectorParameter> VectorParameters { get; init; } = Array.Empty<MaterialInspectorVectorParameter>();
}

internal sealed class MaterialInspectorTextureParameter
{
    public string Name { get; init; } = string.Empty;
    public string TexturePath { get; init; } = string.Empty;
}

internal sealed class MaterialInspectorScalarParameter
{
    public string Name { get; init; } = string.Empty;
    public float Value { get; init; }
}

internal sealed class MaterialInspectorVectorParameter
{
    public string Name { get; init; } = string.Empty;
    public Vector3 Value { get; init; }
}
