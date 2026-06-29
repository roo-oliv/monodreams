#nullable enable
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Tags a toolbar button entity with the <see cref="EditorToolbarAction"/> it fires and the
/// screen-space bounds <c>ToolbarSystem</c> hit-tests the cursor against. Pure data — the click
/// detection + dispatch is the system's job. Toolbar entities are built on the UI/HUD render target
/// (screen-space), so the hit-test uses the cursor's <c>VirtualPosition</c> (the letterbox-scaled,
/// pre-camera coordinate), not its world position.
///
/// <para>The button's visual (an outline + fill mesh) is the engine's <c>SimpleButtonComponent</c> +
/// <c>ButtonMeshPrepSystem</c>; this component adds only the action binding + hover state on top, so
/// the toolbar reuses the existing button rendering rather than a parallel one.</para>
/// </summary>
public struct ToolbarButtonComponent
{
    /// <summary>The action this button fires when clicked.</summary>
    public EditorToolbarAction Action;

    /// <summary>The button's screen-space (virtual-resolution) bounds, used for the cursor hit-test.
    /// Set once when the toolbar is built (the layout system positions the button; the screen records
    /// the resolved bounds here, or the toolbar builder fills it from the fixed slot sizes).</summary>
    public Rectangle Bounds;

    /// <summary>True while the cursor is over this button — drives a hover tint, set by the system.</summary>
    public bool IsHovered;

    /// <summary>True for a toggle/active-state button (e.g. the snap toggle or the currently selected
    /// tool) so the system can render it in an "on" tint. The screen/system sets this to reflect the
    /// gizmo state.</summary>
    public bool IsActive;
}
