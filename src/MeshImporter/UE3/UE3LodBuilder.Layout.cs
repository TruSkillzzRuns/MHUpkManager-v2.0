using UpkManager.Models.UpkFile.Engine.Mesh;

namespace MHUpkManager.MeshImporter;

internal sealed partial class UE3LodBuilder
{
    private BuiltSectionData BuildImportedSection(
        SectionBuildInput input,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights,
        MeshImportContext context,
        int globalVertexStart)
    {
        NeutralSection importedSection = input.Section
            ?? throw new InvalidOperationException("Imported section data was missing.");

        IReadOnlyList<int> chunkBoneMap = BuildBoneMap(input, normalizedWeights, context);
        FSkelMeshSection originalSection = context.OriginalLod.Sections[input.OriginalSectionIndex];
        FSkelMeshChunk originalChunk = context.OriginalLod.Chunks[originalSection.ChunkIndex];
        bool rebuildAsRigidChunk = originalChunk.NumSoftVertices == 0 && chunkBoneMap.Count == 1;
        bool preserveRigidVertices = originalChunk.NumRigidVertices > 0;
        BuiltVertexData builtVertices = _vertexBuilder.Build(
            importedSection,
            normalizedWeights,
            context,
            chunkBoneMap,
            input.SourceVertexIndices,
            rebuildAsRigidChunk,
            preserveRigidVertices);
        ushort[] builtIndices = _indexBuilder.Build(importedSection, globalVertexStart, builtVertices.LocalIndexRemap);

        return new BuiltSectionData(
            new FSkelMeshChunk
            {
                BaseVertexIndex = (uint)globalVertexStart,
                RigidVertices = [.. builtVertices.RigidVertices],
                SoftVertices = [.. builtVertices.SoftVertices],
                BoneMap = [.. chunkBoneMap.Select(static x => (ushort)x)],
                NumRigidVertices = builtVertices.RigidVertices.Count,
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

        FSkelMeshSection originalSection = context.OriginalLod.Sections[input.OriginalSectionIndex];
        FSkelMeshChunk originalChunk = context.OriginalLod.Chunks[originalSection.ChunkIndex];
        IReadOnlyList<int> originalBoneMap = [.. originalChunk.BoneMap.Select(static bone => (int)bone)];
        if (usedBones.All(originalBoneMap.Contains))
            return originalBoneMap;

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
        if (ShouldPreferTriangleOrderSplit(mesh, context))
        {
            NeutralSection combined = CombineImportedSections(mesh);
            mesh.LayoutStrategy = "EqualCountTriangleOrder";
            return SplitSingleSectionByTriangleOrder(mesh, combined, context);
        }

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

        List<SectionBuildInput> merged = TryMergeSectionsToOriginalCount(mesh, context);
        if (merged.Count > 0)
        {
            mesh.LayoutStrategy = "MergedToOriginalCount";
            return merged;
        }

        if (mesh.Sections.Count == 1)
        {
            mesh.LayoutStrategy = "SingleSectionSplit";
            return SplitSingleSection(mesh, mesh.Sections[0], context, normalizedWeights);
        }

        throw new InvalidOperationException($"FBX section count ({mesh.Sections.Count}) does not match original LOD section count ({context.OriginalLod.Sections.Count}).");
    }

    private static bool ShouldPreferTriangleOrderSplit(NeutralMesh mesh, MeshImportContext context)
    {
        if (mesh.Sections.Count <= 1 || mesh.Sections.Count != context.OriginalLod.Sections.Count)
            return false;

        bool sectionTriangleCountsAlreadyAlign = true;
        for (int i = 0; i < mesh.Sections.Count; i++)
        {
            int importedTriangles = mesh.Sections[i].Indices.Count / 3;
            int originalTriangles = checked((int)context.OriginalLod.Sections[i].NumTriangles);
            if (importedTriangles != originalTriangles)
            {
                sectionTriangleCountsAlreadyAlign = false;
                break;
            }
        }

        // If the FBX is already arriving with the same section split as the original LOD,
        // keep that direct split instead of recombining and redistributing triangles.
        if (sectionTriangleCountsAlreadyAlign)
            return false;

        HashSet<int> originalMaterialIndices = [.. context.OriginalLod.Sections.Select(static section => section.MaterialIndex)];
        if (originalMaterialIndices.Count != 1)
            return false;

        HashSet<string> importedMaterialNames = [.. mesh.Sections
            .Select(static section => section.MaterialName ?? string.Empty)
            .Where(static name => string.IsNullOrWhiteSpace(name) == false)];

        return importedMaterialNames.Count <= 1;
    }

    private static NeutralSection CombineImportedSections(NeutralMesh mesh)
    {
        NeutralSection combined = new()
        {
            Name = mesh.Sections.Count > 0 ? mesh.Sections[0].Name : "CombinedSection",
            MaterialName = mesh.Sections.Count > 0 ? mesh.Sections[0].MaterialName : string.Empty,
            ImportedMaterialIndex = mesh.Sections.Count > 0 ? mesh.Sections[0].ImportedMaterialIndex : 0
        };

        foreach (NeutralSection sourceSection in mesh.Sections)
        {
            int vertexBase = combined.Vertices.Count;
            combined.ImportedVertexCount += sourceSection.Vertices.Count;
            combined.ImportedTriangleCount += sourceSection.Indices.Count / 3;

            foreach (NeutralVertex vertex in sourceSection.Vertices)
                combined.Vertices.Add(vertex);

            combined.Indices.AddRange(sourceSection.Indices.Select(index => index + vertexBase));
        }

        return combined;
    }

    private static void CaptureLayoutDiagnostics(
        NeutralMesh mesh,
        MeshImportContext context,
        IReadOnlyList<SectionBuildInput> sectionInputs)
    {
        mesh.LayoutSections.Clear();
        foreach (SectionBuildInput input in sectionInputs)
        {
            mesh.LayoutSections.Add(new LayoutSectionDiagnostic
            {
                OriginalSectionIndex = input.OriginalSectionIndex,
                SourceImportedSectionIndices = [.. input.SourceImportedSectionIndices],
                ImportedVertexCount = input.Section?.Vertices.Count ?? 0,
                ImportedTriangleCount = input.Section == null ? 0 : input.Section.Indices.Count / 3,
                FinalMaterialIndex = context.OriginalLod.Sections[input.OriginalSectionIndex].MaterialIndex,
                PreserveOriginal = input.PreserveOriginal,
                Behavior = input.Behavior
            });
        }
    }

    private static List<SectionBuildInput> TryUseSectionsDirect(NeutralMesh mesh, MeshImportContext context)
    {
        if (mesh.Sections.Count != context.OriginalLod.Sections.Count)
            return [];

        int[] importedVertexOffsets = new int[mesh.Sections.Count];
        int flatVertexOffset = 0;
        for (int i = 0; i < mesh.Sections.Count; i++)
        {
            importedVertexOffsets[i] = flatVertexOffset;
            flatVertexOffset += mesh.Sections[i].Vertices.Count;
        }

        SectionBuildInput[] matched = new SectionBuildInput[context.OriginalLod.Sections.Count];
        HashSet<int> usedOriginalSections = [];

        for (int importedIndex = 0; importedIndex < mesh.Sections.Count; importedIndex++)
        {
            NeutralSection importedSection = mesh.Sections[importedIndex];
            int importedTriangles = importedSection.Indices.Count / 3;

            if (importedSection.Indices.Count % 3 != 0)
                throw new InvalidOperationException($"Imported section {importedIndex} does not contain complete triangles.");

            int originalSectionIndex = FindBestOriginalSection(importedSection, importedIndex, usedOriginalSections, context);
            if (originalSectionIndex < 0)
                return [];

            FSkelMeshSection originalSection = context.OriginalLod.Sections[originalSectionIndex];
            if (!mesh.AllowTopologyChange && importedTriangles != originalSection.NumTriangles)
                throw new InvalidOperationException(
                    $"Imported section {importedIndex} triangle count ({importedTriangles}) does not match original section triangle count ({originalSection.NumTriangles}).");

            usedOriginalSections.Add(originalSectionIndex);
            matched[originalSectionIndex] = new SectionBuildInput(
                originalSectionIndex,
                importedSection,
                [.. Enumerable.Range(importedVertexOffsets[importedIndex], importedSection.Vertices.Count)],
                false,
                [importedIndex],
                "DirectMatched");
        }

        if (matched.Any(static input => input is null))
            return [];

        return [.. matched];
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
                false,
                [importedIndex],
                "MatchedImportedSection");
        }

        List<SectionBuildInput> results = [];
        for (int originalIndex = 0; originalIndex < mapped.Length; originalIndex++)
            results.Add(mapped[originalIndex] ?? new SectionBuildInput(originalIndex, placeholderSection, [], true, [], "PreservedOriginalSection"));

        return results;
    }

