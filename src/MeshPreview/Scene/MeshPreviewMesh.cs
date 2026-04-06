using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;

namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewMesh : IDisposable
{
    public string Name { get; init; } = string.Empty;
    public List<MeshPreviewVertex> Vertices { get; } = [];
    public List<uint> Indices { get; } = [];
    public List<MeshPreviewSection> Sections { get; } = [];
    public List<MeshPreviewBone> Bones { get; } = [];
    public List<Vector3> UvSeamLines { get; } = [];
    public Vector3 Center { get; set; }
    public float Radius { get; set; } = 1.0f;

    public int VertexArrayObject { get; private set; }
    public int VertexBufferObject { get; private set; }
    public int ElementBufferObject { get; private set; }
    public int NormalLineVao { get; private set; }
    public int NormalLineVbo { get; private set; }
    public int NormalLineVertexCount { get; private set; }
    public int TangentLineVao { get; private set; }
    public int TangentLineVbo { get; private set; }
    public int TangentLineVertexCount { get; private set; }
    public int UvSeamVao { get; private set; }
    public int UvSeamVbo { get; private set; }
    public int UvSeamVertexCount { get; private set; }
    public bool IsUploaded { get; private set; }

    public void Upload()
    {
        if (IsUploaded || Vertices.Count == 0 || Indices.Count == 0)
            return;

        VertexArrayObject = GL.GenVertexArray();
        VertexBufferObject = GL.GenBuffer();
        ElementBufferObject = GL.GenBuffer();

        GL.BindVertexArray(VertexArrayObject);
        GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, Vertices.Count * MeshPreviewVertex.SizeInBytes, Vertices.ToArray(), BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ElementBufferObject);
        GL.BufferData(BufferTarget.ElementArrayBuffer, Indices.Count * sizeof(uint), Indices.ToArray(), BufferUsageHint.StaticDraw);

        int offset = 0;
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, MeshPreviewVertex.SizeInBytes, offset);
        offset += Vector3ByteSize;

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, MeshPreviewVertex.SizeInBytes, offset);
        offset += Vector3ByteSize;

        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, MeshPreviewVertex.SizeInBytes, offset);
        offset += Vector3ByteSize;

        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, MeshPreviewVertex.SizeInBytes, offset);
        offset += Vector3ByteSize;

        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, MeshPreviewVertex.SizeInBytes, offset);
        offset += Vector2ByteSize;

        GL.EnableVertexAttribArray(5);
        GL.VertexAttribIPointer(5, 4, VertexAttribIntegerType.Int, MeshPreviewVertex.SizeInBytes, (IntPtr)offset);
        offset += Int4ByteSize;

        GL.EnableVertexAttribArray(6);
        GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, MeshPreviewVertex.SizeInBytes, offset);
        offset += Vector4ByteSize;

        GL.EnableVertexAttribArray(7);
        GL.VertexAttribIPointer(7, 1, VertexAttribIntegerType.Int, MeshPreviewVertex.SizeInBytes, (IntPtr)offset);

        GL.BindVertexArray(0);

        (NormalLineVao, NormalLineVbo, NormalLineVertexCount) = UploadLineBuffer(BuildDirectionLines(static v => v.Normal, Radius * 0.05f));
        (TangentLineVao, TangentLineVbo, TangentLineVertexCount) = UploadLineBuffer(BuildDirectionLines(static v => v.Tangent, Radius * 0.05f));
        (UvSeamVao, UvSeamVbo, UvSeamVertexCount) = UploadLineBuffer(UvSeamLines);

        IsUploaded = true;
    }

    public void Dispose()
    {
        DeleteBuffer(VertexBufferObject);
        DeleteBuffer(ElementBufferObject);
        DeleteVertexArray(VertexArrayObject);
        DeleteBuffer(NormalLineVbo);
        DeleteVertexArray(NormalLineVao);
        DeleteBuffer(TangentLineVbo);
        DeleteVertexArray(TangentLineVao);
        DeleteBuffer(UvSeamVbo);
        DeleteVertexArray(UvSeamVao);
        VertexArrayObject = 0;
        VertexBufferObject = 0;
        ElementBufferObject = 0;
        NormalLineVao = 0;
        NormalLineVbo = 0;
        NormalLineVertexCount = 0;
        TangentLineVao = 0;
        TangentLineVbo = 0;
        TangentLineVertexCount = 0;
        UvSeamVao = 0;
        UvSeamVbo = 0;
        UvSeamVertexCount = 0;
        IsUploaded = false;
    }

    private List<Vector3> BuildDirectionLines(Func<MeshPreviewVertex, Vector3> selector, float scale)
    {
        List<Vector3> lines = new(Vertices.Count * 2);
        foreach (MeshPreviewVertex vertex in Vertices)
        {
            lines.Add(vertex.Position);
            lines.Add(vertex.Position + (Vector3.Normalize(selector(vertex)) * scale));
        }

        return lines;
    }

    private static (int Vao, int Vbo, int VertexCount) UploadLineBuffer(List<Vector3> lines)
    {
        int vertexCount = lines.Count;
        if (vertexCount == 0)
            return (0, 0, 0);

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, lines.Count * Vector3ByteSize, lines.ToArray(), BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vector3ByteSize, 0);
        GL.BindVertexArray(0);
        return (vao, vbo, vertexCount);
    }

    private static void DeleteBuffer(int handle)
    {
        if (handle != 0)
            GL.DeleteBuffer(handle);
    }

    private static void DeleteVertexArray(int handle)
    {
        if (handle != 0)
            GL.DeleteVertexArray(handle);
    }

    private const int Vector2ByteSize = sizeof(float) * 2;
    private const int Vector3ByteSize = sizeof(float) * 3;
    private const int Vector4ByteSize = sizeof(float) * 4;
    private const int Int4ByteSize = sizeof(int) * 4;
}

internal struct MeshPreviewVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Bitangent;
    public Vector2 Uv;
    public int Bone0;
    public int Bone1;
    public int Bone2;
    public int Bone3;
    public float Weight0;
    public float Weight1;
    public float Weight2;
    public float Weight3;
    public int SectionIndex;

    public static int SizeInBytes => sizeof(float) * 3 * 4 + sizeof(float) * 2 + sizeof(int) * 5 + sizeof(float) * 4;
}

internal sealed class MeshPreviewSection
{
    public int Index { get; init; }
    public int MaterialIndex { get; init; }
    public int BaseIndex { get; init; }
    public int IndexCount { get; init; }
    public Vector4 Color { get; init; }
    public string Name { get; init; } = string.Empty;
    public MeshPreviewGameMaterial GameMaterial { get; set; }
}
