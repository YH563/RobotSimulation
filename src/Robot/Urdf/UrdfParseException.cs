namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// URDF 解析失败时抛出，消息尽量包含元素/属性上下文以便定位问题文件。
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
