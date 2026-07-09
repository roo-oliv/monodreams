#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The window <b>status bar</b> (UX3-F design §5, "like Blender and IntelliJ"): a thin strip flush with
/// the window bottom (below the assets shelf, part of the ONE viewport inset — see
/// <see cref="EditorChromeLayout.StatusBar"/>). Plain pooled labels on the native-resolution Editor
/// target, no interaction: the LEFT shows the live modal-transform readout while one is active, else the
/// contextual status (selection name + entity count); the RIGHT shows the scene id + view mode, with a
/// <see cref="EditorTheme.Warning"/> dirty dot (a MESH — the font has no bullet glyph) when there are
/// unsaved edits. All strings come from the pure <see cref="StatusBarModel"/>.
///
/// <para><b>Chrome rules.</b> Labels/dot on the Editor target, identity <c>WorldMatrix</c>, tagged
/// <see cref="EditorInfrastructureComponent"/>, NO <see cref="VisibleComponent"/> (the mesh-chrome rule).
/// The <see cref="EditorTheme.Bg0"/> band + top rule are built by <c>EditorChromeBuilder</c> (so the
/// margin-coverage test sees them); this system only lays out the dynamic content each frame.</para>
///
/// <para><b>Live in both transport states.</b> The scene/mode readout is meaningful Playing too; the
/// modal readout simply never appears there (a modal is Edit-only). <c>RunNormally</c>.</para>
/// </summary>
public sealed class EditorStatusBarSystem : ISystem<GameState>
{
    private static readonly Vector2 ParkPosition = new(-100000f, -100000f);

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly ModalTransformSystem _modal;
    private readonly Func<string> _sceneId;
    private readonly Func<bool> _isDirty;
    private readonly Func<EditorViewMode> _viewMode;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _entitySet;

    private bool _built;
    private Entity _leftLabel, _rightLabel, _dirtyDot;

    public bool IsEnabled { get; set; } = true;

    /// <param name="modal">The modal system whose <see cref="ModalTransformSystem.IsActive"/> /
    /// <see cref="ModalTransformSystem.Readout"/> drive the left readout.</param>
    /// <param name="sceneId">The current scene id (right side).</param>
    /// <param name="isDirty">Whether there are unsaved edits (the dirty dot) — the SAME source the Scenes
    /// panel reads (the Game-mode snapshot dirty state while sandboxed, else the history).</param>
    /// <param name="viewMode">The current Scene/Game view mode (right side).</param>
    public EditorStatusBarSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        ModalTransformSystem modal,
        Func<string> sceneId,
        Func<bool> isDirty,
        Func<EditorViewMode> viewMode)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _modal = modal ?? throw new ArgumentNullException(nameof(modal));
        _sceneId = sceneId ?? throw new ArgumentNullException(nameof(sceneId));
        _isDirty = isDirty ?? throw new ArgumentNullException(nameof(isDirty));
        _viewMode = viewMode ?? throw new ArgumentNullException(nameof(viewMode));
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        // "Entity count" = the editable, non-infra spatial entities (what the Entities tree pools).
        _entitySet = world.GetEntities().With<TransformComponent>().Without<EditorInfrastructureComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        EnsureBuilt();
        var scale = _viewportManager.DevicePixelRatio;
        var bar = EditorChromeLayout.StatusBar(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var y = bar.Y + (bar.Height - labelHeight) / 2f;
        var pad = EditorChromeLayout.Px(EditorChromeLayout.RowMarginX, scale);

        // ── Left: the modal readout while active, else the contextual status.
        var leftText = _modal.IsActive
            ? StatusBarModel.LeftModal(_modal.Readout)
            : StatusBarModel.LeftStatus(SelectedName(), CountEntities());
        PlaceLabel(_leftLabel, new Vector2(bar.X + pad, y), leftText, EditorTheme.Text1, scale);

        // ── Right: the scene id + mode, right-aligned; the dirty dot sits just left of it.
        var rightText = StatusBarModel.Right(_sceneId(), _viewMode());
        var rightWidth = MeasureLabel(rightText) * scale;
        var rightX = bar.Right - pad - rightWidth;
        PlaceLabel(_rightLabel, new Vector2(rightX, y), rightText, EditorTheme.Text1, scale);

        if (_isDirty())
        {
            var dot = EditorChromeLayout.Px(6, scale);
            var gap = EditorChromeLayout.Px(6, scale);
            var dotRect = new Rectangle(
                (int)(rightX - gap - dot), bar.Y + (bar.Height - dot) / 2, dot, dot);
            SetMesh(_dirtyDot, new FilledRectangleMeshGenerator(dotRect, EditorTheme.Warning).Generate());
        }
        else
        {
            ClearMesh(_dirtyDot);
        }
    }

    private string SelectedName()
    {
        foreach (var e in _selectedSet.GetEntities())
        {
            if (!e.IsAlive) continue;
            if (e.Has<CameraRigComponent>()) return "Camera";
            if (e.Has<EntityInfoComponent>())
            {
                var info = e.Get<EntityInfoComponent>();
                if (!string.IsNullOrEmpty(info.Name)) return info.Name;
                if (!string.IsNullOrEmpty(info.Type)) return info.Type;
            }
            return "Entity";
        }
        return string.Empty; // → "No selection"
    }

    private int CountEntities()
    {
        var n = 0;
        foreach (var _ in _entitySet.GetEntities()) n++;
        return n;
    }

    private float MeasureLabel(string text) =>
        _font == null ? 0f : _font.MeasureString(text).Width * EditorChromeBuilder.LabelScale;

    // ─── entity construction (chrome: Editor target, no VisibleComponent) ────────────────────────────

    private void EnsureBuilt()
    {
        if (_built) return;
        _leftLabel = CreateLabel();
        _rightLabel = CreateLabel();
        _dirtyDot = CreateMesh();
        _built = true;
    }

    private Entity CreateLabel()
    {
        var e = _world.CreateEntity();
        e.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        e.Set(new TransformComponent(ParkPosition));
        e.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Label,
            TextContent = string.Empty,
            Font = _font!,
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

    private void PlaceLabel(Entity e, Vector2 position, string text, Color color, float scale)
    {
        ref var transform = ref e.Get<TransformComponent>();
        transform.Position = position;
        e.NotifyChanged<TransformComponent>();
        ref var display = ref e.Get<DynamicTextComponent>();
        display.TextContent = text;
        display.Color = color;
        display.Scale = EditorChromeBuilder.LabelScale * scale;
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
        _selectedSet.Dispose();
        _entitySet.Dispose();
        GC.SuppressFinalize(this);
    }
}
