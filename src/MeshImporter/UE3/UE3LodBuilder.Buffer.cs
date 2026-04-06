using System.Numerics;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Types;

namespace MHUpkManager.MeshImporter;

internal sealed partial class UE3LodBuilder
{
    private static byte[] BuildRawPointIndices(MeshImportContext context, int vertexCount)
    {
        if (context.OriginalLod.RawPointIndices == null || context.OriginalLod.RawPointIndices.Length == 0)
            return [];

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        for (int i = 0; i < vertexCount; i++)
            writer.Write(i);

        return stream.ToArray();
    }

    private static FSkeletalMeshVertexBuffer BuildVertexBuffer(MeshImportContext context, IReadOnlyList<FGPUSkinVertexBase> vertices)
    {
        GetBounds(vertices, out Vector3 min, out Vector3 max);
        FVector meshOrigin = context.UsePackedPosition
            ? new FVector((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f)
            : new FVector(context.OriginalLod.VertexBufferGPUSkin.MeshOrigin.X, context.OriginalLod.VertexBufferGPUSkin.MeshOrigin.Y, context.OriginalLod.VertexBufferGPUSkin.MeshOrigin.Z);
        FVector meshExtension = context.UsePackedPosition
            ? new FVector((max.X - min.X) * 0.5f, (max.Y - min.Y) * 0.5f, (max.Z - min.Z) * 0.5f)
            : new FVector(context.OriginalLod.VertexBufferGPUSkin.MeshExtension.X, context.OriginalLod.VertexBufferGPUSkin.MeshExtension.Y, context.OriginalLod.VertexBufferGPUSkin.MeshExtension.Z);

        FSkeletalMeshVertexBuffer buffer = new()
        {
            NumTexCoords = (uint)context.NumTexCoords,
            bUseFullPrecisionUVs = context.UseFullPrecisionUvs,
            bUsePackedPosition = context.UsePackedPosition,
            MeshOrigin = meshOrigin,
            MeshExtension = meshExtension
        };

        if (context.UseFullPrecisionUvs)
        {
            if (context.UsePackedPosition)
            {
                buffer.VertsF32UV32 = [.. vertices.Cast<FGPUSkinVertexFloat32Uvs>().Select(vertex => new FGPUSkinVertexFloat32Uvs32Xyz
                {
                    TangentX = vertex.TangentX,
                    TangentZ = vertex.TangentZ,
                    InfluenceBones = [.. vertex.InfluenceBones],
                    InfluenceWeights = [.. vertex.InfluenceWeights],
                    Positon = PackPosition(vertex.Positon, meshOrigin, meshExtension),
                    UVs = [.. vertex.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))]
                })];
            }
            else
            {
                buffer.VertsF32 = [.. vertices.Cast<FGPUSkinVertexFloat32Uvs>()];
            }
        }
        else if (context.UsePackedPosition)
        {
            buffer.VertsF16UV32 = [.. vertices.Cast<FGPUSkinVertexFloat16Uvs>().Select(vertex => new FGPUSkinVertexFloat16Uvs32Xyz
            {
                TangentX = vertex.TangentX,
                TangentZ = vertex.TangentZ,
                InfluenceBones = [.. vertex.InfluenceBones],
                InfluenceWeights = [.. vertex.InfluenceWeights],
                Positon = PackPosition(vertex.Positon, meshOrigin, meshExtension),
                UVs = [.. vertex.UVs.Select(static uv => new FVector2DHalf
                {
                    X = new FFloat16 { Encoded = uv.X.Encoded },
                    Y = new FFloat16 { Encoded = uv.Y.Encoded }
                })]
            })];
        }
        else
        {
            buffer.VertsF16 = [.. vertices.Cast<FGPUSkinVertexFloat16Uvs>()];
        }

