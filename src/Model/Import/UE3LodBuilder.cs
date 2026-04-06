using System.Numerics;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Types;

namespace MHUpkManager.Model.Import;

internal sealed class UE3LodBuilder
{
    private readonly UE3VertexBuilder _vertexBuilder = new();
    private readonly UE3IndexBuilder _indexBuilder = new();

    public FStaticLODModel Build(
        NeutralMesh mesh,
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        IReadOnlyList<SectionBuildInput> sectionInputs = NormalizeImportLayout(mesh, context, normalizedWeights);
        CaptureLayoutDiagnostics(mesh, sectionInputs);

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

        return new FStaticLODModel
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
        };
    }

    private BuiltSectionData BuildImportedSection(
        SectionBuildInput input,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights,
        MeshImportContext context,
        int globalVertexStart)
    {
        NeutralSection importedSection = input.Section
            ?? throw new InvalidOperationException("Imported section data was missing.");

        IReadOnlyList<int> chunkBoneMap = BuildBoneMap(input, normalizedWeights, context);
        BuiltVertexData builtVertices = _vertexBuilder.Build(importedSection, normalizedWeights, context, chunkBoneMap, input.SourceVertexIndices);
        ushort[] builtIndices = _indexBuilder.Build(importedSection, globalVertexStart);

        return new BuiltSectionData(
            new FSkelMeshChunk
            {
                BaseVertexIndex = (uint)globalVertexStart,
                RigidVertices = [],
                SoftVertices = [.. builtVertices.SoftVertices],
                BoneMap = [.. chunkBoneMap.Select(static x => (ushort)x)],
                NumRigidVertices = 0,
                NumSoftVertices = builtVertices.SoftVertices.Count,
                MaxBoneInfluences = 4
            },
            builtIndices,
            builtVertices.GpuVertices,
            builtVertices.Influences,
            importedSection.Vertices.Count);
    }

    private static BuiltSectionData BuildPreservedSection(
        FSkelMeshSection originalSection,
        MeshImportContext context,
        int globalVertexStart)
    {
        FSkelMeshChunk originalChunk = context.OriginalLod.Chunks[originalSection.ChunkIndex];
        int originalBaseVertexIndex = checked((int)originalChunk.BaseVertexIndex);
        int vertexCount = originalChunk.NumRigidVertices + originalChunk.NumSoftVertices;
        int originalIndexStart = checked((int)originalSection.BaseIndex);
        int originalIndexCount = checked((int)originalSection.NumTriangles * 3);

        ushort[] remappedIndices = new ushort[originalIndexCount];
        for (int i = 0; i < originalIndexCount; i++)
        {
            int originalVertexIndex = checked((int)context.OriginalLod.MultiSizeIndexContainer.IndexBuffer[originalIndexStart + i]);
            int localVertexIndex = originalVertexIndex - originalBaseVertexIndex;
            if (localVertexIndex < 0 || localVertexIndex >= vertexCount)
                throw new InvalidOperationException("Original section index buffer references vertices outside its chunk.");

            remappedIndices[i] = checked((ushort)(globalVertexStart + localVertexIndex));
        }

        IReadOnlyList<FGPUSkinVertexBase> originalGpuVertices = [.. context.OriginalLod.VertexBufferGPUSkin.VertexData];
        if (originalBaseVertexIndex + vertexCount > originalGpuVertices.Count)
            throw new InvalidOperationException("Original chunk vertex range exceeds the GPU vertex buffer.");

        return new BuiltSectionData(
            new FSkelMeshChunk
            {
                BaseVertexIndex = (uint)globalVertexStart,
                RigidVertices = [.. originalChunk.RigidVertices.Select(CloneRigidVertex)],
                SoftVertices = [.. originalChunk.SoftVertices.Select(CloneSoftVertex)],
                BoneMap = [.. originalChunk.BoneMap],
                NumRigidVertices = originalChunk.NumRigidVertices,
                NumSoftVertices = originalChunk.NumSoftVertices,
                MaxBoneInfluences = originalChunk.MaxBoneInfluences
            },
            remappedIndices,
            [.. originalGpuVertices.Skip(originalBaseVertexIndex).Take(vertexCount).Select(CloneGpuVertex)],
            BuildPreservedInfluences(originalChunk),
            vertexCount);
    }

    private static IReadOnlyList<int> BuildBoneMap(
        SectionBuildInput input,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights,
        MeshImportContext context)
    {
        HashSet<int> usedBones = [];
        for (int i = 0; i < input.SourceVertexIndices.Count; i++)
        {
            foreach (NormalizedWeight weight in normalizedWeights[input.SourceVertexIndices[i]])
            {
                if (weight.Weight > 0)
                    usedBones.Add(weight.BoneIndex);
            }
        }

        IReadOnlyList<int> ordered = context.SortBonesByRequiredOrder(usedBones);
        if (ordered.Count > byte.MaxValue)
            throw new InvalidOperationException("A UE3 chunk cannot reference more than 255 bones.");

        return ordered;
    }

    private static IReadOnlyList<SectionBuildInput> NormalizeImportLayout(
        NeutralMesh mesh,
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        List<SectionBuildInput> direct = TryUseSectionsDirect(mesh, context);
        if (direct.Count > 0)
        {
            mesh.LayoutStrategy = "Direct";
            return direct;
        }

        List<SectionBuildInput> partial = TryMatchSectionsWithPreservation(mesh, context);
        if (partial.Count > 0)
        {
            mesh.LayoutStrategy = "PartialPreservation";
            return partial;
        }

        if (mesh.Sections.Count == 1)
        {
            mesh.LayoutStrategy = "SingleSectionSplit";
            return SplitSingleSection(mesh, mesh.Sections[0], context, normalizedWeights);
        }

        throw new InvalidOperationException($"FBX section count ({mesh.Sections.Count}) does not match original LOD section count ({context.OriginalLod.Sections.Count}).");
    }

    private static void CaptureLayoutDiagnostics(NeutralMesh mesh, IReadOnlyList<SectionBuildInput> sectionInputs)
    {
        mesh.LayoutSections.Clear();
        foreach (SectionBuildInput input in sectionInputs)
        {
            mesh.LayoutSections.Add(new LayoutSectionDiagnostic
            {
                OriginalSectionIndex = input.OriginalSectionIndex,
                ImportedVertexCount = input.Section?.Vertices.Count ?? 0,
                ImportedTriangleCount = input.Section == null ? 0 : input.Section.Indices.Count / 3,
                PreserveOriginal = input.PreserveOriginal
            });
        }
    }

    private static List<SectionBuildInput> TryUseSectionsDirect(NeutralMesh mesh, MeshImportContext context)
    {
        if (mesh.Sections.Count != context.OriginalLod.Sections.Count)
            return [];

        List<SectionBuildInput> inputs = [];
        int flatVertexOffset = 0;
        for (int i = 0; i < context.OriginalLod.Sections.Count; i++)
        {
            FSkelMeshSection originalSection = context.OriginalLod.Sections[i];
            NeutralSection importedSection = mesh.Sections[i];
            int importedTriangles = importedSection.Indices.Count / 3;

            if (importedSection.Indices.Count % 3 != 0)
                throw new InvalidOperationException($"Imported section {i} does not contain complete triangles.");

            if (importedTriangles != originalSection.NumTriangles)
                throw new InvalidOperationException($"Imported section {i} triangle count ({importedTriangles}) does not match original section triangle count ({originalSection.NumTriangles}).");

            inputs.Add(new SectionBuildInput(i, importedSection, [.. Enumerable.Range(flatVertexOffset, importedSection.Vertices.Count)], false));
            flatVertexOffset += importedSection.Vertices.Count;
        }

        return inputs;
    }

    private static List<SectionBuildInput> TryMatchSectionsWithPreservation(NeutralMesh mesh, MeshImportContext context)
    {
        if (mesh.Sections.Count == 0 || mesh.Sections.Count >= context.OriginalLod.Sections.Count)
            return [];

        int totalOriginalTriangles = context.OriginalLod.Sections.Sum(static s => (int)s.NumTriangles);
        if (mesh.Sections.Any(section => section.Indices.Count / 3 == totalOriginalTriangles))
            return [];

        NeutralSection placeholderSection = new();
        int[] importedVertexOffsets = new int[mesh.Sections.Count];
        int flatVertexOffset = 0;
        for (int i = 0; i < mesh.Sections.Count; i++)
        {
            importedVertexOffsets[i] = flatVertexOffset;
            flatVertexOffset += mesh.Sections[i].Vertices.Count;
        }

        SectionBuildInput[] mapped = new SectionBuildInput[context.OriginalLod.Sections.Count];
        HashSet<int> usedOriginalSections = [];

        for (int importedIndex = 0; importedIndex < mesh.Sections.Count; importedIndex++)
        {
            NeutralSection importedSection = mesh.Sections[importedIndex];
            if (importedSection.Indices.Count % 3 != 0)
                throw new InvalidOperationException($"Imported section {importedIndex} does not contain complete triangles.");

            int originalSectionIndex = FindBestOriginalSection(importedSection, importedIndex, usedOriginalSections, context);
            if (originalSectionIndex < 0)
                return [];

            usedOriginalSections.Add(originalSectionIndex);
            mapped[originalSectionIndex] = new SectionBuildInput(
                originalSectionIndex,
                importedSection,
                [.. Enumerable.Range(importedVertexOffsets[importedIndex], importedSection.Vertices.Count)],
                false);
        }

        List<SectionBuildInput> results = [];
        for (int originalIndex = 0; originalIndex < mapped.Length; originalIndex++)
        {
            results.Add(mapped[originalIndex] ?? new SectionBuildInput(originalIndex, placeholderSection, [], true));
        }

        return results;
    }

    private static int FindBestOriginalSection(
        NeutralSection importedSection,
        int importedIndex,
        IReadOnlySet<int> usedOriginalSections,
        MeshImportContext context)
    {
        int importedTriangles = importedSection.Indices.Count / 3;
        int desiredMaterialIndex = context.ResolveMaterialIndex(importedSection.MaterialName, importedSection.ImportedMaterialIndex);

        List<int> exactMaterialAndCount = [];
        List<int> exactCount = [];
        List<int> materialOnly = [];

        for (int i = 0; i < context.OriginalLod.Sections.Count; i++)
        {
            if (usedOriginalSections.Contains(i))
                continue;

            FSkelMeshSection originalSection = context.OriginalLod.Sections[i];
            if (originalSection.MaterialIndex == desiredMaterialIndex)
                materialOnly.Add(i);

            if (originalSection.NumTriangles == importedTriangles)
            {
                exactCount.Add(i);
                if (originalSection.MaterialIndex == desiredMaterialIndex)
                    exactMaterialAndCount.Add(i);
            }
        }

        if (exactMaterialAndCount.Count == 1)
            return exactMaterialAndCount[0];

        if (materialOnly.Count == 1)
            return materialOnly[0];

        if (exactCount.Count == 1)
            return exactCount[0];

        if (importedIndex < context.OriginalLod.Sections.Count && !usedOriginalSections.Contains(importedIndex))
            return importedIndex;

        return -1;
    }

    private static List<SectionBuildInput> SplitSingleSection(
        NeutralMesh mesh,
        NeutralSection combinedSection,
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        mesh.UsedSingleSectionSplit = true;
        mesh.SplitSourceVertexCount = combinedSection.Vertices.Count;
        int totalOriginalTriangles = context.OriginalLod.Sections.Sum(static s => (int)s.NumTriangles);
        int totalImportedTriangles = combinedSection.Indices.Count / 3;
        if (combinedSection.Indices.Count % 3 != 0)
            throw new InvalidOperationException("Imported section does not contain complete triangles.");

        if (totalImportedTriangles != totalOriginalTriangles)
            throw new InvalidOperationException($"Imported triangle count ({totalImportedTriangles}) does not match original LOD triangle count ({totalOriginalTriangles}).");

        List<SectionBuildInput> affinityResults = TrySplitSingleSectionByBoneAffinity(
            mesh,
            combinedSection,
            context,
            normalizedWeights);
        if (affinityResults != null)
            return affinityResults;

        return SplitSingleSectionByTriangleOrder(mesh, combinedSection, context);
    }

    private static List<SectionBuildInput> TrySplitSingleSectionByBoneAffinity(
        NeutralMesh mesh,
        NeutralSection combinedSection,
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        HashSet<int>[] sectionBoneSets = [.. context.OriginalLod.Sections
            .Select(section => context.OriginalLod.Chunks[section.ChunkIndex])
            .Select(static chunk => chunk.BoneMap.Select(static bone => (int)bone).ToHashSet())];

        if (sectionBoneSets.All(static set => set.Count == 0))
            return null;

        float[][] vertexAffinities = new float[combinedSection.Vertices.Count][];
        bool hasDiscriminatoryAffinity = false;
        for (int i = 0; i < combinedSection.Vertices.Count; i++)
        {
            float[] scores = new float[sectionBoneSets.Length];
            IReadOnlyList<NormalizedWeight> weights = normalizedWeights[i];

            for (int sectionIndex = 0; sectionIndex < sectionBoneSets.Length; sectionIndex++)
            {
                float score = 0.0f;
                foreach (NormalizedWeight weight in weights)
                {
                    if (weight.Weight > 0 && sectionBoneSets[sectionIndex].Contains(weight.BoneIndex))
                        score += weight.Weight;
                }

                scores[sectionIndex] = score;
            }

            float maxScore = scores.Max();
            if (maxScore > 0.0f && scores.Count(score => Math.Abs(score - maxScore) <= 0.5f) == 1)
                hasDiscriminatoryAffinity = true;

            vertexAffinities[i] = scores;
        }

        if (!hasDiscriminatoryAffinity)
            return null;

        List<int>[] sectionTriangleIndices = Enumerable.Range(0, context.OriginalLod.Sections.Count)
            .Select(static _ => new List<int>())
            .ToArray();

        int triangleCount = combinedSection.Indices.Count / 3;
        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
        {
            int indexStart = triangleIndex * 3;
            int a = combinedSection.Indices[indexStart];
            int b = combinedSection.Indices[indexStart + 1];
            int c = combinedSection.Indices[indexStart + 2];

            int bestSectionIndex = 0;
            float bestScore = float.MinValue;

            for (int sectionIndex = 0; sectionIndex < context.OriginalLod.Sections.Count; sectionIndex++)
            {
                float score = vertexAffinities[a][sectionIndex] + vertexAffinities[b][sectionIndex] + vertexAffinities[c][sectionIndex];
                if (score > bestScore + 0.5f)
                {
                    bestScore = score;
                    bestSectionIndex = sectionIndex;
                }
                else if (Math.Abs(score - bestScore) <= 0.5f)
                {
                    uint originalTriangles = context.OriginalLod.Sections[sectionIndex].NumTriangles;
                    uint currentBestOriginalTriangles = context.OriginalLod.Sections[bestSectionIndex].NumTriangles;
                    if (originalTriangles > currentBestOriginalTriangles)
                        bestSectionIndex = sectionIndex;
                }
            }

            sectionTriangleIndices[bestSectionIndex].Add(triangleIndex);
        }

        if (sectionTriangleIndices.Any(static list => list.Count == 0))
            return null;

        mesh.SplitStrategy = "BoneAffinity";
        return BuildSplitSectionsFromTriangleAssignments(mesh, combinedSection, sectionTriangleIndices);
    }

    private static List<SectionBuildInput> SplitSingleSectionByTriangleOrder(
        NeutralMesh mesh,
        NeutralSection combinedSection,
        MeshImportContext context)
    {
        mesh.SplitStrategy = "TriangleOrder";
        List<int>[] sectionTriangleIndices = [];
        int triangleCursor = 0;

        foreach (FSkelMeshSection originalSection in context.OriginalLod.Sections)
        {
            int triangleCount = checked((int)originalSection.NumTriangles);
            List<int> triangles = new(triangleCount);
            for (int i = 0; i < triangleCount; i++)
                triangles.Add(triangleCursor + i);

            Array.Resize(ref sectionTriangleIndices, sectionTriangleIndices.Length + 1);
            sectionTriangleIndices[^1] = triangles;
            triangleCursor += triangleCount;
        }

        return BuildSplitSectionsFromTriangleAssignments(mesh, combinedSection, sectionTriangleIndices);
    }

    private static List<SectionBuildInput> BuildSplitSectionsFromTriangleAssignments(
        NeutralMesh mesh,
        NeutralSection combinedSection,
        IReadOnlyList<List<int>> sectionTriangleIndices)
    {
        List<SectionBuildInput> results = [];
        Dictionary<int, int> sourceVertexUsageCount = [];

        for (int sectionIndex = 0; sectionIndex < sectionTriangleIndices.Count; sectionIndex++)
        {
            Dictionary<int, int> vertexMap = [];
            List<int> sourceVertexIndices = [];
            NeutralSection splitSection = new()
            {
                Name = combinedSection.Name,
                MaterialName = combinedSection.MaterialName,
                ImportedMaterialIndex = combinedSection.ImportedMaterialIndex
            };

            foreach (int triangleIndex in sectionTriangleIndices[sectionIndex])
            {
                int indexStart = triangleIndex * 3;
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertexIndex = combinedSection.Indices[indexStart + corner];
                    if (!vertexMap.TryGetValue(sourceVertexIndex, out int remappedIndex))
                    {
                        remappedIndex = splitSection.Vertices.Count;
                        vertexMap[sourceVertexIndex] = remappedIndex;
                        splitSection.Vertices.Add(combinedSection.Vertices[sourceVertexIndex]);
                        sourceVertexIndices.Add(sourceVertexIndex);
                        sourceVertexUsageCount[sourceVertexIndex] = sourceVertexUsageCount.TryGetValue(sourceVertexIndex, out int usageCount)
                            ? usageCount + 1
                            : 1;
                    }

                    splitSection.Indices.Add(remappedIndex);
                }
            }

            results.Add(new SectionBuildInput(sectionIndex, splitSection, sourceVertexIndices, false));
        }

        mesh.SplitOutputVertexCount = results.Sum(static r => r.Section?.Vertices.Count ?? 0);
        mesh.SplitDuplicatedVertexCount = mesh.SplitOutputVertexCount - mesh.SplitSourceVertexCount;
        mesh.SplitSharedSourceVertexCount = sourceVertexUsageCount.Count(static pair => pair.Value > 1);
        return results;
    }

    private static byte[] BuildRawPointIndices(MeshImportContext context, int vertexCount)
    {
        if (context.OriginalLod.RawPointIndices == null || context.OriginalLod.RawPointIndices.Length == 0)
            return [];

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        for (int i = 0; i < vertexCount; i++)
            writer.Write(i);

        return stream.ToArray();
    }

    private static FSkeletalMeshVertexBuffer BuildVertexBuffer(MeshImportContext context, IReadOnlyList<FGPUSkinVertexBase> vertices)
    {
        GetBounds(vertices, out Vector3 min, out Vector3 max);
        FVector meshOrigin = context.UsePackedPosition
            ? new FVector((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f)
            : new FVector(
                context.OriginalLod.VertexBufferGPUSkin.MeshOrigin.X,
                context.OriginalLod.VertexBufferGPUSkin.MeshOrigin.Y,
                context.OriginalLod.VertexBufferGPUSkin.MeshOrigin.Z);
        FVector meshExtension = context.UsePackedPosition
            ? new FVector((max.X - min.X) * 0.5f, (max.Y - min.Y) * 0.5f, (max.Z - min.Z) * 0.5f)
            : new FVector(
                context.OriginalLod.VertexBufferGPUSkin.MeshExtension.X,
                context.OriginalLod.VertexBufferGPUSkin.MeshExtension.Y,
                context.OriginalLod.VertexBufferGPUSkin.MeshExtension.Z);

        FSkeletalMeshVertexBuffer buffer = new()
        {
            NumTexCoords = (uint)context.NumTexCoords,
            bUseFullPrecisionUVs = context.UseFullPrecisionUvs,
            bUsePackedPosition = context.UsePackedPosition,
            MeshOrigin = meshOrigin,
            MeshExtension = meshExtension
        };

        if (context.UseFullPrecisionUvs)
        {
            if (context.UsePackedPosition)
            {
                buffer.VertsF32UV32 = [.. vertices
                    .Cast<FGPUSkinVertexFloat32Uvs>()
                    .Select(vertex => new FGPUSkinVertexFloat32Uvs32Xyz
                    {
                        TangentX = vertex.TangentX,
                        TangentZ = vertex.TangentZ,
                        InfluenceBones = [.. vertex.InfluenceBones],
                        InfluenceWeights = [.. vertex.InfluenceWeights],
                        Positon = PackPosition(vertex.Positon, meshOrigin, meshExtension),
                        UVs = [.. vertex.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))]
                    })];
            }
            else
            {
                buffer.VertsF32 = [.. vertices.Cast<FGPUSkinVertexFloat32Uvs>()];
            }
        }
        else
        {
            if (context.UsePackedPosition)
            {
                buffer.VertsF16UV32 = [.. vertices
                    .Cast<FGPUSkinVertexFloat16Uvs>()
                    .Select(vertex => new FGPUSkinVertexFloat16Uvs32Xyz
                    {
                        TangentX = vertex.TangentX,
                        TangentZ = vertex.TangentZ,
                        InfluenceBones = [.. vertex.InfluenceBones],
                        InfluenceWeights = [.. vertex.InfluenceWeights],
                        Positon = PackPosition(vertex.Positon, meshOrigin, meshExtension),
                        UVs = [.. vertex.UVs.Select(static uv => new FVector2DHalf
                        {
                            X = new FFloat16 { Encoded = uv.X.Encoded },
                            Y = new FFloat16 { Encoded = uv.Y.Encoded }
                        })]
                    })];
            }
            else
            {
                buffer.VertsF16 = [.. vertices.Cast<FGPUSkinVertexFloat16Uvs>()];
            }
        }

        return buffer;
    }

    private static FPackedPosition PackPosition(FVector position, FVector meshOrigin, FVector meshExtension)
    {
        float normalizedX = NormalizePackedAxis(position.X, meshOrigin.X, meshExtension.X);
        float normalizedY = NormalizePackedAxis(position.Y, meshOrigin.Y, meshExtension.Y);
        float normalizedZ = NormalizePackedAxis(position.Z, meshOrigin.Z, meshExtension.Z);

        uint packedX = PackSignedComponent(normalizedX, 11);
        uint packedY = PackSignedComponent(normalizedY, 11);
        uint packedZ = PackSignedComponent(normalizedZ, 10);

        return new FPackedPosition
        {
            Packed = packedX | (packedY << 11) | (packedZ << 22)
        };
    }

    private static float NormalizePackedAxis(float value, float origin, float extension)
    {
        if (MathF.Abs(extension) <= 1e-8f)
            return 0.0f;

        return Math.Clamp((value - origin) / extension, -1.0f, 1.0f);
    }

    private static uint PackSignedComponent(float value, int bits)
    {
        int maxPositive = (1 << (bits - 1)) - 1;
        int minNegative = -(1 << (bits - 1));
        int quantized = (int)MathF.Round(Math.Clamp(value, -1.0f, 1.0f) * maxPositive);
        quantized = Math.Clamp(quantized, minNegative, maxPositive);

        return (uint)(quantized & ((1 << bits) - 1));
    }

    private static void GetBounds(IReadOnlyList<FGPUSkinVertexBase> vertices, out Vector3 min, out Vector3 max)
    {
        if (vertices.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
            return;
        }

        min = vertices[0].GetVector3();
        max = min;

        foreach (FGPUSkinVertexBase vertex in vertices)
        {
            Vector3 position = vertex.GetVector3();
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
    }

    private static FSkeletalMeshVertexColorBuffer BuildColorBuffer(int count)
    {
        return new FSkeletalMeshVertexColorBuffer
        {
            Colors = [.. Enumerable.Range(0, count).Select(static _ => new FGPUSkinVertexColor
            {
                VertexColor = new FColor { R = 255, G = 255, B = 255, A = 255 }
            })]
        };
    }

    private static FSkeletalMeshVertexInfluences BuildInfluenceBuffer(
        IReadOnlyList<FVertexInfluence> influences,
        IReadOnlyList<FSkelMeshSection> sections,
        IReadOnlyList<FSkelMeshChunk> chunks,
        MeshImportContext context)
    {
        UMap<BoneIndexPair, UArray<uint>> mapping = [];
        for (uint vertexIndex = 0; vertexIndex < influences.Count; vertexIndex++)
        {
            FVertexInfluence influence = influences[(int)vertexIndex];
            BoneIndexPair key = new(influence.Bones.Bones[0], influence.Bones.Bones[1]);
            if (!mapping.TryGetValue(key, out UArray<uint> vertices))
            {
                vertices = [];
                mapping[key] = vertices;
            }

            vertices.Add(vertexIndex);
        }

        return new FSkeletalMeshVertexInfluences
        {
            Influences = [.. influences],
            VertexInfluenceMapping = mapping,
            Sections = [.. sections],
            Chunks = [.. chunks],
            RequiredBones = [.. context.RequiredBones],
            Usage = 0
        };
    }

    private static FRigidSkinVertex CloneRigidVertex(FRigidSkinVertex value)
    {
        return new FRigidSkinVertex
        {
            Position = new FVector(value.Position.X, value.Position.Y, value.Position.Z),
            TangentX = new FPackedNormal { Packed = value.TangentX.Packed },
            TangentY = new FPackedNormal { Packed = value.TangentY.Packed },
            TangentZ = new FPackedNormal { Packed = value.TangentZ.Packed },
            UVs = [.. value.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))],
            Color = new FColor { R = value.Color.R, G = value.Color.G, B = value.Color.B, A = value.Color.A },
            Bone = value.Bone
        };
    }

    private static FSoftSkinVertex CloneSoftVertex(FSoftSkinVertex value)
    {
        return new FSoftSkinVertex
        {
            Position = new FVector(value.Position.X, value.Position.Y, value.Position.Z),
            TangentX = new FPackedNormal { Packed = value.TangentX.Packed },
            TangentY = new FPackedNormal { Packed = value.TangentY.Packed },
            TangentZ = new FPackedNormal { Packed = value.TangentZ.Packed },
            UVs = [.. value.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))],
            Color = new FColor { R = value.Color.R, G = value.Color.G, B = value.Color.B, A = value.Color.A },
            InfluenceBones = [.. value.InfluenceBones],
            InfluenceWeights = [.. value.InfluenceWeights]
        };
    }

    private static FGPUSkinVertexBase CloneGpuVertex(FGPUSkinVertexBase value)
    {
        return value switch
        {
            FGPUSkinVertexFloat16Uvs v => new FGPUSkinVertexFloat16Uvs
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FVector(v.Positon.X, v.Positon.Y, v.Positon.Z),
                UVs = [.. v.UVs.Select(static uv => new FVector2DHalf { X = new FFloat16 { Encoded = uv.X.Encoded }, Y = new FFloat16 { Encoded = uv.Y.Encoded } })]
            },
            FGPUSkinVertexFloat32Uvs v => new FGPUSkinVertexFloat32Uvs
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FVector(v.Positon.X, v.Positon.Y, v.Positon.Z),
                UVs = [.. v.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))]
            },
            FGPUSkinVertexFloat16Uvs32Xyz v => new FGPUSkinVertexFloat16Uvs32Xyz
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FPackedPosition { Packed = v.Positon.Packed },
                UVs = [.. v.UVs.Select(static uv => new FVector2DHalf { X = new FFloat16 { Encoded = uv.X.Encoded }, Y = new FFloat16 { Encoded = uv.Y.Encoded } })]
            },
            FGPUSkinVertexFloat32Uvs32Xyz v => new FGPUSkinVertexFloat32Uvs32Xyz
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FPackedPosition { Packed = v.Positon.Packed },
                UVs = [.. v.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))]
            },
            _ => throw new InvalidOperationException($"Unsupported GPU vertex type '{value.GetType().Name}'.")
        };
    }

    private static FVertexInfluence CloneInfluence(FVertexInfluence value)
    {
        return new FVertexInfluence
        {
            Bones = new FInfluenceBones { Bones = [.. value.Bones.Bones] },
            Weights = new FInfluenceWeights { Weights = [.. value.Weights.Weights] }
        };
    }

    private static IReadOnlyList<FVertexInfluence> BuildPreservedInfluences(FSkelMeshChunk chunk)
    {
        List<FVertexInfluence> influences = new(chunk.NumRigidVertices + chunk.NumSoftVertices);

        foreach (FRigidSkinVertex rigidVertex in chunk.RigidVertices)
        {
            influences.Add(new FVertexInfluence
            {
                Bones = new FInfluenceBones { Bones = [rigidVertex.Bone, 0, 0, 0] },
                Weights = new FInfluenceWeights { Weights = [255, 0, 0, 0] }
            });
        }

        foreach (FSoftSkinVertex softVertex in chunk.SoftVertices)
        {
            influences.Add(new FVertexInfluence
            {
                Bones = new FInfluenceBones { Bones = [.. softVertex.InfluenceBones] },
                Weights = new FInfluenceWeights { Weights = [.. softVertex.InfluenceWeights] }
            });
        }

        return influences;
    }

    private sealed record SectionBuildInput(int OriginalSectionIndex, NeutralSection Section, IReadOnlyList<int> SourceVertexIndices, bool PreserveOriginal);
    private sealed record BuiltSectionData(FSkelMeshChunk Chunk, IReadOnlyList<ushort> Indices, IReadOnlyList<FGPUSkinVertexBase> GpuVertices, IReadOnlyList<FVertexInfluence> Influences, int VertexCount);
}
