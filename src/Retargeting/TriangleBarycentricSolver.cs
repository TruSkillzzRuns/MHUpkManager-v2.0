using System.Numerics;

namespace MHUpkManager.Retargeting;

internal static class TriangleBarycentricSolver
{
    public static TriangleProjection FindClosestPoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = point - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0.0f && d2 <= 0.0f)
            return Create(point, a, new Vector3(1.0f, 0.0f, 0.0f));

        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0.0f && d4 <= d3)
            return Create(point, b, new Vector3(0.0f, 1.0f, 0.0f));

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f)
        {
            float v = d1 / (d1 - d3);
            Vector3 closest = a + (v * ab);
            return Create(point, closest, new Vector3(1.0f - v, v, 0.0f));
        }

        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0.0f && d5 <= d6)
            return Create(point, c, new Vector3(0.0f, 0.0f, 1.0f));

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f)
        {
            float w = d2 / (d2 - d6);
            Vector3 closest = a + (w * ac);
            return Create(point, closest, new Vector3(1.0f - w, 0.0f, w));
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f)
        {
            Vector3 bc = c - b;
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            Vector3 closest = b + (w * bc);
            return Create(point, closest, new Vector3(0.0f, 1.0f - w, w));
        }

        float denominator = 1.0f / (va + vb + vc);
        float barycentricV = vb * denominator;
        float barycentricW = vc * denominator;
        Vector3 projected = a + (ab * barycentricV) + (ac * barycentricW);
        return Create(point, projected, new Vector3(1.0f - barycentricV - barycentricW, barycentricV, barycentricW));
    }

    private static TriangleProjection Create(Vector3 originalPoint, Vector3 closestPoint, Vector3 barycentric)
    {
        return new TriangleProjection(
            closestPoint,
            barycentric,
            Vector3.DistanceSquared(originalPoint, closestPoint));
    }
}

internal readonly record struct TriangleProjection(
    Vector3 ClosestPoint,
    Vector3 Barycentric,
    float DistanceSquared);
