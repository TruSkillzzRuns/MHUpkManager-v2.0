using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: MeshParseProbe <upkPath> <exportPath>");
    return 2;
}

string upkPath = args[0];
string exportPath = args[1];

DumpRawHeader(upkPath);

try
{
    UpkFileRepository repository = new();
    var header = await repository.LoadUpkFile(upkPath).ConfigureAwait(false);
    await header.ReadHeaderAsync(null).ConfigureAwait(false);

    int minExportOffset = header.ExportTable.Count > 0
        ? header.ExportTable.Min(e => e.SerialDataOffset)
        : -1;
    int minCompressedOffset = header.CompressedChunks.Count > 0
        ? header.CompressedChunks.Min(c => c.UncompressedOffset)
        : -1;

    Console.WriteLine($"HEADER Size={header.Size} MinExportOffset={minExportOffset} MinCompressedOffset={minCompressedOffset} CompressionFlags={header.CompressionFlags} CompressionChunkCount={header.CompressedChunks.Count}");

    var export = header.ExportTable
        .FirstOrDefault(e => string.Equals(e.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase));

    if (export == null)
    {
        Console.Error.WriteLine($"Export not found: {exportPath}");
        return 3;
    }

    await export.ParseUnrealObject(false, false).ConfigureAwait(false);

    if (export.UnrealObject is not IUnrealObject unrealObject)
    {
        Console.Error.WriteLine("Export parsed but UnrealObject wrapper was null.");
        return 4;
    }

    Console.WriteLine($"PARSE_OK {unrealObject.UObject.GetType().FullName}");
    if (unrealObject.UObject is USkeletalMesh skeletalMesh && skeletalMesh.LODModels.Count > 0)
    {
        DumpSkeletalLod(skeletalMesh.LODModels[0]);
        DumpSkeletalMaterials(skeletalMesh);
    }
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
    Exception? inner = ex.InnerException;
    while (inner != null)
    {
        Console.WriteLine("INNER:");
        Console.WriteLine(inner.ToString());
        inner = inner.InnerException;
    }

    return 1;
}

static void DumpRawHeader(string path)
{
    byte[] fileBytes = File.ReadAllBytes(path);
    using MemoryStream stream = new(fileBytes, writable: false);
    using BinaryReader reader = new(stream);

    uint signature = reader.ReadUInt32();
    ushort version = reader.ReadUInt16();
    ushort licensee = reader.ReadUInt16();
    int size = reader.ReadInt32();
    int groupSize = reader.ReadInt32();

    if (groupSize < 0)
    {
        stream.Position += (-groupSize) * 2L;
    }
    else if (groupSize > 0)
    {
        stream.Position += groupSize;
    }

    long packageFlagsOffset = stream.Position;
    uint packageFlags = reader.ReadUInt32();
    int nameCount = reader.ReadInt32();
    int nameOffset = reader.ReadInt32();
    int exportCount = reader.ReadInt32();
    int exportOffset = reader.ReadInt32();
    int importCount = reader.ReadInt32();
    int importOffset = reader.ReadInt32();
    int dependsOffset = reader.ReadInt32();
    int importExportGuidsOffset = reader.ReadInt32();
    int importGuidsCount = reader.ReadInt32();
    int exportGuidsCount = reader.ReadInt32();
    int thumbnailOffset = reader.ReadInt32();
    stream.Position += 16;
    int generationCount = reader.ReadInt32();
    stream.Position += generationCount * 12L;
    uint engineVersion = reader.ReadUInt32();
    uint cookerVersion = reader.ReadUInt32();
    long compressionFlagsOffset = stream.Position;
    uint compressionFlags = reader.ReadUInt32();
    int compressionTableCount = reader.ReadInt32();
    long compressionTableOffset = stream.Position;
    stream.Position += compressionTableCount * 16L;
    uint packageSource = reader.ReadUInt32();
    int additionalPackagesCount = reader.ReadInt32();
    for (int i = 0; i < additionalPackagesCount; i++)
    {
        int packageNameSize = reader.ReadInt32();
        if (packageNameSize < 0)
            stream.Position += (-packageNameSize) * 2L;
        else if (packageNameSize > 0)
            stream.Position += packageNameSize;
    }
    long textureAllocationsOffset = stream.Position;
    int textureAllocationsCount = reader.ReadInt32();
    List<string> textureTypes = [];
    for (int i = 0; i < textureAllocationsCount; i++)
    {
        long typeOffset = stream.Position;
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int mipMapsCount = reader.ReadInt32();
        uint textureFormat = reader.ReadUInt32();
        uint textureCreateFlags = reader.ReadUInt32();
        int textureIndexCount = reader.ReadInt32();
        stream.Position += textureIndexCount * 4L;
        textureTypes.Add($"Type{i}@{typeOffset}: {width}x{height} Mips={mipMapsCount} Format=0x{textureFormat:X8} CreateFlags=0x{textureCreateFlags:X8} Indices={textureIndexCount}");
    }

    Console.WriteLine(
        $"RAW_HEADER Signature=0x{signature:X8} Version={version} Licensee={licensee} Size={size} PackageFlags=0x{packageFlags:X8} NameCount={nameCount} NameOffset={nameOffset} ExportCount={exportCount} ExportOffset={exportOffset} ImportCount={importCount} ImportOffset={importOffset} DependsOffset={dependsOffset} ImportExportGuidsOffset={importExportGuidsOffset} ImportGuidsCount={importGuidsCount} ExportGuidsCount={exportGuidsCount} ThumbnailOffset={thumbnailOffset} GenerationCount={generationCount} EngineVersion={engineVersion} CookerVersion={cookerVersion} CompressionFlags=0x{compressionFlags:X8} CompressionTableCount={compressionTableCount} CompressionTableOffset={compressionTableOffset} PackageSource=0x{packageSource:X8} AdditionalPackagesCount={additionalPackagesCount} TextureAllocationsOffset={textureAllocationsOffset} TextureAllocationsCount={textureAllocationsCount} PackageFlagsOffset={packageFlagsOffset} CompressionFlagsOffset={compressionFlagsOffset}");
    foreach (string textureType in textureTypes)
        Console.WriteLine(textureType);

    TryParseNameTable(fileBytes, nameOffset, nameCount);
}

