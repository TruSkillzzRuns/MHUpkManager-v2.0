using System.Text;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Types;

namespace MHUpkManager.MeshImporter;

internal sealed class UE3LodSerializer
{
    public SerializedLodModel Serialize(UE3LodModel lodModel, MeshImportContext context)
    {
        return SerializeLodModel(lodModel.Inner, context);
    }

    internal SerializedLodModel SerializeLodModel(FStaticLODModel lod, MeshImportContext context)
    {
        using UE3BinaryWriter writer = new();
        List<BulkDataPatch> bulkDataPatches = [];

        WriteArray(writer, lod.Sections, WriteSection);
        WriteMultiSizeIndexContainer(writer, lod.MultiSizeIndexContainer);
        WriteArray(writer, lod.ActiveBoneIndices, static (w, value) => w.Write(value));
        WriteArray(writer, lod.Chunks, WriteChunk);

        long sizePosition = writer.Position;
        writer.Write(0u);
        writer.Write(lod.NumVertices);

        WriteByteBlob(writer, lod.RequiredBones);
        WriteRawBulkData(writer, lod.RawPointIndices, context.OriginalRawPointIndicesBlob, bulkDataPatches);
        writer.Write(lod.NumTexCoords);
        WriteVertexBuffer(writer, lod.VertexBufferGPUSkin, context.NumTexCoords, context.UseFullPrecisionUvs);

        if (context.HasVertexColors)
            WriteColorVertexBuffer(writer, lod.ColorVertexBuffer);

        WriteArray(writer, lod.VertexInfluences, WriteVertexInfluenceBuffer);
        WriteMultiSizeIndexContainer(writer, lod.AdjacencyMultiSizeIndexContainer);

        writer.PatchUInt32(sizePosition, checked((uint)writer.Position));
        return new SerializedLodModel(writer.ToArray(), [.. bulkDataPatches]);
    }

    private static void WriteVertexInfluenceBuffer(UE3BinaryWriter writer, FSkeletalMeshVertexInfluences value)
    {
        WriteArray(writer, value.Influences, WriteVertexInfluence);
        writer.Write(value.VertexInfluenceMapping.Count);
        foreach ((BoneIndexPair key, UArray<uint> vertices) in value.VertexInfluenceMapping)
        {
            writer.Write(key.BoneInd0);
            writer.Write(key.BoneInd1);
            WriteArray(writer, vertices, static (w, entry) => w.Write(entry));
        }

        WriteArray(writer, value.Sections, WriteSection);
        WriteArray(writer, value.Chunks, WriteChunk);
        WriteByteBlob(writer, value.RequiredBones);
        writer.Write(value.Usage);
    }

    private static void WriteVertexInfluence(UE3BinaryWriter writer, FVertexInfluence influence)
    {
        writer.WriteBytes(influence.Bones.Bones);
        writer.WriteBytes(influence.Weights.Weights);
    }

    private static void WriteColorVertexBuffer(UE3BinaryWriter writer, FSkeletalMeshVertexColorBuffer value)
    {
        writer.Write(4);
        writer.Write(value.Colors.Count);
        foreach (FGPUSkinVertexColor color in value.Colors)
            WriteColor(writer, color.VertexColor);
    }

    private static void WriteVertexBuffer(UE3BinaryWriter writer, FSkeletalMeshVertexBuffer value, int numTexCoords, bool useFullPrecisionUvs)
    {
        writer.Write(value.NumTexCoords);
        writer.Write(value.bUseFullPrecisionUVs);
        writer.Write(value.bUsePackedPosition);
        WriteVector(writer, value.MeshExtension);
        WriteVector(writer, value.MeshOrigin);

        if (useFullPrecisionUvs)
        {
            if (value.bUsePackedPosition)
            {
                writer.Write(16 + 4 + (4 * 2 * numTexCoords));
                writer.Write(value.VertsF32UV32.Count);
                foreach (FGPUSkinVertexFloat32Uvs32Xyz vertex in value.VertsF32UV32)
                    WriteGpuVertexFloat32Packed(writer, vertex, numTexCoords);
            }
            else
            {
                writer.Write(16 + 12 + (4 * 2 * numTexCoords));
                writer.Write(value.VertsF32.Count);
                foreach (FGPUSkinVertexFloat32Uvs vertex in value.VertsF32)
                    WriteGpuVertexFloat32(writer, vertex, numTexCoords);
            }
        }
        else
        {
            if (value.bUsePackedPosition)
            {
                writer.Write(16 + 4 + (2 * 2 * numTexCoords));
                writer.Write(value.VertsF16UV32.Count);
                foreach (FGPUSkinVertexFloat16Uvs32Xyz vertex in value.VertsF16UV32)
                    WriteGpuVertexFloat16Packed(writer, vertex, numTexCoords);
            }
            else
            {
                writer.Write(16 + 12 + (2 * 2 * numTexCoords));
                writer.Write(value.VertsF16.Count);
                foreach (FGPUSkinVertexFloat16Uvs vertex in value.VertsF16)
                    WriteGpuVertexFloat16(writer, vertex, numTexCoords);
            }
        }
    }

