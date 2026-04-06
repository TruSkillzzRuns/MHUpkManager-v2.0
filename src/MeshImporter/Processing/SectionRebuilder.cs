namespace MHUpkManager.MeshImporter;

internal sealed class SectionRebuilder
{
    private readonly UE3LodBuilder _lodBuilder = new();

    public UE3LodModel RebuildSections(
        NeutralMesh mesh,
        MeshImportContext context,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights)
    {
        return _lodBuilder.Build(mesh, context, normalizedWeights);
    }
}
