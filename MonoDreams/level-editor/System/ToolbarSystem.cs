#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor toolbar's interaction system. It hit-tests the cursor against every
/// <see cref="ToolbarButtonComponent"/> and, on a click (left button released over a button), hands
/// the button's <see cref="EditorToolbarAction"/> plus the frame's <see cref="GameState"/> to a
/// dispatch callback the composer supplies — which wires the transport (Play/Pause / Restart
/// through <c>EditorTransport</c>), Save → open the three-action Save dialog, Undo/Redo →
/// <c>EditorHistory</c>, and the tool/snap actions → the shared <see cref="GizmoStateComponent"/>.
/// (There is no Load action — a scene is opened via the Scenes panel.) It also tracks per-button
/// hover, tints the button fill, keeps the Play/Pause toggle button's icon/label in sync with the
/// transport state, and — UX2-C — <b>bakes each icon button's glyph mesh</b> in a state-driven colour.
///
/// <para><b>Icon buttons (UX2-C).</b> A button with a <see cref="ToolbarButtonComponent.IconEntity"/>
/// renders a procedural glyph mesh (<see cref="EditorIcons"/>) instead of a text label — a screen-baked
/// <c>DrawComponent</c> (identity <c>WorldMatrix</c>, native Editor target, no <c>VisibleComponent</c>)
/// refilled here each frame, the disclosure-arrow pattern. The glyph colour reads the button's state:
/// <c>TextDisabled</c> when inert, <c>Success</c> for the Snap toggle when on, <c>Accent</c> for the
/// ACTIVE transform/boundary tool (the radio over the shared gizmo state), else a hover-fade from
/// <c>Text1</c> (idle) to <c>Text0</c> (hovered). The Play/Pause toggle swaps Play↔Pause with the
/// transport state (the icon analog of the old label swap). The button BODY keeps its existing
/// hover-fade / pressed fill.</para>
///
/// <para><b>Transport model: live in BOTH modes.</b> Under the editor run configuration the shell
/// never collapses, so the toolbar hit-tests in both transport states. What changes with the state
/// is which buttons are active: the TRANSPORT buttons (<see cref="EditorToolbarAction.PlayPause"/>
/// / <see cref="EditorToolbarAction.Restart"/>) dispatch always — they are how you leave either
/// state — while the EDITING buttons (tools / Save / Undo / Redo / Snap) dispatch only
/// while Paused (<see cref="RunMode.Edit"/>) and render with the disabled fill while Playing
/// (in Play a click belongs to the game; an undo racing live physics would be surprising).</para>
///
/// <para><b>Native screen-space hit-test (Wave 7).</b> The toolbar lives on the Editor render
/// target — native window resolution, composited 1:1 — so button <c>Bounds</c> are physical screen
/// pixels and the system tests the cursor's raw <see cref="CursorInputComponent.ScreenPosition"/>
/// (hardware pixels, set before any letterbox/camera mapping) against them. The chrome sits in the
/// viewport-inset margins where the virtual mapping is null, so <c>VirtualPosition</c> (the old
/// HUD hit-test coordinate) would be frozen/stale there; <c>ScreenPosition</c> is always live.</para>
///
/// <para><b>Game-agnostic.</b> The system fires the action enum through the supplied callback so
/// <c>level-editor</c> needs no game type; the overlay owns the concrete transport / writer /
/// history and supplies the dispatch.</para>
/// </summary>
[With(typeof(ToolbarButtonComponent), typeof(TransformComponent))]
public sealed class ToolbarSystem : AEntitySetSystem<GameState>
{
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _gizmoSet;
    private readonly Action<EditorToolbarAction, GameState> _dispatch;
    private readonly Func<EditorToolbarAction, GameState, bool>? _isEditingActionBlocked;
    private readonly Func<bool>? _isInputSuppressed;
    private readonly Func<EditorViewMode>? _viewMode;          // UX2-F: which mode-toggle segment is active
    private readonly ViewportManager? _viewportManager;        // UX2-F: DPR for the segment underline bar

