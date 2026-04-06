namespace MHUpkManager.Retargeting;

internal sealed class WeightTransfer
{
    public RetargetMesh Apply(RetargetMesh sourceMesh, IReadOnlyDictionary<string, string> boneMapping, SkeletonDefinition playerSkeleton, Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (boneMapping == null)
            throw new ArgumentNullException(nameof(boneMapping));
        if (playerSkeleton == null)
            throw new ArgumentNullException(nameof(playerSkeleton));

        playerSkeleton.RebuildBoneLookup();
        RetargetMesh retargeted = sourceMesh.DeepClone();
        retargeted.Bones.Clear();
        retargeted.Bones.AddRange(playerSkeleton.Bones.Select(static bone => bone.DeepClone()));
        retargeted.RebuildBoneLookup();

        int vertexCounter = 0;
        foreach (RetargetSection section in retargeted.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                List<RetargetWeight> reassigned = [];
                foreach (RetargetWeight sourceWeight in vertex.Weights)
                {
                    if (sourceWeight.Weight <= 0.0f)
                        continue;

                    string mappedBone = boneMapping.TryGetValue(sourceWeight.BoneName, out string mapped)
                        ? mapped
                        : playerSkeleton.Bones.First().Name;

                    reassigned.Add(new RetargetWeight(mappedBone, sourceWeight.Weight));
                }

                vertex.Weights.Clear();
                vertex.Weights.AddRange(NormalizeWeights(reassigned));
                vertexCounter++;
            }
        }

        log?.Invoke($"Transferred weights for {vertexCounter} vertices using {boneMapping.Count} mapped bones.");
        return retargeted;
    }

    internal static IReadOnlyList<RetargetWeight> NormalizeWeights(IEnumerable<RetargetWeight> weights)
    {
        Dictionary<string, float> combined = new(StringComparer.OrdinalIgnoreCase);
        foreach (RetargetWeight weight in weights)
        {
            if (weight.Weight <= 0.0f || string.IsNullOrWhiteSpace(weight.BoneName))
                continue;

            if (!combined.TryAdd(weight.BoneName, weight.Weight))
                combined[weight.BoneName] += weight.Weight;
        }

        List<KeyValuePair<string, float>> topWeights = [.. combined
            .Where(static pair => pair.Value > 0.0f)
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(4)];

        if (topWeights.Count == 0)
            return [new RetargetWeight(string.Empty, 1.0f)];

        float total = topWeights.Sum(static pair => pair.Value);
        if (total <= 0.0f)
            total = 1.0f;

        return [.. topWeights.Select(static pair => pair).Select(pair => new RetargetWeight(pair.Key, pair.Value / total))];
    }
}
