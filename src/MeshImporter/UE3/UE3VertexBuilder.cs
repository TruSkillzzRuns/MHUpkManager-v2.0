using System.Numerics;
using System.Linq;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;

namespace MHUpkManager.MeshImporter;

internal sealed class UE3VertexBuilder
{
    public BuiltVertexData Build(
        NeutralSection section,
        IReadOnlyList<IReadOnlyList<NormalizedWeight>> normalizedWeights,
        MeshImportContext context,
        IReadOnlyList<int> boneMap,
        IReadOnlyList<int> sourceVertexIndices,
        bool rebuildAsRigidChunk,
        bool preserveRigidVertices)
    {
        Dictionary<int, byte> localBoneLookup = boneMap
            .Select((boneIndex, localIndex) => new { boneIndex, localIndex })
            .ToDictionary(static x => x.boneIndex, static x => (byte)x.localIndex);

        List<(NeutralVertex Vertex, byte[] Bones, byte[] Weights, FPackedNormal TangentX, FPackedNormal TangentY, FPackedNormal TangentZ, bool IsRigid)> preparedVertices = new(section.Vertices.Count);
        int[] localIndexRemap = new int[section.Vertices.Count];

        for (int i = 0; i < section.Vertices.Count; i++)
        {
            NeutralVertex vertex = section.Vertices[i];
            IReadOnlyList<NormalizedWeight> weights = normalizedWeights[sourceVertexIndices[i]];
            (byte[] localBones, byte[] influenceWeights) = BuildBones(weights, localBoneLookup);

            FPackedNormal tangentX = PackNormal(vertex.Tangent, 1.0f);
            FPackedNormal tangentY = PackNormal(Vector3.Normalize(vertex.Bitangent), 1.0f);
            FPackedNormal tangentZ = PackNormal(vertex.Normal, ComputeDeterminantSign(vertex.Normal, vertex.Tangent, vertex.Bitangent));

            bool isRigid = rebuildAsRigidChunk || (preserveRigidVertices && IsSingleInfluenceVertex(influenceWeights));
            preparedVertices.Add((vertex, localBones, influenceWeights, tangentX, tangentY, tangentZ, isRigid));
        }

        List<FRigidSkinVertex> rigidVertices = new(section.Vertices.Count);
        List<FSoftSkinVertex> softVertices = new(section.Vertices.Count);
        List<FGPUSkinVertexBase> gpuVertices = new(section.Vertices.Count);
        List<FVertexInfluence> influences = new(section.Vertices.Count);

        int outputIndex = 0;
        foreach (int i in Enumerable.Range(0, preparedVertices.Count).Where(index => preparedVertices[index].IsRigid))
        {
            localIndexRemap[i] = outputIndex++;
            var prepared = preparedVertices[i];
            NeutralVertex vertex = prepared.Vertex;
            byte[] localBones = prepared.Bones;
            byte[] influenceWeights = prepared.Weights;
            FPackedNormal tangentX = prepared.TangentX;
            FPackedNormal tangentY = prepared.TangentY;
            FPackedNormal tangentZ = prepared.TangentZ;

            rigidVertices.Add(new FRigidSkinVertex
            {
                Position = new FVector(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
                TangentX = tangentX,
                TangentY = tangentY,
                TangentZ = tangentZ,
                UVs = BuildSoftUvs(vertex.UVs),
                Color = new FColor { R = 255, G = 255, B = 255, A = 255 },
                Bone = localBones[0]
            });

            gpuVertices.Add(BuildGpuVertex(context, vertex, tangentX, tangentZ, localBones, influenceWeights));
            influences.Add(new FVertexInfluence
            {
                Bones = new FInfluenceBones { Bones = [.. localBones] },
                Weights = new FInfluenceWeights { Weights = [.. influenceWeights] }
            });
        }

        foreach (int i in Enumerable.Range(0, preparedVertices.Count).Where(index => preparedVertices[index].IsRigid == false))
        {
            localIndexRemap[i] = outputIndex++;
            var prepared = preparedVertices[i];
            NeutralVertex vertex = prepared.Vertex;
            byte[] localBones = prepared.Bones;
            byte[] influenceWeights = prepared.Weights;
            FPackedNormal tangentX = prepared.TangentX;
            FPackedNormal tangentY = prepared.TangentY;
            FPackedNormal tangentZ = prepared.TangentZ;

            softVertices.Add(new FSoftSkinVertex
            {
                Position = new FVector(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
                TangentX = tangentX,
                TangentY = tangentY,
                TangentZ = tangentZ,
                UVs = BuildSoftUvs(vertex.UVs),
                Color = new FColor { R = 255, G = 255, B = 255, A = 255 },
                InfluenceBones = localBones,
                InfluenceWeights = influenceWeights
            });

            gpuVertices.Add(BuildGpuVertex(context, vertex, tangentX, tangentZ, localBones, influenceWeights));
            influences.Add(new FVertexInfluence
            {
                Bones = new FInfluenceBones { Bones = [.. localBones] },
                Weights = new FInfluenceWeights { Weights = [.. influenceWeights] }
            });
        }

        return new BuiltVertexData(rigidVertices, softVertices, gpuVertices, influences, localIndexRemap);
    }

    private static (byte[] Bones, byte[] Weights) BuildBones(
        IReadOnlyList<NormalizedWeight> weights,
        IReadOnlyDictionary<int, byte> localBoneLookup)
    {
        byte[] bones = new byte[4];
        byte[] influenceWeights = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            NormalizedWeight weight = weights[i];
            bones[i] = localBoneLookup.TryGetValue(weight.BoneIndex, out byte localBone) ? localBone : (byte)0;
            influenceWeights[i] = weight.Weight;
        }

        return (bones, influenceWeights);
    }

    private static FGPUSkinVertexBase BuildGpuVertex(
        MeshImportContext context,
        NeutralVertex vertex,
        FPackedNormal tangentX,
        FPackedNormal tangentZ,
        byte[] localBones,
        byte[] influenceWeights)
    {
        if (context.UseFullPrecisionUvs)
        {
            return new FGPUSkinVertexFloat32Uvs
            {
                TangentX = tangentX,
                TangentZ = tangentZ,
                InfluenceBones = [.. localBones],
                InfluenceWeights = [.. influenceWeights],
                Positon = new FVector(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
                UVs = BuildGpuFullUvs(vertex.UVs, context.NumTexCoords)
            };
        }

        return new FGPUSkinVertexFloat16Uvs
        {
            TangentX = tangentX,
            TangentZ = tangentZ,
            InfluenceBones = [.. localBones],
            InfluenceWeights = [.. influenceWeights],
            Positon = new FVector(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
            UVs = BuildGpuHalfUvs(vertex.UVs, context.NumTexCoords)
        };
    }

    private static FVector2D[] BuildSoftUvs(IReadOnlyList<Vector2> source)
    {
        FVector2D[] values = new FVector2D[4];
        for (int i = 0; i < values.Length; i++)
        {
            Vector2 uv = i < source.Count ? source[i] : Vector2.Zero;
            values[i] = new FVector2D(uv.X, uv.Y);
        }

        return values;
    }

    private static FVector2D[] BuildGpuFullUvs(IReadOnlyList<Vector2> source, int count)
    {
        FVector2D[] values = new FVector2D[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 uv = i < source.Count ? source[i] : Vector2.Zero;
            values[i] = new FVector2D(uv.X, uv.Y);
        }

        return values;
    }

    private static FVector2DHalf[] BuildGpuHalfUvs(IReadOnlyList<Vector2> source, int count)
    {
        FVector2DHalf[] values = new FVector2DHalf[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 uv = i < source.Count ? source[i] : Vector2.Zero;
            values[i] = new FVector2DHalf
            {
                X = new FFloat16 { Encoded = BitConverter.HalfToUInt16Bits((Half)uv.X) },
                Y = new FFloat16 { Encoded = BitConverter.HalfToUInt16Bits((Half)uv.Y) }
            };
        }

        return values;
    }

    private static FPackedNormal PackNormal(Vector3 vector, float w)
    {
        Vector3 normalized = vector.LengthSquared() > 1e-10f ? Vector3.Normalize(vector) : Vector3.UnitY;
        return new FPackedNormal
        {
            Packed = PackSignedByte(normalized.X)
                | ((uint)PackSignedByte(normalized.Y) << 8)
                | ((uint)PackSignedByte(normalized.Z) << 16)
                | ((uint)PackSignedByte(w) << 24)
        };
    }

    private static uint PackSignedByte(float value)
    {
        float scaled = (Math.Clamp(value, -1.0f, 1.0f) + 1.0f) * 127.5f;
        return (uint)Math.Clamp((int)MathF.Round(scaled), 0, 255);
    }

    private static float ComputeDeterminantSign(Vector3 normal, Vector3 tangent, Vector3 bitangent)
    {
        Vector3 computed = Vector3.Cross(Vector3.Normalize(normal), Vector3.Normalize(tangent));
        return Vector3.Dot(computed, Vector3.Normalize(bitangent)) < 0.0f ? -1.0f : 1.0f;
    }

    private static bool IsSingleInfluenceVertex(IReadOnlyList<byte> influenceWeights)
    {
        return influenceWeights.Count > 0 &&
            influenceWeights[0] == 255 &&
            influenceWeights.Skip(1).All(static weight => weight == 0);
    }
}

internal sealed record BuiltVertexData(
    List<FRigidSkinVertex> RigidVertices,
    List<FSoftSkinVertex> SoftVertices,
    List<FGPUSkinVertexBase> GpuVertices,
    List<FVertexInfluence> Influences,
    IReadOnlyList<int> LocalIndexRemap);
