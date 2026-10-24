using System.Numerics;
using RobotSimulation.Core.Scene;
using RobotSimulation.Robot;
using RobotSimulation.Robot.Collision;
using RobotSimulation.Robot.Description;

namespace RobotSimulation.Tests;

/// <summary>
/// URDF collision parsing and robot self-collision without a GL context. The robot tree is pure CPU
/// data, so both the parser and the collision model are exercised headless.
/// </summary>
public class UrdfCollisionTests
{
    [Fact]
    public void CollisionGeometryAndOriginAreParsed()
    {
        RobotModel robot = RobotModel.Parse("""
            <robot name="parser_test">
              <link name="base">
                <collision name="body">
                  <origin xyz="1 2 3" rpy="0 0 0"/>
                  <geometry><box size="1 2 3"/></geometry>
                </collision>
              </link>
            </robot>
            """);

        Link link = robot.Description.FindLink("base")!;
        CollisionElement collision = Assert.Single(link.CollisionElements);
        Assert.Equal("body", collision.Name);
        Assert.Equal(new Vector3(1f, 2f, 3f), collision.LocalTransform.Translation);
        Assert.IsType<BoxGeometry>(collision.Geometry);
        Assert.Single(robot.CollisionShapes);
    }

    [Fact]
    public void AdjacentLinksAreSkippedByDefault()
    {
        RobotModel robot = AdjacentOverlapRobot();

        Assert.Empty(robot.DetectSelfCollision());
        Assert.NotEmpty(robot.DetectSelfCollision(includeAdjacent: true));
    }

    [Fact]
    public void NonAdjacentLinksAreTested()
    {
        RobotModel robot = NonAdjacentOverlapRobot();
        IReadOnlyList<CollisionContact> contacts = robot.DetectSelfCollision();

        Assert.Contains(contacts, c =>
            (c.A.Name == "base" && c.B.Name == "tool") ||
            (c.A.Name == "tool" && c.B.Name == "base"));
    }

    [Fact]
    public void CollisionModelCanBeAccessedDirectly()
    {
        RobotModel robot = NonAdjacentOverlapRobot();
        RobotCollisionModel model = robot.CollisionModel;

        Assert.NotEmpty(model.DetectSelfCollision());
    }

    [Fact]
    public void DebugViewMountsFillsForEveryCollisionShape()
    {
        RobotModel robot = NonAdjacentOverlapRobot();
        using RobotCollisionDebugView view = robot.CollisionModel.CreateDebugView();

        Assert.True(view.NodeCount > 0);
        Assert.True(view.Visible);

        view.Visible = false;
        Assert.False(view.Visible);
    }

    [Fact]
    public void SelfCollisionFollowsJointDriving()
    {
        RobotModel robot = RobotModel.Parse("""
            <robot name="moving">
              <link name="base">
                <collision><geometry><box size="1 1 1"/></geometry></collision>
              </link>
              <link name="arm">
                <collision>
                  <origin xyz="0.8 0 0"/>
                  <geometry><box size="1 1 1"/></geometry>
                </collision>
              </link>
              <joint name="slide" type="prismatic">
                <parent link="base"/><child link="arm"/>
                <axis xyz="1 0 0"/>
              </joint>
            </robot>
            """);

        Assert.NotEmpty(robot.DetectSelfCollision(includeAdjacent: true));

        robot.SetJointValue("slide", 2f);
        Assert.Empty(robot.DetectSelfCollision(includeAdjacent: true));
    }

    private static RobotModel AdjacentOverlapRobot() => RobotModel.Parse("""
        <robot name="adjacent">
          <link name="base">
            <collision><geometry><box size="1 1 1"/></geometry></collision>
          </link>
          <link name="arm">
            <collision><geometry><box size="1 1 1"/></geometry></collision>
          </link>
          <joint name="j" type="fixed">
            <parent link="base"/><child link="arm"/>
          </joint>
        </robot>
        """);

    private static RobotModel NonAdjacentOverlapRobot() => RobotModel.Parse("""
        <robot name="non_adjacent">
          <link name="base">
            <collision><geometry><box size="1 1 1"/></geometry></collision>
          </link>
          <link name="arm">
            <collision>
              <origin xyz="3 0 0"/>
              <geometry><box size="0.1 0.1 0.1"/></geometry>
            </collision>
          </link>
          <link name="tool">
            <collision><geometry><box size="1 1 1"/></geometry></collision>
          </link>
          <joint name="base_arm" type="fixed">
            <parent link="base"/><child link="arm"/>
          </joint>
          <joint name="arm_tool" type="fixed">
            <parent link="arm"/><child link="tool"/>
          </joint>
        </robot>
        """);
}
