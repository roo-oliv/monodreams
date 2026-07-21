namespace MonoDreams.Component.Draw;

public enum RenderTargetID
{
    Main,
    UI,
    HUD,
    Scroll,

    /// <summary>
    /// The editor chrome target: a render target at <b>native window resolution</b> (recreated on
    /// resize), rendered by a screen-space pass and composited 1:1 over the whole window, above the
    /// game layers (see <c>RenderLayer.Native</c>). Entities on this target lay out in physical
    /// screen pixels, so editor chrome (toolbar, panels, labels) is crisp and readable regardless
    /// of the game's virtual resolution. Deliberately not <see cref="Scroll"/>: Scroll is a
    /// game-facing screen-space overlay authored in <i>virtual</i> coordinates and composited via
    /// <c>RenderLayer.Overlay</c> into the aspect-fit viewport — reusing it would mix two
    /// coordinate spaces on one target and upscale the chrome with the game.
    /// </summary>
    Editor,
}
