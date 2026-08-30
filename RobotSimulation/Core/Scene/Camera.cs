using System.Numerics;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 3D场景中的轨道相机，支持旋转、缩放、平移。
/// 使用球坐标系（Yaw/Pitch/Distance）控制观察位置，目标点固定。
/// </summary>
public class Camera
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
    
    // 相机与目标点的距离。取值范围：[0.5, 20]，防止穿模或无限远。
    private  float _distance = 5f;
    public float Distance
    {
        get => _distance;
        set 
        { 
            _distance = Math.Clamp(value, 0.5f, 20f); 
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
    
    // 近裁剪平面距离，默认为 0.1。小于该距离的物体不会被渲染。
    public float NearPlane { get; set; } = 0.1f;
    
    // 远裁剪平面距离，默认为 100。大于该距离的物体不会被渲染。
    public float FarPlane { get; set; } = 100f;
    
    public Camera()
    {
        UpdateCamera(); // 计算初始位置
    }
    
    private void UpdateCamera()
    {
        // 角度转弧度
        float yawRad = MathUtils.DegreesToRadians(_yaw);
        float pitchRad = MathUtils.DegreesToRadians(_pitch);

        // 计算视线方向向量（球坐标转直角坐标）
        Vector3 direction = new Vector3(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad)
        );
        direction = Vector3.Normalize(direction);

        // 相机位置 = 目标点 - 方向 * 距离
        Position = _target - direction * _distance;
    }
    
    /// <summary>
    /// 获取视图矩阵（观察矩阵），用于将世界坐标转换到相机空间。
    /// </summary>
    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, _target, Vector3.UnitY);
    }

    /// <summary>
    /// 获取透视投影矩阵，用于将相机空间坐标转换到裁剪空间。
    /// </summary>
    public Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(
            MathUtils.DegreesToRadians(Fov),
            AspectRatio,
            NearPlane,
            FarPlane
        );
    }
    
    /// <summary>
    /// 旋转相机（通过偏航角和俯仰角增量）。
    /// </summary>
    /// <param name="deltaYaw">偏航角增量（度）</param>
    /// <param name="deltaPitch">俯仰角增量（度）</param>
    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch += deltaPitch;
    }

    /// <summary>
    /// 缩放相机（改变距离）。
    /// </summary>
    /// <param name="delta">距离变化量（正值为拉近，负值为拉远）</param>
    public void Zoom(float delta)
    {
        Distance -= delta;
    }

    /// <summary>
    /// 平移相机（移动目标点，同时位置跟随移动）。
    /// </summary>
    /// <param name="delta">屏幕空间的平移量（X向右为正，Y向上为正）</param>
    public void Pan(Vector2 delta)
    {
        Vector3 forward = Vector3.Normalize(Target - Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);

        const float speed = 0.05f;
        Vector3 offset = (right * -delta.X + up * delta.Y) * speed;

        // 直接修改目标点并刷新位置
        _target += offset;
        UpdateCamera();
    }

    /// <summary>
    /// 将相机重置到初始状态（目标在原点，仰角20°，偏航-90°，距离5）。
    /// </summary>
    public void Reset()
    {
        _yaw = -90f;
        _pitch = 20f;
        _distance = 5f;
        _target = Vector3.Zero;
        UpdateCamera(); // 一次性刷新位置
    }
}