using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager.Model.Import;

internal sealed class UE3SkeletalMeshInjector
{
    private readonly UE3LodSerializer _serializer = new();

    public async Task InjectAsync(string upkPath, UnrealExportTableEntry targetExport, MeshImportContext context, FStaticLODModel newLod, string outputUpkPath)
    {
        byte[] originalBytes = await File.ReadAllBytesAsync(upkPath).ConfigureAwait(false);
        UnrealHeader header = context.Header;

        List<UpkRepacker.ExportBuffer> exportBuffers = header.ExportTable
            .Select(static export => new UpkRepacker.ExportBuffer(export.UnrealObjectReader.GetBytes(), []))
            .ToList();
        exportBuffers[targetExport.TableIndex - 1] = BuildReplacementExportBuffer(context, newLod);

        byte[] repacked = header.CompressedChunks.Count > 0
            ? UpkRepacker.RepackCompressed(originalBytes, header, exportBuffers)
            : UpkRepacker.Repack(originalBytes, header, exportBuffers);

        await File.WriteAllBytesAsync(outputUpkPath, repacked).ConfigureAwait(false);
    }

    private UpkRepacker.ExportBuffer BuildReplacementExportBuffer(MeshImportContext context, FStaticLODModel newLod)
    {
        SerializedLodModel serializedLod = _serializer.SerializeLodModel(newLod, context);
        byte[] newLodBytes = serializedLod.Bytes;
        int prefixLength = context.LodDataOffset;
        int suffixOffset = context.LodDataOffset + context.LodDataSize;
        int suffixLength = context.RawExportData.Length - suffixOffset;

        byte[] output = new byte[prefixLength + newLodBytes.Length + suffixLength];
        Buffer.BlockCopy(context.RawExportData, 0, output, 0, prefixLength);
        Buffer.BlockCopy(newLodBytes, 0, output, prefixLength, newLodBytes.Length);
        Buffer.BlockCopy(context.RawExportData, suffixOffset, output, prefixLength + newLodBytes.Length, suffixLength);

        IReadOnlyList<UpkRepacker.BulkDataPatch> patches = serializedLod.BulkDataPatches
            .Select(p => new UpkRepacker.BulkDataPatch(
                context.LodDataOffset + p.OffsetFieldPosition,
                context.LodDataOffset + p.DataStartPosition))
            .ToArray();

        return new UpkRepacker.ExportBuffer(output, patches);
    }
}

internal sealed class SkeletalMeshImportPipeline
{
    private readonly UpkFileRepository _repository = new();
    private readonly FbxMeshImporter _fbxImporter = new();
    private readonly BoneRemapper _boneRemapper = new();
    private readonly WeightNormalizer _weightNormalizer = new();
    private readonly UE3LodBuilder _lodBuilder = new();
    private readonly UE3SkeletalMeshInjector _injector = new();

    public async Task ImportAsync(string upkPath, string exportPath, string fbxPath, string outputUpkPath, int lodIndex = 0)
    {
        UnrealHeader header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(false);
        await header.ReadHeaderAsync(null).ConfigureAwait(false);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(e => string.Equals(e.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{exportPath}' in '{upkPath}'.");

        MeshImportContext context = await MeshImportContext.CreateAsync(header, export, lodIndex).ConfigureAwait(false);
        NeutralMesh neutralMesh = _fbxImporter.Import(fbxPath);
        IReadOnlyList<IReadOnlyList<RemappedWeight>> remapped = _boneRemapper.Remap(neutralMesh, context);
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalized = _weightNormalizer.Normalize(remapped);
        FStaticLODModel newLod = _lodBuilder.Build(neutralMesh, context, normalized);
        ImportDiagnostics.WriteImportSummary(context, neutralMesh, newLod);

        await _injector.InjectAsync(upkPath, export, context, newLod, outputUpkPath).ConfigureAwait(false);
    }
}
