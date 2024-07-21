using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;
using System.Collections.Generic;
using MonoDreams.Draw;

namespace MonoDreams.System.Draw;

public sealed class DrawSystem(GraphicsDevice graphicsDevice, World world, SpriteBatch spriteBatch)
    : AEntitySetSystem<GameState>(world.GetEntities().With<DrawInfo>().AsSet())
{
    private readonly List<VertexPositionColorTexture> _vertexList = [];
    private readonly BasicEffect _basicEffect = new(graphicsDevice) 
    {
        VertexColorEnabled = true,
        TextureEnabled = true,
        LightingEnabled = false
    };

    protected override void PreUpdate(GameState state)
    {
        _vertexList.Clear();
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawInfo = ref entity.Get<DrawInfo>();
        ref var position = ref entity.Get<Position>();

        if (drawInfo.NinePatchInfo.HasValue)
        {
            AddNinePatchVertices(drawInfo, position);
        }
        else
        {
            AddRegularSpriteVertices(drawInfo, position);
        }
    }

    protected override void PostUpdate(GameState state)
    {
        // spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

        if (_vertexList.Count > 0)
        {
            var viewport = graphicsDevice.Viewport;
            _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1);
            foreach (var pass in _basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _vertexList.ToArray(), 0, _vertexList.Count / 3);
            }
        }

        // spriteBatch.End();
    }

    private void AddRegularSpriteVertices(DrawInfo drawInfo, Position position)
    {
        var layerDepth = DrawLayerDepth.GetLayerDepth(drawInfo.Layer);
        spriteBatch.Draw(drawInfo.SpriteSheet, new Rectangle(position.CurrentLocation.ToPoint(), drawInfo.Size), drawInfo.Source, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
    }

    private void AddNinePatchVertices(DrawInfo drawInfo, Position position)
    {
        var ninePatch = drawInfo.NinePatchInfo.Value;
        var destination = new Rectangle(position.CurrentLocation.ToPoint(), drawInfo.Size);

        // Add vertices for each part of the nine-patch texture
        AddQuad(drawInfo.SpriteSheet, ninePatch.TopLeft, new Rectangle(destination.Left, destination.Top, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.Top, new Rectangle(destination.Left + destination.Width / 3, destination.Top, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.TopRight, new Rectangle(destination.Left + 2 * destination.Width / 3, destination.Top, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.Left, new Rectangle(destination.Left, destination.Top + destination.Height / 3, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.Center, new Rectangle(destination.Left + destination.Width / 3, destination.Top + destination.Height / 3, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.Right, new Rectangle(destination.Left + 2 * destination.Width / 3, destination.Top + destination.Height / 3, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.BottomLeft, new Rectangle(destination.Left, destination.Top + 2 * destination.Height / 3, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.Bottom, new Rectangle(destination.Left + destination.Width / 3, destination.Top + 2 * destination.Height / 3, destination.Width / 3, destination.Height / 3), drawInfo.Color);
        AddQuad(drawInfo.SpriteSheet, ninePatch.BottomRight, new Rectangle(destination.Left + 2 * destination.Width / 3, destination.Top + 2 * destination.Height / 3, destination.Width / 3, destination.Height / 3), drawInfo.Color);
    }

    private void AddQuad(Texture2D texture, Rectangle source, Rectangle destination, Color color)
    {
        Vector2 tl = new Vector2(source.Left / (float)texture.Width, source.Top / (float)texture.Height);
        Vector2 tr = new Vector2(source.Right / (float)texture.Width, source.Top / (float)texture.Height);
        Vector2 br = new Vector2(source.Right / (float)texture.Width, source.Bottom / (float)texture.Height);
        Vector2 bl = new Vector2(source.Left / (float)texture.Width, source.Bottom / (float)texture.Height);

        _vertexList.Add(new VertexPositionColorTexture(new Vector3(destination.Left, destination.Top, 0), color, tl));
        _vertexList.Add(new VertexPositionColorTexture(new Vector3(destination.Left, destination.Bottom, 0), color, bl));
        _vertexList.Add(new VertexPositionColorTexture(new Vector3(destination.Right, destination.Bottom, 0), color, br));
        _vertexList.Add(new VertexPositionColorTexture(new Vector3(destination.Right, destination.Bottom, 0), color, br));
        _vertexList.Add(new VertexPositionColorTexture(new Vector3(destination.Right, destination.Top, 0), color, tr));
        _vertexList.Add(new VertexPositionColorTexture(new Vector3(destination.Left, destination.Top, 0), color, tl));
        
        _basicEffect.Texture = texture;
        _basicEffect.CurrentTechnique.Passes[0].Apply();
    }
}
