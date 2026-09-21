using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Scene-graph container: holds the scene's root object tree, the active camera, a set of lights, and
/// the scene's display defaults (background/ambient light/grid floor). The settings are exposed as
/// read-write properties on this class plus default assembly methods — there is no separate
/// "settings/defaults" class; the out-of-the-box values are the defaults below.
/// <para>
/// Threading — one owner, and never a second one. The scene belongs to the thread that constructed it, or to
/// whichever thread took it over explicitly through <see cref="ClaimOwnership"/> before the first frame
/// boundary (the hand-over a host needs when it builds the scene in one place and runs the frame loop in
/// another). Ownership never changes on its own: only the owner thread may walk <see cref="Roots"/> /
/// <see cref="Lights"/> (<see cref="Pick"/>, <see cref="Update"/>, rendering), so "who may write" has to be one
/// answer for a whole frame — a call that turns whoever made it into an owner would put a second writer next to
/// the thread walking the lists. <see cref="Add"/> / <see cref="Remove"/> and re-parenting through
/// <see cref="Transform.Parent"/> may be called from any thread: on the owner thread they take effect
/// immediately, on any other thread they are queued and applied at the next frame boundary. That is what makes a
/// scene that keeps rendering while objects arrive safe — a producer never edits a list the owner may be
/// walking, and there is no live view to corrupt.
/// </para>
/// </summary>
public class SceneGraph : IDisposable
{
    private readonly List<GameObject> _roots = new();
    private readonly List<Light> _lights = new();

    /// <summary>Structural changes requested by a thread that does not own the lists; drained at a frame boundary.</summary>
    private readonly ConcurrentQueue<PendingChange> _pending = new();

    /// <summary>
    /// How many changes are sitting in <see cref="_pending"/>. Kept by hand with atomic add/subtract instead of
    /// reading <c>ConcurrentQueue&lt;T&gt;.Count</c>, because two hot paths need the number in O(1): the
    /// producer's cap check (<see cref="MaxPendingChanges"/>) and the frame boundary's "is there anything to
    /// do?" test. It is exact (every successful enqueue adds one, every successful dequeue takes one back), so
    /// <see cref="PendingChangeCount"/> stays a true count.
    /// </summary>
    private int _pendingCount;

    /// <summary>Guards both lists against a second thread draining the queue while the owner thread edits them.</summary>
    private readonly object _structureGate = new();

    /// <summary>
    /// Thread allowed to touch <see cref="_roots"/> / <see cref="_lights"/> directly: the constructing thread,
    /// or whichever thread took the scene over through <see cref="ClaimOwnership"/> before the first frame
    /// boundary. Nothing else ever writes it — in particular, running a frame boundary does <em>not</em> claim
    /// ownership, so a stray call on some other thread cannot silently become a second writer.
    /// </summary>
    private int _ownerThreadId = Environment.CurrentManagedThreadId;

    /// <summary>
    /// Whether a frame boundary has already run on the owner thread (guarded by <see cref="_structureGate"/>).
    /// Once it has, ownership is frozen: <see cref="ClaimOwnership"/> refuses to move it, because the current
    /// owner may be in the middle of walking the lists.
    /// </summary>
    private bool _driven;

    /// <summary>Structural version, published through <see cref="Version"/>.</summary>
    private long _version;

    /// <summary>Upper bound requested through <see cref="MaxPendingChanges"/> (0 = unbounded).</summary>
    private int _maxPendingChanges = DefaultMaxPendingChanges;

    /// <summary>Changes refused because the queue was full, published through <see cref="DroppedPendingChangeCount"/>.</summary>
    private long _droppedPendingChanges;

    /// <summary>Set once the over-limit message has been logged, so the producer path says it once instead of per change.</summary>
    private int _overflowWarningLogged;

    /// <summary>Set once a re-parent was dropped because it would have closed a cycle by the time it was applied.</summary>
    private int _cycleWarningLogged;

    /// <summary>
    /// Root nodes of the scene (for renderer traversal and external read-only access). Walk it on the
    /// scene's owner thread — the thread that renders. The invariant is "added <em>and</em> parentless": an
    /// object added from another thread is not here yet (it joins at the next frame boundary, see
    /// <see cref="ApplyPendingChanges"/> and <see cref="PendingChangeCount"/>), and an object that later gains a
    /// parent leaves this list again — it is reached through its parent then, and holding it here as well would
    /// draw, pick and measure it twice.
    /// </summary>
    public IReadOnlyList<GameObject> Roots => _roots;

    /// <summary>
    /// How many structural changes other threads have queued and not applied yet. Diagnostics: a value that
    /// keeps climbing means the frame boundary (<see cref="Update"/> or the renderer) is not running, so
    /// nobody is draining the queue — and once it reaches <see cref="MaxPendingChanges"/> the changes are
    /// dropped and counted in <see cref="DroppedPendingChangeCount"/> instead. It is 0 right after a frame
    /// boundary on the owner thread.
    /// </summary>
    public int PendingChangeCount => Volatile.Read(ref _pendingCount);

    /// <summary>
    /// Monotonic count of the structural changes applied to this scene (default assembly, <see cref="Add"/>,
    /// <see cref="Remove"/>, a re-parent applied through <see cref="Transform.Parent"/>; a drain counts once per
    /// change). Lock-free to read, so a host can use it to invalidate whatever it derived from
    /// <see cref="Roots"/> — only a change of this value is meaningful, not its starting point.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Whether the calling thread is the scene's owner thread: the one allowed to walk <see cref="Roots"/> /
    /// <see cref="Lights"/> and to run <see cref="Update"/> / <see cref="ApplyPendingChanges"/>, and the one on
    /// which <see cref="Add"/> / <see cref="Remove"/> apply immediately instead of queueing. A host can check a
    /// thread it is about to drive the scene from here, instead of finding out from the exception the boundary
    /// throws (see <see cref="ApplyPendingChanges"/>).
    /// </summary>
    public bool IsOwnerThread => Environment.CurrentManagedThreadId == Volatile.Read(ref _ownerThreadId);

