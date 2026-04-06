using Assimp;
using System.Numerics;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;

namespace MHUpkManager.MeshPreview;

internal sealed class FbxToPreviewMeshConverter
{
    public MeshPreviewMesh Convert(string fbxPath, Action<string> log = null)
    {
        if (!File.Exists(fbxPath))
            throw new FileNotFoundException("FBX file not found.", fbxPath);

        using AssimpContext context = new();
        Scene scene = context.ImportFile(
            fbxPath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.ValidateDataStructure);

        if (scene == null || scene.MeshCount == 0)
            throw new InvalidOperationException("The FBX did not contain any renderable meshes.");

        MeshPreviewMesh previewMesh = new()
        {
            Name = Path.GetFileNameWithoutExtension(fbxPath)
        };

        HashSet<string> skinnedBoneNames = CollectSkinnedBoneNames(scene);
        Dictionary<string, int> boneIndexByName = [];
        BuildBoneHierarchy(scene.RootNode, -1, previewMesh.Bones, boneIndexByName, NumericsMatrix4x4.Identity, skinnedBoneNames);

        uint indexBase = 0;
        int sectionCounter = 0;
        foreach (Node node in EnumerateNodes(scene.RootNode))
        {
            NumericsMatrix4x4 nodeTransform = GetNodeGlobalTransform(node);
            foreach (int meshIndex in node.MeshIndices)
            {
                Mesh mesh = scene.Meshes[meshIndex];
                Dictionary<int, BoneWeightAccumulator> weightsByVertex = BuildWeights(mesh, boneIndexByName, log);
                int vertexBase = previewMesh.Vertices.Count;

                for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; vertexIndex++)
                {
                    Vector3 position = ConvertPosition(Vector3.Transform(new Vector3(mesh.Vertices[vertexIndex].X, mesh.Vertices[vertexIndex].Y, mesh.Vertices[vertexIndex].Z), nodeTransform));
                    Vector3 normal = mesh.HasNormals
                        ? ConvertDirection(Vector3.TransformNormal(new Vector3(mesh.Normals[vertexIndex].X, mesh.Normals[vertexIndex].Y, mesh.Normals[vertexIndex].Z), nodeTransform))
                        : Vector3.UnitY;
                    Vector3 tangent = mesh.HasTangentBasis
                        ? ConvertDirection(Vector3.TransformNormal(new Vector3(mesh.Tangents[vertexIndex].X, mesh.Tangents[vertexIndex].Y, mesh.Tangents[vertexIndex].Z), nodeTransform))
                        : BuildFallbackTangent(normal);
                    Vector3 bitangent = mesh.HasTangentBasis
                        ? ConvertDirection(Vector3.TransformNormal(new Vector3(mesh.BiTangents[vertexIndex].X, mesh.BiTangents[vertexIndex].Y, mesh.BiTangents[vertexIndex].Z), nodeTransform))
                        : Vector3.Normalize(Vector3.Cross(normal, tangent));
                    Vector2 uv = mesh.TextureCoordinateChannelCount > 0
                        ? new Vector2(mesh.TextureCoordinateChannels[0][vertexIndex].X, 1.0f - mesh.TextureCoordinateChannels[0][vertexIndex].Y)
                        : Vector2.Zero;

                    BoneWeightAccumulator weights = weightsByVertex.TryGetValue(vertexIndex, out BoneWeightAccumulator stored)
                        ? stored
                        : BoneWeightAccumulator.Empty;

                    previewMesh.Vertices.Add(new MeshPreviewVertex
                    {
                        Position = position,
                        Normal = NormalizeOrFallback(normal),
                        Tangent = NormalizeOrFallback(tangent),
                        Bitangent = NormalizeOrFallback(bitangent),
                        Uv = uv,
                        Bone0 = weights.Bones[0],
                        Bone1 = weights.Bones[1],
                        Bone2 = weights.Bones[2],
                        Bone3 = weights.Bones[3],
                        Weight0 = weights.Weights[0],
                        Weight1 = weights.Weights[1],
                        Weight2 = weights.Weights[2],
                        Weight3 = weights.Weights[3],
                        SectionIndex = sectionCounter
                    });
                }

                foreach (Face face in mesh.Faces)
                {
                    if (face.IndexCount != 3)
                        continue;

                    previewMesh.Indices.Add((uint)(vertexBase + face.Indices[0]));
                    previewMesh.Indices.Add((uint)(vertexBase + face.Indices[1]));
                    previewMesh.Indices.Add((uint)(vertexBase + face.Indices[2]));
                }

                previewMesh.Sections.Add(new MeshPreviewSection
                {
                    Index = sectionCounter,
                    MaterialIndex = mesh.MaterialIndex,
                    BaseIndex = (int)indexBase,
                    IndexCount = mesh.FaceCount * 3,
                    Name = string.IsNullOrWhiteSpace(mesh.Name) ? $"Mesh {meshIndex}" : mesh.Name,
                    Color = PreviewPalette.ColorForIndex(sectionCounter)
                });
                indexBase += (uint)(mesh.FaceCount * 3);
                sectionCounter++;
            }
        }

