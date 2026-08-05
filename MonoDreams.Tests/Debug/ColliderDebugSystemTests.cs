using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.State;
using MonoDreams.System.Debug;

namespace MonoDreams.Tests.Debug;

/// <summary>
/// Protects the two readability affordances of the collider overlay:
/// <see cref="ColliderDebugSystem.Filter"/> (null draws every collider; a predicate narrows the
/// overlay to the handful worth looking at in a world full of baked terrain colliders) and
/// <see cref="ColliderDebugSystem.Flash"/> (a caller-driven white blink that survives long enough
/// to be seen, and ages even while the system is muted so re-enabling never shows a stale blink).
///
/// <para>The system needs no <c>GraphicsDevice</c> — it only creates transient mesh entities in the
/// world — so these are pure in-process unit tests. <c>ColliderDebugSystem.Enabled</c> is static
/// process state: every test sets it and restores it in a <c>finally</c>.</para>
/// </summary>
public class ColliderDebugSystemTests
{
    /// <summary>A frame whose <see cref="GameState.Time"/> (elapsed seconds) is <paramref name="seconds"/>.</summary>
    private static GameState Frame(float seconds) =>
        new(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));

    /// <summary>A standalone collider ENTITY: tag + its own transform + a box shape (active, enabled ⇒ red).</summary>
    private static Entity BoxCollider(World world, Vector2 position)
    {
        var collider = world.CreateEntity();
        collider.Set<ColliderTagComponent>();
        collider.Set(new TransformComponent(position));
        collider.Set(new BoxColliderComponent(new Vector2(8, 8)));
        return collider;
    }

    /// <summary>
    /// The one colour every debug-mesh vertex currently in the world shares — asserting uniformity
    /// along the way, so a partially-recoloured overlay fails loudly instead of passing on its first
    /// vertex.
    /// </summary>
    private static Color SingleOutlineColor(EntitySet debugMeshes)
    {
        var colors = new HashSet<Color>();
        foreach (var entity in debugMeshes.GetEntities())
        {
            var vertices = entity.Get<DrawComponent>().Vertices;
            Assert.NotNull(vertices);
            foreach (var vertex in vertices) colors.Add(vertex.Color);
        }
        Assert.Single(colors);
        return colors.First();
    }

    // ─── Filter narrows the overlay ─────────────────────────────────────────────

    [Fact]
    public void Filter_Null_DrawsEveryCollider()
    {
        var wasEnabled = ColliderDebugSystem.Enabled;
        ColliderDebugSystem.Enabled = true;
        try
        {
            using var world = new World();
            BoxCollider(world, new Vector2(0, 0));
            BoxCollider(world, new Vector2(50, 0));
            BoxCollider(world, new Vector2(100, 0));
            using var debugMeshes = world.GetEntities().With<DrawComponent>().AsSet();
            using var system = new ColliderDebugSystem(world);

            Assert.Null(system.Filter); // the default: no narrowing
            system.Update(Frame(1f / 60f));

            Assert.Equal(3, debugMeshes.Count);
        }
        finally
        {
            ColliderDebugSystem.Enabled = wasEnabled;
        }
    }

    [Fact]
    public void Filter_NarrowsTheOverlay_ToTheMatchingCollidersOnly()
    {
        var wasEnabled = ColliderDebugSystem.Enabled;
        ColliderDebugSystem.Enabled = true;
        try
        {
            using var world = new World();
            BoxCollider(world, new Vector2(0, 0));
            var wanted = BoxCollider(world, new Vector2(50, 0));
            BoxCollider(world, new Vector2(100, 0));
            using var debugMeshes = world.GetEntities().With<DrawComponent>().AsSet();
            using var system = new ColliderDebugSystem(world);

            system.Filter = entity => entity == wanted;
            system.Update(Frame(1f / 60f));

            Assert.Equal(1, debugMeshes.Count);

            // Dropping the filter restores the full overlay on the next frame (the previous frame's
            // transient meshes are disposed at the top of Update, so the count is not cumulative).
            system.Filter = null;
            system.Update(Frame(1f / 60f));
            Assert.Equal(3, debugMeshes.Count);
        }
        finally
        {
            ColliderDebugSystem.Enabled = wasEnabled;
        }
    }

    // ─── Flash blinks white, then reverts ───────────────────────────────────────

    [Fact]
    public void Flash_TurnsTheOutlineWhite_ThenRevertsWhenTheTimerExpires()
    {
        var wasEnabled = ColliderDebugSystem.Enabled;
        ColliderDebugSystem.Enabled = true;
        try
        {
            using var world = new World();
            var collider = BoxCollider(world, Vector2.Zero);
            using var debugMeshes = world.GetEntities().With<DrawComponent>().AsSet();
            using var system = new ColliderDebugSystem(world);

            // Baseline: an active, enabled collider outlines red.
            system.Update(Frame(1f / 60f));
            Assert.Equal(Color.Red, SingleOutlineColor(debugMeshes));

            // A flash lasts FlashSeconds (0.12 by default), so a short frame still shows white.
            system.Flash(collider);
            system.Update(Frame(0.01f));
            Assert.Equal(Color.White, SingleOutlineColor(debugMeshes));

            // Advance past FlashSeconds: the flash expires and the outline reverts to red.
            system.Update(Frame(0.5f));
            Assert.Equal(Color.Red, SingleOutlineColor(debugMeshes));
        }
        finally
        {
            ColliderDebugSystem.Enabled = wasEnabled;
        }
    }

    [Fact]
    public void Flash_AgesWhileDisabled_SoReEnablingShowsNoStaleBlink()
    {
        var wasEnabled = ColliderDebugSystem.Enabled;
        ColliderDebugSystem.Enabled = true;
        try
        {
            using var world = new World();
            var collider = BoxCollider(world, Vector2.Zero);
            using var debugMeshes = world.GetEntities().With<DrawComponent>().AsSet();
            using var system = new ColliderDebugSystem(world);

            system.Flash(collider);

            // Muted: nothing is drawn, but the flash timer still ages down past FlashSeconds.
            system.IsEnabled = false;
            system.Update(Frame(0.5f));
            Assert.Equal(0, debugMeshes.Count);

            // Re-enabled: the expired flash is gone, so the outline is red — not a stale white blink.
            system.IsEnabled = true;
            system.Update(Frame(1f / 60f));
            Assert.Equal(1, debugMeshes.Count);
            Assert.Equal(Color.Red, SingleOutlineColor(debugMeshes));
        }
        finally
        {
            ColliderDebugSystem.Enabled = wasEnabled;
        }
    }

    [Fact]
    public void Flash_OnADeadColliderEntity_IsDroppedWithoutThrowing()
    {
        var wasEnabled = ColliderDebugSystem.Enabled;
        ColliderDebugSystem.Enabled = true;
        try
        {
            using var world = new World();
            var collider = BoxCollider(world, Vector2.Zero);
            var survivor = BoxCollider(world, new Vector2(50, 0));
            using var debugMeshes = world.GetEntities().With<DrawComponent>().AsSet();
            using var system = new ColliderDebugSystem(world);

            system.Flash(collider);
            collider.Dispose(); // the flashed entity dies before the flash expires

            system.Update(Frame(0.01f)); // dead-entity bookkeeping must not throw
            Assert.Equal(1, debugMeshes.Count); // only the survivor is outlined
            Assert.Equal(Color.Red, SingleOutlineColor(debugMeshes));
            Assert.True(survivor.IsAlive);
        }
        finally
        {
            ColliderDebugSystem.Enabled = wasEnabled;
        }
    }
}
