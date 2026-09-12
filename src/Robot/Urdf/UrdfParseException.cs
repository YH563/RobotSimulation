namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// Thrown when URDF parsing fails; the message includes element/attribute context where possible to
/// help locate the offending file.
/// </summary>
public sealed class UrdfParseException : Exception
{
    /// <summary>Creates the exception from a message describing what failed in the URDF.</summary>
    /// <param name="message">Problem description, ideally including element/attribute context.</param>
    public UrdfParseException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception from a message plus the underlying failure.</summary>
    /// <param name="message">Problem description, ideally including element/attribute context.</param>
    /// <param name="innerException">The cause (e.g. an XML or IO error).</param>
    public UrdfParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
