using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Orbit camera in the 3D scene (a special scene GameObject).
/// Uses a spherical coordinate system (Yaw/Pitch/Distance) around a fixed target to control the view
/// pose; it maintains its own orbit state and derives Position/View/Projection, and can be driven
/// directly by mouse input (Rotate/Pan/Zoom/Reset) without relying on a generic Transform.
/// </summary>
public class Camera : GameObject
{
    // Yaw angle (degrees), i.e. the horizontal rotation; any float, usually kept in [-360, 360].
    private float _yaw = -90f;
    public float Yaw
    {
        get => _yaw;
        set { _yaw = value; UpdateCamera(); }
    }

    // Pitch angle (degrees), i.e. the vertical rotation. Clamped to [-89, 89] to prevent flipping.
    private float _pitch = 20f;
    public float Pitch
    {
        get => _pitch;
        set
        {
            _pitch = Math.Clamp(value, -89f, 89f);
            UpdateCamera();
        }
    }

    // Distance between the camera and the target.
    private float _distance = 5f;
    public float Distance
    {
        get => _distance;
        set
        {
            _distance = Math.Clamp(value, 0.5f, 200f);
            UpdateCamera();
        }
    }

    // The look-at target (world coordinates).
    private Vector3 _target = Vector3.Zero;
    public Vector3 Target
    {
        get => _target;
        set { _target = value; UpdateCamera(); }
    }

    // Camera position.
    public Vector3 Position { get; private set; }

    // Field of view (degrees), default 45°. Controls the perspective projection's view width.
    public float Fov { get; set; } = 45f;

    // Screen aspect ratio (Width/Height), used for the projection matrix.
    public float AspectRatio { get; set; } = 1.6f;

    // Near clip plane distance, default 0.1.
    public float NearPlane { get; set; } = 0.1f;

    // Far clip plane distance, default 100.
    public float FarPlane { get; set; } = 100f;

    public Camera(string? name = "Camera") : base(null, null, name)
    {
        UpdateCamera(); // Compute initial position.
    }

    private void UpdateCamera()
    {
        // Degrees to radians.
        float yawRad = MathUtils.DegreesToRadians(_yaw);
        float pitchRad = MathUtils.DegreesToRadians(_pitch);

        // Compute the view direction (spherical to Cartesian; world Z-up, pitch raises Z).
        Vector3 direction = new Vector3(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad)
        );
        direction = Vector3.Normalize(direction);

        // Camera position = target - direction * distance.
        Position = _target - direction * _distance;
    }

    /// <summary>
    /// Gets the view matrix, which transforms world coordinates into camera space.
    /// World is Z-up, so the view up vector is +Z.
    /// </summary>
    public Matrix4x4 GetViewMatrix()
        => Matrix4x4.CreateLookAt(Position, _target, Vector3.UnitZ);

    /// <summary>
    /// Gets the perspective projection matrix, which transforms camera-space coordinates into clip space.
    /// </summary>
    public Matrix4x4 GetProjectionMatrix()
        => Matrix4x4.CreatePerspectiveFieldOfView(
            MathUtils.DegreesToRadians(Fov),
            AspectRatio,
            NearPlane,
            FarPlane);

    /// <summary>
    /// Unprojects screen pixel coordinates into a world-space ray (the entrance for picking).
    /// <paramref name="screenPositionPixels"/> has a top-left origin (X right, Y down), and
    /// <paramref name="viewportSizePixels"/> is the viewport size in pixels. The ray origin is the camera
    /// position and the direction points to that screen point's 3D line of sight. Usable with
    /// <see cref="SceneGraph.Pick"/>.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">A viewport axis is non-positive.</exception>
    /// <exception cref="System.InvalidOperationException">The view/projection matrix is not invertible.</exception>
    public Ray ScreenToWorldRay(Vector2 screenPositionPixels, Vector2 viewportSizePixels)
    {
        if (viewportSizePixels.X <= 0f || viewportSizePixels.Y <= 0f)
            throw new System.ArgumentOutOfRangeException(
                nameof(viewportSizePixels), viewportSizePixels, "Viewport size must be positive.");

        // Pixels (top-left origin) → NDC ([-1,1], Y up).
        float ndcX = screenPositionPixels.X / viewportSizePixels.X * 2f - 1f;
        float ndcY = 1f - screenPositionPixels.Y / viewportSizePixels.Y * 2f;

        // Unproject two NDC depths to world points; their difference is the view direction. Invert view*proj.
        Matrix4x4 viewProj = GetViewMatrix() * GetProjectionMatrix();
        bool invertible = Matrix4x4.Invert(viewProj, out Matrix4x4 invViewProj);
        if (!invertible)
            throw new System.InvalidOperationException("Camera view/projection matrix is not invertible; cannot unproject.");

        Vector3 near = DivideByW(Vector4.Transform(new Vector4(ndcX, ndcY, 0f, 1f), invViewProj));
        Vector3 far = DivideByW(Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invViewProj));

        return new Ray(Position, far - near);
    }

    private static Vector3 DivideByW(Vector4 v)
        => new(v.X / v.W, v.Y / v.W, v.Z / v.W);


    /// <summary>Rotates the view by yaw/pitch deltas (degrees).</summary>
    public void Rotate(float deltaYawDeg, float deltaPitchDeg)
    {
        Yaw += deltaYawDeg;
        Pitch += deltaPitchDeg;
    }

    /// <summary>Zooms the view (positive is closer, negative is farther).</summary>
    public void Zoom(float delta)
    {
        Distance -= delta;
    }

    /// <summary>Pans the target (screen-space delta: X right is positive, Y up is positive).</summary>
    public void Pan(Vector2 screenDelta)
    {
        Vector3 forward = Vector3.Normalize(Target - Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 up = Vector3.Cross(right, forward);

        const float speed = 0.05f;
        Vector3 offset = (right * -screenDelta.X + up * screenDelta.Y) * speed;

        _target += offset;
        UpdateCamera();
    }

    /// <summary>Resets to the initial state (target at origin, pitch 20°, yaw -90°, distance 5).</summary>
    public void Reset()
    {
        _yaw = -90f;
        _pitch = 20f;
        _distance = 5f;
        _target = Vector3.Zero;
        UpdateCamera();
    }
}