    private static List<SectionBuildInput> TryMergeSectionsToOriginalCount(NeutralMesh mesh, MeshImportContext context)
    {
        if (mesh.Sections.Count <= context.OriginalLod.Sections.Count || context.OriginalLod.Sections.Count == 0)
            return [];

        int[] importedVertexOffsets = new int[mesh.Sections.Count];
        int flatVertexOffset = 0;
        for (int i = 0; i < mesh.Sections.Count; i++)
        {
            NeutralSection importedSection = mesh.Sections[i];
            if (importedSection.Indices.Count % 3 != 0)
                throw new InvalidOperationException($"Imported section {i} does not contain complete triangles.");

            importedVertexOffsets[i] = flatVertexOffset;
            flatVertexOffset += importedSection.Vertices.Count;
        }

        List<(int ImportedIndex, int OriginalIndex, double Score)> scoredPairs = [];
        for (int importedIndex = 0; importedIndex < mesh.Sections.Count; importedIndex++)
        {
            for (int originalIndex = 0; originalIndex < context.OriginalLod.Sections.Count; originalIndex++)
                scoredPairs.Add((importedIndex, originalIndex, ScoreSectionMatch(mesh.Sections[importedIndex], importedIndex, context, originalIndex)));
        }

        List<int>[] assignments = Enumerable.Range(0, context.OriginalLod.Sections.Count)
            .Select(static _ => new List<int>())
            .ToArray();
        HashSet<int> assignedImported = [];
        HashSet<int> coveredOriginal = [];

        foreach ((int importedIndex, int originalIndex, _) in scoredPairs.OrderBy(static pair => pair.Score))
        {
            if (assignedImported.Contains(importedIndex) || coveredOriginal.Contains(originalIndex))
                continue;

            assignments[originalIndex].Add(importedIndex);
            assignedImported.Add(importedIndex);
            coveredOriginal.Add(originalIndex);

            if (coveredOriginal.Count == context.OriginalLod.Sections.Count)
                break;
        }

        for (int importedIndex = 0; importedIndex < mesh.Sections.Count; importedIndex++)
        {
            if (assignedImported.Contains(importedIndex))
                continue;

            int bestOriginalIndex = Enumerable.Range(0, context.OriginalLod.Sections.Count)
                .OrderBy(originalIndex => ScoreSectionMatch(mesh.Sections[importedIndex], importedIndex, context, originalIndex))
                .First();
            assignments[bestOriginalIndex].Add(importedIndex);
            assignedImported.Add(importedIndex);
        }

        NeutralSection placeholderSection = new();
        List<SectionBuildInput> results = [];
        for (int originalIndex = 0; originalIndex < assignments.Length; originalIndex++)
        {
            if (assignments[originalIndex].Count == 0)
            {
                results.Add(new SectionBuildInput(originalIndex, placeholderSection, [], true, [], "PreservedOriginalSection"));
                continue;
            }

            (NeutralSection mergedSection, IReadOnlyList<int> sourceVertexIndices) = MergeAssignedSections(
                mesh,
                assignments[originalIndex],
                importedVertexOffsets,
                context,
                originalIndex);
            results.Add(new SectionBuildInput(
                originalIndex,
                mergedSection,
                sourceVertexIndices,
                false,
                [.. assignments[originalIndex].OrderBy(static index => index)],
                "MergedImportedSections"));
        }

        return results;
    }

