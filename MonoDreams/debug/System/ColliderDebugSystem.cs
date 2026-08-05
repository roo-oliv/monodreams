using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Extensions.Monogame;
using MonoDreams.State;

namespace MonoDreams.System.Debug;

/// <summary>
/// Per-frame debug visualization for BoxColliderComponent and ConvexColliderComponent shapes.
/// Creates ephemeral mesh entities each frame that render colored outlines:
/// red = active, green = passive, gray = disabled — and WHITE for a moment after a contact.
/// Development only — allocates per-frame (ToArray, transient entities) and is not
/// intended for production builds.
///
/// <para><b>Filter.</b> <see cref="Filter"/> narrows what is drawn (null = every collider, the
/// default). A game with hundreds of baked terrain colliders sets it to the handful it cares
/// about — e.g. the player, enemies and hazards — so the overlay stays readable.</para>
///
/// <para><b>Flash.</b> <see cref="Flash"/> blinks a collider white for <see cref="FlashSeconds"/>,
/// so an event that resolves within a single frame is still visible. It is <b>caller-driven on
/// purpose</b>: flashing every contact would strobe constantly on the floor and walls a body rests
/// against, which drowns out the events worth seeing. The game calls it from the moments it cares
/// about — for this game, damage landing.</para>
///
/// <para>Reads collider ENTITIES (colliders-as-entities): each collider is its own entity — a
/// <c>ColliderTagComponent</c>-tagged shape + its own <c>TransformComponent</c> — and this system
/// draws every one from that transform's world pose (box via <c>SATCollision.BoxWorldRect</c>,
/// convex via the collider's <c>WorldVertices</c>). It coexists with the editor: this system is the
/// global diagnostic (thin outlines for EVERY collider, behind the static flag, selection-unaware);
/// the editor draws the SELECTED collider entity's own outline + gives it a gizmo (colliders are
/// first-class selectable entities now — the whole-shape proxies retired). In Edit the editor keeps
/// the selected convex collider's <c>WorldVertices</c> fresh (<c>ProxySyncSystem</c>), so a selected
/// collider's debug outline tracks its vertex edits.</para>
/// </summary>
public class ColliderDebugSystem : ISystem<GameState>
{
    public static bool Enabled = false;

    private const float DebugLayerDepth = 1f;
    private const float LineThickness = 0.5f;

    private readonly World _world;
    private readonly EntitySet _colliderEntities;
    private readonly List<Entity> _debugEntities = [];
    private readonly Dictionary<Entity, float> _flashing = new();
    private readonly List<Entity> _expired = [];

    public ColliderDebugSystem(World world)
    {
        _world = world;
        _colliderEntities = world.GetEntities()
            .With<ColliderTagComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Which collider entities to draw; null (default) draws every one.</summary>
    public Func<Entity, bool> Filter { get; set; }

    /// <summary>How long a collider stays white after a contact.</summary>
    public float FlashSeconds { get; set; } = 0.12f;

    /// <summary>Blinks <paramref name="collider"/>'s outline white for <see cref="FlashSeconds"/>.</summary>
    public void Flash(Entity collider)
    {
        if (collider.IsAlive) _flashing[collider] = FlashSeconds;
    }

    public void Update(GameState state)
    {
        foreach (var entity in _debugEntities)
        {
            if (entity.IsAlive)
                entity.Dispose();
        }
        _debugEntities.Clear();

        // Age the flashes even while disabled, so re-enabling never shows a stale blink.
        foreach (var (entity, remaining) in _flashing)
        {
            var left = remaining - state.Time;
            if (left <= 0f || !entity.IsAlive) _expired.Add(entity);
            else _flashing[entity] = left;
        }
        foreach (var entity in _expired) _flashing.Remove(entity);
        _expired.Clear();

        if (!IsEnabled || !Enabled) return;

        foreach (var entity in _colliderEntities.GetEntities())
        {
            if (Filter != null && !Filter(entity)) continue;
            ref readonly var transform = ref entity.Get<TransformComponent>();
            var flashing = _flashing.ContainsKey(entity);

            if (entity.Has<BoxColliderComponent>())
            {
                ref readonly var box = ref entity.Get<BoxColliderComponent>();
                CreateBoxOutline(transform, box, flashing);
            }

            if (entity.Has<ConvexColliderComponent>())
            {
                var convex = entity.Get<ConvexColliderComponent>();
                CreateConvexOutline(convex, flashing);
            }
        }
    }

    private void CreateBoxOutline(in TransformComponent transform, in BoxColliderComponent box, bool flashing)
    {
        var color = flashing ? Color.White : GetDebugColor(box);

        // Box pose comes from the collider entity's transform (centered, scaled) — the single
        // source is SATCollision.BoxWorldRect, so the outline matches what detection tests.
        var rect = SATCollision.BoxWorldRect(box, transform);
        var topLeft = new Vector2(rect.Left, rect.Top);
        var topRight = new Vector2(rect.Right, rect.Top);
        var bottomRight = new Vector2(rect.Right, rect.Bottom);
        var bottomLeft = new Vector2(rect.Left, rect.Bottom);

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        int indexOffset = 0;

        LineMeshGenerator.AddLine(vertices, indices, topLeft, topRight, LineThickness, color, ref indexOffset);
        LineMeshGenerator.AddLine(vertices, indices, topRight, bottomRight, LineThickness, color, ref indexOffset);
        LineMeshGenerator.AddLine(vertices, indices, bottomRight, bottomLeft, LineThickness, color, ref indexOffset);
        LineMeshGenerator.AddLine(vertices, indices, bottomLeft, topLeft, LineThickness, color, ref indexOffset);

        CreateDebugEntity(vertices, indices);
    }

    private void CreateConvexOutline(ConvexColliderComponent convex, bool flashing)
    {
        var color = flashing ? Color.White : GetDebugColor(convex);
        var verts = convex.WorldVertices;
        if (verts == null || verts.Length < 3) return;

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        int indexOffset = 0;

        for (int i = 0; i < verts.Length; i++)
        {
            var start = verts[i];
            var end = verts[(i + 1) % verts.Length];
            LineMeshGenerator.AddLine(vertices, indices, start, end, LineThickness, color, ref indexOffset);
        }

        CreateDebugEntity(vertices, indices);
    }

    private void CreateDebugEntity(List<VertexPositionColor> vertices, List<int> indices)
    {
        var entity = _world.CreateEntity();
        _debugEntities.Add(entity);

        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            PrimitiveType = PrimitiveType.TriangleList,
            Target = RenderTargetID.Main,
            LayerDepth = DebugLayerDepth
        });
        entity.Set<VisibleComponent>();
    }

    private static Color GetDebugColor(IColliderComponent collider)
    {
        if (!collider.Enabled) return Color.Gray;
        return collider.Passive ? Color.Green : Color.Red;
    }

    public void Dispose()
    {
        foreach (var entity in _debugEntities)
        {
            if (entity.IsAlive)
                entity.Dispose();
        }
        _debugEntities.Clear();
        _flashing.Clear();
        _colliderEntities.Dispose();
    }
}
