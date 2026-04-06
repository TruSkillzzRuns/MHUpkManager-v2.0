using System.Numerics;

namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewBone
{
    public string Name { get; init; } = string.Empty;
    public int ParentIndex { get; init; } = -1;
    public Matrix4x4 LocalTransform { get; init; } = Matrix4x4.Identity;
    public Matrix4x4 GlobalTransform { get; set; } = Matrix4x4.Identity;
}
