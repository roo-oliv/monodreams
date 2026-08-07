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
/// The <b>workspace tab strip</b> (WS — the Blender workspace-tab model): <c>[ Level Editor ]
/// [ Autotile Rules ]</c> at the window top bar's LEFT, switching the whole window between
/// task-oriented views (the general Undo/Redo/Refresh buttons share the bar, right-anchored).
/// Renders + hit-tests two full-height tabs over <see cref="EditorShellStateComponent.ActiveWorkspace"/>;
/// a click routes through the composer-supplied dispatch (the overlay owns what a switch MEANS —
/// e.g. entering Autotile Rules binds the rules view to the active Paint layer, and a switch while
/// Playing is refused with a status hint).
///
/// <para><b>Visuals mirror the panel/viewport tabs</b> (one tab language everywhere): active =
/// <see cref="EditorTheme.Bg1"/> fill + <see cref="EditorTheme.Accent"/> underline +
/// <see cref="EditorTheme.Text0"/> label; inactive = <see cref="EditorTheme.Bg0"/> hover-fading to
/// <see cref="EditorTheme.Bg2"/> + <see cref="EditorTheme.Text1"/>. Chrome entities on the native
/// Editor target (no <c>VisibleComponent</c>), live in BOTH transport states, suppressed during a
/// shell drag. Headless twin: <c>workspace:&lt;level-editor|autotile-rules&gt;</c>.</para>
/// </summary>
public sealed class WorkspaceTabStripSystem : ISystem<GameState>
{
    private static readonly Vector2 ParkPosition = new(-100000f, -100000f);

    private static readonly (EditorWorkspace Workspace, string Label)[] Tabs =
    {
        (EditorWorkspace.LevelEditor, "Level Editor"),
        (EditorWorkspace.AutotileRules, "Autotile Rules"),
    };

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Func<string, float> _measureLabel; // already LabelScale-scaled (the chrome seam)
    private readonly EditorShellStateComponent _shell;
    private readonly Action<EditorWorkspace, GameState> _switchWorkspace;
    private readonly Func<bool>? _isInputSuppressed;
    private readonly EntitySet _cursorSet;

    private bool _built;
    private readonly Entity[] _fills = new Entity[Tabs.Length];
    private readonly Entity[] _labels = new Entity[Tabs.Length];
    private readonly Entity[] _underlines = new Entity[Tabs.Length];
    private readonly float[] _hover = new float[Tabs.Length];

    public bool IsEnabled { get; set; } = true;

    public WorkspaceTabStripSystem(World world, ViewportManager viewportManager, BitmapFont font,
        EditorShellStateComponent shell, Action<EditorWorkspace, GameState> switchWorkspace,
        Func<bool>? isInputSuppressed = null)
        : this(world, viewportManager, font,
            label => font.MeasureString(label).Width * EditorChromeBuilder.LabelScale,
            shell, switchWorkspace, isInputSuppressed)
    {
    }

    /// <summary>Test/layout-only ctor (injected label measure, no font) — the chrome-builder seam.</summary>
    public WorkspaceTabStripSystem(World world, ViewportManager viewportManager,
        Func<string, float> measureLabel, EditorShellStateComponent shell,
        Action<EditorWorkspace, GameState> switchWorkspace, Func<bool>? isInputSuppressed = null)
        : this(world, viewportManager, null, measureLabel, shell, switchWorkspace, isInputSuppressed)
    {
    }

