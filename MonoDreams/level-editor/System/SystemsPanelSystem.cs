#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor's <b>systems panel</b> (Wave 8a): a live listing of EVERY
/// <see cref="EditorPipelineRegistrar"/> entry of BOTH pipelines (update + draw), in execution
/// order, inside the shell's right strip — name + edit-time policy + a checkbox reflecting the
/// entry's current enabled state. Clicking a row flips the entry through
/// <see cref="EditorPipelineRegistrar.SetEnabled"/> (the gate's master switch: off stops the child
/// in <b>both</b> modes), so a gamedev can watch the ECS pipeline and turn systems on/off live —
/// e.g. re-enable a Freeze-gated collision block while editing, or silence a debug draw system.
///
/// <para><b>Chrome, native pixels.</b> Rows are ordinary chrome entities on
/// <c>RenderTargetID.Editor</c> (checkboxes are fill-only <see cref="SimpleButtonComponent"/>
/// meshes prepped by the woven <c>ButtonMeshPrepSystem</c>; labels are
/// <see cref="DynamicTextComponent"/> at <see cref="EditorChromeBuilder.LabelScale"/>), laid out
/// by the pure <see cref="SystemsPanelLayout"/> inside <see cref="EditorChromeLayout.RightPanel"/>
/// and hit-tested against the cursor's raw <see cref="CursorInputComponent.ScreenPosition"/> —
/// never <c>VirtualPosition</c>, which is frozen over the chrome margins. Per the chrome rule the
/// row entities carry <b>no</b> <c>VisibleComponent</c>. Visibility in Play is owned by the chrome
/// render pass (it contributes nothing outside Edit); this system's own Edit guard makes the
/// interaction inert there.</para>
///
/// <para><b>Scroll.</b> When the list overflows the strip, the mouse wheel over the panel scrolls
/// it by whole lines (<see cref="SystemsPanelLayout.LinesPerNotch"/> per notch); scrolled-out rows
/// are parked off-screen (GPU-clipped) so no partially visible line bleeds over the top/bottom
/// bars. <c>CameraNavSystem</c> already ignores scroll while the pointer is outside the game
/// viewport, so the panel never fights the camera zoom.</para>
///
/// <para><b>Binding.</b> The registrars are bound onto the overlay only after the screen finishes
/// building its pipelines (<c>EditorOverlay.BindPipelines</c>), so this system takes a lazy
/// provider and builds its rows on the first Edit frame where both registrars are present.
/// Entries are fixed after <c>Build()</c> (the registrar refuses later additions), so the rows are
/// built once. The row entities are owned by this system (private visuals, like the gizmo's
/// overlay entities — no other system reads them, so they carry no dedicated component).</para>
///
/// <para><b>Self-protection.</b> The row for this system's own entry (the panel itself) ignores
/// clicks: disabling the panel through the panel would stop its own update — including its
/// hit-test — leaving no UI path to re-enable it. Every other entry, including
/// <c>editor.renderChrome</c> (which blanks the whole chrome while the rows keep hit-testing, so
/// clicking the same spot again restores it), stays toggleable.</para>
/// </summary>
public sealed class SystemsPanelSystem : ISystem<GameState>
{
    private const string UpdateHeader = "UPDATE";
    private const string DrawHeader = "DRAW";

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Func<(EditorPipelineRegistrar? Update, EditorPipelineRegistrar? Draw)> _pipelines;
    private readonly EntitySet _cursorSet;

    /// <summary>One panel line: a section header (<see cref="Entry"/> null) or an entry row.</summary>
    private sealed class Line
    {
        public required string Label;
        public EditorPipelineEntry? Entry;
        public EditorPipelineRegistrar? Registrar;
        public Entity LabelEntity;
        public Entity CheckboxEntity; // default (dead) for headers
    }

    private readonly List<Line> _lines = new();
    private bool _built;
    private int _scroll;

    // Last-applied layout inputs, so rows are repositioned only when something changed.
    private int _laidOutWidth, _laidOutHeight, _laidOutScroll = -1;

    public bool IsEnabled { get; set; } = true;

    /// <summary>The current line-scroll offset (whole lines). Exposed for tests/tooling.</summary>
    public int ScrollOffset => _scroll;

