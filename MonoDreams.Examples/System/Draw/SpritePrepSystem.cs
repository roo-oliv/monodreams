using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Extensions.Monogame;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

// Prepares DrawElements for static sprites and 9-patches
[With(typeof(SpriteInfo), typeof(Position))] // Ensures entities have these + DrawComponent (from base)
public sealed class SpritePrepSystem : DrawPrepSystemBase
{
    public SpritePrepSystem(World world) : base(world, false) { } // Set useParallel = true if desired and safe

    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var position = ref entity.Get<Position>();
        ref readonly var spriteInfo = ref entity.Get<SpriteInfo>();
        ref var drawComponent = ref entity.Get<DrawComponent>();
        drawComponent.Drawables.Clear();
        
        if (spriteInfo.NinePatchData.HasValue && spriteInfo.SpriteSheet != null)
        {
            var ninePatch = spriteInfo.NinePatchData.Value;

            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current,
                Size = ninePatch.TopLeft.Size.ToVector2(),
                SourceRectangle = ninePatch.TopLeft,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + ninePatch.Top.Origin(),
                Size = new Vector2(spriteInfo.Size.X - ninePatch.TopLeft.Width - ninePatch.TopRight.Width, ninePatch.Top.Height),
                SourceRectangle = ninePatch.Top,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + new Vector2(spriteInfo.Size.X - ninePatch.TopRight.Width, 0),
                Size = ninePatch.TopRight.Size.ToVector2(),
                SourceRectangle = ninePatch.TopRight,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + ninePatch.Left.Origin(),
                Size = new Vector2(ninePatch.Left.Width, spriteInfo.Size.Y - ninePatch.TopLeft.Height - ninePatch.BottomLeft.Height),
                SourceRectangle = ninePatch.Left,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + new Vector2(ninePatch.Center.X, ninePatch.Left.Y),
                Size = new Vector2(spriteInfo.Size.X - ninePatch.Left.Width - ninePatch.Right.Width, spriteInfo.Size.Y - ninePatch.TopLeft.Height - ninePatch.BottomLeft.Height),
                SourceRectangle = ninePatch.Center,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + new Vector2(spriteInfo.Size.X - ninePatch.Right.Width, ninePatch.Left.Y),
                Size = new Vector2(ninePatch.Right.Width, spriteInfo.Size.Y - ninePatch.TopRight.Height - ninePatch.BottomRight.Height),
                SourceRectangle = ninePatch.Right,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + new Vector2(0, spriteInfo.Size.Y - ninePatch.BottomLeft.Height),
                Size = ninePatch.BottomLeft.Size.ToVector2(),
                SourceRectangle = ninePatch.BottomLeft,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + new Vector2(ninePatch.Bottom.X, spriteInfo.Size.Y - ninePatch.Bottom.Height),
                Size = new Vector2(spriteInfo.Size.X - ninePatch.BottomLeft.Width - ninePatch.BottomRight.Width, ninePatch.Bottom.Height),
                SourceRectangle = ninePatch.Bottom,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = position.Current + new Vector2(spriteInfo.Size.X - ninePatch.BottomRight.Width, spriteInfo.Size.Y - ninePatch.BottomRight.Width),
                Size = ninePatch.BottomRight.Size.ToVector2(),
                SourceRectangle = ninePatch.BottomRight,
                Color = spriteInfo.Color,
                LayerDepth = spriteInfo.LayerDepth
            });
        }
        // --- Handle Regular Sprite ---
        else if (spriteInfo.SpriteSheet != null)
        {
             drawComponent.Drawables.Add(new DrawElement
             {
                 Type = DrawElementType.Sprite,
                 Target = spriteInfo.Target,
                 Texture = spriteInfo.SpriteSheet,
                 Position = position.Current,
                 Size = spriteInfo.Size,
                 SourceRectangle = spriteInfo.Source, // Use the source from SpriteInfo
                 Color = spriteInfo.Color,
                 LayerDepth = spriteInfo.LayerDepth
                 // Add Rotation, Origin, Scale, Effects from SpriteInfo if they exist
             });
        }
    }
}