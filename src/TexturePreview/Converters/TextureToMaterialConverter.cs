namespace MHUpkManager.TexturePreview;

internal sealed class TextureToMaterialConverter
{
    public TexturePreviewMaterialSlot ResolveSlot(string sourceName, TexturePreviewMaterialSlot fallbackSlot)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return fallbackSlot;

        string name = sourceName.ToLowerInvariant();
        if (ContainsAny(name, "normal", "_n", "_nm", "nrm", "norm"))
            return TexturePreviewMaterialSlot.Normal;
        if (ContainsAny(name, "spec", "gloss", "rough"))
            return TexturePreviewMaterialSlot.Specular;
        if (ContainsAny(name, "emiss", "glow", "illum"))
            return TexturePreviewMaterialSlot.Emissive;
        if (ContainsAny(name, "mask", "opacity", "orm", "ao"))
            return TexturePreviewMaterialSlot.Mask;
        if (ContainsAny(name, "diff", "albedo", "basecolor", "color"))
            return TexturePreviewMaterialSlot.Diffuse;

        return fallbackSlot;
    }

    public void ApplyToMaterial(TexturePreviewMaterialSet materialSet, TexturePreviewTexture texture)
    {
        materialSet.SetTexture(texture.Slot, texture);
    }

    private static bool ContainsAny(string source, params string[] values)
    {
        foreach (string value in values)
        {
            if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