        return buffer;
    }

    private static FPackedPosition PackPosition(FVector position, FVector meshOrigin, FVector meshExtension)
    {
        float normalizedX = NormalizePackedAxis(position.X, meshOrigin.X, meshExtension.X);
        float normalizedY = NormalizePackedAxis(position.Y, meshOrigin.Y, meshExtension.Y);
        float normalizedZ = NormalizePackedAxis(position.Z, meshOrigin.Z, meshExtension.Z);
        uint packedX = PackSignedComponent(normalizedX, 11);
        uint packedY = PackSignedComponent(normalizedY, 11);
        uint packedZ = PackSignedComponent(normalizedZ, 10);

        return new FPackedPosition { Packed = packedX | (packedY << 11) | (packedZ << 22) };
    }

    private static float NormalizePackedAxis(float value, float origin, float extension)
    {
        if (MathF.Abs(extension) <= 1e-8f)
            return 0.0f;

        return Math.Clamp((value - origin) / extension, -1.0f, 1.0f);
    }

    private static uint PackSignedComponent(float value, int bits)
    {
        int maxPositive = (1 << (bits - 1)) - 1;
        int minNegative = -(1 << (bits - 1));
        int quantized = (int)MathF.Round(Math.Clamp(value, -1.0f, 1.0f) * maxPositive);
        quantized = Math.Clamp(quantized, minNegative, maxPositive);
        return (uint)(quantized & ((1 << bits) - 1));
    }

    private static void GetBounds(IReadOnlyList<FGPUSkinVertexBase> vertices, out Vector3 min, out Vector3 max)
    {
        if (vertices.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
            return;
        }

        min = vertices[0].GetVector3();
        max = min;
        foreach (FGPUSkinVertexBase vertex in vertices)
        {
            Vector3 position = vertex.GetVector3();
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
    }

    private static FSkeletalMeshVertexColorBuffer BuildColorBuffer(int count)
    {
        return new FSkeletalMeshVertexColorBuffer
        {
            Colors = [.. Enumerable.Range(0, count).Select(static _ => new FGPUSkinVertexColor
            {
                VertexColor = new FColor { R = 255, G = 255, B = 255, A = 255 }
            })]
        };
    }

    private static FSkeletalMeshVertexInfluences BuildInfluenceBuffer(
        IReadOnlyList<FVertexInfluence> influences,
        IReadOnlyList<FSkelMeshSection> sections,
        IReadOnlyList<FSkelMeshChunk> chunks,
        MeshImportContext context)
    {
        UMap<BoneIndexPair, UArray<uint>> mapping = [];
        for (uint vertexIndex = 0; vertexIndex < influences.Count; vertexIndex++)
        {
            FVertexInfluence influence = influences[(int)vertexIndex];
            BoneIndexPair key = new(influence.Bones.Bones[0], influence.Bones.Bones[1]);
            if (!mapping.TryGetValue(key, out UArray<uint> vertices))
            {
                vertices = [];
                mapping[key] = vertices;
            }

            vertices.Add(vertexIndex);
        }

        return new FSkeletalMeshVertexInfluences
        {
            Influences = [.. influences],
            VertexInfluenceMapping = mapping,
            Sections = [.. sections],
            Chunks = [.. chunks],
            RequiredBones = [.. context.RequiredBones],
            Usage = 0
        };
    }

    private static FRigidSkinVertex CloneRigidVertex(FRigidSkinVertex value)
    {
        return new FRigidSkinVertex
        {
            Position = new FVector(value.Position.X, value.Position.Y, value.Position.Z),
            TangentX = new FPackedNormal { Packed = value.TangentX.Packed },
            TangentY = new FPackedNormal { Packed = value.TangentY.Packed },
            TangentZ = new FPackedNormal { Packed = value.TangentZ.Packed },
            UVs = [.. value.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))],
            Color = new FColor { R = value.Color.R, G = value.Color.G, B = value.Color.B, A = value.Color.A },
            Bone = value.Bone
        };
    }

    private static FSoftSkinVertex CloneSoftVertex(FSoftSkinVertex value)
    {
        return new FSoftSkinVertex
        {
            Position = new FVector(value.Position.X, value.Position.Y, value.Position.Z),
            TangentX = new FPackedNormal { Packed = value.TangentX.Packed },
            TangentY = new FPackedNormal { Packed = value.TangentY.Packed },
            TangentZ = new FPackedNormal { Packed = value.TangentZ.Packed },
            UVs = [.. value.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))],
            Color = new FColor { R = value.Color.R, G = value.Color.G, B = value.Color.B, A = value.Color.A },
            InfluenceBones = [.. value.InfluenceBones],
            InfluenceWeights = [.. value.InfluenceWeights]
        };
    }

    private static FGPUSkinVertexBase CloneGpuVertex(FGPUSkinVertexBase value)
    {
        return value switch
        {
            FGPUSkinVertexFloat16Uvs v => new FGPUSkinVertexFloat16Uvs
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FVector(v.Positon.X, v.Positon.Y, v.Positon.Z),
                UVs = [.. v.UVs.Select(static uv => new FVector2DHalf { X = new FFloat16 { Encoded = uv.X.Encoded }, Y = new FFloat16 { Encoded = uv.Y.Encoded } })]
            },
            FGPUSkinVertexFloat32Uvs v => new FGPUSkinVertexFloat32Uvs
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FVector(v.Positon.X, v.Positon.Y, v.Positon.Z),
                UVs = [.. v.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))]
            },
            FGPUSkinVertexFloat16Uvs32Xyz v => new FGPUSkinVertexFloat16Uvs32Xyz
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FPackedPosition { Packed = v.Positon.Packed },
                UVs = [.. v.UVs.Select(static uv => new FVector2DHalf { X = new FFloat16 { Encoded = uv.X.Encoded }, Y = new FFloat16 { Encoded = uv.Y.Encoded } })]
            },
            FGPUSkinVertexFloat32Uvs32Xyz v => new FGPUSkinVertexFloat32Uvs32Xyz
            {
                TangentX = new FPackedNormal { Packed = v.TangentX.Packed },
                TangentZ = new FPackedNormal { Packed = v.TangentZ.Packed },
                InfluenceBones = [.. v.InfluenceBones],
                InfluenceWeights = [.. v.InfluenceWeights],
                Positon = new FPackedPosition { Packed = v.Positon.Packed },
                UVs = [.. v.UVs.Select(static uv => new FVector2D(uv.X, uv.Y))]
            },
            _ => throw new InvalidOperationException($"Unsupported GPU vertex type '{value.GetType().Name}'.")
        };
    }

    private static IReadOnlyList<FVertexInfluence> BuildPreservedInfluences(FSkelMeshChunk chunk)
    {
        List<FVertexInfluence> influences = new(chunk.NumRigidVertices + chunk.NumSoftVertices);
        foreach (FRigidSkinVertex rigidVertex in chunk.RigidVertices)
        {
            influences.Add(new FVertexInfluence
            {
                Bones = new FInfluenceBones { Bones = [rigidVertex.Bone, 0, 0, 0] },
                Weights = new FInfluenceWeights { Weights = [255, 0, 0, 0] }
            });
        }

        foreach (FSoftSkinVertex softVertex in chunk.SoftVertices)
        {
            influences.Add(new FVertexInfluence
            {
                Bones = new FInfluenceBones { Bones = [.. softVertex.InfluenceBones] },
                Weights = new FInfluenceWeights { Weights = [.. softVertex.InfluenceWeights] }
            });
        }

        return influences;
    }
}
