using MHUpkManager.MeshPreview;

namespace MHUpkManager.TexturePreview;

internal sealed class MaterialPreviewBinder
{
    private readonly MeshPreviewUI _meshPreviewUi;
    private readonly TexturePreviewLogger _logger;
    private readonly TextureToMaterialConverter _converter;
    private readonly TexturePreviewMaterialSet _materialSet = new();

    public MaterialPreviewBinder(MeshPreviewUI meshPreviewUi, TexturePreviewLogger logger, TextureToMaterialConverter converter)
    {
        _meshPreviewUi = meshPreviewUi;
        _logger = logger;
        _converter = converter;
    }

    public TexturePreviewMaterialSet MaterialSet => _materialSet;

    public void ApplyTexture(TexturePreviewTexture texture, bool applyToMeshPreview)
    {
        _converter.ApplyToMaterial(_materialSet, texture);
        _logger.Log($"Assigned {texture.Name} to {texture.Slot}.");
        if (applyToMeshPreview)
            PushToMeshPreview();
    }

    public void ApplyTextures(IEnumerable<TexturePreviewTexture> textures, bool applyToMeshPreview)
    {
        _materialSet.Clear();
        foreach (TexturePreviewTexture texture in textures.Where(static texture => texture != null))
        {
            _converter.ApplyToMaterial(_materialSet, texture);
            _logger.Log($"Assigned {texture.Name} to {texture.Slot}.");
        }

        if (applyToMeshPreview)
            PushToMeshPreview();
    }

    public void SetEnabled(bool enabled)
    {
        _materialSet.Enabled = enabled;
        PushToMeshPreview();
    }

    public void ResetMaterial()
    {
        _materialSet.Clear();
        _materialSet.Enabled = false;
        _meshPreviewUi.ResetPreviewMaterial();
        _logger.Log("Material preview reset.");
    }

    public void PushToMeshPreview()
    {
        if (_meshPreviewUi == null)
            return;

        if (!_materialSet.Enabled)
        {
            _meshPreviewUi.SetMaterialPreviewEnabled(false);
            _meshPreviewUi.RefreshPreview();
            return;
        }

        foreach ((TexturePreviewMaterialSlot slot, TexturePreviewTexture texture) in _materialSet.Textures)
            _meshPreviewUi.SetPreviewMaterialTexture(slot, texture);

        _meshPreviewUi.SetMaterialPreviewEnabled(true);
        _meshPreviewUi.RefreshPreview();

        if (_meshPreviewUi.CurrentBackend != MeshPreviewBackend.OpenTK)
            _logger.Log("Mesh material preview is currently implemented on the OpenTK mesh renderer path.");
    }
}
