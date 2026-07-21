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
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Draws the editor's icon-button tooltip (UX2-C): ONE pooled visual (a <c>Bg2</c> box + <c>Border</c>
/// outline mesh, and a <c>Text0</c> label) parked when idle, shown near the cursor after a button has
/// been hovered ~0.45s (<see cref="EditorTooltip"/>). It scans every <see cref="ToolbarButtonComponent"/>
/// for the one whose <see cref="ToolbarButtonComponent.HoverSeconds"/> has crossed the delay (that clock
/// is advanced — and reset on move-off / press — by <c>ToolbarSystem</c>, which must run before this),
/// reads its <see cref="ToolbarButtonComponent.Tooltip"/> text, and positions the box clamped to the
/// window. Text buttons (no tooltip) are skipped; a press or move-off drops the clock to 0, hiding the
/// tooltip instantly.
///
/// <para>The visual is native-resolution chrome like the rest of the shell: Editor render target,
/// identity <c>WorldMatrix</c> screen-baked meshes, no <c>VisibleComponent</c>, above the dialog band
/// (<c>EditorTheme.Depths.Tooltip</c>) so it is never occluded. Live in BOTH transport states — hovering
/// a transport button while Playing still explains it.</para>
/// </summary>
public sealed class EditorTooltipSystem : ISystem<GameState>
{
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _buttonSet;

    private readonly Entity _box;   // fill + outline mesh
    private readonly Entity _label; // the one-line text

    public bool IsEnabled { get; set; } = true;

    public EditorTooltipSystem(World world, ViewportManager viewportManager, BitmapFont? font)
    {
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _buttonSet = world.GetEntities().With<ToolbarButtonComponent>().AsSet();

        _box = world.CreateEntity();
        _box.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        _box.Set(new TransformComponent(Vector2.Zero));
        _box.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Tooltip,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });

        _label = world.CreateEntity();
        _label.Set(new EditorInfrastructureComponent());
        _label.Set(new TransformComponent(Vector2.Zero));
        _label.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.TooltipLabel,
            TextContent = string.Empty,
            Font = _font!,
            Color = EditorTheme.Text0,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // The cursor's native-pixel position anchors the box.
        var haveCursor = false;
        var cursor = Vector2.Zero;
        foreach (var c in _cursorSet.GetEntities())
        {
            cursor = c.Get<CursorInputComponent>().ScreenPosition;
            haveCursor = true;
            break;
        }

        // The winning button: a tooltip-bearing button whose hover clock crossed the delay (longest wins).
        string? text = null;
        var bestSeconds = 0f;
        if (haveCursor)
        {
            foreach (var e in _buttonSet.GetEntities())
            {
                ref readonly var b = ref e.Get<ToolbarButtonComponent>();
                if (b.Tooltip == null || !EditorTooltip.ShouldShow(b.HoverSeconds)) continue;
                if (b.HoverSeconds < bestSeconds) continue;
                bestSeconds = b.HoverSeconds;
                text = b.Tooltip;
            }
        }

        if (text == null)
        {
            Park();
            return;
        }

        Show(text, cursor);
    }

    private void Show(string text, Vector2 cursor)
    {
        var scale = _viewportManager.DevicePixelRatio;
        var labelWidth = MeasureLabel(text) * scale;
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var size = EditorTooltip.BoxSize(labelWidth, labelHeight, scale);
        var pos = EditorTooltip.Position(
            cursor, size, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);

        var boxRect = new Rectangle((int)pos.X, (int)pos.Y, (int)MathF.Ceiling(size.X), (int)MathF.Ceiling(size.Y));
        var border = MathF.Max(1f, EditorChromeLayout.Px(EditorTooltip.BorderThickness, scale));
        var mesh = new CompositeMeshGenerator()
            .Add(new FilledRectangleMeshGenerator(boxRect, EditorTheme.Bg2))
            .Add(new RectangleOutlineMeshGenerator(boxRect, border, EditorTheme.Border))
            .Generate();
        SetMesh(_box, mesh, EditorTheme.Depths.Tooltip);

        ref var label = ref _label.Get<DynamicTextComponent>();
        label.TextContent = text;
        label.Scale = EditorChromeBuilder.LabelScale * scale;
        label.Color = EditorTheme.Text0;
        Place(_label, new Vector2(
            boxRect.X + EditorChromeLayout.Px(EditorTooltip.PaddingX, scale),
            boxRect.Y + EditorChromeLayout.Px(EditorTooltip.PaddingY, scale)));
    }

    /// <summary>Hides the tooltip: empty the box mesh (an invalid mesh is skipped by
    /// <c>MasterRenderSystem</c>) and blank the label — the mesh/text analog of parking.</summary>
    private void Park()
    {
        ref var box = ref _box.Get<DrawComponent>();
        box.Vertices = Array.Empty<VertexPositionColor>();
        box.Indices = Array.Empty<int>();
        _label.Get<DynamicTextComponent>().TextContent = string.Empty;
    }

    private float MeasureLabel(string text) =>
        _font != null ? _font.MeasureString(text).Width * EditorChromeBuilder.LabelScale : text.Length;

    private static void SetMesh(Entity e, MeshData mesh, float depth)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Type = DrawElementType.Mesh;
        dc.Vertices = mesh.Vertices;
        dc.Indices = mesh.Indices;
        dc.PrimitiveType = mesh.PrimitiveType;
        dc.WorldMatrix = Matrix.Identity;
        dc.Target = RenderTargetID.Editor;
        dc.LayerDepth = depth;
    }

    private static void Place(Entity entity, Vector2 position)
    {
        ref var transform = ref entity.Get<TransformComponent>();
        transform.Position = position;
        entity.NotifyChanged<TransformComponent>();
    }

    public void Dispose()
    {
        _cursorSet.Dispose();
        _buttonSet.Dispose();
        if (_box.IsAlive) _box.Dispose();
        if (_label.IsAlive) _label.Dispose();
    }
}
