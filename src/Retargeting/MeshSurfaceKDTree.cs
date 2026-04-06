using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class MeshSurfaceKDTree
{
    private readonly TriangleEntry[] _triangles;
    private readonly Node _root;
    private readonly Dictionary<string, int[]> _triangleIndicesByDominantBone;
    private readonly Dictionary<RetargetRegion, int[]> _triangleIndicesByDominantRegion;

    public MeshSurfaceKDTree(RetargetMesh sourceMesh)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));

        _triangles = BuildTriangles(sourceMesh).ToArray();
        if (_triangles.Length == 0)
            throw new InvalidOperationException("Source mesh did not contain any triangles.");

        _triangleIndicesByDominantBone = _triangles
            .Select((triangle, index) => (triangle, index))
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.triangle.DominantBoneName))
            .GroupBy(static pair => pair.triangle.DominantBoneName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static item => item.index).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        _triangleIndicesByDominantRegion = _triangles
            .Select((triangle, index) => (triangle, index))
            .Where(static pair => pair.triangle.DominantRegion != RetargetRegion.Unknown)
            .GroupBy(static pair => pair.triangle.DominantRegion)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static item => item.index).ToArray());

        int[] indices = Enumerable.Range(0, _triangles.Length).ToArray();
        _root = Build(indices, 0, indices.Length);
    }

    public TriangleHit FindNearestTriangle(Vector3 point)
    {
        BestHit best = new()
        {
            DistanceSquared = float.PositiveInfinity,
            TriangleIndex = -1,
            Result = default
        };

        Search(_root, point, ref best);
        if (best.TriangleIndex < 0)
            throw new InvalidOperationException("No nearest triangle could be resolved.");

        return best.Result;
    }

    public TriangleHit FindNearestTriangle(Vector3 point, IReadOnlyList<string> preferredBoneNames, IReadOnlyList<RetargetRegion> preferredRegions = null)
    {
        if (TryFindNearestTriangle(point, preferredBoneNames, preferredRegions, out TriangleHit filteredHit))
            return filteredHit;

        return FindNearestTriangle(point);
    }

    public bool TryFindNearestTriangle(
        Vector3 point,
        IReadOnlyList<string> preferredBoneNames,
        IReadOnlyList<RetargetRegion> preferredRegions,
        out TriangleHit hit)
    {
        if (preferredBoneNames != null || preferredRegions != null)
        {
            BestHit filteredBest = new()
            {
                DistanceSquared = float.PositiveInfinity,
                TriangleIndex = -1,
                Result = default
            };

            HashSet<int> visitedTriangles = [];
            if (preferredRegions != null)
            {
                foreach (RetargetRegion region in preferredRegions)
                {
                    if (!_triangleIndicesByDominantRegion.TryGetValue(region, out int[] triangleIndices))
                        continue;

                    foreach (int triangleIndex in triangleIndices)
                    {
                        if (!visitedTriangles.Add(triangleIndex))
                            continue;

                        EvaluateTriangle(point, triangleIndex, ref filteredBest);
                    }
                }
            }

            foreach (string boneName in preferredBoneNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(boneName) ||
                    !_triangleIndicesByDominantBone.TryGetValue(boneName, out int[] triangleIndices))
                {
                    continue;
                }

                foreach (int triangleIndex in triangleIndices)
                {
                    if (!visitedTriangles.Add(triangleIndex))
                        continue;

                    EvaluateTriangle(point, triangleIndex, ref filteredBest);
                }
            }

            if (filteredBest.TriangleIndex >= 0)
            {
                hit = filteredBest.Result;
                return true;
            }
        }

        hit = default;
        return false;
    }

    private static IEnumerable<TriangleEntry> BuildTriangles(RetargetMesh sourceMesh)
    {
        foreach (RetargetSection section in sourceMesh.Sections)
        {
            for (int index = 0; index + 2 < section.Indices.Count; index += 3)
            {
                int i0 = section.Indices[index];
                int i1 = section.Indices[index + 1];
                int i2 = section.Indices[index + 2];
                if ((uint)i0 >= section.Vertices.Count || (uint)i1 >= section.Vertices.Count || (uint)i2 >= section.Vertices.Count)
                    continue;

                RetargetVertex v0 = section.Vertices[i0];
                RetargetVertex v1 = section.Vertices[i1];
                RetargetVertex v2 = section.Vertices[i2];
                yield return new TriangleEntry(v0, v1, v2);
            }
        }
    }

    private Node Build(int[] indices, int start, int length)
    {
        BoundingBox bounds = BoundingBox.Empty;
        BoundingBox centroidBounds = BoundingBox.Empty;
        for (int i = start; i < start + length; i++)
        {
            TriangleEntry triangle = _triangles[indices[i]];
            bounds = BoundingBox.Encapsulate(bounds, triangle.Bounds);
            centroidBounds = BoundingBox.Include(centroidBounds, triangle.Centroid);
        }

        if (length <= 12)
            return new Node(bounds, start, length, -1, null, null, indices);

        int axis = centroidBounds.GetLargestAxis();
        Array.Sort(indices, start, length, Comparer<int>.Create((left, right) =>
            GetAxisValue(_triangles[left].Centroid, axis).CompareTo(GetAxisValue(_triangles[right].Centroid, axis))));

        int leftLength = length / 2;
        int rightLength = length - leftLength;
        Node leftNode = Build(indices, start, leftLength);
        Node rightNode = Build(indices, start + leftLength, rightLength);
        return new Node(bounds, start, length, axis, leftNode, rightNode, null);
    }

    private void Search(Node node, Vector3 point, ref BestHit best)
    {
        if (node == null)
            return;

        float nodeDistance = node.Bounds.DistanceSquaredTo(point);
        if (nodeDistance > best.DistanceSquared)
            return;

        if (node.IsLeaf)
        {
            for (int i = node.Start; i < node.Start + node.Length; i++)
                EvaluateTriangle(point, node.LeafIndices[i], ref best);

            return;
        }

        float leftDistance = node.Left?.Bounds.DistanceSquaredTo(point) ?? float.PositiveInfinity;
        float rightDistance = node.Right?.Bounds.DistanceSquaredTo(point) ?? float.PositiveInfinity;

        if (leftDistance <= rightDistance)
        {
            Search(node.Left, point, ref best);
            Search(node.Right, point, ref best);
        }
        else
        {
            Search(node.Right, point, ref best);
            Search(node.Left, point, ref best);
        }
    }

    private void EvaluateTriangle(Vector3 point, int triangleIndex, ref BestHit best)
    {
        TriangleEntry triangle = _triangles[triangleIndex];
        TriangleProjection projection = TriangleBarycentricSolver.FindClosestPoint(point, triangle.A.Position, triangle.B.Position, triangle.C.Position);
        if (projection.DistanceSquared < best.DistanceSquared)
        {
            best.DistanceSquared = projection.DistanceSquared;
            best.TriangleIndex = triangleIndex;
            best.Result = new TriangleHit(
                triangle.A,
                triangle.B,
                triangle.C,
                projection,
                triangle.DominantBoneName,
                triangle.DominantRegion);
        }
    }

    internal readonly record struct TriangleHit(
        RetargetVertex A,
        RetargetVertex B,
        RetargetVertex C,
        TriangleProjection Projection,
        string DominantBoneName,
        RetargetRegion DominantRegion);

    private readonly record struct TriangleEntry(RetargetVertex A, RetargetVertex B, RetargetVertex C)
    {
        public string DominantBoneName => ResolveDominantBoneName(A, B, C);
        public RetargetRegion DominantRegion => RetargetRegions.ClassifyBone(DominantBoneName);
        public Vector3 Centroid => (A.Position + B.Position + C.Position) / 3.0f;
        public BoundingBox Bounds => BoundingBox.FromTriangle(A.Position, B.Position, C.Position);

        private static string ResolveDominantBoneName(RetargetVertex a, RetargetVertex b, RetargetVertex c)
        {
            Dictionary<string, float> totals = new(StringComparer.OrdinalIgnoreCase);
            Accumulate(totals, a.Weights);
            Accumulate(totals, b.Weights);
            Accumulate(totals, c.Weights);
            return totals.Count == 0
                ? null
                : totals.OrderByDescending(static pair => pair.Value).ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase).First().Key;
        }

        private static void Accumulate(Dictionary<string, float> totals, IEnumerable<RetargetWeight> weights)
        {
            foreach (RetargetWeight weight in weights)
            {
                if (weight.Weight <= 0.0f || string.IsNullOrWhiteSpace(weight.BoneName))
                    continue;

                if (!totals.TryAdd(weight.BoneName, weight.Weight))
                    totals[weight.BoneName] += weight.Weight;
            }
        }
    }

    private sealed record Node(
        BoundingBox Bounds,
        int Start,
        int Length,
        int Axis,
        Node Left,
        Node Right,
        int[] LeafIndices)
    {
        public bool IsLeaf => LeafIndices != null;
    }

    private struct BestHit
    {
        public float DistanceSquared;
        public int TriangleIndex;
        public TriangleHit Result;
    }

    private static float GetAxisValue(Vector3 value, int axis)
    {
        return axis switch
        {
            1 => value.Y,
            2 => value.Z,
            _ => value.X
        };
    }

    private readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
    {
        public static BoundingBox Empty => new(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

        public static BoundingBox FromTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
            Vector3 max = Vector3.Max(a, Vector3.Max(b, c));
            return new BoundingBox(min, max);
        }

        public static BoundingBox Encapsulate(BoundingBox current, BoundingBox other)
        {
            if (!current.IsValid)
                return other;
            if (!other.IsValid)
                return current;

            return new BoundingBox(Vector3.Min(current.Min, other.Min), Vector3.Max(current.Max, other.Max));
        }

        public static BoundingBox Include(BoundingBox current, Vector3 point)
        {
            if (!current.IsValid)
                return new BoundingBox(point, point);

            return new BoundingBox(Vector3.Min(current.Min, point), Vector3.Max(current.Max, point));
        }

        public bool IsValid => float.IsFinite(Min.X) && float.IsFinite(Max.X);

        public int GetLargestAxis()
        {
            Vector3 extents = Max - Min;
            if (extents.Y >= extents.X && extents.Y >= extents.Z)
                return 1;
            if (extents.Z >= extents.X && extents.Z >= extents.Y)
                return 2;
            return 0;
        }

        public float DistanceSquaredTo(Vector3 point)
        {
            Vector3 clamped = Vector3.Clamp(point, Min, Max);
            return Vector3.DistanceSquared(clamped, point);
        }
    }
}
