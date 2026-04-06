using DDSLib;
using DDSLib.Constants;
using MHUpkManager.TextureManager;
using System.Numerics;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager.TexturePreview;

internal sealed class TexturePreviewInjector
{
    private readonly TextureImportPolicy _importPolicy = new();

    public async Task<TextureInjectionTargetInfo> ResolveTargetInfoAsync(string upkPath, string exportPath)
    {
        if (TextureManifest.Instance == null || TextureManifest.Instance.Entries.Count == 0 || string.IsNullOrWhiteSpace(TextureManifest.Instance.ManifestFilePath))
            throw new InvalidOperationException($"Load {TextureManifest.ManifestName} first before injecting textures.");

        (UTexture2D texture, TextureEntry textureEntry) = await LoadTargetAsync(upkPath, exportPath).ConfigureAwait(true);
        TextureImportDecision importDecision = _importPolicy.Resolve(texture, textureEntry);
        string sourceTfcPath = Path.Combine(TextureManifest.Instance.ManifestPath, textureEntry.Data.TextureFileName + ".tfc");
        string destinationTfcPath = Path.Combine(TextureManifest.Instance.ManifestPath, importDecision.TextureCacheName + ".tfc");

        return new TextureInjectionTargetInfo
        {
            ManifestFilePath = TextureManifest.Instance.ManifestFilePath,
            SourceTextureCachePath = sourceTfcPath,
            DestinationTextureCachePath = destinationTfcPath
        };
    }

    public async Task InjectAsync(string upkPath, string exportPath, TexturePreviewTexture sourceTexture, Action<string> log = null)
    {
        if (sourceTexture == null)
            throw new ArgumentNullException(nameof(sourceTexture));

        if (TextureManifest.Instance == null || TextureManifest.Instance.Entries.Count == 0 || string.IsNullOrWhiteSpace(TextureManifest.Instance.ManifestFilePath))
            throw new InvalidOperationException($"Load {TextureManifest.ManifestName} first before injecting textures.");

        log?.Invoke($"Opening package: {Path.GetFileName(upkPath)}");

        (UTexture2D texture, TextureEntry textureEntry) = await LoadTargetAsync(upkPath, exportPath).ConfigureAwait(true);

        if (sourceTexture.Width != texture.SizeX || sourceTexture.Height != texture.SizeY)
        {
            throw new InvalidOperationException(
                $"Texture dimensions must exactly match the target Texture2D. Target is {texture.SizeX}x{texture.SizeY}, source is {sourceTexture.Width}x{sourceTexture.Height}.");
        }

        FileFormat targetFormat = UTexture2D.ParseFileFormat(texture.Format);
        int targetMipCount = textureEntry.Data.Maps.Count;
        bool targetLooksLikeNormalMap = IsLikelyNormalTarget(texture, exportPath);
        bool normalizeAsNormalMap = targetLooksLikeNormalMap || sourceTexture.Slot == TexturePreviewMaterialSlot.Normal;
        TextureImportDecision importDecision = _importPolicy.Resolve(texture, textureEntry);
        log?.Invoke(
            $"Target texture profile: format={texture.Format}, lodGroup={texture.LODGroup}, size={texture.SizeX}x{texture.SizeY}, mipCount={targetMipCount}, tfc={textureEntry.Data.TextureFileName}, sourceSlot={sourceTexture.Slot}, normalTarget={targetLooksLikeNormalMap}.");
        log?.Invoke($"Import cache policy: mode={importDecision.ImportType}, cache={importDecision.TextureCacheName}, standardCurrent={importDecision.CurrentCacheIsStandard}. {importDecision.Reason}");
        log?.Invoke($"Preparing texture for {exportPath}.");
        DdsFile dds = await Task.Run(() => BuildWritableTexture(sourceTexture, targetFormat, targetMipCount, normalizeAsNormalMap, log)).ConfigureAwait(true);

        if (dds.MipMaps.Count < targetMipCount)
            throw new InvalidOperationException($"Converted texture only produced {dds.MipMaps.Count} mipmaps, but target requires {targetMipCount}.");

        TextureFileCache.Instance.SetEntry(textureEntry, texture);

        string sourceTfcPath = Path.Combine(TextureManifest.Instance.ManifestPath, textureEntry.Data.TextureFileName + ".tfc");
        log?.Invoke($"Loading existing texture cache: {Path.GetFileName(sourceTfcPath)}");
        if (!TextureFileCache.Instance.LoadFromFile(sourceTfcPath, textureEntry))
            throw new InvalidOperationException($"Could not load existing texture cache data from {sourceTfcPath}.");

        EnsureBackupExists(TextureManifest.Instance.ManifestFilePath);
        EnsureBackupExists(sourceTfcPath);
        string destinationTfcPath = Path.Combine(TextureManifest.Instance.ManifestPath, importDecision.TextureCacheName + ".tfc");
        EnsureBackupExists(destinationTfcPath);

        log?.Invoke("Writing converted texture to cache.");
        WriteResult result = await Task.Run(() =>
            TextureFileCache.Instance.WriteTexture(TextureManifest.Instance.ManifestPath, importDecision.TextureCacheName, importDecision.ImportType, dds)).ConfigureAwait(true);
        switch (result)
        {
            case WriteResult.Success:
                log?.Invoke("Saving updated texture manifest.");
                TextureManifest.Instance.SaveManifest();
                log?.Invoke($"Injected DDS into {exportPath}.");
                log?.Invoke($"Updated texture cache: {destinationTfcPath}");
                log?.Invoke($"Saved manifest: {TextureManifest.Instance.ManifestFilePath}");
                return;
            case WriteResult.MipMapError:
                throw new InvalidOperationException("Texture injection failed while rebuilding mip data for the target texture cache.");
            case WriteResult.SizeReplaceError:
                throw new InvalidOperationException("Injected DDS payload is larger than the existing texture cache allocation. Resize/compress the DDS to fit or extend the writer to support relocation.");
            default:
                throw new InvalidOperationException($"Texture injection failed with result '{result}'.");
        }
    }

