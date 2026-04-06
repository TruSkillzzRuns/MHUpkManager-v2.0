using Assimp;
using System.Collections.Immutable;
using System.Numerics;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;

namespace MHUpkManager.Retargeting;

internal sealed class MeshImporter
{
    public RetargetMesh Import(string meshPath, Action<string> log = null)
    {
        if (string.IsNullOrWhiteSpace(meshPath))
            throw new ArgumentException("Mesh path is required.", nameof(meshPath));

        string extension = Path.GetExtension(meshPath);
        if (extension.Equals(".psk", StringComparison.OrdinalIgnoreCase))
            return ImportPsk(meshPath, log);

        return ImportViaAssimp(meshPath, log);
    }

    private static RetargetMesh ImportViaAssimp(string meshPath, Action<string> log)
    {
        if (!File.Exists(meshPath))
            throw new FileNotFoundException("Mesh file was not found.", meshPath);

        using AssimpContext context = new();
        Scene scene = context.ImportFile(
            meshPath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.CalculateTangentSpace |
            PostProcessSteps.ValidateDataStructure |
            PostProcessSteps.ImproveCacheLocality);

        if (scene == null || scene.MeshCount == 0)
            throw new InvalidOperationException("The selected mesh file did not contain any meshes.");

        RetargetMesh mesh = new()
        {
            SourcePath = meshPath,
            MeshName = Path.GetFileNameWithoutExtension(meshPath)
        };

        PopulateBonesFromScene(scene, mesh, log);
        ReadSceneNode(scene, scene.RootNode, NumericsMatrix4x4.Identity, mesh);
        mesh.RebuildBoneLookup();
        log?.Invoke($"Imported {mesh.Sections.Count} section(s), {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles, and {mesh.Bones.Count} bones from {meshPath}.");
        return mesh;
    }

    private static void PopulateBonesFromScene(Scene scene, RetargetMesh mesh, Action<string> log)
    {
        HashSet<string> meshBoneNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (Assimp.Mesh sourceMesh in scene.Meshes)
        {
            foreach (Assimp.Bone bone in sourceMesh.Bones)
                meshBoneNames.Add(bone.Name);
        }

        if (meshBoneNames.Count == 0)
        {
            log?.Invoke("Imported mesh did not expose explicit bone data. Bone hierarchy extraction will be limited.");
            return;
        }

        Dictionary<string, int> nodeToIndex = new(StringComparer.OrdinalIgnoreCase);

        void AddNodeRecursive(Node node, int parentIndex, NumericsMatrix4x4 parentTransform)
        {
            bool keepNode = meshBoneNames.Contains(node.Name) || ContainsTrackedDescendant(node, meshBoneNames);
            NumericsMatrix4x4 localTransform = ToNumerics(node.Transform);
            NumericsMatrix4x4 globalTransform = parentTransform * localTransform;
            int currentIndex = parentIndex;

            if (keepNode)
            {
                currentIndex = mesh.Bones.Count;
                mesh.Bones.Add(new RetargetBone
                {
                    Name = node.Name,
                    ParentIndex = parentIndex,
                    LocalTransform = ConvertTransform(localTransform),
                    GlobalTransform = ConvertTransform(globalTransform)
                });
                nodeToIndex[node.Name] = currentIndex;
            }

            foreach (Node child in node.Children)
                AddNodeRecursive(child, currentIndex, globalTransform);
        }

        AddNodeRecursive(scene.RootNode, -1, NumericsMatrix4x4.Identity);
    }

    private static bool ContainsTrackedDescendant(Node node, HashSet<string> trackedNames)
    {
        foreach (Node child in node.Children)
        {
            if (trackedNames.Contains(child.Name) || ContainsTrackedDescendant(child, trackedNames))
                return true;
        }

        return false;
    }

    private static void ReadSceneNode(Scene scene, Node node, NumericsMatrix4x4 parentTransform, RetargetMesh mesh)
    {
        NumericsMatrix4x4 nodeTransform = parentTransform * ToNumerics(node.Transform);

        foreach (int meshIndex in node.MeshIndices)
        {
            Assimp.Mesh assimpMesh = scene.Meshes[meshIndex];
            if (assimpMesh.PrimitiveType != PrimitiveType.Triangle)
                continue;

            mesh.Sections.Add(ReadSection(scene, assimpMesh, nodeTransform));
        }

        foreach (Node child in node.Children)
            ReadSceneNode(scene, child, nodeTransform, mesh);
    }

