using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Cursor;

// TODO: Render target is hardcoded here, Size too, LayerDepth is hardcoded, Opacity is hardcoded, these should all be configurable
public class CursorDrawPrepSystem(World world)
    : AEntitySetSystem<GameState>(world.GetEntities()
        .With<CursorControllerComponent>()
        .With<CursorTexturesComponent>()
        .With<DrawComponent>()
        .With<TransformComponent>()
        .AsSet())
{
    private readonly Vector2 _size = new(64);

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
        drawComponent.Size = _size;
    }
}
