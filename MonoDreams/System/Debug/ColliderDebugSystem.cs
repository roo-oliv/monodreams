using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Debug;

/// <summary>
/// Per-frame debug visualization for BoxCollider and ConvexCollider shapes.
/// Creates ephemeral mesh entities each frame that render colored outlines:
/// red = active, green = passive, gray = disabled.
/// Development only — allocates per-frame (ToArray, transient entities) and is not
/// intended for production builds.
/// </summary>
public class ColliderDebugSystem : ISystem<GameState>
{
    public static bool Enabled = false;

    private const float DebugLayerDepth = 1f;
    private const float LineThickness = 0.5f;

    private readonly World _world;
    private readonly EntitySet _colliderEntities;
    private readonly List<Entity> _debugEntities = [];

    public ColliderDebugSystem(World world)
    {
        _world = world;
        _colliderEntities = world.GetEntities()
            .With<ColliderTag>()
            .With<Transform>()
            .AsSet();
    }

    public bool IsEnabled { get; set; } = true;

    public void Update(GameState state)
    {
        foreach (var entity in _debugEntities)
        {
            if (entity.IsAlive)
                entity.Dispose();
        }
        _debugEntities.Clear();

        if (!IsEnabled || !Enabled) return;

        foreach (var entity in _colliderEntities.GetEntities())
        {
            ref readonly var transform = ref entity.Get<Transform>();

            if (entity.Has<BoxCollider>())
            {
                ref readonly var box = ref entity.Get<BoxCollider>();
                CreateBoxOutline(transform, box);
            }

            if (entity.Has<ConvexCollider>())
            {
                var convex = entity.Get<ConvexCollider>();
                CreateConvexOutline(convex);
            }
        }
    }

    private void CreateBoxOutline(in Transform transform, in BoxCollider box)
    {
        var color = GetDebugColor(box);

        var topLeft = new Vector2(
            transform.WorldPosition.X + box.Bounds.Left,
            transform.WorldPosition.Y + box.Bounds.Top);
        var topRight = new Vector2(
            transform.WorldPosition.X + box.Bounds.Right,
            transform.WorldPosition.Y + box.Bounds.Top);
        var bottomRight = new Vector2(
            transform.WorldPosition.X + box.Bounds.Right,
            transform.WorldPosition.Y + box.Bounds.Bottom);
        var bottomLeft = new Vector2(
            transform.WorldPosition.X + box.Bounds.Left,
            transform.WorldPosition.Y + box.Bounds.Bottom);

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        int indexOffset = 0;

        LineMeshGenerator.AddLine(vertices, indices, topLeft, topRight, LineThickness, color, ref indexOffset);
        LineMeshGenerator.AddLine(vertices, indices, topRight, bottomRight, LineThickness, color, ref indexOffset);
        LineMeshGenerator.AddLine(vertices, indices, bottomRight, bottomLeft, LineThickness, color, ref indexOffset);
        LineMeshGenerator.AddLine(vertices, indices, bottomLeft, topLeft, LineThickness, color, ref indexOffset);

        CreateDebugEntity(vertices, indices);
    }

    private void CreateConvexOutline(ConvexCollider convex)
    {
        var color = GetDebugColor(convex);
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
        entity.Set<Visible>();
    }

    private static Color GetDebugColor(ICollider collider)
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
        _colliderEntities.Dispose();
    }
}
