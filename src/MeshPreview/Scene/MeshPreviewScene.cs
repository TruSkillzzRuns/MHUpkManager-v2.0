using MHUpkManager.TexturePreview;

namespace MHUpkManager.MeshPreview;

internal enum MeshPreviewDisplayMode
{
    Overlay,
    SideBySide,
    FbxOnly,
    Ue3Only
}

internal enum MeshPreviewWeightViewMode
{
    SelectedBoneHeatmap,
    MaxInfluenceHeatmap
}

internal enum MeshPreviewShadingMode
{
    Lit,
    Studio,
    Clay,
    MatCap,
    GameApprox
}

internal enum MeshPreviewBackgroundStyle
{
    DarkGradient,
    StudioGray,
    FlatBlack,
    Checker
}

internal enum MeshPreviewLightingPreset
{
    Neutral,
    Studio,
    Harsh,
    Soft
}

internal enum MeshPreviewMaterialChannel
{
    FullMaterial,
    BaseColor,
    Normal,
    Specular,
    Emissive,
    Mask
}

internal enum MeshPreviewSectionFocusMode
{
    None,
    Highlight,
    Isolate
}

internal enum MeshPreviewSectionFocusMesh
{
    None,
    Fbx,
    Ue3
}

internal sealed class MeshPreviewScene
{
    private readonly HashSet<int> _hiddenFbxSections = [];
    private readonly HashSet<int> _hiddenUe3Sections = [];
    private readonly Dictionary<int, TexturePreviewMaterialSet> _fbxSectionMaterialOverrides = [];
    private readonly Dictionary<int, TexturePreviewMaterialSet> _ue3SectionMaterialOverrides = [];

    public MeshPreviewMesh FbxMesh { get; private set; }
    public MeshPreviewMesh Ue3Mesh { get; private set; }
    public TexturePreviewMaterialSet MaterialSet { get; } = new();
    public bool ShowFbxMesh { get; set; } = true;
    public bool ShowUe3Mesh { get; set; } = true;
    public bool Wireframe { get; set; }
    public bool ShowBones { get; set; }
    public bool ShowWeights { get; set; }
    public bool ShowSections { get; set; }
    public bool ShowNormals { get; set; }
    public bool ShowTangents { get; set; }
    public bool ShowUvSeams { get; set; }
    public bool MaterialPreviewEnabled { get; set; }
    public bool DisableBackfaceCullingForFbx { get; set; }
    public bool DisableBackfaceCullingForUe3 { get; set; }
    public MeshPreviewDisplayMode DisplayMode { get; set; } = MeshPreviewDisplayMode.Overlay;
    public MeshPreviewWeightViewMode WeightViewMode { get; set; } = MeshPreviewWeightViewMode.SelectedBoneHeatmap;
    public MeshPreviewShadingMode ShadingMode { get; set; } = MeshPreviewShadingMode.Lit;
    public MeshPreviewBackgroundStyle BackgroundStyle { get; set; } = MeshPreviewBackgroundStyle.DarkGradient;
    public MeshPreviewLightingPreset LightingPreset { get; set; } = MeshPreviewLightingPreset.Neutral;
    public MeshPreviewMaterialChannel MaterialChannel { get; set; } = MeshPreviewMaterialChannel.FullMaterial;
    public MeshPreviewSectionFocusMode SectionFocusMode { get; set; } = MeshPreviewSectionFocusMode.None;
    public MeshPreviewSectionFocusMesh SectionFocusMesh { get; set; } = MeshPreviewSectionFocusMesh.None;
    public int FocusedSectionIndex { get; set; } = -1;
    public bool ShowGroundPlane { get; set; } = true;
    public float AmbientLight { get; set; } = 0.3f;
    public string SelectedBoneName { get; set; } = string.Empty;

    public IEnumerable<string> BoneNames =>
        (FbxMesh?.Bones ?? []).Concat(Ue3Mesh?.Bones ?? [])
            .Select(static bone => bone.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name);

