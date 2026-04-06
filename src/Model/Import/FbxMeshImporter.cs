using Assimp;
using System.Collections.Immutable;
using System.Numerics;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;

namespace MHUpkManager.Model.Import;

internal sealed class FbxMeshImporter
{
    private static bool EnableSurfaceVectorMerge => false;

    public NeutralMesh Import(string fbxPath)
    {
        if (!File.Exists(fbxPath))
            throw new FileNotFoundException("FBX file was not found.", fbxPath);

        using AssimpContext context = new();
        Scene scene = context.ImportFile(
            fbxPath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.ValidateDataStructure |
            PostProcessSteps.ImproveCacheLocality);

        if (scene == null || scene.MeshCount == 0)
            throw new InvalidOperationException("The FBX did not contain any meshes.");

        NeutralMesh neutralMesh = new();
        ReadNode(scene, scene.RootNode, NumericsMatrix4x4.Identity, neutralMesh);
        return neutralMesh;
    }

    private static void ReadNode(Scene scene, Node node, NumericsMatrix4x4 parentTransform, NeutralMesh neutralMesh)
    {
        NumericsMatrix4x4 nodeTransform = parentTransform * ToNumerics(node.Transform);

        foreach (int meshIndex in node.MeshIndices)
        {
            Mesh mesh = scene.Meshes[meshIndex];
            if (mesh.PrimitiveType != PrimitiveType.Triangle)
                continue;

            neutralMesh.Sections.Add(ReadSection(scene, mesh, nodeTransform));
        }

        foreach (Node child in node.Children)
            ReadNode(scene, child, nodeTransform, neutralMesh);
    }

    private static NeutralSection ReadSection(Scene scene, Mesh mesh, NumericsMatrix4x4 meshTransform)
    {
        string materialName = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount
            ? scene.Materials[mesh.MaterialIndex].Name ?? $"Material_{mesh.MaterialIndex}"
            : $"Material_{mesh.MaterialIndex}";

        NeutralSection section = new()
        {
            Name = string.IsNullOrWhiteSpace(mesh.Name) ? $"Mesh_{mesh.MaterialIndex}" : mesh.Name,
            MaterialName = materialName,
            ImportedMaterialIndex = mesh.MaterialIndex
        };

        Dictionary<int, List<VertexWeight>> weightsByVertex = BuildWeights(mesh);
        bool invertible = NumericsMatrix4x4.Invert(meshTransform, out NumericsMatrix4x4 inverseTransform);
        NumericsMatrix4x4 normalTransform = NumericsMatrix4x4.Transpose(invertible ? inverseTransform : NumericsMatrix4x4.Identity);

        for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; vertexIndex++)
        {
            Vector3 position = ConvertPosition(TransformPosition(mesh.Vertices[vertexIndex], meshTransform));
            Vector3 normal = mesh.HasNormals
                ? NormalizeOrUnitY(ConvertDirection(TransformDirection(mesh.Normals[vertexIndex], normalTransform)))
                : Vector3.UnitY;

            Vector3 tangent = mesh.HasTangentBasis
                ? NormalizeOrUnitY(ConvertDirection(TransformDirection(mesh.Tangents[vertexIndex], normalTransform)))
                : BuildFallbackTangent(normal);

            Vector3 bitangent = mesh.HasTangentBasis
                ? NormalizeOrUnitY(ConvertDirection(TransformDirection(mesh.BiTangents[vertexIndex], normalTransform)))
                : NormalizeOrUnitY(Vector3.Cross(normal, tangent));

            List<Vector2> uvs = [];
            for (int channel = 0; channel < mesh.TextureCoordinateChannelCount; channel++)
            {
                Vector3D uv = mesh.TextureCoordinateChannels[channel][vertexIndex];
                uvs.Add(new Vector2(uv.X, 1.0f - uv.Y));
            }

            section.Vertices.Add(new NeutralVertex
            {
                Position = position,
                Normal = normal,
                Tangent = tangent,
                Bitangent = bitangent,
                UVs = uvs,
                Weights = weightsByVertex.TryGetValue(vertexIndex, out var weights) ? weights : []
            });
        }

