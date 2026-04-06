using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Compression;
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

        List<ExportReplacement> exportBuffers = header.ExportTable
            .Select(static export => new ExportReplacement(export.UnrealObjectReader.GetBytes(), []))
            .ToList();
        exportBuffers[targetExport.TableIndex - 1] = BuildReplacementExportBuffer(context, newLod);

        byte[] repacked = header.CompressedChunks.Count > 0
            ? RepackCompressedFile(originalBytes, header, exportBuffers)
            : RepackFile(originalBytes, header, exportBuffers);

        await File.WriteAllBytesAsync(outputUpkPath, repacked).ConfigureAwait(false);
    }

    private ExportReplacement BuildReplacementExportBuffer(MeshImportContext context, FStaticLODModel newLod)
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

        List<BulkDataPatch> patches = serializedLod.BulkDataPatches
            .Select(static patch => new BulkDataPatch(
                patch.OffsetFieldPosition,
                patch.DataStartPosition))
            .ToList();

        return new ExportReplacement(output, [.. patches.Select(p => new BulkDataPatch(context.LodDataOffset + p.OffsetFieldPosition, context.LodDataOffset + p.DataStartPosition))]);
    }

    private static byte[] RepackFile(byte[] originalBytes, UnrealHeader header, IReadOnlyList<ExportReplacement> exportBuffers)
    {
        int headerSize = header.Size;
        byte[] repacked = new byte[headerSize + exportBuffers.Sum(static b => b.Buffer.Length)];
        Buffer.BlockCopy(originalBytes, 0, repacked, 0, Math.Min(headerSize, originalBytes.Length));

        List<int> entryOffsets = LocateExportTableOffsets(originalBytes, header);
        int cursor = headerSize;

        for (int i = 0; i < exportBuffers.Count; i++)
        {
            ExportReplacement exportReplacement = exportBuffers[i];
            byte[] exportData = exportReplacement.Buffer;
            Buffer.BlockCopy(exportData, 0, repacked, cursor, exportData.Length);

            foreach (BulkDataPatch patch in exportReplacement.BulkDataPatches)
                WriteInt32(repacked, cursor + patch.OffsetFieldPosition, cursor + patch.DataStartPosition);

            int serialSizeOffset = entryOffsets[i] + 32;
            int serialOffsetOffset = entryOffsets[i] + 36;
            WriteInt32(repacked, serialSizeOffset, exportData.Length);
            WriteInt32(repacked, serialOffsetOffset, cursor);

            cursor += exportData.Length;
        }

        WriteInt32(repacked, 8, headerSize);
        return repacked;
    }

    private static byte[] RepackCompressedFile(byte[] originalBytes, UnrealHeader header, IReadOnlyList<ExportReplacement> exportBuffers)
    {
        byte[] decompressedBytes = DecompressFullPackage(header);
        HeaderPatchOffsets offsets = LocateHeaderPatchOffsets(originalBytes);
        int compressionTableOffset = offsets.CompressionCountOffset + sizeof(int);
        int compressionTableLength = header.CompressionTableCount * 16;
        int compressedDataStart = header.CompressedChunks.Min(static chunk => chunk.CompressedOffset);

        Buffer.BlockCopy(originalBytes, 0, decompressedBytes, 0, Math.Min(compressionTableOffset, Math.Min(originalBytes.Length, decompressedBytes.Length)));

        int shiftedHeaderSourceOffset = compressionTableOffset + compressionTableLength;
        int shiftedHeaderLength = Math.Max(0, compressedDataStart - shiftedHeaderSourceOffset);
        if (shiftedHeaderLength > 0)
        {
            Buffer.BlockCopy(
                originalBytes,
                shiftedHeaderSourceOffset,
                decompressedBytes,
                compressionTableOffset,
                Math.Min(shiftedHeaderLength, Math.Min(originalBytes.Length - shiftedHeaderSourceOffset, decompressedBytes.Length - compressionTableOffset)));
        }

        ClearCompressionHeaderFlags(decompressedBytes);
        WriteInt32(decompressedBytes, offsets.CompressionCountOffset, 0);
        return RepackFile(decompressedBytes, header, exportBuffers);
    }

    private static byte[] DecompressFullPackage(UnrealHeader header)
    {
        int start = header.CompressedChunks.Min(static chunk => chunk.UncompressedOffset);
        int totalSize = header.CompressedChunks
            .SelectMany(static chunk => chunk.Header.Blocks)
            .Sum(static block => block.UncompressedSize) + start;

        byte[] data = new byte[totalSize];
        foreach (UnrealCompressedChunk chunk in header.CompressedChunks)
        {
            int localOffset = 0;
            foreach (UnrealCompressedChunkBlock block in chunk.Header.Blocks)
            {
                byte[] decompressed = block.CompressedData.Decompress(block.UncompressedSize);
                Buffer.BlockCopy(decompressed, 0, data, chunk.UncompressedOffset + localOffset, decompressed.Length);
                localOffset += block.UncompressedSize;
            }
        }

        return data;
    }

    private static void ClearCompressionHeaderFlags(byte[] bytes)
    {
        HeaderPatchOffsets offsets = LocateHeaderPatchOffsets(bytes);

        WriteUInt32(bytes, offsets.PackageFlagsOffset, ReadUInt32(bytes, offsets.PackageFlagsOffset) &
            ~(uint)(EPackageFlags.Compressed | EPackageFlags.FullyCompressed));
        WriteUInt32(bytes, offsets.CompressionFlagsOffset, 0);
    }

    private static HeaderPatchOffsets LocateHeaderPatchOffsets(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using BinaryReader reader = new(stream);

        stream.Position = 8;
        _ = reader.ReadInt32();

        int groupSize = reader.ReadInt32();
        if (groupSize < 0)
            stream.Position += -groupSize * 2L;
        else if (groupSize > 0)
            stream.Position += groupSize;

        int packageFlagsOffset = checked((int)stream.Position);
        stream.Position += sizeof(uint);

        stream.Position += sizeof(int) * 11L;
        stream.Position += 16;

        int generationCount = reader.ReadInt32();
        stream.Position += generationCount * 12L;
        stream.Position += sizeof(uint) * 2L;

        int compressionFlagsOffset = checked((int)stream.Position);
        int compressionCountOffset = compressionFlagsOffset + sizeof(uint);

        return new HeaderPatchOffsets(packageFlagsOffset, compressionFlagsOffset, compressionCountOffset);
    }

    private static List<int> LocateExportTableOffsets(byte[] originalBytes, UnrealHeader header)
    {
        List<int> offsets = new(header.ExportTable.Count);
        int cursor = header.ExportTableOffset;
        foreach (UnrealExportTableEntry export in header.ExportTable)
        {
            offsets.Add(cursor);
            cursor += 68 + (export.NetObjects.Count * sizeof(int));
        }

        return offsets;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return BitConverter.ToUInt32(buffer, offset);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    private readonly record struct HeaderPatchOffsets(
        int PackageFlagsOffset,
        int CompressionFlagsOffset,
        int CompressionCountOffset);

    private sealed record ExportReplacement(byte[] Buffer, IReadOnlyList<BulkDataPatch> BulkDataPatches);
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
