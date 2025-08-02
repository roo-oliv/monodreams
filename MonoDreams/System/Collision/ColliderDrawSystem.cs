using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.State;
using MonoGame.Extended;

namespace MonoDreams.System;

public sealed class ColliderDrawSystem : AEntitySetSystem<GameState>
{
    private readonly SpriteBatch _batch;
    private readonly Camera _camera;
    private readonly Texture2D _pointTexture;

    public ColliderDrawSystem(
        Camera camera,
        SpriteBatch batch,
        World world
        ) : base(world.GetEntities().With<BoxCollider>().AsSet())
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
        ref var collider = ref entity.Get<BoxCollider>();
        ref var position = ref entity.Get<Transform>();
        var rect = new Rectangle(collider.Bounds.Location + position.CurrentPosition.ToPoint(), collider.Bounds.Size);
        DrawRectangle(rect, collider.Passive ? Color.Red : Color.Blue, 2);
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