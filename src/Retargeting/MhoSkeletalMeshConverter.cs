using System.Numerics;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Types;

namespace MHUpkManager.Retargeting;

internal sealed class MhoSkeletalMeshConverter
{
    public RetargetMesh Convert(USkeletalMesh skeletalMesh, string meshName, int lodIndex, Action<string> log = null)
    {
        if (skeletalMesh == null)
            throw new ArgumentNullException(nameof(skeletalMesh));
        if (lodIndex < 0 || lodIndex >= skeletalMesh.LODModels.Count)
            throw new ArgumentOutOfRangeException(nameof(lodIndex));

        RetargetMesh mesh = new()
        {
            SourcePath = meshName,
            MeshName = meshName
        };

        mesh.Bones.AddRange(BuildBones(skeletalMesh.RefSkeleton));
        mesh.RebuildBoneLookup();

        FStaticLODModel lod = skeletalMesh.LODModels[lodIndex];
        FGPUSkinVertexBase[] sourceVertices = [.. lod.VertexBufferGPUSkin.VertexData];
        uint[] sourceIndices = [.. lod.MultiSizeIndexContainer.IndexBuffer];
        int uvChannelCount = Math.Max(1, checked((int)lod.VertexBufferGPUSkin.NumTexCoords));

        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
        {
            FSkelMeshSection sourceSection = lod.Sections[sectionIndex];
            if (sourceSection.ChunkIndex < 0 || sourceSection.ChunkIndex >= lod.Chunks.Count)
            {
                log?.Invoke($"Skipping section {sectionIndex} because chunk index {sourceSection.ChunkIndex} is out of range.");
                continue;
            }

            FSkelMeshChunk chunk = lod.Chunks[sourceSection.ChunkIndex];
            RetargetSection section = new()
            {
                Name = $"Section_{sectionIndex}",
                MaterialName = $"Material_{sourceSection.MaterialIndex}",
                MaterialIndex = sourceSection.MaterialIndex
            };

            Dictionary<int, int> localVertexByGlobalVertex = [];
            uint start = sourceSection.BaseIndex;
            uint end = start + (sourceSection.NumTriangles * 3);
            if (start >= sourceIndices.Length)
            {
                log?.Invoke($"Skipping section {sectionIndex} because base index {start} is outside the index buffer ({sourceIndices.Length}).");
                continue;
            }

            if (end > sourceIndices.Length)
            {
                log?.Invoke($"Clamping section {sectionIndex} from {end - start} index entries to {sourceIndices.Length - start} because it overruns the index buffer.");
                end = (uint)sourceIndices.Length;
            }

            for (uint i = start; i < end; i++)
            {
                int globalVertexIndex = checked((int)sourceIndices[i]);
                if (globalVertexIndex < 0 || globalVertexIndex >= sourceVertices.Length)
                {
                    log?.Invoke($"Skipping vertex index {globalVertexIndex} in section {sectionIndex} because it is outside the vertex buffer ({sourceVertices.Length}).");
                    continue;
                }

                if (!localVertexByGlobalVertex.TryGetValue(globalVertexIndex, out int localVertexIndex))
                {
                    localVertexIndex = section.Vertices.Count;
                    localVertexByGlobalVertex.Add(globalVertexIndex, localVertexIndex);
                    section.Vertices.Add(CreateVertex(
                        lod.VertexBufferGPUSkin.GetVertexPosition(sourceVertices[globalVertexIndex]),
                        sourceVertices[globalVertexIndex],
                        uvChannelCount,
                        chunk.BoneMap,
                        mesh.Bones));
                }

                section.Indices.Add(localVertexIndex);
            }

            int trimmedIndexCount = section.Indices.Count - (section.Indices.Count % 3);
            if (trimmedIndexCount != section.Indices.Count)
            {
                log?.Invoke($"Trimming {section.Indices.Count - trimmedIndexCount} dangling indices from section {sectionIndex} to keep triangle data aligned.");
                section.Indices.RemoveRange(trimmedIndexCount, section.Indices.Count - trimmedIndexCount);
            }

            if (section.Indices.Count == 0)
            {
                log?.Invoke($"Skipping section {sectionIndex} because no valid triangles remained after validation.");
                continue;
            }

            while (section.TriangleSmoothingGroups.Count < section.Indices.Count / 3)
                section.TriangleSmoothingGroups.Add(1);

            mesh.Sections.Add(section);
        }

        log?.Invoke($"Converted original MHO SkeletalMesh '{meshName}' LOD{lodIndex} to transfer source with {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles, and {mesh.Bones.Count} bones.");
        return mesh;
    }

