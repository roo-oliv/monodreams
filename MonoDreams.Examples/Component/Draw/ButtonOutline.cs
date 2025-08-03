using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component.Draw;

public struct ButtonOutline
{
    public Vector2 Size { get; set; }
    public float LineThickness { get; set; }
    public Color Color { get; set; }
    public RenderTargetID Target { get; set; }
}
