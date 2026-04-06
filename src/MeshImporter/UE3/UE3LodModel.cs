using UpkManager.Models.UpkFile.Engine.Mesh;

namespace MHUpkManager.MeshImporter;

internal sealed class UE3LodModel
{
    public UE3LodModel(FStaticLODModel inner)
    {
        Inner = inner;
    }

    public FStaticLODModel Inner { get; }
    public uint NumVertices => Inner.NumVertices;
    public uint NumTexCoords => Inner.NumTexCoords;
}
