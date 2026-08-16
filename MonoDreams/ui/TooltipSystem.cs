using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.UI;

/// <summary>
/// Floats a one-line label next to the pointer for whatever the pointer is over. It performs NO
/// hit-test of its own: it reads <see cref="PointerPickComponent"/> off the cursor entity — the ONE
/// pointer pick <see cref="UIFocusSystem"/> publishes, the same resolution focus and click act on —
/// and shows the <see cref="TooltipComponent"/> of the picked entity once the pointer has rested on
/// it for the tooltip's delay. The label rides the cursor, flips away from the screen edges instead
/// of sliding off them, renders on a screen-space (top) target, and is disposed the moment the pick
/// moves away or the picked entity dies.
///
/// <para><b>Pipeline placement.</b> After <see cref="UIFocusSystem"/> (which publishes the pick) and
/// after <c>CursorPositionSystem</c> (which writes the pointer's virtual position this frame) —
/// i.e. alongside <see cref="CursorHoverSystem"/>, at the end of the update pipeline, before the
/// draw stack. The tooltip entities carry <c>VisibleComponent</c> so <c>MeshPrepSystem</c> writes
/// the panel's world matrix the same frame they are created.</para>
///
/// <para><b>Ownership.</b> The system owns its two entities (a rounded mesh panel and the text on
/// top) and nothing else touches them: they are created on show, repositioned every frame, rebuilt
/// when the label text changes, and disposed on hide. They carry no <c>SceneObjectComponent</c>, so
/// the editor never serializes a transient tooltip into a scene.</para>
///
/// <para><b>Being stopped.</b> Because it owns entities, "stop running the tooltip" is not the same
/// as "no tooltip": the draw stack keeps rendering whatever is left behind. So it implements
/// <see cref="ISuspendableSystem"/> — the gate that stops forwarding to it (an editor-capable screen
/// entering <c>RunMode.Edit</c> with a <c>Freeze</c> policy, or the systems panel switching the entry
/// off) calls <see cref="Suspend"/>, which despawns the live label. Its own
/// <see cref="IsEnabled"/> = <c>false</c> does the same on the next update.</para>
/// </summary>
public sealed class TooltipSystem : ISystem<GameState>, ISuspendableSystem
{
    private readonly World _world;
    private readonly EntitySet _cursors;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly TooltipStyle _style;
    private readonly Func<string, Vector2> _measure;
    private readonly RenderTargetID _target;

    // The live tooltip: the panel (mesh) and its label (text). Both default (not alive) while hidden.
    private Entity _panel;
    private Entity _label;

    // What the live tooltip is for — the picked entity and the exact string baked into it. A change
    // in either rebuilds the panel mesh; otherwise the frame is a pair of transform writes.
    private Entity _shownFor;
    private string _shownText = string.Empty;
    private Vector2 _shownSize;

    public bool IsEnabled { get; set; } = true;

