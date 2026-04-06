using System.Numerics;
using MHUpkManager.TexturePreview;
using UpkManager.Models.UpkFile.Engine.Material;

namespace MHUpkManager.MeshPreview;

internal enum MeshPreviewGameTextureSlot
{
    Diffuse,
    Normal,
    Smspsk,
    Espa,
    Smrr,
    SpecColor
}

internal sealed class MeshPreviewGameMaterial
{
    private readonly Dictionary<MeshPreviewGameTextureSlot, TexturePreviewTexture> _textures = [];

    public string MaterialPath { get; init; } = string.Empty;
    public bool Enabled { get; set; }
    public int Revision { get; private set; }

    public float LambertDiffusePower { get; set; } = 1.0f;
    public float PhongDiffusePower { get; set; } = 1.0f;
    public float LightingAmbient { get; set; } = 0.3f;
    public float ShadowAmbientMult { get; set; } = 1.0f;
    public float NormalStrength { get; set; } = 1.0f;
    public float ReflectionMult { get; set; } = 1.0f;
    public float RimColorMult { get; set; }
    public float RimFalloff { get; set; } = 2.0f;
    public float ScreenLightAmount { get; set; }
    public float ScreenLightMult { get; set; } = 1.0f;
    public float ScreenLightPower { get; set; } = 1.0f;
    public float SpecMult { get; set; } = 1.0f;
    public float SpecMultLq { get; set; } = 0.5f;
    public float SpecularPower { get; set; } = 15.0f;
    public float SpecularPowerMask { get; set; } = 1.0f;
    public Vector3 LambertAmbient { get; set; } = new(0.1f, 0.1f, 0.1f);
    public Vector3 ShadowAmbientColor { get; set; } = new(0.05f, 0.05f, 0.08f);
    public Vector3 FillLightColor { get; set; } = new(0.2f, 0.19f, 0.18f);
    public Vector3 DiffuseColor { get; set; } = new(0.5f, 0.5f, 0.5f);
    public Vector3 SpecularColor { get; set; } = new(0.502f, 0.502f, 0.502f);
    public Vector3 SubsurfaceInscatteringColor { get; set; } = new(1.0f, 1.0f, 1.0f);
    public Vector3 SubsurfaceAbsorptionColor { get; set; } = new(0.902f, 0.784f, 0.784f);
    public float ImageReflectionNormalDampening { get; set; } = 5.0f;
    public float SkinScatterStrength { get; set; } = 0.5f;
    public float TwoSidedLighting { get; set; }
    public EBlendMode BlendMode { get; set; } = EBlendMode.BLEND_Opaque;
    public bool TwoSided { get; set; }

    public IEnumerable<KeyValuePair<MeshPreviewGameTextureSlot, TexturePreviewTexture>> Textures => _textures;

    public bool HasTexture(MeshPreviewGameTextureSlot slot) => _textures.ContainsKey(slot);

    public TexturePreviewTexture GetTexture(MeshPreviewGameTextureSlot slot)
    {
        return _textures.TryGetValue(slot, out TexturePreviewTexture texture) ? texture : null;
    }

    public void SetTexture(MeshPreviewGameTextureSlot slot, TexturePreviewTexture texture)
    {
        _textures[slot] = texture;
        Revision++;
    }
}
