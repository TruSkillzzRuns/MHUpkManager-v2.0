namespace MHUpkManager.MeshImporter;

internal sealed class RequiredBonesBuilder
{
    public IReadOnlyList<byte> Build(MeshImportContext context)
    {
        return [.. context.RequiredBones];
    }
}
