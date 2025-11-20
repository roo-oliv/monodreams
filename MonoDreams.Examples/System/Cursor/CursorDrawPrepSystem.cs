using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.Examples.Component.Draw;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Cursor;

// Updates SpriteInfo based on cursor state (visibility, type changes)
// This runs before SpritePrepSystem to ensure correct texture is used
public static class CursorDrawPrepSystem
{
    public static void Register(World world)
    {
        world.System<CursorController, CursorTextures, SpriteInfo>()
            .Kind(CullingPhase) // Run in culling phase, before sprite prep
            .Each((ref CursorController controller, ref CursorTextures textures, ref SpriteInfo spriteInfo) =>
            {
                // Update texture based on cursor type if it changed
                if (textures.Textures.TryGetValue(controller.Type, out var texture))
                {
                    if (spriteInfo.SpriteSheet != texture)
                    {
                        spriteInfo.SpriteSheet = texture;
                        spriteInfo.Source = new Rectangle(0, 0, texture.Width, texture.Height);
                    }
                }
            });
    }
}
