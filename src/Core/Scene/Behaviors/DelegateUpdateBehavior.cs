using System;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Adapts a delegate to <see cref="IUpdateBehavior"/>, so a lambda can be injected without declaring a
/// class. Created by <see cref="GameObject.AddUpdate(System.Action{GameObject,double})"/>, which returns
/// this instance so the caller can disable (<see cref="Enabled"/>) or remove it later.
/// </summary>
public sealed class DelegateUpdateBehavior : IUpdateBehavior
{
    private readonly Action<GameObject, double> _update;

    /// <summary>Wraps <paramref name="update"/> as an update behavior.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="update"/> is null.</exception>
    public DelegateUpdateBehavior(Action<GameObject, double> update)
        => _update = update ?? throw new ArgumentNullException(nameof(update));

    /// <summary>Whether this behavior runs (skipped by <see cref="GameObject.Update"/> when false).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Invokes the wrapped delegate with the owning node and the frame's delta time.</summary>
    public void Update(GameObject owner, double deltaTime) => _update(owner, deltaTime);
}