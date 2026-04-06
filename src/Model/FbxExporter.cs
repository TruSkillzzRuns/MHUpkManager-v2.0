using DDSLib;

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Windows.Media.Imaging;

using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Types;

namespace MHUpkManager.Model
{
    internal static class FbxExporter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private const string Creator = "MHUpkManager";

        public static void Export(string fileName, ModelMesh model)
        {
            ArgumentNullException.ThrowIfNull(model);

            // Keep the authored FBX structure intact. The previous ASCII -> Assimp -> binary
            // round-trip dropped skeleton semantics and caused Blender to collapse armatures.
            ExportAscii(fileName, model);
        }

        private static void ExportAscii(string fileName, ModelMesh model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var directory = Path.GetDirectoryName(fileName);
            if (string.IsNullOrWhiteSpace(directory))
                directory = Environment.CurrentDirectory;

            Directory.CreateDirectory(directory);

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var id = new IdGenerator();
            var materials = BuildMaterials(model, directory, baseName, id);
            var bones = BuildBones(model, id);
            var sectionMeshes = BuildSectionMeshes(model, id);
            var skins = BuildSkins(sectionMeshes, bones, id);

            long rootModelId = id.Next();

            using var writer = new StreamWriter(fileName, false, new UTF8Encoding(false));

            WriteHeader(writer);
            WriteGlobalSettings(writer);
            WriteDocuments(writer);
            WriteReferences(writer);
            WriteDefinitions(writer, materials, bones, sectionMeshes, skins);
            WriteObjects(writer, model, materials, bones, sectionMeshes, skins, rootModelId);
            WriteConnections(writer, materials, bones, sectionMeshes, skins, rootModelId);
        }

        private static void WriteHeader(StreamWriter writer)
        {
            writer.WriteLine("; FBX 7.3.0 project file");
            writer.WriteLine("FBXHeaderExtension:  {");
            writer.WriteLine("\tFBXHeaderVersion: 1003");
            writer.WriteLine("\tFBXVersion: 7300");
            writer.WriteLine($"\tCreator: \"{Escape(Creator)}\"");
            writer.WriteLine("}");
        }

