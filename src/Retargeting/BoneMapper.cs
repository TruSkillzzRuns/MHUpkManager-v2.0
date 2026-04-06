namespace MHUpkManager.Retargeting;

internal sealed class BoneMapper
{
    public BoneMappingResult AutoMap(RetargetMesh mesh, SkeletonDefinition playerSkeleton, Action<string> log = null)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));
        if (playerSkeleton == null)
            throw new ArgumentNullException(nameof(playerSkeleton));

        mesh.RebuildBoneLookup();
        playerSkeleton.RebuildBoneLookup();

        List<BoneDescriptor> targetDescriptors = playerSkeleton.Bones
            .Select((bone, index) => BoneDescriptor.Create(bone.Name, index, bone.ParentIndex))
            .ToList();

        BoneMappingResult result = new();
        foreach (RetargetBone sourceBone in mesh.Bones)
        {
            string mapped = ResolveDirectMatch(sourceBone.Name, playerSkeleton) ??
                ResolveByScoredHeuristics(sourceBone, mesh, targetDescriptors, result.Mapping) ??
                ResolveParentFallback(sourceBone, mesh, result.Mapping, playerSkeleton) ??
                ResolveRootFallback(targetDescriptors);

            result.Mapping[sourceBone.Name] = mapped;
            log?.Invoke($"Mapped bone {sourceBone.Name} -> {mapped}");
        }

        foreach (RetargetBone sourceBone in mesh.Bones.Where(static bone => !string.IsNullOrWhiteSpace(bone.Name)))
        {
            if (!result.Mapping.ContainsKey(sourceBone.Name))
                result.UnmappedBones.Add(sourceBone.Name);
        }

        return result;
    }

    private static string ResolveDirectMatch(string sourceBoneName, SkeletonDefinition playerSkeleton)
    {
        if (playerSkeleton.BonesByName.TryGetValue(sourceBoneName, out RetargetBone exact))
            return exact.Name;

        string normalized = NormalizeBoneName(sourceBoneName);
        RetargetBone normalizedMatch = playerSkeleton.Bones
            .FirstOrDefault(bone => string.Equals(NormalizeBoneName(bone.Name), normalized, StringComparison.OrdinalIgnoreCase));
        return normalizedMatch?.Name;
    }

    private static string ResolveByScoredHeuristics(
        RetargetBone sourceBone,
        RetargetMesh sourceMesh,
        IReadOnlyList<BoneDescriptor> targetDescriptors,
        IReadOnlyDictionary<string, string> resolvedMappings)
    {
        BoneDescriptor sourceDescriptor = BoneDescriptor.Create(sourceBone.Name, -1, sourceBone.ParentIndex);
        if (sourceDescriptor.IsEmpty)
            return null;

        int? mappedParentIndex = GetMappedParentIndex(sourceBone, sourceMesh, targetDescriptors, resolvedMappings);
        BoneDescriptor best = null;
        int bestScore = int.MinValue;

        foreach (BoneDescriptor target in targetDescriptors)
        {
            int score = ScoreBoneMatch(sourceDescriptor, target, mappedParentIndex);
            if (score > bestScore)
            {
                bestScore = score;
                best = target;
            }
        }

        return bestScore >= 80 ? best?.Name : null;
    }

    private static int ScoreBoneMatch(BoneDescriptor source, BoneDescriptor target, int? mappedParentIndex)
    {
        int score = 0;

        if (source.Normalized == target.Normalized)
            score += 1000;

        if (target.IsHelper)
            score -= 260;

        if (source.IsHelper != target.IsHelper)
            score -= 120;

        if (source.Side != BoneSide.Unknown && target.Side != BoneSide.Unknown)
            score += source.Side == target.Side ? 140 : -220;

        if (source.Region != BoneRegion.Unknown && target.Region != BoneRegion.Unknown)
            score += source.Region == target.Region ? 120 : -180;

        score += ScoreRegionAffinity(source.Region, target.Region);

        if (source.Digit != FingerDigit.Unknown && target.Digit != FingerDigit.Unknown)
            score += source.Digit == target.Digit ? 70 : -70;

        if (source.Ordinal > 0 && target.Ordinal > 0)
            score += source.Ordinal == target.Ordinal ? 50 : Math.Max(0, 30 - (Math.Abs(source.Ordinal - target.Ordinal) * 12));

        if (!string.IsNullOrWhiteSpace(source.Canonical) && !string.IsNullOrWhiteSpace(target.Canonical))
        {
            if (source.Canonical == target.Canonical)
            {
                score += 500;
            }
            else
            {
                int tokenOverlap = source.CanonicalTokens.Intersect(target.CanonicalTokens, StringComparer.OrdinalIgnoreCase).Count();
                score += tokenOverlap * 45;

                if (source.Canonical.Contains(target.Canonical, StringComparison.OrdinalIgnoreCase) ||
                    target.Canonical.Contains(source.Canonical, StringComparison.OrdinalIgnoreCase))
                {
                    score += 60;
                }
            }
        }

        if (mappedParentIndex.HasValue && target.ParentIndex == mappedParentIndex.Value)
            score += 80;

        if (source.Region == BoneRegion.Root && target.Region == BoneRegion.Root)
            score += 160;

        if (source.Region == BoneRegion.Root && target.IsHelper)
            score -= 220;

        return score;
    }

    private static int ScoreRegionAffinity(BoneRegion source, BoneRegion target)
    {
        if (source == BoneRegion.Unknown || target == BoneRegion.Unknown || source == target)
            return 0;

        return (source, target) switch
        {
            (BoneRegion.UpperArm, BoneRegion.Forearm) => -220,
            (BoneRegion.Forearm, BoneRegion.UpperArm) => -160,
            (BoneRegion.Shoulder, BoneRegion.Forearm) => -200,
            (BoneRegion.Shoulder, BoneRegion.Hand) => -220,
            (BoneRegion.Spine, BoneRegion.Root) => -180,
            (BoneRegion.Spine, BoneRegion.Shoulder) => -90,
            (BoneRegion.Thigh, BoneRegion.Calf) => -180,
            (BoneRegion.Thigh, BoneRegion.Foot) => -240,
            (BoneRegion.Calf, BoneRegion.Thigh) => -120,
            (BoneRegion.Foot, BoneRegion.Toe) => -80,
            (BoneRegion.Head, BoneRegion.Neck) => -80,
            (BoneRegion.Hand, BoneRegion.Forearm) => -160,
            _ => 0
        };
    }

    private static int? GetMappedParentIndex(
        RetargetBone sourceBone,
        RetargetMesh sourceMesh,
        IReadOnlyList<BoneDescriptor> targetDescriptors,
        IReadOnlyDictionary<string, string> resolvedMappings)
    {
        int parentIndex = sourceBone.ParentIndex;
        while (parentIndex >= 0 && parentIndex < sourceMesh.Bones.Count)
        {
            RetargetBone parentBone = sourceMesh.Bones[parentIndex];
            if (resolvedMappings.TryGetValue(parentBone.Name, out string mappedParent))
            {
                BoneDescriptor target = targetDescriptors.FirstOrDefault(candidate => string.Equals(candidate.Name, mappedParent, StringComparison.OrdinalIgnoreCase));
                return target?.Index;
            }

            parentIndex = parentBone.ParentIndex;
        }

        return null;
    }

    private static string ResolveParentFallback(
        RetargetBone sourceBone,
        RetargetMesh sourceMesh,
        IReadOnlyDictionary<string, string> resolvedMappings,
        SkeletonDefinition playerSkeleton)
    {
        int parentIndex = sourceBone.ParentIndex;
        while (parentIndex >= 0 && parentIndex < sourceMesh.Bones.Count)
        {
            RetargetBone parentBone = sourceMesh.Bones[parentIndex];
            if (resolvedMappings.TryGetValue(parentBone.Name, out string parentMapped))
                return parentMapped;

            string normalizedParent = NormalizeBoneName(parentBone.Name);
            RetargetBone candidate = playerSkeleton.Bones
                .FirstOrDefault(bone => NormalizeBoneName(bone.Name).Contains(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                                        normalizedParent.Contains(NormalizeBoneName(bone.Name), StringComparison.OrdinalIgnoreCase));
            if (candidate != null)
                return candidate.Name;

            parentIndex = parentBone.ParentIndex;
        }

        return null;
    }

    private static string ResolveRootFallback(IReadOnlyList<BoneDescriptor> targetDescriptors)
    {
        return targetDescriptors
            .OrderByDescending(static descriptor => descriptor.Region == BoneRegion.Root)
            .ThenBy(static descriptor => descriptor.Index)
            .Select(static descriptor => descriptor.Name)
            .FirstOrDefault();
    }

    internal static string NormalizeBoneName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string normalized = name.Trim().ToLowerInvariant();
        foreach (string prefix in new[] { "b_", "bone_", "bn_", "def_", "skel_", "mixamorig:", "valvebiped_", "g_" })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                normalized = normalized[prefix.Length..];
        }

        return string.Concat(normalized.Where(char.IsLetterOrDigit));
    }

    private sealed class BoneDescriptor
    {
        public required string Name { get; init; }
        public required string Normalized { get; init; }
        public required string Canonical { get; init; }
        public required string[] CanonicalTokens { get; init; }
        public required BoneSide Side { get; init; }
        public required BoneRegion Region { get; init; }
        public required FingerDigit Digit { get; init; }
        public required int Ordinal { get; init; }
        public required int Index { get; init; }
        public required int ParentIndex { get; init; }
        public required bool IsHelper { get; init; }
        public bool IsEmpty => string.IsNullOrWhiteSpace(Normalized);

        public static BoneDescriptor Create(string name, int index, int parentIndex)
        {
            string normalized = NormalizeBoneName(name);
            string canonical = BuildCanonicalName(normalized);
            return new BoneDescriptor
            {
                Name = name,
                Normalized = normalized,
                Canonical = canonical,
                CanonicalTokens = canonical.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Side = ResolveSide(normalized),
                Region = ResolveRegion(normalized),
                Digit = ResolveDigit(normalized),
                Ordinal = ResolveOrdinal(normalized),
                Index = index,
                ParentIndex = parentIndex,
                IsHelper = ResolveIsHelper(normalized)
            };
        }

        private static string BuildCanonicalName(string normalized)
        {
            List<string> tokens = [];
            BoneSide side = ResolveSide(normalized);
            if (side == BoneSide.Left) tokens.Add("left");
            if (side == BoneSide.Right) tokens.Add("right");

            BoneRegion region = ResolveRegion(normalized);
            if (region != BoneRegion.Unknown)
                tokens.Add(region.ToString().ToLowerInvariant());

            FingerDigit digit = ResolveDigit(normalized);
            if (digit != FingerDigit.Unknown)
                tokens.Add(digit.ToString().ToLowerInvariant());

            int ordinal = ResolveOrdinal(normalized);
            if (ordinal > 0)
                tokens.Add(ordinal.ToString());

            if (tokens.Count == 0)
                tokens.Add(normalized);

            return string.Join('_', tokens);
        }
    }

    private static BoneSide ResolveSide(string normalized)
    {
        if (ContainsAny(normalized, "left", "lft", "lhand", "larm", "lleg", "lclav", "lshoulder", "leye") ||
            normalized.StartsWith("l", StringComparison.Ordinal))
            return BoneSide.Left;

        if (ContainsAny(normalized, "right", "rgt", "rhand", "rarm", "rleg", "rclav", "rshoulder", "reye") ||
            normalized.StartsWith("r", StringComparison.Ordinal))
            return BoneSide.Right;

        return BoneSide.Unknown;
    }

    private static BoneRegion ResolveRegion(string normalized)
    {
        return normalized switch
        {
            _ when ContainsAny(normalized, "ik", "ikbase", "ikeffector") => BoneRegion.Ik,
            _ when ContainsAny(normalized, "attach", "weapon", "throwable") => BoneRegion.Attachment,
            _ when ContainsAny(normalized, "root", "armature", "rootnode") => BoneRegion.Root,
            _ when ContainsAny(normalized, "pelvis", "hip", "hips") => BoneRegion.Pelvis,
            _ when ContainsAny(normalized, "spine", "chest", "torso", "breast") => BoneRegion.Spine,
            _ when ContainsAny(normalized, "neck") => BoneRegion.Neck,
            _ when ContainsAny(normalized, "head", "jaw", "brow", "eyelid", "eye") => BoneRegion.Head,
            _ when ContainsAny(normalized, "clav", "shoulder") => BoneRegion.Shoulder,
            _ when ContainsAny(normalized, "upperarm", "uparm", "bicep", "shoulder") => BoneRegion.UpperArm,
            _ when ContainsAny(normalized, "forearm", "forarm", "lowerarm", "elbow") => BoneRegion.Forearm,
            _ when ContainsAny(normalized, "hand", "palm", "wrist") => BoneRegion.Hand,
            _ when ContainsAny(normalized, "thumb") => BoneRegion.Thumb,
            _ when ContainsAny(normalized, "index") => BoneRegion.IndexFinger,
            _ when ContainsAny(normalized, "middle") => BoneRegion.MiddleFinger,
            _ when ContainsAny(normalized, "ring") => BoneRegion.RingFinger,
            _ when ContainsAny(normalized, "pinky", "pinkie", "birdy") => BoneRegion.PinkyFinger,
            _ when ContainsAny(normalized, "thigh", "upleg", "legupper") => BoneRegion.Thigh,
            _ when ContainsAny(normalized, "calf", "knee", "leg", "shin") => BoneRegion.Calf,
            _ when ContainsAny(normalized, "ankle", "foot") => BoneRegion.Foot,
            _ when ContainsAny(normalized, "toe", "ball") => BoneRegion.Toe,
            _ when normalized.EndsWith("arm", StringComparison.OrdinalIgnoreCase) => BoneRegion.UpperArm,
            _ => BoneRegion.Unknown
        };
    }

    private static FingerDigit ResolveDigit(string normalized)
    {
        return normalized switch
        {
            _ when ContainsAny(normalized, "thumb") => FingerDigit.Thumb,
            _ when ContainsAny(normalized, "index") => FingerDigit.Index,
            _ when ContainsAny(normalized, "middle") => FingerDigit.Middle,
            _ when ContainsAny(normalized, "ring") => FingerDigit.Ring,
            _ when ContainsAny(normalized, "pinky", "pinkie", "birdy") => FingerDigit.Pinky,
            _ => FingerDigit.Unknown
        };
    }

    private static int ResolveOrdinal(string normalized)
    {
        for (int i = normalized.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(normalized[i]))
            {
                if (i == normalized.Length - 1)
                    return 0;

                if (int.TryParse(normalized[(i + 1)..], out int parsed))
                    return parsed;
                return 0;
            }
        }

        return int.TryParse(normalized, out int value) ? value : 0;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ResolveIsHelper(string normalized)
    {
        return ContainsAny(normalized, "offset", "twist", "helper", "ik", "effector", "attach", "weapon", "throwable");
    }

    private enum BoneSide
    {
        Unknown,
        Left,
        Right
    }

    private enum BoneRegion
    {
        Unknown,
        Root,
        Pelvis,
        Spine,
        Neck,
        Head,
        Shoulder,
        UpperArm,
        Forearm,
        Hand,
        Thumb,
        IndexFinger,
        MiddleFinger,
        RingFinger,
        PinkyFinger,
        Thigh,
        Calf,
        Foot,
        Toe,
        Ik,
        Attachment
    }

    private enum FingerDigit
    {
        Unknown,
        Thumb,
        Index,
        Middle,
        Ring,
        Pinky
    }
}
