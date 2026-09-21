using System.Runtime.ExceptionServices;
using System.Threading;

using RobotSimulation.Core.Scene;

namespace RobotSimulation.Tests;

/// <summary>
/// The scene's threading contract, in tests that need no GL context, because the contract is about who may write
/// the lists — not about drawing them. One owner thread (fixed at construction, or handed over explicitly before
/// the frame loop starts); producers on any other thread only ever queue; the frame boundary is the single place
/// the lists change on their behalf.
/// </summary>
public class SceneGraphThreadingTests
{
    /// <summary>A node with no geometry: these tests are about the tree, never about drawing.</summary>
    private static GameObject Node(string name) => new(name: name);

    /// <summary>
    /// Runs <paramref name="action"/> on a second thread and waits for it. These tests are about <em>which</em>
    /// thread may do something, so the thread has to be an explicit one — a <c>Task</c> would do for the action
    /// itself but its continuation is free to land on any pool thread, which would break the very assumption under
    /// test (the thread that constructed the scene owns it).
    /// </summary>
    private static Exception? CaptureOnAnotherThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(action)) { IsBackground = true };
        thread.Start();
        thread.Join();
        return failure;
    }

    /// <summary>Same, but the action must succeed: a failure (including an assertion failure) is rethrown here, on the test thread, with its original stack.</summary>
    private static void OnAnotherThread(Action action)
    {
        if (CaptureOnAnotherThread(action) is { } failure)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void FrameBoundaryOnAnotherThreadThrowsInsteadOfTakingOwnership()
    {
        var scene = new SceneGraph();

        bool ownerOnOtherThread = true;
        Exception? failure = CaptureOnAnotherThread(() =>
        {
            ownerOnOtherThread = scene.IsOwnerThread;
            scene.ApplyPendingChanges();
        });

        Assert.False(ownerOnOtherThread);
        Assert.IsType<InvalidOperationException>(failure);

        // The wrong call must not have moved the lists to another thread: this thread is still the owner, and the
        // boundary on it still works.
        Assert.True(scene.IsOwnerThread);
        Assert.Equal(0, scene.ApplyPendingChanges());
    }

    [Fact]
    public void AddFromAnotherThreadIsQueuedAndAppliesAtTheFrameBoundary()
    {
        var scene = new SceneGraph();
        GameObject node = Node("queued");

        OnAnotherThread(() => scene.Add(node));

        // Queued, not applied: the producer never touched a list a running frame would be walking.
        Assert.Equal(1, scene.PendingChangeCount);
        Assert.DoesNotContain(node, scene.Roots);

        Assert.Equal(1, scene.ApplyPendingChanges());
        Assert.Contains(node, scene.Roots);
        Assert.Equal(0, scene.PendingChangeCount);
    }

    [Fact]
    public void RemoveFromAnotherThreadIsQueuedAndAppliesAtTheFrameBoundary()
    {
        var scene = new SceneGraph();
        GameObject node = Node("removed");
        scene.Add(node);

        OnAnotherThread(() => scene.Remove(node));

        Assert.Equal(1, scene.PendingChangeCount);
        Assert.Contains(node, scene.Roots);

        Assert.Equal(1, scene.ApplyPendingChanges());
        Assert.DoesNotContain(node, scene.Roots);
    }

    [Fact]
    public void QueueStopsAtMaxPendingChangesAndCountsTheDrops()
    {
        var scene = new SceneGraph { MaxPendingChanges = 3 };

        OnAnotherThread(() =>
        {
            for (int i = 0; i < 10; i++)
                scene.Add(Node($"queued-{i}"));
        });

        // The cap drops instead of growing without bound — and says so through the counter, which is the point of
        // having one: misuse has to be visible, not silently absorbed.
        Assert.Equal(3, scene.PendingChangeCount);
        Assert.Equal(7, scene.DroppedPendingChangeCount);

        Assert.Equal(3, scene.ApplyPendingChanges());
        Assert.Equal(0, scene.PendingChangeCount);
        Assert.Equal(7, scene.DroppedPendingChangeCount);
    }

    [Fact]
    public void MaxPendingChangesRejectsNegativeValues()
    {
        var scene = new SceneGraph();

        Assert.Throws<ArgumentOutOfRangeException>(() => scene.MaxPendingChanges = -1);
        scene.MaxPendingChanges = 0;   // 0 is the documented "unbounded" setting.
        Assert.Equal(0, scene.MaxPendingChanges);
    }

    [Fact]
    public void ClaimOwnershipHandsTheSceneOverBeforeTheFrameLoopStarts()
    {
        var scene = new SceneGraph();
        GameObject node = Node("handed-over");

        OnAnotherThread(() =>
        {
            scene.ClaimOwnership();
            Assert.True(scene.IsOwnerThread);

            // On the new owner thread Add applies immediately: no frame boundary, nothing queued.
            scene.Add(node);
            Assert.Equal(0, scene.PendingChangeCount);
        });

        Assert.Contains(node, scene.Roots);
        Assert.False(scene.IsOwnerThread);   // the thread that constructed the scene is a producer now
    }

    [Fact]
    public void ClaimOwnershipAfterAFrameBoundaryThrows()
    {
        var scene = new SceneGraph();
        scene.ApplyPendingChanges();   // the frame loop has started on this thread

        Exception? failure = CaptureOnAnotherThread(scene.ClaimOwnership);

        Assert.IsType<InvalidOperationException>(failure);
        Assert.True(scene.IsOwnerThread);   // and the scene did not change hands
    }

    [Fact]
    public void ANodeThatGainsAParentLeavesTheRootList()
    {
        var scene = new SceneGraph();
        GameObject parent = Node("parent");
        GameObject child = Node("child");
        scene.Add(parent);
        scene.Add(child);
        Assert.Contains(child, scene.Roots);

        child.Transform.Parent = parent.Transform;

        // Roots holds "added and parentless": the child is reached through its parent now, and holding it in both
        // places would draw, pick and measure it twice.
        Assert.DoesNotContain(child, scene.Roots);
        Assert.Contains(child.Transform, parent.Transform.Children);

        // Which is also why one Remove is enough, whichever state the node is in.
        scene.Remove(parent);
        Assert.DoesNotContain(parent, scene.Roots);
        Assert.DoesNotContain(child, scene.Roots);
        Assert.Null(parent.Transform.Parent);
    }

    [Fact]
    public void ReparentFromAnotherThreadIsQueuedAndAppliesAtTheFrameBoundary()
    {
        var scene = new SceneGraph();
        GameObject parent = Node("parent");
        GameObject child = Node("child");
        scene.Add(parent);

        OnAnotherThread(() => child.Transform.Parent = parent.Transform);

        // The scene owns the child lists, so a re-parent goes through its frame boundary like Add/Remove do — a
        // renderer walking the parent's children never meets an insert from another thread.
        Assert.Null(child.Transform.Parent);
        Assert.Equal(1, scene.PendingChangeCount);

        Assert.Equal(1, scene.ApplyPendingChanges());
        Assert.Same(parent.Transform, child.Transform.Parent);
        Assert.Contains(child.Transform, parent.Transform.Children);
    }

    [Fact]
    public void ProducersOnOtherThreadsCannotDisturbAFrameLoop()
    {
        const int MinimumFrames = 50;
        const int ProducerIterations = 2000;

        var scene = new SceneGraph();
        GameObject root = Node("root");
        scene.Add(root);

        var producerDone = 0;
        var producer = new Thread(() =>
        {
            for (int i = 0; i < ProducerIterations; i++)
            {
                GameObject node = Node($"producer-{i}");
                scene.Add(node);                          // queued
                node.Transform.Parent = root.Transform;   // queued (the node is not in a scene yet, the parent is)
                scene.Remove(node);                       // queued
            }

            Volatile.Write(ref producerDone, 1);
        }) { IsBackground = true };

        producer.Start();

        // A host's frame loop, with a producer queueing structure as fast as it can: a boundary plus the walk a
        // renderer does. Nothing here may throw — above all not "Collection was modified", which is exactly what
        // the routing exists to make impossible.
        for (int frame = 0; frame < MinimumFrames || Volatile.Read(ref producerDone) == 0; frame++)
        {
            scene.Update(1.0 / 60.0);
            foreach (GameObject node in scene.Roots)
                _ = node.Transform.GetModelMatrix();
        }

        producer.Join();
        scene.ApplyPendingChanges();   // ends from a drained queue, not from a half-applied one

        Assert.Equal(0, scene.PendingChangeCount);
        Assert.Equal(0, scene.DroppedPendingChangeCount);
    }
}
