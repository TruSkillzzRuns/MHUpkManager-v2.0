using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class AutoScaleProcessor
{
    public float ComputeScaleFactorToReferenceMesh(
        RetargetMesh sourceMesh,
        RetargetMesh referenceMesh,
        Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (referenceMesh == null)
            throw new ArgumentNullException(nameof(referenceMesh));

        float boundsRatio = ComputeBoundsRatio(sourceMesh, referenceMesh);
        log?.Invoke($"Auto scale matched imported geometry against original MHO reference mesh bounds; selected uniform scale {boundsRatio:0.###}x.");
        return ClampScale(boundsRatio);
    }

    public float ComputeScaleFactor(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string> boneMapping,
        Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (targetSkeleton == null)
            throw new ArgumentNullException(nameof(targetSkeleton));

        sourceMesh.RebuildBoneLookup();
        targetSkeleton.RebuildBoneLookup();

        List<float> segmentRatios = CollectMappedSegmentRatios(sourceMesh, targetSkeleton, boneMapping);
        if (segmentRatios.Count >= 3)
        {
            float medianRatio = ComputeMedian(segmentRatios);
            log?.Invoke($"Auto scale matched {segmentRatios.Count} mapped bone segment(s); selected uniform scale {medianRatio:0.###}x.");
            return ClampScale(medianRatio);
        }

        float boundsRatio = ComputeBoundsRatio(sourceMesh, targetSkeleton);
        log?.Invoke($"Auto scale used geometry/skeleton bounds fallback; selected uniform scale {boundsRatio:0.###}x.");
        return ClampScale(boundsRatio);
    }

    public RetargetMesh ApplyScale(RetargetMesh sourceMesh, float scaleFactor, Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));

        if (!float.IsFinite(scaleFactor) || scaleFactor <= 0.0f)
            throw new InvalidOperationException("The calculated mesh scale factor was not valid.");

        RetargetMesh scaled = sourceMesh.DeepClone();
        scaled.AppliedScale *= scaleFactor;

        foreach (RetargetSection section in scaled.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
                vertex.Position *= scaleFactor;
        }

        foreach (RetargetBone bone in scaled.Bones)
        {
            bone.LocalTransform = ScaleTranslation(bone.LocalTransform, scaleFactor);
            bone.GlobalTransform = ScaleTranslation(bone.GlobalTransform, scaleFactor);
        }

        scaled.RebuildBoneLookup();
        log?.Invoke($"Applied automatic mesh scale {scaleFactor:0.###}x to imported source mesh.");
        return scaled;
    }

    private static List<float> CollectMappedSegmentRatios(
        RetargetMesh sourceMesh,
        SkeletonDefinition targetSkeleton,
        IReadOnlyDictionary<string, string> boneMapping)
    {
        List<float> ratios = [];
        if (boneMapping == null || boneMapping.Count == 0)
            return ratios;

        foreach (RetargetBone sourceBone in sourceMesh.Bones)
        {
            if (sourceBone.ParentIndex < 0 || sourceBone.ParentIndex >= sourceMesh.Bones.Count)
                continue;

            if (!boneMapping.TryGetValue(sourceBone.Name, out string targetBoneName))
                continue;

            RetargetBone sourceParent = sourceMesh.Bones[sourceBone.ParentIndex];
            if (!boneMapping.TryGetValue(sourceParent.Name, out string targetParentName))
                continue;

            if (!targetSkeleton.BonesByName.TryGetValue(targetBoneName, out RetargetBone targetBone) ||
                !targetSkeleton.BonesByName.TryGetValue(targetParentName, out RetargetBone targetParent))
            {
                continue;
            }

            float sourceLength = GetDistance(sourceParent.GlobalTransform, sourceBone.GlobalTransform);
            float targetLength = GetDistance(targetParent.GlobalTransform, targetBone.GlobalTransform);
            if (sourceLength <= 1e-4f || targetLength <= 1e-4f)
                continue;

            if (IsLikelyHelperBone(sourceBone.Name) || IsLikelyHelperBone(targetBone.Name))
                continue;

            float ratio = targetLength / sourceLength;
            if (float.IsFinite(ratio) && ratio > 0.05f && ratio < 20.0f)
                ratios.Add(ratio);
        }

        return ratios;
    }

    private static float ComputeBoundsRatio(RetargetMesh sourceMesh, SkeletonDefinition targetSkeleton)
    {
        Vector3 sourceMin = new(float.PositiveInfinity);
        Vector3 sourceMax = new(float.NegativeInfinity);
        foreach (RetargetSection section in sourceMesh.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                sourceMin = Vector3.Min(sourceMin, vertex.Position);
                sourceMax = Vector3.Max(sourceMax, vertex.Position);
            }
        }

        Vector3 targetMin = new(float.PositiveInfinity);
        Vector3 targetMax = new(float.NegativeInfinity);
        foreach (RetargetBone bone in targetSkeleton.Bones)
        {
            Vector3 position = ExtractTranslation(bone.GlobalTransform);
            targetMin = Vector3.Min(targetMin, position);
            targetMax = Vector3.Max(targetMax, position);
        }

        Vector3 sourceExtents = sourceMax - sourceMin;
        Vector3 targetExtents = targetMax - targetMin;
        float sourceSize = Math.Max(sourceExtents.X, Math.Max(sourceExtents.Y, sourceExtents.Z));
        float targetSize = Math.Max(targetExtents.X, Math.Max(targetExtents.Y, targetExtents.Z));

        if (sourceSize <= 1e-4f || targetSize <= 1e-4f)
            return 1.0f;

        return targetSize / sourceSize;
    }

    private static float ComputeBoundsRatio(RetargetMesh sourceMesh, RetargetMesh referenceMesh)
    {
        GetMeshBounds(sourceMesh, out Vector3 sourceMin, out Vector3 sourceMax);
        GetMeshBounds(referenceMesh, out Vector3 targetMin, out Vector3 targetMax);

        Vector3 sourceExtents = sourceMax - sourceMin;
        Vector3 targetExtents = targetMax - targetMin;
        float sourceSize = Math.Max(sourceExtents.X, Math.Max(sourceExtents.Y, sourceExtents.Z));
        float targetSize = Math.Max(targetExtents.X, Math.Max(targetExtents.Y, targetExtents.Z));

        if (sourceSize <= 1e-4f || targetSize <= 1e-4f)
            return 1.0f;

        return targetSize / sourceSize;
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

    private static float ComputeMedian(List<float> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        if ((values.Count & 1) == 1)
            return values[middle];

        return (values[middle - 1] + values[middle]) * 0.5f;
    }

    private static float GetDistance(Matrix4x4 a, Matrix4x4 b)
    {
        return Vector3.Distance(ExtractTranslation(a), ExtractTranslation(b));
    }

    private static Vector3 ExtractTranslation(Matrix4x4 value)
    {
        return new Vector3(value.M41, value.M42, value.M43);
    }

    private static Matrix4x4 ScaleTranslation(Matrix4x4 value, float scale)
    {
        value.M41 *= scale;
        value.M42 *= scale;
        value.M43 *= scale;
        return value;
    }

    private static float ClampScale(float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0.0f)
            return 1.0f;

        return Math.Clamp(scale, 0.1f, 10.0f);
    }

    private static bool IsLikelyHelperBone(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        string normalized = name.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        return normalized.Contains("offset", StringComparison.Ordinal) ||
            normalized.Contains("ik", StringComparison.Ordinal) ||
            normalized.Contains("helper", StringComparison.Ordinal) ||
            normalized.Contains("attach", StringComparison.Ordinal) ||
            normalized.Contains("twist", StringComparison.Ordinal) ||
            normalized.Contains("armature", StringComparison.Ordinal);
    }
}
