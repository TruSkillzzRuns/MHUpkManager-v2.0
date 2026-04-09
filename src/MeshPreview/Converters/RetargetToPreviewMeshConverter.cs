using System.Numerics;
using MHUpkManager.Retargeting;

namespace MHUpkManager.MeshPreview;

internal sealed class RetargetToPreviewMeshConverter
{
    public MeshPreviewMesh Convert(RetargetMesh retargetMesh, string name, Action<string> log = null)
    {
        if (retargetMesh == null)
            throw new ArgumentNullException(nameof(retargetMesh));

        retargetMesh.RebuildBoneLookup();
        Dictionary<string, int> boneIndexByName = retargetMesh.Bones
            .Select((bone, index) => new { bone.Name, index })
            .ToDictionary(static entry => entry.Name, static entry => entry.index, StringComparer.OrdinalIgnoreCase);

        MeshPreviewMesh previewMesh = new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? retargetMesh.MeshName : name
        };

        int vertexBase = 0;
        int indexBase = 0;
        for (int sectionIndex = 0; sectionIndex < retargetMesh.Sections.Count; sectionIndex++)
        {
            RetargetSection section = retargetMesh.Sections[sectionIndex];
            foreach (RetargetVertex vertex in section.Vertices)
            {
                (int[] bones, float[] weights) = ResolveWeights(vertex.Weights, boneIndexByName);
                previewMesh.Vertices.Add(new MeshPreviewVertex
                {
                    Position = vertex.Position,
                    Normal = NormalizeOrFallback(vertex.Normal),
                    Tangent = NormalizeOrFallback(vertex.Tangent),
                    Bitangent = NormalizeOrFallback(vertex.Bitangent),
                    Uv = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.Zero,
                    Bone0 = bones[0],
                    Bone1 = bones[1],
                    Bone2 = bones[2],
                    Bone3 = bones[3],
                    Weight0 = weights[0],
                    Weight1 = weights[1],
                    Weight2 = weights[2],
                    Weight3 = weights[3],
                    SectionIndex = sectionIndex
                });
            }

            for (int i = 0; i + 2 < section.Indices.Count; i += 3)
            {
                previewMesh.Indices.Add((uint)(vertexBase + section.Indices[i]));
                previewMesh.Indices.Add((uint)(vertexBase + section.Indices[i + 2]));
                previewMesh.Indices.Add((uint)(vertexBase + section.Indices[i + 1]));
            }

            previewMesh.Sections.Add(new MeshPreviewSection
            {
                Index = sectionIndex,
                MaterialIndex = section.MaterialIndex,
                BaseIndex = indexBase,
                IndexCount = section.Indices.Count,
                Name = string.IsNullOrWhiteSpace(section.Name) ? $"Section {sectionIndex}" : section.Name,
                Color = PreviewPalette.ColorForIndex(sectionIndex)
            });

            vertexBase += section.Vertices.Count;
            indexBase += section.Indices.Count;
        }

        foreach (RetargetBone bone in retargetMesh.Bones)
        {
            previewMesh.Bones.Add(new MeshPreviewBone
            {
                Name = bone.Name,
                ParentIndex = bone.ParentIndex,
                LocalTransform = bone.LocalTransform,
                GlobalTransform = bone.GlobalTransform
            });
        }

        RebuildSurfaceBasis(previewMesh);
        BuildBounds(previewMesh);
        BuildUvSeams(previewMesh);
        log?.Invoke($"Pose preview mesh '{previewMesh.Name}' prepared with {previewMesh.Vertices.Count} vertices, {previewMesh.Indices.Count / 3} triangles, and {previewMesh.Bones.Count} bones.");
        return previewMesh;
    }

    private static (int[] Bones, float[] Weights) ResolveWeights(IReadOnlyList<RetargetWeight> sourceWeights, Dictionary<string, int> boneIndexByName)
    {
        int[] bones = [0, 0, 0, 0];
        float[] weights = [0f, 0f, 0f, 0f];

        List<(int BoneIndex, float Weight)> ordered = sourceWeights
            .Where(static weight => weight.Weight > 0.0f)
            .Select(weight => (boneIndexByName.TryGetValue(weight.BoneName, out int index) ? index : 0, weight.Weight))
            .OrderByDescending(static weight => weight.Weight)
            .Take(4)
            .ToList();

        float total = ordered.Sum(static weight => weight.Weight);
        if (total <= 1e-5f)
            return (bones, weights);

        for (int i = 0; i < ordered.Count; i++)
        {
            bones[i] = ordered[i].BoneIndex;
            weights[i] = ordered[i].Weight / total;
        }

        return (bones, weights);
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
        Vector3 axis = MathF.Abs(Vector3.Dot(normal, Vector3.UnitY)) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
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
        return new Vector3(MathF.Round(value.X, 4), MathF.Round(value.Y, 4), MathF.Round(value.Z, 4));
    }

    private static Vector2 Quantize(Vector2 value)
    {
        return new Vector2(MathF.Round(value.X, 4), MathF.Round(value.Y, 4));
    }

    private static int Compare(Vector3 left, Vector3 right)
    {
        int x = left.X.CompareTo(right.X);
        if (x != 0)
            return x;
        int y = left.Y.CompareTo(right.Y);
        return y != 0 ? y : left.Z.CompareTo(right.Z);
    }

    private static int Compare(Vector2 left, Vector2 right)
    {
        int x = left.X.CompareTo(right.X);
        return x != 0 ? x : left.Y.CompareTo(right.Y);
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