    private static DdsFile BuildWritableTexture(
        TexturePreviewTexture sourceTexture,
        FileFormat targetFormat,
        int targetMipCount,
        bool normalizeAsNormalMap,
        Action<string> log)
    {
        if (sourceTexture.Width <= 0 || sourceTexture.Height <= 0)
            throw new InvalidOperationException("Source texture has invalid dimensions.");

        if (sourceTexture.RgbaPixels == null || sourceTexture.RgbaPixels.Length != sourceTexture.Width * sourceTexture.Height * 4)
            throw new InvalidOperationException("Source texture does not contain a valid RGBA pixel buffer.");

        if (targetMipCount <= 0)
            targetMipCount = 1;

        byte[] preparedRgba = normalizeAsNormalMap
            ? PrepareNormalMapRgba(sourceTexture.RgbaPixels, log)
            : (byte[])sourceTexture.RgbaPixels.Clone();

        if (string.Equals(sourceTexture.ContainerType, "DDS", StringComparison.OrdinalIgnoreCase) && sourceTexture.ContainerBytes != null)
        {
            DdsFile sourceDds = new();
            log?.Invoke("Decoding source DDS.");
            using MemoryStream stream = new(sourceTexture.ContainerBytes, writable: false);
            sourceDds.Load(stream);

            if (sourceDds.FileFormat != targetFormat)
                log?.Invoke($"Converting DDS from {sourceDds.FileFormat} to {targetFormat}.");

            if (sourceDds.MipMaps.Count < targetMipCount)
                log?.Invoke($"DDS mip count {sourceDds.MipMaps.Count} is lower than target mip count {targetMipCount}; regenerating mipmaps.");

            log?.Invoke($"Encoding texture as {targetFormat} with {targetMipCount} mip level(s).");
            byte[] ddsRgba = normalizeAsNormalMap
                ? PrepareNormalMapRgba(sourceDds.BitmapData, log)
                : sourceDds.BitmapData;
            return DdsFile.FromRgba(sourceDds.Width, sourceDds.Height, ddsRgba, targetFormat, targetMipCount);
        }

        if (normalizeAsNormalMap)
            log?.Invoke("Normal-map preprocessing: renormalizing tangent-space RGB before DDS conversion.");

        log?.Invoke($"Converting {sourceTexture.ContainerType} source to {targetFormat}.");
        log?.Invoke($"Encoding texture as {targetFormat} with {targetMipCount} mip level(s).");
        return DdsFile.FromRgba(sourceTexture.Width, sourceTexture.Height, preparedRgba, targetFormat, targetMipCount);
    }

