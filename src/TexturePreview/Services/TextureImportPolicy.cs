using MHUpkManager.TextureManager;
using UpkManager.Models.UpkFile.Engine.Texture;

namespace MHUpkManager.TexturePreview;

internal sealed class TextureImportPolicy
{
    private readonly Lazy<HashSet<string>> _standardCaches;

    public TextureImportPolicy()
    {
        _standardCaches = new Lazy<HashSet<string>>(LoadStandardCaches);
    }

    public TextureImportDecision Resolve(UTexture2D texture, TextureEntry entry)
    {
        string currentCache = entry?.Data?.TextureFileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentCache))
            return new TextureImportDecision(ImportType.Replace, currentCache, false, "No cache name on manifest entry; falling back to Replace.");

        bool isStandard = _standardCaches.Value.Contains(currentCache);
        if (!isStandard)
            return new TextureImportDecision(ImportType.Replace, currentCache, false, $"Current cache '{currentCache}' is not in the standard cache list.");

        string suggestedCache = SuggestAlternateCache(texture, currentCache);
        if (string.Equals(suggestedCache, currentCache, StringComparison.OrdinalIgnoreCase))
            return new TextureImportDecision(ImportType.Replace, currentCache, true, $"Current cache '{currentCache}' is standard, but no alternate cache was available.");

        return new TextureImportDecision(ImportType.Add, suggestedCache, true, $"Current cache '{currentCache}' is standard; relocating texture data to '{suggestedCache}.tfc' with Add mode.");
    }

    private string SuggestAlternateCache(UTexture2D texture, string currentCache)
    {
        List<string> candidates = [];

        if (texture != null)
        {
            switch (texture.LODGroup)
            {
                case UTexture.TextureGroup.TEXTUREGROUP_Character:
                case UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap:
                case UTexture.TextureGroup.TEXTUREGROUP_CharacterSpecular:
                    candidates.Add("CharTextures");
                    break;
            }
        }

        candidates.Add("CharTextures");
        candidates.Add("Textures");

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(candidate, currentCache, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_standardCaches.Value.Contains(candidate))
                return candidate;
        }

        return currentCache;
    }

    private static HashSet<string> LoadStandardCaches()
    {
        HashSet<string> caches = new(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "TFCLIst.txt");
        if (!File.Exists(path))
            return caches;

        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                caches.Add(trimmed);
        }

        return caches;
    }
}

internal sealed record TextureImportDecision(
    ImportType ImportType,
    string TextureCacheName,
    bool CurrentCacheIsStandard,
    string Reason);
