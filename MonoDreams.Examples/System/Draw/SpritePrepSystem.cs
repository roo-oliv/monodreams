using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

// Prepares DrawElements for static sprites and 9-patches
[With(typeof(SpriteInfo), typeof(Position))] // Ensures entities have these + DrawComponent (from base)
public sealed class SpritePrepSystem : DrawPrepSystemBase
{
    public SpritePrepSystem(World world) : base(world, false) { } // Set useParallel = true if desired and safe

    protected override void Update(GameState state, in Entity entity)
    {
        // Get components (using 'ref' for potential modification, 'ref read-only' for read-only)
        ref readonly var position = ref entity.Get<Position>();
        ref readonly var spriteInfo = ref entity.Get<SpriteInfo>();
        ref var drawComponent = ref entity.Get<DrawComponent>(); // Get the DrawComponent ensured by base class

        // *** Strategy: Clear drawables added by this system type ***
        // A simple way is to clear all - assumes this system owns all sprite/9patch DrawElements for the entity.
        // A more robust way could involve tagging DrawElements or clearing selectively.
        drawComponent.Drawables.Clear(); // Clear previous frame's elements for this entity
        
        var layerDepth = spriteInfo.LayerDepth;

        // --- Handle 9-Patch ---
        if (spriteInfo.NinePatchData.HasValue && spriteInfo.SpriteSheet != null)
        {
            var ninePatch = spriteInfo.NinePatchData.Value; // Your NinePatch definition
            Vector2 currentPos = position.Current;
            Vector2 targetSize = spriteInfo.Size; // Overall size for the 9-patch

            // --- This is complex: Calculate the 9 destination rectangles ---
            // You likely have this logic already. Simplified placeholder:
            // You need Rectangles for topLeft, top, topRight, left, center, right, bottomLeft, bottom, bottomRight
            // based on ninePatch margins and targetSize.
            // You also need the corresponding 9 source rectangles from the ninePatch definition.

            // Example for top-left piece (repeat for all 9):
            Rectangle srcRect = ninePatch.TopLeft; // Get source rect from definition
            Rectangle destRect = CalculateNinePatchDestRect(currentPos, targetSize, ninePatch, NinePatchPiece.TopLeft); // Your calculation function

            drawComponent.Drawables.Add(new DrawElement
            {
                Type = DrawElementType.Sprite, // Rendered as simple sprites
                Target = spriteInfo.Target,
                Texture = spriteInfo.SpriteSheet,
                Position = destRect.Location.ToVector2(), // Use calculated dest position
                Size = destRect.Size.ToVector2(),      // Use calculated dest size
                SourceRectangle = srcRect,
                Color = spriteInfo.Color,
                LayerDepth = layerDepth
                // Add Rotation, Origin, Scale, Effects if needed (usually default for 9-patch)
            });

            // ... Add DrawElements for the other 8 pieces ...
             // TODO: Implement calculation and adding for all 9 nine-patch pieces
             // You might extract the nine-patch DrawElement creation logic into a helper method.
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
                 LayerDepth = layerDepth
                 // Add Rotation, Origin, Scale, Effects from SpriteInfo if they exist
             });
        }
    }

    // Placeholder for your 9-patch destination rectangle calculation logic
    private Rectangle CalculateNinePatchDestRect(Vector2 basePos, Vector2 targetSize, MonoDreams.Component.NinePatchInfo ninePatch, NinePatchPiece piece)
    {
        // Implement the logic to determine the destination rectangle for a given piece
        // based on the ninePatch margins and the overall targetSize.
        // This depends heavily on how your NinePatchInfo struct is defined.
        return Rectangle.Empty; // Placeholder
    }
    // Helper enum for clarity in calculation logic
    private enum NinePatchPiece { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

}