    private static double ScoreSectionMatch(NeutralSection importedSection, int importedIndex, MeshImportContext context, int originalIndex)
    {
        FSkelMeshSection originalSection = context.OriginalLod.Sections[originalIndex];
        int importedTriangles = importedSection.Indices.Count / 3;
        int desiredMaterialIndex = context.ResolveMaterialIndex(importedSection.MaterialName, importedSection.ImportedMaterialIndex);

        double score = Math.Abs(importedTriangles - (int)originalSection.NumTriangles);
        if (originalSection.MaterialIndex != desiredMaterialIndex)
            score += 100000.0;

        score += Math.Abs(importedIndex - originalIndex) * 0.25;
        return score;
    }

    private static (NeutralSection Section, IReadOnlyList<int> SourceVertexIndices) MergeAssignedSections(
        NeutralMesh mesh,
        IReadOnlyList<int> assignedImportedIndices,
        IReadOnlyList<int> importedVertexOffsets,
        MeshImportContext context,
        int originalIndex)
    {
        NeutralSection merged = new()
        {
            Name = mesh.Sections[assignedImportedIndices[0]].Name,
            MaterialName = $"Material_{context.OriginalLod.Sections[originalIndex].MaterialIndex}",
            ImportedMaterialIndex = context.OriginalLod.Sections[originalIndex].MaterialIndex
        };

        List<int> sourceVertexIndices = [];
        foreach (int importedIndex in assignedImportedIndices.OrderBy(static index => index))
        {
            NeutralSection sourceSection = mesh.Sections[importedIndex];
            int vertexBase = merged.Vertices.Count;
            merged.ImportedVertexCount += sourceSection.Vertices.Count;
            merged.ImportedTriangleCount += sourceSection.Indices.Count / 3;

            foreach (NeutralVertex vertex in sourceSection.Vertices)
                merged.Vertices.Add(vertex);

            merged.Indices.AddRange(sourceSection.Indices.Select(index => index + vertexBase));
            sourceVertexIndices.AddRange(Enumerable.Range(importedVertexOffsets[importedIndex], sourceSection.Vertices.Count));
        }

        return (merged, sourceVertexIndices);
    }
}