    /// <summary>Default of <see cref="MaxPendingChanges"/>: far above any sane burst of queued structural changes, low enough that a runaway producer is caught while the scene still works.</summary>
    public const int DefaultMaxPendingChanges = 65536;

    /// <summary>
    /// How many structural changes producer threads may queue before further ones are dropped with one warning
    /// (0 disables the limit). This is <em>not</em> a performance knob — the queue is lock-free and draining a
    /// few thousand entries at a frame boundary costs nothing next to drawing a frame. It exists so that misuse
    /// is visible. A producer that queues structure at data rate (a hot loop that calls <see cref="Add"/> /
    /// <see cref="Remove"/> once per sample, for something that should have been a state or stream hand-off
    /// instead) climbs without bound whenever the frame boundary is slower than the producer, and a scene that
    /// grows silently until memory runs out is far harder to diagnose than one that says so. Dropped changes
    /// are counted in <see cref="DroppedPendingChangeCount"/>; the scene keeps what it had, so a producer hitting
    /// the limit never takes the frame loop down.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public int MaxPendingChanges
    {
        get => Volatile.Read(ref _maxPendingChanges);
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "MaxPendingChanges cannot be negative (0 means unbounded).");
            Volatile.Write(ref _maxPendingChanges, value);
        }
    }

    /// <summary>
    /// How many structural changes were refused because <see cref="MaxPendingChanges"/> was reached. Diagnostics,
    /// and the reason the cap is not silent: it is 0 in healthy use, and non-zero means a producer is outrunning
    /// the frame boundary (see <see cref="MaxPendingChanges"/> for why that is worth failing loudly about).
    /// </summary>
    public long DroppedPendingChangeCount => Interlocked.Read(ref _droppedPendingChanges);

    /// <summary>The scene's active camera (a built-in orbit camera by default; can be replaced entirely).</summary>
    public Camera Camera { get; set; } = new Camera();

    // ---- Scene display defaults (host/UI reads and writes these properties directly) ----

    /// <summary>Clear/background color (medium gray, the default "sky" base).</summary>
    public Vector4 BackgroundColor { get; set; } = new(0.20f, 0.22f, 0.25f, 1f);

    /// <summary>Ambient light (RGB intensity, 0-1). Provides the floor for shadowed faces so they do not go black.</summary>
    public Vector3 AmbientColor { get; set; } = new(0.30f, 0.32f, 0.36f);

    /// <summary>Whether to show the default grid floor (<see cref="AddDefaultGrid"/> creates it based on this).</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Whether to show the default world-origin axes (<see cref="AddDefaultWorldAxes"/> creates it based on
    /// this). Off by default: the world origin is not where a robot's interesting frames are, the set is a
    /// plain scene object that the model occludes, and the screen-space orientation gizmo already answers
    /// "which way is X/Y/Z" without occupying the scene. Turn it on — and size it with
    /// <see cref="WorldAxesLength"/> or <see cref="FitWorldAxesToContent"/> so it stands out past the model —
    /// when a world-scale ruler is what the scene needs.
    /// </summary>
    public bool ShowWorldAxes { get; set; } = false;

    /// <summary>
    /// World length of the default world-origin axes: used when <see cref="AddDefaultWorldAxes"/> creates them
    /// and re-applied to the set already in the scene by <see cref="FitWorldAxesToContent"/>. The default axes
    /// are <see cref="AxesSizing.FixedWorldLength"/>, so this is a length in meters, not a screen size.
    /// </summary>
    public float WorldAxesLength { get; set; } = 0.5f;

    /// <summary>
    /// Whether the renderer draws the screen-space orientation gizmo: a small axes set pinned to the
    /// viewport's bottom-right corner that follows the camera's orientation, like a 3D engine's view widget.
    /// It is drawn last, into its own small viewport, so it is never occluded by scene geometry, it keeps its
    /// pixel size independent of the camera's zoom (its square follows the viewport's shorter side), and — being
    /// no scene node — it can never take part in picking.
    /// </summary>
    public bool ShowOrientationGizmo { get; set; } = true;

    /// <summary>
    /// Gap (pixels) between the orientation gizmo and the viewport's bottom/right edges. Only this gap is a scene
    /// setting: the marker's own size belongs to the renderer, which takes it as a fixed fraction of the viewport's
    /// shorter side (see <c>Renderer.GizmoSizeRatio</c>), so the widget keeps the same visual weight in any window
    /// instead of being an absolute pixel square.
    /// </summary>
    public float OrientationGizmoMargin { get; set; } = 16f;

    /// <summary>
    /// Whether the selected object shows its own local axes (<see cref="GameObject.ShowLocalAxes"/>): a small marker
    /// at that node's origin, tilted with its frame. A highlight tells the user *what* was picked; the axes tell them
    /// which way that node's X/Y/Z point — the same question the corner gizmo answers for the world. On by default,
    /// so a host that only calls <see cref="Select"/> gets both.
    /// <para>
    /// Display only, never a handle: the marker is a plain child node with no drag affordance, and nothing in this
    /// class writes back to a node's transform. Reference frames can be read with no chance of an accidental edit —
    /// which is what an articulated model needs, because a link's pose belongs to its joint chain, not to a widget.
    /// </para>
    /// </summary>
    public bool ShowSelectionAxes { get; set; } = true;

    /// <summary>
    /// Length (meters) of the axes mounted on the selected object. It is read when they are mounted
    /// (<see cref="Select"/>), so a change applies to the next selection rather than the current one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public float SelectionAxesLength
    {
        get => _selectionAxesLength;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Selection axes length must be a positive finite value.");
            _selectionAxesLength = value;
        }
    }

    private float _selectionAxesLength = 0.3f;

    /// <summary>
    /// The currently selected object (null when nothing is selected). Read-only: selection changes go through
    /// <see cref="Select"/> / <see cref="PickAndSelect"/>, which keep the highlight and the mounted axes in step.
    /// </summary>
    public GameObject? Selected { get; private set; }

    /// <summary>Cell edge length (meters).</summary>
    public float GridCellSize { get; set; } = 1f;

    /// <summary>Cell count from the center in each direction (total span = 2 × GridCellCount × GridCellSize).</summary>
    public int GridCellCount { get; set; } = 10;

    /// <summary>Grid line color.</summary>
    public Vector4 GridColor { get; set; } = new(0.30f, 0.30f, 0.35f, 1f);

    /// <summary>All lights in the scene (collected automatically by <see cref="Add"/>).</summary>
    public IReadOnlyList<Light> Lights => _lights;

    /// <summary>Whether <see cref="Dispose"/> has dropped the lists; read from producer threads, hence volatile.</summary>
    private volatile bool _disposed;

    /// <summary>
    /// Whether the "this scene is disposed" warning has already been logged, so a producer thread that
    /// arrives late cannot turn teardown into a log flood.
    /// </summary>
    private bool _disposedWarningLogged;

    /// <summary>Whether <see cref="Select"/> mounted the current selection's axes, so only those are unmounted again.</summary>
    private bool _selectionAxesMounted;

    /// <summary>
    /// Construction completes the default scene assembly: default camera pose, default lights, grid floor,
    /// and the world-origin axes when <see cref="ShowWorldAxes"/> is on (it is off by default — the renderer's
    /// orientation gizmo covers the "which way is up" question without putting geometry in the scene). The
    /// host does not need to call these manually (it only adds its own content, such as a URDF robot or demo
    /// objects).
    /// </summary>
    public SceneGraph()
    {
        ApplyDefaultCamera();
        AddDefaultLights();
        AddDefaultGrid();
        AddDefaultWorldAxes();
    }

    /// <summary>
    /// Adds an object (auto-detecting whether it is a root node). If <paramref name="obj"/> is a
    /// <see cref="Light"/> and not already registered, it is also added to <see cref="Lights"/>.
    /// <para>
    /// Any thread may call this. On the scene's owner thread — the thread that constructed the scene, or the one
    /// that took it over through <see cref="ClaimOwnership"/> before the frame loop started — the object joins
    /// <see cref="Roots"/> right away. On any other thread it is queued and joins at the next frame
    /// boundary, which is what keeps "keep building the scene while it renders" safe; the object is then
    /// built, drawn and picked normally, because the renderer creates a node's GPU resources lazily on the
    /// first frame it sees it and walks <see cref="Roots"/> again on every frame.
    /// </para>
    /// <para>
    /// An object that has a parent is not a root: adding one that is already attached to a node of this scene
    /// registers nothing new (it is drawn through that node), and a parentless object that is re-parented later
    /// leaves <see cref="Roots"/> again — so <see cref="Remove"/> and <see cref="Transform.Parent"/> can never
    /// leave the same node in the scene twice.
    /// </para>
    /// </summary>
    /// <param name="obj">The object to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
    public void Add(GameObject? obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        if (!IsOwnerThread)
        {
            Queue(PendingChange.Add(obj));
            return;
        }

        lock (_structureGate)
        {
            if (ReportIfDisposed())
                return;

            AddImmediate(obj);
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Removes a game object, detaching it from its parent or the root list; if it is a <see cref="Light"/>
    /// it is also removed from the lights list. Both places are tried, not whichever one the node's parent
    /// points at: a node that is in <see cref="Roots"/> is taken out of it even if it has meanwhile been
    /// re-parented, so removing is idempotent and a node can never be "gone only after a second Remove".
    /// A node that leaves the scene cannot stay selected: removing the selected node — or an ancestor of it,
    /// which takes its whole subtree out with it — drops the selection's highlight and mounted axes.
    /// <para>
    /// Threading follows <see cref="Add"/>: immediate on the scene's owner thread, queued for any other
    /// thread and applied at the next frame boundary. Passing null does nothing.
    /// </para>
    /// </summary>
    /// <param name="obj">The object to remove, or null (a no-op).</param>
    public void Remove(GameObject? obj)
    {
        if (obj == null) return;

        if (!IsOwnerThread)
        {
            Queue(PendingChange.Remove(obj));
            return;
        }

        lock (_structureGate)
        {
            if (ReportIfDisposed())
                return;

            RemoveImmediate(obj);
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Applies the structural changes other threads queued through <see cref="Add"/> / <see cref="Remove"/>
    /// (and the re-parents requested through <see cref="Transform.Parent"/>). This is the scene's frame
    /// boundary, and the whole reason a producer thread cannot disturb a running frame: producers only enqueue,
    /// the lists are edited here and nowhere else, so no traversal can ever see a list being modified.
    /// <para>
    /// The renderer calls it for you at the start of
    /// <see cref="RobotSimulation.Core.Rendering.IRenderer.Render"/>, and <see cref="Update"/> calls it
    /// before it recurses — so a host that owns the usual frame loop needs no extra call, and gets two calls
    /// per frame without paying for the second: an empty queue returns before the lock is taken. A host that
    /// renders through some other path, or drives no frame loop at all, can call it itself: it is the one
    /// place that hands pending work to the scene.
    /// </para>
    /// <para>
    /// It runs on the <em>owner</em> thread and no longer takes ownership: a call from any other thread throws
    /// instead of quietly moving the lists to a thread that may not be the one walking them, which is what made
    /// "who edits the lists" depend on which thread happened to call last. A host whose frame loop lives on
    /// another thread hands the scene over once with <see cref="ClaimOwnership"/>, before the loop starts.
    /// After <see cref="Dispose"/> the queue is drained without applying anything, because a frame boundary
    /// must not throw when a producer arrived late.
    /// </para>
    /// </summary>
    /// <returns>How many changes were applied (0 when nothing was queued).</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not the scene's owner thread (see <see cref="ClaimOwnership"/>).</exception>
    public int ApplyPendingChanges()
    {
        AssertOwnerThread(nameof(ApplyPendingChanges));

        // Cheap re-entry: the frame's second boundary (Update after Render, or the other way round) finds nothing
        // waiting and leaves without touching the lock. `_driven` is only ever set inside it, so this cannot skip
        // the first boundary — nor the ownership bookkeeping that goes with it.
        if (Volatile.Read(ref _driven) && Volatile.Read(ref _pendingCount) == 0)
            return 0;

        lock (_structureGate)
        {
            _driven = true;

            int applied = 0;
            while (_pending.TryDequeue(out PendingChange change))
            {
                Interlocked.Decrement(ref _pendingCount);

                // Dropped rather than applied once the lists are gone: an Add here would resurrect a root
                // list Dispose() has already cleared.
                if (_disposed)
                    continue;

                Apply(change);
                applied++;
            }

            if (applied > 0)
                Interlocked.Add(ref _version, applied);

            return applied;
        }
    }

    /// <summary>Applies one queued change; the caller holds <see cref="_structureGate"/> on the owner thread.</summary>
    private void Apply(PendingChange change)
    {
        switch (change.Kind)
        {
            case PendingChangeKind.Add:
                AddImmediate(change.Object);
                break;

            case PendingChangeKind.Remove:
                RemoveImmediate(change.Object);
                break;

            default:
                AttachImmediate(change.Object, change.Parent);
                break;
        }
    }

    /// <summary>
    /// Takes the scene over for the calling thread: after it, that thread is the owner — its <see cref="Add"/> /
    /// <see cref="Remove"/> calls apply immediately, and <see cref="ApplyPendingChanges"/> / <see cref="Update"/>
    /// are allowed on it. This is for a host that builds the scene in one place and runs the frame loop on
    /// another thread; a host that constructs the scene on (or inside) its frame loop never needs it.
    /// <para>
    /// Call it on the thread that will run the frame loop, <em>before</em> the loop starts and before any other
    /// thread renders or updates the scene. It is one hand-over in one direction: once a frame boundary has run,
    /// ownership is frozen and a late call throws, because the current owner may be in the middle of walking the
    /// lists and a second owner would be editing them underneath it. Calling it on the owner thread is a no-op,
    /// so a host can call it defensively.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">A frame boundary has already run on another thread, so the scene can no longer be handed over.</exception>
    /// <exception cref="ObjectDisposedException">The scene is disposed.</exception>
    public void ClaimOwnership()
    {
        lock (_structureGate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SceneGraph),
                    "The scene is disposed: it no longer holds a node list to own.");

            if (IsOwnerThread)
                return;

            if (_driven)
                throw new InvalidOperationException(
                    $"SceneGraph: ownership cannot move to thread {Environment.CurrentManagedThreadId} — thread " +
                    $"{Volatile.Read(ref _ownerThreadId)} has already run a frame boundary on this scene, so the " +
                    $"lists are already being walked there. Hand the scene over with {nameof(ClaimOwnership)}() " +
                    "before the frame loop starts (the thread that constructs a scene owns it by default).");

            Volatile.Write(ref _ownerThreadId, Environment.CurrentManagedThreadId);
        }
    }

    /// <summary>
    /// Throws unless the calling thread is the scene's owner thread. The frame boundary belongs to the owner
    /// because that is the thread allowed to walk <see cref="Roots"/> / <see cref="Lights"/>, and its two jobs are
    /// one job: apply what producers queued, then traverse. A boundary on any other thread would mean two threads
    /// believing they may edit the lists. Failing loudly here — at the call that is wrong, once — is what keeps
    /// that from turning into the intermittent "Collection was modified" that only shows up in production.
    /// </summary>
    private void AssertOwnerThread(string member)
    {
        if (IsOwnerThread)
            return;

        throw new InvalidOperationException(
            $"SceneGraph.{member}() must run on the scene's owner thread (thread {Volatile.Read(ref _ownerThreadId)}), " +
            $"but was called on thread {Environment.CurrentManagedThreadId}. The frame boundary belongs to the owner " +
            "thread — the one that walks Roots/Lights and renders. A host whose frame loop lives on another thread " +
            $"calls {nameof(ClaimOwnership)}() on it once, before the loop starts.");
    }

    /// <summary>
    /// Reports — once, and without throwing — that the scene is disposed, so the change is ignored. A
    /// producer that arrives after teardown is a bug worth saying out loud, but throwing on someone else's
    /// thread would take the process down with it. On the owner thread callers hold
    /// <see cref="_structureGate"/>; producer threads call it without it (they must not take a lock the owner
    /// holds while walking the lists), and the only field it writes is the warning flag, so the worst a race can
    /// do is print the line twice.
    /// </summary>
    /// <returns>Whether the scene is disposed, in which case the caller skips the change.</returns>
    private bool ReportIfDisposed()
    {
        if (!_disposed)
            return false;

        if (!_disposedWarningLogged)
        {
            _disposedWarningLogged = true;
            Logger.Warning("SceneGraph.Add/Remove ignored: the scene is disposed and holds no node list any more.");
        }

        return true;
    }

    /// <summary>
    /// Queues a structural change made by a thread that is not the owner — the producer path of
    /// <see cref="Add"/> / <see cref="Remove"/>, and of a <see cref="Transform.Parent"/> assignment on a node
    /// that already belongs to this scene. Honours <see cref="MaxPendingChanges"/>: past the limit the change is
    /// dropped and counted instead of growing the queue without bound, because a producer that outruns the frame
    /// boundary by that much is a bug the host has to see, not a backlog to hold.
    /// </summary>
    private void Queue(PendingChange change)
    {
        if (ReportIfDisposed())
            return;

        int max = MaxPendingChanges;
        if (max > 0 && Volatile.Read(ref _pendingCount) >= max)
        {
            Interlocked.Increment(ref _droppedPendingChanges);
            WarnQueueOverflow(max);
            return;
        }

        _pending.Enqueue(change);
        Interlocked.Increment(ref _pendingCount);
    }

    /// <summary>Logs the over-limit condition once: the producer path must not flood the log on its way to the limit.</summary>
    private void WarnQueueOverflow(int max)
    {
        if (Interlocked.Exchange(ref _overflowWarningLogged, 1) != 0)
            return;

        Logger.Warning(
            $"SceneGraph: MaxPendingChanges ({max}) reached, structural changes from other threads are now being " +
            $"dropped (PendingChangeCount {Volatile.Read(ref _pendingCount)}, dropped " +
            $"{Interlocked.Read(ref _droppedPendingChanges)} so far). The frame boundary is not draining the queue: " +
            "either nothing is calling Update()/Render() on the owner thread, or a producer is queueing far more " +
            "structure per frame than the scene can consume. Structural changes are for real structure; per-sample " +
            "data belongs in a state or stream hand-off (poses, joint values), not in one Add/Remove per sample.");
    }

    /// <summary>Adds to the lists; the caller holds <see cref="_structureGate"/> on the owner thread.</summary>
    private void AddImmediate(GameObject obj)
    {
        if (obj.Transform.Parent == null)
        {
            // Adding the same root twice is a no-op: a duplicated entry would be drawn, picked and measured twice
            // while Remove() — like the light list below — only takes one copy back out.
            if (!_roots.Contains(obj))
                _roots.Add(obj);

            // Roots holds the *parentless* added nodes, and this registration is what lets a later re-parent find
            // the scene it has to route itself through (GameObject.RegisteredScene, Transform.FindScene).
            obj.RegisteredScene = this;
        }

        if (obj is Light light && !_lights.Contains(light))
            _lights.Add(light);
    }

    /// <summary>Removes from the lists; the caller holds <see cref="_structureGate"/> on the owner thread.</summary>
    private void RemoveImmediate(GameObject obj)
    {
        if (obj is Light light)
            _lights.Remove(light);

        // Both places are tried, not whichever one the node's parent points at. A node can be in the root list
        // *and* have been re-parented since it was added, and taking only the branch its Parent points at used to
        // leave it in Roots — drawn, picked and measured although it was removed, and gone only after a second
        // Remove(). With this, removing is idempotent whatever state the node is in.
        if (_roots.Remove(obj))
            obj.RegisteredScene = null;

        if (obj.Transform.Parent is not null)
        {
            // Auto-detaches it from its parent's Children — and with it the whole subtree — so a detached node is
            // not reachable from Roots any more. SetParentDirect, not the property: this *is* the scene applying
            // the change, and a routed set would ask the scene to apply it again.
            obj.Transform.SetParentDirect(null);
        }

        // Selection is feedback about the scene, so it cannot outlive what it points at: removing the
        // selected node — or an ancestor that takes it out with the subtree — clears the selection
        // (highlight plus the axes Select mounted) instead of leaving it on a node the scene no longer holds.
        if (Selected is { } selected && !IsInScene(selected))
            Select(null);
    }

    /// <summary>
    /// Entry point for <see cref="Transform.Parent"/> on a node that belongs to this scene: the re-parent is
    /// applied here on the owner thread, or queued on any other thread, instead of letting the property relink a
    /// child list the owner may be walking.
    /// </summary>
    internal void ApplyParentChange(Transform child, Transform? newParent)
    {
        if (!IsOwnerThread)
        {
            Queue(PendingChange.Attach(child.Owner, newParent));
            return;
        }

        lock (_structureGate)
        {
            if (ReportIfDisposed())
                return;

            AttachImmediate(child.Owner, newParent);
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Applies a re-parent on the owner thread (the caller holds <see cref="_structureGate"/>): relinks the tree
    /// and keeps <see cref="Roots"/> / <see cref="Lights"/> in step with it, so a node is never reachable both as
    /// a root and through a parent.
    /// </summary>
    private void AttachImmediate(GameObject obj, Transform? newParent)
    {
        Transform transform = obj.Transform;

        // A node that was a scene root and now has a parent is reached through that parent: it leaves Roots, and
        // the other half of the "Remove only works twice" state cannot happen either.
        if (newParent is not null && obj.RegisteredScene is not null)
        {
            obj.RegisteredScene = null;
            _roots.Remove(obj);

            // A light that left the scene must stop feeding its parameters to the shader. It stays registered when
            // it merely moved inside the scene (under another node of it): it is still drawn there.
            if (obj is Light light && newParent.FindScene() != this)
                _lights.Remove(light);
        }

        if (newParent is not null && transform.IsInSubtreeOf(newParent))
        {
            // The tree changed between the request and this frame boundary — on the owner thread, which is allowed
            // to do that — so the re-parent would close a cycle now. It is dropped: the request came from another
            // thread, and throwing here would take the frame loop down over a request that was valid when made.
            WarnDroppedAttach();
            return;
        }

        transform.SetParentDirect(newParent);
    }

    /// <summary>Logs once that a queued re-parent was dropped because it had become a cycle by the time it was applied.</summary>
    private void WarnDroppedAttach()
    {
        if (Interlocked.Exchange(ref _cycleWarningLogged, 1) != 0)
            return;

        Logger.Warning(
            "SceneGraph: a re-parent queued from another thread was dropped because it would have made the scene " +
            "graph a cycle by the time the frame boundary applied it. The tree changed between the request and the " +
            "boundary; re-issue the change from the owner thread (or through Add/Remove) if it is still wanted.");
    }

    /// <summary>
    /// Whether <paramref name="obj"/> is still reachable from <see cref="Roots"/> (walks up to its root, so a
    /// node whose ancestor was just detached counts as gone).
    /// </summary>
    private bool IsInScene(GameObject obj)
    {
        Transform top = obj.Transform;
        while (top.Parent is { } parent)
            top = parent;

        return _roots.Contains(top.Owner);
    }

    // ------------------------------------------------------------------
    // Default scene assembly (grid / lights / initial camera pose; no separate "settings class")
    // ------------------------------------------------------------------

    /// <summary>Creates and adds the grid floor from the current grid properties; does not create it when <see cref="ShowGrid"/> is off.</summary>
    public Grid? AddDefaultGrid()
    {
        if (!ShowGrid)
            return null;

        var grid = new Grid(GridCellSize, GridCellCount, GridColor);
        grid.SetSubtreePickable(false);   // The grid is a reference plane and should not participate in picking.
        Add(grid);
        return grid;
    }

    /// <summary>Adds the default key light plus a fill light.</summary>
    public void AddDefaultLights()
    {
        var key = new Light("key_light") { Color = Vector3.One, Intensity = 0.85f };
        key.Transform.Position = new Vector3(4f, 5f, 10f);   // Z-up: place it above the ground.
        Add(key);

        var fill = new Light("fill_light") { Color = new Vector3(0.55f, 0.55f, 0.65f), Intensity = 0.4f };
        fill.Transform.Position = new Vector3(-5f, -3f, 6f);
        Add(fill);
    }

    /// <summary>Name of the default world-origin axes node (how a re-fit finds the set already in the scene).</summary>
    public const string WorldAxesName = "world-axes";

    /// <summary>
    /// Creates and adds the default global axes at the world origin based on <see cref="ShowWorldAxes"/>; does
    /// not create when off. The set is <see cref="AxesSizing.FixedWorldLength"/> with
    /// <see cref="WorldAxesLength"/> meters per axis, so it measures the scene instead of tracking the zoom.
    /// </summary>
    public Axes? AddDefaultWorldAxes()
    {
        if (!ShowWorldAxes)
            return null;

        var axes = new Axes(WorldAxesLength, name: WorldAxesName)
        {
            Transform = { Position = Vector3.Zero },
            Sizing = AxesSizing.FixedWorldLength,
        };
        axes.SetSubtreePickable(false);   // The axes are a display aid and should not participate in picking.
        Add(axes);
        return axes;
    }

    /// <summary>
    /// Sizes the world axes to the scene's content, so a reference axes set stands out beyond the model instead
    /// of hiding inside it: <see cref="WorldAxesLength"/> becomes the content's largest world extent ×
    /// <paramref name="factor"/>, and the world axes already in the scene (if any) take that length as well.
    /// Display aids (the axes themselves, the grid, lights) are ignored while measuring, so calling this twice
    /// converges instead of growing. Returns the applied length; with nothing measurable in the scene the
    /// current <see cref="WorldAxesLength"/> is kept and returned.
    /// </summary>
    /// <param name="factor">Multiplier on the measured extent (&gt;1 puts the axes past the content).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factor"/> is not a positive finite number.</exception>
    public float FitWorldAxesToContent(float factor = 1.15f)
    {
        if (!float.IsFinite(factor) || factor <= 0f)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be a positive finite value.");

        float extent = MeasureContentExtent();
        if (extent <= 0f)
            return WorldAxesLength;

        WorldAxesLength = extent * factor;

        // A set created by an earlier AddDefaultWorldAxes keeps its own copy of the length; push the new value
        // into it too, so the call works whether it comes before or after creation.
        foreach (GameObject root in _roots)
        {
            if (root is Axes { Name: WorldAxesName } axes)
                axes.Length = WorldAxesLength;
        }

        return WorldAxesLength;
    }

    /// <summary>
    /// Largest world-space extent of the scene's drawable content (the biggest side of the combined AABB), or 0
    /// when nothing measurable is in the scene. Axes sets, the grid and lights are skipped: they are display
    /// aids, and including the axes would make them grow with themselves on every call.
    /// </summary>
    private float MeasureContentExtent()
    {
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        foreach (GameObject root in _roots)
            MeasureRecursive(root, ref min, ref max);

        if (min.X > max.X)   // Nothing measurable was visited.
            return 0f;

        Vector3 size = max - min;
        return MathF.Max(size.X, MathF.Max(size.Y, size.Z));
    }

    /// <summary>Accumulates a node subtree's world-space AABB corners into <paramref name="min"/>/<paramref name="max"/>.</summary>
    private static void MeasureRecursive(GameObject node, ref Vector3 min, ref Vector3 max)
    {
        if (node is Axes)
            return;   // An axes set describes the world, it is not part of the world's content.

        if (node.MeshData is { } mesh)
        {
            Bounds bounds = mesh.ComputeBounds();
            Matrix4x4 model = node.Transform.GetModelMatrix();
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                    (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                    (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                Vector3 world = Vector3.Transform(local, model);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
        }

        foreach (Transform child in node.Transform.Children)
            MeasureRecursive(child.Owner, ref min, ref max);
    }

    /// <summary>Poses the camera to the default initial pose (viewing the scene up close).</summary>
    public void ApplyDefaultCamera()
    {
        Camera.Target = new Vector3(0f, 0f, 0.3f);
        Camera.Distance = 3.2f;
        Camera.Pitch = 25f;
        Camera.Yaw = -90f;
    }

    /// <summary>
    /// Ray-picks all "visible, pickable, mesh-carrying" nodes in the scene and returns the nearest hit,
    /// or null if none. By default it only picks objects where <see cref="GameObject.Visible"/> and
    /// <see cref="GameObject.Pickable"/> are both true and <see cref="GameObject.MeshData"/> is non-null, and an
    /// invisible node ends the walk for its whole subtree — the same grouping <see cref="GameObject.Visible"/>
    /// means for drawing, so a click cannot reach a child that is not on screen.
    /// <see cref="LineData"/>-based nodes (grid floor, axes) and point-cloud
    /// <see cref="PointCloud2Data"/> carry no lighting and are skipped during picking.
    /// </summary>
    /// <param name="ray">A world-space ray (ideally from <see cref="Camera.ScreenToWorldRay"/>).</param>
    /// <param name="predicate">Optional filter (e.g. only a certain kind/link); return true to participate.</param>
    /// <param name="hitInvisible">
    /// When true, also hit invisible objects — visibility is ignored for whole subtrees, so an editor can still
    /// select something it just hid.
    /// </param>
    public RaycastHit? Pick(Ray ray, Func<GameObject, bool>? predicate = null, bool hitInvisible = false)
    {
        RaycastHit? best = null;
        foreach (var root in _roots)
            PickRecursive(root, ray, predicate, hitInvisible, ref best);
        return best;
    }

    /// <summary>
    /// Pick-and-highlight closed loop: runs <see cref="Pick"/> on <paramref name="ray"/>, and on a hit
    /// sets that object's <see cref="GameObject.Highlighted"/> to <paramref name="enable"/> (default true)
    /// and returns it; returns null on a miss. A pure forwarding helper — it only sets highlight data and
    /// does not own the host's input framework (e.g. mouse event polling).
    /// </summary>
    /// <param name="ray">The ray to test (world coordinates, ideally from <see cref="Camera.ScreenToWorldRay"/>).</param>
    /// <param name="enable">Whether to set <see cref="GameObject.Highlighted"/> true/false on a hit.</param>
    /// <param name="predicate">Optional filter: objects returning false do not participate (e.g. only respond to robot nodes).</param>
    /// <param name="hitInvisible">
    /// When true, also hit invisible objects — visibility is ignored for whole subtrees, so an editor can still
    /// select something it just hid.
    /// </param>
    /// <returns>The hit object, or null if no ray/node was hit.</returns>
    public GameObject? PickAndHighlight(Ray ray, bool enable = true,
        Func<GameObject, bool>? predicate = null, bool hitInvisible = false)
    {
        RaycastHit? hit = Pick(ray, predicate, hitInvisible);
        if (hit is not { } found)
            return null;

        found.Object.Highlighted = enable;
        return found.Object;
    }

    /// <summary>
    /// Single-selection entry point: the new object becomes <see cref="Selected"/>, the previous one returns to its
    /// plain look, and — with <see cref="ShowSelectionAxes"/> on — the new object gets its local axes mounted as an
    /// ordinary, non-pickable child node (<see cref="GameObject.ShowLocalAxes"/>), so a later click still hits the
    /// object instead of its marker. That marker is read-only by design: it shows a frame, it never moves one, and
    /// selecting is never a manipulation mode — a link's pose stays the business of its joint chain. Passing null
    /// clears the selection. A host that owns its own selection state can keep calling <see cref="PickAndHighlight"/>,
    /// which only flips the highlight.
    /// </summary>
    /// <param name="obj">The object to select, or null to clear the selection.</param>
    /// <param name="highlight">Whether the newly selected object takes the highlight color (default true).</param>
    /// <returns>The selected object (the same <paramref name="obj"/>), or null when the selection was cleared.</returns>
    public GameObject? Select(GameObject? obj, bool highlight = true)
    {
        if (ReferenceEquals(obj, Selected))
            return Selected;

        if (Selected is { } previous)
        {
            previous.Highlighted = false;

            // Unmount only what Select mounted: local axes the scene's author turned on deliberately stay on.
            if (_selectionAxesMounted)
            {
                previous.ShowLocalAxes = false;
                _selectionAxesMounted = false;
            }
        }

        Selected = obj;
        if (obj is null)
            return null;

        obj.Highlighted = highlight;

        if (ShowSelectionAxes && !obj.ShowLocalAxes)
        {
            obj.LocalAxesLength = SelectionAxesLength;
            obj.ShowLocalAxes = true;
            if (obj.LocalAxes is { } mounted)
                mounted.Length = SelectionAxesLength;   // Axes created by an earlier selection take the new length.
            _selectionAxesMounted = true;
        }

        return obj;
    }

    /// <summary>
    /// Pick-and-select closed loop: runs <see cref="Pick"/> on <paramref name="ray"/> and feeds the hit — or null on a
    /// miss — through <see cref="Select"/>. One call is a host's whole click path: highlight what was picked, mount
    /// its axes, drop the previous selection.
    /// </summary>
    /// <param name="ray">The ray to test (world coordinates, ideally from <see cref="Camera.ScreenToWorldRay"/>).</param>
    /// <param name="predicate">Optional filter: objects returning false do not participate.</param>
    /// <param name="hitInvisible">
    /// When true, also hit invisible objects — visibility is ignored for whole subtrees, so an editor can still
    /// select something it just hid.
    /// </param>
    /// <returns>The object the ray selected, or null when it hit nothing (the selection is cleared).</returns>
    public GameObject? PickAndSelect(Ray ray, Func<GameObject, bool>? predicate = null, bool hitInvisible = false)
        => Select(Pick(ray, predicate, hitInvisible)?.Object);

    private void PickRecursive(GameObject node, in Ray ray, Func<GameObject, bool>? predicate,
        bool hitInvisible, ref RaycastHit? best)
    {
        // Visibility is a subtree switch, matching what it means for drawing: a host that hides a group expects a
        // click to pass through all of it, not just through the parent whose flag was set. An editor picking
        // something it just hid passes hitInvisible: true, which ignores the flag for whole subtrees.
        if (!hitInvisible && !node.Visible)
            return;

        if (node.Pickable
            && node.MeshData is { } mesh && mesh.TriangleCount > 0
            && (predicate?.Invoke(node) ?? true))
        {
            TryPickMesh(node, mesh, ray, ref best);
        }

        foreach (var child in node.Transform.Children)
            PickRecursive(child.Owner, ray, predicate, hitInvisible, ref best);
    }

    private void TryPickMesh(GameObject node, MeshData mesh, in Ray ray, ref RaycastHit? best)
    {
        Matrix4x4 model = node.Transform.GetModelMatrix();
        if (!Matrix4x4.Invert(model, out Matrix4x4 invModel))
            return;   // Non-invertible model matrix (e.g. scale=0): ignore.

        // Transform the ray into the node's local space: broad-phase against the local AABB, then
        // per-triangle exact hits.
        Vector3 localOrigin = Vector3.Transform(ray.Origin, invModel);
        Vector3 localDir = Vector3.Normalize(Vector3.TransformNormal(ray.Direction, invModel));
        var localRay = new Ray(localOrigin, localDir);

        if (Raycast.HitAABB(localRay, mesh.ComputeBounds()) is null)
            return;

        IReadOnlyList<Vector3> pos = mesh.Positions;
        IReadOnlyList<uint> idx = mesh.Indices;

        float bestLocalDist = float.MaxValue;
        Vector3 bestLocalPoint = default, bestLocalNormal = default;
        float bestU = 0f, bestV = 0f;

        for (int i = 0; i < idx.Count; i += 3)
        {
            Vector3 a = pos[(int)idx[i]];
            Vector3 b = pos[(int)idx[i + 1]];
            Vector3 c = pos[(int)idx[i + 2]];

            if (Raycast.HitTriangle(localRay, a, b, c, out float t, out Vector3 n, out float u, out float v)
                && t < bestLocalDist)
            {
                bestLocalDist = t;
                bestLocalPoint = localOrigin + localDir * t;
                bestLocalNormal = n;
                bestU = u;
                bestV = v;
            }
        }

        if (bestLocalDist >= float.MaxValue)
            return;

        // Transform the local hit point/normal back to world; world distance is measured from the world
        // hit point to the ray origin (under non-uniform scaling local t ≠ world distance).
        Vector3 worldPoint = Vector3.Transform(bestLocalPoint, model);
        Vector3 worldNormal = Vector3.Normalize(Vector3.TransformNormal(bestLocalNormal, model));
        float distance = (worldPoint - ray.Origin).Length();

        if (best is null || distance < best.Value.Distance)
            best = new RaycastHit(node, worldPoint, distance, worldNormal, bestU, bestV);
    }

    /// <summary>
    /// Recursively updates all nodes (never manipulate OpenGL here — the update pass only writes CPU data).
    /// It is also a frame boundary: the structural changes producers queued are applied first, so the walk
    /// below never sees a list changing underneath it. Call it on the scene's owner thread — the same thread
    /// that renders (both are frame boundaries, so they belong to one frame loop).
    /// </summary>
    public void Update(double deltaTime)
    {
        ApplyPendingChanges();

        foreach (var root in _roots)
            UpdateRecursive(root, deltaTime);
    }

    private void UpdateRecursive(GameObject node, double deltaTime)
    {
        node.Update(deltaTime);
        foreach (var child in node.Transform.Children)
            UpdateRecursive(child.Owner, deltaTime);
    }

    /// <summary>
    /// Drops the scene's node and light lists and everything producers had queued. GPU resources are owned by
    /// the renderer (<c>RobotSimulation.OpenGL.Renderer</c>), so nothing here touches OpenGL; the call is
    /// idempotent. A producer that calls <see cref="Add"/> / <see cref="Remove"/> afterwards is ignored with
    /// one warning (and a queue drained after this point is dropped, never applied).
    /// </summary>
    public void Dispose()
    {
        lock (_structureGate)
        {
            if (_disposed)
                return;

            // GPU meshes/materials are released by the renderer (Renderer); the scene only holds data
            // references, so here we just clear the node lists.
            foreach (GameObject root in _roots)
                root.RegisteredScene = null;   // A disposed scene must not stay the one a later re-parent routes into.
            _roots.Clear();
            _lights.Clear();
            Selected = null;   // The node list is gone: a stale selection would point into nothing.
            _selectionAxesMounted = false;
            _pending.Clear();  // Nothing left to apply: the frame boundary drops these too.
            Interlocked.Exchange(ref _pendingCount, 0);
            _disposed = true;
        }
    }

    /// <summary>What a queued structural change asks the frame boundary to do.</summary>
    private enum PendingChangeKind : byte
    {
        /// <summary>Join the scene (<see cref="SceneGraph.Add"/>).</summary>
        Add,

        /// <summary>Leave the scene (<see cref="SceneGraph.Remove"/>).</summary>
        Remove,

        /// <summary>Change parent (a <see cref="Transform.Parent"/> set); the tree is relinked and <see cref="SceneGraph.Roots"/> membership follows.</summary>
        Attach,
    }

    /// <summary>A structural change queued by a producer thread, applied at the next frame boundary.</summary>
    /// <param name="Kind">The operation to perform.</param>
    /// <param name="Object">The object the change refers to.</param>
    /// <param name="Parent">For <see cref="PendingChangeKind.Attach"/>, the new parent (null = detach to no parent); ignored by the other kinds.</param>
    private readonly record struct PendingChange(PendingChangeKind Kind, GameObject Object, Transform? Parent)
    {
        public static PendingChange Add(GameObject obj) => new(PendingChangeKind.Add, obj, null);

        public static PendingChange Remove(GameObject obj) => new(PendingChangeKind.Remove, obj, null);

        public static PendingChange Attach(GameObject obj, Transform? parent) => new(PendingChangeKind.Attach, obj, parent);
    }
}