static void TryParseNameTable(byte[] fileBytes, int nameOffset, int nameCount)
{
    int cursor = nameOffset;
    for (int i = 0; i < nameCount; i++)
    {
        if (cursor + 4 > fileBytes.Length)
        {
            Console.WriteLine($"NAME_FAIL entry={i} cursor={cursor} stage=size");
            return;
        }

        int size = BitConverter.ToInt32(fileBytes, cursor);
        cursor += 4;

        if (size < 0)
        {
            int byteCount = checked((-size) * 2);
            if (cursor + byteCount > fileBytes.Length)
            {
                Console.WriteLine($"NAME_FAIL entry={i} cursor={cursor} unicodeBytes={byteCount}");
                return;
            }

            cursor += byteCount;
        }
        else if (size > 0)
        {
            int byteCount = size - 1;
            if (cursor + byteCount + 1 > fileBytes.Length)
            {
                Console.WriteLine($"NAME_FAIL entry={i} cursor={cursor} ansiBytes={byteCount}");
                return;
            }

            cursor += byteCount + 1;
        }

        if (cursor + 8 > fileBytes.Length)
        {
            Console.WriteLine($"NAME_FAIL entry={i} cursor={cursor} stage=flags");
            return;
        }

        cursor += 8;
    }

    Console.WriteLine($"NAME_OK finalCursor={cursor}");
}

static void DumpSkeletalLod(FStaticLODModel lod)
{
    Console.WriteLine($"LOD NumVertices={lod.NumVertices} Sections={lod.Sections.Count} Chunks={lod.Chunks.Count} ActiveBoneIndices={lod.ActiveBoneIndices.Count} RequiredBones={lod.RequiredBones?.Length ?? -1}");

    for (int i = 0; i < lod.Chunks.Count; i++)
    {
        FSkelMeshChunk chunk = lod.Chunks[i];
        Console.WriteLine($"CHUNK[{i}] BaseVertexIndex={chunk.BaseVertexIndex} Rigid={chunk.NumRigidVertices} Soft={chunk.NumSoftVertices} MaxInfluences={chunk.MaxBoneInfluences} BoneMapCount={chunk.BoneMap.Count} BoneMap={string.Join(',', chunk.BoneMap)}");
    }

    if (lod.VertexInfluences == null || lod.VertexInfluences.Count == 0)
    {
        Console.WriteLine("VERTEX_INFLUENCES None");
        return;
    }

    FSkeletalMeshVertexInfluences vi = lod.VertexInfluences[0];
    Console.WriteLine($"VERTEX_INFLUENCES Usage={vi.Usage} InfluenceCount={vi.Influences.Count} MappingCount={vi.VertexInfluenceMapping.Count} Sections={vi.Sections.Count} Chunks={vi.Chunks.Count} RequiredBones={vi.RequiredBones?.Length ?? -1}");

    int influenceDumpCount = Math.Min(8, vi.Influences.Count);
    for (int i = 0; i < influenceDumpCount; i++)
    {
        FVertexInfluence influence = vi.Influences[i];
        Console.WriteLine($"INFLUENCE[{i}] Bones={string.Join(',', influence.Bones.Bones)} Weights={string.Join(',', influence.Weights.Weights)}");
    }

    int mappingIndex = 0;
    foreach ((BoneIndexPair key, var vertices) in vi.VertexInfluenceMapping)
    {
        Console.WriteLine($"MAPPING[{mappingIndex}] Key={key.BoneInd0},{key.BoneInd1} Vertices={string.Join(',', vertices.Take(8))}");
        mappingIndex++;
        if (mappingIndex >= 8)
            break;
    }
}

