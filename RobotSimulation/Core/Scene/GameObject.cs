using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

public class GameObject
{
    public Transform Transform { get; }
    public Mesh? Mesh { get; set; }
    public Material? Material { get; set; }
    public bool Visible { get; set; } = true;
    
    public GameObject(Mesh? mesh = null, Material? material = null)
    {
        Transform = new Transform(this);
        Mesh = mesh;
        Material = material;
    }
    
    /// <summary>
    /// 单帧更新逻辑
    /// </summary>
    /// <param name="deltaTime"></param>
    public virtual void Update(double deltaTime) { }
}