    public void SetFbxMesh(MeshPreviewMesh mesh)
    {
        FbxMesh?.Dispose();
        FbxMesh = mesh;
        ShowFbxMesh = mesh != null;
        _hiddenFbxSections.Clear();
    }

    public void SetUe3Mesh(MeshPreviewMesh mesh)
    {
        Ue3Mesh?.Dispose();
        Ue3Mesh = mesh;
        ShowUe3Mesh = mesh != null;
        _hiddenUe3Sections.Clear();
    }

    public void SetHiddenSections(bool ue3Mesh, IEnumerable<int> sectionIndices)
    {
        HashSet<int> target = ue3Mesh ? _hiddenUe3Sections : _hiddenFbxSections;
        target.Clear();
        foreach (int sectionIndex in sectionIndices ?? [])
            target.Add(sectionIndex);
    }

    public bool IsSectionVisible(bool ue3Mesh, int sectionIndex)
    {
        HashSet<int> target = ue3Mesh ? _hiddenUe3Sections : _hiddenFbxSections;
        if (target.Contains(sectionIndex))
            return false;

        if (SectionFocusMode == MeshPreviewSectionFocusMode.Isolate)
        {
            MeshPreviewSectionFocusMesh mesh = ue3Mesh ? MeshPreviewSectionFocusMesh.Ue3 : MeshPreviewSectionFocusMesh.Fbx;
            if (SectionFocusMesh == mesh)
                return FocusedSectionIndex == sectionIndex;
        }

        return true;
    }

    public bool IsSectionHighlighted(bool ue3Mesh, int sectionIndex)
    {
        if (SectionFocusMode != MeshPreviewSectionFocusMode.Highlight || FocusedSectionIndex < 0)
            return false;

        MeshPreviewSectionFocusMesh mesh = ue3Mesh ? MeshPreviewSectionFocusMesh.Ue3 : MeshPreviewSectionFocusMesh.Fbx;
        return SectionFocusMesh == mesh && FocusedSectionIndex == sectionIndex;
    }

    public void Clear()
    {
        SetFbxMesh(null);
        SetUe3Mesh(null);
        ClearFbxSectionMaterialOverrides();
        ClearUe3SectionMaterialOverrides();
    }

    public void SetFbxSectionMaterialTexture(int sectionIndex, TexturePreviewMaterialSlot slot, TexturePreviewTexture texture)
    {
        if (!_fbxSectionMaterialOverrides.TryGetValue(sectionIndex, out TexturePreviewMaterialSet materialSet))
        {
            materialSet = new TexturePreviewMaterialSet { Enabled = true };
            _fbxSectionMaterialOverrides[sectionIndex] = materialSet;
        }

        materialSet.SetTexture(slot, texture);
        materialSet.Enabled = true;
    }

    public bool TryGetFbxSectionMaterialSet(int sectionIndex, out TexturePreviewMaterialSet materialSet)
    {
        return _fbxSectionMaterialOverrides.TryGetValue(sectionIndex, out materialSet) && materialSet.Enabled;
    }

    public void ClearFbxSectionMaterialOverrides()
    {
        _fbxSectionMaterialOverrides.Clear();
    }

    public void SetUe3SectionMaterialTexture(int sectionIndex, TexturePreviewMaterialSlot slot, TexturePreviewTexture texture)
    {
        if (!_ue3SectionMaterialOverrides.TryGetValue(sectionIndex, out TexturePreviewMaterialSet materialSet))
        {
            materialSet = new TexturePreviewMaterialSet { Enabled = true };
            _ue3SectionMaterialOverrides[sectionIndex] = materialSet;
        }

        materialSet.SetTexture(slot, texture);
        materialSet.Enabled = true;
    }

    public bool TryGetUe3SectionMaterialSet(int sectionIndex, out TexturePreviewMaterialSet materialSet)
    {
        return _ue3SectionMaterialOverrides.TryGetValue(sectionIndex, out materialSet) && materialSet.Enabled;
    }

    public void ClearUe3SectionMaterialOverrides()
    {
        _ue3SectionMaterialOverrides.Clear();
    }
}
