namespace MHUpkManager.Model.Import;

internal sealed class BoneRemapper
{
    public IReadOnlyList<IReadOnlyList<RemappedWeight>> Remap(NeutralMesh mesh, MeshImportContext context)
    {
        List<IReadOnlyList<RemappedWeight>> result = [];

        foreach (NeutralSection section in mesh.Sections)
        {
            foreach (NeutralVertex vertex in section.Vertices)
            {
                List<RemappedWeight> remapped = [];
                foreach (VertexWeight weight in vertex.Weights)
                {
                    if (weight.Weight <= 0.0f)
                        continue;

                    remapped.Add(new RemappedWeight(context.ResolveBoneIndex(weight.BoneName), weight.Weight));
                }

                result.Add(remapped);
            }
        }

        return result;
    }
}