    private static void WriteGpuVertexFloat32Packed(UE3BinaryWriter writer, FGPUSkinVertexFloat32Uvs32Xyz value, int numTexCoords)
    {
        writer.Write(value.TangentX.Packed);
        writer.Write(value.TangentZ.Packed);
        writer.WriteBytes(value.InfluenceBones);
        writer.WriteBytes(value.InfluenceWeights);
        writer.Write(value.Positon.Packed);
        for (int i = 0; i < numTexCoords; i++)
            WriteVector2(writer, value.UVs[i]);
    }

    private static void WriteGpuVertexFloat32(UE3BinaryWriter writer, FGPUSkinVertexFloat32Uvs value, int numTexCoords)
    {
        writer.Write(value.TangentX.Packed);
        writer.Write(value.TangentZ.Packed);
        writer.WriteBytes(value.InfluenceBones);
        writer.WriteBytes(value.InfluenceWeights);
        WriteVector(writer, value.Positon);
        for (int i = 0; i < numTexCoords; i++)
            WriteVector2(writer, value.UVs[i]);
    }

    private static void WriteGpuVertexFloat16(UE3BinaryWriter writer, FGPUSkinVertexFloat16Uvs value, int numTexCoords)
    {
        writer.Write(value.TangentX.Packed);
        writer.Write(value.TangentZ.Packed);
        writer.WriteBytes(value.InfluenceBones);
        writer.WriteBytes(value.InfluenceWeights);
        WriteVector(writer, value.Positon);
        for (int i = 0; i < numTexCoords; i++)
        {
            writer.Write(value.UVs[i].X.Encoded);
            writer.Write(value.UVs[i].Y.Encoded);
        }
    }

    private static void WriteGpuVertexFloat16Packed(UE3BinaryWriter writer, FGPUSkinVertexFloat16Uvs32Xyz value, int numTexCoords)
    {
        writer.Write(value.TangentX.Packed);
        writer.Write(value.TangentZ.Packed);
        writer.WriteBytes(value.InfluenceBones);
        writer.WriteBytes(value.InfluenceWeights);
        writer.Write(value.Positon.Packed);
        for (int i = 0; i < numTexCoords; i++)
        {
            writer.Write(value.UVs[i].X.Encoded);
            writer.Write(value.UVs[i].Y.Encoded);
        }
    }

    private static void WriteChunk(UE3BinaryWriter writer, FSkelMeshChunk value)
    {
        writer.Write(value.BaseVertexIndex);
        WriteArray(writer, value.RigidVertices, WriteRigidVertex);
        WriteArray(writer, value.SoftVertices, WriteSoftVertex);
        WriteArray(writer, value.BoneMap, static (w, entry) => w.Write(entry));
        writer.Write(value.NumRigidVertices);
        writer.Write(value.NumSoftVertices);
        writer.Write(value.MaxBoneInfluences);
    }

    private static void WriteRigidVertex(UE3BinaryWriter writer, FRigidSkinVertex value)
    {
        WriteVector(writer, value.Position);
        writer.Write(value.TangentX.Packed);
        writer.Write(value.TangentY.Packed);
        writer.Write(value.TangentZ.Packed);
        foreach (FVector2D uv in value.UVs)
            WriteVector2(writer, uv);
        WriteColor(writer, value.Color);
        writer.Write(value.Bone);
    }

    private static void WriteSoftVertex(UE3BinaryWriter writer, FSoftSkinVertex value)
    {
        WriteVector(writer, value.Position);
        writer.Write(value.TangentX.Packed);
        writer.Write(value.TangentY.Packed);
        writer.Write(value.TangentZ.Packed);
        foreach (FVector2D uv in value.UVs)
            WriteVector2(writer, uv);
        WriteColor(writer, value.Color);
        writer.WriteBytes(value.InfluenceBones);
        writer.WriteBytes(value.InfluenceWeights);
    }

