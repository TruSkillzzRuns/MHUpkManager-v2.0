using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class ReferenceAlignmentProcessor
{
    public RetargetMesh AlignToReferenceMesh(
        RetargetMesh sourceMesh,
        RetargetMesh referenceMesh,
        IReadOnlyList<RetargetBone> originalSkeleton,
        Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (referenceMesh == null)
            throw new ArgumentNullException(nameof(referenceMesh));
        if (originalSkeleton == null || originalSkeleton.Count == 0)
            throw new ArgumentException("Original MHO skeleton is required.", nameof(originalSkeleton));

        GetMeshBounds(sourceMesh, out Vector3 sourceMin, out Vector3 sourceMax);
        GetMeshBounds(referenceMesh, out Vector3 referenceMin, out Vector3 referenceMax);

        Vector3 sourceBaseCenter = new((sourceMin.X + sourceMax.X) * 0.5f, sourceMin.Y, (sourceMin.Z + sourceMax.Z) * 0.5f);
        Vector3 referenceBaseCenter = new((referenceMin.X + referenceMax.X) * 0.5f, referenceMin.Y, (referenceMin.Z + referenceMax.Z) * 0.5f);

        List<RegionAnchor> anchors = RetargetRegions.BuildAnchors(originalSkeleton);
        Vector3 sourcePelvisCenter = GetMeshRegionCenter(sourceMesh, RetargetRegion.Pelvis, sourceBaseCenter);
        Vector3 sourceChestCenter = GetMeshRegionCenter(sourceMesh, RetargetRegion.Chest, sourceBaseCenter);
        Vector3 referencePelvisCenter = GetMeshRegionCenter(referenceMesh, RetargetRegion.Pelvis, RetargetRegions.GetAnchorCenter(RetargetRegions.GetRegionBoneNames(RetargetRegion.Pelvis), anchors));
        Vector3 referenceChestCenter = GetMeshRegionCenter(referenceMesh, RetargetRegion.Chest, RetargetRegions.GetAnchorCenter(RetargetRegions.GetRegionBoneNames(RetargetRegion.Chest), anchors));

        Vector3 baseDelta = referenceBaseCenter - sourceBaseCenter;
        Vector3 pelvisDelta = referencePelvisCenter - sourcePelvisCenter;
        Vector3 chestDelta = referenceChestCenter - sourceChestCenter;
        Vector3 translation = (baseDelta * 0.45f) + (pelvisDelta * 0.4f) + (chestDelta * 0.15f);

        RetargetMesh aligned = ApplyTranslation(sourceMesh, translation);
        log?.Invoke($"Aligned imported mesh to original MHO body frame with translation X {translation.X:0.###}, Y {translation.Y:0.###}, Z {translation.Z:0.###}.");
        return aligned;
    }

    private static RetargetMesh ApplyTranslation(RetargetMesh mesh, Vector3 translation)
    {
        RetargetMesh translated = mesh.DeepClone();
        foreach (RetargetSection section in translated.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
                vertex.Position += translation;
        }

        foreach (RetargetBone bone in translated.Bones)
        {
            bone.LocalTransform = TranslateMatrix(bone.LocalTransform, translation);
            bone.GlobalTransform = TranslateMatrix(bone.GlobalTransform, translation);
        }

        translated.RebuildBoneLookup();
        return translated;
    }

    private static Matrix4x4 TranslateMatrix(Matrix4x4 matrix, Vector3 translation)
    {
        matrix.M41 += translation.X;
        matrix.M42 += translation.Y;
        matrix.M43 += translation.Z;
        return matrix;
    }

    private static Vector3 GetMeshRegionCenter(RetargetMesh mesh, RetargetRegion region, Vector3 fallback)
    {
        List<RegionAnchor> anchors = BuildMeshAnchors(mesh);
        List<string> regionBones = [.. RetargetRegions.GetRegionBoneNames(region)];
        if (regionBones.Count == 0)
            return fallback;

        Vector3 center = RetargetRegions.GetAnchorCenter(regionBones, anchors);
        if (!float.IsFinite(center.X))
            return fallback;

        return center;
    }

    private static List<RegionAnchor> BuildMeshAnchors(RetargetMesh mesh)
    {
        GetMeshBounds(mesh, out Vector3 min, out Vector3 max);
        Vector3 center = (min + max) * 0.5f;
        float height = MathF.Max(1e-3f, max.Y - min.Y);
        float width = MathF.Max(1e-3f, max.X - min.X);
        float depth = MathF.Max(1e-3f, max.Z - min.Z);

        return
        [
            new RegionAnchor("root", RetargetRegion.Root, new Vector3(center.X, min.Y, center.Z)),
            new RegionAnchor("g_pelvis", RetargetRegion.Pelvis, new Vector3(center.X, min.Y + (height * 0.47f), center.Z)),
            new RegionAnchor("g_spine01", RetargetRegion.Spine, new Vector3(center.X, min.Y + (height * 0.58f), center.Z)),
            new RegionAnchor("g_spine03", RetargetRegion.Chest, new Vector3(center.X, min.Y + (height * 0.72f), center.Z)),
            new RegionAnchor("g_neck", RetargetRegion.Neck, new Vector3(center.X, min.Y + (height * 0.86f), center.Z)),
            new RegionAnchor("g_head", RetargetRegion.Head, new Vector3(center.X, min.Y + (height * 0.95f), center.Z)),
            new RegionAnchor("g_l_clavical", RetargetRegion.LeftShoulder, new Vector3(center.X, min.Y + (height * 0.78f), center.Z - (depth * 0.18f))),
            new RegionAnchor("g_r_clavical", RetargetRegion.RightShoulder, new Vector3(center.X, min.Y + (height * 0.78f), center.Z + (depth * 0.18f))),
            new RegionAnchor("g_l_shoulder", RetargetRegion.LeftUpperArm, new Vector3(center.X + (width * 0.22f), min.Y + (height * 0.72f), center.Z - (depth * 0.22f))),
            new RegionAnchor("g_r_shoulder", RetargetRegion.RightUpperArm, new Vector3(center.X + (width * 0.22f), min.Y + (height * 0.72f), center.Z + (depth * 0.22f))),
            new RegionAnchor("g_l_elbow", RetargetRegion.LeftForearm, new Vector3(center.X + (width * 0.36f), min.Y + (height * 0.67f), center.Z - (depth * 0.28f))),
            new RegionAnchor("g_r_elbow", RetargetRegion.RightForearm, new Vector3(center.X + (width * 0.36f), min.Y + (height * 0.67f), center.Z + (depth * 0.28f))),
            new RegionAnchor("g_l_wrist", RetargetRegion.LeftHand, new Vector3(center.X + (width * 0.48f), min.Y + (height * 0.63f), center.Z - (depth * 0.32f))),
            new RegionAnchor("g_r_wrist", RetargetRegion.RightHand, new Vector3(center.X + (width * 0.48f), min.Y + (height * 0.63f), center.Z + (depth * 0.32f))),
            new RegionAnchor("g_l_hip", RetargetRegion.LeftThigh, new Vector3(center.X, min.Y + (height * 0.42f), center.Z - (depth * 0.10f))),
            new RegionAnchor("g_r_hip", RetargetRegion.RightThigh, new Vector3(center.X, min.Y + (height * 0.42f), center.Z + (depth * 0.10f))),
            new RegionAnchor("g_l_knee", RetargetRegion.LeftCalf, new Vector3(center.X, min.Y + (height * 0.22f), center.Z - (depth * 0.08f))),
            new RegionAnchor("g_r_knee", RetargetRegion.RightCalf, new Vector3(center.X, min.Y + (height * 0.22f), center.Z + (depth * 0.08f))),
            new RegionAnchor("g_l_ankle", RetargetRegion.LeftFoot, new Vector3(center.X, min.Y + (height * 0.04f), center.Z - (depth * 0.08f))),
            new RegionAnchor("g_r_ankle", RetargetRegion.RightFoot, new Vector3(center.X, min.Y + (height * 0.04f), center.Z + (depth * 0.08f)))
        ];
    }

    private static void GetMeshBounds(RetargetMesh mesh, out Vector3 min, out Vector3 max)
    {
        min = new(float.PositiveInfinity);
        max = new(float.NegativeInfinity);

        foreach (RetargetSection section in mesh.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }
        }

        if (!float.IsFinite(min.X) || !float.IsFinite(max.X))
        {
            min = Vector3.Zero;
            max = Vector3.One;
        }
    }
}