    /// <param name="world">The screen's world.</param>
    /// <param name="viewportManager">Supplies the virtual screen size the edge-flip works against.</param>
    /// <param name="font">Font for the label. <c>null</c> renders the panel with no text — useful for
    /// tests (which have no <c>GraphicsDevice</c>) and harmless otherwise.</param>
    /// <param name="style">Look and feel; <see cref="TooltipStyle.Default"/> when omitted.</param>
    /// <param name="target">Screen-space target the label draws on. HUD (the default) is the topmost
    /// pass; UI also works. The PICKED entity may live on any target — only the label is screen-space,
    /// which is what makes the edge-flip well defined.</param>
    /// <param name="measure">Overrides how the label's text is measured (default:
    /// <c>font.MeasureString(text) * style.TextScale</c>). The same callback-measurement seam the
    /// layout slots use, so a font-less test — or a game with its own text metrics — can drive the
    /// system.</param>
    public TooltipSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        TooltipStyle? style = null,
        RenderTargetID target = RenderTargetID.HUD,
        Func<string, Vector2>? measure = null)
    {
        if (target is not (RenderTargetID.HUD or RenderTargetID.UI))
            throw new ArgumentException(
                "The tooltip label renders in screen space — use RenderTargetID.HUD or .UI. " +
                "(The entity the tooltip belongs to may live on any target; only the label is screen-space.)",
                nameof(target));

        _world = world;
        _viewportManager = viewportManager;
        _font = font;
        _style = style ?? TooltipStyle.Default;
        _target = target;
        _measure = measure ?? DefaultMeasure;
        _cursors = world.GetEntities().With<CursorInputComponent>().With<PointerPickComponent>().AsSet();
    }

    private Vector2 DefaultMeasure(string text)
    {
        if (_font == null) return Vector2.Zero;
        var measured = _font.MeasureString(text);
        return new Vector2(measured.Width, measured.Height) * _style.TextScale;
    }

    public void Update(GameState state)
    {
        // A disabled tooltip system means NO tooltip, not "the current one stays forever": every
        // stand-down path in this system despawns what it owns.
        if (!IsEnabled) { Hide(); return; }

        var cursors = _cursors.GetEntities();
        if (cursors.Length == 0) { Hide(); return; }

        var cursorEntity = cursors[0];
        ref readonly var pick = ref cursorEntity.Get<PointerPickComponent>();
        ref readonly var input = ref cursorEntity.Get<CursorInputComponent>();

        // Hover-out and target death are the same case: the pick no longer names a live entity with
        // something to say.
        var target = pick.Hovered;
        if (!target.IsAlive || !target.Has<TooltipComponent>()) { Hide(); return; }

        ref readonly var tooltip = ref target.Get<TooltipComponent>();
        if (string.IsNullOrEmpty(tooltip.Text)) { Hide(); return; }

        // Dwell: the pick owns "since when", so the delay is one subtraction and survives the
        // tooltip being hidden and shown again on the same entity.
        var delay = tooltip.Delay ?? _style.Delay;
        if (state.TotalTime - pick.HoverStartTime < delay) { Hide(); return; }

        Show(target, tooltip.Text, input.VirtualPosition);
    }

    /// Creates (or updates) the label for <paramref name="target"/> and places it beside the pointer.
    private void Show(Entity target, string text, Vector2 pointer)
    {
        if (!_panel.IsAlive || target != _shownFor || text != _shownText)
            Build(target, text);

        var position = TooltipPlacement.Place(
            pointer, _shownSize, _style.Offset, _style.ScreenMargin,
            new Vector2(_viewportManager.VirtualWidth, _viewportManager.VirtualHeight));

        // Both entities are positioned absolutely (no parenting): the tooltip is created and moved
        // AFTER HierarchySystem has run for the frame, so a parent-relative label would render at a
        // stale world position on the frame it appears.
        _panel.Get<TransformComponent>().Position = position;
        if (_label.IsAlive) _label.Get<TransformComponent>().Position = position + _style.Padding;
    }

    /// (Re)builds the panel mesh + label for a new target/text pair.
    private void Build(Entity target, string text)
    {
        Hide();

        var textSize = _measure(text);
        _shownSize = textSize + _style.Padding * 2f;
        _shownFor = target;
        _shownText = text;

        var panelRect = new Rectangle(0, 0,
            (int)MathF.Ceiling(_shownSize.X), (int)MathF.Ceiling(_shownSize.Y));

        var mesh = new CompositeMeshGenerator()
            .Add(new FilledRoundedRectangleMeshGenerator(panelRect, _style.CornerRadius, _style.Fill));
        if (_style.OutlineThickness > 0f && _style.Outline.A > 0)
            mesh.Add(new RoundedRectangleOutlineMeshGenerator(
                panelRect, _style.CornerRadius, _style.OutlineThickness, _style.Outline));

        _panel = _world.CreateEntity();
        _panel.Set(new EntityInfoComponent("Tooltip", "panel"));
        _panel.Set(new TransformComponent(Vector2.Zero));
        var draw = new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = _target,
            LayerDepth = _style.LayerDepth,
        };
        draw.SetMeshData(mesh);
        _panel.Set(draw);
        _panel.Set<VisibleComponent>();

        if (_font == null) return; // panel-only (font-less) mode: nothing to write the label with

        _label = _world.CreateEntity();
        _label.Set(new EntityInfoComponent("Tooltip", "label"));
        _label.Set(new TransformComponent(Vector2.Zero));
        _label.Set(new DynamicTextComponent
        {
            Target = _target,
            LayerDepth = MathF.Min(_style.LayerDepth + 0.005f, 1f),
            TextContent = text,
            Font = _font,
            Color = _style.TextColor,
            Scale = _style.TextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        _label.Set<VisibleComponent>();
    }

    /// <summary>
    /// The gate's teardown hook (<see cref="ISuspendableSystem"/>): the pipeline has STOPPED running
    /// this system — a screen that freezes it in <c>RunMode.Edit</c>, or the systems panel switching
    /// the entry off — so the live label must go now. No further <see cref="Update"/> is coming to
    /// hide it, while the prep + render systems (which do not freeze) would keep drawing it on the
    /// screen-space target for the rest of the session. Idempotent.
    /// </summary>
    public void Suspend(GameState state) => Hide();

    /// Despawns the live tooltip, if any. Idempotent — hover-out, target death, being stopped by the
    /// gate, a disposed world and the editor's Restart sweep all land here.
    private void Hide()
    {
        if (_label.IsAlive) _label.Dispose();
        if (_panel.IsAlive) _panel.Dispose();
        _label = default;
        _panel = default;
        _shownFor = default;
        _shownText = string.Empty;
        _shownSize = Vector2.Zero;
    }

    public void Dispose()
    {
        Hide();
        _cursors.Dispose();
    }
}

/// <summary>
/// The tooltip's placement math, kept pure (no world, no font, no graphics device) so the edge
/// behavior is unit-testable: where a label of a given size goes for a given pointer position on a
/// given screen.
/// </summary>
public static class TooltipPlacement
{
    /// <summary>
    /// Top-left of a <paramref name="size"/> label for a pointer at <paramref name="pointer"/> on a
    /// <paramref name="screen"/>-sized surface. It sits at <paramref name="offset"/> from the pointer
    /// (below-right by default) and FLIPS to the opposite side of the pointer on whichever axis would
    /// otherwise push it past <paramref name="margin"/> from the screen edge — so the tooltip of the
    /// right-most icon opens leftwards instead of sliding off screen. A label too big to fit on
    /// either side is clamped inside the margins (top-left wins), never pushed out of view.
    /// </summary>
    public static Vector2 Place(Vector2 pointer, Vector2 size, Vector2 offset, float margin, Vector2 screen)
    {
        var x = pointer.X + offset.X;
        if (x + size.X > screen.X - margin) x = pointer.X - offset.X - size.X;

        var y = pointer.Y + offset.Y;
        if (y + size.Y > screen.Y - margin) y = pointer.Y - offset.Y - size.Y;

        return new Vector2(
            Clamp(x, margin, screen.X - margin - size.X),
            Clamp(y, margin, screen.Y - margin - size.Y));
    }

    // MathHelper.Clamp is undefined when max < min (a label wider than the screen); pin to min there.
    private static float Clamp(float value, float min, float max) =>
        max <= min ? min : MathHelper.Clamp(value, min, max);
}
