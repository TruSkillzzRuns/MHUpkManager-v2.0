using UpkManager.Models.UpkFile.Engine.Mesh;

namespace MHUpkManager.MeshImporter;

internal sealed partial class UE3LodBuilder
{
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

            results.Add(new SectionBuildInput(
                sectionIndex,
                splitSection,
                sourceVertexIndices,
                false,
                [0],
                mesh.SplitStrategy == "BoneAffinity" ? "SplitSingleImportedSectionByBoneAffinity" : "SplitSingleImportedSectionByTriangleOrder"));
        }

        mesh.SplitOutputVertexCount = results.Sum(static r => r.Section?.Vertices.Count ?? 0);
        mesh.SplitDuplicatedVertexCount = mesh.SplitOutputVertexCount - mesh.SplitSourceVertexCount;
        mesh.SplitSharedSourceVertexCount = sourceVertexUsageCount.Count(static pair => pair.Value > 1);
        return results;
    }
}