    private static void WriteSection(UE3BinaryWriter writer, FSkelMeshSection value)
    {
        writer.Write(value.MaterialIndex);
        writer.Write(value.ChunkIndex);
        writer.Write(value.BaseIndex);
        writer.Write(value.NumTriangles);
        writer.Write(value.TriangleSorting);
    }

    private static void WriteMultiSizeIndexContainer(UE3BinaryWriter writer, FMultiSizeIndexContainer value)
    {
        writer.Write(value.NeedsCPUAccess);
        writer.Write(value.DataTypeSize);

        if (value.DataTypeSize == 2)
        {
            writer.Write(sizeof(ushort));
            writer.Write(value.IndexBuffer.Count);
            foreach (uint index in value.IndexBuffer)
                writer.Write((ushort)index);
        }
        else
        {
            writer.Write(sizeof(uint));
            writer.Write(value.IndexBuffer.Count);
            foreach (uint index in value.IndexBuffer)
                writer.Write(index);
        }
    }

    private static void WriteArray<T>(UE3BinaryWriter writer, IReadOnlyCollection<T> values, Action<UE3BinaryWriter, T> writeElement)
    {
        writer.Write(values.Count);
        foreach (T value in values)
            writeElement(writer, value);
    }

    private static void WriteByteBlob(UE3BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.WriteBytes(bytes);
    }

    private static void WriteRawBulkData(UE3BinaryWriter writer, byte[] bytes, byte[] originalBlob, ICollection<BulkDataPatch> bulkDataPatches)
    {
        if (bytes.Length == 0)
        {
            writer.Write(0x20u);
            writer.Write(0);
            writer.Write(-1);
            writer.Write(-1);
            return;
        }

        if (originalBlob.Length >= 16 && TryExtractInlineBulkPayloadLength(originalBlob, out int originalPayloadLength) && originalPayloadLength == bytes.Length)
        {
            long basePosition = writer.Position;
            writer.WriteBytes(originalBlob);
            bulkDataPatches.Add(new BulkDataPatch(
                checked((int)(basePosition + 12)),
                checked((int)(basePosition + 16))));
            return;
        }

        writer.Write(0u);
        writer.Write(bytes.Length);
        writer.Write(bytes.Length);
        long offsetFieldPosition = writer.Position;
        writer.Write(0);
        bulkDataPatches.Add(new BulkDataPatch(
            checked((int)offsetFieldPosition),
            checked((int)writer.Position)));
        writer.WriteBytes(bytes);
    }

    private static bool TryExtractInlineBulkPayloadLength(byte[] originalBlob, out int length)
    {
        length = 0;
        if (originalBlob.Length < 16)
            return false;

        uint flags = BitConverter.ToUInt32(originalBlob, 0);
        if (flags != 0)
            return false;

        int uncompressedSize = BitConverter.ToInt32(originalBlob, 4);
        int compressedSize = BitConverter.ToInt32(originalBlob, 8);
        if (uncompressedSize < 0 || compressedSize < 0)
            return false;

        if (originalBlob.Length != 16 + compressedSize)
            return false;

        length = uncompressedSize;
        return true;
    }

    private static void WriteVector(UE3BinaryWriter writer, FVector value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteVector2(UE3BinaryWriter writer, FVector2D value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private static void WriteColor(UE3BinaryWriter writer, FColor value)
    {
        writer.Write(value.B);
        writer.Write(value.G);
        writer.Write(value.R);
        writer.Write(value.A);
    }
}

internal sealed record SerializedLodModel(byte[] Bytes, IReadOnlyList<BulkDataPatch> BulkDataPatches);
internal sealed record BulkDataPatch(int OffsetFieldPosition, int DataStartPosition);

internal sealed class UE3BinaryWriter : IDisposable
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;

    public UE3BinaryWriter()
    {
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
    }

    public long Position => _stream.Position;

    public void Write(byte value) => _writer.Write(value);
    public void Write(bool value) => _writer.Write(value ? 1 : 0);
    public void Write(ushort value) => _writer.Write(value);
    public void Write(int value) => _writer.Write(value);
    public void Write(uint value) => _writer.Write(value);
    public void Write(float value) => _writer.Write(value);
    public void WriteBytes(byte[] value) => _writer.Write(value);

    public void PatchUInt32(long offset, uint value)
    {
        long current = _stream.Position;
        _stream.Position = offset;
        _writer.Write(value);
        _stream.Position = current;
    }

    public byte[] ToArray() => _stream.ToArray();

    public void Dispose()
    {
        _writer.Dispose();
        _stream.Dispose();
    }
}
