using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class PoseConformProcessor
{
    private const int PreferredBoneCount = 4;

    public RetargetMesh ConformToReferencePose(
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

        MeshSurfaceKDTree referenceSurface = new(referenceMesh);
        List<RegionAnchor> anchors = RetargetRegions.BuildAnchors(originalSkeleton);
        RetargetMesh conformed = sourceMesh.DeepClone();
        int adjustedVertices = 0;

        foreach (RetargetSection section in conformed.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                RetargetRegion region = RetargetRegions.InferRegion(vertex.Position, anchors);
                IReadOnlyList<string> preferredBones = RetargetRegions.GetPreferredBoneNames(vertex.Position, anchors, PreferredBoneCount);
                if (preferredBones.Count == 0)
                    continue;

                Vector3 anchorCenter = RetargetRegions.GetAnchorCenter(preferredBones, anchors);
                IReadOnlyList<RetargetRegion> allowedRegions = RetargetRegions.GetAllowedRegions(region);
                MeshSurfaceKDTree.TriangleHit hit = ResolveReferenceHit(referenceSurface, vertex.Position, preferredBones, allowedRegions);
                Vector3 sourceOffset = vertex.Position - anchorCenter;
                Vector3 referenceOffset = hit.Projection.ClosestPoint - anchorCenter;
                if (referenceOffset.LengthSquared() <= 1e-6f)
                    continue;

                float positionBlend = GetPositionBlend(region);
                float lengthBlend = GetLengthBlend(region);
                Vector3 alignedDirection = Vector3.Normalize(referenceOffset);
                float sourceLength = sourceOffset.Length();
                float referenceLength = referenceOffset.Length();
                float targetLength = (sourceLength * (1.0f - lengthBlend)) + (referenceLength * lengthBlend);
                Vector3 targetPosition = anchorCenter + (alignedDirection * targetLength);
                Vector3 delta = targetPosition - vertex.Position;
                if (delta.LengthSquared() <= 1e-8f)
                    continue;

                vertex.Position = Vector3.Lerp(vertex.Position, targetPosition, positionBlend);
                vertex.Normal = RotateVectorToward(vertex.Normal, sourceOffset, referenceOffset);
                vertex.Tangent = RotateVectorToward(vertex.Tangent, sourceOffset, referenceOffset);
                vertex.Bitangent = RotateVectorToward(vertex.Bitangent, sourceOffset, referenceOffset);
                adjustedVertices++;
            }
        }

        log?.Invoke($"Pose conform adjusted {adjustedVertices} vertices toward the original MHO bind-pose regions before weight transfer.");
        return conformed;
    }

    private static MeshSurfaceKDTree.TriangleHit ResolveReferenceHit(
        MeshSurfaceKDTree referenceSurface,
        Vector3 position,
        IReadOnlyList<string> preferredBones,
        IReadOnlyList<RetargetRegion> allowedRegions)
    {
        if (referenceSurface.TryFindNearestTriangle(position, preferredBones, allowedRegions, out MeshSurfaceKDTree.TriangleHit combinedHit))
            return combinedHit;

        if (referenceSurface.TryFindNearestTriangle(position, null, allowedRegions, out MeshSurfaceKDTree.TriangleHit regionalHit))
            return regionalHit;

        if (referenceSurface.TryFindNearestTriangle(position, preferredBones, null, out MeshSurfaceKDTree.TriangleHit boneHit))
            return boneHit;

        return referenceSurface.FindNearestTriangle(position);
    }

    private static float GetPositionBlend(RetargetRegion region)
    {
        return region switch
        {
            RetargetRegion.Pelvis => 0.9f,
            RetargetRegion.Spine => 0.88f,
            RetargetRegion.Chest => 0.88f,
            RetargetRegion.LeftShoulder => 0.85f,
            RetargetRegion.RightShoulder => 0.85f,
            RetargetRegion.LeftThigh => 0.82f,
            RetargetRegion.RightThigh => 0.82f,
            RetargetRegion.LeftUpperArm => 0.78f,
            RetargetRegion.RightUpperArm => 0.78f,
            RetargetRegion.LeftCalf => 0.72f,
            RetargetRegion.RightCalf => 0.72f,
            RetargetRegion.LeftForearm => 0.68f,
            RetargetRegion.RightForearm => 0.68f,
            _ => 0.62f
        };
    }

    private static float GetLengthBlend(RetargetRegion region)
    {
        return region switch
        {
            RetargetRegion.Pelvis => 0.55f,
            RetargetRegion.Spine => 0.52f,
            RetargetRegion.Chest => 0.52f,
            RetargetRegion.LeftShoulder => 0.48f,
            RetargetRegion.RightShoulder => 0.48f,
            RetargetRegion.LeftThigh => 0.44f,
            RetargetRegion.RightThigh => 0.44f,
            RetargetRegion.LeftUpperArm => 0.4f,
            RetargetRegion.RightUpperArm => 0.4f,
            _ => 0.32f
        };
    }

    private static Vector3 RotateVectorToward(Vector3 vector, Vector3 fromDirection, Vector3 toDirection)
    {
        if (vector.LengthSquared() <= 1e-8f || fromDirection.LengthSquared() <= 1e-8f || toDirection.LengthSquared() <= 1e-8f)
            return vector;

        Quaternion rotation = FromToRotation(fromDirection, toDirection);
        Vector3 rotated = Vector3.TransformNormal(vector, Matrix4x4.CreateFromQuaternion(rotation));
        return rotated.LengthSquared() > 1e-8f ? Vector3.Normalize(rotated) : vector;
    }

    private static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        Vector3 source = Vector3.Normalize(from);
        Vector3 target = Vector3.Normalize(to);
        float dot = Math.Clamp(Vector3.Dot(source, target), -1.0f, 1.0f);

        if (dot > 0.9999f)
            return Quaternion.Identity;

        if (dot < -0.9999f)
        {
            Vector3 axis = MathF.Abs(source.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
            axis = Vector3.Normalize(Vector3.Cross(source, axis));
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }

        Vector3 cross = Vector3.Cross(source, target);
        float s = MathF.Sqrt((1.0f + dot) * 2.0f);
        float invS = 1.0f / s;
        return Quaternion.Normalize(new Quaternion(cross * invS, s * 0.5f));
    }
}
