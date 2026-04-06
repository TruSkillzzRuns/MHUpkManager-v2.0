using UpkManager.Helpers;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Types;

namespace MHUpkManager.MeshImporter;

internal sealed class MeshImportContext
{
    private readonly Dictionary<int, int> _requiredBoneOrder;
    private readonly Dictionary<string, int> _boneNameToIndex;
    private readonly Dictionary<string, int> _materialNameToIndex;

    private MeshImportContext(
        UnrealHeader header,
        UnrealExportTableEntry exportEntry,
        USkeletalMesh skeletalMesh,
        FStaticLODModel lod,
        int lodIndex,
        byte[] rawExportData,
        int objectDataOffset,
        int lodDataOffset,
        int lodDataSize,
        byte[] originalRawPointIndicesBlob)
    {
        Header = header;
        ExportEntry = exportEntry;
        SkeletalMesh = skeletalMesh;
        OriginalLod = lod;
        LodIndex = lodIndex;
        RawExportData = rawExportData;
        ObjectDataOffset = objectDataOffset;
        LodDataOffset = lodDataOffset;
        LodDataSize = lodDataSize;
        OriginalRawPointIndicesBlob = originalRawPointIndicesBlob;
        RequiredBones = [.. lod.RequiredBones];
        NumTexCoords = Math.Max(1, (int)lod.NumTexCoords);
        UseFullPrecisionUvs = lod.VertexBufferGPUSkin.bUseFullPrecisionUVs;
        UsePackedPosition = lod.VertexBufferGPUSkin.bUsePackedPosition;
        HasVertexColors = skeletalMesh.bHasVertexColors;

        _requiredBoneOrder = RequiredBones
            .Select((value, index) => (BoneIndex: (int)value, index))
            .ToDictionary(static x => x.BoneIndex, static x => x.index);

        _boneNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < skeletalMesh.RefSkeleton.Count; i++)
            _boneNameToIndex[skeletalMesh.RefSkeleton[i].Name.Name] = i;

