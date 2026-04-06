using System.Numerics;

namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewCamera
{
    private const float MinDistance = 0.1f;
    private const float MaxDistance = 100000.0f;

    public Vector3 Target { get; private set; }
    public float Distance { get; private set; } = 10.0f;
    public float YawDegrees { get; private set; } = 45.0f;
    public float PitchDegrees { get; private set; } = 20.0f;

    public Matrix4x4 GetViewMatrix()
    {
        Vector3 position = GetPosition();
        return Matrix4x4.CreateLookAt(position, Target, Vector3.UnitZ);
    }

    public Matrix4x4 GetProjectionMatrix(float aspect)
    {
        float clampedAspect = MathF.Max(0.1f, aspect);
        return Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, clampedAspect, MathF.Max(0.01f, Distance / 200.0f), MaxDistance);
    }

    public Vector3 GetPosition()
    {
        float yaw = MathF.PI / 180.0f * YawDegrees;
        float pitch = MathF.PI / 180.0f * PitchDegrees;
        Vector3 orbit = new(
            Distance * MathF.Cos(pitch) * MathF.Cos(yaw),
            Distance * MathF.Cos(pitch) * MathF.Sin(yaw),
            Distance * MathF.Sin(pitch));

        return Target + orbit;
    }

    public void Orbit(float deltaX, float deltaY)
    {
        YawDegrees -= deltaX * 0.4f;
        PitchDegrees = Math.Clamp(PitchDegrees - (deltaY * 0.4f), -89.0f, 89.0f);
    }

    public void Pan(float deltaX, float deltaY)
    {
        Vector3 position = GetPosition();
        Vector3 forward = Vector3.Normalize(Target - position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        if (right.LengthSquared() < 1e-6f)
            right = Vector3.UnitX;
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        float scale = MathF.Max(0.001f, Distance * 0.0025f);
        Target += (-right * deltaX * scale) + (up * deltaY * scale);
    }

    public void Zoom(float wheelSteps)
    {
        Distance = Math.Clamp(Distance * (1.0f - (wheelSteps * 0.1f)), MinDistance, MaxDistance);
    }

    public void Reset(Vector3 center, float radius)
    {
        Target = center;
        Distance = MathF.Max(1.0f, radius * 3.0f);
        YawDegrees = 45.0f;
        PitchDegrees = 20.0f;
    }
}
