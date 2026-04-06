using System.Numerics;

namespace MHUpkManager.Retargeting;

internal enum RetargetRegion
{
    Unknown,
    Root,
    Pelvis,
    Spine,
    Chest,
    Neck,
    Head,
    LeftShoulder,
    RightShoulder,
    LeftUpperArm,
    RightUpperArm,
    LeftForearm,
    RightForearm,
    LeftHand,
    RightHand,
    LeftThigh,
    RightThigh,
    LeftCalf,
    RightCalf,
    LeftFoot,
    RightFoot
}

internal readonly record struct RegionAnchor(string Name, RetargetRegion Region, Vector3 Position);

internal static class RetargetRegions
{
    public static List<RegionAnchor> BuildAnchors(IReadOnlyList<RetargetBone> bones)
    {
        List<RegionAnchor> anchors = [];
        foreach (RetargetBone bone in bones)
        {
            if (string.IsNullOrWhiteSpace(bone.Name) || IsLikelyHelperBone(bone.Name))
                continue;

            anchors.Add(new RegionAnchor(
                bone.Name,
                ClassifyBone(bone.Name),
                new Vector3(bone.GlobalTransform.M41, bone.GlobalTransform.M42, bone.GlobalTransform.M43)));
        }

        return anchors;
    }

    public static RetargetRegion InferRegion(Vector3 position, IReadOnlyList<RegionAnchor> anchors)
    {
        if (anchors == null || anchors.Count == 0)
            return RetargetRegion.Unknown;

        if (TryInferRegionByBodyBands(position, anchors, out RetargetRegion bandRegion))
            return bandRegion;

        float centerLateral = GetBodyCenterLateral(anchors);
        int preferredSide = GetPreferredSide(position, centerLateral);

        RegionAnchor? best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (RegionAnchor anchor in anchors)
        {
            if (!MatchesPreferredSide(anchor.Region, preferredSide))
                continue;

            float distance = Vector3.DistanceSquared(position, anchor.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = anchor;
            }
        }

        if (best == null)
        {
            foreach (RegionAnchor anchor in anchors)
            {
                float distance = Vector3.DistanceSquared(position, anchor.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = anchor;
                }
            }
        }

        return best?.Region ?? RetargetRegion.Unknown;
    }

    public static IReadOnlyList<string> GetPreferredBoneNames(Vector3 position, IReadOnlyList<RegionAnchor> anchors, int count)
    {
        RetargetRegion region = InferRegion(position, anchors);
        IEnumerable<RegionAnchor> candidates = anchors;
        if (region != RetargetRegion.Unknown)
        {
            List<RetargetRegion> allowed = GetAllowedRegions(region);
            IEnumerable<RegionAnchor> regional = anchors.Where(anchor => allowed.Contains(anchor.Region));
            if (regional.Any())
                candidates = regional;
        }

        float centerLateral = GetBodyCenterLateral(anchors);
        int preferredSide = GetPreferredSide(position, centerLateral);
        List<RegionAnchor> sameSideCandidates = [.. candidates.Where(anchor => MatchesPreferredSide(anchor.Region, preferredSide))];
        if (sameSideCandidates.Count > 0)
            candidates = sameSideCandidates;

        return [.. candidates
            .OrderBy(anchor => Vector3.DistanceSquared(position, anchor.Position))
            .Take(count)
            .Select(static anchor => anchor.Name)];
    }

