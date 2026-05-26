using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// Two-state toggle backed by a sprite sheet. A small system
/// (<see cref="ToggleSwitchSystem"/>) mirrors <see cref="On"/> onto the linked
/// <see cref="SpriteEntity"/>'s <c>SpriteInfoComponent.Source</c> rectangle each
/// frame. Click handling is game-side: typically pair this with a
/// <see cref="SimpleButtonComponent"/> (with a transparent outline) on the same
/// entity so the existing hit-test path fires, then flip <see cref="On"/>.
public struct ToggleSwitchComponent
{
    public bool On;
    public Rectangle OffSource;
    public Rectangle OnSource;
    /// Sprite child entity rendering the toggle visual. Its
    /// <c>SpriteInfoComponent.Source</c> is overwritten every frame.
    public Entity SpriteEntity;
}
