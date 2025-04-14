using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.LegacyComponents;
using MonoDreams.Renderer;
using MonoDreams.State;
using Position = MonoDreams.LegacyComponents.Position;

namespace MonoDreams.System;

public sealed class DebugInfoSystem : AEntitySetSystem<GameState>
{
    private readonly ViewportManager _renderer;
    private readonly Camera _camera;
    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;

    public DebugInfoSystem(ViewportManager renderer, Camera camera, SpriteBatch batch, SpriteFont font, World world)
        : base(world.GetEntities().With<PlayerInput>().AsSet(), true)
    {
        _renderer = renderer;
        _camera = camera;
        _batch = batch;
        _font = font;
    }

    private void DrawText(string text, float x, float y, Color foreColor, float scale)
    {
        _batch.DrawString(
            _font,
            text,
            new Vector2(x, y),
            foreColor,
            0.0f,
            // new Vector2((_font.MeasureString(text).X / 2 * scale - _font.MeasureString(text).X / 2), (_font.MeasureString(text).Y / 2 * scale - _font.MeasureString(text).Y / 2)),
            Vector2.Zero,
            scale,
            SpriteEffects.None,
            0.0f);
    }

    protected override void PreUpdate(GameState state)
    {
        // _renderer.BeginDraw();
        _batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearWrap,
            DepthStencilState.None,
            RasterizerState.CullNone, 
            null,
            _camera.GetViewTransformationMatrix());
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        ref var collidable = ref entity.Get<BoxCollider>();
        ref var playerInput = ref entity.Get<PlayerInput>();
        var x = _camera.Position.X + 310;
        var y = _camera.Position.Y - 530;
        var textSlot = 0;
        var spacing = 40;
        var scale = Vector2.One;
        
        DrawText("TotalTime: " + state.TotalTime, x, y + textSlot++ * spacing, Color.Black, 0.5f);
        DrawText("Current: " + position.CurrentLocation, x, y + textSlot++ * spacing, Color.Black, 0.5f);
        DrawText("Bounds: " + collidable.Bounds, x, y + textSlot++ * spacing, Color.Black, 0.5f);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}