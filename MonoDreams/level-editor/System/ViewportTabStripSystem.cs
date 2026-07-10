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
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The <b>viewport tab strip</b> (PF-B) — the header control that replaced the retired Scene/Game mode
/// toggle: <c>[ Scene ] [ ▶ Game × ]</c> at the START of the Scene panel header. It is
/// <b>descriptor-driven</b>: it renders + hit-tests the pure <see cref="EditorShellStateComponent.ViewportTabs"/>
/// list the <c>ViewportContextStack</c> writes — so PF-D appends a prefab tab by adding a descriptor,
/// with no change here (a <c>Kind</c>-agnostic body / close / dirty affordance).
///
/// <para><b>Visuals mirror the panel tabs.</b> The active tab is a <see cref="EditorTheme.Bg1"/> fill +
/// a <see cref="EditorTheme.Accent"/> underline + a <see cref="EditorTheme.Text0"/> label; an inactive
/// tab is a <see cref="EditorTheme.Bg0"/> → <see cref="EditorTheme.Bg2"/> hover-faded fill + a
/// <see cref="EditorTheme.Text1"/> label (per-widget fade, never a pooled row). A closable tab shows a
/// <c>×</c> in its right gutter (<see cref="EditorTheme.TextMuted"/> → <see cref="EditorTheme.Danger"/> on
/// hover); the Game tab shows a small ▶ play marker in its left gutter. The fill + label are the engine's
/// <c>SimpleButtonComponent</c> / <c>ButtonMeshPrepSystem</c>; the underline / ▶ / <c>×</c> are raw
/// screen-baked meshes (identity <c>WorldMatrix</c>, native Editor target, no <c>VisibleComponent</c>).</para>
///
/// <para><b>Clicks route to the transport by slot index.</b> A click on a tab body → <c>SwitchToTab(slot)</c>;
/// a click on the <c>×</c> → <c>CloseTab(slot)</c> (the dirty-close gate lives in the transport / the
/// stack). Live in BOTH transport states (leaving the Game tab must work while Playing), and suppressed
/// while a shell splitter/scrollbar drag owns the pointer (a drag releasing over a tab must not fire it).
/// Owns a small pool of tab entities (created lazily), laid out from the descriptors each frame — parked
/// slots are emptied + zero-sized so they never render or hit-test.</para>
/// </summary>
public sealed class ViewportTabStripSystem : ISystem<GameState>
{
    /// <summary>The pooled tab slots — Scene + Game today; headroom for PF-D prefab tabs.</summary>
    private const int PoolSize = 8;

    private static readonly Vector2 ParkPosition = new(-100000f, -100000f);

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Func<string, float> _measureLabel; // already LabelScale-scaled, like EditorChromeBuilder
    private readonly EditorShellStateComponent _shell;
    private readonly Action<int, GameState> _switchToTab;
    private readonly Action<int, GameState> _closeTab;
    private readonly Func<bool>? _isInputSuppressed;
    private readonly EntitySet _cursorSet;

    private bool _built;
    private readonly Entity[] _tabs = new Entity[PoolSize];

    public bool IsEnabled { get; set; } = true;

    /// <summary>Production ctor: measures labels through <paramref name="font"/> and renders them.</summary>
    public ViewportTabStripSystem(World world, ViewportManager viewportManager, BitmapFont font,
        EditorShellStateComponent shell, Action<int, GameState> switchToTab, Action<int, GameState> closeTab,
        Func<bool>? isInputSuppressed = null)
        : this(world, viewportManager, font,
            label => font.MeasureString(label).Width * EditorChromeBuilder.LabelScale,
            shell, switchToTab, closeTab, isInputSuppressed)
    {
    }

    /// <summary>Test/layout-only ctor: an injected (already <see cref="EditorChromeBuilder.LabelScale"/>-scaled)
    /// label-width measure, no font — labels carry a null font and are not rendered (layout + hit-test only),
    /// mirroring <c>EditorChromeBuilder</c>'s test seam.</summary>
    public ViewportTabStripSystem(World world, ViewportManager viewportManager, Func<string, float> measureLabel,
        EditorShellStateComponent shell, Action<int, GameState> switchToTab, Action<int, GameState> closeTab,
        Func<bool>? isInputSuppressed = null)
        : this(world, viewportManager, null, measureLabel, shell, switchToTab, closeTab, isInputSuppressed)
    {
    }

    private ViewportTabStripSystem(World world, ViewportManager viewportManager, BitmapFont? font,
        Func<string, float> measureLabel, EditorShellStateComponent shell,
        Action<int, GameState> switchToTab, Action<int, GameState> closeTab, Func<bool>? isInputSuppressed)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font;
        _measureLabel = measureLabel ?? throw new ArgumentNullException(nameof(measureLabel));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _switchToTab = switchToTab ?? throw new ArgumentNullException(nameof(switchToTab));
        _closeTab = closeTab ?? throw new ArgumentNullException(nameof(closeTab));
        _isInputSuppressed = isInputSuppressed;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        EnsureBuilt();

