#nullable enable
using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags a pooled <b>viewport tab</b> visual entity (PF-B) with the strip-slot it renders and the
/// screen-space rectangles <c>ViewportTabStripSystem</c> hit-tests. Pure data — the layout + click
/// dispatch is the system's job. Like <see cref="ToolbarButtonComponent"/>, the tab lives on the native
/// <c>RenderTargetID.Editor</c> target, so the hit-test uses the cursor's raw <c>ScreenPosition</c>
/// (physical pixels). The tab's fill + label are the engine's <c>SimpleButtonComponent</c> +
/// <c>ButtonMeshPrepSystem</c>; this adds the tab body + close hit rects, per-widget hover fades, and the
/// refs to the accent underline / ▶ play-marker / <c>×</c> close meshes the system fills each frame.
///
/// <para>The tab dispatches by SLOT INDEX (the descriptor's position in
/// <see cref="EditorShellStateComponent.ViewportTabs"/>), not a fixed action — that is why viewport tabs
/// carry this component rather than <see cref="ToolbarButtonComponent"/> (a fixed
/// <see cref="EditorToolbarAction"/>): the strip is data-driven, so a click routes to
/// <c>SwitchToTab(slot)</c> / <c>CloseTab(slot)</c>.</para>
/// </summary>
public struct ViewportTabComponent
{
    /// <summary>The pool slot's index into <see cref="EditorShellStateComponent.ViewportTabs"/> for the
    /// tab it currently renders — the SwitchToTab / CloseTab dispatch key. <c>-1</c> when the slot is
    /// parked (no descriptor bound this frame).</summary>
    public int Slot;

    /// <summary>The tab body's bounds in physical screen pixels (the click-to-switch hit rect). Empty
    /// while the slot is parked (so the hit-test never matches).</summary>
    public Rectangle Bounds;

    /// <summary>The <c>×</c> close affordance's bounds in physical screen pixels (the click-to-close hit
    /// rect). Empty when the tab is not closable or the slot is parked.</summary>
    public Rectangle CloseBounds;

    /// <summary>Per-widget hover-fade progress for the tab body (0 = idle, 1 = full hover), eased
    /// framerate-independently by the system — on the component (never a pooled row) so the fade is stable.</summary>
    public float HoverProgress;

    /// <summary>Per-widget hover-fade progress for the <c>×</c> close affordance (drives its
    /// <c>TextMuted → Danger</c> tint).</summary>
    public float CloseHoverProgress;

    /// <summary>The raw-mesh entity drawing this tab's active accent underline bar (filled while the tab
    /// is the active context, else emptied) — the tab-style underline, mirroring the panel tabs.</summary>
    public Entity? UnderlineEntity;

    /// <summary>The raw-mesh entity drawing this tab's ▶ play marker (filled for the Game tab, else
    /// emptied) — the small play glyph identifying the sandbox tab.</summary>
    public Entity? PlayMarkerEntity;

    /// <summary>The raw-mesh entity drawing this tab's <c>×</c> close glyph (filled for a closable tab,
    /// else emptied).</summary>
    public Entity? CloseEntity;
}
