using Assimp;
using System.Globalization;
using System.Numerics;
using System.Text;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;

namespace MHUpkManager.Retargeting;

internal sealed class FBX2013Exporter
{
    private const string Creator = "MHUpkManager SkeletalMesh Retargeter";
    private static readonly NumericsMatrix4x4 AxisSwap = new(
        1, 0, 0, 0,
        0, 0, 1, 0,
        0, 1, 0, 0,
        0, 0, 0, 1);

    public void Export(string filePath, RetargetMesh mesh, Action<string> log = null)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Output FBX path is required.", nameof(filePath));

        string directory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        ExportAssimpScene(filePath, mesh);
        log?.Invoke($"Exported binary FBX file to {filePath}.");
    }

    private static void ExportAssimpScene(string filePath, RetargetMesh mesh)
    {
        using AssimpContext context = new();
        Scene scene = new()
        {
            RootNode = new Node(SanitizeName($"{mesh.MeshName}_Root"))
        };

        Node armatureNode = new(SanitizeName("Armature"));
        scene.RootNode.Children.Add(armatureNode);

        List<AssimpExportBone> bones = BuildAssimpBones(mesh.Bones);
        List<Node> boneNodes = BuildBoneNodes(bones);
        AttachBoneHierarchy(armatureNode, boneNodes, bones);
        BuildMaterials(scene, mesh);

        for (int sectionIndex = 0; sectionIndex < mesh.Sections.Count; sectionIndex++)
        {
            RetargetSection section = mesh.Sections[sectionIndex];
            Assimp.Mesh assimpMesh = BuildAssimpMesh(section, bones, sectionIndex);
            int meshIndex = scene.Meshes.Count;
            scene.Meshes.Add(assimpMesh);

            Node meshNode = new(SanitizeName(string.IsNullOrWhiteSpace(section.Name) ? $"Section_{sectionIndex}" : section.Name))
            {
                Transform = ToAssimpMatrix(NumericsMatrix4x4.Identity)
            };
            meshNode.MeshIndices.Add(meshIndex);
            armatureNode.Children.Add(meshNode);
        }

        if (!context.ExportFile(scene, filePath, "fbx"))
            throw new InvalidOperationException("Assimp failed to export the retargeted mesh as FBX.");
    }

    private static void BuildMaterials(Scene scene, RetargetMesh mesh)
    {
        if (mesh.Sections.Count == 0)
        {
            scene.Materials.Add(new Material { Name = "DefaultMaterial", ShadingMode = ShadingMode.Phong });
            return;
        }

        for (int i = 0; i < mesh.Sections.Count; i++)
        {
            RetargetSection section = mesh.Sections[i];
            scene.Materials.Add(new Material
            {
                Name = string.IsNullOrWhiteSpace(section.MaterialName) ? $"Material_{i}" : section.MaterialName,
                ShadingMode = ShadingMode.Phong,
                ColorDiffuse = new Color4D(1f, 1f, 1f, 1f),
                ColorAmbient = new Color4D(0f, 0f, 0f, 1f),
                ColorSpecular = new Color4D(0.2f, 0.2f, 0.2f, 1f),
                Opacity = 1f
            });
        }
    }

    private static List<AssimpExportBone> BuildAssimpBones(IReadOnlyList<RetargetBone> sourceBones)
    {
        List<AssimpExportBone> bones = new(sourceBones.Count);
        for (int i = 0; i < sourceBones.Count; i++)
        {
            RetargetBone bone = sourceBones[i];
            NumericsMatrix4x4 localTransform = ConvertBoneTransform(bone.LocalTransform);
            bones.Add(new AssimpExportBone(bone.Name, bone.ParentIndex, localTransform));
        }

        for (int i = 0; i < bones.Count; i++)
        {
            NumericsMatrix4x4 globalTransform = bones[i].ParentIndex >= 0 && bones[i].ParentIndex < bones.Count
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

    private static List<Node> BuildBoneNodes(IReadOnlyList<AssimpExportBone> bones)
    {
        List<Node> boneNodes = new(bones.Count);
        foreach (AssimpExportBone bone in bones)
        {
            boneNodes.Add(new Node(SanitizeName(bone.Name))
            {
                Transform = ToAssimpMatrix(bone.LocalTransform)
            });
        }

        return boneNodes;
    }

    private static void AttachBoneHierarchy(Node armatureNode, IReadOnlyList<Node> boneNodes, IReadOnlyList<AssimpExportBone> bones)
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

    private static Assimp.Mesh BuildAssimpMesh(RetargetSection section, IReadOnlyList<AssimpExportBone> bones, int materialIndex)
    {
        Assimp.Mesh mesh = new(SanitizeName(string.IsNullOrWhiteSpace(section.Name) ? $"Section_{materialIndex}" : section.Name), PrimitiveType.Triangle)
        {
            MaterialIndex = materialIndex
        };

        foreach (RetargetVertex vertex in section.Vertices)
        {
            mesh.Vertices.Add(ToAssimpVector(ToFbxPosition(vertex.Position)));
            mesh.Normals.Add(ToAssimpVector(ToFbxDirection(vertex.Normal)));
            mesh.Tangents.Add(ToAssimpVector(ToFbxDirection(vertex.Tangent)));
            mesh.BiTangents.Add(ToAssimpVector(ToFbxDirection(vertex.Bitangent)));
        }

        int uvChannelCount = section.Vertices.Count == 0 ? 1 : Math.Max(1, section.Vertices.Max(static vertex => vertex.UVs.Count));
        for (int uvChannelIndex = 0; uvChannelIndex < uvChannelCount; uvChannelIndex++)
        {
            mesh.TextureCoordinateChannels[uvChannelIndex] = [];
            mesh.UVComponentCount[uvChannelIndex] = 2;
            foreach (RetargetVertex vertex in section.Vertices)
            {
                Vector2 uv = uvChannelIndex < vertex.UVs.Count ? vertex.UVs[uvChannelIndex] : Vector2.Zero;
                mesh.TextureCoordinateChannels[uvChannelIndex].Add(new Vector3D(uv.X, 1.0f - uv.Y, 0.0f));
            }
        }

        for (int i = 0; i + 2 < section.Indices.Count; i += 3)
            mesh.Faces.Add(new Face([section.Indices[i], section.Indices[i + 1], section.Indices[i + 2]]));

        Dictionary<string, int> boneIndexByName = bones
            .Select((bone, index) => (bone, index))
            .ToDictionary(static pair => pair.bone.Name, static pair => pair.index, StringComparer.OrdinalIgnoreCase);

        for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
        {
            List<VertexWeight> weights = [];
            for (int vertexIndex = 0; vertexIndex < section.Vertices.Count; vertexIndex++)
            {
                foreach (RetargetWeight weight in section.Vertices[vertexIndex].Weights)
                {
                    if (weight.Weight <= 0.0f ||
                        !boneIndexByName.TryGetValue(weight.BoneName, out int mappedBoneIndex) ||
                        mappedBoneIndex != boneIndex)
                    {
                        continue;
                    }

                    weights.Add(new VertexWeight(vertexIndex, weight.Weight));
                }
            }

            Assimp.Bone assimpBone = new()
            {
                Name = SanitizeName(bones[boneIndex].Name),
                OffsetMatrix = ToAssimpMatrix(bones[boneIndex].InverseGlobalTransform)
            };

            foreach (VertexWeight weight in weights)
                assimpBone.VertexWeights.Add(weight);

            mesh.Bones.Add(assimpBone);
        }

        return mesh;
    }

    private static List<ExportBone> BuildBones(RetargetMesh mesh, IdGenerator id)
    {
        List<ExportBone> bones = new(mesh.Bones.Count);
        for (int i = 0; i < mesh.Bones.Count; i++)
        {
            RetargetBone bone = mesh.Bones[i];
            NumericsMatrix4x4 localTransform = ConvertBoneTransform(bone.LocalTransform);
            NumericsMatrix4x4.Decompose(localTransform, out Vector3 scale, out NumericsQuaternion rotation, out Vector3 translation);
            bones.Add(new ExportBone(
                bone.Name,
                bone.ParentIndex,
                id.Next(),
                id.Next(),
                localTransform,
                ToEulerDegrees(rotation),
                scale));
        }

        for (int i = 0; i < bones.Count; i++)
        {
            NumericsMatrix4x4 globalTransform = bones[i].ParentIndex >= 0 && bones[i].ParentIndex < bones.Count
                ? bones[i].LocalTransform * bones[bones[i].ParentIndex].GlobalTransform
                : bones[i].LocalTransform;

            bones[i] = bones[i] with { GlobalTransform = globalTransform };
        }

        return bones;
    }

    private static List<ExportSection> BuildSections(RetargetMesh mesh, IdGenerator id)
    {
        List<ExportSection> sections = [];
        foreach (RetargetSection section in mesh.Sections)
        {
            Dictionary<string, List<(int VertexIndex, float Weight)>> clusterWeights = new(StringComparer.OrdinalIgnoreCase);
            for (int vertexIndex = 0; vertexIndex < section.Vertices.Count; vertexIndex++)
            {
                foreach (RetargetWeight weight in section.Vertices[vertexIndex].Weights)
                {
                    if (weight.Weight <= 0.0f || string.IsNullOrWhiteSpace(weight.BoneName))
                        continue;

                    if (!clusterWeights.TryGetValue(weight.BoneName, out List<(int VertexIndex, float Weight)> entries))
                    {
                        entries = [];
                        clusterWeights[weight.BoneName] = entries;
                    }

                    entries.Add((vertexIndex, weight.Weight));
                }
            }

            sections.Add(new ExportSection(
                section,
                id.Next(),
                id.Next(),
                id.Next(),
                clusterWeights.ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlyList<(int VertexIndex, float Weight)>)pair.Value)));
        }

        return sections;
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

    private static void WriteDefinitions(StreamWriter writer, IReadOnlyList<ExportBone> bones, IReadOnlyList<ExportSection> sections)
    {
        int deformerCount = sections.Count + sections.Sum(section => section.ClusterWeights.Count);
        writer.WriteLine("Definitions:  {");
        writer.WriteLine("\tVersion: 100");
        writer.WriteLine($"\tCount: {6 + (deformerCount > 0 ? 2 : 0)}");
        writer.WriteLine("\tObjectType: \"GlobalSettings\" {");
        writer.WriteLine("\t\tCount: 1");
        writer.WriteLine("\t}");
        writer.WriteLine("\tObjectType: \"Model\" {");
        writer.WriteLine($"\t\tCount: {1 + sections.Count + bones.Count}");
        writer.WriteLine("\t}");
        writer.WriteLine("\tObjectType: \"NodeAttribute\" {");
        writer.WriteLine($"\t\tCount: {bones.Count}");
        writer.WriteLine("\t}");
        writer.WriteLine("\tObjectType: \"Geometry\" {");
        writer.WriteLine($"\t\tCount: {sections.Count}");
        writer.WriteLine("\t}");
        writer.WriteLine("\tObjectType: \"Material\" {");
        writer.WriteLine($"\t\tCount: {sections.Count}");
        writer.WriteLine("\t}");
        if (deformerCount > 0)
        {
            writer.WriteLine("\tObjectType: \"Deformer\" {");
            writer.WriteLine($"\t\tCount: {deformerCount}");
            writer.WriteLine("\t}");
            writer.WriteLine("\tObjectType: \"Pose\" {");
            writer.WriteLine($"\t\tCount: {sections.Count}");
            writer.WriteLine("\t}");
        }

        writer.WriteLine("}");
    }

    private static void WriteObjects(StreamWriter writer, RetargetMesh mesh, long rootModelId, IReadOnlyList<ExportBone> bones, IReadOnlyList<ExportSection> sections)
    {
        writer.WriteLine("Objects:  {");
        WriteRootModel(writer, mesh.MeshName, rootModelId);
        foreach (ExportSection section in sections)
        {
            WriteGeometry(writer, section);
            WriteMeshModel(writer, section);
            WriteMaterial(writer, section);
        }

        foreach (ExportBone bone in bones)
        {
            WriteBoneModel(writer, bone);
            WriteBoneAttribute(writer, bone);
        }

        foreach (ExportSection section in sections)
        {
            WriteSkin(writer, section);
            foreach ((string boneName, IReadOnlyList<(int VertexIndex, float Weight)> weights) in section.ClusterWeights)
            {
                ExportBone bone = bones.First(bone => string.Equals(bone.Name, boneName, StringComparison.OrdinalIgnoreCase));
                WriteCluster(writer, section, bone, weights);
            }

            WriteBindPose(writer, sections, bones, section, rootModelId);
        }

        writer.WriteLine("}");
    }

    private static void WriteRootModel(StreamWriter writer, string meshName, long rootModelId)
    {
        writer.WriteLine($"\tModel: {rootModelId}, \"Model::{Escape(meshName)}_Root\", \"Null\" {{");
        writer.WriteLine("\t\tVersion: 232");
        writer.WriteLine("\t\tProperties70:  {");
        writer.WriteLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
        writer.WriteLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
        writer.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
        writer.WriteLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
        writer.WriteLine("\t\t}");
        writer.WriteLine("\t\tShading: Y");
        writer.WriteLine("\t\tCulling: \"CullingOff\"");
        writer.WriteLine("\t}");
    }

    private static void WriteBoneAttribute(StreamWriter writer, ExportBone bone)
    {
        string skeletonType = bone.ParentIndex < 0 ? "Root" : "Limb";
        writer.WriteLine($"\tNodeAttribute: {bone.AttributeId}, \"NodeAttribute::{Escape(bone.Name)}\", \"Skeleton\" {{");
        writer.WriteLine("\t\tTypeFlags: \"Skeleton\"");
        writer.WriteLine("\t\tProperties70:  {");
        writer.WriteLine("\t\t\tP: \"Size\", \"double\", \"Number\", \"\",100");
        writer.WriteLine("\t\t\tP: \"LimbLength\", \"double\", \"Number\", \"\",1");
        writer.WriteLine("\t\t}");
        writer.WriteLine($"\t\tType: \"{skeletonType}\"");
        writer.WriteLine("\t}");
    }

    private static void WriteGeometry(StreamWriter writer, ExportSection section)
    {
        writer.WriteLine($"\tGeometry: {section.GeometryId}, \"Geometry::{Escape(section.Section.Name)}\", \"Mesh\" {{");
        writer.WriteLine("\t\tVersion: 124");
        writer.WriteLine("\t\tVertices: " + FormatArray(BuildVertexPositions(section.Section)));
        writer.WriteLine("\t\tPolygonVertexIndex: " + FormatArray(ToPolygonVertexIndices(section.Section.Indices)));

        writer.WriteLine("\t\tLayerElementNormal: 0 {");
        writer.WriteLine("\t\t\tVersion: 101");
        writer.WriteLine("\t\t\tName: \"\"");
        writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
        writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
        writer.WriteLine("\t\t\tNormals: " + FormatArray(BuildPolygonNormals(section.Section)));
        writer.WriteLine("\t\t}");

        for (int uvChannel = 0; uvChannel < Math.Max(1, section.Section.Vertices.Max(static vertex => vertex.UVs.Count)); uvChannel++)
        {
            writer.WriteLine($"\t\tLayerElementUV: {uvChannel} {{");
            writer.WriteLine("\t\t\tVersion: 101");
            writer.WriteLine($"\t\t\tName: \"UVChannel_{uvChannel + 1}\"");
            writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
            writer.WriteLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
            writer.WriteLine("\t\t\tUV: " + FormatArray(BuildPolygonUvs(section.Section, uvChannel)));
            writer.WriteLine("\t\t\tUVIndex: " + FormatArray(BuildPolygonLinearIndices(section.Section)));
            writer.WriteLine("\t\t}");
        }

        writer.WriteLine("\t\tLayerElementColor: 0 {");
        writer.WriteLine("\t\t\tVersion: 101");
        writer.WriteLine("\t\t\tName: \"\"");
        writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygonVertex\"");
        writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
        writer.WriteLine("\t\t\tColors: " + FormatArray(BuildPolygonColors(section.Section)));
        writer.WriteLine("\t\t}");

        writer.WriteLine("\t\tLayerElementMaterial: 0 {");
        writer.WriteLine("\t\t\tVersion: 101");
        writer.WriteLine("\t\t\tName: \"\"");
        writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygon\"");
        writer.WriteLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
        writer.WriteLine("\t\t\tMaterials: " + FormatArray(Enumerable.Repeat(0, section.Section.Indices.Count / 3)));
        writer.WriteLine("\t\t}");

        writer.WriteLine("\t\tLayerElementSmoothing: 0 {");
        writer.WriteLine("\t\t\tVersion: 102");
        writer.WriteLine("\t\t\tName: \"\"");
        writer.WriteLine("\t\t\tMappingInformationType: \"ByPolygon\"");
        writer.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
        writer.WriteLine("\t\t\tSmoothing: " + FormatArray(section.Section.TriangleSmoothingGroups.Count == 0
            ? Enumerable.Repeat(1, section.Section.Indices.Count / 3)
            : section.Section.TriangleSmoothingGroups));
        writer.WriteLine("\t\t}");

        writer.WriteLine("\t\tLayer: 0 {");
        writer.WriteLine("\t\t\tVersion: 100");
        writer.WriteLine("\t\t\tLayerElement:  {");
        writer.WriteLine("\t\t\t\tType: \"LayerElementNormal\"");
        writer.WriteLine("\t\t\t\tTypedIndex: 0");
        writer.WriteLine("\t\t\t}");
        writer.WriteLine("\t\t\tLayerElement:  {");
        writer.WriteLine("\t\t\t\tType: \"LayerElementColor\"");
        writer.WriteLine("\t\t\t\tTypedIndex: 0");
        writer.WriteLine("\t\t\t}");
        writer.WriteLine("\t\t\tLayerElement:  {");
        writer.WriteLine("\t\t\t\tType: \"LayerElementMaterial\"");
        writer.WriteLine("\t\t\t\tTypedIndex: 0");
        writer.WriteLine("\t\t\t}");
        writer.WriteLine("\t\t\tLayerElement:  {");
        writer.WriteLine("\t\t\t\tType: \"LayerElementSmoothing\"");
        writer.WriteLine("\t\t\t\tTypedIndex: 0");
        writer.WriteLine("\t\t\t}");
        for (int uvChannel = 0; uvChannel < Math.Max(1, section.Section.Vertices.Max(static vertex => vertex.UVs.Count)); uvChannel++)
        {
            writer.WriteLine("\t\t\tLayerElement:  {");
            writer.WriteLine("\t\t\t\tType: \"LayerElementUV\"");
            writer.WriteLine($"\t\t\t\tTypedIndex: {uvChannel}");
            writer.WriteLine("\t\t\t}");
        }

        writer.WriteLine("\t\t}");
        writer.WriteLine("\t}");
    }

    private static void WriteMeshModel(StreamWriter writer, ExportSection section)
    {
        writer.WriteLine($"\tModel: {section.ModelId}, \"Model::{Escape(section.Section.Name)}\", \"Mesh\" {{");
        writer.WriteLine("\t\tVersion: 232");
        writer.WriteLine("\t\tProperties70:  {");
        writer.WriteLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
        writer.WriteLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
        writer.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
        writer.WriteLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
        writer.WriteLine("\t\t}");
        writer.WriteLine("\t\tShading: T");
        writer.WriteLine("\t\tCulling: \"CullingOff\"");
        writer.WriteLine("\t}");
    }

    private static void WriteMaterial(StreamWriter writer, ExportSection section)
    {
        writer.WriteLine($"\tMaterial: {section.MaterialId}, \"Material::{Escape(section.Section.MaterialName)}\", \"\" {{");
        writer.WriteLine("\t\tVersion: 102");
        writer.WriteLine("\t\tShadingModel: \"phong\"");
        writer.WriteLine("\t\tMultiLayer: 0");
        writer.WriteLine("\t\tProperties70:  {");
        writer.WriteLine("\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",1,1,1");
        writer.WriteLine("\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",0,0,0");
        writer.WriteLine("\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0.2,0.2,0.2");
        writer.WriteLine("\t\t}");
        writer.WriteLine("\t}");
    }

    private static void WriteBoneModel(StreamWriter writer, ExportBone bone)
    {
        writer.WriteLine($"\tModel: {bone.ModelId}, \"Model::{Escape(bone.Name)}\", \"LimbNode\" {{");
        writer.WriteLine("\t\tVersion: 232");
        writer.WriteLine("\t\tProperties70:  {");
        writer.WriteLine("\t\t\tP: \"Size\", \"double\", \"Number\", \"\",100");
        writer.WriteLine("\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\"," + FormatVector(bone.Translation));
        writer.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\"," + FormatVector(bone.RotationDegrees));
        writer.WriteLine("\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\"," + FormatVector(bone.Scale));
        writer.WriteLine("\t\t}");
        writer.WriteLine("\t\tShading: Y");
        writer.WriteLine("\t\tCulling: \"CullingOff\"");
        writer.WriteLine("\t}");
    }

    private static void WriteSkin(StreamWriter writer, ExportSection section)
    {
        writer.WriteLine($"\tDeformer: {section.SkinId}, \"Deformer::{Escape(section.Section.Name)}Skin\", \"Skin\" {{");
        writer.WriteLine("\t\tVersion: 101");
        writer.WriteLine("\t\tType: \"Skin\"");
        writer.WriteLine("\t}");
    }

    private static void WriteCluster(StreamWriter writer, ExportSection section, ExportBone bone, IReadOnlyList<(int VertexIndex, float Weight)> weights)
    {
        long clusterId = ComputeClusterId(section.SkinId, bone.ModelId);
        writer.WriteLine($"\tDeformer: {clusterId}, \"SubDeformer::{Escape(bone.Name)}\", \"Cluster\" {{");
        writer.WriteLine("\t\tVersion: 100");
        writer.WriteLine("\t\tIndexes: " + FormatArray(weights.Select(static weight => weight.VertexIndex)));
        writer.WriteLine("\t\tWeights: " + FormatArray(weights.Select(static weight => (double)weight.Weight)));
        writer.WriteLine("\t\tTransform: " + FormatMatrix(NumericsMatrix4x4.Identity));
        writer.WriteLine("\t\tTransformLink: " + FormatMatrix(bone.GlobalTransform));
        writer.WriteLine("\t}");
    }

    private static void WriteBindPose(StreamWriter writer, IReadOnlyList<ExportSection> sections, IReadOnlyList<ExportBone> bones, ExportSection section, long rootModelId)
    {
        long poseId = section.SkinId + 1000;
        writer.WriteLine($"\tPose: {poseId}, \"Pose::{Escape(section.Section.Name)}\", \"BindPose\" {{");
        writer.WriteLine("\t\tType: \"BindPose\"");
        writer.WriteLine($"\t\tNbPoseNodes: {2 + bones.Count}");

        writer.WriteLine("\t\tPoseNode:  {");
        writer.WriteLine($"\t\t\tNode: {rootModelId}");
        writer.WriteLine("\t\t\tMatrix: " + FormatMatrix(NumericsMatrix4x4.Identity));
        writer.WriteLine("\t\t}");

        writer.WriteLine("\t\tPoseNode:  {");
        writer.WriteLine($"\t\t\tNode: {section.ModelId}");
        writer.WriteLine("\t\t\tMatrix: " + FormatMatrix(NumericsMatrix4x4.Identity));
        writer.WriteLine("\t\t}");

        foreach (ExportBone bone in bones)
        {
            writer.WriteLine("\t\tPoseNode:  {");
            writer.WriteLine($"\t\t\tNode: {bone.ModelId}");
            writer.WriteLine("\t\t\tMatrix: " + FormatMatrix(bone.GlobalTransform));
            writer.WriteLine("\t\t}");
        }

        writer.WriteLine("\t}");
    }

    private static void WriteConnections(StreamWriter writer, long rootModelId, IReadOnlyList<ExportBone> bones, IReadOnlyList<ExportSection> sections)
    {
        writer.WriteLine("Connections:  {");
        writer.WriteLine($"\tC: \"OO\",{rootModelId},0");
        foreach (ExportSection section in sections)
        {
            writer.WriteLine($"\tC: \"OO\",{section.GeometryId},{section.ModelId}");
            writer.WriteLine($"\tC: \"OO\",{section.ModelId},{rootModelId}");
            writer.WriteLine($"\tC: \"OO\",{section.MaterialId},{section.ModelId}");
            writer.WriteLine($"\tC: \"OO\",{section.SkinId},{section.GeometryId}");
        }

        for (int i = 0; i < bones.Count; i++)
        {
            ExportBone bone = bones[i];
            long parentId = bone.ParentIndex >= 0 && bone.ParentIndex < bones.Count ? bones[bone.ParentIndex].ModelId : rootModelId;
            writer.WriteLine($"\tC: \"OO\",{bone.ModelId},{parentId}");
            writer.WriteLine($"\tC: \"OO\",{bone.AttributeId},{bone.ModelId}");
        }

        foreach (ExportSection section in sections)
        {
            foreach (string boneName in section.ClusterWeights.Keys)
            {
                ExportBone bone = bones.First(bone => string.Equals(bone.Name, boneName, StringComparison.OrdinalIgnoreCase));
                long clusterId = ComputeClusterId(section.SkinId, bone.ModelId);
                writer.WriteLine($"\tC: \"OO\",{clusterId},{section.SkinId}");
                writer.WriteLine($"\tC: \"OO\",{bone.ModelId},{clusterId}");
            }
        }

        writer.WriteLine("}");
    }

    private static IEnumerable<double> BuildPolygonNormals(RetargetSection section)
    {
        for (int i = 0; i < section.Indices.Count; i++)
        {
            RetargetVertex vertex = section.Vertices[section.Indices[i]];
            Vector3 normal = ToFbxDirection(vertex.Normal);
            yield return normal.X;
            yield return normal.Y;
            yield return normal.Z;
        }
    }

    private static IEnumerable<double> BuildVertexPositions(RetargetSection section)
    {
        foreach (RetargetVertex vertex in section.Vertices)
        {
            Vector3 position = ToFbxPosition(vertex.Position);
            yield return position.X;
            yield return position.Y;
            yield return position.Z;
        }
    }

    private static IEnumerable<double> BuildPolygonUvs(RetargetSection section, int uvChannel)
    {
        for (int i = 0; i < section.Indices.Count; i++)
        {
            RetargetVertex vertex = section.Vertices[section.Indices[i]];
            Vector2 uv = uvChannel < vertex.UVs.Count ? vertex.UVs[uvChannel] : Vector2.Zero;
            yield return uv.X;
            yield return 1.0 - uv.Y;
        }
    }

    private static IEnumerable<double> BuildPolygonColors(RetargetSection section)
    {
        for (int i = 0; i < section.Indices.Count; i++)
        {
            UpkManager.Models.UpkFile.Core.FColor color = section.Vertices[section.Indices[i]].Color;
            yield return color.R / 255.0;
            yield return color.G / 255.0;
            yield return color.B / 255.0;
            yield return color.A / 255.0;
        }
    }

    private static IEnumerable<int> BuildPolygonLinearIndices(RetargetSection section)
    {
        return Enumerable.Range(0, section.Indices.Count);
    }

    private static IEnumerable<int> ToPolygonVertexIndices(IReadOnlyList<int> indices)
    {
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            yield return indices[i];
            yield return indices[i + 1];
            yield return -indices[i + 2] - 1;
        }
    }

    private static Vector3 ToFbxPosition(Vector3 value) => new(value.X, value.Z, value.Y);
    private static Vector3 ToFbxDirection(Vector3 value) => new(value.X, value.Z, value.Y);

    private static NumericsMatrix4x4 ConvertBoneTransform(NumericsMatrix4x4 source)
    {
        return AxisSwap * source * AxisSwap;
    }

    private static Vector3 ToEulerDegrees(NumericsQuaternion rotation)
    {
        Vector3 euler = QuaternionToEuler(rotation);
        return new Vector3(
            MathF.PI == 0 ? 0.0f : euler.X * (180.0f / MathF.PI),
            MathF.PI == 0 ? 0.0f : euler.Y * (180.0f / MathF.PI),
            MathF.PI == 0 ? 0.0f : euler.Z * (180.0f / MathF.PI));
    }

    private static Vector3 QuaternionToEuler(NumericsQuaternion quaternion)
    {
        Vector3 angles = new();

        float sinrCosp = 2.0f * ((quaternion.W * quaternion.X) + (quaternion.Y * quaternion.Z));
        float cosrCosp = 1.0f - (2.0f * ((quaternion.X * quaternion.X) + (quaternion.Y * quaternion.Y)));
        angles.X = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2.0f * ((quaternion.W * quaternion.Y) - (quaternion.Z * quaternion.X));
        angles.Y = MathF.Abs(sinp) >= 1.0f
            ? MathF.CopySign(MathF.PI / 2.0f, sinp)
            : MathF.Asin(sinp);

        float sinyCosp = 2.0f * ((quaternion.W * quaternion.Z) + (quaternion.X * quaternion.Y));
        float cosyCosp = 1.0f - (2.0f * ((quaternion.Y * quaternion.Y) + (quaternion.Z * quaternion.Z)));
        angles.Z = MathF.Atan2(sinyCosp, cosyCosp);

        return angles;
    }

    private static string FormatVector(Vector3 value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{value.X:0.######},{value.Y:0.######},{value.Z:0.######}");
    }

    private static string FormatMatrix(NumericsMatrix4x4 matrix)
    {
        return string.Join(",",
            new[]
            {
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44
            }.Select(static value => value.ToString("0.######", CultureInfo.InvariantCulture)));
    }

    private static string FormatArray<T>(IEnumerable<T> values)
    {
        return "*" + values.Count() + " {" + string.Join(",", values.Select(FormatValue)) + "}";
    }

    private static string FormatValue<T>(T value)
    {
        return value switch
        {
            double number => number.ToString("0.######", CultureInfo.InvariantCulture),
            float number => number.ToString("0.######", CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string SanitizeName(string value) => string.IsNullOrWhiteSpace(value) ? "Node" : value.Replace("::", "_", StringComparison.Ordinal);

    private static long ComputeClusterId(long skinId, long boneId) => skinId ^ (boneId << 1);

    private static Assimp.Matrix4x4 ToAssimpMatrix(NumericsMatrix4x4 source)
    {
        source = NumericsMatrix4x4.Transpose(source);
        return new Assimp.Matrix4x4(
            source.M11, source.M12, source.M13, source.M14,
            source.M21, source.M22, source.M23, source.M24,
            source.M31, source.M32, source.M33, source.M34,
            source.M41, source.M42, source.M43, source.M44);
    }

    private static Vector3D ToAssimpVector(Vector3 source)
    {
        return new Vector3D(source.X, source.Y, source.Z);
    }

    private sealed class IdGenerator
    {
        private long _next = 100000;
        public long Next() => _next++;
    }

    private sealed record ExportBone(string Name, int ParentIndex, long ModelId, long AttributeId, NumericsMatrix4x4 LocalTransform, Vector3 RotationDegrees, Vector3 Scale)
    {
        public Vector3 Translation => new(LocalTransform.M41, LocalTransform.M42, LocalTransform.M43);
        public NumericsMatrix4x4 GlobalTransform { get; init; } = NumericsMatrix4x4.Identity;
    }

    private sealed record AssimpExportBone(string Name, int ParentIndex, NumericsMatrix4x4 LocalTransform)
    {
        public NumericsMatrix4x4 GlobalTransform { get; init; } = NumericsMatrix4x4.Identity;
        public NumericsMatrix4x4 InverseGlobalTransform { get; init; } = NumericsMatrix4x4.Identity;
    }

    private sealed record ExportSection(
        RetargetSection Section,
        long GeometryId,
        long ModelId,
        long MaterialId,
        IReadOnlyDictionary<string, IReadOnlyList<(int VertexIndex, float Weight)>> ClusterWeights)
    {
        public long SkinId => GeometryId + 500000;
    }
}
