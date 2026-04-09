using System.Numerics;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Types;

namespace MHUpkManager.MeshPreview;

internal sealed class UE3ToPreviewMeshConverter
{
    public MeshPreviewMesh Convert(USkeletalMesh skeletalMesh, int lodIndex, Action<string> log = null)
    {
        if (skeletalMesh == null)
            throw new ArgumentNullException(nameof(skeletalMesh));

        if (lodIndex < 0 || lodIndex >= skeletalMesh.LODModels.Count)
            throw new ArgumentOutOfRangeException(nameof(lodIndex));

        FStaticLODModel lod = skeletalMesh.LODModels[lodIndex];
        GLVertex[] vertices = [.. lod.VertexBufferGPUSkin.GetGLVertexData()];
        uint[] indices = [.. lod.MultiSizeIndexContainer.IndexBuffer];
        List<MeshPreviewSection> sections = [];

        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
        {
            FSkelMeshSection section = lod.Sections[sectionIndex];
            FSkelMeshChunk chunk = lod.Chunks[section.ChunkIndex];
            HashSet<int> processedVertices = [];
            uint start = section.BaseIndex;
            uint end = start + (section.NumTriangles * 3);

            for (uint i = start; i < end; i++)
            {
                int vertexIndex = (int)indices[i];
                if ((uint)vertexIndex >= vertices.Length)
                    continue;

                if (!processedVertices.Add(vertexIndex))
                    continue;

                GLVertex vertex = vertices[vertexIndex];
                vertex.Bone0 = RemapBone(vertex.Bone0, chunk.BoneMap, log);
                vertex.Bone1 = RemapBone(vertex.Bone1, chunk.BoneMap, log);
                vertex.Bone2 = RemapBone(vertex.Bone2, chunk.BoneMap, log);
                vertex.Bone3 = RemapBone(vertex.Bone3, chunk.BoneMap, log);
                vertices[vertexIndex] = vertex;
            }

            sections.Add(new MeshPreviewSection
            {
                Index = sectionIndex,
                MaterialIndex = section.MaterialIndex,
                BaseIndex = (int)section.BaseIndex,
                IndexCount = (int)section.NumTriangles * 3,
                Name = $"Section {sectionIndex}",
                Color = PreviewPalette.ColorForIndex(sectionIndex)
            });
        }

        MeshPreviewMesh previewMesh = new()
        {
            Name = "UE3 SkeletalMesh"
        };

        previewMesh.Sections.AddRange(sections);
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            previewMesh.Indices.Add(indices[i]);
            previewMesh.Indices.Add(indices[i + 2]);
            previewMesh.Indices.Add(indices[i + 1]);
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            previewMesh.Vertices.Add(new MeshPreviewVertex
            {
                Position = vertices[i].Position,
                Normal = NormalizeOrFallback(vertices[i].Normal),
                Tangent = NormalizeOrFallback(vertices[i].Tangent),
                Bitangent = NormalizeOrFallback(vertices[i].Bitangent),
                Uv = vertices[i].TexCoord,
                Bone0 = vertices[i].Bone0,
                Bone1 = vertices[i].Bone1,
                Bone2 = vertices[i].Bone2,
                Bone3 = vertices[i].Bone3,
                Weight0 = vertices[i].Weight0 / 255.0f,
                Weight1 = vertices[i].Weight1 / 255.0f,
                Weight2 = vertices[i].Weight2 / 255.0f,
                Weight3 = vertices[i].Weight3 / 255.0f,
                SectionIndex = ResolveSectionIndex(i, sections, previewMesh.Indices)
            });
        }

