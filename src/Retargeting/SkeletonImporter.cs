using Assimp;
using System.Numerics;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;

namespace MHUpkManager.Retargeting;

internal sealed class SkeletonImporter
{
    public SkeletonDefinition Import(string fbxPath, Action<string> log = null)
    {
        if (string.IsNullOrWhiteSpace(fbxPath))
            throw new ArgumentException("Skeleton path is required.", nameof(fbxPath));

        if (!File.Exists(fbxPath))
            throw new FileNotFoundException("Skeleton FBX was not found.", fbxPath);

        using AssimpContext context = new();
        Scene scene = context.ImportFile(
            fbxPath,
            PostProcessSteps.ValidateDataStructure |
            PostProcessSteps.ImproveCacheLocality);

        if (scene == null || scene.RootNode == null)
            throw new InvalidOperationException("The selected FBX does not contain a readable scene.");

        SkeletonDefinition skeleton = new()
        {
            SourcePath = fbxPath
        };

        HashSet<string> meshBoneNames = CollectBoneNames(scene);
        AddBonesRecursive(scene.RootNode, -1, NumericsMatrix4x4.Identity, meshBoneNames, skeleton);
        skeleton.RebuildBoneLookup();

        if (skeleton.Bones.Count == 0)
            throw new InvalidOperationException("The selected FBX did not expose a usable bone hierarchy.");

        log?.Invoke($"Imported player skeleton with {skeleton.Bones.Count} bones from {fbxPath}.");
        return skeleton;
    }

    private static HashSet<string> CollectBoneNames(Scene scene)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (Assimp.Mesh mesh in scene.Meshes)
        {
            foreach (Assimp.Bone bone in mesh.Bones)
                names.Add(bone.Name);
        }

        return names;
    }

    private static void AddBonesRecursive(
        Node node,
        int parentIndex,
        NumericsMatrix4x4 parentTransform,
        HashSet<string> meshBoneNames,
        SkeletonDefinition skeleton)
    {
        bool keepNode = meshBoneNames.Count == 0 ||
            meshBoneNames.Contains(node.Name) ||
            ContainsTrackedDescendant(node, meshBoneNames);

        NumericsMatrix4x4 local = ToNumerics(node.Transform);
        NumericsMatrix4x4 global = parentTransform * local;
        int currentIndex = parentIndex;

        if (keepNode)
        {
            currentIndex = skeleton.Bones.Count;
            skeleton.Bones.Add(new RetargetBone
            {
                Name = node.Name,
                ParentIndex = parentIndex,
                LocalTransform = ConvertTransform(local),
                GlobalTransform = ConvertTransform(global)
            });
        }

        foreach (Node child in node.Children)
            AddBonesRecursive(child, currentIndex, global, meshBoneNames, skeleton);
    }

    private static bool ContainsTrackedDescendant(Node node, HashSet<string> names)
    {
        foreach (Node child in node.Children)
        {
            if (names.Contains(child.Name) || ContainsTrackedDescendant(child, names))
                return true;
        }

        return false;
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
}