        foreach (Face face in mesh.Faces)
        {
            if (face.IndexCount != 3)
                throw new InvalidOperationException("The FBX contains a non-triangulated face after triangulation.");

            section.Indices.Add(face.Indices[0]);
            section.Indices.Add(face.Indices[1]);
            section.Indices.Add(face.Indices[2]);
        }

        section.ImportedVertexCount = section.Vertices.Count;
        section.ImportedTriangleCount = section.Indices.Count / 3;
        section.ImportedUniquePositionCount = CountUniquePositions(section.Vertices);
        section.ImportedUniqueFullVertexCount = CountUniqueVertices(section.Vertices);
        section.ImportedUniqueVerticesIgnoringSurfaceVectors = CountUniqueVerticesIgnoringSurfaceVectors(section.Vertices);
        section.ImportedMergeableVerticesIgnoringSurfaceVectors =
            section.ImportedVertexCount - section.ImportedUniqueVerticesIgnoringSurfaceVectors;
        PopulatePositionSplitDiagnostics(section);
        WeldExactDuplicateVertices(section);
        section.ImportedExactWeldVertexCount = section.Vertices.Count;
        if (EnableSurfaceVectorMerge)
            MergeVerticesIgnoringSurfaceVectors(section);
        section.ImportedMergedVertexCount = section.Vertices.Count;
        return section;
    }

    private static void WeldExactDuplicateVertices(NeutralSection section)
    {
        if (section.Vertices.Count == 0 || section.Indices.Count == 0)
            return;

        Dictionary<VertexKey, int> remap = new(VertexKeyComparer.Instance);
        List<NeutralVertex> weldedVertices = new(section.Vertices.Count);
        int[] indexMap = new int[section.Vertices.Count];

        for (int i = 0; i < section.Vertices.Count; i++)
        {
            NeutralVertex vertex = section.Vertices[i];
            VertexKey key = VertexKey.Create(vertex);
            if (!remap.TryGetValue(key, out int weldedIndex))
            {
                weldedIndex = weldedVertices.Count;
                remap[key] = weldedIndex;
                weldedVertices.Add(vertex);
            }

            indexMap[i] = weldedIndex;
        }

        for (int i = 0; i < section.Indices.Count; i++)
            section.Indices[i] = indexMap[section.Indices[i]];

        if (weldedVertices.Count == section.Vertices.Count)
            return;

        section.Vertices.Clear();
        section.Vertices.AddRange(weldedVertices);
    }

    private static void MergeVerticesIgnoringSurfaceVectors(NeutralSection section)
    {
        if (section.Vertices.Count == 0 || section.Indices.Count == 0)
            return;

        Dictionary<VertexUvWeightKey, int> remap = new(VertexUvWeightKeyComparer.Instance);
        List<NeutralVertex> mergedVertices = new(section.Vertices.Count);
        int[] indexMap = new int[section.Vertices.Count];

        for (int i = 0; i < section.Vertices.Count; i++)
        {
            NeutralVertex vertex = section.Vertices[i];
            VertexUvWeightKey key = VertexUvWeightKey.Create(vertex);
            if (!remap.TryGetValue(key, out int mergedIndex))
            {
                mergedIndex = mergedVertices.Count;
                remap[key] = mergedIndex;
                mergedVertices.Add(vertex);
            }

            indexMap[i] = mergedIndex;
        }

        if (mergedVertices.Count == section.Vertices.Count)
            return;

        for (int i = 0; i < section.Indices.Count; i++)
            section.Indices[i] = indexMap[section.Indices[i]];

        List<NeutralVertex> regeneratedVertices = RegenerateSurfaceVectors(mergedVertices, section.Indices);
        section.Vertices.Clear();
        section.Vertices.AddRange(regeneratedVertices);
    }

    private static List<NeutralVertex> RegenerateSurfaceVectors(
        IReadOnlyList<NeutralVertex> vertices,
        IReadOnlyList<int> indices)
    {
        Vector3[] normalSums = new Vector3[vertices.Count];
        Vector3[] tangentSums = new Vector3[vertices.Count];
        Vector3[] bitangentSums = new Vector3[vertices.Count];

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            NeutralVertex v0 = vertices[i0];
            NeutralVertex v1 = vertices[i1];
            NeutralVertex v2 = vertices[i2];

            Vector3 edge1 = v1.Position - v0.Position;
            Vector3 edge2 = v2.Position - v0.Position;
            Vector3 faceNormal = Vector3.Cross(edge1, edge2);
            if (faceNormal.LengthSquared() > 1e-10f)
            {
                normalSums[i0] += faceNormal;
                normalSums[i1] += faceNormal;
                normalSums[i2] += faceNormal;
            }

            Vector2 uv0 = v0.UVs.Count > 0 ? v0.UVs[0] : Vector2.Zero;
            Vector2 uv1 = v1.UVs.Count > 0 ? v1.UVs[0] : Vector2.Zero;
            Vector2 uv2 = v2.UVs.Count > 0 ? v2.UVs[0] : Vector2.Zero;
            Vector2 deltaUv1 = uv1 - uv0;
            Vector2 deltaUv2 = uv2 - uv0;
            float determinant = (deltaUv1.X * deltaUv2.Y) - (deltaUv1.Y * deltaUv2.X);
            if (MathF.Abs(determinant) <= 1e-10f)
                continue;

            float invDeterminant = 1.0f / determinant;
            Vector3 tangent = (edge1 * deltaUv2.Y - edge2 * deltaUv1.Y) * invDeterminant;
            Vector3 bitangent = (edge2 * deltaUv1.X - edge1 * deltaUv2.X) * invDeterminant;

            tangentSums[i0] += tangent;
            tangentSums[i1] += tangent;
            tangentSums[i2] += tangent;
            bitangentSums[i0] += bitangent;
            bitangentSums[i1] += bitangent;
            bitangentSums[i2] += bitangent;
        }

        List<NeutralVertex> regenerated = new(vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            NeutralVertex vertex = vertices[i];
            Vector3 normal = NormalizeOrUnitY(normalSums[i]);

            Vector3 tangent = tangentSums[i];
            tangent -= normal * Vector3.Dot(normal, tangent);
            tangent = tangent.LengthSquared() > 1e-10f ? Vector3.Normalize(tangent) : BuildFallbackTangent(normal);

            Vector3 bitangentReference = bitangentSums[i];
            Vector3 basisBitangent = NormalizeOrUnitY(Vector3.Cross(normal, tangent));
            if (bitangentReference.LengthSquared() > 1e-10f && Vector3.Dot(basisBitangent, bitangentReference) < 0.0f)
                basisBitangent = -basisBitangent;

            regenerated.Add(new NeutralVertex
            {
                Position = vertex.Position,
                Normal = normal,
                Tangent = tangent,
                Bitangent = basisBitangent,
                UVs = vertex.UVs,
                Weights = vertex.Weights
            });
        }

        return regenerated;
    }

    private static int CountUniquePositions(IReadOnlyList<NeutralVertex> vertices)
    {
        HashSet<Float3Key> unique = [];
        foreach (NeutralVertex vertex in vertices)
            unique.Add(Float3Key.Create(vertex.Position));

        return unique.Count;
    }

    private static int CountUniqueVertices(IReadOnlyList<NeutralVertex> vertices)
    {
        HashSet<VertexKey> unique = new(VertexKeyComparer.Instance);
        foreach (NeutralVertex vertex in vertices)
            unique.Add(VertexKey.Create(vertex));

        return unique.Count;
    }

    private static int CountUniqueVerticesIgnoringSurfaceVectors(IReadOnlyList<NeutralVertex> vertices)
    {
        HashSet<VertexUvWeightKey> unique = new(VertexUvWeightKeyComparer.Instance);
        foreach (NeutralVertex vertex in vertices)
            unique.Add(VertexUvWeightKey.Create(vertex));

        return unique.Count;
    }

    private static void PopulatePositionSplitDiagnostics(NeutralSection section)
    {
        Dictionary<Float3Key, List<NeutralVertex>> groups = [];
        foreach (NeutralVertex vertex in section.Vertices)
        {
            Float3Key key = Float3Key.Create(vertex.Position);
            if (!groups.TryGetValue(key, out List<NeutralVertex> list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(vertex);
        }

        int splitGroupCount = 0;
        int splitVertices = 0;
        int uvSplitGroupCount = 0;
        int uvSplitVertices = 0;
        int normalSplitGroupCount = 0;
        int normalSplitVertices = 0;
        int weightSplitGroupCount = 0;
        int weightSplitVertices = 0;
        int uvOnlySplitGroupCount = 0;
        int uvOnlySplitVertices = 0;
        int normalOnlySplitGroupCount = 0;
        int normalOnlySplitVertices = 0;
        int uvAndNormalSplitGroupCount = 0;
        int uvAndNormalSplitVertices = 0;
        int maxVerticesPerPosition = 0;

        foreach ((_, List<NeutralVertex> group) in groups)
        {
            maxVerticesPerPosition = Math.Max(maxVerticesPerPosition, group.Count);
            if (group.Count <= 1)
                continue;

            splitGroupCount++;
            splitVertices += group.Count - 1;

            bool hasUvSplit = CountUniqueUvs(group) > 1;
            bool hasNormalSplit = CountUniqueSurfaceVectors(group) > 1;
            bool hasWeightSplit = CountUniqueWeights(group) > 1;

            if (hasUvSplit)
            {
                uvSplitGroupCount++;
                uvSplitVertices += group.Count - 1;
            }

            if (hasNormalSplit)
            {
                normalSplitGroupCount++;
                normalSplitVertices += group.Count - 1;
            }

            if (hasWeightSplit)
            {
                weightSplitGroupCount++;
                weightSplitVertices += group.Count - 1;
            }

            if (hasUvSplit && hasNormalSplit)
            {
                uvAndNormalSplitGroupCount++;
                uvAndNormalSplitVertices += group.Count - 1;
            }
            else if (hasUvSplit)
            {
                uvOnlySplitGroupCount++;
                uvOnlySplitVertices += group.Count - 1;
            }
            else if (hasNormalSplit)
            {
                normalOnlySplitGroupCount++;
                normalOnlySplitVertices += group.Count - 1;
            }
        }

        section.ImportedSplitPositionGroupCount = splitGroupCount;
        section.ImportedSplitVerticesFromPositionGroups = splitVertices;
        section.ImportedPositionGroupsWithUvSplits = uvSplitGroupCount;
        section.ImportedSplitVerticesFromUvSplits = uvSplitVertices;
        section.ImportedPositionGroupsWithNormalSplits = normalSplitGroupCount;
        section.ImportedSplitVerticesFromNormalSplits = normalSplitVertices;
        section.ImportedPositionGroupsWithWeightSplits = weightSplitGroupCount;
        section.ImportedSplitVerticesFromWeightSplits = weightSplitVertices;
        section.ImportedPositionGroupsWithUvOnlySplits = uvOnlySplitGroupCount;
        section.ImportedSplitVerticesFromUvOnlySplits = uvOnlySplitVertices;
        section.ImportedPositionGroupsWithNormalOnlySplits = normalOnlySplitGroupCount;
        section.ImportedSplitVerticesFromNormalOnlySplits = normalOnlySplitVertices;
        section.ImportedPositionGroupsWithUvAndNormalSplits = uvAndNormalSplitGroupCount;
        section.ImportedSplitVerticesFromUvAndNormalSplits = uvAndNormalSplitVertices;
        section.ImportedMaxVerticesPerPosition = maxVerticesPerPosition;
    }

    private static int CountUniqueUvs(IReadOnlyList<NeutralVertex> vertices)
    {
        HashSet<ImmutableArray<Float2Key>> unique = ImmutableArrayComparer<Float2Key>.CreateHashSet();
        foreach (NeutralVertex vertex in vertices)
            unique.Add([.. vertex.UVs.Select(Float2Key.Create)]);

        return unique.Count;
    }

    private static int CountUniqueSurfaceVectors(IReadOnlyList<NeutralVertex> vertices)
    {
        HashSet<SurfaceKey> unique = [];
        foreach (NeutralVertex vertex in vertices)
            unique.Add(SurfaceKey.Create(vertex));

        return unique.Count;
    }

    private static int CountUniqueWeights(IReadOnlyList<NeutralVertex> vertices)
    {
        HashSet<ImmutableArray<VertexWeightKey>> unique = ImmutableArrayComparer<VertexWeightKey>.CreateHashSet();
        foreach (NeutralVertex vertex in vertices)
            unique.Add([.. vertex.Weights.Select(VertexWeightKey.Create)]);

        return unique.Count;
    }

    private static Dictionary<int, List<VertexWeight>> BuildWeights(Mesh mesh)
    {
        Dictionary<int, List<VertexWeight>> weightsByVertex = [];
        foreach (Assimp.Bone bone in mesh.Bones)
        {
            foreach (Assimp.VertexWeight vertexWeight in bone.VertexWeights)
            {
                if (!weightsByVertex.TryGetValue(vertexWeight.VertexID, out var list))
                {
                    list = [];
                    weightsByVertex[vertexWeight.VertexID] = list;
                }

                list.Add(new VertexWeight(bone.Name, vertexWeight.Weight));
            }
        }

        return weightsByVertex;
    }

    private static NumericsMatrix4x4 ToNumerics(Assimp.Matrix4x4 value)
    {
        return new NumericsMatrix4x4(
            value.A1, value.B1, value.C1, value.D1,
            value.A2, value.B2, value.C2, value.D2,
            value.A3, value.B3, value.C3, value.D3,
            value.A4, value.B4, value.C4, value.D4);
    }

    private static Vector3 TransformPosition(Vector3D value, NumericsMatrix4x4 transform)
    {
        return Vector3.Transform(new Vector3(value.X, value.Y, value.Z), transform);
    }

    private static Vector3 TransformDirection(Vector3D value, NumericsMatrix4x4 transform)
    {
        return Vector3.TransformNormal(new Vector3(value.X, value.Y, value.Z), transform);
    }

    private static Vector3 ConvertPosition(Vector3 value) => new(value.X, value.Z, value.Y);
    private static Vector3 ConvertDirection(Vector3 value) => new(value.X, value.Z, value.Y);

    private static Vector3 NormalizeOrUnitY(Vector3 value)
    {
        return value.LengthSquared() > 1e-10f ? Vector3.Normalize(value) : Vector3.UnitY;
    }

    private static Vector3 BuildFallbackTangent(Vector3 normal)
    {
        Vector3 axis = MathF.Abs(Vector3.Dot(normal, Vector3.UnitX)) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        return NormalizeOrUnitY(Vector3.Cross(axis, normal));
    }

    private readonly record struct VertexKey(
        Float3Key Position,
        Float3Key Normal,
        Float3Key Tangent,
        Float3Key Bitangent,
        ImmutableArray<Float2Key> Uvs,
        ImmutableArray<VertexWeightKey> Weights)
    {
        public static VertexKey Create(NeutralVertex vertex)
        {
            return new VertexKey(
                Float3Key.Create(vertex.Position),
                Float3Key.Create(vertex.Normal),
                Float3Key.Create(vertex.Tangent),
                Float3Key.Create(vertex.Bitangent),
                [.. vertex.UVs.Select(Float2Key.Create)],
                [.. vertex.Weights.Select(VertexWeightKey.Create)]);
        }
    }

    private readonly record struct Float3Key(int X, int Y, int Z)
    {
        public static Float3Key Create(Vector3 value)
        {
            return new Float3Key(
                BitConverter.SingleToInt32Bits(value.X),
                BitConverter.SingleToInt32Bits(value.Y),
                BitConverter.SingleToInt32Bits(value.Z));
        }
    }

    private readonly record struct Float2Key(int X, int Y)
    {
        public static Float2Key Create(Vector2 value)
        {
            return new Float2Key(
                BitConverter.SingleToInt32Bits(value.X),
                BitConverter.SingleToInt32Bits(value.Y));
        }
    }

    private readonly record struct SurfaceKey(Float3Key Normal, Float3Key Tangent, Float3Key Bitangent)
    {
        public static SurfaceKey Create(NeutralVertex vertex)
        {
            return new SurfaceKey(
                Float3Key.Create(vertex.Normal),
                Float3Key.Create(vertex.Tangent),
                Float3Key.Create(vertex.Bitangent));
        }
    }

    private readonly record struct VertexUvWeightKey(
        Float3Key Position,
        ImmutableArray<Float2Key> Uvs,
        ImmutableArray<VertexWeightKey> Weights)
    {
        public static VertexUvWeightKey Create(NeutralVertex vertex)
        {
            return new VertexUvWeightKey(
                Float3Key.Create(vertex.Position),
                [.. vertex.UVs.Select(Float2Key.Create)],
                [.. vertex.Weights.Select(VertexWeightKey.Create)]);
        }
    }

    private readonly record struct VertexWeightKey(string BoneName, int WeightBits)
    {
        public static VertexWeightKey Create(VertexWeight value)
        {
            return new VertexWeightKey(value.BoneName, BitConverter.SingleToInt32Bits(value.Weight));
        }
    }

    private sealed class VertexKeyComparer : IEqualityComparer<VertexKey>
    {
        public static VertexKeyComparer Instance { get; } = new();

        public bool Equals(VertexKey x, VertexKey y)
        {
            return x.Position.Equals(y.Position) &&
                x.Normal.Equals(y.Normal) &&
                x.Tangent.Equals(y.Tangent) &&
                x.Bitangent.Equals(y.Bitangent) &&
                ImmutableArrayComparer<Float2Key>.Instance.Equals(x.Uvs, y.Uvs) &&
                ImmutableArrayComparer<VertexWeightKey>.Instance.Equals(x.Weights, y.Weights);
        }

        public int GetHashCode(VertexKey obj)
        {
            HashCode hash = new();
            hash.Add(obj.Position);
            hash.Add(obj.Normal);
            hash.Add(obj.Tangent);
            hash.Add(obj.Bitangent);
            hash.Add(ImmutableArrayComparer<Float2Key>.Instance.GetHashCode(obj.Uvs));
            hash.Add(ImmutableArrayComparer<VertexWeightKey>.Instance.GetHashCode(obj.Weights));
            return hash.ToHashCode();
        }
    }

    private sealed class VertexUvWeightKeyComparer : IEqualityComparer<VertexUvWeightKey>
    {
        public static VertexUvWeightKeyComparer Instance { get; } = new();

        public bool Equals(VertexUvWeightKey x, VertexUvWeightKey y)
        {
            return x.Position.Equals(y.Position) &&
                ImmutableArrayComparer<Float2Key>.Instance.Equals(x.Uvs, y.Uvs) &&
                ImmutableArrayComparer<VertexWeightKey>.Instance.Equals(x.Weights, y.Weights);
        }

        public int GetHashCode(VertexUvWeightKey obj)
        {
            HashCode hash = new();
            hash.Add(obj.Position);
            hash.Add(ImmutableArrayComparer<Float2Key>.Instance.GetHashCode(obj.Uvs));
            hash.Add(ImmutableArrayComparer<VertexWeightKey>.Instance.GetHashCode(obj.Weights));
            return hash.ToHashCode();
        }
    }

    private sealed class ImmutableArrayComparer<T> : IEqualityComparer<ImmutableArray<T>>
        where T : notnull
    {
        public static ImmutableArrayComparer<T> Instance { get; } = new();

        public static HashSet<ImmutableArray<T>> CreateHashSet() => new(Instance);

        public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y)
        {
            return x.AsSpan().SequenceEqual(y.AsSpan());
        }

        public int GetHashCode(ImmutableArray<T> obj)
        {
            HashCode hash = new();
            foreach (T item in obj)
                hash.Add(item);

            return hash.ToHashCode();
        }
    }
}
