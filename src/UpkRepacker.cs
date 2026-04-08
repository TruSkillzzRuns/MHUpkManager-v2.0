using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Compression;
using UpkManager.Models.UpkFile.Tables;

namespace MHUpkManager;

/// <summary>
/// Shared UPK repack and decompression utility used by all export-replacement injectors.
/// </summary>
internal static class UpkRepacker
{
    public readonly record struct BulkDataPatch(int OffsetFieldPosition, int DataStartPosition);
    public sealed record ExportBuffer(byte[] Data, IReadOnlyList<BulkDataPatch> Patches);

    // Single-export convenience overloads ----------------------------------------

    /// <summary>
    /// Replaces one export in an uncompressed UPK, repacks, and returns the result.
    /// </summary>
    public static byte[] Repack(
        byte[] originalBytes,
        UnrealHeader header,
        int targetExportIndex,
        byte[] newExportData,
        IReadOnlyList<BulkDataPatch> patches = null)
    {
        List<ExportBuffer> buffers = BuildBufferList(header, targetExportIndex, newExportData, patches);
        return RepackCore(originalBytes, header, buffers);
    }

    /// <summary>
    /// Decompresses a fully-compressed UPK, replaces one export, and repacks as uncompressed.
    /// </summary>
    public static byte[] RepackCompressed(
        byte[] originalBytes,
        UnrealHeader header,
        int targetExportIndex,
        byte[] newExportData,
        IReadOnlyList<BulkDataPatch> patches = null)
    {
        List<ExportBuffer> buffers = BuildBufferList(header, targetExportIndex, newExportData, patches);
        return RepackCompressedCore(originalBytes, header, buffers);
    }

    // Multi-export overloads (used by mesh injector for bulk-data offset patches) --

    /// <summary>
    /// Replaces exports (with optional bulk-data offset patches) in an uncompressed UPK.
    /// </summary>
    public static byte[] Repack(
        byte[] originalBytes,
        UnrealHeader header,
        IReadOnlyList<ExportBuffer> exportBuffers)
        => RepackCore(originalBytes, header, exportBuffers);

    /// <summary>
    /// Decompresses a fully-compressed UPK, replaces exports, and repacks as uncompressed.
    /// </summary>
    public static byte[] RepackCompressed(
        byte[] originalBytes,
        UnrealHeader header,
        IReadOnlyList<ExportBuffer> exportBuffers)
        => RepackCompressedCore(originalBytes, header, exportBuffers);

    // -------------------------------------------------------------------------

    private static List<ExportBuffer> BuildBufferList(UnrealHeader header, int targetIndex, byte[] newData, IReadOnlyList<BulkDataPatch> patches = null)
    {
        List<ExportBuffer> buffers = header.ExportTable
            .Select(static e => new ExportBuffer(e.UnrealObjectReader.GetBytes(), []))
            .ToList();
        buffers[targetIndex] = new ExportBuffer(newData, patches ?? []);
        return buffers;
    }

    private static byte[] RepackCore(byte[] sourceBytes, UnrealHeader header, IReadOnlyList<ExportBuffer> exportBuffers)
    {
        int headerSize = header.Size;
        byte[] repacked = new byte[headerSize + exportBuffers.Sum(static b => b.Data.Length)];
        Buffer.BlockCopy(sourceBytes, 0, repacked, 0, Math.Min(headerSize, sourceBytes.Length));

        List<int> entryOffsets = LocateExportTableOffsets(sourceBytes, header);
        int cursor = headerSize;

        for (int i = 0; i < exportBuffers.Count; i++)
        {
            byte[] exportData = exportBuffers[i].Data;
            Buffer.BlockCopy(exportData, 0, repacked, cursor, exportData.Length);

            foreach (BulkDataPatch patch in exportBuffers[i].Patches)
                WriteInt32(repacked, cursor + patch.OffsetFieldPosition, cursor + patch.DataStartPosition);

            WriteInt32(repacked, entryOffsets[i] + 32, exportData.Length);  // SerialSize
            WriteInt32(repacked, entryOffsets[i] + 36, cursor);             // SerialOffset
            cursor += exportData.Length;
        }

        WriteInt32(repacked, 8, headerSize);
        return repacked;
    }

    private static byte[] RepackCompressedCore(byte[] originalBytes, UnrealHeader header, IReadOnlyList<ExportBuffer> exportBuffers)
    {
        byte[] decompressedBytes = DecompressFullPackage(header);
        HeaderPatchOffsets offsets = LocateHeaderPatchOffsets(originalBytes);
        int compressionTableOffset = offsets.CompressionCountOffset + sizeof(int);
        int compressionTableLength = header.CompressionTableCount * 16;
        int compressedDataStart = header.CompressedChunks.Min(static chunk => chunk.CompressedOffset);

        Buffer.BlockCopy(originalBytes, 0, decompressedBytes, 0,
            Math.Min(compressionTableOffset, Math.Min(originalBytes.Length, decompressedBytes.Length)));

        int shiftedHeaderSourceOffset = compressionTableOffset + compressionTableLength;
        int shiftedHeaderLength = Math.Max(0, compressedDataStart - shiftedHeaderSourceOffset);
        if (shiftedHeaderLength > 0)
        {
            Buffer.BlockCopy(
                originalBytes,
                shiftedHeaderSourceOffset,
                decompressedBytes,
                compressionTableOffset,
                Math.Min(shiftedHeaderLength, Math.Min(
                    originalBytes.Length - shiftedHeaderSourceOffset,
                    decompressedBytes.Length - compressionTableOffset)));
        }

        ClearCompressionHeaderFlags(decompressedBytes);
        WriteInt32(decompressedBytes, offsets.CompressionCountOffset, 0);
        return RepackCore(decompressedBytes, header, exportBuffers);
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
        WriteUInt32(bytes, offsets.PackageFlagsOffset,
            ReadUInt32(bytes, offsets.PackageFlagsOffset) & ~(uint)(EPackageFlags.Compressed | EPackageFlags.FullyCompressed));
        WriteUInt32(bytes, offsets.CompressionFlagsOffset, 0);
    }

    private static HeaderPatchOffsets LocateHeaderPatchOffsets(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using BinaryReader reader = new(stream);

        stream.Position = 8;
        _ = reader.ReadInt32();  // Size field

        int groupSize = reader.ReadInt32();
        if (groupSize < 0)
            stream.Position += -groupSize * 2L;
        else if (groupSize > 0)
            stream.Position += groupSize;

        int packageFlagsOffset = checked((int)stream.Position);
        stream.Position += sizeof(uint);

        stream.Position += sizeof(int) * 11L;
        stream.Position += 16;  // GUID

        int generationCount = reader.ReadInt32();
        stream.Position += generationCount * 12L;
        stream.Position += sizeof(uint) * 2L;  // engine/cooker version

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

    private static uint ReadUInt32(byte[] buffer, int offset) => BitConverter.ToUInt32(buffer, offset);

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    private readonly record struct HeaderPatchOffsets(
        int PackageFlagsOffset,
        int CompressionFlagsOffset,
        int CompressionCountOffset);
}
