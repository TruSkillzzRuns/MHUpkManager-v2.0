using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class OrientationProcessor
{
    private const float RadToDeg = 180.0f / MathF.PI;

    public Quaternion ComputeAlignmentRotation(
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

        if (!TryBuildBasis(sourceMesh.Bones, sourceBone => ResolveSemanticName(sourceBone.Name, boneMapping), out SkeletonBasis sourceBasis))
            throw new InvalidOperationException("Could not derive a stable orientation basis from the imported source skeleton.");

        if (!TryBuildBasis(targetSkeleton.Bones, targetBone => targetBone.Name, out SkeletonBasis targetBasis))
            throw new InvalidOperationException("Could not derive a stable orientation basis from the target skeleton.");

        Quaternion alignUp = FromToRotation(sourceBasis.Up, targetBasis.Up);
        Vector3 alignedForward = Vector3.Normalize(Vector3.TransformNormal(sourceBasis.Forward, Matrix4x4.CreateFromQuaternion(alignUp)));
        Vector3 planarSourceForward = NormalizeOnPlane(alignedForward, targetBasis.Up);
        Vector3 planarTargetForward = NormalizeOnPlane(targetBasis.Forward, targetBasis.Up);

        Quaternion alignForward = Quaternion.Identity;
        if (planarSourceForward.LengthSquared() > 1e-6f && planarTargetForward.LengthSquared() > 1e-6f)
        {
            float signedAngle = SignedAngle(planarSourceForward, planarTargetForward, targetBasis.Up);
            alignForward = Quaternion.CreateFromAxisAngle(targetBasis.Up, signedAngle);
        }

        Quaternion rotation = Quaternion.Normalize(alignForward * alignUp);
        Vector3 rotatedSourceForward = Vector3.Normalize(Vector3.TransformNormal(sourceBasis.Forward, Matrix4x4.CreateFromQuaternion(rotation)));
        float finalDeltaDegrees = MathF.Abs(MathF.Round(RadToDeg * SignedAngle(
            NormalizeOnPlane(rotatedSourceForward, targetBasis.Up),
            planarTargetForward,
            targetBasis.Up)));

        log?.Invoke($"Auto orientation aligned source skeleton to target skeleton. Residual facing delta: {finalDeltaDegrees:0.#} degrees.");
        return rotation;
    }

    public Quaternion ComputeGeometryAlignmentRotation(
        RetargetMesh sourceMesh,
        RetargetMesh referenceMesh,
        SkeletonDefinition targetSkeleton,
        Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));
        if (referenceMesh == null)
            throw new ArgumentNullException(nameof(referenceMesh));
        if (targetSkeleton == null)
            throw new ArgumentNullException(nameof(targetSkeleton));

        targetSkeleton.RebuildBoneLookup();
        if (!TryBuildBasis(targetSkeleton.Bones, targetBone => targetBone.Name, out SkeletonBasis targetBasis))
            throw new InvalidOperationException("Could not derive a stable orientation basis from the target skeleton.");

        MeshAxisMetrics referenceMetrics = ComputeMetrics(referenceMesh, targetBasis);
        Quaternion bestRotation = Quaternion.Identity;
        float bestScore = float.MaxValue;
        Vector3 bestForward = targetBasis.Forward;

        foreach ((Vector3 sourceRight, Vector3 sourceUp) in EnumerateCandidateAxes())
        {
            Vector3 sourceForward = Vector3.Normalize(Vector3.Cross(sourceRight, sourceUp));
            Quaternion alignUp = FromToRotation(sourceUp, targetBasis.Up);
            Vector3 alignedForward = NormalizeOrUnitY(Vector3.Transform(sourceForward, alignUp));
            Vector3 planarSourceForward = NormalizeOnPlane(alignedForward, targetBasis.Up);
            Vector3 planarTargetForward = NormalizeOnPlane(targetBasis.Forward, targetBasis.Up);

            Quaternion alignForward = Quaternion.Identity;
            if (planarSourceForward.LengthSquared() > 1e-6f && planarTargetForward.LengthSquared() > 1e-6f)
            {
                float signedAngle = SignedAngle(planarSourceForward, planarTargetForward, targetBasis.Up);
                alignForward = Quaternion.CreateFromAxisAngle(targetBasis.Up, signedAngle);
            }

            Quaternion candidate = Quaternion.Normalize(alignForward * alignUp);
            MeshAxisMetrics candidateMetrics = ComputeMetrics(sourceMesh, targetBasis, candidate);
            float score = ScoreMetrics(candidateMetrics, referenceMetrics);
            if (score < bestScore)
            {
                bestScore = score;
                bestRotation = candidate;
                bestForward = NormalizeOrUnitY(Vector3.Transform(sourceForward, candidate));
            }
        }

        foreach (Quaternion refinement in EnumerateQuarterTurnRefinements(targetBasis))
        {
            Quaternion candidate = Quaternion.Normalize(refinement * bestRotation);
            MeshAxisMetrics candidateMetrics = ComputeMetrics(sourceMesh, targetBasis, candidate);
            float score = ScoreMetrics(candidateMetrics, referenceMetrics);
            if (score < bestScore)
            {
                bestScore = score;
                bestRotation = candidate;
                bestForward = NormalizeOrUnitY(Vector3.Transform(bestForward, refinement));
            }
        }

        Vector3 rotatedForward = bestForward;
        float residualDeltaDegrees = MathF.Abs(MathF.Round(RadToDeg * SignedAngle(
            NormalizeOnPlane(rotatedForward, targetBasis.Up),
            NormalizeOnPlane(targetBasis.Forward, targetBasis.Up),
            targetBasis.Up)));

        log?.Invoke($"Auto orientation aligned unrigged mesh to target skeleton/reference basis. Heuristic score: {bestScore:0.###}. Residual facing delta: {residualDeltaDegrees:0.#} degrees.");
        return bestRotation;
    }

    public RetargetMesh ApplyRotation(RetargetMesh sourceMesh, Quaternion rotation, Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));

        if (!IsValid(rotation))
            throw new InvalidOperationException("The calculated orientation rotation was not valid.");

        RetargetMesh rotated = sourceMesh.DeepClone();
        Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);
        float appliedDegrees = RadToDeg * (2.0f * MathF.Acos(Math.Clamp(rotation.W, -1.0f, 1.0f)));
        if (appliedDegrees > 180.0f)
            appliedDegrees = 360.0f - appliedDegrees;

        foreach (RetargetSection section in rotated.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                vertex.Position = Vector3.Transform(vertex.Position, rotationMatrix);
                vertex.Normal = NormalizeOrUnitY(Vector3.TransformNormal(vertex.Normal, rotationMatrix));
                vertex.Tangent = NormalizeOrUnitY(Vector3.TransformNormal(vertex.Tangent, rotationMatrix));
                vertex.Bitangent = NormalizeOrUnitY(Vector3.TransformNormal(vertex.Bitangent, rotationMatrix));
            }
        }

        List<Matrix4x4> rotatedGlobals = new(rotated.Bones.Count);
        foreach (RetargetBone bone in rotated.Bones)
            rotatedGlobals.Add(RotateTransform(bone.GlobalTransform, rotation));

        for (int boneIndex = 0; boneIndex < rotated.Bones.Count; boneIndex++)
        {
            RetargetBone bone = rotated.Bones[boneIndex];
            Matrix4x4 global = rotatedGlobals[boneIndex];
            bone.GlobalTransform = global;

            if (bone.ParentIndex >= 0 && bone.ParentIndex < rotatedGlobals.Count && Matrix4x4.Invert(rotatedGlobals[bone.ParentIndex], out Matrix4x4 parentInverse))
                bone.LocalTransform = global * parentInverse;
            else
                bone.LocalTransform = global;
        }

        rotated.RebuildBoneLookup();
        log?.Invoke($"Applied automatic orientation adjustment ({appliedDegrees:0.#} degrees).");
        return rotated;
    }

    public Vector3 GetTargetUpAxis(SkeletonDefinition targetSkeleton)
    {
        return GetTargetAxis(targetSkeleton, "up");
    }

    public Vector3 GetTargetAxis(SkeletonDefinition targetSkeleton, string axisName)
    {
        if (targetSkeleton == null)
            throw new ArgumentNullException(nameof(targetSkeleton));

        targetSkeleton.RebuildBoneLookup();
        if (!TryBuildBasis(targetSkeleton.Bones, targetBone => targetBone.Name, out SkeletonBasis basis))
            throw new InvalidOperationException("Could not derive a stable orientation basis from the target skeleton.");

        return axisName?.ToLowerInvariant() switch
        {
            "right" => basis.Right,
            "forward" => basis.Forward,
            _ => basis.Up
        };
    }

    private static string ResolveSemanticName(string sourceBoneName, IReadOnlyDictionary<string, string> boneMapping)
    {
        if (boneMapping != null && boneMapping.TryGetValue(sourceBoneName, out string mapped) && !string.IsNullOrWhiteSpace(mapped))
            return mapped;

        return sourceBoneName;
    }

    private static bool TryBuildBasis(
        IReadOnlyList<RetargetBone> bones,
        Func<RetargetBone, string> semanticNameProvider,
        out SkeletonBasis basis)
    {
        basis = default;
        if (bones == null || bones.Count == 0)
            return false;

        List<Vector3> centerAnchors = [];
        List<Vector3> upperAnchors = [];
        List<Vector3> leftAnchors = [];
        List<Vector3> rightAnchors = [];

        foreach (RetargetBone bone in bones)
        {
            string semanticName = BoneMapper.NormalizeBoneName(semanticNameProvider(bone));
            Vector3 position = ExtractTranslation(bone.GlobalTransform);

            if (IsCenterBase(semanticName))
                centerAnchors.Add(position);
            if (IsUpperAnchor(semanticName))
                upperAnchors.Add(position);
            if (IsLeftAnchor(semanticName))
                leftAnchors.Add(position);
            if (IsRightAnchor(semanticName))
                rightAnchors.Add(position);
        }

        if (centerAnchors.Count == 0 || upperAnchors.Count == 0 || leftAnchors.Count == 0 || rightAnchors.Count == 0)
            return false;

        Vector3 center = Average(centerAnchors);
        Vector3 upper = Average(upperAnchors);
        Vector3 left = Average(leftAnchors);
        Vector3 right = Average(rightAnchors);

        Vector3 up = NormalizeOrUnitY(upper - center);
        Vector3 rightAxis = right - left;
        rightAxis -= up * Vector3.Dot(rightAxis, up);
        if (rightAxis.LengthSquared() <= 1e-6f)
            return false;

        rightAxis = Vector3.Normalize(rightAxis);
        Vector3 forward = Vector3.Cross(rightAxis, up);
        if (forward.LengthSquared() <= 1e-6f)
            return false;

        forward = Vector3.Normalize(forward);
        rightAxis = Vector3.Normalize(Vector3.Cross(up, forward));

        basis = new SkeletonBasis(center, forward, up, rightAxis);
        return true;
    }

    private static IEnumerable<(Vector3 Right, Vector3 Up)> EnumerateCandidateAxes()
    {
        Vector3[] axes =
        [
            Vector3.UnitX,
            -Vector3.UnitX,
            Vector3.UnitY,
            -Vector3.UnitY,
            Vector3.UnitZ,
            -Vector3.UnitZ
        ];

        foreach (Vector3 up in axes)
        {
            foreach (Vector3 right in axes)
            {
                if (MathF.Abs(Vector3.Dot(up, right)) > 1e-5f)
                    continue;

                yield return (right, up);
            }
        }
    }

    private static IEnumerable<Quaternion> EnumerateQuarterTurnRefinements(SkeletonBasis basis)
    {
        yield return Quaternion.Identity;

        float[] quarterAngles = [MathF.PI * 0.5f, -MathF.PI * 0.5f, MathF.PI];
        foreach (float angle in quarterAngles)
            yield return Quaternion.CreateFromAxisAngle(basis.Right, angle);

        foreach (float angle in quarterAngles)
            yield return Quaternion.CreateFromAxisAngle(basis.Forward, angle);

        foreach (float angle in quarterAngles)
            yield return Quaternion.CreateFromAxisAngle(basis.Up, angle);
    }

    private static MeshAxisMetrics ComputeMetrics(RetargetMesh mesh, SkeletonBasis basis, Quaternion? rotation = null)
    {
        Vector3 right = basis.Right;
        Vector3 up = basis.Up;
        Vector3 forward = basis.Forward;
        Quaternion appliedRotation = rotation ?? Quaternion.Identity;

        float minRight = float.PositiveInfinity;
        float maxRight = float.NegativeInfinity;
        float minUp = float.PositiveInfinity;
        float maxUp = float.NegativeInfinity;
        float minForward = float.PositiveInfinity;
        float maxForward = float.NegativeInfinity;
        Vector3 centroid = Vector3.Zero;
        List<Vector3> projectedPositions = [];
        int count = 0;

        foreach (RetargetSection section in mesh.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                Vector3 position = rotation.HasValue
                    ? Vector3.Transform(vertex.Position, appliedRotation)
                    : vertex.Position;
                centroid += position;
                count++;

                float rightProjection = Vector3.Dot(position, right);
                float upProjection = Vector3.Dot(position, up);
                float forwardProjection = Vector3.Dot(position, forward);
                projectedPositions.Add(new Vector3(rightProjection, upProjection, forwardProjection));

                minRight = MathF.Min(minRight, rightProjection);
                maxRight = MathF.Max(maxRight, rightProjection);
                minUp = MathF.Min(minUp, upProjection);
                maxUp = MathF.Max(maxUp, upProjection);
                minForward = MathF.Min(minForward, forwardProjection);
                maxForward = MathF.Max(maxForward, forwardProjection);
            }
        }

        if (count == 0)
            return default;

        centroid /= count;
        Vector3 extents = new(maxRight - minRight, maxUp - minUp, maxForward - minForward);
        float maxExtent = MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
        Vector3 normalizedExtents = maxExtent > 1e-6f ? extents / maxExtent : Vector3.One;
        Vector3 centroidProjected = new(Vector3.Dot(centroid, right), Vector3.Dot(centroid, up), Vector3.Dot(centroid, forward));
        Vector3 boundsCenter = new((minRight + maxRight) * 0.5f, (minUp + maxUp) * 0.5f, (minForward + maxForward) * 0.5f);
        Vector3 skew = new(
            NormalizeSkew(centroidProjected.X - boundsCenter.X, extents.X),
            NormalizeSkew(centroidProjected.Y - boundsCenter.Y, extents.Y),
            NormalizeSkew(centroidProjected.Z - boundsCenter.Z, extents.Z));
        float topBandStart = minUp + (extents.Y * 0.75f);
        float bottomBandEnd = minUp + (extents.Y * 0.25f);
        (float topRightSpan, float topForwardSpan) = ComputeBandSpans(projectedPositions, topBandStart, float.PositiveInfinity);
        (float bottomRightSpan, float bottomForwardSpan) = ComputeBandSpans(projectedPositions, float.NegativeInfinity, bottomBandEnd);
        float normalizedTopRightSpan = maxExtent > 1e-6f ? topRightSpan / maxExtent : 0.0f;
        float normalizedTopForwardSpan = maxExtent > 1e-6f ? topForwardSpan / maxExtent : 0.0f;
        float normalizedBottomRightSpan = maxExtent > 1e-6f ? bottomRightSpan / maxExtent : 0.0f;
        float normalizedBottomForwardSpan = maxExtent > 1e-6f ? bottomForwardSpan / maxExtent : 0.0f;

        return new MeshAxisMetrics(
            normalizedExtents,
            skew,
            normalizedTopRightSpan,
            normalizedTopForwardSpan,
            normalizedBottomRightSpan,
            normalizedBottomForwardSpan);
    }

    private static float ScoreMetrics(MeshAxisMetrics candidate, MeshAxisMetrics reference)
    {
        Vector3 extentDelta = candidate.NormalizedExtents - reference.NormalizedExtents;
        Vector3 skewDelta = candidate.NormalizedSkew - reference.NormalizedSkew;
        float score = extentDelta.LengthSquared() + (skewDelta.LengthSquared() * 0.35f);
        float bandSpanDelta =
            MathF.Abs(candidate.NormalizedTopRightSpan - reference.NormalizedTopRightSpan) +
            MathF.Abs(candidate.NormalizedTopForwardSpan - reference.NormalizedTopForwardSpan) +
            MathF.Abs(candidate.NormalizedBottomRightSpan - reference.NormalizedBottomRightSpan) +
            MathF.Abs(candidate.NormalizedBottomForwardSpan - reference.NormalizedBottomForwardSpan);
        score += bandSpanDelta * 3.0f;

        // Humanoid meshes should be tallest along the target up axis after orientation.
        float tallestNonUp = MathF.Max(candidate.NormalizedExtents.X, candidate.NormalizedExtents.Z);
        if (candidate.NormalizedExtents.Y < tallestNonUp)
        {
            float upDeficit = tallestNonUp - candidate.NormalizedExtents.Y;
            score += 10.0f + (upDeficit * upDeficit * 20.0f);
        }

        return score;
    }

    private static (float RightSpan, float ForwardSpan) ComputeBandSpans(
        IReadOnlyList<Vector3> projectedPositions,
        float minUpInclusive,
        float maxUpInclusive)
    {
        float minRight = float.PositiveInfinity;
        float maxRight = float.NegativeInfinity;
        float minForward = float.PositiveInfinity;
        float maxForward = float.NegativeInfinity;
        int count = 0;

        foreach (Vector3 projected in projectedPositions)
        {
            if (projected.Y < minUpInclusive || projected.Y > maxUpInclusive)
                continue;

            minRight = MathF.Min(minRight, projected.X);
            maxRight = MathF.Max(maxRight, projected.X);
            minForward = MathF.Min(minForward, projected.Z);
            maxForward = MathF.Max(maxForward, projected.Z);
            count++;
        }

        if (count == 0)
            return (0.0f, 0.0f);

        return (maxRight - minRight, maxForward - minForward);
    }

    private static float NormalizeSkew(float value, float extent)
    {
        if (extent <= 1e-6f)
            return 0.0f;

        return Math.Clamp(value / extent, -1.0f, 1.0f);
    }

    private static bool IsCenterBase(string normalizedName)
    {
        return normalizedName.Contains("root", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("pelvis", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("hip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUpperAnchor(string normalizedName)
    {
        return normalizedName.Contains("head", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("neck", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("spine", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("chest", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("torso", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLeftAnchor(string normalizedName)
    {
        return IsLeft(normalizedName) && (
            normalizedName.Contains("shoulder", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("clav", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("upperarm", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("arm", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("hand", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("thigh", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("upleg", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("leg", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("foot", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRightAnchor(string normalizedName)
    {
        return IsRight(normalizedName) && (
            normalizedName.Contains("shoulder", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("clav", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("upperarm", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("arm", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("hand", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("thigh", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("upleg", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("leg", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("foot", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLeft(string normalizedName)
    {
        return normalizedName.StartsWith("l", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("left", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRight(string normalizedName)
    {
        return normalizedName.StartsWith("r", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Contains("right", StringComparison.OrdinalIgnoreCase);
    }

    private static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        Vector3 source = NormalizeOrUnitY(from);
        Vector3 target = NormalizeOrUnitY(to);
        float dot = Math.Clamp(Vector3.Dot(source, target), -1.0f, 1.0f);

        if (dot > 0.9999f)
            return Quaternion.Identity;

        if (dot < -0.9999f)
        {
            Vector3 axis = MathF.Abs(source.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
            axis = NormalizeOrUnitY(Vector3.Cross(source, axis));
            return Quaternion.CreateFromAxisAngle(axis, MathF.PI);
        }

        Vector3 cross = Vector3.Cross(source, target);
        float s = MathF.Sqrt((1.0f + dot) * 2.0f);
        float invS = 1.0f / s;
        return Quaternion.Normalize(new Quaternion(cross * invS, s * 0.5f));
    }

    private static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
    {
        Vector3 cross = Vector3.Cross(from, to);
        float dot = Math.Clamp(Vector3.Dot(from, to), -1.0f, 1.0f);
        float angle = MathF.Atan2(cross.Length(), dot);
        float sign = MathF.Sign(Vector3.Dot(cross, axis));
        return angle * (sign == 0.0f ? 1.0f : sign);
    }

    private static Vector3 NormalizeOnPlane(Vector3 value, Vector3 planeNormal)
    {
        Vector3 projected = value - (planeNormal * Vector3.Dot(value, planeNormal));
        return projected.LengthSquared() > 1e-6f ? Vector3.Normalize(projected) : Vector3.Zero;
    }

    private static Matrix4x4 RotateTransform(Matrix4x4 transform, Quaternion rotation)
    {
        if (!Matrix4x4.Decompose(transform, out Vector3 scale, out Quaternion orientation, out Vector3 translation))
            return transform;

        Quaternion rotatedOrientation = Quaternion.Normalize(rotation * orientation);
        Vector3 rotatedTranslation = Vector3.Transform(translation, rotation);
        return Matrix4x4.CreateScale(scale) *
            Matrix4x4.CreateFromQuaternion(rotatedOrientation) *
            Matrix4x4.CreateTranslation(rotatedTranslation);
    }

    private static Vector3 ExtractTranslation(Matrix4x4 value)
    {
        return new Vector3(value.M41, value.M42, value.M43);
    }

    private static Vector3 Average(IReadOnlyList<Vector3> values)
    {
        Vector3 sum = Vector3.Zero;
        foreach (Vector3 value in values)
            sum += value;
        return sum / values.Count;
    }

    private static Vector3 NormalizeOrUnitY(Vector3 value)
    {
        return value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : Vector3.UnitY;
    }

    private static bool IsValid(Quaternion rotation)
    {
        return float.IsFinite(rotation.X) &&
            float.IsFinite(rotation.Y) &&
            float.IsFinite(rotation.Z) &&
            float.IsFinite(rotation.W) &&
            rotation.LengthSquared() > 1e-6f;
    }

    private readonly record struct SkeletonBasis(Vector3 Center, Vector3 Forward, Vector3 Up, Vector3 Right);
    private readonly record struct MeshAxisMetrics(
        Vector3 NormalizedExtents,
        Vector3 NormalizedSkew,
        float NormalizedTopRightSpan,
        float NormalizedTopForwardSpan,
        float NormalizedBottomRightSpan,
        float NormalizedBottomForwardSpan);
}