        private static void WriteGlobalSettings(StreamWriter writer)
        {
            writer.WriteLine("GlobalSettings:  {");
            writer.WriteLine("\tVersion: 1000");
            writer.WriteLine("\tProperties70:  {");
            writer.WriteLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
            writer.WriteLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            writer.WriteLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
            writer.WriteLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            writer.WriteLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            writer.WriteLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            writer.WriteLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
            writer.WriteLine("\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",1");
            writer.WriteLine("\t}");
            writer.WriteLine("}");
        }

        private static void WriteDocuments(StreamWriter writer)
        {
            writer.WriteLine("Documents:  {");
            writer.WriteLine("\tCount: 1");
            writer.WriteLine("\tDocument: 1, \"Scene\", \"Scene\" {");
            writer.WriteLine("\t\tProperties70:  {");
            writer.WriteLine("\t\t}");
            writer.WriteLine("\t\tRootNode: 0");
            writer.WriteLine("\t}");
            writer.WriteLine("}");
        }

        private static void WriteReferences(StreamWriter writer)
        {
            writer.WriteLine("References:  {");
            writer.WriteLine("}");
        }

        private static void WriteDefinitions(
            StreamWriter writer,
            List<MaterialInfo> materials,
            List<BoneInfo> bones,
            List<SectionMeshInfo> sectionMeshes,
            List<SkinInfo> skins)
        {
            int textureCount = materials.Count(x => x.DiffuseTexture != null) + materials.Count(x => x.NormalTexture != null);
            int deformerCount = skins.Sum(static skin => 1 + skin.Clusters.Count);
            int objectTypeCount = 3;

            if (materials.Count > 0)
            {
                objectTypeCount += 1;
                if (textureCount > 0)
                    objectTypeCount += 2;
            }

            if (deformerCount > 0)
                objectTypeCount += 2;

            writer.WriteLine("Definitions:  {");
            writer.WriteLine("\tVersion: 100");
            writer.WriteLine($"\tCount: {objectTypeCount}");

            writer.WriteLine("\tObjectType: \"GlobalSettings\" {");
            writer.WriteLine("\t\tCount: 1");
            writer.WriteLine("\t}");

            writer.WriteLine("\tObjectType: \"Model\" {");
            writer.WriteLine($"\t\tCount: {1 + sectionMeshes.Count + bones.Count}");
            writer.WriteLine("\t}");

            if (bones.Count > 0)
            {
                writer.WriteLine("\tObjectType: \"NodeAttribute\" {");
                writer.WriteLine($"\t\tCount: {bones.Count}");
                writer.WriteLine("\t}");
            }

            writer.WriteLine("\tObjectType: \"Geometry\" {");
            writer.WriteLine($"\t\tCount: {sectionMeshes.Count}");
            writer.WriteLine("\t}");

            if (materials.Count > 0)
            {
                writer.WriteLine("\tObjectType: \"Material\" {");
                writer.WriteLine($"\t\tCount: {materials.Count}");
                writer.WriteLine("\t}");
            }

            if (textureCount > 0)
            {
                writer.WriteLine("\tObjectType: \"Texture\" {");
                writer.WriteLine($"\t\tCount: {textureCount}");
                writer.WriteLine("\t}");

                writer.WriteLine("\tObjectType: \"Video\" {");
                writer.WriteLine($"\t\tCount: {textureCount}");
                writer.WriteLine("\t}");
            }

            if (deformerCount > 0)
            {
                writer.WriteLine("\tObjectType: \"Deformer\" {");
                writer.WriteLine($"\t\tCount: {deformerCount}");
                writer.WriteLine("\t}");

                writer.WriteLine("\tObjectType: \"Pose\" {");
                writer.WriteLine($"\t\tCount: {skins.Count}");
                writer.WriteLine("\t}");
            }

            writer.WriteLine("}");
        }

        private static void WriteObjects(
            StreamWriter writer,
            ModelMesh model,
            List<MaterialInfo> materials,
            List<BoneInfo> bones,
            List<SectionMeshInfo> sectionMeshes,
            List<SkinInfo> skins,
            long rootModelId)
        {
            writer.WriteLine("Objects:  {");
            WriteRootModel(writer, model, rootModelId);

            foreach (var sectionMesh in sectionMeshes)
            {
                WriteGeometry(writer, sectionMesh);
                WriteMeshModel(writer, sectionMesh);
            }

            foreach (var bone in bones)
                WriteBoneAttribute(writer, bone);

            foreach (var bone in bones)
                WriteBoneModel(writer, bone);

            foreach (var material in materials)
            {
                WriteMaterial(writer, material);
                WriteTexture(writer, material.DiffuseTexture);
                WriteTexture(writer, material.NormalTexture);
            }

            foreach (var skin in skins)
                WriteSkin(writer, skin, rootModelId, bones);

            writer.WriteLine("}");
        }

        private static void WriteGeometry(StreamWriter writer, SectionMeshInfo sectionMesh)
        {
            writer.WriteLine($"\tGeometry: {sectionMesh.GeometryId}, \"Geometry::{Escape(sectionMesh.Name)}\", \"Mesh\" {{");
            writer.WriteLine("\t\tVersion: 124");
            writer.WriteLine("\t\tVertices: " + FormatArray(sectionMesh.Vertices.SelectMany(v => ToFbxPosition(v.Position))));
            writer.WriteLine("\t\tPolygonVertexIndex: " + FormatArray(ToPolygonVertexIndices(sectionMesh.Indices)));

            writer.WriteLine("\t\tLayerElementNormal: 0 {");
            writer.WriteLine("\t\t\tVersion: 101");
            writer.WriteLine("\t\t\tName: \"\"");
            writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
            writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
            writer.WriteLine("\t\t\tNormals: " + FormatArray(BuildPolygonVertexNormals(sectionMesh)));
            writer.WriteLine("\t\t}");

            for (int uvChannelIndex = 0; uvChannelIndex < sectionMesh.UvChannelCount; uvChannelIndex++)
            {
                writer.WriteLine($"\t\tLayerElementUV: {uvChannelIndex} {{");
                writer.WriteLine("\t\t\tVersion: 101");
                writer.WriteLine($"\t\t\tName: \"UVChannel_{uvChannelIndex + 1}\"");
                writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                writer.WriteLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
                writer.WriteLine("\t\t\tUV: " + FormatArray(BuildPolygonVertexUvs(sectionMesh, uvChannelIndex)));
                writer.WriteLine("\t\t\tUVIndex: " + FormatArray(BuildPolygonVertexLinearIndices(sectionMesh)));
                writer.WriteLine("\t\t}");
            }

            writer.WriteLine("\t\tLayerElementMaterial: 0 {");
            writer.WriteLine("\t\t\tVersion: 101");
            writer.WriteLine("\t\t\tName: \"\"");
            writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygon\"");
            writer.WriteLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
            writer.WriteLine("\t\t\tMaterials: " + FormatArray(Enumerable.Repeat(0, sectionMesh.Indices.Count / 3)));
            writer.WriteLine("\t\t}");

            writer.WriteLine("\t\tLayerElementSmoothing: 0 {");
            writer.WriteLine("\t\t\tVersion: 102");
            writer.WriteLine("\t\t\tName: \"\"");
            writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygon\"");
            writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
            writer.WriteLine("\t\t\tSmoothing: " + FormatArray(Enumerable.Repeat(1, sectionMesh.Indices.Count / 3)));
            writer.WriteLine("\t\t}");