    private static RetargetSection ReadSection(Scene scene, Assimp.Mesh mesh, NumericsMatrix4x4 meshTransform)
    {
        RetargetSection section = new()
        {
            Name = string.IsNullOrWhiteSpace(mesh.Name) ? $"Mesh_{mesh.MaterialIndex}" : mesh.Name,
            MaterialName = ResolveMaterialName(scene, mesh.MaterialIndex),
            MaterialIndex = mesh.MaterialIndex
        };

        Dictionary<int, List<RetargetWeight>> weightsByVertex = BuildWeights(mesh);
        bool invertible = NumericsMatrix4x4.Invert(meshTransform, out NumericsMatrix4x4 inverseTransform);
        NumericsMatrix4x4 normalTransform = NumericsMatrix4x4.Transpose(invertible ? inverseTransform : NumericsMatrix4x4.Identity);
        bool hasVertexColors = mesh.VertexColorChannelCount > 0 && mesh.HasVertexColors(0);

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

            RetargetVertex vertex = new()
            {
                Position = position,
                Normal = normal,
                Tangent = tangent,
                Bitangent = bitangent,
                Color = hasVertexColors
                    ? ToFColor(mesh.VertexColorChannels[0][vertexIndex])
                    : new UpkManager.Models.UpkFile.Core.FColor { R = 255, G = 255, B = 255, A = 255 }
            };

            for (int channel = 0; channel < mesh.TextureCoordinateChannelCount; channel++)
            {
                Vector3D uv = mesh.TextureCoordinateChannels[channel][vertexIndex];
                vertex.UVs.Add(new Vector2(uv.X, 1.0f - uv.Y));
            }

            if (weightsByVertex.TryGetValue(vertexIndex, out List<RetargetWeight> weights))
                vertex.Weights.AddRange(weights);

            section.Vertices.Add(vertex);
        }

        foreach (Face face in mesh.Faces)
        {
            if (face.IndexCount != 3)
                throw new InvalidOperationException("The imported mesh contains a non-triangulated face.");

            section.Indices.Add(face.Indices[0]);
            section.Indices.Add(face.Indices[1]);
            section.Indices.Add(face.Indices[2]);
            section.TriangleSmoothingGroups.Add(1);
        }