    private static IEnumerable<RetargetBone> BuildBones(UArray<FMeshBone> skeleton)
    {
        List<RetargetBone> bones = new(skeleton.Count);
        List<Matrix4x4> rawGlobals = new(skeleton.Count);
        for (int i = 0; i < skeleton.Count; i++)
        {
            FMeshBone sourceBone = skeleton[i];
            Matrix4x4 rawLocalTransform = sourceBone.BonePos.ToMatrix();
            Matrix4x4 rawGlobalTransform = sourceBone.ParentIndex >= 0 && sourceBone.ParentIndex < rawGlobals.Count
                ? rawLocalTransform * rawGlobals[sourceBone.ParentIndex]
                : rawLocalTransform;
            rawGlobals.Add(rawGlobalTransform);

            bones.Add(new RetargetBone
            {
                Name = sourceBone.Name?.Name ?? $"Bone_{i}",
                ParentIndex = sourceBone.ParentIndex,
                LocalTransform = ConvertTransform(rawLocalTransform),
                GlobalTransform = ConvertTransform(rawGlobalTransform)
            });
        }

        return bones;
    }

    private static RetargetVertex CreateVertex(
        Vector3 sourcePosition,
        FGPUSkinVertexBase vertex,
        int uvChannelCount,
        UArray<ushort> boneMap,
        IReadOnlyList<RetargetBone> bones)
    {
        Vector3 normal = GLVertex.SafeNormal(vertex.TangentZ);
        Vector3 tangent = GLVertex.SafeNormal(vertex.TangentX);
        Vector3 bitangent = GLVertex.ComputeBitangent(normal, tangent, vertex.TangentZ);

        RetargetVertex result = new()
        {
            Position = ConvertPosition(sourcePosition),
            Normal = NormalizeOrUnitY(ConvertDirection(normal)),
            Tangent = NormalizeOrUnitY(ConvertDirection(tangent)),
            Bitangent = NormalizeOrUnitY(ConvertDirection(bitangent))
        };

        int availableUvCount = GetAvailableUvCount(vertex, uvChannelCount);
        for (int uvIndex = 0; uvIndex < availableUvCount; uvIndex++)
            result.UVs.Add(vertex.GetVector2(uvIndex));

        while (result.UVs.Count == 0)
            result.UVs.Add(Vector2.Zero);

        for (int influenceIndex = 0; influenceIndex < 4; influenceIndex++)
        {
            byte localBoneIndex = vertex.InfluenceBones[influenceIndex];
            byte weight = vertex.InfluenceWeights[influenceIndex];
            if (weight == 0)
                continue;

            int mappedBoneIndex = localBoneIndex < boneMap.Count ? boneMap[localBoneIndex] : 0;
            string boneName = mappedBoneIndex >= 0 && mappedBoneIndex < bones.Count
                ? bones[mappedBoneIndex].Name
                : bones.Count > 0
                    ? bones[0].Name
                    : "Root";
            result.Weights.Add(new RetargetWeight(boneName, weight / 255.0f));
        }

        return result;
    }

    private static Matrix4x4 ConvertTransform(Matrix4x4 value)
    {
        return new Matrix4x4(
            value.M11, value.M13, value.M12, value.M14,
            value.M31, value.M33, value.M32, value.M34,
            value.M21, value.M23, value.M22, value.M24,
            value.M41, value.M43, value.M42, value.M44);
    }

    private static Vector3 ConvertPosition(Vector3 source) => new(source.X, source.Z, source.Y);
    private static Vector3 ConvertDirection(Vector3 source) => new(source.X, source.Z, source.Y);

    private static int GetAvailableUvCount(FGPUSkinVertexBase vertex, int requestedUvCount)
    {
        return vertex switch
        {
            FGPUSkinVertexFloat16Uvs32Xyz float16Packed => Math.Min(requestedUvCount, float16Packed.UVs?.Length ?? 0),
            FGPUSkinVertexFloat16Uvs float16 => Math.Min(requestedUvCount, float16.UVs?.Length ?? 0),
            FGPUSkinVertexFloat32Uvs32Xyz float32Packed => Math.Min(requestedUvCount, float32Packed.UVs?.Length ?? 0),
            FGPUSkinVertexFloat32Uvs float32 => Math.Min(requestedUvCount, float32.UVs?.Length ?? 0),
            _ => Math.Max(1, requestedUvCount)
        };
    }

    private static Vector3 NormalizeOrUnitY(Vector3 value)
    {
        return value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : Vector3.UnitY;
    }
}
