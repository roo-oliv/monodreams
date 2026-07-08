#nullable enable
using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags a toolbar button entity with the <see cref="EditorToolbarAction"/> it fires and the
/// screen-space bounds <c>ToolbarSystem</c> hit-tests the cursor against. Pure data — the click
/// detection + dispatch is the system's job. Toolbar entities are built on the <b>Editor</b>
/// render target at native window resolution (Wave 7), so the hit-test uses the cursor's raw
/// <c>ScreenPosition</c> (physical hardware pixels), not its virtual or world position.
///
/// <para>The button's visual (an outline + fill mesh) is the engine's <c>SimpleButtonComponent</c> +
/// <c>ButtonMeshPrepSystem</c>; this component adds only the action binding + hover state on top, so
/// the toolbar reuses the existing button rendering rather than a parallel one.</para>
/// </summary>
public struct ToolbarButtonComponent
{
    /// <summary>The action this button fires when clicked.</summary>
    public EditorToolbarAction Action;

    /// <summary>The button's bounds in <b>physical screen pixels</b> (the Editor target maps 1:1
    /// to the window), used for the cursor hit-test against <c>ScreenPosition</c>. Written by
    /// <c>EditorChromeBuilder</c> at build time and on every <c>Relayout</c> (window resize).</summary>
    public Rectangle Bounds;

    /// <summary>True while the cursor is over this button — drives a hover tint, set by the system.</summary>
    public bool IsHovered;

    /// <summary>Per-widget hover-fade progress in [0,1] (0 = idle fill, 1 = full hover fill), eased
    /// framerate-independently by <c>ToolbarSystem</c> each frame (<c>EditorTheme.AdvanceHover</c>).
    /// Lives on the button component — never keyed to a pooled entity — so the ~120ms fade is stable.</summary>
    public float HoverProgress;

    /// <summary>True for a toggle/active-state button (e.g. the snap toggle or the currently selected
    /// tool) so the system can render it in an "on" tint. The screen/system sets this to reflect the
    /// gizmo state.</summary>
    public bool IsActive;

    /// <summary>The screen-baked mesh entity that draws this button's ICON (UX2-C), or <c>null</c> for a
    /// text button (the selection-context Order / collider / vertex actions, which have no icon this
    /// wave). When set, <c>ToolbarSystem</c> refills it each frame with the glyph
    /// (<see cref="MonoDreams.LevelEditor.UI.EditorIcons"/>) in the state-driven colour (idle
    /// <c>Text1</c> → hovered <c>Text0</c> → active tool <c>Accent</c> → snap-on <c>Success</c> →
    /// disabled <c>TextDisabled</c>). It is a raw <c>DrawComponent</c> mesh — identity <c>WorldMatrix</c>,
    /// native Editor target, no <c>VisibleComponent</c>, no <c>SimpleButtonComponent</c> — exactly the
    /// disclosure-arrow pattern.</summary>
    public Entity? IconEntity;

    /// <summary>The one-line tooltip shown after a short hover (UX2-C), or <c>null</c> for a button
    /// with no tooltip (text buttons — their label is already visible). Set from the button's display
    /// name at build; <c>ToolbarSystem</c> swaps the Play/Pause entry with the transport state.</summary>
    public string? Tooltip;

    /// <summary>Continuous seconds the cursor has rested over this button — the tooltip delay
    /// accumulator (<c>EditorTooltip.HoverDelaySeconds</c>). Advanced by <c>ToolbarSystem</c> each frame
    /// (reset to 0 on move-off or a press), read by <c>EditorTooltipSystem</c> to pick the button whose
    /// tooltip to show. Lives on the button (never a pooled entity) so the timing is stable.</summary>
    public float HoverSeconds;
}
