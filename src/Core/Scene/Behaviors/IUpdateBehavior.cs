namespace RobotSimulation.Core.Scene;

/// <summary>
/// A per-frame update behavior attached to a <see cref="GameObject"/> — composition instead of inheritance.
/// Implementations run on the update thread and may only touch CPU data (never GL). Attach with
/// <see cref="GameObject.AddBehavior{T}"/> or <see cref="GameObject.AddUpdate(System.Action{GameObject,double})"/>;
/// <see cref="GameObject.Update"/> then dispatches every attached behavior once per frame, in attach order.
/// </summary>
public interface IUpdateBehavior
{
    /// <summary>
    /// Whether this behavior runs. The dispatcher skips disabled behaviors while they stay attached, so
    /// pausing does not require detaching and re-attaching.
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>
    /// Called once per frame by <see cref="GameObject.Update"/> (only while <see cref="Enabled"/> is true).
    /// </summary>
    /// <param name="owner">The node this behavior is attached to.</param>
    /// <param name="deltaTime">Seconds since the previous update.</param>
    void Update(GameObject owner, double deltaTime);
}