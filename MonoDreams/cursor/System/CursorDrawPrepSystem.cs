using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Cursor;

// TODO: Render target is hardcoded here, LayerDepth is hardcoded, Opacity is hardcoded — these should all be configurable.
// Size IS now per-entity: whatever DrawComponent.Size was set to in Cursor.Create wins (zero falls back to DefaultSize).
public class CursorDrawPrepSystem(World world)
    : AEntitySetSystem<GameState>(world.GetEntities()
        .With<CursorControllerComponent>()
        .With<CursorTexturesComponent>()
        .With<DrawComponent>()
        .With<TransformComponent>()
        .AsSet())
{
    /// Fallback used only when the cursor's DrawComponent.Size is unset.
    /// Per-entity size lives on the DrawComponent — set it in Cursor.Create.
    private static readonly Vector2 DefaultSize = new(32);

    protected override void Update(GameState state, in Entity entity)
    {
        ref var controller = ref entity.Get<CursorControllerComponent>();
        ref var textures = ref entity.Get<CursorTexturesComponent>();
        ref var transform = ref entity.Get<TransformComponent>();
        ref var drawComponent = ref entity.Get<DrawComponent>();

        // Only add draw element if cursor is visible
        if (!controller.IsVisible || !textures.Textures.TryGetValue(controller.Type, out var value))
            return;

        drawComponent.Texture = value;
        drawComponent.Position = transform.Position;
        if (drawComponent.Size == Vector2.Zero) drawComponent.Size = DefaultSize;
    }
}