        var scale = _viewportManager.DevicePixelRatio;
        var header = EditorChromeLayout.SceneHeader(
            _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale,
            _shell.LeftWidthPt, _shell.RightWidthPt);
        // TB-A: the tab strip owns the header's FULL-WIDTH first row (the tools + transport are row 2), so
        // many named scene tabs never collide with the buttons.
        var tabRow = EditorChromeLayout.SceneHeaderTabRow(header, scale);

        var tabs = _shell.ViewportTabs;
        var active = _shell.ActiveViewportTab;
        var count = Math.Min(tabs.Count, PoolSize);

        // Lay out the visible tabs left-to-right across the tab row.
        var widths = new int[count];
        for (var i = 0; i < count; i++)
            widths[i] = EditorChromeLayout.ViewportTabWidth(
                _measureLabel(tabs[i].Label) * scale,
                showPlayMarker: tabs[i].Kind == ViewportContextKind.Game,
                closable: tabs[i].Closable, scale);
        var rects = EditorChromeLayout.ViewportTabRow(tabRow, widths, scale);

        ReadCursor(out var cursorPresent, out var point, out var clicked, out var leftDown);
        var suppressed = _isInputSuppressed?.Invoke() ?? false;
        var dispatchSlot = -1;
        var dispatchClose = false;

        for (var i = 0; i < PoolSize; i++)
        {
            ref var tab = ref _tabs[i].Get<ViewportTabComponent>();
            if (i >= count)
            {
                ParkSlot(_tabs[i], ref tab);
                continue;
            }

            var descriptor = tabs[i];
            var rect = rects[i];
            var closable = descriptor.Closable;
            var isActive = i == active;
            var closeRect = closable ? EditorChromeLayout.ViewportTabClose(rect, scale) : Rectangle.Empty;

            tab.Slot = i;
            tab.Bounds = rect;
            tab.CloseBounds = closeRect;

            var overBody = cursorPresent && rect.Contains(point);
            var overClose = closable && cursorPresent && closeRect.Contains(point);
            tab.HoverProgress = EditorTheme.AdvanceHover(tab.HoverProgress, overBody && !isActive, state.Time);
            tab.CloseHoverProgress = EditorTheme.AdvanceHover(tab.CloseHoverProgress, overClose, state.Time);

            RenderTab(_tabs[i], ref tab, descriptor, rect, isActive, scale);

            if (clicked && !suppressed && dispatchSlot < 0)
            {
                if (overClose) { dispatchSlot = i; dispatchClose = true; }
                else if (overBody) { dispatchSlot = i; dispatchClose = false; }
            }
        }