    private static bool IsLikelyNormalTarget(UTexture2D texture, string exportPath)
    {
        string exportName = exportPath ?? string.Empty;
        return texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_WorldNormalMap
            or UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap
            or UTexture.TextureGroup.TEXTUREGROUP_WeaponNormalMap
            or UTexture.TextureGroup.TEXTUREGROUP_VehicleNormalMap
            || exportName.Contains("normal", StringComparison.OrdinalIgnoreCase)
            || exportName.Contains("_n", StringComparison.OrdinalIgnoreCase)
            || exportName.Contains("_nm", StringComparison.OrdinalIgnoreCase)
            || exportName.Contains("norm", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] PrepareNormalMapRgba(byte[] rgba, Action<string> log)
    {
        byte[] prepared = (byte[])rgba.Clone();
        for (int i = 0; i < prepared.Length; i += 4)
        {
            float x = (prepared[i + 0] / 255.0f) * 2.0f - 1.0f;
            float y = (prepared[i + 1] / 255.0f) * 2.0f - 1.0f;
            float zSquared = MathF.Max(0.0f, 1.0f - (x * x) - (y * y));
            float z = MathF.Sqrt(zSquared);
            Vector3 normal = Vector3.Normalize(new Vector3(x, y, zSquared > 1e-8f ? z : 0.0f));

            prepared[i + 0] = EncodeNormalComponent(normal.X);
            prepared[i + 1] = EncodeNormalComponent(normal.Y);
            prepared[i + 2] = EncodeNormalComponent(normal.Z);
            prepared[i + 3] = 255;
        }

        return prepared;
    }

    private static byte EncodeNormalComponent(float value)
    {
        float encoded = ((Math.Clamp(value, -1.0f, 1.0f) * 0.5f) + 0.5f) * 255.0f;
        return (byte)Math.Clamp((int)MathF.Round(encoded), 0, 255);
    }

    private static void EnsureBackupExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string backupPath = path + ".bak";
        if (!File.Exists(backupPath))
            File.Copy(path, backupPath, overwrite: false);
    }

    private static async Task<(UTexture2D Texture, TextureEntry TextureEntry)> LoadTargetAsync(string upkPath, string exportPath)
    {
        UpkFileRepository repository = new();
        var header = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(e => string.Equals(e.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find texture export '{exportPath}'.");

        if (export.UnrealObject == null)
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UTexture2D texture)
            throw new InvalidOperationException($"Export '{exportPath}' is not a Texture2D.");

        TextureEntry textureEntry = TextureManifest.Instance.GetTextureEntryFromObject(export.ObjectNameIndex);
        if (textureEntry == null)
            throw new InvalidOperationException($"Texture '{exportPath}' was not found in {TextureManifest.ManifestName}.");

        if (string.IsNullOrWhiteSpace(textureEntry.Data.TextureFileName))
            throw new InvalidOperationException($"Texture '{exportPath}' does not point at a writable texture cache file.");

        return (texture, textureEntry);
    }
}

internal sealed class TextureInjectionTargetInfo
{
    public string ManifestFilePath { get; init; } = string.Empty;
    public string SourceTextureCachePath { get; init; } = string.Empty;
    public string DestinationTextureCachePath { get; init; } = string.Empty;
}