        BuildBounds(previewMesh);
        BuildUvSeams(previewMesh);
        log?.Invoke($"Loaded FBX preview with {previewMesh.Vertices.Count} vertices, {previewMesh.Indices.Count / 3} triangles, {previewMesh.Bones.Count} bones, and {previewMesh.Sections.Count} sections.");
        return previewMesh;
    }

    private static void BuildBoneHierarchy(
        Node node,
        int parentIndex,
        List<MeshPreviewBone> bones,
        Dictionary<string, int> boneIndexByName,
        NumericsMatrix4x4 parentTransform,
        IReadOnlySet<string> skinnedBoneNames)
    {
        NumericsMatrix4x4 local = ToNumerics(node.Transform);
        NumericsMatrix4x4 global = parentTransform * local;
        string nodeName = node.Name ?? string.Empty;
        int currentIndex = parentIndex;

        if (skinnedBoneNames.Contains(nodeName))
        {
            currentIndex = bones.Count;
            bones.Add(new MeshPreviewBone
            {
                Name = nodeName,
                ParentIndex = parentIndex,
                LocalTransform = ConvertTransform(local),
                GlobalTransform = ConvertTransform(global)
            });
            boneIndexByName[nodeName] = currentIndex;
        }

        foreach (Node child in node.Children)
            BuildBoneHierarchy(child, currentIndex, bones, boneIndexByName, global, skinnedBoneNames);
    }

    private static Dictionary<int, BoneWeightAccumulator> BuildWeights(Mesh mesh, Dictionary<string, int> boneIndexByName, Action<string> log)
    {
        Dictionary<int, List<(int BoneIndex, float Weight)>> raw = [];
        foreach (Assimp.Bone bone in mesh.Bones)
        {
            if (!boneIndexByName.TryGetValue(bone.Name, out int boneIndex))
            {
                log?.Invoke($"Missing FBX skeleton node for mesh bone '{bone.Name}'.");
                continue;
            }

            foreach (VertexWeight weight in bone.VertexWeights)
            {
                if (!raw.TryGetValue(weight.VertexID, out List<(int BoneIndex, float Weight)> weights))
                {
                    weights = [];
                    raw[weight.VertexID] = weights;
                }

                weights.Add((boneIndex, weight.Weight));
            }
        }

        Dictionary<int, BoneWeightAccumulator> result = [];
        foreach ((int vertexIndex, List<(int BoneIndex, float Weight)> weights) in raw)
            result[vertexIndex] = BoneWeightAccumulator.Normalize(weights, log);

        return result;
    }

    private static IEnumerable<Node> EnumerateNodes(Node root)
    {
        Stack<Node> stack = new();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            yield return node;
            for (int i = node.ChildCount - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }

    private static HashSet<string> CollectSkinnedBoneNames(Scene scene)
    {
        HashSet<string> names = [];
        foreach (Mesh mesh in scene.Meshes)
        {
            foreach (Assimp.Bone bone in mesh.Bones)
            {
                if (!string.IsNullOrWhiteSpace(bone.Name))
                    names.Add(bone.Name);
            }
        }

        return names;
    }

    private static NumericsMatrix4x4 GetNodeGlobalTransform(Node node)
    {
        NumericsMatrix4x4 transform = NumericsMatrix4x4.Identity;
        Node current = node;
        while (current != null)
        {
            transform = ToNumerics(current.Transform) * transform;
            current = current.Parent;
        }

        return transform;
    }

    private static NumericsMatrix4x4 ToNumerics(Assimp.Matrix4x4 value)
    {
        return new NumericsMatrix4x4(
            value.A1, value.B1, value.C1, value.D1,
            value.A2, value.B2, value.C2, value.D2,
            value.A3, value.B3, value.C3, value.D3,
            value.A4, value.B4, value.C4, value.D4);
    }

    private static NumericsMatrix4x4 ConvertTransform(NumericsMatrix4x4 value)
    {
        return new NumericsMatrix4x4(
            value.M11, value.M13, value.M12, value.M14,
            value.M31, value.M33, value.M32, value.M34,
            value.M21, value.M23, value.M22, value.M24,
            value.M41, value.M43, value.M42, value.M44);
    }

    private static Vector3 ConvertPosition(Vector3 value) => new(value.X, value.Z, value.Y);
    private static Vector3 ConvertDirection(Vector3 value) => new(value.X, value.Z, value.Y);

    private static Vector3 NormalizeOrFallback(Vector3 value)
    {
        return value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : Vector3.UnitY;
    }

    private static Vector3 BuildFallbackTangent(Vector3 normal)
    {
        Vector3 axis = Math.Abs(Vector3.Dot(normal, Vector3.UnitX)) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return Vector3.Normalize(Vector3.Cross(axis, normal));
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

    private static Vector3 Quantize(Vector3 value)
    {
        return new(
            MathF.Round(value.X, 4),
            MathF.Round(value.Y, 4),
            MathF.Round(value.Z, 4));
    }

    private static Vector2 Quantize(Vector2 value)
    {
        return new(
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

    private readonly record struct BoneWeightAccumulator(int[] Bones, float[] Weights)
    {
        public static BoneWeightAccumulator Empty => new([0, 0, 0, 0], [1.0f, 0.0f, 0.0f, 0.0f]);

        public static BoneWeightAccumulator Normalize(List<(int BoneIndex, float Weight)> weights, Action<string> log)
        {
            List<(int BoneIndex, float Weight)> ordered = [.. weights
                .Where(static x => x.Weight > 0.0f)
                .GroupBy(static x => x.BoneIndex)
                .Select(static x => (x.Key, x.Sum(static y => y.Weight)))
                .OrderByDescending(static x => x.Item2)
                .ThenBy(static x => x.Key)
                .Take(4)];

            if (weights.Count > 4)
                log?.Invoke("FBX preview dropped extra weights beyond 4 influences on at least one vertex.");

            if (ordered.Count == 0)
                return Empty;

            float total = ordered.Sum(static x => x.Weight);
            int[] bones = [0, 0, 0, 0];
            float[] normalized = [0f, 0f, 0f, 0f];
            for (int i = 0; i < ordered.Count; i++)
            {
                bones[i] = ordered[i].BoneIndex;
                normalized[i] = ordered[i].Weight / total;
            }

            return new BoneWeightAccumulator(bones, normalized);
        }
    }
}