        // Dispatch AFTER the render loop (the callback can mutate the descriptor list — e.g. dropping the
        // Game tab — which would invalidate the loop's indices).
        if (dispatchSlot >= 0)
        {
            if (dispatchClose) _closeTab(dispatchSlot, state);
            else _switchToTab(dispatchSlot, state);
        }
    }

    private void RenderTab(Entity entity, ref ViewportTabComponent tab, in ViewportTabDescriptor descriptor,
        Rectangle rect, bool isActive, float scale)
    {
        var showPlayMarker = descriptor.Kind == ViewportContextKind.Game;

        // Body fill + size (SimpleButtonComponent → ButtonMeshPrepSystem). Active = Bg1 (merges into the
        // header body); inactive = Bg0 hover-fading toward Bg2 — the panel-tab recipe (never ControlFill,
        // so it reads as a tab, not a button).
        ref var visual = ref entity.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(rect.Width, rect.Height);
        visual.FillColor = isActive
            ? EditorTheme.Bg1
            : Color.Lerp(EditorTheme.Bg0, EditorTheme.Bg2, MathHelper.Clamp(tab.HoverProgress, 0f, 1f));
        visual.Color = visual.FillColor;
        Place(entity, new Vector2(rect.X, rect.Y));

        // Label: past the left padding + the ▶ gutter, vertically centered.
        if (visual.TextEntity is { IsAlive: true } label && label.Has<DynamicTextComponent>())
        {
            ref var text = ref label.Get<DynamicTextComponent>();
            text.TextContent = descriptor.Label;
            text.Color = isActive ? EditorTheme.Text0 : EditorTheme.Text1;
            text.Scale = EditorChromeBuilder.LabelScale * scale;
            var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
            Place(label, new Vector2(
                EditorChromeLayout.ViewportTabLabelX(rect, showPlayMarker, scale),
                rect.Y + (rect.Height - labelHeight) / 2f));
        }

        // Active accent underline (mirrors the panel tab underline).
        if (tab.UnderlineEntity is { IsAlive: true } underline)
        {
            if (isActive)
                SetMesh(underline, new FilledRectangleMeshGenerator(
                    EditorChromeLayout.TabUnderline(rect, scale), EditorTheme.Accent).Generate());
            else
                ClearMesh(underline);
        }

        // ▶ play marker (the Game tab only).
        if (tab.PlayMarkerEntity is { IsAlive: true } marker)
        {
            if (showPlayMarker)
                SetMesh(marker, EditorIcons.Build(EditorIcons.EditorIcon.Play,
                    EditorChromeLayout.ViewportTabPlayMarker(rect, scale), EditorTheme.Text1));
            else
                ClearMesh(marker);
        }

        // × close affordance (closable tabs) — TextMuted → Danger on hover.
        if (tab.CloseEntity is { IsAlive: true } close)
        {
            if (descriptor.Closable)
            {
                var color = Color.Lerp(EditorTheme.TextMuted, EditorTheme.Danger,
                    MathHelper.Clamp(tab.CloseHoverProgress, 0f, 1f));
                SetMesh(close, BuildCross(EditorChromeLayout.ViewportTabClose(rect, scale), color, scale));
            }
            else
            {
                ClearMesh(close);
            }
        }
    }

    private void ParkSlot(Entity entity, ref ViewportTabComponent tab)
    {
        tab.Slot = -1;
        tab.Bounds = Rectangle.Empty;   // never hit-tests
        tab.CloseBounds = Rectangle.Empty;
        tab.HoverProgress = 0f;
        tab.CloseHoverProgress = 0f;

        ref var visual = ref entity.Get<SimpleButtonComponent>();
        visual.Size = Vector2.Zero;     // degenerate fill — nothing renders
        Place(entity, ParkPosition);
        if (visual.TextEntity is { IsAlive: true } label && label.Has<DynamicTextComponent>())
        {
            label.Get<DynamicTextComponent>().TextContent = string.Empty;
            Place(label, ParkPosition);
        }
        if (tab.UnderlineEntity is { IsAlive: true } u) ClearMesh(u);
        if (tab.PlayMarkerEntity is { IsAlive: true } m) ClearMesh(m);
        if (tab.CloseEntity is { IsAlive: true } c) ClearMesh(c);
    }

    // ─── The × glyph (two crossed strokes) ────────────────────────────────────────────────────────

    private static MeshData BuildCross(Rectangle box, Color color, float scale)
    {
        var thickness = Math.Max(1f, 1.5f * scale);
        var tl = new Vector2(box.Left, box.Top);
        var br = new Vector2(box.Right, box.Bottom);
        var tr = new Vector2(box.Right, box.Top);
        var bl = new Vector2(box.Left, box.Bottom);
        return new CompositeMeshGenerator()
            .Add(new LineMeshGenerator(tl, br, thickness, color))
            .Add(new LineMeshGenerator(tr, bl, thickness, color))
            .Generate();
    }

    // ─── cursor ───────────────────────────────────────────────────────────────────────────────────

    private void ReadCursor(out bool present, out Point point, out bool clicked, out bool leftDown)
    {
        present = false;
        point = Point.Zero;
        clicked = false;
        leftDown = false;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            present = true;
            point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            clicked = input.LeftButtonReleased;
            leftDown = input.LeftButton;
            break;
        }
    }

    // ─── entity construction (chrome: Editor target, no VisibleComponent) ────────────────────────────

    private void EnsureBuilt()
    {
        if (_built) return;
        for (var i = 0; i < PoolSize; i++)
        {
            var label = CreateLabel();
            var tab = _world.CreateEntity();
            tab.Set(new EditorInfrastructureComponent()); // survives a transport Restart, hidden from the tree
            tab.Set(new TransformComponent(ParkPosition));
            tab.Set(new SimpleButtonComponent
            {
                Size = Vector2.Zero,   // parked until the first bind
                LineThickness = 0f,    // tab-style: no outline
                Color = EditorTheme.Bg0,
                FillColor = EditorTheme.Bg0,
                TextEntity = label,
                Target = RenderTargetID.Editor,
                LayerDepth = EditorTheme.Depths.Button,
            });
            tab.Set(new ViewportTabComponent
            {
                Slot = -1,
                Bounds = Rectangle.Empty,
                CloseBounds = Rectangle.Empty,
                UnderlineEntity = CreateMesh(),
                PlayMarkerEntity = CreateMesh(),
                CloseEntity = CreateMesh(),
            });
            _tabs[i] = tab;
        }
        _built = true;
    }

    private Entity CreateLabel()
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(new TransformComponent(ParkPosition));
        e.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Label,
            TextContent = string.Empty,
            Font = _font!, // null in layout-only tests (mirrors EditorChromeBuilder's seam)
            Color = EditorTheme.Text1,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        return e;
    }

    private Entity CreateMesh()
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(new TransformComponent(ParkPosition));
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Label,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });
        return e;
    }

    private static void Place(Entity e, Vector2 position)
    {
        ref var transform = ref e.Get<TransformComponent>();
        transform.Position = position;
        e.NotifyChanged<TransformComponent>();
    }

    private static void SetMesh(Entity e, MeshData mesh)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Type = DrawElementType.Mesh;
        dc.Vertices = mesh.Vertices;
        dc.Indices = mesh.Indices;
        dc.PrimitiveType = mesh.PrimitiveType;
        dc.WorldMatrix = Matrix.Identity;
        dc.Target = RenderTargetID.Editor;
        dc.LayerDepth = EditorTheme.Depths.Label;
    }

    private static void ClearMesh(Entity e)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Vertices = Array.Empty<VertexPositionColor>();
        dc.Indices = Array.Empty<int>();
    }

    public void Dispose()
    {
        _cursorSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