static void DumpSkeletalMaterials(USkeletalMesh skeletalMesh)
{
    Console.WriteLine($"MATERIAL_SLOT_COUNT {skeletalMesh.Materials.Count}");
    if (skeletalMesh.LODModels.Count == 0)
        return;

    FStaticLODModel lod = skeletalMesh.LODModels[0];
    for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
    {
        FSkelMeshSection section = lod.Sections[sectionIndex];
        FObject materialRef = section.MaterialIndex >= 0 && section.MaterialIndex < skeletalMesh.Materials.Count
            ? skeletalMesh.Materials[section.MaterialIndex]
            : null;
        Console.WriteLine($"SECTION[{sectionIndex}] MaterialIndex={section.MaterialIndex} MaterialPath={materialRef?.GetPathName() ?? "<null>"}");

        if (materialRef?.LoadObject<UMaterialInstanceConstant>() is not UMaterialInstanceConstant mic)
        {
            Console.WriteLine($"SECTION[{sectionIndex}] MaterialResolve=<null>");
            continue;
        }

        Console.WriteLine($"SECTION[{sectionIndex}] MaterialResolve=UMaterialInstanceConstant Parent={mic.Parent?.GetPathName() ?? "<null>"}");
        DumpTextureParameter(sectionIndex, mic, "Diffuse");
        DumpTextureParameter(sectionIndex, mic, "Norm");
        DumpTextureParameter(sectionIndex, mic, "specmult_specpow_skinmask");
        DumpTextureParameter(sectionIndex, mic, "emissivespecpow");
        DumpTextureParameter(sectionIndex, mic, "specmultrimmaskrefl");
        DumpTextureParameter(sectionIndex, mic, "SpecColor");

        DumpScalarParameter(sectionIndex, mic, "normalstrength");
        DumpScalarParameter(sectionIndex, mic, "specmult");
        DumpScalarParameter(sectionIndex, mic, "specmult_lq");
        DumpScalarParameter(sectionIndex, mic, "specularpower");
        DumpScalarParameter(sectionIndex, mic, "specularpowermask");
        DumpScalarParameter(sectionIndex, mic, "reflectionmult");
        DumpScalarParameter(sectionIndex, mic, "rimcolormult");
        DumpScalarParameter(sectionIndex, mic, "rimfalloff");

        DumpVectorParameter(sectionIndex, mic, "lambertambient");
        DumpVectorParameter(sectionIndex, mic, "shadowambientcolor");
        DumpVectorParameter(sectionIndex, mic, "filllightcolor");
        DumpVectorParameter(sectionIndex, mic, "specularcolor");
        DumpVectorParameter(sectionIndex, mic, "diffusecolor");

        if (mic.Parent?.LoadObject<UMaterial>() is UMaterial parentMaterial)
        {
            Console.WriteLine($"SECTION[{sectionIndex}] ParentMaterial BlendMode={parentMaterial.BlendMode} TwoSided={parentMaterial.TwoSided} OpacityMaskClipValue={parentMaterial.OpacityMaskClipValue} LightingModel={parentMaterial.LightingModel}");

            FMaterialResource resource = parentMaterial.MaterialResource?.FirstOrDefault(static value => value != null);
            if (resource != null)
            {
                Console.WriteLine($"SECTION[{sectionIndex}] ParentResource BlendModeOverrideValue={resource.BlendModeOverrideValue} bIsBlendModeOverrided={resource.bIsBlendModeOverrided} bIsMaskedOverrideValue={resource.bIsMaskedOverrideValue} UniformExpressionTextures={resource.UniformExpressionTextures?.Count ?? 0}");
                if (resource.UniformExpressionTextures != null)
                {
                    for (int i = 0; i < resource.UniformExpressionTextures.Count; i++)
                    {
                        Console.WriteLine($"SECTION[{sectionIndex}] ParentUniformTexture[{i}]={resource.UniformExpressionTextures[i]?.GetPathName() ?? "<null>"}");
                    }
                }
            }
        }

        if (mic.bHasStaticPermutationResource && mic.StaticPermutationResources.Length > 0 && mic.StaticPermutationResources[0] != null)
        {
            FMaterialResource resource = mic.StaticPermutationResources[0];
            Console.WriteLine($"SECTION[{sectionIndex}] StaticPermutation BlendModeOverrideValue={resource.BlendModeOverrideValue} bIsBlendModeOverrided={resource.bIsBlendModeOverrided} bIsMaskedOverrideValue={resource.bIsMaskedOverrideValue}");
        }
    }
}

static void DumpTextureParameter(int sectionIndex, UMaterialInstanceConstant mic, string parameterName)
{
    FObject texture = mic.GetTextureParameterValue(parameterName);
    Console.WriteLine($"SECTION[{sectionIndex}] TextureParam {parameterName}={texture?.GetPathName() ?? "<null>"}");
}

static void DumpScalarParameter(int sectionIndex, UMaterialInstanceConstant mic, string parameterName)
{
    float? value = mic.GetScalarParameterValue(parameterName);
    Console.WriteLine($"SECTION[{sectionIndex}] ScalarParam {parameterName}={(value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "<null>")}");
}

static void DumpVectorParameter(int sectionIndex, UMaterialInstanceConstant mic, string parameterName)
{
    var value = mic.GetVectorParameterValue(parameterName);
    Console.WriteLine($"SECTION[{sectionIndex}] VectorParam {parameterName}={(value.HasValue ? value.Value.ToString() : "<null>")}");
}