    public static Vector3 GetAnchorCenter(IReadOnlyList<string> preferredBones, IReadOnlyList<RegionAnchor> anchors)
    {
        Vector3 sum = Vector3.Zero;
        int count = 0;
        foreach (string preferredBone in preferredBones)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (!string.Equals(anchors[i].Name, preferredBone, StringComparison.OrdinalIgnoreCase))
                    continue;

                sum += anchors[i].Position;
                count++;
                break;
            }
        }

        return count > 0 ? sum / count : Vector3.Zero;
    }

    public static RetargetRegion ClassifyBone(string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
            return RetargetRegion.Unknown;

        string normalized = Normalize(boneName);
        bool left = IsLeft(normalized);
        bool right = IsRight(normalized);

        if (normalized.Contains("head", StringComparison.Ordinal))
            return RetargetRegion.Head;
        if (normalized.Contains("neck", StringComparison.Ordinal))
            return RetargetRegion.Neck;
        if (normalized.Contains("chest", StringComparison.Ordinal) || normalized.Contains("clav", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftShoulder : right ? RetargetRegion.RightShoulder : RetargetRegion.Chest;
        if (normalized.Contains("spine", StringComparison.Ordinal) || normalized.Contains("torso", StringComparison.Ordinal))
            return RetargetRegion.Spine;
        if (normalized.Contains("root", StringComparison.Ordinal))
            return RetargetRegion.Root;
        if (normalized.Contains("pelvis", StringComparison.Ordinal))
            return RetargetRegion.Pelvis;
        if (normalized.Contains("hip", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftThigh : right ? RetargetRegion.RightThigh : RetargetRegion.Pelvis;
        if (normalized.Contains("shoulder", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftShoulder : right ? RetargetRegion.RightShoulder : RetargetRegion.Chest;
        if (normalized.Contains("upperarm", StringComparison.Ordinal) || (normalized.Contains("arm", StringComparison.Ordinal) && !normalized.Contains("forearm", StringComparison.Ordinal)))
            return left ? RetargetRegion.LeftUpperArm : right ? RetargetRegion.RightUpperArm : RetargetRegion.Unknown;
        if (normalized.Contains("forearm", StringComparison.Ordinal) || normalized.Contains("lowerarm", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftForearm : right ? RetargetRegion.RightForearm : RetargetRegion.Unknown;
        if (normalized.Contains("hand", StringComparison.Ordinal) || normalized.Contains("wrist", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftHand : right ? RetargetRegion.RightHand : RetargetRegion.Unknown;
        if (normalized.Contains("thigh", StringComparison.Ordinal) || normalized.Contains("upleg", StringComparison.Ordinal) || normalized.Contains("upperleg", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftThigh : right ? RetargetRegion.RightThigh : RetargetRegion.Unknown;
        if (normalized.Contains("calf", StringComparison.Ordinal) || normalized.Contains("leg", StringComparison.Ordinal) || normalized.Contains("knee", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftCalf : right ? RetargetRegion.RightCalf : RetargetRegion.Unknown;
        if (normalized.Contains("foot", StringComparison.Ordinal) || normalized.Contains("ankle", StringComparison.Ordinal) || normalized.Contains("toe", StringComparison.Ordinal))
            return left ? RetargetRegion.LeftFoot : right ? RetargetRegion.RightFoot : RetargetRegion.Unknown;

        return RetargetRegion.Unknown;
    }

    public static List<RetargetRegion> GetAllowedRegions(RetargetRegion region)
    {
        return region switch
        {
            RetargetRegion.Root => [RetargetRegion.Root, RetargetRegion.Pelvis],
            RetargetRegion.Pelvis => [RetargetRegion.Pelvis, RetargetRegion.Root, RetargetRegion.Spine],
            RetargetRegion.Spine => [RetargetRegion.Spine, RetargetRegion.Pelvis, RetargetRegion.Chest],
            RetargetRegion.Chest => [RetargetRegion.Chest, RetargetRegion.Spine, RetargetRegion.Neck],
            RetargetRegion.Neck => [RetargetRegion.Neck, RetargetRegion.Chest, RetargetRegion.Head],
            RetargetRegion.Head => [RetargetRegion.Head, RetargetRegion.Neck],
            RetargetRegion.LeftShoulder => [RetargetRegion.LeftShoulder, RetargetRegion.LeftUpperArm, RetargetRegion.Chest],
            RetargetRegion.RightShoulder => [RetargetRegion.RightShoulder, RetargetRegion.RightUpperArm, RetargetRegion.Chest],
            RetargetRegion.LeftUpperArm => [RetargetRegion.LeftUpperArm, RetargetRegion.LeftShoulder, RetargetRegion.LeftForearm],
            RetargetRegion.RightUpperArm => [RetargetRegion.RightUpperArm, RetargetRegion.RightShoulder, RetargetRegion.RightForearm],
            RetargetRegion.LeftForearm => [RetargetRegion.LeftForearm, RetargetRegion.LeftUpperArm, RetargetRegion.LeftHand],
            RetargetRegion.RightForearm => [RetargetRegion.RightForearm, RetargetRegion.RightUpperArm, RetargetRegion.RightHand],
            RetargetRegion.LeftHand => [RetargetRegion.LeftForearm, RetargetRegion.LeftHand],
            RetargetRegion.RightHand => [RetargetRegion.RightForearm, RetargetRegion.RightHand],
            RetargetRegion.LeftThigh => [RetargetRegion.LeftThigh, RetargetRegion.Pelvis, RetargetRegion.LeftCalf],
            RetargetRegion.RightThigh => [RetargetRegion.RightThigh, RetargetRegion.Pelvis, RetargetRegion.RightCalf],
            RetargetRegion.LeftCalf => [RetargetRegion.LeftCalf, RetargetRegion.LeftThigh, RetargetRegion.LeftFoot],
            RetargetRegion.RightCalf => [RetargetRegion.RightCalf, RetargetRegion.RightThigh, RetargetRegion.RightFoot],
            RetargetRegion.LeftFoot => [RetargetRegion.LeftFoot, RetargetRegion.LeftCalf],
            RetargetRegion.RightFoot => [RetargetRegion.RightFoot, RetargetRegion.RightCalf],
            _ => [RetargetRegion.Unknown]
        };
    }

    public static IReadOnlyList<string> GetRegionBoneNames(RetargetRegion region)
    {
        return region switch
        {
            RetargetRegion.Root => ["root"],
            RetargetRegion.Pelvis => ["g_pelvis", "g_spine01"],
            RetargetRegion.Spine => ["g_spine01", "g_spine02"],
            RetargetRegion.Chest => ["g_spine03", "g_chest"],
            RetargetRegion.Neck => ["g_neck"],
            RetargetRegion.Head => ["g_head"],
            RetargetRegion.LeftShoulder => ["g_l_clavical", "g_l_shoulder"],
            RetargetRegion.RightShoulder => ["g_r_clavical", "g_r_shoulder"],
            RetargetRegion.LeftUpperArm => ["g_l_shoulder", "g_l_elbow"],
            RetargetRegion.RightUpperArm => ["g_r_shoulder", "g_r_elbow"],
            RetargetRegion.LeftForearm => ["g_l_elbow", "g_l_wrist"],
            RetargetRegion.RightForearm => ["g_r_elbow", "g_r_wrist"],
            RetargetRegion.LeftHand => ["g_l_wrist", "g_l_hand"],
            RetargetRegion.RightHand => ["g_r_wrist", "g_r_hand"],
            RetargetRegion.LeftThigh => ["g_l_hip", "g_l_knee"],
            RetargetRegion.RightThigh => ["g_r_hip", "g_r_knee"],
            RetargetRegion.LeftCalf => ["g_l_knee", "g_l_ankle"],
            RetargetRegion.RightCalf => ["g_r_knee", "g_r_ankle"],
            RetargetRegion.LeftFoot => ["g_l_ankle", "g_l_ball"],
            RetargetRegion.RightFoot => ["g_r_ankle", "g_r_ball"],
            _ => Array.Empty<string>()
        };
    }

    private static float GetBodyCenterLateral(IReadOnlyList<RegionAnchor> anchors)
    {
        List<RegionAnchor> centerAnchors = [.. anchors.Where(anchor => anchor.Region is
            RetargetRegion.Root or
            RetargetRegion.Pelvis or
            RetargetRegion.Spine or
            RetargetRegion.Chest or
            RetargetRegion.Neck or
            RetargetRegion.Head)];

        if (centerAnchors.Count == 0)
            return 0.0f;

        return centerAnchors.Average(static anchor => anchor.Position.Z);
    }

    private static bool TryInferRegionByBodyBands(Vector3 position, IReadOnlyList<RegionAnchor> anchors, out RetargetRegion region)
    {
        region = RetargetRegion.Unknown;

        List<RegionAnchor> coreAnchors = [.. anchors.Where(anchor => anchor.Region is
            RetargetRegion.Root or
            RetargetRegion.Pelvis or
            RetargetRegion.Spine or
            RetargetRegion.Chest or
            RetargetRegion.Neck or
            RetargetRegion.Head)];

        if (coreAnchors.Count < 3)
            return false;

        float minY = coreAnchors.Min(static anchor => anchor.Position.Y);
        float maxY = coreAnchors.Max(static anchor => anchor.Position.Y);
        float height = MathF.Max(1e-3f, maxY - minY);
        float normalizedY = Math.Clamp((position.Y - minY) / height, 0.0f, 1.0f);

        float centerLateral = GetBodyCenterLateral(anchors);
        float lateralDelta = position.Z - centerLateral;

        float leftHandZ = GetAnchorCenter(GetRegionBoneNames(RetargetRegion.LeftHand), anchors).Z;
        float rightHandZ = GetAnchorCenter(GetRegionBoneNames(RetargetRegion.RightHand), anchors).Z;
        float armReach = MathF.Max(MathF.Abs(leftHandZ - centerLateral), MathF.Abs(rightHandZ - centerLateral));
        float armThreshold = MathF.Max(4.0f, armReach * 0.35f);

        float leftFootZ = GetAnchorCenter(GetRegionBoneNames(RetargetRegion.LeftFoot), anchors).Z;
        float rightFootZ = GetAnchorCenter(GetRegionBoneNames(RetargetRegion.RightFoot), anchors).Z;
        float legReach = MathF.Max(MathF.Abs(leftFootZ - centerLateral), MathF.Abs(rightFootZ - centerLateral));
        float legThreshold = MathF.Max(3.0f, legReach * 0.45f);

        bool leftSide = lateralDelta < 0.0f;
        bool rightSide = lateralDelta > 0.0f;
        float absLateral = MathF.Abs(lateralDelta);

        if (normalizedY >= 0.93f)
            region = RetargetRegion.Head;
        else if (normalizedY >= 0.84f)
            region = RetargetRegion.Neck;
        else if (normalizedY >= 0.66f && absLateral <= armThreshold * 0.7f)
            region = RetargetRegion.Chest;
        else if (normalizedY >= 0.50f && absLateral <= armThreshold * 0.6f)
            region = RetargetRegion.Spine;
        else if (normalizedY >= 0.34f && absLateral <= legThreshold * 0.75f)
            region = RetargetRegion.Pelvis;
        else if (normalizedY >= 0.58f && absLateral >= armThreshold)
            region = normalizedY >= 0.70f
                ? leftSide ? RetargetRegion.LeftUpperArm : rightSide ? RetargetRegion.RightUpperArm : RetargetRegion.Chest
                : leftSide ? RetargetRegion.LeftForearm : rightSide ? RetargetRegion.RightForearm : RetargetRegion.Spine;
        else if (normalizedY >= 0.50f && absLateral >= armThreshold * 1.25f)
            region = leftSide ? RetargetRegion.LeftHand : rightSide ? RetargetRegion.RightHand : RetargetRegion.Unknown;
        else if (normalizedY <= 0.34f && absLateral >= legThreshold * 0.45f)
            region = normalizedY <= 0.10f
                ? leftSide ? RetargetRegion.LeftFoot : rightSide ? RetargetRegion.RightFoot : RetargetRegion.Unknown
                : normalizedY <= 0.22f
                    ? leftSide ? RetargetRegion.LeftCalf : rightSide ? RetargetRegion.RightCalf : RetargetRegion.Unknown
                    : leftSide ? RetargetRegion.LeftThigh : rightSide ? RetargetRegion.RightThigh : RetargetRegion.Unknown;

        return region != RetargetRegion.Unknown;
    }

    private static int GetPreferredSide(Vector3 position, float centerLateral)
    {
        float lateralDelta = position.Z - centerLateral;
        if (MathF.Abs(lateralDelta) <= 0.01f)
            return 0;

        return lateralDelta < 0.0f ? -1 : 1;
    }

    private static bool MatchesPreferredSide(RetargetRegion region, int preferredSide)
    {
        if (preferredSide == 0 || !IsSidedRegion(region))
            return true;

        return preferredSide < 0
            ? IsLeftRegion(region)
            : IsRightRegion(region);
    }

    private static bool IsSidedRegion(RetargetRegion region)
    {
        return IsLeftRegion(region) || IsRightRegion(region);
    }

    private static bool IsLeftRegion(RetargetRegion region)
    {
        return region is
            RetargetRegion.LeftShoulder or
            RetargetRegion.LeftUpperArm or
            RetargetRegion.LeftForearm or
            RetargetRegion.LeftHand or
            RetargetRegion.LeftThigh or
            RetargetRegion.LeftCalf or
            RetargetRegion.LeftFoot;
    }

    private static bool IsRightRegion(RetargetRegion region)
    {
        return region is
            RetargetRegion.RightShoulder or
            RetargetRegion.RightUpperArm or
            RetargetRegion.RightForearm or
            RetargetRegion.RightHand or
            RetargetRegion.RightThigh or
            RetargetRegion.RightCalf or
            RetargetRegion.RightFoot;
    }

    private static string Normalize(string boneName)
    {
        return boneName.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private static bool IsLeft(string normalized)
    {
        return normalized.StartsWith("gl", StringComparison.Ordinal) ||
            normalized.StartsWith("l", StringComparison.Ordinal) ||
            normalized.Contains("left", StringComparison.Ordinal) ||
            normalized.Contains("lclav", StringComparison.Ordinal) ||
            normalized.Contains("lshoulder", StringComparison.Ordinal) ||
            normalized.Contains("lelbow", StringComparison.Ordinal) ||
            normalized.Contains("lwrist", StringComparison.Ordinal) ||
            normalized.Contains("lhand", StringComparison.Ordinal) ||
            normalized.Contains("lhip", StringComparison.Ordinal) ||
            normalized.Contains("lknee", StringComparison.Ordinal) ||
            normalized.Contains("lankle", StringComparison.Ordinal) ||
            normalized.Contains("lball", StringComparison.Ordinal) ||
            normalized.Contains("lfoot", StringComparison.Ordinal) ||
            normalized.Contains("left", StringComparison.Ordinal);
    }

    private static bool IsRight(string normalized)
    {
        return normalized.StartsWith("gr", StringComparison.Ordinal) ||
            normalized.StartsWith("r", StringComparison.Ordinal) ||
            normalized.Contains("right", StringComparison.Ordinal) ||
            normalized.Contains("rclav", StringComparison.Ordinal) ||
            normalized.Contains("rshoulder", StringComparison.Ordinal) ||
            normalized.Contains("relbow", StringComparison.Ordinal) ||
            normalized.Contains("rwrist", StringComparison.Ordinal) ||
            normalized.Contains("rhand", StringComparison.Ordinal) ||
            normalized.Contains("rhip", StringComparison.Ordinal) ||
            normalized.Contains("rknee", StringComparison.Ordinal) ||
            normalized.Contains("rankle", StringComparison.Ordinal) ||
            normalized.Contains("rball", StringComparison.Ordinal) ||
            normalized.Contains("rfoot", StringComparison.Ordinal) ||
            normalized.Contains("right", StringComparison.Ordinal);
    }

    private static bool IsLikelyHelperBone(string name)
    {
        string normalized = Normalize(name);
        return normalized.Contains("offset", StringComparison.Ordinal) ||
            normalized.Contains("ik", StringComparison.Ordinal) ||
            normalized.Contains("helper", StringComparison.Ordinal) ||
            normalized.Contains("attach", StringComparison.Ordinal) ||
            normalized.Contains("twist", StringComparison.Ordinal) ||
            normalized.Contains("armature", StringComparison.Ordinal);
    }
}