            writer.WriteLine("\t\tLayer: 0 {");
            writer.WriteLine("\t\t\tVersion: 100");
            writer.WriteLine("\t\t\tLayerElement:  {");
            writer.WriteLine("\t\t\t\tType: \"LayerElementNormal\"");
            writer.WriteLine("\t\t\t\tTypedIndex: 0");
            writer.WriteLine("\t\t\t}");
            writer.WriteLine("\t\t\tLayerElement:  {");
            writer.WriteLine("\t\t\t\tType: \"LayerElementMaterial\"");
            writer.WriteLine("\t\t\t\tTypedIndex: 0");
            writer.WriteLine("\t\t\t}");
            for (int uvChannelIndex = 0; uvChannelIndex < sectionMesh.UvChannelCount; uvChannelIndex++)
            {
                writer.WriteLine("\t\t\tLayerElement:  {");
                writer.WriteLine("\t\t\t\tType: \"LayerElementUV\"");
                writer.WriteLine($"\t\t\t\tTypedIndex: {uvChannelIndex}");
                writer.WriteLine("\t\t\t}");
            }
            writer.WriteLine("\t\t\tLayerElement:  {");
            writer.WriteLine("\t\t\t\tType: \"LayerElementSmoothing\"");
            writer.WriteLine("\t\t\t\tTypedIndex: 0");
            writer.WriteLine("\t\t\t}");
            writer.WriteLine("\t\t}");
            writer.WriteLine("\t}");
        }

        private static void WriteRootModel(StreamWriter writer, ModelMesh model, long rootModelId)
        {
            writer.WriteLine($"\tModel: {rootModelId}, \"Model::{Escape(model.ModelName)}_Root\", \"Null\" {{");
            writer.WriteLine("\t\tVersion: 232");
            writer.WriteLine("\t\tProperties70:  {");
            writer.WriteLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            writer.WriteLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            writer.WriteLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
            writer.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
            writer.WriteLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
            writer.WriteLine("\t\t}");
            writer.WriteLine("\t\tShading: Y");
            writer.WriteLine("\t\tCulling: \"CullingOff\"");
            writer.WriteLine("\t}");
        }

        private static void WriteMeshModel(StreamWriter writer, SectionMeshInfo sectionMesh)
        {
            writer.WriteLine($"\tModel: {sectionMesh.ModelId}, \"Model::{Escape(sectionMesh.Name)}\", \"Mesh\" {{");
            writer.WriteLine("\t\tVersion: 232");
            writer.WriteLine("\t\tProperties70:  {");
            writer.WriteLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            writer.WriteLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            writer.WriteLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
            writer.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
            writer.WriteLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
            writer.WriteLine("\t\t}");
            writer.WriteLine("\t\tShading: T");
            writer.WriteLine("\t\tCulling: \"CullingOff\"");
            writer.WriteLine("\t}");
        }

        private static void WriteBoneModel(StreamWriter writer, BoneInfo bone)
        {
            writer.WriteLine($"\tModel: {bone.ModelId}, \"Model::{Escape(bone.Name)}\", \"LimbNode\" {{");
            writer.WriteLine("\t\tVersion: 232");
            writer.WriteLine("\t\tProperties70:  {");
            writer.WriteLine("\t\t\tP: \"Size\", \"double\", \"Number\", \"\",100");
            writer.WriteLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\"," +
                $"{FormatNumber(bone.Translation.X)},{FormatNumber(bone.Translation.Y)},{FormatNumber(bone.Translation.Z)}");
            writer.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\"," +
                $"{FormatNumber(bone.RotationEuler.X)},{FormatNumber(bone.RotationEuler.Y)},{FormatNumber(bone.RotationEuler.Z)}");
            writer.WriteLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\"," +
                $"{FormatNumber(bone.Scale.X)},{FormatNumber(bone.Scale.Y)},{FormatNumber(bone.Scale.Z)}");
            writer.WriteLine("\t\t}");
            writer.WriteLine("\t\tShading: Y");
            writer.WriteLine("\t\tCulling: \"CullingOff\"");
            writer.WriteLine("\t}");
        }

        private static void WriteBoneAttribute(StreamWriter writer, BoneInfo bone)
        {
            string skeletonType = bone.ParentIndex < 0 ? "Root" : "Limb";
            writer.WriteLine($"\tNodeAttribute: {bone.AttributeId}, \"NodeAttribute::{Escape(bone.Name)}\", \"Skeleton\" {{");
            writer.WriteLine("\t\tTypeFlags: \"Skeleton\"");
            writer.WriteLine("\t\tProperties70:  {");
            writer.WriteLine($"\t\t\tP: \"Size\", \"double\", \"Number\", \"\",{FormatNumber(100)}");
            writer.WriteLine($"\t\t\tP: \"LimbLength\", \"double\", \"Number\", \"\",{FormatNumber(1)}");
            writer.WriteLine("\t\t}"); 
            writer.WriteLine($"\t\tType: \"{skeletonType}\"");
            writer.WriteLine("\t}");
        }

        private static void WriteMaterial(StreamWriter writer, MaterialInfo material)
        {
            writer.WriteLine($"\tMaterial: {material.MaterialId}, \"Material::{Escape(material.Name)}\", \"\" {{");
            writer.WriteLine("\t\tVersion: 102");
            writer.WriteLine("\t\tShadingModel: \"phong\"");
            writer.WriteLine("\t\tMultiLayer: 0");
            writer.WriteLine("\t\tProperties70:  {");
            writer.WriteLine("\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",1,1,1");
            writer.WriteLine("\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",0,0,0");
            writer.WriteLine("\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0.2,0.2,0.2");
            writer.WriteLine("\t\t\tP: \"Shininess\", \"double\", \"Number\", \"\",16");
            writer.WriteLine("\t\t\tP: \"Opacity\", \"double\", \"Number\", \"\",1");
            writer.WriteLine("\t\t}");
            writer.WriteLine("\t}");
        }

        private static void WriteTexture(StreamWriter writer, TextureInfo texture)
        {
            if (texture == null)
                return;

            writer.WriteLine($"\tVideo: {texture.VideoId}, \"Video::{Escape(texture.Name)}\", \"Clip\" {{");
            writer.WriteLine("\t\tType: \"Clip\"");
            writer.WriteLine($"\t\tFileName: \"{Escape(texture.AbsolutePath)}\"");
            writer.WriteLine($"\t\tRelativeFilename: \"{Escape(texture.RelativePath)}\"");
            writer.WriteLine("\t}");

            writer.WriteLine($"\tTexture: {texture.TextureId}, \"Texture::{Escape(texture.Name)}\", \"TextureVideoClip\" {{");
            writer.WriteLine("\t\tType: \"TextureVideoClip\"");
            writer.WriteLine("\t\tVersion: 202");
            writer.WriteLine($"\t\tTextureName: \"Texture::{Escape(texture.Name)}\"");
            writer.WriteLine($"\t\tMedia: \"Video::{Escape(texture.Name)}\"");
            writer.WriteLine($"\t\tFileName: \"{Escape(texture.AbsolutePath)}\"");
            writer.WriteLine($"\t\tRelativeFilename: \"{Escape(texture.RelativePath)}\"");
            writer.WriteLine("\t\tModelUVTranslation: 0,0");
            writer.WriteLine("\t\tModelUVScaling: 1,1");
            writer.WriteLine("\t\tTexture_Alpha_Source: \"None\"");
            writer.WriteLine("\t\tCropping: 0,0,0,0");
            writer.WriteLine("\t}");
        }

        private static void WriteSkin(StreamWriter writer, SkinInfo skin, long rootModelId, List<BoneInfo> bones)
        {
            writer.WriteLine($"\tDeformer: {skin.SkinId}, \"Deformer::{Escape(skin.Name)}\", \"Skin\" {{");
            writer.WriteLine("\t\tVersion: 101");
            writer.WriteLine("\t}");

            foreach (var cluster in skin.Clusters)
            {
                var bone = bones[cluster.BoneIndex];
                writer.WriteLine($"\tDeformer: {cluster.ClusterId}, \"SubDeformer::{Escape(bone.Name)}\", \"Cluster\" {{");
                writer.WriteLine("\t\tVersion: 100");
                writer.WriteLine("\t\tIndexes: " + FormatArray(cluster.Indices));
                writer.WriteLine("\t\tWeights: " + FormatArray(cluster.Weights));
                writer.WriteLine("\t\tTransform: " + FormatArray(IdentityMatrix()));
                writer.WriteLine("\t\tTransformLink: " + FormatArray(ToFbxMatrix(bone.GlobalMatrix)));
                writer.WriteLine("\t\tMode: \"TotalOne\"");
                writer.WriteLine("\t}");
            }

            writer.WriteLine($"\tPose: {skin.PoseId}, \"Pose::BindPose\", \"BindPose\" {{");
            writer.WriteLine("\t\tType: \"BindPose\"");
            writer.WriteLine("\t\tVersion: 100");
            writer.WriteLine($"\t\tNbPoseNodes: {2 + bones.Count}");

            writer.WriteLine("\t\tPoseNode:  {");
            writer.WriteLine($"\t\t\tNode: {rootModelId}");
            writer.WriteLine("\t\t\tMatrix: " + FormatArray(IdentityMatrix()));
            writer.WriteLine("\t\t}");

            writer.WriteLine("\t\tPoseNode:  {");
            writer.WriteLine($"\t\t\tNode: {skin.MeshModelId}");
            writer.WriteLine("\t\t\tMatrix: " + FormatArray(IdentityMatrix()));
            writer.WriteLine("\t\t}");

            foreach (var bone in bones)
            {
                writer.WriteLine("\t\tPoseNode:  {");
                writer.WriteLine($"\t\t\tNode: {bone.ModelId}");
                writer.WriteLine("\t\t\tMatrix: " + FormatArray(ToFbxMatrix(bone.GlobalMatrix)));
                writer.WriteLine("\t\t}");
            }

            writer.WriteLine("\t}");
        }

        private static void WriteConnections(
            StreamWriter writer,
            List<MaterialInfo> materials,
            List<BoneInfo> bones,
            List<SectionMeshInfo> sectionMeshes,
            List<SkinInfo> skins,
            long rootModelId)
        {
            writer.WriteLine("Connections:  {");
            writer.WriteLine($"\tC: \"OO\",{rootModelId},0");

            foreach (var sectionMesh in sectionMeshes)
            {
                writer.WriteLine($"\tC: \"OO\",{sectionMesh.GeometryId},{sectionMesh.ModelId}");
                writer.WriteLine($"\tC: \"OO\",{sectionMesh.ModelId},{rootModelId}");
            }

            foreach (var bone in bones)
            {
                long parentId = bone.ParentIndex >= 0 ? bones[bone.ParentIndex].ModelId : rootModelId;
                writer.WriteLine($"\tC: \"OO\",{bone.ModelId},{parentId}");
                writer.WriteLine($"\tC: \"OO\",{bone.AttributeId},{bone.ModelId}");
            }

            Dictionary<int, MaterialInfo> materialsByIndex = materials.ToDictionary(static x => x.MaterialIndex);
            foreach (var sectionMesh in sectionMeshes)
            {
                if (!materialsByIndex.TryGetValue(sectionMesh.MaterialIndex, out MaterialInfo material))
                    continue;

                writer.WriteLine($"\tC: \"OO\",{material.MaterialId},{sectionMesh.ModelId}");

                if (material.DiffuseTexture != null)
                {
                    writer.WriteLine($"\tC: \"OO\",{material.DiffuseTexture.VideoId},{material.DiffuseTexture.TextureId}");
                    writer.WriteLine($"\tC: \"OP\",{material.DiffuseTexture.TextureId},{material.MaterialId},\"DiffuseColor\"");
                }

                if (material.NormalTexture != null)
                {
                    writer.WriteLine($"\tC: \"OO\",{material.NormalTexture.VideoId},{material.NormalTexture.TextureId}");
                    writer.WriteLine($"\tC: \"OP\",{material.NormalTexture.TextureId},{material.MaterialId},\"NormalMap\"");
                }
            }

            foreach (var skin in skins)
            {
                writer.WriteLine($"\tC: \"OO\",{skin.SkinId},{skin.GeometryId}");
                foreach (var cluster in skin.Clusters)
                {
                    writer.WriteLine($"\tC: \"OO\",{cluster.ClusterId},{skin.SkinId}");
                    writer.WriteLine($"\tC: \"OO\",{bones[cluster.BoneIndex].ModelId},{cluster.ClusterId}");
                }
            }

            writer.WriteLine("}");
        }

        private static List<MaterialInfo> BuildMaterials(ModelMesh model, string directory, string baseName, IdGenerator id)
        {
            var materials = new List<MaterialInfo>();
            var seen = new HashSet<int>();

            foreach (var section in model.Sections)
            {
                if (!seen.Add(section.MaterialIndex))
                    continue;

                var info = new MaterialInfo
                {
                    Name = $"Material_{section.MaterialIndex}",
                    MaterialIndex = section.MaterialIndex,
                    MaterialId = id.Next()
                };

                if (section.GetTextureType(TextureType.uDiffuseMap, out var diffuse))
                    info.DiffuseTexture = SaveTexture(diffuse, directory, baseName, section.MaterialIndex, "diffuse", id);

                if (section.GetTextureType(TextureType.uNormalMap, out var normal))
                    info.NormalTexture = SaveTexture(normal, directory, baseName, section.MaterialIndex, "normal", id);

                materials.Add(info);
            }

            return materials;
        }

        private static TextureInfo SaveTexture(
            Texture2DData texture,
            string directory,
            string baseName,
            int materialIndex,
            string suffix,
            IdGenerator id)
        {
            var safeTextureName = SanitizeFileName(texture.Name ?? $"{baseName}_{suffix}");
            var fileName = $"{SanitizeFileName(baseName)}_mat{materialIndex}_{suffix}_{safeTextureName}.png";
            var absolutePath = Path.Combine(directory, fileName);

            File.WriteAllBytes(absolutePath, EncodeTexture(texture.Texture2D, texture.MipIndex, texture.Data));

            return new TextureInfo
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                AbsolutePath = NormalizePath(absolutePath),
                RelativePath = NormalizePath(Path.GetFileName(absolutePath)),
                TextureId = id.Next(),
                VideoId = id.Next()
            };
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

        private static List<BoneInfo> BuildBones(ModelMesh model, IdGenerator id)
        {
            if (model.Bones == null || model.Bones.Count == 0)
                return [];

            var bones = new List<BoneInfo>(model.Bones.Count);

            for (int i = 0; i < model.Bones.Count; i++)
            {
                var sourceBone = model.Bones[i];
                var localMatrix = ModelFormats.MHInvert * sourceBone.LocalTransform * ModelFormats.MHInvert;

                if (!Matrix4x4.Decompose(localMatrix, out var scale, out var rotation, out var translation))
                {
                    scale = Vector3.One;
                    rotation = Quaternion.Identity;
                    translation = localMatrix.Translation;
                }

                bones.Add(new BoneInfo
                {
                    ModelId = id.Next(),
                    AttributeId = id.Next(),
                    Name = sourceBone.Name,
                    ParentIndex = -1,
                    LocalMatrix = localMatrix,
                    GlobalMatrix = Matrix4x4.Identity,
                    Translation = translation,
                    RotationEuler = QuaternionToEulerDegrees(rotation),
                    Scale = scale
                });
            }

            for (int i = 0; i < bones.Count; i++)
            {
                var bone = bones[i];
                bone.ParentIndex = NormalizeParentIndex(model.Bones[i].ParentIndex, i, bones);
                bone.GlobalMatrix = bone.ParentIndex >= 0
                    ? bone.LocalMatrix * bones[bone.ParentIndex].GlobalMatrix
                    : bone.LocalMatrix;
                bones[i] = bone;
            }

            return bones;
        }

        private static List<SectionMeshInfo> BuildSectionMeshes(ModelMesh model, IdGenerator id)
        {
            if (model.Mesh is USkeletalMesh skeletalMesh)
                return BuildSkeletalSectionMeshes(model, skeletalMesh, id);

            return BuildRigidSectionMeshes(model, id);
        }

        private static List<SectionMeshInfo> BuildSkeletalSectionMeshes(ModelMesh model, USkeletalMesh skeletalMesh, IdGenerator id)
        {
            List<SectionMeshInfo> results = [];
            FStaticLODModel lod = skeletalMesh.LODModels[0];
            FGPUSkinVertexBase[] sourceVertices = [.. lod.VertexBufferGPUSkin.VertexData];
            uint[] sourceIndices = [.. lod.MultiSizeIndexContainer.IndexBuffer];
            int uvChannelCount = Math.Max(1, checked((int)lod.VertexBufferGPUSkin.NumTexCoords));

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
                        vertices.Add(CreateExportVertex(sourceVertices[globalVertexIndex], uvChannelCount, chunk.BoneMap));
                    }

                    indices.Add(localVertexIndex);
                }

                results.Add(new SectionMeshInfo
                {
                    Name = $"{model.ModelName}_Section_{sectionIndex}",
                    SectionIndex = sectionIndex,
                    MaterialIndex = checked((int)section.MaterialIndex),
                    GeometryId = id.Next(),
                    ModelId = id.Next(),
                    UvChannelCount = uvChannelCount,
                    Vertices = vertices,
                    Indices = indices
                });
            }

            return results;
        }

        private static List<SectionMeshInfo> BuildRigidSectionMeshes(ModelMesh model, IdGenerator id)
        {
            List<SectionMeshInfo> results = [];

            for (int sectionIndex = 0; sectionIndex < model.Sections.Count; sectionIndex++)
            {
                MeshSectionData section = model.Sections[sectionIndex];
                Dictionary<int, int> localVertexByGlobalVertex = [];
                List<ExportVertex> vertices = [];
                List<int> indices = [];
                uint start = section.BaseIndex;
                uint end = start + section.NumTriangles * 3;

                for (uint i = start; i < end; i++)
                {
                    int globalVertexIndex = model.Indices[i];
                    if (!localVertexByGlobalVertex.TryGetValue(globalVertexIndex, out int localVertexIndex))
                    {
                        localVertexIndex = vertices.Count;
                        localVertexByGlobalVertex.Add(globalVertexIndex, localVertexIndex);
                        vertices.Add(CreateExportVertex(model.Vertices[globalVertexIndex]));
                    }

                    indices.Add(localVertexIndex);
                }

                results.Add(new SectionMeshInfo
                {
                    Name = $"{model.ModelName}_Section_{sectionIndex}",
                    SectionIndex = sectionIndex,
                    MaterialIndex = section.MaterialIndex,
                    GeometryId = id.Next(),
                    ModelId = id.Next(),
                    UvChannelCount = 1,
                    Vertices = vertices,
                    Indices = indices
                });
            }

            return results;
        }

        private static List<SkinInfo> BuildSkins(List<SectionMeshInfo> sectionMeshes, List<BoneInfo> bones, IdGenerator id)
        {
            if (bones.Count == 0)
                return [];

            List<SkinInfo> skins = [];

            foreach (SectionMeshInfo sectionMesh in sectionMeshes)
            {
                List<ClusterInfo> clusters = [];

                for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
                {
                    var indices = new List<int>();
                    var weights = new List<double>();

                    for (int vertexIndex = 0; vertexIndex < sectionMesh.Vertices.Count; vertexIndex++)
                    {
                        var vertex = sectionMesh.Vertices[vertexIndex];
                        AddWeight(indices, weights, vertexIndex, vertex.BoneIndices[0], vertex.BoneWeights[0], boneIndex);
                        AddWeight(indices, weights, vertexIndex, vertex.BoneIndices[1], vertex.BoneWeights[1], boneIndex);
                        AddWeight(indices, weights, vertexIndex, vertex.BoneIndices[2], vertex.BoneWeights[2], boneIndex);
                        AddWeight(indices, weights, vertexIndex, vertex.BoneIndices[3], vertex.BoneWeights[3], boneIndex);
                    }

                    if (indices.Count == 0)
                        continue;

                    clusters.Add(new ClusterInfo
                    {
                        BoneIndex = boneIndex,
                        ClusterId = id.Next(),
                        Indices = indices,
                        Weights = weights
                    });
                }

                skins.Add(new SkinInfo
                {
                    Name = sectionMesh.Name,
                    MeshModelId = sectionMesh.ModelId,
                    GeometryId = sectionMesh.GeometryId,
                    SkinId = id.Next(),
                    PoseId = id.Next(),
                    Clusters = clusters
                });
            }

            return skins;
        }

        private static void AddWeight(List<int> indices, List<double> weights, int vertexIndex, byte vertexBone, byte vertexWeight, int boneIndex)
        {
            if (vertexWeight == 0 || vertexBone != boneIndex)
                return;

            indices.Add(vertexIndex);
            weights.Add(vertexWeight / 255.0);
        }

        private static int NormalizeParentIndex(int candidateParentIndex, int boneIndex, List<BoneInfo> bones)
        {
            if (candidateParentIndex < 0 || candidateParentIndex >= bones.Count)
                return -1;

            if (candidateParentIndex == boneIndex)
                return -1;

            if (candidateParentIndex > boneIndex)
                return -1;

            int cursor = candidateParentIndex;
            while (cursor >= 0)
            {
                if (cursor == boneIndex)
                    return -1;

                cursor = bones[cursor].ParentIndex;
            }

            return candidateParentIndex;
        }

        private static List<int> ToPolygonVertexIndices(IReadOnlyList<int> indices)
        {
            var polygonIndices = new List<int>(indices.Count);

            for (int i = 0; i < indices.Count; i += 3)
            {
                polygonIndices.Add(indices[i]);
                polygonIndices.Add(indices[i + 1]);
                polygonIndices.Add(-indices[i + 2] - 1);
            }

            return polygonIndices;
        }

        private static List<double> BuildPolygonVertexNormals(SectionMeshInfo sectionMesh)
        {
            var normals = new List<double>(sectionMesh.Indices.Count * 3);

            foreach (var index in sectionMesh.Indices)
                normals.AddRange(ToFbxNormal(sectionMesh.Vertices[index].Normal));

            return normals;
        }

        private static List<double> BuildPolygonVertexUvs(SectionMeshInfo sectionMesh, int uvChannelIndex)
        {
            var uvs = new List<double>(sectionMesh.Vertices.Count * 2);

            foreach (var vertex in sectionMesh.Vertices)
                uvs.AddRange(ToFbxUv(vertex.GetUv(uvChannelIndex)));

            return uvs;
        }

        private static List<int> BuildPolygonVertexLinearIndices(SectionMeshInfo sectionMesh)
        {
            return [.. sectionMesh.Indices];
        }

        private static IEnumerable<double> ToFbxPosition(Vector3 position)
        {
            yield return position.X;
            yield return position.Z;
            yield return position.Y;
        }

        private static IEnumerable<double> ToFbxNormal(Vector3 normal)
        {
            yield return normal.X;
            yield return normal.Z;
            yield return normal.Y;
        }

        private static IEnumerable<double> ToFbxUv(Vector2 uv)
        {
            yield return uv.X;
            yield return 1.0 - uv.Y;
        }

        private static IEnumerable<double> ToFbxMatrix(Matrix4x4 matrix)
        {
            yield return matrix.M11; yield return matrix.M12; yield return matrix.M13; yield return matrix.M14;
            yield return matrix.M21; yield return matrix.M22; yield return matrix.M23; yield return matrix.M24;
            yield return matrix.M31; yield return matrix.M32; yield return matrix.M33; yield return matrix.M34;
            yield return matrix.M41; yield return matrix.M42; yield return matrix.M43; yield return matrix.M44;
        }

        private static IEnumerable<double> IdentityMatrix()
        {
            yield return 1; yield return 0; yield return 0; yield return 0;
            yield return 0; yield return 1; yield return 0; yield return 0;
            yield return 0; yield return 0; yield return 1; yield return 0;
            yield return 0; yield return 0; yield return 0; yield return 1;
        }

        private static Vector3 QuaternionToEulerDegrees(Quaternion quaternion)
        {
            quaternion = Quaternion.Normalize(quaternion);

            double sinrCosp = 2 * (quaternion.W * quaternion.X + quaternion.Y * quaternion.Z);
            double cosrCosp = 1 - 2 * (quaternion.X * quaternion.X + quaternion.Y * quaternion.Y);
            double x = Math.Atan2(sinrCosp, cosrCosp);

            double sinp = 2 * (quaternion.W * quaternion.Y - quaternion.Z * quaternion.X);
            double y = Math.Abs(sinp) >= 1
                ? Math.CopySign(Math.PI / 2, sinp)
                : Math.Asin(sinp);

            double sinyCosp = 2 * (quaternion.W * quaternion.Z + quaternion.X * quaternion.Y);
            double cosyCosp = 1 - 2 * (quaternion.Y * quaternion.Y + quaternion.Z * quaternion.Z);
            double z = Math.Atan2(sinyCosp, cosyCosp);

            return new Vector3(
                (float)(x * 180.0 / Math.PI),
                (float)(y * 180.0 / Math.PI),
                (float)(z * 180.0 / Math.PI));
        }

        private static string FormatArray(IEnumerable<int> values)
        {
            var list = values.Select(x => x.ToString(Invariant)).ToArray();
            return $"*{list.Length} {{ a: {string.Join(",", list)} }}";
        }

        private static string FormatArray(IEnumerable<double> values)
        {
            var list = values.Select(FormatNumber).ToArray();
            return $"*{list.Length} {{ a: {string.Join(",", list)} }}";
        }

        private static string FormatNumber(double value)
        {
            if (Math.Abs(value) < 1e-9)
                value = 0;

            return value.ToString("0.########", Invariant);
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string NormalizePath(string value)
        {
            return value.Replace('\\', '/');
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);

            foreach (char c in value)
                builder.Append(invalidChars.Contains(c) ? '_' : c);

            return builder.ToString();
        }

        private static ExportVertex CreateExportVertex(GLVertex vertex)
        {
            return new ExportVertex
            {
                Position = vertex.Position,
                Normal = vertex.Normal,
                Tangent = vertex.Tangent,
                Bitangent = vertex.Bitangent,
                Uvs = [vertex.TexCoord],
                BoneIndices = [vertex.Bone0, vertex.Bone1, vertex.Bone2, vertex.Bone3],
                BoneWeights = [vertex.Weight0, vertex.Weight1, vertex.Weight2, vertex.Weight3]
            };
        }

        private static ExportVertex CreateExportVertex(FGPUSkinVertexBase vertex, int uvChannelCount, UArray<ushort> boneMap)
        {
            Vector3 normal = GLVertex.SafeNormal(vertex.TangentZ);
            Vector3 tangent = GLVertex.SafeNormal(vertex.TangentX);

            return new ExportVertex
            {
                Position = vertex.GetVector3(),
                Normal = normal,
                Tangent = tangent,
                Bitangent = GLVertex.ComputeBitangent(normal, tangent, vertex.TangentZ),
                Uvs = [.. Enumerable.Range(0, uvChannelCount).Select(vertex.GetVector2)],
                BoneIndices =
                [
                    RemapBone(vertex.InfluenceBones[0], boneMap),
                    RemapBone(vertex.InfluenceBones[1], boneMap),
                    RemapBone(vertex.InfluenceBones[2], boneMap),
                    RemapBone(vertex.InfluenceBones[3], boneMap)
                ],
                BoneWeights = [.. vertex.InfluenceWeights]
            };
        }

        private static byte RemapBone(byte localBoneIndex, UArray<ushort> boneMap)
        {
            if (localBoneIndex < boneMap.Count)
                return checked((byte)boneMap[localBoneIndex]);

            return 0;
        }

        private sealed class IdGenerator
        {
            private long _next = 100000;

            public long Next() => _next++;
        }

        private sealed class MaterialInfo
        {
            public string Name { get; set; } = string.Empty;
            public int MaterialIndex { get; set; }
            public long MaterialId { get; set; }
            public TextureInfo DiffuseTexture { get; set; }
            public TextureInfo NormalTexture { get; set; }
        }

        private sealed class TextureInfo
        {
            public string Name { get; set; } = string.Empty;
            public string AbsolutePath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public long TextureId { get; set; }
            public long VideoId { get; set; }
        }

        private struct BoneInfo
        {
            public long ModelId;
            public long AttributeId;
            public string Name;
            public int ParentIndex;
            public Matrix4x4 LocalMatrix;
            public Matrix4x4 GlobalMatrix;
            public Vector3 Translation;
            public Vector3 RotationEuler;
            public Vector3 Scale;
        }

        private sealed class SkinInfo
        {
            public string Name { get; set; } = string.Empty;
            public long MeshModelId { get; set; }
            public long GeometryId { get; set; }
            public long SkinId { get; set; }
            public long PoseId { get; set; }
            public List<ClusterInfo> Clusters { get; set; } = [];
        }

        private sealed class SectionMeshInfo
        {
            public string Name { get; set; } = string.Empty;
            public int SectionIndex { get; set; }
            public int MaterialIndex { get; set; }
            public long GeometryId { get; set; }
            public long ModelId { get; set; }
            public int UvChannelCount { get; set; }
            public List<ExportVertex> Vertices { get; set; } = [];
            public List<int> Indices { get; set; } = [];
        }

        private sealed class ClusterInfo
        {
            public int BoneIndex { get; set; }
            public long ClusterId { get; set; }
            public List<int> Indices { get; set; } = [];
            public List<double> Weights { get; set; } = [];
        }

        private sealed class ExportVertex
        {
            public Vector3 Position { get; set; }
            public Vector3 Normal { get; set; }
            public Vector3 Tangent { get; set; }
            public Vector3 Bitangent { get; set; }
            public List<Vector2> Uvs { get; set; } = [];
            public byte[] BoneIndices { get; set; } = [0, 0, 0, 0];
            public byte[] BoneWeights { get; set; } = [0, 0, 0, 0];

            public Vector2 GetUv(int index)
            {
                if ((uint)index < Uvs.Count)
                    return Uvs[index];

                return Uvs.Count > 0 ? Uvs[0] : Vector2.Zero;
            }
        }
    }
}
