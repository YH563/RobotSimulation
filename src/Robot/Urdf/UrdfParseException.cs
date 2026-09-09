namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// Thrown when URDF parsing fails; the message includes element/attribute context where possible to
/// help locate the offending file.
/// </summary>
public sealed class UrdfParseException : Exception
{
    public UrdfParseException(string message) : base(message)
    {
    }

    public UrdfParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
