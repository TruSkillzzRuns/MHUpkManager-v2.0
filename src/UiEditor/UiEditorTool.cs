using System.Drawing.Imaging;
using System.Text;
using MHUpkManager.TexturePreview;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager.UiEditor;

internal sealed class UiEditorTextureAsset
{
    public string ExportPath { get; init; } = string.Empty;
    public string ReplacementFilePath { get; set; } = string.Empty;
}

internal enum UiEditorSwfTransferMode
{
    RawExport,
    EmbeddedPayload
}

internal sealed class UiEditorSwfPreview
{
    public string PackagePath { get; init; } = string.Empty;
    public string ExportPath { get; init; } = string.Empty;
    public int ExportIndex { get; init; }
    public int ExportSize { get; init; }
    public bool EmbeddedPayloadFound { get; init; }
    public string EmbeddedSignature { get; init; } = string.Empty;
    public int EmbeddedOffset { get; init; } = -1;
    public int EmbeddedLength { get; init; }
    public IReadOnlyList<string> StringPreview { get; init; } = Array.Empty<string>();

    public string BuildSummaryText()
    {
        List<string> lines =
        [
            "SWF Export Preview",
            string.Empty,
            $"Package: {Path.GetFileName(PackagePath)}",
            $"Export: {ExportPath}",
            $"Export Index: {ExportIndex}",
            $"Export Size: {ExportSize:N0} bytes",
            $"Embedded Payload Found: {EmbeddedPayloadFound}"
        ];

        if (EmbeddedPayloadFound)
        {
            lines.Add($"Embedded Signature: {EmbeddedSignature}");
            lines.Add($"Embedded Offset: 0x{EmbeddedOffset:X}");
            lines.Add($"Embedded Length: {EmbeddedLength:N0} bytes");
        }

        lines.Add(string.Empty);
        lines.Add("ASCII Preview");
        if (StringPreview.Count == 0)
            lines.Add("No previewable strings were found.");
        else
            lines.AddRange(StringPreview);

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed class UiEditorTool
{
    private readonly EnemyClientUiTargetFinder _targetFinder = new();
    private readonly EnemyClientUiPatchExperiment _patchExperiment = new();
    private readonly UpkTextureLoader _textureLoader = new();
    private readonly TextureLoader _diskTextureLoader = new();
    private readonly TexturePreviewInjector _textureInjector = new();
    private readonly UpkRawExportPatcher _rawExportPatcher = new();

    public Task<IReadOnlyList<EnemyClientUiTarget>> ScanPackageAsync(string packagePath, string subjectName)
    {
        return _targetFinder.FindTargetsAsync(packagePath, subjectName ?? string.Empty);
    }

    public async Task<IReadOnlyList<UiEditorTextureAsset>> ScanTextureAssetsAsync(string packagePath, string subjectName)
    {
        List<string> exports = await _textureLoader.GetTextureExportsAsync(packagePath).ConfigureAwait(true);
        IEnumerable<string> filtered = exports;
        if (!string.IsNullOrWhiteSpace(subjectName))
        {
            filtered = filtered.Where(exportPath => exportPath.Contains(subjectName, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderBy(static exportPath => exportPath, StringComparer.OrdinalIgnoreCase)
            .Select(exportPath => new UiEditorTextureAsset { ExportPath = exportPath })
            .ToArray();
    }

    public Task<EnemyClientUiPatchExperimentResult> CreateDryRunPatchedCopyAsync(
        string packagePath,
        string subjectName,
        string outputDirectory,
        Action<string> log)
    {
        string effectiveSubject = string.IsNullOrWhiteSpace(subjectName)
            ? "Hero"
            : subjectName;

        return _patchExperiment.CreateDryRunPatchedCopyAsync(packagePath, effectiveSubject, outputDirectory, log);
    }

    public Task<TexturePreviewTexture> LoadTexturePreviewAsync(string packagePath, UiEditorTextureAsset asset, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.ExportPath);

        return _textureLoader.LoadFromUpkAsync(packagePath, asset.ExportPath, TexturePreviewMaterialSlot.Diffuse, log);
    }

    public async Task ExportTextureAssetAsync(string packagePath, UiEditorTextureAsset asset, string outputPath, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        using TexturePreviewTexture texture = await LoadTexturePreviewAsync(packagePath, asset, log).ConfigureAwait(true);
        string extension = Path.GetExtension(outputPath).ToLowerInvariant();

        switch (extension)
        {
            case ".dds":
            case ".tga":
                if (texture.ContainerBytes == null || texture.ContainerBytes.Length == 0)
                    throw new InvalidOperationException($"Texture '{asset.ExportPath}' does not expose exportable {extension} container bytes.");

                await File.WriteAllBytesAsync(outputPath, texture.ContainerBytes).ConfigureAwait(true);
                break;

            case ".bmp":
                texture.Bitmap.Save(outputPath, ImageFormat.Bmp);
                break;

            case ".jpg":
            case ".jpeg":
                texture.Bitmap.Save(outputPath, ImageFormat.Jpeg);
                break;

            default:
                texture.Bitmap.Save(outputPath, ImageFormat.Png);
                break;
        }

        log?.Invoke($"UI Editor: exported {asset.ExportPath} to {outputPath}.");
    }

    public async Task InjectTextureAssetAsync(string packagePath, UiEditorTextureAsset asset, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.ExportPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.ReplacementFilePath);

        await ValidateTextureReplacementSupportAsync(packagePath, asset.ExportPath).ConfigureAwait(true);

        TexturePreviewTexture replacement = _diskTextureLoader.LoadFromFile(asset.ReplacementFilePath, TexturePreviewMaterialSlot.Diffuse);
        await _textureInjector.InjectAsync(packagePath, asset.ExportPath, replacement, log).ConfigureAwait(true);
    }

    private async Task ValidateTextureReplacementSupportAsync(string packagePath, string exportPath)
    {
        try
        {
            await _textureInjector.ResolveTargetInfoAsync(packagePath, exportPath).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Load TextureFileCacheManifest.bin first", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "UI image replacement needs TextureFileCacheManifest.bin to be loaded first. Load the texture manifest in the app, then try the replacement again.",
                ex);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("was not found in TextureFileCacheManifest.bin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Texture '{exportPath}' is not tracked by TextureFileCacheManifest.bin. Right now the UI Editor can only inject textures that are manifest/cache-backed. This UPK-only icon asset can be previewed and exported, but direct replacement for it is not implemented yet.",
                ex);
        }
    }

    public async Task<UiEditorSwfPreview> PreviewSwfMovieAsync(string packagePath, EnemyClientUiTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(target);

        byte[] exportBytes = await LoadCurrentExportBytesAsync(packagePath, target.ExportPath).ConfigureAwait(true);
        EmbeddedPayloadInfo payloadInfo = FindEmbeddedPayload(exportBytes);

        return new UiEditorSwfPreview
        {
            PackagePath = packagePath,
            ExportPath = target.ExportPath,
            ExportIndex = target.ExportIndex,
            ExportSize = exportBytes.Length,
            EmbeddedPayloadFound = payloadInfo.Found,
            EmbeddedSignature = payloadInfo.Signature,
            EmbeddedOffset = payloadInfo.Offset,
            EmbeddedLength = payloadInfo.Length,
            StringPreview = ExtractAsciiStrings(exportBytes, 5, 24)
        };
    }

    public async Task ExportSwfMovieAsync(
        string packagePath,
        EnemyClientUiTarget target,
        string outputPath,
        UiEditorSwfTransferMode mode,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        byte[] exportBytes = await LoadCurrentExportBytesAsync(packagePath, target.ExportPath).ConfigureAwait(true);
        byte[] bytesToWrite = mode == UiEditorSwfTransferMode.RawExport
            ? exportBytes
            : ExtractEmbeddedPayload(exportBytes, target.ExportPath);

        await File.WriteAllBytesAsync(outputPath, bytesToWrite).ConfigureAwait(true);
        log?.Invoke($"UI Editor: exported {target.ExportPath} ({mode}) to {outputPath}.");
    }

    public async Task<string> ImportSwfMovieAsync(
        string packagePath,
        EnemyClientUiTarget target,
        string importPath,
        UiEditorSwfTransferMode mode,
        Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(importPath);

        byte[] importBytes = await File.ReadAllBytesAsync(importPath).ConfigureAwait(true);
        (UnrealHeader header, UnrealExportTableEntry export) = await LoadExportEntryAsync(packagePath, target.ExportPath).ConfigureAwait(true);
        byte[] currentExportBytes = SliceExportBytes(await _rawExportPatcher.GetLogicalPackageBytesAsync(packagePath).ConfigureAwait(true), export);
        byte[] replacementBytes = mode == UiEditorSwfTransferMode.RawExport
            ? importBytes
            : ReplaceEmbeddedPayload(currentExportBytes, importBytes, target.ExportPath);

        string backupPath = packagePath + ".bak";
        if (!File.Exists(backupPath))
            File.Copy(packagePath, backupPath, overwrite: false);

        string tempPath = packagePath + ".ui-editor.tmp";
        try
        {
            await _rawExportPatcher.PatchExportsAsync(packagePath, new Dictionary<int, byte[]>
            {
                [export.TableIndex] = replacementBytes
            }, tempPath).ConfigureAwait(true);

            File.Copy(tempPath, packagePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        log?.Invoke($"UI Editor: imported {mode} bytes from {importPath} into {target.ExportPath}.");
        log?.Invoke($"UI Editor: package backup is {backupPath}.");
        return backupPath;
    }

    private async Task<byte[]> LoadCurrentExportBytesAsync(string packagePath, string exportPath)
    {
        (UnrealHeader header, UnrealExportTableEntry export) = await LoadExportEntryAsync(packagePath, exportPath).ConfigureAwait(true);
        byte[] logicalBytes = await _rawExportPatcher.GetLogicalPackageBytesAsync(packagePath).ConfigureAwait(true);
        return SliceExportBytes(logicalBytes, export);
    }

    private static byte[] SliceExportBytes(byte[] logicalBytes, UnrealExportTableEntry export)
    {
        byte[] bytes = new byte[export.SerialDataSize];
        Buffer.BlockCopy(logicalBytes, export.SerialDataOffset, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] ReplaceEmbeddedPayload(byte[] exportBytes, byte[] payloadBytes, string exportPath)
    {
        EmbeddedPayloadInfo payloadInfo = FindEmbeddedPayload(exportBytes);
        if (!payloadInfo.Found)
            throw new InvalidOperationException($"No embedded SWF/GFX payload was found inside export '{exportPath}'.");

        byte[] updated = new byte[exportBytes.Length - payloadInfo.Length + payloadBytes.Length];
        Buffer.BlockCopy(exportBytes, 0, updated, 0, payloadInfo.Offset);
        Buffer.BlockCopy(payloadBytes, 0, updated, payloadInfo.Offset, payloadBytes.Length);

        int suffixLength = exportBytes.Length - (payloadInfo.Offset + payloadInfo.Length);
        if (suffixLength > 0)
        {
            Buffer.BlockCopy(
                exportBytes,
                payloadInfo.Offset + payloadInfo.Length,
                updated,
                payloadInfo.Offset + payloadBytes.Length,
                suffixLength);
        }

        return updated;
    }

    private static byte[] ExtractEmbeddedPayload(byte[] exportBytes, string exportPath)
    {
        EmbeddedPayloadInfo payloadInfo = FindEmbeddedPayload(exportBytes);
        if (!payloadInfo.Found)
            throw new InvalidOperationException($"No embedded SWF/GFX payload was found inside export '{exportPath}'.");

        byte[] bytes = new byte[payloadInfo.Length];
        Buffer.BlockCopy(exportBytes, payloadInfo.Offset, bytes, 0, bytes.Length);
        return bytes;
    }

    private static EmbeddedPayloadInfo FindEmbeddedPayload(byte[] bytes)
    {
        string[] signatures = ["FWS", "CWS", "ZWS", "GFX"];
        foreach (string signature in signatures)
        {
            int offset = FindAscii(bytes, signature);
            if (offset < 0)
                continue;

            int remaining = bytes.Length - offset;
            int length = remaining;
            if (remaining >= 8)
            {
                int declaredLength = BitConverter.ToInt32(bytes, offset + 4);
                if (declaredLength > 0 && declaredLength <= remaining)
                    length = declaredLength;
            }

            return new EmbeddedPayloadInfo(true, signature, offset, length);
        }

        return new EmbeddedPayloadInfo(false, string.Empty, -1, 0);
    }

    private static IReadOnlyList<string> ExtractAsciiStrings(byte[] bytes, int minimumLength, int maxCount)
    {
        List<string> values = [];
        StringBuilder builder = new();

        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if (value is >= 32 and <= 126)
            {
                builder.Append((char)value);
                continue;
            }

            Flush();
        }

        Flush();
        return values.Take(maxCount).ToArray();

        void Flush()
        {
            if (builder.Length >= minimumLength)
            {
                string text = builder.ToString();
                if (!values.Any(existing => string.Equals(existing, text, StringComparison.OrdinalIgnoreCase)))
                    values.Add(text);
            }

            builder.Clear();
        }
    }

    private static int FindAscii(byte[] bytes, string value)
    {
        byte[] needle = Encoding.ASCII.GetBytes(value);
        for (int index = 0; index <= bytes.Length - needle.Length; index++)
        {
            bool matched = true;
            for (int needleIndex = 0; needleIndex < needle.Length; needleIndex++)
            {
                if (bytes[index + needleIndex] == needle[needleIndex])
                    continue;

                matched = false;
                break;
            }

            if (matched)
                return index;
        }

        return -1;
    }

    private static async Task<(UnrealHeader Header, UnrealExportTableEntry Export)> LoadExportEntryAsync(string packagePath, string exportPath)
    {
        UpkFileRepository repository = new();
        UnrealHeader header = await repository.LoadUpkFile(packagePath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(item => string.Equals(item.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find export '{exportPath}' in {Path.GetFileName(packagePath)}.");

        return (header, export);
    }

    private readonly record struct EmbeddedPayloadInfo(bool Found, string Signature, int Offset, int Length);
}
