using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Core.GameObjects;

public class Transform
{
    public GameObject Owner { get; }
    public List<Transform> Children { get; } = new();
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;
    
    private Transform? _parent;
    public Transform? Parent
    {
        get => _parent;
        set
        {
            _parent?.Children.Remove(this);
            _parent = value;
            _parent?.Children.Add(this);
        }
    }
    
    public Transform(GameObject owner)
    {
        Owner = owner;
    }
    
    public Matrix4x4 GetLocalMatrix()
    {
        return Matrix4x4.CreateScale(Scale) *
               Matrix4x4.CreateFromQuaternion(Rotation) *
               Matrix4x4.CreateTranslation(Position);
    }

    public Matrix4x4 GetModelMatrix()
    {
        var local = GetLocalMatrix();
        if (Parent != null)
            return local * Parent.GetModelMatrix();
        return local;
    }
}