using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Message;
using MonoDreams.State;
using MonoGame.Extended;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.System;

public sealed class CollidableDrawSystem : AEntitySetSystem<GameState>
{
    private readonly SpriteBatch _batch;
    private readonly Camera _camera;
    private readonly Texture2D _pointTexture;

    public CollidableDrawSystem(
        Camera camera,
        SpriteBatch batch,
        World world
        ) : base(world.GetEntities().With<BoxCollidable>().AsSet())
    {
        _camera = camera;
        _batch = batch;
        _pointTexture = new Texture2D(_batch.GraphicsDevice, 1, 1);
        _pointTexture.SetData(new[]{Color.White});
    }

    protected override void PreUpdate(GameState state)
    {
        _batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone, 
            null,
            _camera.GetViewTransformationMatrix());
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var collidable = ref entity.Get<BoxCollidable>();
        ref var position = ref entity.Get<Position>();
        var rect = new Rectangle(collidable.Bounds.Location + position.CurrentLocation.ToPoint(), collidable.Bounds.Size);
        DrawRectangle(rect, collidable.Passive ? Color.Red : Color.Blue, 2);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
    
    public void DrawRectangle(Rectangle rectangle, Color color, int lineWidth)
    {
        _batch.Draw(_pointTexture, new Rectangle(rectangle.X, rectangle.Y, lineWidth, rectangle.Height + lineWidth), color);
        _batch.Draw(_pointTexture, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width + lineWidth, lineWidth), color);
        _batch.Draw(_pointTexture, new Rectangle(rectangle.X + rectangle.Width, rectangle.Y, lineWidth, rectangle.Height + lineWidth), color);
        _batch.Draw(_pointTexture, new Rectangle(rectangle.X, rectangle.Y + rectangle.Height, rectangle.Width + lineWidth, lineWidth), color);
        _batch.DrawLine(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom, color, lineWidth);
        _batch.DrawLine(rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Top, color, lineWidth);
    }
}