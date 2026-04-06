using Assimp;
using DDSLib;
using System.Numerics;
using System.Text;
using System.Windows.Media.Imaging;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Models.UpkFile.Types;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using AssimpTextureType = Assimp.TextureType;
using UpkTextureType = MHUpkManager.Model.TextureType;
using MHUpkManager.TextureManager;

namespace MHUpkManager.Model;

internal static class SkeletalFbxExporter
{
    private const string ArmatureName = "Armature";

    public static void Export(string fileName, ModelMesh model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Mesh is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException("The selected model is not a SkeletalMesh.");

        string exportDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory;
        ExportData exportData = BuildFromModelMesh(model, skeletalMesh, 0, exportDirectory);
        ExportAssimpScene(fileName, exportData);
    }

    public static void Export(
        string fileName,
        USkeletalMesh skeletalMesh,
        string meshName,
        int lodIndex,
        Action<string> log = null)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);

        log?.Invoke($"Preparing SkeletalMesh '{meshName}' for FBX export.");
        string exportDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory;
        ExportData exportData = BuildFromSkeletalMesh(skeletalMesh, meshName, lodIndex, exportDirectory, log);
        log?.Invoke($"Writing binary FBX: {fileName}");
        ExportAssimpScene(fileName, exportData);
        log?.Invoke("Binary FBX export completed.");
    }

    private static void ExportAssimpScene(string fileName, ExportData exportData)
    {
        string directory = Path.GetDirectoryName(fileName);
        if (string.IsNullOrWhiteSpace(directory))
            directory = Environment.CurrentDirectory;

        Directory.CreateDirectory(directory);

        using AssimpContext context = new();
        Scene scene = new()
        {
            RootNode = new Node(SanitizeName($"{exportData.Name}_Root"))
        };

        Node armatureNode = new(SanitizeName(ArmatureName));
        scene.RootNode.Children.Add(armatureNode);

        Dictionary<int, int> materialIndices = BuildMaterials(scene, exportData.Materials);
        List<Node> boneNodes = BuildBoneNodes(exportData.Bones);
        AttachBoneHierarchy(armatureNode, boneNodes, exportData.Bones);

        foreach (ExportSectionMesh section in exportData.Sections)
        {
            Mesh mesh = BuildMesh(section, exportData.Bones, materialIndices);
            int meshIndex = scene.Meshes.Count;
            scene.Meshes.Add(mesh);

            Node meshNode = new(SanitizeName(section.Name))
            {
                Transform = NumericsMatrix4x4.Identity.ToAssimp()
            };
            meshNode.MeshIndices.Add(meshIndex);
            armatureNode.Children.Add(meshNode);
        }

        if (!context.ExportFile(scene, fileName, "fbx"))
            throw new InvalidOperationException("Assimp failed to export the SkeletalMesh as FBX.");
    }

    private static Dictionary<int, int> BuildMaterials(Scene scene, IReadOnlyList<ExportMaterial> materials)
    {
        Dictionary<int, int> materialIndices = [];

        foreach (ExportMaterial material in materials)
        {
            Material assimpMaterial = new()
            {
                Name = string.IsNullOrWhiteSpace(material.Name) ? $"Material_{material.MaterialIndex}" : material.Name,
                ShadingMode = ShadingMode.Phong,
                ColorDiffuse = new Color4D(1f, 1f, 1f, 1f),
                ColorAmbient = new Color4D(0f, 0f, 0f, 1f),
                ColorSpecular = new Color4D(0.2f, 0.2f, 0.2f, 1f),
                Opacity = 1f
            };

            if (!string.IsNullOrWhiteSpace(material.DiffuseRelativePath))
                assimpMaterial.TextureDiffuse = CreateTextureSlot(material.DiffuseRelativePath, AssimpTextureType.Diffuse);

            if (!string.IsNullOrWhiteSpace(material.NormalRelativePath))
                assimpMaterial.TextureNormal = CreateTextureSlot(material.NormalRelativePath, AssimpTextureType.Normals);

            int sceneMaterialIndex = scene.Materials.Count;
            scene.Materials.Add(assimpMaterial);
            materialIndices[material.MaterialIndex] = sceneMaterialIndex;
        }

        if (scene.Materials.Count == 0)
        {
            scene.Materials.Add(new Material
            {
                Name = "DefaultMaterial",
                ShadingMode = ShadingMode.Phong,
                ColorDiffuse = new Color4D(1f, 1f, 1f, 1f)
            });
        }

        return materialIndices;
    }

    private static TextureSlot CreateTextureSlot(string filePath, AssimpTextureType textureType)
    {
        return new TextureSlot(
            filePath,
            textureType,
            0,
            TextureMapping.FromUV,
            0,
            1.0f,
            TextureOperation.Add,
            TextureWrapMode.Wrap,
            TextureWrapMode.Wrap,
            0);
    }

    private static List<Node> BuildBoneNodes(IReadOnlyList<ExportBone> bones)
    {
        List<Node> boneNodes = new(bones.Count);
        foreach (ExportBone bone in bones)
        {
            boneNodes.Add(new Node(SanitizeName(bone.Name))
            {
                Transform = bone.LocalTransform.ToAssimp()
            });
        }

        return boneNodes;
    }

    private static void AttachBoneHierarchy(Node armatureNode, IReadOnlyList<Node> boneNodes, IReadOnlyList<ExportBone> bones)
    {
        for (int i = 0; i < bones.Count; i++)
        {
            int parentIndex = bones[i].ParentIndex;
            if (parentIndex >= 0 && parentIndex < boneNodes.Count && parentIndex != i)
                boneNodes[parentIndex].Children.Add(boneNodes[i]);
            else
                armatureNode.Children.Add(boneNodes[i]);
        }
    }

    private static Mesh BuildMesh(
        ExportSectionMesh section,
        IReadOnlyList<ExportBone> bones,
        IReadOnlyDictionary<int, int> materialIndices)
    {
        Mesh mesh = new(SanitizeName(section.Name), PrimitiveType.Triangle)
        {
            MaterialIndex = materialIndices.TryGetValue(section.MaterialIndex, out int materialIndex) ? materialIndex : 0
        };

        foreach (ExportVertex vertex in section.Vertices)
        {
            mesh.Vertices.Add(vertex.Position.ToAssimpVector());
            mesh.Normals.Add(vertex.Normal.ToAssimpVector());
            mesh.Tangents.Add(vertex.Tangent.ToAssimpVector());
            mesh.BiTangents.Add(vertex.Bitangent.ToAssimpVector());
        }

        for (int uvChannelIndex = 0; uvChannelIndex < section.UvChannelCount; uvChannelIndex++)
        {
            mesh.TextureCoordinateChannels[uvChannelIndex] = [];
            mesh.UVComponentCount[uvChannelIndex] = 2;
            foreach (ExportVertex vertex in section.Vertices)
            {
                Vector2 uv = vertex.GetUv(uvChannelIndex);
                mesh.TextureCoordinateChannels[uvChannelIndex].Add(new Vector3D(uv.X, 1.0f - uv.Y, 0.0f));
            }
        }

        for (int i = 0; i < section.Indices.Count; i += 3)
            mesh.Faces.Add(new Face([section.Indices[i], section.Indices[i + 1], section.Indices[i + 2]]));

        for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            BoneVertexWeights boneWeights = CollectBoneWeights(section.Vertices, boneIndex);

            Assimp.Bone assimpBone = new()
            {
                Name = SanitizeName(bones[boneIndex].Name),
                OffsetMatrix = bones[boneIndex].InverseGlobalTransform.ToAssimp()
            };

            if (boneWeights != null)
            {
                foreach (VertexWeight vertexWeight in boneWeights.Weights)
                    assimpBone.VertexWeights.Add(vertexWeight);
            }

            mesh.Bones.Add(assimpBone);
        }

        return mesh;
    }

    private static BoneVertexWeights CollectBoneWeights(IReadOnlyList<ExportVertex> vertices, int boneIndex)
    {
        List<VertexWeight> weights = [];

        for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
        {
            ExportVertex vertex = vertices[vertexIndex];
            AddWeight(weights, vertexIndex, vertex.BoneIndices[0], vertex.BoneWeights[0], boneIndex);
            AddWeight(weights, vertexIndex, vertex.BoneIndices[1], vertex.BoneWeights[1], boneIndex);
            AddWeight(weights, vertexIndex, vertex.BoneIndices[2], vertex.BoneWeights[2], boneIndex);
            AddWeight(weights, vertexIndex, vertex.BoneIndices[3], vertex.BoneWeights[3], boneIndex);
        }

        return weights.Count == 0 ? null : new BoneVertexWeights(weights);
    }

    private static void AddWeight(List<VertexWeight> weights, int vertexIndex, byte vertexBone, byte vertexWeight, int boneIndex)
    {
        if (vertexWeight == 0 || vertexBone != boneIndex)
            return;

        weights.Add(new VertexWeight(vertexIndex, vertexWeight / 255.0f));
    }

    private static ExportData BuildFromModelMesh(ModelMesh model, USkeletalMesh skeletalMesh, int lodIndex, string exportDirectory)
    {
        List<ExportBone> bones = BuildBones(skeletalMesh.RefSkeleton, lodIndex);
        List<ExportSectionMesh> sections = BuildSections(skeletalMesh, model.ModelName, lodIndex);
        List<ExportMaterial> materials = BuildMaterialsFromModelSections(model, sections, model.ModelName, exportDirectory);
        return new ExportData(model.ModelName, bones, sections, materials);
    }

    private static ExportData BuildFromSkeletalMesh(USkeletalMesh skeletalMesh, string meshName, int lodIndex, string exportDirectory, Action<string> log)
    {
        List<ExportBone> bones = BuildBones(skeletalMesh.RefSkeleton, lodIndex);
        log?.Invoke($"Bone count: {bones.Count}");

        List<ExportSectionMesh> sections = BuildSections(skeletalMesh, meshName, lodIndex);
        log?.Invoke($"Section count: {sections.Count}");

        List<ExportMaterial> materials = BuildMaterialsFromSkeletalMesh(skeletalMesh, sections, meshName, exportDirectory, log);
        log?.Invoke($"Material count: {materials.Count}");

        return new ExportData(meshName, bones, sections, materials);
    }

    private static List<ExportBone> BuildBones(UArray<FMeshBone> skeleton, int lodIndex)
    {
        _ = lodIndex;

        if (skeleton == null || skeleton.Count == 0)
            return [];

        List<ExportBone> bones = new(skeleton.Count);

        for (int i = 0; i < skeleton.Count; i++)
        {
            FMeshBone sourceBone = skeleton[i];
            NumericsMatrix4x4 localTransform = ConvertBoneTransform(sourceBone.BonePos.ToMatrix());
            bones.Add(new ExportBone(sourceBone.Name.ToString(), sourceBone.ParentIndex, localTransform));
        }

        for (int i = 0; i < bones.Count; i++)
        {
            NumericsMatrix4x4 globalTransform = bones[i].ParentIndex >= 0
                ? bones[i].LocalTransform * bones[bones[i].ParentIndex].GlobalTransform
                : bones[i].LocalTransform;

            NumericsMatrix4x4.Invert(globalTransform, out NumericsMatrix4x4 inverseGlobalTransform);
            bones[i] = bones[i] with
            {
                GlobalTransform = globalTransform,
                InverseGlobalTransform = inverseGlobalTransform
            };
        }

        return bones;
    }

    private static NumericsMatrix4x4 ConvertBoneTransform(NumericsMatrix4x4 source)
    {
        return ModelFormats.MHInvert * source * ModelFormats.MHInvert;
    }

    private static List<ExportSectionMesh> BuildSections(USkeletalMesh skeletalMesh, string meshName, int lodIndex)
    {
        if (lodIndex < 0 || lodIndex >= skeletalMesh.LODModels.Count)
            throw new ArgumentOutOfRangeException(nameof(lodIndex), $"LOD {lodIndex} is out of range.");

        FStaticLODModel lod = skeletalMesh.LODModels[lodIndex];
        FGPUSkinVertexBase[] sourceVertices = [.. lod.VertexBufferGPUSkin.VertexData];
        uint[] sourceIndices = [.. lod.MultiSizeIndexContainer.IndexBuffer];
        int uvChannelCount = Math.Max(1, checked((int)lod.VertexBufferGPUSkin.NumTexCoords));

        List<ExportSectionMesh> sections = [];

        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
        {
            FSkelMeshSection section = lod.Sections[sectionIndex];
            FSkelMeshChunk chunk = lod.Chunks[section.ChunkIndex];

            Dictionary<int, int> localVertexByGlobalVertex = [];
            List<ExportVertex> vertices = [];
            List<int> indices = [];

            uint start = section.BaseIndex;
            uint end = start + section.NumTriangles * 3;

            for (uint i = start; i < end; i++)
            {
                int globalVertexIndex = checked((int)sourceIndices[i]);
                if (!localVertexByGlobalVertex.TryGetValue(globalVertexIndex, out int localVertexIndex))
                {
                    localVertexIndex = vertices.Count;
                    localVertexByGlobalVertex.Add(globalVertexIndex, localVertexIndex);
                    vertices.Add(CreateVertex(sourceVertices[globalVertexIndex], uvChannelCount, chunk.BoneMap));
                }

                indices.Add(localVertexIndex);
            }

            sections.Add(new ExportSectionMesh(
                $"{meshName}_LOD{lodIndex}_Section_{sectionIndex}",
                sectionIndex,
                checked((int)section.MaterialIndex),
                uvChannelCount,
                vertices,
                indices));
        }

        return sections;
    }

    private static ExportVertex CreateVertex(FGPUSkinVertexBase vertex, int uvChannelCount, UArray<ushort> boneMap)
    {
        Vector3 normal = GLVertex.SafeNormal(vertex.TangentZ);
        Vector3 tangent = GLVertex.SafeNormal(vertex.TangentX);

        return new ExportVertex(
            ConvertPosition(vertex.GetVector3()),
            ConvertDirection(normal),
            ConvertDirection(tangent),
            ConvertDirection(GLVertex.ComputeBitangent(normal, tangent, vertex.TangentZ)),
            [.. Enumerable.Range(0, uvChannelCount).Select(vertex.GetVector2)],
            [
                RemapBone(vertex.InfluenceBones[0], boneMap),
                RemapBone(vertex.InfluenceBones[1], boneMap),
                RemapBone(vertex.InfluenceBones[2], boneMap),
                RemapBone(vertex.InfluenceBones[3], boneMap)
            ],
            [.. vertex.InfluenceWeights]);
    }

    private static byte RemapBone(byte localBoneIndex, UArray<ushort> boneMap)
    {
        if (localBoneIndex < boneMap.Count)
            return checked((byte)boneMap[localBoneIndex]);

        return 0;
    }

    private static Vector3 ConvertPosition(Vector3 source)
    {
        return new Vector3(source.X, source.Z, source.Y);
    }

    private static Vector3 ConvertDirection(Vector3 source)
    {
        return new Vector3(source.X, source.Z, source.Y);
    }

    private static List<ExportMaterial> BuildMaterialsFromModelSections(ModelMesh model, IReadOnlyList<ExportSectionMesh> sections, string baseName, string exportDirectory)
    {
        List<ExportMaterial> materials = [];
        HashSet<int> seen = [];
        string directory = string.IsNullOrWhiteSpace(exportDirectory) ? Environment.CurrentDirectory : exportDirectory;

        foreach (ExportSectionMesh section in sections)
        {
            if (!seen.Add(section.MaterialIndex))
                continue;

            if (section.MaterialIndex < 0 || section.MaterialIndex >= model.Sections.Count)
            {
                materials.Add(new ExportMaterial($"Material_{section.MaterialIndex}", section.MaterialIndex, null, null));
                continue;
            }

            MeshSectionData sectionData = model.Sections[section.MaterialIndex];
            string diffusePath = TrySaveTexture(directory, baseName, section.MaterialIndex, "diffuse", GetTexture(sectionData, UpkTextureType.uDiffuseMap));
            string normalPath = TrySaveTexture(directory, baseName, section.MaterialIndex, "normal", GetTexture(sectionData, UpkTextureType.uNormalMap));

            string materialName = sectionData.Material?.GetType().Name ?? $"Material_{section.MaterialIndex}";
            materials.Add(new ExportMaterial(materialName, section.MaterialIndex, diffusePath, normalPath));
        }

        return materials;
    }

    private static TextureExportData GetTexture(MeshSectionData sectionData, UpkTextureType textureType)
    {
        if (!sectionData.GetTextureType(textureType, out Texture2DData texture))
            return null;

        return new TextureExportData(texture.Name ?? string.Empty, texture.Texture2D, texture.MipIndex, texture.Data);
    }

    private static List<ExportMaterial> BuildMaterialsFromSkeletalMesh(
        USkeletalMesh skeletalMesh,
        IReadOnlyList<ExportSectionMesh> sections,
        string baseName,
        string exportDirectory,
        Action<string> log)
    {
        List<ExportMaterial> materials = [];
        HashSet<int> seen = [];
        string directory = string.IsNullOrWhiteSpace(exportDirectory) ? Environment.CurrentDirectory : exportDirectory;

        foreach (ExportSectionMesh section in sections)
        {
            if (!seen.Add(section.MaterialIndex))
                continue;

            string materialName = $"Material_{section.MaterialIndex}";
            string diffusePath = null;
            string normalPath = null;

            if (section.MaterialIndex >= 0 && section.MaterialIndex < skeletalMesh.Materials.Count)
            {
                FObject materialObject = skeletalMesh.Materials[section.MaterialIndex];
                materialName = materialObject?.Name?.ToString() ?? materialName;

                if (materialObject?.LoadObject<UMaterialInstanceConstant>() is UMaterialInstanceConstant material)
                {
                    diffusePath = TrySaveTexture(directory, baseName, section.MaterialIndex, "diffuse", LoadTexture(material, "Diffuse"));
                    normalPath = TrySaveTexture(directory, baseName, section.MaterialIndex, "normal", LoadTexture(material, "Norm"));
                }
            }

            if (diffusePath != null || normalPath != null)
                log?.Invoke($"Exported texture references for material {materialName}.");

            materials.Add(new ExportMaterial(materialName, section.MaterialIndex, diffusePath, normalPath));
        }

        return materials;
    }

    private static TextureExportData LoadTexture(UMaterialInstanceConstant material, string parameterName)
    {
        FObject textureObject = material.GetTextureParameterValue(parameterName);
        if (textureObject == null)
            return null;

        UTexture2D texture = textureObject.LoadObject<UTexture2D>();
        if (texture == null)
            return null;

        int mipIndex = texture.FirstResourceMemMip;
        FTexture2DMipMap mip;
        byte[] data;

        var textureCache = TextureFileCache.Instance;
        var textureEntry = TextureManifest.Instance.GetTextureEntryFromObject(textureObject);
        if (textureEntry != null)
        {
            textureCache.SetEntry(textureEntry, texture);
            textureCache.LoadTextureCache();
            mipIndex = (int)textureEntry.Data.Maps[0].Index;
            mip = textureCache.Texture2D.Mips[0];
        }
        else
        {
            mip = texture.Mips[mipIndex];
        }

        if (texture.Format != EPixelFormat.PF_A8R8G8B8)
        {
            DdsFile ddsFile = new();
            using Stream stream = textureEntry != null
                ? textureCache.Texture2D.GetObjectStream(0)
                : texture.GetObjectStream(mipIndex);
            ddsFile.Load(stream);
            data = ddsFile.BitmapData;
        }
        else
        {
            data = mip.Data;
        }

        return new TextureExportData(textureObject.Name?.ToString() ?? parameterName, texture, mipIndex, data);
    }

    private static string TrySaveTexture(
        string directory,
        string baseName,
        int materialIndex,
        string suffix,
        TextureExportData texture)
    {
        if (texture == null)
            return null;

        string safeTextureName = SanitizeFileName(texture.Name ?? $"{baseName}_{suffix}");
        string fileName = $"{SanitizeFileName(Path.GetFileNameWithoutExtension(baseName))}_mat{materialIndex}_{suffix}_{safeTextureName}.png";
        string absolutePath = Path.Combine(directory, fileName);

        File.WriteAllBytes(absolutePath, EncodeTexture(texture.Texture, texture.MipIndex, texture.Data));
        return fileName;
    }

    private static byte[] EncodeTexture(UTexture2D texture, int mipIndex, byte[] textureData)
    {
        int width = texture.Mips[mipIndex].SizeX;
        var bitmapSource = new RgbaBitmapSource(textureData, width);
        using MemoryStream stream = new();
        BitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);

        foreach (char c in value)
            builder.Append(invalidChars.Contains(c) ? '_' : c);

        return builder.ToString();
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Node";

        int separatorIndex = value.IndexOf('\0');
        if (separatorIndex >= 0)
            value = value[..separatorIndex];

        return value;
    }

    private sealed record ExportData(
        string Name,
        List<ExportBone> Bones,
        List<ExportSectionMesh> Sections,
        List<ExportMaterial> Materials);

    private sealed record ExportMaterial(
        string Name,
        int MaterialIndex,
        string DiffuseRelativePath,
        string NormalRelativePath);

    private sealed record ExportSectionMesh(
        string Name,
        int SectionIndex,
        int MaterialIndex,
        int UvChannelCount,
        List<ExportVertex> Vertices,
        List<int> Indices);

    private sealed record ExportBone(string Name, int ParentIndex, NumericsMatrix4x4 LocalTransform)
    {
        public NumericsMatrix4x4 GlobalTransform { get; init; } = NumericsMatrix4x4.Identity;
        public NumericsMatrix4x4 InverseGlobalTransform { get; init; } = NumericsMatrix4x4.Identity;
    }

    private sealed record ExportVertex(
        Vector3 Position,
        Vector3 Normal,
        Vector3 Tangent,
        Vector3 Bitangent,
        List<Vector2> Uvs,
        byte[] BoneIndices,
        byte[] BoneWeights)
    {
        public Vector2 GetUv(int index)
        {
            if ((uint)index < Uvs.Count)
                return Uvs[index];

            return Uvs.Count > 0 ? Uvs[0] : Vector2.Zero;
        }
    }

    private sealed record BoneVertexWeights(List<VertexWeight> Weights);

    private sealed record TextureExportData(string Name, UTexture2D Texture, int MipIndex, byte[] Data);

    private static Assimp.Matrix4x4 ToAssimp(this NumericsMatrix4x4 source)
    {
        // Assimp's FBX path expects matrices in aiMatrix4x4 field order, which does
        // not line up with System.Numerics.Matrix4x4 storage the way we use it here.
        // Without transposition, Blender imports the armature hierarchy but collapses
        // all bone heads to the origin.
        source = NumericsMatrix4x4.Transpose(source);

        return new Assimp.Matrix4x4(
            source.M11, source.M12, source.M13, source.M14,
            source.M21, source.M22, source.M23, source.M24,
            source.M31, source.M32, source.M33, source.M34,
            source.M41, source.M42, source.M43, source.M44);
    }

    private static Vector3D ToAssimpVector(this Vector3 source)
    {
        return new Vector3D(source.X, source.Y, source.Z);
    }
}