        RebuildSurfaceBasis(previewMesh);
        previewMesh.Bones.AddRange(BuildBones(skeletalMesh.RefSkeleton));
        BuildBounds(previewMesh);
        BuildUvSeams(previewMesh);
        log?.Invoke($"Loaded UE3 SkeletalMesh preview with {previewMesh.Vertices.Count} vertices, {previewMesh.Indices.Count / 3} triangles, {previewMesh.Bones.Count} bones, and {previewMesh.Sections.Count} sections.");
        return previewMesh;
    }

    private static List<MeshPreviewBone> BuildBones(UArray<FMeshBone> refSkeleton)
    {
        List<MeshPreviewBone> bones = new(refSkeleton.Count);
        for (int i = 0; i < refSkeleton.Count; i++)
        {
            FMeshBone bone = refSkeleton[i];
            bones.Add(new MeshPreviewBone
            {
                Name = bone.Name.ToString(),
                ParentIndex = bone.ParentIndex,
                LocalTransform = bone.BonePos.ToMatrix()
            });
        }

        for (int i = 0; i < bones.Count; i++)
        {
            MeshPreviewBone bone = bones[i];
            bone.GlobalTransform = bone.ParentIndex >= 0
                ? bone.LocalTransform * bones[bone.ParentIndex].GlobalTransform
                : bone.LocalTransform;
        }

        return bones;
    }

    private static byte RemapBone(byte localIndex, UArray<ushort> boneMap, Action<string> log)
    {
        if (localIndex < boneMap.Count)
            return (byte)boneMap[localIndex];

        log?.Invoke($"Missing bone mapping for local chunk bone {localIndex}; defaulting to root.");
        return 0;
    }

    private static int ResolveSectionIndex(int vertexIndex, List<MeshPreviewSection> sections, List<uint> indices)
    {
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            MeshPreviewSection section = sections[sectionIndex];
            for (int i = 0; i < section.IndexCount; i++)
            {
                if (indices[section.BaseIndex + i] == vertexIndex)
                    return sectionIndex;
            }
        }

        return 0;
    }

    private static Vector3 NormalizeOrFallback(Vector3 value)
    {
        return value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : Vector3.UnitY;
    }

    private static void RebuildSurfaceBasis(MeshPreviewMesh mesh)
    {
        if (mesh.Vertices.Count == 0 || mesh.Indices.Count == 0)
            return;

        Vector3[] normalSums = new Vector3[mesh.Vertices.Count];
        Vector3[] tangentSums = new Vector3[mesh.Vertices.Count];

        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int i0 = (int)mesh.Indices[i];
            int i1 = (int)mesh.Indices[i + 1];
            int i2 = (int)mesh.Indices[i + 2];
            if ((uint)i0 >= mesh.Vertices.Count || (uint)i1 >= mesh.Vertices.Count || (uint)i2 >= mesh.Vertices.Count)
                continue;

            MeshPreviewVertex v0 = mesh.Vertices[i0];
            MeshPreviewVertex v1 = mesh.Vertices[i1];
            MeshPreviewVertex v2 = mesh.Vertices[i2];

            Vector3 edge1 = v1.Position - v0.Position;
            Vector3 edge2 = v2.Position - v0.Position;
            Vector3 faceNormal = Vector3.Cross(edge1, edge2);
            if (faceNormal.LengthSquared() > 1e-10f)
            {
                normalSums[i0] += faceNormal;
                normalSums[i1] += faceNormal;
                normalSums[i2] += faceNormal;
            }

            Vector2 uv0 = v0.Uv;
            Vector2 uv1 = v1.Uv;
            Vector2 uv2 = v2.Uv;
            float determinant = ((uv1.X - uv0.X) * (uv2.Y - uv0.Y)) - ((uv1.Y - uv0.Y) * (uv2.X - uv0.X));
            if (MathF.Abs(determinant) <= 1e-10f)
                continue;

            float inverseDeterminant = 1.0f / determinant;
            Vector3 tangent = ((edge1 * (uv2.Y - uv0.Y)) - (edge2 * (uv1.Y - uv0.Y))) * inverseDeterminant;
            tangentSums[i0] += tangent;
            tangentSums[i1] += tangent;
            tangentSums[i2] += tangent;
        }

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            MeshPreviewVertex vertex = mesh.Vertices[i];
            Vector3 normal = NormalizeOrFallback(normalSums[i]);
            Vector3 tangent = tangentSums[i].LengthSquared() > 1e-10f
                ? Vector3.Normalize(tangentSums[i])
                : BuildFallbackTangent(normal);
            Vector3 bitangent = NormalizeOrFallback(Vector3.Cross(normal, tangent));

            vertex.Normal = normal;
            vertex.Tangent = tangent;
            vertex.Bitangent = bitangent;
            mesh.Vertices[i] = vertex;
        }
    }

    private static Vector3 BuildFallbackTangent(Vector3 normal)
    {
        Vector3 axis = Math.Abs(Vector3.Dot(normal, Vector3.UnitX)) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return NormalizeOrFallback(Vector3.Cross(axis, normal));
    }

    private static void BuildBounds(MeshPreviewMesh mesh)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (MeshPreviewVertex vertex in mesh.Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        mesh.Center = (min + max) * 0.5f;
        mesh.Radius = MathF.Max(1.0f, Vector3.Distance(mesh.Center, max));
    }

    private static void BuildUvSeams(MeshPreviewMesh mesh)
    {
        Dictionary<EdgeKey, HashSet<UvEdgeKey>> uvEdgesByPositionEdge = [];
        HashSet<EdgeKey> seamEdges = [];

        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int a = (int)mesh.Indices[i];
            int b = (int)mesh.Indices[i + 1];
            int c = (int)mesh.Indices[i + 2];

            RegisterTriangleEdge(mesh, a, b, uvEdgesByPositionEdge, seamEdges);
            RegisterTriangleEdge(mesh, b, c, uvEdgesByPositionEdge, seamEdges);
            RegisterTriangleEdge(mesh, c, a, uvEdgesByPositionEdge, seamEdges);
        }

        foreach (EdgeKey edge in seamEdges)
        {
            mesh.UvSeamLines.Add(edge.Start);
            mesh.UvSeamLines.Add(edge.End);
        }
    }

    private static void RegisterTriangleEdge(
        MeshPreviewMesh mesh,
        int startIndex,
        int endIndex,
        Dictionary<EdgeKey, HashSet<UvEdgeKey>> uvEdgesByPositionEdge,
        HashSet<EdgeKey> seamEdges)
    {
        if ((uint)startIndex >= mesh.Vertices.Count || (uint)endIndex >= mesh.Vertices.Count)
            return;

        MeshPreviewVertex start = mesh.Vertices[startIndex];
        MeshPreviewVertex end = mesh.Vertices[endIndex];
        EdgeKey positionEdge = EdgeKey.FromPositions(start.Position, end.Position);
        UvEdgeKey uvEdge = UvEdgeKey.FromUvs(start.Uv, end.Uv);

        if (!uvEdgesByPositionEdge.TryGetValue(positionEdge, out HashSet<UvEdgeKey> knownUvEdges))
        {
            knownUvEdges = [];
            uvEdgesByPositionEdge[positionEdge] = knownUvEdges;
        }

        if (knownUvEdges.Count > 0 && !knownUvEdges.Contains(uvEdge))
            seamEdges.Add(positionEdge);

        knownUvEdges.Add(uvEdge);
    }

    private static Vector3 Quantize(Vector3 value)
    {
        return new Vector3(
            MathF.Round(value.X, 4),
            MathF.Round(value.Y, 4),
            MathF.Round(value.Z, 4));
    }

    private static Vector2 Quantize(Vector2 value)
    {
        return new Vector2(
            MathF.Round(value.X, 4),
            MathF.Round(value.Y, 4));
    }

    private static int Compare(Vector3 left, Vector3 right)
    {
        int x = left.X.CompareTo(right.X);
        if (x != 0)
            return x;

        int y = left.Y.CompareTo(right.Y);
        if (y != 0)
            return y;

        return left.Z.CompareTo(right.Z);
    }

    private static int Compare(Vector2 left, Vector2 right)
    {
        int x = left.X.CompareTo(right.X);
        if (x != 0)
            return x;

        return left.Y.CompareTo(right.Y);
    }

    private readonly record struct EdgeKey(Vector3 Start, Vector3 End)
    {
        public static EdgeKey FromPositions(Vector3 a, Vector3 b)
        {
            Vector3 qa = Quantize(a);
            Vector3 qb = Quantize(b);
            return Compare(qa, qb) <= 0 ? new EdgeKey(qa, qb) : new EdgeKey(qb, qa);
        }
    }

    private readonly record struct UvEdgeKey(Vector2 Start, Vector2 End)
    {
        public static UvEdgeKey FromUvs(Vector2 a, Vector2 b)
        {
            Vector2 qa = Quantize(a);
            Vector2 qb = Quantize(b);
            return Compare(qa, qb) <= 0 ? new UvEdgeKey(qa, qb) : new UvEdgeKey(qb, qa);
        }
    }
}

internal static class PreviewPalette
{
    public static Vector4 ColorForIndex(int index)
    {
        Vector4[] colors =
        [
            new Vector4(0.92f, 0.35f, 0.28f, 1f),
            new Vector4(0.23f, 0.66f, 0.91f, 1f),
            new Vector4(0.36f, 0.78f, 0.46f, 1f),
            new Vector4(0.95f, 0.78f, 0.26f, 1f),
            new Vector4(0.72f, 0.45f, 0.92f, 1f),
            new Vector4(0.96f, 0.56f, 0.22f, 1f)
        ];

        return colors[Math.Abs(index) % colors.Length];
    }
}
