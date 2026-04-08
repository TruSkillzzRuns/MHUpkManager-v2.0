using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;

namespace MHUpkManager.MeshImporter;

internal sealed class UpkSkeletalMeshInjector
{
    private readonly UE3LodSerializer _serializer = new();

    public async Task InjectAsync(
        string upkPath,
        UnrealExportTableEntry targetExport,
        MeshImportContext context,
        UE3LodModel lodModel,
        string outputUpkPath)
    {
        byte[] originalBytes = await File.ReadAllBytesAsync(upkPath).ConfigureAwait(false);
        UnrealHeader header = context.Header;

        List<UpkRepacker.ExportBuffer> exportBuffers = header.ExportTable
            .Select(static export => new UpkRepacker.ExportBuffer(export.UnrealObjectReader.GetBytes(), []))
            .ToList();
        exportBuffers[targetExport.TableIndex - 1] = BuildReplacementExportBuffer(context, lodModel);

        byte[] repacked = header.CompressedChunks.Count > 0
            ? UpkRepacker.RepackCompressed(originalBytes, header, exportBuffers)
            : UpkRepacker.Repack(originalBytes, header, exportBuffers);

        await File.WriteAllBytesAsync(outputUpkPath, repacked).ConfigureAwait(false);
    }

    private UpkRepacker.ExportBuffer BuildReplacementExportBuffer(MeshImportContext context, UE3LodModel lodModel)
    {
        SerializedLodModel serializedLod = _serializer.Serialize(lodModel, context);
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
