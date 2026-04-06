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
