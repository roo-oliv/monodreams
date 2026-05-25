using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Draw;

namespace MonoDreams.UI;

/// <summary>
/// Component that holds a button's properties.
/// </summary>
public struct SimpleButtonComponent
{
    public Vector2 Size { get; set; }
    public float LineThickness { get; set; }
    public Color Color { get; set; }
    public Entity? TextEntity { get; set; }
    public RenderTargetID Target { get; set; }
}
