using DDSLib;
using DDSLib.Constants;
using MHUpkManager.TextureManager;
using System.Numerics;
using UpkManager.Constants;
using UpkManager.Models.UpkFile;
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

    public async Task InjectAsync(string upkPath, string exportPath, TexturePreviewTexture sourceTexture, Action<string> log = null)    {
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

    /// <summary>
    /// Injects a replacement texture directly into a UPK whose Texture2D mip data is stored
    /// inline (not in a .tfc file).  Used for HUD/UI textures that have no manifest entry.
    /// Dimensions and format must match the target; a .bak backup is created automatically.
    /// </summary>
    public async Task InjectInlineAsync(string upkPath, string exportPath, TexturePreviewTexture sourceTexture, Action<string> log = null)
    {
        if (sourceTexture == null) throw new ArgumentNullException(nameof(sourceTexture));

        log?.Invoke($"Opening package: {Path.GetFileName(upkPath)}");
        (UTexture2D texture, byte[] originalBytes, UnrealHeader header, UnrealExportTableEntry export) =
            await LoadTextureExportAsync(upkPath, exportPath).ConfigureAwait(true);

        if (sourceTexture.Width != texture.SizeX || sourceTexture.Height != texture.SizeY)
            throw new InvalidOperationException(
                $"Texture dimensions must match. Target is {texture.SizeX}\u00d7{texture.SizeY}, source is {sourceTexture.Width}\u00d7{sourceTexture.Height}.");

        FileFormat targetFormat = UTexture2D.ParseFileFormat(texture.Format);
        int targetMipCount = texture.Mips.Count(m => m.Data != null && m.Data.Length > 0);
        if (targetMipCount == 0) targetMipCount = 1;

        bool isNormal = IsLikelyNormalTarget(texture, exportPath);
        log?.Invoke($"Target: format={texture.Format}, size={texture.SizeX}\u00d7{texture.SizeY}, mips={targetMipCount}.");

        DdsFile dds = await Task.Run(() => BuildWritableTexture(sourceTexture, targetFormat, targetMipCount, isNormal, log)).ConfigureAwait(true);

        if (dds.MipMaps.Count < targetMipCount)
            throw new InvalidOperationException($"DDS produced {dds.MipMaps.Count} mip(s) but target requires {targetMipCount}.");

        // Re-encode each mip as an LZO_ENC bulk data block in a new export buffer.
        // Layout mirrors FTexture2DMipMap: [BulkData header 16B] [raw data] [SizeX int32] [SizeY int32]
        // The original package stores mip data as uncompressed inline bulk data (flags=0) inside
        // package-level LZO chunks.  We output an uncompressed package, so we also write the mip
        // data as uncompressed inline bulk data — NOT as per-export LZO_ENC.
        byte[] prefix = GetTextureMipPrefix(export, texture);
        byte[] mipSuffix = GetTextureMipSuffix(export, texture);

        log?.Invoke($"[Diag] exportBytes={export.UnrealObjectReader.GetBytes().Length} prefix={prefix.Length} mipArrayOffset={texture.MipArrayOffset} mipCount={targetMipCount} suffix={mipSuffix.Length}");

        using MemoryStream mipStream = new();
        List<UpkRepacker.BulkDataPatch> bulkPatches = [];
        int mipRegionBase = prefix.Length + 4; // prefix + mipCount int32
        for (int i = 0; i < targetMipCount; i++)
        {
            byte[] mipData = dds.MipMaps[i].MipMap;

            // Build uncompressed inline bulk data:
            //   uint32 BulkDataFlags = 0          (uncompressed inline)
            //   int32  UncompressedSize            (= raw data length)
            //   int32  CompressedSize              (= same as uncompressed)
            //   int32  Offset                      (absolute file offset — patched by UpkRepacker)
            //   byte[] RawData[CompressedSize]
            //   int32  SizeX
            //   int32  SizeY
            int chunkStartInExport = mipRegionBase + (int)mipStream.Position;
            // offset field is at byte 12, data starts at byte 16
            bulkPatches.Add(new UpkRepacker.BulkDataPatch(chunkStartInExport + 12, chunkStartInExport + 16));

            mipStream.Write(BitConverter.GetBytes((uint)0), 0, 4);         // BulkDataFlags = 0 (uncompressed)
            mipStream.Write(BitConverter.GetBytes(mipData.Length), 0, 4);   // UncompressedSize
            mipStream.Write(BitConverter.GetBytes(mipData.Length), 0, 4);   // CompressedSize (same)
            mipStream.Write(BitConverter.GetBytes(0), 0, 4);               // Offset placeholder (patched later)
            mipStream.Write(mipData, 0, mipData.Length);                   // Raw pixel data
            mipStream.Write(BitConverter.GetBytes(dds.MipMaps[i].Width),  0, 4); // SizeX
            mipStream.Write(BitConverter.GetBytes(dds.MipMaps[i].Height), 0, 4); // SizeY

            log?.Invoke($"[Diag] mip[{i}] mipData={mipData.Length} bulkEntry={16 + mipData.Length + 8}");
        }

        byte[] mipCountBytes = BitConverter.GetBytes(targetMipCount);
        byte[] newMipRegion = mipStream.ToArray();
        log?.Invoke($"[Diag] newMipRegion={newMipRegion.Length} mipSuffix={mipSuffix.Length} newExportBuffer={prefix.Length + 4 + newMipRegion.Length + mipSuffix.Length}");
        byte[] newExportBuffer = new byte[prefix.Length + 4 + newMipRegion.Length + mipSuffix.Length];
        Buffer.BlockCopy(prefix,        0, newExportBuffer, 0,                                         prefix.Length);
        Buffer.BlockCopy(mipCountBytes, 0, newExportBuffer, prefix.Length,                             4);
        Buffer.BlockCopy(newMipRegion,  0, newExportBuffer, prefix.Length + 4,                         newMipRegion.Length);
        Buffer.BlockCopy(mipSuffix,     0, newExportBuffer, prefix.Length + 4 + newMipRegion.Length,  mipSuffix.Length);

        byte[] repacked = header.CompressedChunks.Count > 0
            ? UpkRepacker.RepackCompressed(originalBytes, header, export.TableIndex - 1, newExportBuffer, bulkPatches)
            : UpkRepacker.Repack(originalBytes, header, export.TableIndex - 1, newExportBuffer, bulkPatches);

        EnsureBackupExists(upkPath);
        await File.WriteAllBytesAsync(upkPath, repacked).ConfigureAwait(true);
        log?.Invoke($"Inline texture injected into {exportPath} in {Path.GetFileName(upkPath)}.");
    }

    private static async Task<(UTexture2D Texture, byte[] OriginalBytes, UnrealHeader Header, UnrealExportTableEntry Export)>
        LoadTextureExportAsync(string upkPath, string exportPath)
    {
        byte[] originalBytes = await File.ReadAllBytesAsync(upkPath).ConfigureAwait(true);
        UpkFileRepository repository = new();
        UnrealHeader header = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(e => string.Equals(e.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find texture export '{exportPath}'.");

        if (export.UnrealObject == null)
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UTexture2D texture)
            throw new InvalidOperationException($"Export '{exportPath}' is not a Texture2D.");

        return (texture, originalBytes, header, export);
    }

    // Returns the bytes of the export buffer up to (not including) the mip count int32.
    private static byte[] GetTextureMipPrefix(UnrealExportTableEntry export, UTexture2D texture)
    {
        byte[] exportBytes = export.UnrealObjectReader.GetBytes();
        // MipArrayOffset is the reader position right after the tagged properties (after the None
        // terminator), which is where the Mips count int32 begins.
        return exportBytes[..texture.MipArrayOffset];
    }

    // Returns the bytes after the last original mip's SizeY field, i.e. everything from the
    // TextureFileCacheGuid onward (GUID + cached platform mip arrays + flash mip data).
    private static byte[] GetTextureMipSuffix(UnrealExportTableEntry export, UTexture2D texture)
    {
        byte[] exportBytes = export.UnrealObjectReader.GetBytes();
        int cursor = texture.MipArrayOffset + 4; // skip mip count int32
        foreach (var mip in texture.Mips)
        {
            // Each FTexture2DMipMap bulk data header is: flags(4)+uncompSize(4)+compSize(4)+offset(4) = 16 bytes
            // followed by compressed data blocks, then SizeX(4)+SizeY(4).
            // We stored the original reader bytes so we can scan them directly.
            using MemoryStream scanner = new(exportBytes, cursor, exportBytes.Length - cursor, writable: false);
            using BinaryReader br = new(scanner);
            uint flags = br.ReadUInt32();
            int uncompSize = br.ReadInt32();
            int compSize = br.ReadInt32();
            int offset = br.ReadInt32(); // absolute data offset, ignore
            // If StoreInSeparatefile or Unused: no payload follows
            const uint nothingToDo = (uint)(BulkDataCompressionTypes.Unused | BulkDataCompressionTypes.StoreInSeparatefile);
            int payloadSize = (flags & nothingToDo) != 0 ? 0 : compSize;
            cursor += 16 + payloadSize + 4 + 4; // header + payload + SizeX + SizeY
        }
        return exportBytes[cursor..];
    }
}

internal sealed class TextureInjectionTargetInfo
{
    public string ManifestFilePath { get; init; } = string.Empty;
    public string SourceTextureCachePath { get; init; } = string.Empty;
    public string DestinationTextureCachePath { get; init; } = string.Empty;
}