    private bool _cursorPresent;
    private Vector2 _cursorPoint;
    private bool _clicked;
    private bool _leftDown;
    private bool _gizmoPresent;
    private GizmoStateComponent _gizmo;

    /// <param name="world">The screen's world (the toolbar buttons + cursor live here).</param>
    /// <param name="dispatch">Fires a clicked button's action + the frame's state.</param>
    /// <param name="isEditingActionBlocked">Optional extra gate for an EDITING button beyond the
    /// transport rule: when it returns <c>true</c> the button renders with the disabled fill and its
    /// click is suppressed even while Paused. The overlay wires it to the save-guard's "no project
    /// root" cause so Save dims while the project is unresolved (the "Playing" cause is already
    /// covered by the transport rule). Null (the default) preserves the pre-PS2 behaviour.</param>
    /// <param name="viewMode">Optional (UX2-F): the current editor view mode, so a <c>[Scene | Game]</c>
    /// mode-toggle segment renders its active/inactive tab-style visual. Null (the default) treats every
    /// segment as inactive — fine for unit tests that build bare buttons.</param>
    /// <param name="viewportManager">Optional (UX2-F): supplies the device-pixel ratio for the segment's
    /// accent underline bar thickness. Null (the default) → DPR 1.</param>
    /// <param name="isInputSuppressed">Optional global suppress: while it returns <c>true</c> the
    /// toolbar dispatches nothing (a shell splitter/scrollbar drag owns the pointer — a drag that
    /// happens to release over a toolbar button must not also fire it). Null (the default) never
    /// suppresses.</param>
    public ToolbarSystem(World world, Action<EditorToolbarAction, GameState> dispatch,
        Func<EditorToolbarAction, GameState, bool>? isEditingActionBlocked = null,
        Func<EditorViewMode>? viewMode = null,
        ViewportManager? viewportManager = null,
        Func<bool>? isInputSuppressed = null)
        : base(world.GetEntities().With<ToolbarButtonComponent>().With<TransformComponent>().AsSet())
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _isEditingActionBlocked = isEditingActionBlocked;
        _viewMode = viewMode;
        _viewportManager = viewportManager;
        _isInputSuppressed = isInputSuppressed;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        // The shared gizmo state (tool / snap / mode) drives the ACTIVE-tool icon tint. There is exactly
        // one; absent (unit tests that build bare buttons) → every button reads as inactive.
        _gizmoSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
    }

    protected override void PreUpdate(GameState state)
    {
        _cursorPresent = false;
        _clicked = false;
        _leftDown = false;

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            _cursorPresent = true;
            _cursorPoint = input.ScreenPosition; // native-pixel hit-test (Editor target chrome)
            _clicked = input.LeftButtonReleased;  // a click = press then release over the button
            _leftDown = input.LeftButton;         // held → the instant "pressed" fill
            break;
        }

        _gizmoPresent = false;
        foreach (var gizmo in _gizmoSet.GetEntities())
        {
            _gizmo = gizmo.Get<GizmoStateComponent>();
            _gizmoPresent = true;
            break;
        }
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var button = ref entity.Get<ToolbarButtonComponent>();

        // The Play/Pause toggle mirrors the transport state every frame — its tooltip (and, if this
        // is a text button, its label) swap Play↔Pause; the icon swap is handled by EditorIcons.Resolve.
        if (button.Action == EditorToolbarAction.PlayPause)
        {
            button.Tooltip = state.RunMode == RunMode.Play ? "Pause" : "Play";
            SyncPlayPauseLabel(entity, state);
        }

        // Transport + mode-toggle controls are live in both modes (a mode toggle must be able to exit the
        // sandbox while Playing); editing buttons only while Paused (Edit) — and an editing button may be
        // additionally gated (e.g. Save while the project is unresolved or in Game mode).
        var active = (state.RunMode == RunMode.Edit || button.Action.IsTransport() || button.Action.IsModeToggle())
                     && !(_isEditingActionBlocked?.Invoke(button.Action, state) ?? false);

        // Two hover notions: `over` (drives the fill + dispatch, needs the button active) and the raw
        // cursor-in-bounds (drives the tooltip delay — a dimmed button can still explain itself).
        var overRaw = _cursorPresent && button.Bounds.Contains(_cursorPoint);
        var over = overRaw && active;
        button.IsHovered = over;
        button.IsActive = _gizmoPresent && button.Action.IsActiveIn(_gizmo);

        // Per-widget hover fade (framerate-independent, ~120ms): idle Bg2 → hover Bg3. Stored on the
        // component so it survives frame to frame; a disabled button eases back to idle. The tooltip
        // hover clock accumulates while resting over the button and resets on move-off or a press.
        button.HoverProgress = EditorTheme.AdvanceHover(button.HoverProgress, over, state.Time);
        button.HoverSeconds = overRaw && !_leftDown ? button.HoverSeconds + state.Time : 0f;

        // A [Scene | Game] mode-toggle segment renders tab-style (active = Bg1 fill + Accent underline);
        // every other button uses the standard button fill + optional icon glyph.
        if (button.Action.IsModeToggle())
        {
            RenderSegment(entity, ref button);
        }
        else
        {
            if (entity.Has<SimpleButtonComponent>())
            {
                ref var visual = ref entity.Get<SimpleButtonComponent>();
                visual.FillColor = EditorTheme.ControlFill(
                    disabled: !active, selected: false, pressed: over && _leftDown, button.HoverProgress);
                // A text button dims its label while inert (the Playing state reads at a glance); an icon
                // button's glyph carries the state colour instead (baked below).
                SetLabelColor(entity, active ? EditorTheme.Text0 : EditorTheme.TextDisabled);
            }

            BakeIcon(ref button, active, state);
        }

        if (over && _clicked && !(_isInputSuppressed?.Invoke() ?? false))
            _dispatch(button.Action, state);
    }

    /// <summary>Renders a <c>[Scene | Game]</c> mode-toggle segment tab-style (UX2-F), mirroring the
    /// left-strip tab visual: the <b>active</b> segment (its mode == the current view mode) gets a
    /// <c>Bg1</c> fill + a 3pt <c>Accent</c> underline + a <c>Text0</c> label; an <b>inactive</b>
    /// segment gets a <c>Bg0 → Bg2</c> hover-faded fill + an empty underline + a <c>Text1</c> label.
    /// The active-segment resolution reads the injected <see cref="_viewMode"/> seam.</summary>
    private void RenderSegment(in Entity entity, ref ToolbarButtonComponent button)
    {
        var viewMode = _viewMode?.Invoke() ?? EditorViewMode.Scene;
        var selected =
            (button.Action == EditorToolbarAction.ModeScene && viewMode == EditorViewMode.Scene) ||
            (button.Action == EditorToolbarAction.ModeGame && viewMode == EditorViewMode.Game);

        if (entity.Has<SimpleButtonComponent>())
        {
            ref var visual = ref entity.Get<SimpleButtonComponent>();
            // Active = Bg1 (merges into the header body); inactive = Bg0 hover-fading toward Bg2 — the
            // same recipe the left-strip tabs use (never ControlFill, so it reads as a tab, not a button).
            visual.FillColor = selected
                ? EditorTheme.Bg1
                : Color.Lerp(EditorTheme.Bg0, EditorTheme.Bg2, MathHelper.Clamp(button.HoverProgress, 0f, 1f));
        }

        // The accent underline bar (active only) — a raw mesh on the segment's UnderlineEntity.
        if (button.UnderlineEntity is { IsAlive: true } underline && underline.Has<DrawComponent>())
        {
            ref var dc = ref underline.Get<DrawComponent>();
            if (selected)
            {
                var scale = _viewportManager?.DevicePixelRatio ?? 1f;
                var bar = EditorChromeLayout.TabUnderline(button.Bounds, scale);
                var mesh = new FilledRectangleMeshGenerator(bar, EditorTheme.Accent).Generate();
                dc.Type = DrawElementType.Mesh;
                dc.Vertices = mesh.Vertices;
                dc.Indices = mesh.Indices;
                dc.PrimitiveType = mesh.PrimitiveType;
                dc.WorldMatrix = Matrix.Identity;
                dc.Target = RenderTargetID.Editor;
                dc.LayerDepth = EditorTheme.Depths.TabUnderline;
            }
            else
            {
                // Park the bar (empty mesh — MasterRenderSystem skips it), like a hidden disclosure arrow.
                dc.Vertices = Array.Empty<VertexPositionColor>();
                dc.Indices = Array.Empty<int>();
            }
        }

        SetLabelColor(entity, selected ? EditorTheme.Text0 : EditorTheme.Text1);
    }

    /// <summary>Refills an icon button's screen-baked glyph mesh with the state-driven colour, sized to
    /// the button's <c>Bounds</c>. A text button (no <see cref="ToolbarButtonComponent.IconEntity"/>) is
    /// a no-op.</summary>
    private void BakeIcon(ref ToolbarButtonComponent button, bool active, GameState state)
    {
        if (button.IconEntity is not { IsAlive: true } iconEntity || !iconEntity.Has<DrawComponent>())
            return;
        if (EditorIcons.Resolve(button.Action, state.RunMode == RunMode.Play) is not { } glyph)
            return;

        var color = IconColor(button.Action, active, button.HoverProgress);
        var rect = EditorIcons.CenteredIconRect(button.Bounds);
        var mesh = EditorIcons.Build(glyph, rect, color);

        ref var dc = ref iconEntity.Get<DrawComponent>();
        dc.Type = DrawElementType.Mesh;
        dc.Vertices = mesh.Vertices;
        dc.Indices = mesh.Indices;
        dc.PrimitiveType = mesh.PrimitiveType;
        dc.WorldMatrix = Matrix.Identity;
        dc.Target = RenderTargetID.Editor;
        dc.LayerDepth = EditorTheme.Depths.Label;
    }

    /// <summary>The icon glyph colour for a button's state (priority): disabled → <c>TextDisabled</c>;
    /// the Snap toggle when on → <c>Success</c>; the active transform/boundary tool → <c>Accent</c>;
    /// otherwise a hover-fade from <c>Text1</c> (idle) to <c>Text0</c> (hovered).</summary>
    private Color IconColor(EditorToolbarAction action, bool active, float hoverProgress)
    {
        if (!active) return EditorTheme.TextDisabled;
        if (action == EditorToolbarAction.ToggleSnap && _gizmoPresent && _gizmo.SnapEnabled)
            return EditorTheme.Success;
        if (action != EditorToolbarAction.ToggleSnap && _gizmoPresent && action.IsActiveIn(_gizmo))
            return EditorTheme.Accent;
        return Color.Lerp(EditorTheme.Text1, EditorTheme.Text0, MathHelper.Clamp(hoverProgress, 0f, 1f));
    }

    private static void SyncPlayPauseLabel(in Entity button, GameState state)
    {
        if (!button.Has<SimpleButtonComponent>()) return;
        var textEntity = button.Get<SimpleButtonComponent>().TextEntity;
        if (textEntity is not { IsAlive: true } label || !label.Has<DynamicTextComponent>()) return;

        ref var text = ref label.Get<DynamicTextComponent>();
        // While Playing the button offers "Pause"; while Paused it offers "Play". The button was
        // laid out for the wider label, so the swap never moves the row.
        text.TextContent = state.RunMode == RunMode.Play ? "Pause" : "Play";
    }

    private static void SetLabelColor(in Entity button, Color color)
    {
        if (!button.Has<SimpleButtonComponent>()) return;
        var textEntity = button.Get<SimpleButtonComponent>().TextEntity;
        if (textEntity is not { IsAlive: true } label || !label.Has<DynamicTextComponent>()) return;
        label.Get<DynamicTextComponent>().Color = color;
    }

    public override void Dispose()
    {
        _cursorSet.Dispose();
        _gizmoSet.Dispose();
        base.Dispose();
    }
}
