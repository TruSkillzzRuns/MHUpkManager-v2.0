namespace MHUpkManager.MeshImporter;

internal sealed class LodBoneMapBuilder
{
    public IReadOnlyList<int> Build(
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        HashSet<int> usedBones = [];
        foreach (IReadOnlyList<NormalizedWeight> weights in normalizedWeights)
        {
            foreach (NormalizedWeight weight in weights)
            {
                if (weight.Weight > 0)
                    usedBones.Add(weight.BoneIndex);
            }
        }

        return context.SortBonesByRequiredOrder(usedBones);
    }
}
