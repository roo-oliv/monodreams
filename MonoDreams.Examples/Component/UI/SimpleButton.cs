using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component.Draw;

namespace MonoDreams.Examples.Component.UI;

/// <summary>
/// Component that holds a button's properties.
/// </summary>
public struct SimpleButton
{
    public Vector2 Size { get; set; }
    public float LineThickness { get; set; }
    public Color Color { get; set; }
    public Entity? TextEntity { get; set; }
    public RenderTargetID Target { get; set; }
}
