using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 3D 场景中的轨道相机（场景特殊 GameObject）。
/// 使用球坐标系（Yaw/Pitch/Distance）围绕固定目标点控制观察位姿；
/// 自身维护轨道状态并推导 Position/View/Projection，可直接被鼠标输入驱动
/// （Rotate/Pan/Zoom/Reset），不依赖通用 Transform 定位。
/// </summary>
public class Camera : GameObject
{
    // 偏航角（度），即水平旋转角度，取值范围：任意浮点数，但通常保持在 [-360, 360]。
    private float _yaw = -90f;
    public float Yaw
    {
        get => _yaw;
        set { _yaw = value; UpdateCamera(); }
    }

    // 俯仰角（度），即垂直旋转角度。取值范围：[-89, 89]，防止相机翻转导致画面颠倒。
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

    // 相机与目标点的距离。
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

    // 观察目标点（世界坐标）。
    private Vector3 _target = Vector3.Zero;
    public Vector3 Target
    {
        get => _target;
        set { _target = value; UpdateCamera(); }
    }

    // 相机位置
    public Vector3 Position { get; private set; }

    // 视场角（度），默认 45°。控制透视投影的视野宽度。
    public float Fov { get; set; } = 45f;

    // 屏幕宽高比（Width/Height），用于投影矩阵计算。
    public float AspectRatio { get; set; } = 1.6f;

    // 近裁剪平面距离，默认为 0.1。
    public float NearPlane { get; set; } = 0.1f;

    // 远裁剪平面距离，默认为 100。
    public float FarPlane { get; set; } = 100f;

    public Camera(string? name = "Camera") : base(null, null, name)
    {
        UpdateCamera(); // 计算初始位置
    }

    private void UpdateCamera()
    {
        // 角度转弧度
        float yawRad = MathUtils.DegreesToRadians(_yaw);
        float pitchRad = MathUtils.DegreesToRadians(_pitch);

        // 计算视线方向向量（球坐标转直角坐标；世界 Z 朝上，pitch 抬升 Z）
        Vector3 direction = new Vector3(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad)
        );
        direction = Vector3.Normalize(direction);

        // 相机位置 = 目标点 - 方向 * 距离
        Position = _target - direction * _distance;
    }

    /// <summary>
    /// 获取视图矩阵（观察矩阵），用于将世界坐标转换到相机空间。
    /// 世界 Z 朝上，因此观察上方向为 +Z。
    /// </summary>
    public Matrix4x4 GetViewMatrix()
        => Matrix4x4.CreateLookAt(Position, _target, Vector3.UnitZ);

    /// <summary>
    /// 获取透视投影矩阵，用于将相机空间坐标转换到裁剪空间。
    /// </summary>
    public Matrix4x4 GetProjectionMatrix()
        => Matrix4x4.CreatePerspectiveFieldOfView(
            MathUtils.DegreesToRadians(Fov),
            AspectRatio,
            NearPlane,
            FarPlane);

    /// <summary>
    /// 把屏幕像素坐标反投影成世界空间射线（P0 拾取的入口）。<paramref name="screenPositionPixels"/> 的
    /// 原点在左上角（X 向右、Y 向下），<paramref name="viewportSizePixels"/> 为视口尺寸（像素）。
    /// 射线起点为相机位置，方向指向该屏幕点对应的三维视线。可配合 <see cref="SceneGraph.Pick"/> 使用。
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">视口尺寸任一轴非正。</exception>
    /// <exception cref="System.InvalidOperationException">视图/投影矩阵不可逆。</exception>
    public Ray ScreenToWorldRay(Vector2 screenPositionPixels, Vector2 viewportSizePixels)
    {
        if (viewportSizePixels.X <= 0f || viewportSizePixels.Y <= 0f)
            throw new System.ArgumentOutOfRangeException(
                nameof(viewportSizePixels), viewportSizePixels, "视口尺寸必须为正。");

        // 像素（左上原点）→ NDC（[-1,1]，Y 向上）
        float ndcX = screenPositionPixels.X / viewportSizePixels.X * 2f - 1f;
        float ndcY = 1f - screenPositionPixels.Y / viewportSizePixels.Y * 2f;

        // 用两个深度的 NDC 点反求世界点，差值即视线方向；逆矩阵取 view*proj 的组合。
        Matrix4x4 viewProj = GetViewMatrix() * GetProjectionMatrix();
        bool invertible = Matrix4x4.Invert(viewProj, out Matrix4x4 invViewProj);
        if (!invertible)
            throw new System.InvalidOperationException("相机视图/投影矩阵不可逆，无法反投影。");

        Vector3 near = DivideByW(Vector4.Transform(new Vector4(ndcX, ndcY, 0f, 1f), invViewProj));
        Vector3 far = DivideByW(Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invViewProj));

        return new Ray(Position, far - near);
    }

    private static Vector3 DivideByW(Vector4 v)
        => new(v.X / v.W, v.Y / v.W, v.Z / v.W);


    /// <summary>旋转视角（偏航/俯仰增量，度）。</summary>
    public void Rotate(float deltaYawDeg, float deltaPitchDeg)
    {
        Yaw += deltaYawDeg;
        Pitch += deltaPitchDeg;
    }

    /// <summary>缩放视角（正值为拉近，负值为拉远）。</summary>
    public void Zoom(float delta)
    {
        Distance -= delta;
    }

    /// <summary>平移目标点（屏幕空间位移：X 向右为正、Y 向上为正）。</summary>
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

    /// <summary>重置到初始状态（目标原点，仰角 20°，偏航 -90°，距离 5）。</summary>
    public void Reset()
    {
        _yaw = -90f;
        _pitch = 20f;
        _distance = 5f;
        _target = Vector3.Zero;
        UpdateCamera();
    }
}

