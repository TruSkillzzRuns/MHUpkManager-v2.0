using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class UE3CompatibilityProcessor
{
    private const int MaxSupportedUvSets = 4;

    public RetargetMesh Process(
        RetargetMesh mesh,
        SkeletonDefinition playerSkeleton,
        IReadOnlyDictionary<string, string> boneMapping,
        Action<string> log = null)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));
        if (playerSkeleton == null)
            throw new ArgumentNullException(nameof(playerSkeleton));

        playerSkeleton.RebuildBoneLookup();
        RetargetMesh compatible = mesh.DeepClone();

        compatible.Bones.Clear();
        compatible.Bones.AddRange(playerSkeleton.Bones.Select(static bone => bone.DeepClone()));
        compatible.RebuildBoneLookup();
        compatible.AnimSet = mesh.AnimSet;

        for (int sectionIndex = 0; sectionIndex < compatible.Sections.Count; sectionIndex++)
        {
            RetargetSection section = compatible.Sections[sectionIndex];
            RetargetSection sourceSection = mesh.Sections.Count > sectionIndex ? mesh.Sections[sectionIndex] : section;

            for (int vertexIndex = 0; vertexIndex < section.Vertices.Count; vertexIndex++)
            {
                RetargetVertex vertex = section.Vertices[vertexIndex];
                RetargetVertex sourceVertex = sourceSection.Vertices.Count > vertexIndex
                    ? sourceSection.Vertices[vertexIndex]
                    : vertex;

                if (mesh.Bones.Count > 0 && boneMapping != null && boneMapping.Count > 0)
                    ReposeVertexToTargetSkeleton(vertex, sourceVertex, mesh, playerSkeleton, boneMapping);

                while (vertex.UVs.Count < 1)
                    vertex.UVs.Add(Vector2.Zero);

                if (vertex.UVs.Count > MaxSupportedUvSets)
                    vertex.UVs.RemoveRange(MaxSupportedUvSets, vertex.UVs.Count - MaxSupportedUvSets);

                for (int channel = 0; channel < vertex.UVs.Count; channel++)
                    vertex.UVs[channel] = new Vector2(vertex.UVs[channel].X, vertex.UVs[channel].Y);

                vertex.Weights.Clear();
                vertex.Weights.AddRange(
                    WeightTransfer.NormalizeWeights(sourceVertex.Weights
                        .Select(weight =>
                        {
                            string targetBone = boneMapping != null && boneMapping.TryGetValue(weight.BoneName, out string mapped)
                                ? mapped
                                : weight.BoneName;
                            return new RetargetWeight(targetBone, weight.Weight);
                        })
                        .Where(weight => playerSkeleton.BonesByName.ContainsKey(weight.BoneName))));
            }

            while (section.TriangleSmoothingGroups.Count < section.Indices.Count / 3)
                section.TriangleSmoothingGroups.Add(1);
        }

        log?.Invoke($"Collapsed mesh to UE3-compatible LOD0 data with {compatible.MaxUvSets} UV channel(s) and {compatible.Bones.Count} ordered bones.");
        return compatible;
    }

    private static void ReposeVertexToTargetSkeleton(
        RetargetVertex vertex,
        RetargetVertex sourceVertex,
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string> boneMapping)
    {
        IReadOnlyList<RetargetWeight> normalizedSourceWeights = WeightTransfer.NormalizeWeights(sourceVertex.Weights);
        if (normalizedSourceWeights.Count == 0)
            return;

        Vector3 position = Vector3.Zero;
        Vector3 normal = Vector3.Zero;
        Vector3 tangent = Vector3.Zero;
        Vector3 bitangent = Vector3.Zero;
        float totalWeight = 0.0f;

        foreach (RetargetWeight sourceWeight in normalizedSourceWeights)
        {
            if (sourceWeight.Weight <= 0.0f)
                continue;

            if (!sourceMesh.BonesByName.TryGetValue(sourceWeight.BoneName, out RetargetBone sourceBone))
                continue;

            string targetBoneName = boneMapping.TryGetValue(sourceWeight.BoneName, out string mapped)
                ? mapped
                : sourceWeight.BoneName;
            if (!targetSkeleton.BonesByName.TryGetValue(targetBoneName, out RetargetBone targetBone))
                continue;

            if (!Matrix4x4.Invert(sourceBone.GlobalTransform, out Matrix4x4 sourceInverse))
                continue;

            Matrix4x4 boneTransform = sourceInverse * targetBone.GlobalTransform;
            position += Vector3.Transform(sourceVertex.Position, boneTransform) * sourceWeight.Weight;
            normal += Vector3.TransformNormal(sourceVertex.Normal, boneTransform) * sourceWeight.Weight;
            tangent += Vector3.TransformNormal(sourceVertex.Tangent, boneTransform) * sourceWeight.Weight;
            bitangent += Vector3.TransformNormal(sourceVertex.Bitangent, boneTransform) * sourceWeight.Weight;
            totalWeight += sourceWeight.Weight;
        }

        if (totalWeight <= 0.0f)
            return;

        vertex.Position = position / totalWeight;
        vertex.Normal = NormalizeOrFallback(normal, sourceVertex.Normal);
        vertex.Tangent = NormalizeOrFallback(tangent, sourceVertex.Tangent);
        vertex.Bitangent = NormalizeOrFallback(bitangent, sourceVertex.Bitangent);
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 1e-10f
            ? Vector3.Normalize(value)
            : (fallback.LengthSquared() > 1e-10f ? Vector3.Normalize(fallback) : Vector3.UnitY);
    }
}