        _materialNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (FSkelMeshSection section in lod.Sections)
        {
            string materialName = $"Material_{section.MaterialIndex}";
            if (!_materialNameToIndex.ContainsKey(materialName))
                _materialNameToIndex[materialName] = section.MaterialIndex;
        }
    }

    public UnrealHeader Header { get; }
    public UnrealExportTableEntry ExportEntry { get; }
    public USkeletalMesh SkeletalMesh { get; }
    public FStaticLODModel OriginalLod { get; }
    public int LodIndex { get; }
    public byte[] RawExportData { get; }
    public int ObjectDataOffset { get; }
    public int LodDataOffset { get; }
    public int LodDataSize { get; }
    public byte[] OriginalRawPointIndicesBlob { get; }
    public IReadOnlyList<byte> RequiredBones { get; }
    public int NumTexCoords { get; }
    public bool UseFullPrecisionUvs { get; }
    public bool UsePackedPosition { get; }
    public bool HasVertexColors { get; }

    public static async Task<MeshImportContext> CreateAsync(
        UnrealHeader header,
        UnrealExportTableEntry exportEntry,
        int lodIndex)
    {
        if (exportEntry.UnrealObject == null)
            await exportEntry.ParseUnrealObject(false, false).ConfigureAwait(false);

        if (exportEntry.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException("Target export is not a parsed USkeletalMesh.");

        if (lodIndex < 0 || lodIndex >= skeletalMesh.LODModels.Count)
            throw new ArgumentOutOfRangeException(nameof(lodIndex), "LOD index is outside the SkeletalMesh LOD range.");

        byte[] rawExportData = exportEntry.UnrealObjectReader.GetBytes();
        int objectDataOffset = FindObjectDataOffset(rawExportData, header, skeletalMesh);
        SkeletalMeshByteLayout layout = SkeletalMeshByteLayout.Read(rawExportData, objectDataOffset, header, skeletalMesh);

        return new MeshImportContext(
            header,
            exportEntry,
            skeletalMesh,
            skeletalMesh.LODModels[lodIndex],
            lodIndex,
            rawExportData,
            objectDataOffset,
            layout.LodRanges[lodIndex].Offset,
            layout.LodRanges[lodIndex].Length,
            layout.LodRanges[lodIndex].RawPointIndicesBlob);
    }

    public int ResolveBoneIndex(string boneName)
    {
        if (_boneNameToIndex.TryGetValue(boneName, out int index))
            return index;

        throw new InvalidOperationException($"FBX bone '{boneName}' does not exist in the original UE3 skeleton.");
    }

    public int ResolveMaterialIndex(string materialName, int fallbackOrder)
    {
        if (!string.IsNullOrWhiteSpace(materialName) && _materialNameToIndex.TryGetValue(materialName, out int mapped))
            return mapped;

        if (fallbackOrder >= 0 && fallbackOrder < OriginalLod.Sections.Count)
            return OriginalLod.Sections[fallbackOrder].MaterialIndex;

        return fallbackOrder >= 0 ? fallbackOrder : 0;
    }

    public IReadOnlyList<int> SortBonesByRequiredOrder(IEnumerable<int> boneIndices)
    {
        return [.. boneIndices
            .Distinct()
            .OrderBy(i => _requiredBoneOrder.TryGetValue(i, out int order) ? order : int.MaxValue)
            .ThenBy(static i => i)];
    }

    private static int FindObjectDataOffset(byte[] rawExportData, UnrealHeader header, USkeletalMesh skeletalMesh)
    {
        ByteArrayReader reader = ByteArrayReader.CreateNew(rawExportData, 0);
        UBuffer buffer = new(reader, header);

        _ = reader.ReadInt32();

        while (true)
        {
            UnrealProperty property = new();
            ResultProperty result = buffer.ReadProperty(property, skeletalMesh);
            if (result != ResultProperty.Success)
            {
                buffer.SetDataOffset();
                return buffer.DataOffset;
            }
        }
    }

    private sealed class SkeletalMeshByteLayout
    {
        public required List<LodByteRange> LodRanges { get; init; }

        public static SkeletalMeshByteLayout Read(byte[] rawExportData, int objectDataOffset, UnrealHeader header, USkeletalMesh skeletalMesh)
        {
            ByteArrayReader reader = ByteArrayReader.CreateNew(rawExportData, objectDataOffset);
            UBuffer buffer = new(reader, header);

            _ = FBoxSphereBounds.ReadData(buffer);
            _ = buffer.ReadArray(UBuffer.ReadObject);
            _ = FVector.ReadData(buffer);
            _ = FRotator.ReadData(buffer);
            _ = buffer.ReadArray(FMeshBone.ReadData);
            _ = buffer.ReadInt32();

            int lodCount = reader.ReadInt32();
            List<LodByteRange> lodRanges = new(lodCount);
            for (int i = 0; i < lodCount; i++)
            {
                int start = reader.CurrentOffset;
                byte[] rawPointBlob = ReadRawPointIndicesBlob(rawExportData, start, header, skeletalMesh);
                _ = FStaticLODModel.ReadData(buffer, skeletalMesh);
                int end = reader.CurrentOffset;
                lodRanges.Add(new LodByteRange(start, end - start, rawPointBlob));
            }

            return new SkeletalMeshByteLayout { LodRanges = lodRanges };
        }

        private static byte[] ReadRawPointIndicesBlob(byte[] rawExportData, int lodOffset, UnrealHeader header, USkeletalMesh skeletalMesh)
        {
            ByteArrayReader reader = ByteArrayReader.CreateNew(rawExportData, lodOffset);
            UBuffer buffer = new(reader, header);

            _ = buffer.ReadArray(FSkelMeshSection.ReadData);
            _ = FMultiSizeIndexContainer.ReadData(buffer);
            _ = buffer.ReadArray(UBuffer.ReadUInt16);
            _ = buffer.ReadArray(FSkelMeshChunk.ReadData);
            _ = buffer.Reader.ReadUInt32();
            _ = buffer.Reader.ReadUInt32();
            _ = buffer.ReadBytes();

            int blobStart = reader.CurrentOffset;
            _ = buffer.ReadBulkData();
            int blobEnd = reader.CurrentOffset;

            return rawExportData[blobStart..blobEnd];
        }
    }

    private readonly record struct LodByteRange(int Offset, int Length, byte[] RawPointIndicesBlob);
}
