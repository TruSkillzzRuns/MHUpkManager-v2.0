using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class BoneWeightInterpolator
{
    private readonly WeightNormalizer _normalizer;

    public BoneWeightInterpolator(WeightNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public IReadOnlyList<RetargetWeight> Interpolate(
        RetargetVertex a,
        RetargetVertex b,
        RetargetVertex c,
        Vector3 barycentric)
    {
        Dictionary<string, float> weights = new(StringComparer.OrdinalIgnoreCase);
        Accumulate(weights, a.Weights, barycentric.X);
        Accumulate(weights, b.Weights, barycentric.Y);
        Accumulate(weights, c.Weights, barycentric.Z);
        return _normalizer.Normalize(weights.Select(static pair => new RetargetWeight(pair.Key, pair.Value)));
    }

    private static void Accumulate(Dictionary<string, float> destination, IEnumerable<RetargetWeight> source, float multiplier)
    {
        if (multiplier <= 0.0f)
            return;

        foreach (RetargetWeight weight in source)
        {
            if (weight.Weight <= 0.0f || string.IsNullOrWhiteSpace(weight.BoneName))
                continue;

            float contribution = weight.Weight * multiplier;
            if (!destination.TryAdd(weight.BoneName, contribution))
                destination[weight.BoneName] += contribution;
        }
    }
}

internal sealed class WeightNormalizer
{
    public IReadOnlyList<RetargetWeight> Normalize(IEnumerable<RetargetWeight> weights)
    {
        Dictionary<string, float> combined = new(StringComparer.OrdinalIgnoreCase);
        foreach (RetargetWeight weight in weights)
        {
            if (weight.Weight <= 0.0f || string.IsNullOrWhiteSpace(weight.BoneName))
                continue;

            if (!combined.TryAdd(weight.BoneName, weight.Weight))
                combined[weight.BoneName] += weight.Weight;
        }

        List<KeyValuePair<string, float>> topWeights = [.. combined
            .Where(static pair => pair.Value > 0.0f)
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(4)];

        if (topWeights.Count == 0)
            return Array.Empty<RetargetWeight>();

        float total = topWeights.Sum(static pair => pair.Value);
        if (total <= 0.0f)
            total = 1.0f;

        return [.. topWeights.Select(pair => new RetargetWeight(pair.Key, pair.Value / total))];
    }
}

internal sealed class BoneIndexMapper
{
    private readonly Dictionary<string, int> _boneNameToIndex;

    public BoneIndexMapper(IEnumerable<RetargetBone> bones)
    {
        _boneNameToIndex = bones
            .Select((bone, index) => (bone, index))
            .ToDictionary(static pair => pair.bone.Name, static pair => pair.index, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetIndex(string boneName, out int boneIndex)
    {
        return _boneNameToIndex.TryGetValue(boneName, out boneIndex);
    }

    public void ValidateWeights(IEnumerable<RetargetWeight> weights)
    {
        foreach (RetargetWeight weight in weights)
        {
            if (!_boneNameToIndex.ContainsKey(weight.BoneName))
                throw new InvalidOperationException($"Bone '{weight.BoneName}' was not found in the original MHO skeleton.");
        }
    }
}

internal sealed class SkeletonBinder
{
    private readonly BoneIndexMapper _boneIndexMapper;

    public SkeletonBinder(BoneIndexMapper boneIndexMapper)
    {
        _boneIndexMapper = boneIndexMapper;
    }

    public RetargetMesh Bind(
        RetargetMesh targetMesh,
        IReadOnlyList<RetargetBone> originalSkeleton,
        Action<string> log = null)
    {
        RetargetMesh bound = targetMesh.DeepClone();
        bound.Bones.Clear();
        bound.Bones.AddRange(originalSkeleton.Select(static bone => bone.DeepClone()));
        bound.RebuildBoneLookup();

        int weightedVertices = 0;
        foreach (RetargetSection section in bound.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                _boneIndexMapper.ValidateWeights(vertex.Weights);
                weightedVertices++;
            }
        }

        log?.Invoke($"Bound {weightedVertices} vertices to the original MHO skeleton ({bound.Bones.Count} bones).");
        return bound;
    }
}

internal sealed class WeightTransferEngine
{
    private const int PreferredBoneCount = 4;
    private static readonly RetargetRegion[] DiagnosticRegions =
    [
        RetargetRegion.Head,
        RetargetRegion.Chest,
        RetargetRegion.Pelvis,
        RetargetRegion.LeftHand,
        RetargetRegion.RightHand,
        RetargetRegion.LeftFoot,
        RetargetRegion.RightFoot
    ];

    private readonly WeightNormalizer _normalizer = new();
    private readonly BoneWeightInterpolator _interpolator;

    public WeightTransferEngine()
    {
        _interpolator = new BoneWeightInterpolator(_normalizer);
    }

    public RetargetMesh TransferWeights(
        RetargetMesh originalMhoMesh,
        RetargetMesh newMesh,
        IReadOnlyList<RetargetBone> originalSkeleton,
        Action<string> log = null)
    {
        if (originalMhoMesh == null)
            throw new ArgumentNullException(nameof(originalMhoMesh));
        if (newMesh == null)
            throw new ArgumentNullException(nameof(newMesh));
        if (originalSkeleton == null || originalSkeleton.Count == 0)
            throw new ArgumentException("Original MHO skeleton is required.", nameof(originalSkeleton));

        MeshSurfaceKDTree kdTree = new(originalMhoMesh);
        BoneIndexMapper boneIndexMapper = new(originalSkeleton);
        List<RegionAnchor> skeletonAnchors = RetargetRegions.BuildAnchors(originalSkeleton);
        Dictionary<RetargetRegion, TransferDiagnostic> diagnostics = BuildDiagnosticTargets(skeletonAnchors);

        RetargetMesh weightedGeometry = newMesh.DeepClone();
        int transferredVertices = 0;
        foreach (RetargetSection section in weightedGeometry.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                RetargetRegion region = RetargetRegions.InferRegion(vertex.Position, skeletonAnchors);
                IReadOnlyList<string> preferredBones = RetargetRegions.GetPreferredBoneNames(vertex.Position, skeletonAnchors, PreferredBoneCount);
                IReadOnlyList<RetargetRegion> preferredRegions = RetargetRegions.GetAllowedRegions(region);
                MeshSurfaceKDTree.TriangleHit hit = ResolveTransferHit(kdTree, vertex.Position, preferredBones, preferredRegions);
                IReadOnlyList<RetargetWeight> weights = _interpolator.Interpolate(
                    hit.A,
                    hit.B,
                    hit.C,
                    hit.Projection.Barycentric);

                boneIndexMapper.ValidateWeights(weights);
                vertex.Weights.Clear();
                vertex.Weights.AddRange(weights);
                transferredVertices++;
                TryCaptureDiagnostic(diagnostics, vertex, region, preferredBones, hit, weights);
            }
        }

        log?.Invoke($"Transferred interpolated weights from original MHO mesh to {transferredVertices} vertex/vertices.");
        LogDiagnostics(diagnostics, log);
        SkeletonBinder binder = new(boneIndexMapper);
        return binder.Bind(weightedGeometry, originalSkeleton, log);
    }

    private static Dictionary<RetargetRegion, TransferDiagnostic> BuildDiagnosticTargets(IReadOnlyList<RegionAnchor> anchors)
    {
        Dictionary<RetargetRegion, TransferDiagnostic> diagnostics = [];
        foreach (RetargetRegion region in DiagnosticRegions)
        {
            Vector3 target = RetargetRegions.GetAnchorCenter(RetargetRegions.GetRegionBoneNames(region), anchors);
            diagnostics[region] = new TransferDiagnostic(region, target);
        }

        return diagnostics;
    }

    private static void TryCaptureDiagnostic(
        Dictionary<RetargetRegion, TransferDiagnostic> diagnostics,
        RetargetVertex vertex,
        RetargetRegion inferredRegion,
        IReadOnlyList<string> preferredBones,
        MeshSurfaceKDTree.TriangleHit hit,
        IReadOnlyList<RetargetWeight> weights)
    {
        foreach ((RetargetRegion region, TransferDiagnostic diagnostic) in diagnostics)
        {
            float distance = Vector3.DistanceSquared(vertex.Position, diagnostic.TargetPosition);
            if (distance >= diagnostic.BestDistanceSquared)
                continue;

            diagnostic.BestDistanceSquared = distance;
            diagnostic.VertexPosition = vertex.Position;
            diagnostic.InferredRegion = inferredRegion;
            diagnostic.PreferredBones = preferredBones.Count == 0 ? string.Empty : string.Join(", ", preferredBones);
            diagnostic.SourceDominantBone = hit.DominantBoneName ?? "<none>";
            diagnostic.SourceDominantRegion = hit.DominantRegion;
            diagnostic.AssignedWeights = weights.Count == 0
                ? "<none>"
                : string.Join(", ", weights.Select(weight => $"{weight.BoneName}:{weight.Weight:0.##}"));
        }
    }

    private static void LogDiagnostics(Dictionary<RetargetRegion, TransferDiagnostic> diagnostics, Action<string> log)
    {
        if (log == null)
            return;

        foreach (RetargetRegion region in DiagnosticRegions)
        {
            if (!diagnostics.TryGetValue(region, out TransferDiagnostic diagnostic) || !float.IsFinite(diagnostic.BestDistanceSquared))
            {
                log($"Transfer diagnostic {region}: no sample captured.");
                continue;
            }

            log(
                $"Transfer diagnostic {region}: inferred={diagnostic.InferredRegion}, " +
                $"sourceTriangle={diagnostic.SourceDominantBone}/{diagnostic.SourceDominantRegion}, " +
                $"preferred=[{diagnostic.PreferredBones}], " +
                $"weights=[{diagnostic.AssignedWeights}], " +
                $"vertex=({diagnostic.VertexPosition.X:0.##},{diagnostic.VertexPosition.Y:0.##},{diagnostic.VertexPosition.Z:0.##}).");
        }
    }

    private static MeshSurfaceKDTree.TriangleHit ResolveTransferHit(
        MeshSurfaceKDTree kdTree,
        Vector3 position,
        IReadOnlyList<string> preferredBones,
        IReadOnlyList<RetargetRegion> preferredRegions)
    {
        if (kdTree.TryFindNearestTriangle(position, preferredBones, preferredRegions, out MeshSurfaceKDTree.TriangleHit combinedHit))
            return combinedHit;

        if (kdTree.TryFindNearestTriangle(position, null, preferredRegions, out MeshSurfaceKDTree.TriangleHit regionalHit))
            return regionalHit;

        if (kdTree.TryFindNearestTriangle(position, preferredBones, null, out MeshSurfaceKDTree.TriangleHit boneHit))
            return boneHit;

        return kdTree.FindNearestTriangle(position);
    }

    private sealed class TransferDiagnostic
    {
        public TransferDiagnostic(RetargetRegion region, Vector3 targetPosition)
        {
            Region = region;
            TargetPosition = targetPosition;
        }

        public RetargetRegion Region { get; }
        public Vector3 TargetPosition { get; }
        public float BestDistanceSquared { get; set; } = float.PositiveInfinity;
        public Vector3 VertexPosition { get; set; }
        public RetargetRegion InferredRegion { get; set; } = RetargetRegion.Unknown;
        public string PreferredBones { get; set; } = string.Empty;
        public string SourceDominantBone { get; set; } = string.Empty;
        public RetargetRegion SourceDominantRegion { get; set; } = RetargetRegion.Unknown;
        public string AssignedWeights { get; set; } = string.Empty;
    }
}