    public SystemsPanelSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        Func<(EditorPipelineRegistrar? Update, EditorPipelineRegistrar? Draw)> pipelines)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _pipelines = pipelines ?? throw new ArgumentNullException(nameof(pipelines));
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        // Edit-guarded: the chrome pass already hides the visuals in Play; this keeps the
        // interaction (toggles, scroll) inert there too.
        if (state.RunMode != RunMode.Edit) return;

        if (!_built && !TryBuild()) return;

        var panel = EditorChromeLayout.RightPanel(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight);

        // Interaction first (scroll may shift the layout this same frame), then position, then
        // reflect the live enabled state onto the visuals.
        var hovered = HandleInteraction(panel);

        _scroll = SystemsPanelLayout.ClampScroll(_scroll, _lines.Count, panel);
        if (_laidOutWidth != _viewportManager.ScreenWidth ||
            _laidOutHeight != _viewportManager.ScreenHeight ||
            _laidOutScroll != _scroll)
            PositionLines(panel);

        ReflectState(hovered);
    }

    private bool TryBuild()
    {
        var (update, draw) = _pipelines();
        if (update == null || draw == null) return false; // screen not bound yet

        BuildSection(UpdateHeader, update);
        BuildSection(DrawHeader, draw);
        _built = true;
        return true;
    }

    private void BuildSection(string header, EditorPipelineRegistrar registrar)
    {
        _lines.Add(new Line { Label = header, LabelEntity = CreateLabel(header) });
        foreach (var entry in registrar.Entries)
        {
            var label = entry.Name + PolicySuffix(entry.Policy);
            _lines.Add(new Line
            {
                Label = label,
                Entry = entry,
                Registrar = registrar,
                LabelEntity = CreateLabel(label),
                CheckboxEntity = CreateCheckbox(),
            });
        }
    }

    /// <summary>The policy tag rendered after an entry's name. <c>RunNormally</c> is the default
    /// and renders untagged; <c>Freeze</c> (off in Edit by declaration) and the reserved policies
    /// are spelled out — this is the registration-site edit-mode declaration, shown live.</summary>
    public static string PolicySuffix(EditTimeBehavior policy) => policy switch
    {
        EditTimeBehavior.Freeze => " [freeze]",
        EditTimeBehavior.RunPartial => " [partial]",
        EditTimeBehavior.RuntimeEditable => " [editable]",
        _ => string.Empty,
    };

    private Entity CreateLabel(string label)
    {
        var text = _world.CreateEntity();
        text.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        text.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.LabelDepth,
            TextContent = label,
            Font = _font!,
            Color = EditorChromeBuilder.LabelColor,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        // NOTE: no VisibleComponent — chrome rule (see EditorChromeBuilder).
        return text;
    }

    private Entity CreateCheckbox()
    {
        var box = _world.CreateEntity();
        box.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        box.Set(new SimpleButtonComponent
        {
            Size = new Vector2(SystemsPanelLayout.CheckboxSize, SystemsPanelLayout.CheckboxSize),
            LineThickness = 1.5f,
            Color = EditorChromeBuilder.ButtonOutline,
            FillColor = Color.Transparent, // ReflectState fills it while the entry is enabled
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.ButtonDepth,
        });
        // NOTE: no VisibleComponent (chrome rule) and no ToolbarButtonComponent (it is not a
        // toolbar action button — ToolbarSystem must not hit-test it).
        return box;
    }

    /// <summary>Scroll + hover + click. Returns the hovered line index (or -1).</summary>
    private int HandleInteraction(Rectangle panel)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!panel.Contains(point)) return -1;

            if (input.ScrollWheelDelta != 0)
                _scroll = SystemsPanelLayout.ClampScroll(
                    _scroll + SystemsPanelLayout.ScrollLines(input.ScrollWheelDelta), _lines.Count, panel);

            var visible = SystemsPanelLayout.VisibleLineCount(panel);
            for (var i = 0; i < _lines.Count; i++)
            {
                var vi = i - _scroll;
                if (vi < 0 || vi >= visible) continue;
                if (!SystemsPanelLayout.LineRect(panel, vi).Contains(point)) continue;

                if (input.LeftButtonReleased) ToggleLine(_lines[i]);
                return i;
            }
            return -1;
        }
        return -1;
    }

    private void ToggleLine(Line line)
    {
        if (line.Entry == null || line.Registrar == null) return; // headers don't toggle
        // Never let the panel disable itself: its own gate off = no update = no hit-test = no
        // way back from the UI. Every other entry stays toggleable.
        if (ReferenceEquals(line.Entry.System, this)) return;
        line.Registrar.SetEnabled(line.Entry.Name, !line.Entry.IsEnabled);
    }

    private void PositionLines(Rectangle panel)
    {
        var visible = SystemsPanelLayout.VisibleLineCount(panel);
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale;

        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            var vi = i - _scroll;
            if (vi < 0 || vi >= visible)
            {
                Park(line.LabelEntity);
                if (line.CheckboxEntity.IsAlive) Park(line.CheckboxEntity);
                continue;
            }

            var rect = SystemsPanelLayout.LineRect(panel, vi);
            if (line.Entry == null)
            {
                Place(line.LabelEntity, SystemsPanelLayout.HeaderPosition(rect, labelHeight));
            }
            else
            {
                var checkbox = SystemsPanelLayout.CheckboxRect(rect);
                Place(line.CheckboxEntity, new Vector2(checkbox.X, checkbox.Y));
                Place(line.LabelEntity, SystemsPanelLayout.LabelPosition(rect, labelHeight));
            }
        }

        _laidOutWidth = _viewportManager.ScreenWidth;
        _laidOutHeight = _viewportManager.ScreenHeight;
        _laidOutScroll = _scroll;
    }

    private void ReflectState(int hoveredIndex)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (line.Entry == null)
            {
                SetLabelColor(line.LabelEntity, EditorChromeBuilder.HeaderLabelColor);
                continue;
            }

            ref var box = ref line.CheckboxEntity.Get<SimpleButtonComponent>();
            box.FillColor = line.Entry.IsEnabled ? EditorChromeBuilder.CheckboxOnFill : Color.Transparent;

            var color = i == hoveredIndex
                ? Color.White
                : line.Entry.IsEnabled ? EditorChromeBuilder.LabelColor : EditorChromeBuilder.DisabledLabelColor;
            SetLabelColor(line.LabelEntity, color);
        }
    }

    private static void SetLabelColor(Entity label, Color color)
    {
        ref var text = ref label.Get<DynamicTextComponent>();
        text.Color = color;
    }

    private static void Park(Entity entity) => Place(entity, SystemsPanelLayout.ParkedPosition);

    private static void Place(Entity entity, Vector2 position)
    {
        // Panel entities are standalone (no parent), so WorldPosition derives from Position.
        ref var transform = ref entity.Get<TransformComponent>();
        transform.Position = position;
        entity.NotifyChanged<TransformComponent>();
    }

    public void Dispose()
    {
        foreach (var line in _lines)
        {
            if (line.LabelEntity.IsAlive) line.LabelEntity.Dispose();
            if (line.CheckboxEntity.IsAlive) line.CheckboxEntity.Dispose();
        }
        _lines.Clear();
        _cursorSet.Dispose();
    }
}