    private WorkspaceTabStripSystem(World world, ViewportManager viewportManager, BitmapFont? font,
        Func<string, float> measureLabel, EditorShellStateComponent shell,
        Action<EditorWorkspace, GameState> switchWorkspace, Func<bool>? isInputSuppressed)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font;
        _measureLabel = measureLabel ?? throw new ArgumentNullException(nameof(measureLabel));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _switchWorkspace = switchWorkspace ?? throw new ArgumentNullException(nameof(switchWorkspace));
        _isInputSuppressed = isInputSuppressed;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    /// <summary>The current tab rects (left-to-right), for tests + the overlay's anchor lookups.</summary>
    public Rectangle[] TabRects()
    {
        var scale = _viewportManager.DevicePixelRatio;
        var widths = new int[Tabs.Length];
        for (var i = 0; i < Tabs.Length; i++)
            widths[i] = EditorChromeLayout.TabWidth(_measureLabel(Tabs[i].Label) * scale, scale);
        return EditorChromeLayout.WorkspaceTabRow(_viewportManager.ScreenWidth, widths, scale);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        EnsureBuilt();

        var scale = _viewportManager.DevicePixelRatio;
        var rects = TabRects();

        ReadCursor(out var cursorPresent, out var point, out var clicked);
        var suppressed = _isInputSuppressed?.Invoke() ?? false;
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var dispatch = -1;

        for (var i = 0; i < Tabs.Length; i++)
        {
            var rect = rects[i];
            var isActive = _shell.ActiveWorkspace == Tabs[i].Workspace;
            var over = cursorPresent && rect.Contains(point);
            _hover[i] = EditorTheme.AdvanceHover(_hover[i], over && !isActive, state.Time);

            ref var visual = ref _fills[i].Get<SimpleButtonComponent>();
            visual.Size = new Vector2(rect.Width, rect.Height);
            visual.FillColor = isActive
                ? EditorTheme.Bg1
                : Color.Lerp(EditorTheme.Bg0, EditorTheme.Bg2, MathHelper.Clamp(_hover[i], 0f, 1f));
            visual.Color = visual.FillColor;
            Place(_fills[i], new Vector2(rect.X, rect.Y));

            ref var text = ref _labels[i].Get<DynamicTextComponent>();
            text.TextContent = Tabs[i].Label;
            text.Color = isActive ? EditorTheme.Text0 : EditorTheme.Text1;
            text.Scale = EditorChromeBuilder.LabelScale * scale;
            Place(_labels[i], new Vector2(
                rect.X + EditorChromeLayout.Px(EditorChromeLayout.TabPaddingX, scale),
                rect.Y + (rect.Height - labelHeight) / 2f));

            if (isActive)
                SetMesh(_underlines[i], new FilledRectangleMeshGenerator(
                    EditorChromeLayout.TabUnderline(rect, scale), EditorTheme.Accent).Generate());
            else
                ClearMesh(_underlines[i]);

            if (clicked && !suppressed && over && !isActive) dispatch = i;
        }

        // Dispatch AFTER the render loop (the callback flips ActiveWorkspace, which the loop reads).
        if (dispatch >= 0) _switchWorkspace(Tabs[dispatch].Workspace, state);
    }

    private void ReadCursor(out bool present, out Point point, out bool clicked)
    {
        present = false;
        point = Point.Zero;
        clicked = false;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            present = true;
            point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            clicked = input.LeftButtonReleased;
            break;
        }
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        for (var i = 0; i < Tabs.Length; i++)
        {
            _labels[i] = CreateLabel();
            _underlines[i] = CreateMesh();
            var fill = _world.CreateEntity();
            fill.Set(new EditorInfrastructureComponent()); // survives a transport Restart
            fill.Set(new TransformComponent(ParkPosition));
            fill.Set(new SimpleButtonComponent
            {
                Size = Vector2.Zero,
                LineThickness = 0f, // tab-style: no outline
                Color = EditorTheme.Bg0,
                FillColor = EditorTheme.Bg0,
                TextEntity = _labels[i],
                Target = RenderTargetID.Editor,
                LayerDepth = EditorTheme.Depths.Button,
            });
            _fills[i] = fill;
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
            Font = _font!, // null in layout-only tests (the chrome-builder seam)
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
            LayerDepth = EditorTheme.Depths.TabUnderline,
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
        dc.LayerDepth = EditorTheme.Depths.TabUnderline;
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