        WeldExactDuplicateVertices(section);
        return section;
    }

    private static string ResolveMaterialName(Scene scene, int materialIndex)
    {
        if (materialIndex < 0 || materialIndex >= scene.MaterialCount)
            return $"Material_{materialIndex}";

        return string.IsNullOrWhiteSpace(scene.Materials[materialIndex].Name)
            ? $"Material_{materialIndex}"
            : scene.Materials[materialIndex].Name;
    }

    private static Dictionary<int, List<RetargetWeight>> BuildWeights(Assimp.Mesh mesh)
    {
        Dictionary<int, List<RetargetWeight>> weightsByVertex = [];
        foreach (Assimp.Bone bone in mesh.Bones)
        {
            foreach (Assimp.VertexWeight vertexWeight in bone.VertexWeights)
            {
                if (!weightsByVertex.TryGetValue(vertexWeight.VertexID, out List<RetargetWeight> weights))
                {
                    weights = [];
                    weightsByVertex[vertexWeight.VertexID] = weights;
                }

                weights.Add(new RetargetWeight(bone.Name, vertexWeight.Weight));
            }
        }

        return weightsByVertex;
    }

    private static void WeldExactDuplicateVertices(RetargetSection section)
    {
        if (section.Vertices.Count == 0 || section.Indices.Count == 0)
            return;

        Dictionary<VertexKey, int> remap = new(VertexKeyComparer.Instance);
        List<RetargetVertex> welded = new(section.Vertices.Count);
        int[] map = new int[section.Vertices.Count];

        for (int i = 0; i < section.Vertices.Count; i++)
        {
            RetargetVertex vertex = section.Vertices[i];
            VertexKey key = VertexKey.Create(vertex);
            if (!remap.TryGetValue(key, out int newIndex))
            {
                newIndex = welded.Count;
                remap[key] = newIndex;
                welded.Add(vertex);
            }

            map[i] = newIndex;
        }

        for (int i = 0; i < section.Indices.Count; i++)
            section.Indices[i] = map[section.Indices[i]];

        section.Vertices.Clear();
        section.Vertices.AddRange(welded);
    }

    private static RetargetMesh ImportPsk(string meshPath, Action<string> log)
    {
        if (!File.Exists(meshPath))
            throw new FileNotFoundException("PSK file was not found.", meshPath);

        using FileStream stream = File.OpenRead(meshPath);
        using BinaryReader reader = new(stream);

        List<Vector3> points = [];
        List<PskWedge> wedges = [];
        List<PskFace> faces = [];
        List<PskMaterial> materials = [];
        List<PskBone> bones = [];
        List<PskRawWeight> weights = [];

        while (stream.Position < stream.Length)
        {
            PskChunkHeader chunk = ReadChunkHeader(reader);
            long chunkStart = stream.Position;
            switch (chunk.ChunkId)
            {
                case "PNTS0000":
                    for (int i = 0; i < chunk.DataCount; i++)
                        points.Add(ReadPskPoint(reader));
                    break;
                case "VTXW0000":
                    for (int i = 0; i < chunk.DataCount; i++)
                        wedges.Add(ReadPskWedge(reader));
                    break;
                case "FACE0000":
                    for (int i = 0; i < chunk.DataCount; i++)
                        faces.Add(ReadPskFace(reader));
                    break;
                case "MATT0000":
                    for (int i = 0; i < chunk.DataCount; i++)
                        materials.Add(ReadPskMaterial(reader));
                    break;
                case "REFSKELT":
                    for (int i = 0; i < chunk.DataCount; i++)
                        bones.Add(ReadPskBone(reader));
                    break;
                case "RAWWEIGHTS":
                    for (int i = 0; i < chunk.DataCount; i++)
                        weights.Add(ReadPskRawWeight(reader, bones));
                    break;
            }

            long expectedPosition = chunkStart + ((long)chunk.DataCount * chunk.DataSize);
            if (stream.Position != expectedPosition)
                stream.Position = expectedPosition;
        }

        if (points.Count == 0 || wedges.Count == 0 || faces.Count == 0)
            throw new InvalidOperationException("The PSK file is missing required geometry chunks.");

        RetargetMesh mesh = new()
        {
            SourcePath = meshPath,
            MeshName = Path.GetFileNameWithoutExtension(meshPath)
        };

        Dictionary<int, List<RetargetWeight>> weightsByPoint = weights
            .GroupBy(static weight => weight.PointIndex)
            .ToDictionary(static group => group.Key, static group => group.Select(static weight => new RetargetWeight(weight.BoneName, weight.Weight)).ToList());

        foreach (PskBone bone in bones)
        {
            mesh.Bones.Add(new RetargetBone
            {
                Name = bone.Name,
                ParentIndex = bone.ParentIndex,
                LocalTransform = bone.LocalTransform,
                GlobalTransform = bone.GlobalTransform
            });
        }

        Dictionary<int, RetargetSection> sections = [];
        foreach (PskFace face in faces)
        {
            if (!sections.TryGetValue(face.MaterialIndex, out RetargetSection section))
            {
                PskMaterial material = materials.FirstOrDefault(material => material.MaterialIndex == face.MaterialIndex);
                section = new RetargetSection
                {
                    Name = $"Section_{face.MaterialIndex}",
                    MaterialIndex = face.MaterialIndex,
                    MaterialName = material?.Name ?? $"Material_{face.MaterialIndex}"
                };
                sections[face.MaterialIndex] = section;
            }

            int[] wedgeIndices = [face.WedgeIndex0, face.WedgeIndex1, face.WedgeIndex2];
            foreach (int wedgeIndex in wedgeIndices)
            {
                PskWedge wedge = wedges[wedgeIndex];
                RetargetVertex vertex = new()
                {
                    Position = ConvertPosition(points[wedge.PointIndex]),
                    Normal = Vector3.UnitY,
                    Tangent = Vector3.UnitX,
                    Bitangent = Vector3.UnitZ,
                    Color = new UpkManager.Models.UpkFile.Core.FColor { R = 255, G = 255, B = 255, A = 255 }
                };
                vertex.UVs.Add(new Vector2(wedge.U, 1.0f - wedge.V));
                if (weightsByPoint.TryGetValue(wedge.PointIndex, out List<RetargetWeight> vertexWeights))
                    vertex.Weights.AddRange(vertexWeights);

                int vertexIndex = section.Vertices.Count;
                section.Vertices.Add(vertex);
                section.Indices.Add(vertexIndex);
            }

            section.TriangleSmoothingGroups.Add(face.SmoothingGroups == 0 ? 1 : face.SmoothingGroups);
        }

        foreach (RetargetSection section in sections.OrderBy(static pair => pair.Key).Select(static pair => pair.Value))
        {
            ComputeSectionTangents(section);
            WeldExactDuplicateVertices(section);
            mesh.Sections.Add(section);
        }

        mesh.RebuildBoneLookup();
        log?.Invoke($"Imported {mesh.Sections.Count} PSK section(s), {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles, and {mesh.Bones.Count} bones from {meshPath}.");
        return mesh;
    }

    private static void ComputeSectionTangents(RetargetSection section)
    {
        if (section.Vertices.Count == 0 || section.Indices.Count == 0)
            return;

        Vector3[] normalSums = new Vector3[section.Vertices.Count];
        Vector3[] tangentSums = new Vector3[section.Vertices.Count];

        for (int i = 0; i + 2 < section.Indices.Count; i += 3)
        {
            int i0 = section.Indices[i];
            int i1 = section.Indices[i + 1];
            int i2 = section.Indices[i + 2];
            RetargetVertex v0 = section.Vertices[i0];
            RetargetVertex v1 = section.Vertices[i1];
            RetargetVertex v2 = section.Vertices[i2];

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
            float determinant = ((uv1.X - uv0.X) * (uv2.Y - uv0.Y)) - ((uv1.Y - uv0.Y) * (uv2.X - uv0.X));
            if (MathF.Abs(determinant) <= 1e-10f)
                continue;

            float inv = 1.0f / determinant;
            Vector3 tangent = ((edge1 * (uv2.Y - uv0.Y)) - (edge2 * (uv1.Y - uv0.Y))) * inv;
            tangentSums[i0] += tangent;
            tangentSums[i1] += tangent;
            tangentSums[i2] += tangent;
        }

        for (int i = 0; i < section.Vertices.Count; i++)
        {
            RetargetVertex vertex = section.Vertices[i];
            vertex.Normal = NormalizeOrUnitY(normalSums[i]);
            vertex.Tangent = tangentSums[i].LengthSquared() > 1e-10f
                ? Vector3.Normalize(tangentSums[i])
                : BuildFallbackTangent(vertex.Normal);
            vertex.Bitangent = NormalizeOrUnitY(Vector3.Cross(vertex.Normal, vertex.Tangent));
        }
    }

    private static PskChunkHeader ReadChunkHeader(BinaryReader reader)
    {
        return new PskChunkHeader(
            new string(reader.ReadChars(20)).TrimEnd('\0', ' '),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32());
    }

    private static Vector3 ReadPskPoint(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static PskWedge ReadPskWedge(BinaryReader reader)
    {
        int pointIndex = reader.ReadUInt16();
        float u = reader.ReadSingle();
        float v = reader.ReadSingle();
        _ = reader.ReadByte();
        byte materialIndex = reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        return new PskWedge(pointIndex, u, v, materialIndex);
    }

    private static PskFace ReadPskFace(BinaryReader reader)
    {
        int wedge0 = reader.ReadUInt16();
        int wedge1 = reader.ReadUInt16();
        int wedge2 = reader.ReadUInt16();
        byte materialIndex = reader.ReadByte();
        _ = reader.ReadByte();
        int smoothingGroups = reader.ReadInt32();
        return new PskFace(wedge0, wedge1, wedge2, materialIndex, smoothingGroups);
    }

    private static PskMaterial ReadPskMaterial(BinaryReader reader)
    {
        string name = new string(reader.ReadChars(64)).TrimEnd('\0', ' ');
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        int materialIndex = reader.ReadInt32();
        reader.BaseStream.Position += 68;
        return new PskMaterial(name, materialIndex);
    }

    private static PskBone ReadPskBone(BinaryReader reader)
    {
        string name = new string(reader.ReadChars(64)).TrimEnd('\0', ' ');
        _ = reader.ReadInt32();
        int numChildren = reader.ReadInt32();
        int parentIndex = reader.ReadInt32();
        System.Numerics.Quaternion orientation = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        Vector3 position = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        _ = reader.ReadSingle();
        _ = reader.ReadSingle();
        _ = reader.ReadSingle();
        _ = reader.ReadSingle();
        NumericsMatrix4x4 localTransform = NumericsMatrix4x4.CreateFromQuaternion(orientation) * NumericsMatrix4x4.CreateTranslation(position);
        return new PskBone(name, parentIndex, numChildren, ConvertTransform(localTransform), NumericsMatrix4x4.Identity);
    }

    private static PskRawWeight ReadPskRawWeight(BinaryReader reader, IReadOnlyList<PskBone> bones)
    {
        float weight = reader.ReadSingle();
        int pointIndex = reader.ReadInt32();
        int boneIndex = reader.ReadInt32();
        string boneName = boneIndex >= 0 && boneIndex < bones.Count ? bones[boneIndex].Name : string.Empty;
        return new PskRawWeight(pointIndex, boneIndex, weight, boneName);
    }

    private static UpkManager.Models.UpkFile.Core.FColor ToFColor(Color4D color)
    {
        static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
        return new UpkManager.Models.UpkFile.Core.FColor
        {
            R = ToByte(color.R),
            G = ToByte(color.G),
            B = ToByte(color.B),
            A = ToByte(color.A)
        };
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
        ImmutableArray<int> WeightKeys,
        int ColorKey)
    {
        public static VertexKey Create(RetargetVertex vertex)
        {
            return new VertexKey(
                Float3Key.Create(vertex.Position),
                Float3Key.Create(vertex.Normal),
                Float3Key.Create(vertex.Tangent),
                Float3Key.Create(vertex.Bitangent),
                [.. vertex.UVs.Select(Float2Key.Create)],
                [.. vertex.Weights.Select(static weight => HashCode.Combine(weight.BoneName.ToLowerInvariant(), BitConverter.SingleToInt32Bits(weight.Weight)))],
                HashCode.Combine(vertex.Color.R, vertex.Color.G, vertex.Color.B, vertex.Color.A));
        }
    }

    private readonly record struct Float3Key(int X, int Y, int Z)
    {
        public static Float3Key Create(Vector3 value) => new(BitConverter.SingleToInt32Bits(value.X), BitConverter.SingleToInt32Bits(value.Y), BitConverter.SingleToInt32Bits(value.Z));
    }

    private readonly record struct Float2Key(int X, int Y)
    {
        public static Float2Key Create(Vector2 value) => new(BitConverter.SingleToInt32Bits(value.X), BitConverter.SingleToInt32Bits(value.Y));
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
                x.Uvs.AsSpan().SequenceEqual(y.Uvs.AsSpan()) &&
                x.WeightKeys.AsSpan().SequenceEqual(y.WeightKeys.AsSpan()) &&
                x.ColorKey == y.ColorKey;
        }

        public int GetHashCode(VertexKey obj)
        {
            HashCode hash = new();
            hash.Add(obj.Position);
            hash.Add(obj.Normal);
            hash.Add(obj.Tangent);
            hash.Add(obj.Bitangent);
            foreach (Float2Key uv in obj.Uvs)
                hash.Add(uv);
            foreach (int weightKey in obj.WeightKeys)
                hash.Add(weightKey);
            hash.Add(obj.ColorKey);
            return hash.ToHashCode();
        }
    }

    private sealed record PskChunkHeader(string ChunkId, int TypeFlag, int DataSize, int DataCount);
    private sealed record PskWedge(int PointIndex, float U, float V, int MaterialIndex);
    private sealed record PskFace(int WedgeIndex0, int WedgeIndex1, int WedgeIndex2, int MaterialIndex, int SmoothingGroups);
    private sealed record PskMaterial(string Name, int MaterialIndex);
    private sealed record PskBone(string Name, int ParentIndex, int NumChildren, NumericsMatrix4x4 LocalTransform, NumericsMatrix4x4 GlobalTransform);
    private sealed record PskRawWeight(int PointIndex, int BoneIndex, float Weight, string BoneName);
}
