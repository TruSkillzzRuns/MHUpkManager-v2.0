using UpkManager.Models.UpkFile.Engine.Mesh;

namespace MHUpkManager.MeshImporter;

internal sealed partial class UE3LodBuilder
{
    private const int MaxAddressableVertexCount = ushort.MaxValue + 1;
    private readonly UE3VertexBuilder _vertexBuilder = new();
    private readonly UE3IndexBuilder _indexBuilder = new();

    public UE3LodModel Build(
        NeutralMesh mesh,
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        IReadOnlyList<SectionBuildInput> sectionInputs = NormalizeImportLayout(mesh, context, normalizedWeights);
        CaptureLayoutDiagnostics(mesh, context, sectionInputs);

        List<FSkelMeshSection> sections = [];
        FSkelMeshChunk[] chunks = new FSkelMeshChunk[context.OriginalLod.Chunks.Count];
        List<ushort> allIndices = [];
        List<FGPUSkinVertexBase> gpuVertices = [];
        List<FVertexInfluence> allInfluences = [];
        HashSet<int> activeBones = [];

        int globalVertexStart = 0;

        foreach (SectionBuildInput input in sectionInputs)
        {
            FSkelMeshSection originalSection = context.OriginalLod.Sections[input.OriginalSectionIndex];
            BuiltSectionData builtSection = input.PreserveOriginal
                ? BuildPreservedSection(originalSection, context, globalVertexStart)
                : BuildImportedSection(input, normalizedWeights, context, globalVertexStart);

            if (globalVertexStart + builtSection.VertexCount > MaxAddressableVertexCount)
            {
                throw new InvalidOperationException(
                    $"UE3 skeletal mesh vertex buffer exceeded {MaxAddressableVertexCount} vertices while rebuilding LOD {context.LodIndex}. " +
                    $"Current layout reached {globalVertexStart + builtSection.VertexCount} vertices. Reduce mesh complexity before UPK replacement.");
            }

            if (chunks[originalSection.ChunkIndex] != null)
                throw new InvalidOperationException("Shared chunk sections are not supported by the importer yet.");

            chunks[originalSection.ChunkIndex] = builtSection.Chunk;

            sections.Add(new FSkelMeshSection
            {
                MaterialIndex = originalSection.MaterialIndex,
                ChunkIndex = originalSection.ChunkIndex,
                BaseIndex = (uint)allIndices.Count,
                NumTriangles = (uint)(builtSection.Indices.Count / 3),
                TriangleSorting = originalSection.TriangleSorting
            });

            foreach (ushort boneIndex in builtSection.Chunk.BoneMap)
                activeBones.Add(boneIndex);

            allIndices.AddRange(builtSection.Indices);
            gpuVertices.AddRange(builtSection.GpuVertices);
            allInfluences.AddRange(builtSection.Influences);

            globalVertexStart += builtSection.VertexCount;
        }

        return new UE3LodModel(new FStaticLODModel
        {
            Sections = [.. sections],
            MultiSizeIndexContainer = new FMultiSizeIndexContainer
            {
                NeedsCPUAccess = true,
                DataTypeSize = 2,
                IndexBuffer = [.. allIndices.Select(static i => (uint)i)]
            },
            ActiveBoneIndices = [.. context.SortBonesByRequiredOrder(activeBones).Select(static i => (ushort)i)],
            Chunks = [.. chunks],
            NumVertices = (uint)gpuVertices.Count,
            RequiredBones = [.. context.RequiredBones],
            RawPointIndices = BuildRawPointIndices(context, gpuVertices.Count),
            NumTexCoords = (uint)context.NumTexCoords,
            VertexBufferGPUSkin = BuildVertexBuffer(context, gpuVertices),
            ColorVertexBuffer = context.HasVertexColors ? BuildColorBuffer(gpuVertices.Count) : null,
            VertexInfluences = [BuildInfluenceBuffer(allInfluences, sections, chunks, context)],
            AdjacencyMultiSizeIndexContainer = new FMultiSizeIndexContainer
            {
                NeedsCPUAccess = false,
                DataTypeSize = 2,
                IndexBuffer = []
            },
            Size = 0
        });
    }

    private sealed record SectionBuildInput(
        int OriginalSectionIndex,
        NeutralSection Section,
        IReadOnlyList<int> SourceVertexIndices,
        bool PreserveOriginal,
        IReadOnlyList<int> SourceImportedSectionIndices,
        string Behavior);
    private sealed record BuiltSectionData(FSkelMeshChunk Chunk, IReadOnlyList<ushort> Indices, IReadOnlyList<FGPUSkinVertexBase> GpuVertices, IReadOnlyList<FVertexInfluence> Influences, int VertexCount);
}
