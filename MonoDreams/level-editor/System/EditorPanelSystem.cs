#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Inspector;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>Which region + content shape an <see cref="EditorPanelSystem"/> instance renders (UX2-B):
/// the framework parameterizes the ONE panel system over the two regions rather than forking a
/// second class.</summary>
public enum EditorPanelRole
{
    /// <summary>The LEFT strip: a tab bar (Entities / Systems / Scenes) over the active tab's body,
    /// its splitter/scrollbar on the strip's right edge.</summary>
    LeftTabs,

    /// <summary>The RIGHT strip: the dedicated Inspector (a slim title header, no tabs; the
    /// selection-bound component list + expandable members), its splitter on the strip's left edge.</summary>
    RightInspector,
}

/// <summary>
/// The editor's tabbed left panel AND dedicated right Inspector panel — ONE system parameterized by
/// an <see cref="EditorPanelRole"/> (UX2-B), extending the Wave-8 systems panel. As
/// <see cref="EditorPanelRole.LeftTabs"/> it is a tab bar (<b>Entities</b> — the world's entities as
/// a parent/child tree, selectable both ways with the viewport; <b>Systems</b> — every
/// <see cref="EditorPipelineRegistrar"/> entry of both pipelines with per-<b>group</b> collapse + the
/// live tri-state enabled toggle; <b>Scenes</b> — the scene catalog + project info) over a body of
/// collapsible sections. As <see cref="EditorPanelRole.RightInspector"/> it has a slim title header
/// and its body is the selected entity's attached components, each expandable to its member values.
/// The left panel keeps the entry name <c>editor.systemsPanel</c> and the overlay's
/// <c>SystemsPanel</c> hook so every screen weaves it unchanged; the Inspector is the new
/// <c>editor.inspector</c> entry.
///
/// <para><b>Sections + groups collapse.</b> Each section header toggles its whole body; a Systems
/// group row's arrow toggles its children; an Entities entity row's arrow toggles its subtree; an
/// Inspector component row toggles its member rows. In-session state lives in the pure-data
/// <see cref="EditorPanelStateComponent"/> (shared by both panels via injection — ECS purity), and
/// the flat row list is assembled purely by <see cref="EditorPanelModel.Build"/> /
/// <see cref="EditorPanelModel.BuildInspector"/> — the panel's rendering is a thin pooled projection
/// of that model.</para>
///
/// <para><b>Two-way selection across panels.</b> An Entities-tree row click (LEFT panel) sets
/// <c>SelectedComponent</c> on that entity (the same tag <c>SelectionSystem</c> sets from a viewport
/// click); the RIGHT Inspector panel reads that same tag and binds to it — a tree click in the left
/// panel updates the right inspector next frame. Chrome clicks are <c>OutsideViewport</c>, so
/// <c>SelectionSystem</c> never clobbers a tree selection.</para>
///
/// <para><b>Chrome, native pixels.</b> Rows are ordinary chrome entities on
/// <c>RenderTargetID.Editor</c> laid out by the pure <see cref="SystemsPanelLayout"/> and hit-tested
/// against the cursor's raw <see cref="CursorInputComponent.ScreenPosition"/>. Because the content
/// is dynamic (entities come and go; expand state changes the row count) the panel <b>pools</b>
/// row visuals sized to the visible window and re-purposes them each frame (rather than one entity
/// per row), so the entity count is bounded by the visible line count. Row entities carry no
/// <c>VisibleComponent</c> (the chrome rule). Live in BOTH transport states.</para>
///
/// <para><b>Self-protection.</b> The Systems row for this panel's own entry — and any ancestor
/// group of it — ignores enable-toggle clicks: disabling the panel through the panel would stop its
/// own hit-test, leaving no UI path back.</para>
/// </summary>
public sealed class EditorPanelSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly EditorPanelRole _role;
    private readonly ShellDragKind _scrollDrag; // this panel's scrollbar-thumb drag token (Left/Right)
    private readonly Func<(EditorPipelineRegistrar? Update, EditorPipelineRegistrar? Draw)> _pipelines;
    private readonly Func<EditorProjectInfo> _projectInfo;
    private readonly Func<IReadOnlyList<SceneCatalogEntry>> _sceneCatalog;
    private readonly Action<SceneCatalogEntry, GameState>? _selectScene;
    private readonly Func<bool> _isDirty;

    // PF-A: the editable Inspector's shared history + serializer registry (RightInspector role). Null on
    // the left panel and in pure model tests → the Inspector is view-only there (no edits/add/remove).
    private readonly EditorHistory? _history;
    private readonly ComponentSerializerRegistry? _registry;
    private readonly Func<KeyboardState> _getKeyboardState;

    private readonly EntitySet _cursorSet;
    private readonly EntitySet _sceneSet;
    private readonly EntitySet _cameraRigSet; // the UX2-E rig: infra-tagged, but an explicit tree include
    private readonly EntitySet _selectedSet;
    private readonly Entity _stateEntity; // default = not created (the state was injected)
    private readonly EditorPanelStateComponent _state;
    private readonly EditorShellStateComponent _shellState;
    private Entity _titleLabel; // RightInspector: the slim header title ("Inspector"); default for LeftTabs

    // Stable display ids for entities with no EntityInfo name (a panel-local render detail).
    private readonly Dictionary<Entity, int> _displayIds = new();
    private int _nextDisplayId = 1;

    private readonly List<PanelRow> _rows = new();
    private readonly List<RowVisual> _pool = new();
    private readonly List<TabButton> _tabs = new();
    private Entity _scrollTrack;
    private Entity _scrollThumb;
    private int _scroll;

    // PF-A editable-Inspector transient state (RightInspector role): which member (if any) is being
    // inline-edited, whether the filter field owns the keyboard, and the caret blink phase.
    private InlineEdit? _editing;
    private bool _filterFocused;
    private KeyboardState _prevKeys;
    private bool _caretVisible;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Raised on a RIGHT-button press inside this panel (UX2-D §4) — the overlay wires it (for
    /// the LEFT strip only) to open the Entities/Scenes context menu at the cursor. Null (the Inspector
    /// panel, or a composition with no context menus) makes right-clicks a no-op.</summary>
    public Action<GameState>? ContextMenuRequested { get; set; }

    /// <summary>Raised when the "+ Add component" row is clicked (PF-A §3) — the overlay wires it (for
    /// the Inspector) to open the filterable add-component popup at the cursor. Null → the row is inert.</summary>
    public Action<GameState>? AddComponentRequested { get; set; }

    /// <summary>Whether the editable Inspector currently OWNS the keyboard — the filter field is focused
    /// OR an inline member edit is open (PF-A §3). The overlay ORs this into the host keyboard's
    /// <c>ShouldSuppressInput</c> and the shortcut gate, exactly like the dialog/menu, so typing in the
    /// Inspector never fires an editor chord (G/S/R, Delete) or a game key.</summary>
    public bool OwnsKeyboard => _role == EditorPanelRole.RightInspector && (_filterFocused || _editing != null);

    /// <summary>Whether an inline member edit field is currently open (PF-A §3). Exposed for tests +
    /// tooling (the filter-focused case is the remaining half of <see cref="OwnsKeyboard"/>).</summary>
    public bool IsEditingMember => _editing != null;

    /// <summary>The current line-scroll offset (whole lines). Exposed for tests/tooling.</summary>
    public int ScrollOffset => _scroll;

    /// <summary>The pure-data panel state (collapse/expand flags). Exposed for tests.</summary>
    public EditorPanelStateComponent State => _state;

    /// <summary>The shared region-layout state (active tab, region sizes, drag ownership). Exposed
    /// for tests.</summary>
    public EditorShellStateComponent ShellState => _shellState;

    /// <summary>The last-built flat row list (post-collapse). Exposed for tests.</summary>
    public IReadOnlyList<PanelRow> Rows => _rows;

    /// <summary>One left-strip tab (Entities / Systems / Scenes) as a persistent chrome widget: a
    /// screen-baked fill mesh + a label + an Accent underline (shown only while active), with its
    /// OWN hover-fade progress (never a pooled row — pre-mortem #6).</summary>
    private sealed class TabButton
    {
        public EditorPanelTab Tab;
        public string Label = string.Empty;
        public Entity Fill;
        public Entity LabelEntity;
        public Entity Underline;
        public float HoverProgress;
        public Rectangle Bounds;
    }

    /// <summary>One pooled row's visual entities (repurposed each frame for whichever row is at
    /// this slot; unused ones are parked off-screen). <see cref="Arrow"/>, <see cref="BgFill"/> and
    /// <see cref="AccentBar"/> are screen-baked MESHES (not text/glyphs — see
    /// <see cref="ConfigureVisual"/>): identity <c>WorldMatrix</c>, native Editor target, no
    /// <c>VisibleComponent</c>. Hover/selection highlight is INSTANT on pooled rows (a fade would
    /// smear across scroll as the pool repurposes rows — pre-mortem #6).</summary>
    private sealed class RowVisual
    {
        public Entity Label;
        public Entity Checkbox;
        public Entity MinusBar;
        public Entity Arrow;
        public Entity BgFill;     // full-row background fill (hover Bg3 / selected AccentSoft)
        public Entity AccentBar;  // the selected scene row's 3pt Accent left-edge bar
        // PF-A editable Inspector: the member VALUE label (type-colored) / inline edit text (ValueLabel),
        // the filter + inline-edit field background (FieldBox, a SimpleButtonComponent), and the
        // component-row delete × glyph (DeleteGlyph, a screen-baked mesh). Parked when not applicable.
        public Entity ValueLabel;
        public Entity FieldBox;
        public Entity DeleteGlyph;
    }

    /// <summary>An open inline member edit (PF-A §3): the target component + member (by type + name) and
    /// the live edit field, plus whether the current text fails to parse (rendered <c>Danger</c>).</summary>
    private sealed class InlineEdit
    {
        public required string ComponentKey;   // the component's full type name (the row key)
        public required Type ComponentType;
        public required string Member;
        public required Type MemberType;
        public readonly EditorTextField Field = new();
        public bool Invalid;
    }

    public EditorPanelSystem(
        World world,
        ViewportManager viewportManager,
        BitmapFont? font,
        Func<(EditorPipelineRegistrar? Update, EditorPipelineRegistrar? Draw)>? pipelines = null,
        EditorShellStateComponent? shellState = null,
        Func<EditorProjectInfo>? projectInfo = null,
        Func<IReadOnlyList<SceneCatalogEntry>>? sceneCatalog = null,
        Action<SceneCatalogEntry, GameState>? selectScene = null,
        Func<bool>? isDirty = null,
        EditorPanelRole role = EditorPanelRole.LeftTabs,
        EditorPanelStateComponent? panelState = null,
        EditorHistory? history = null,
        ComponentSerializerRegistry? registry = null,
        Func<KeyboardState>? getKeyboardState = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _role = role;
        _scrollDrag = role == EditorPanelRole.LeftTabs ? ShellDragKind.LeftScrollbar : ShellDragKind.RightScrollbar;
        _pipelines = pipelines ?? (() => (null, null));
        _projectInfo = projectInfo ?? (() => default);
        _sceneCatalog = sceneCatalog ?? (() => Array.Empty<SceneCatalogEntry>());
        _selectScene = selectScene;
        _isDirty = isDirty ?? (() => false);
        _history = history;
        _registry = registry;
        _getKeyboardState = getKeyboardState ?? Keyboard.GetState;
        _shellState = shellState ?? new EditorShellStateComponent();

        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        // Scene candidates: entities with a transform, minus the editor's own machinery (chrome,
        // gizmo overlays, proxies, ghost, cursor, the gizmo/panel state entities) — hidden by default.
        _sceneSet = world.GetEntities()
            .With<TransformComponent>().Without<EditorInfrastructureComponent>().AsSet();
        // The camera rig (UX2-E) carries EditorInfrastructureComponent (so _sceneSet excludes it) but is
        // an EXPLICIT tree include — the designer must see + select the authored camera. Only the rig is
        // folded back in; all OTHER infra stays hidden (see MaterializeScene).
        _cameraRigSet = world.GetEntities()
            .With<CameraRigComponent>().With<TransformComponent>().AsSet();
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();

        // The collapse/expand state is shared by both panels: the overlay creates it once and injects
        // it into both. A lone panel (tests) creates + owns its own state entity.
        if (panelState != null)
        {
            _state = panelState;
        }
        else
        {
            _stateEntity = world.CreateEntity();
            _stateEntity.Set(new EditorInfrastructureComponent()); // survives a transport Restart
            _state = new EditorPanelStateComponent();
            _stateEntity.Set(_state);
        }

        if (_role == EditorPanelRole.LeftTabs)
        {
            // The left-strip tab bar (persistent widgets).
            foreach (var (tab, label) in new[]
                     {
                         (EditorPanelTab.Entities, "Entities"),
                         (EditorPanelTab.Systems, "Systems"),
                         (EditorPanelTab.Scenes, "Scenes"),
                     })
                _tabs.Add(new TabButton
                {
                    Tab = tab,
                    Label = label,
                    Fill = CreateMesh(EditorTheme.Depths.Button),
                    LabelEntity = CreateText(),
                    Underline = CreateMesh(EditorTheme.Depths.TabUnderline),
                });
        }
        else
        {
            // The Inspector's slim header title (no tabs) — the panel-header framework for the right
            // region: a fixed label in the region's header band.
            _titleLabel = CreateText();
        }

        // The scrollbar (screen-baked meshes) — both roles.
        _scrollTrack = CreateMesh(EditorTheme.Depths.Scrollbar);
        _scrollThumb = CreateMesh(EditorTheme.Depths.Scrollbar);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        // Live in BOTH transport states — inspecting the pipeline/scene while Playing is the point.

        var scale = _viewportManager.DevicePixelRatio;
        var panel = RegionRect(scale);
        var header = EditorChromeLayout.TabStrip(panel, scale); // the region header band (tabs or title)
        var body = EditorChromeLayout.RegionBody(panel, scale); // rows live below the header

        // The Inspector panel tracks selection changes (clearing stale expand state); the left tabbed
        // panel does not own the Inspector's expand set. It also owns the keyboard while the filter is
        // focused or a member is being inline-edited (PF-A §3) — read BEFORE building rows so this
        // frame's typed text shows immediately.
        if (_role == EditorPanelRole.RightInspector)
        {
            SyncInspectorSelection();
            _caretVisible = (state.TotalTime % 1.0) < 0.5;
            ReadInspectorKeyboard(state);
        }

        // Build the flat rows for hit-testing, handle clicks/scroll (which may mutate collapse state,
        // the selection, the active tab or the scroll), then rebuild so the visuals reflect the
        // post-click state this same frame.
        BuildRows();
        HandleInteraction(panel, header, body, scale, state);
        BuildRows();

        _scroll = SystemsPanelLayout.ClampScroll(_scroll, _rows.Count, body, scale);
        if (_role == EditorPanelRole.LeftTabs) PositionTabs(header, scale, state.Time);
        else PositionTitle(header, scale);
        PositionVisuals(body, scale);
        PositionScrollbar(body, scale);
    }

    /// <summary>This panel's region rectangle by role — the left strip or the right strip — at the
    /// shell's runtime region sizes (never a cached rect; re-read every frame).</summary>
    private Rectangle RegionRect(float scale) => _role == EditorPanelRole.LeftTabs
        ? EditorChromeLayout.LeftPanel(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale,
            _shellState.LeftWidthPt, _shellState.BottomHeightPt)
        : EditorChromeLayout.RightPanel(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale,
            _shellState.RightWidthPt, _shellState.BottomHeightPt);

    // ---- Model assembly ----------------------------------------------------

    private void SyncInspectorSelection()
    {
        var selected = FirstSelected();
        if (_state.InspectorEntity != selected)
        {
            _state.ExpandedInspectorComponents.Clear();
            _state.InspectorEntity = selected;
            _editing = null; // a stale inline edit belongs to the previous selection
            // the filter (DevTools search) persists across selections, so it is deliberately NOT cleared
        }
    }

    private void BuildRows()
    {
        _rows.Clear();

        // The dedicated Inspector panel: the selection's components + members only.
        if (_role == EditorPanelRole.RightInspector)
        {
            var sel = FirstSelected();
            IReadOnlyList<ComponentInspector.ComponentInfo>? inspector = null;
            string? inspectorTitle = null;
            if (sel.IsAlive)
            {
                inspector = ComponentInspector.Inspect(sel);
                inspectorTitle = SceneLabel(sel);
            }
            _rows.AddRange(EditorPanelModel.BuildInspector(
                _state, inspector, inspectorTitle,
                filter: _state.InspectorFilter,
                deleteAffordance: DeleteAffordanceFor,
                showAddRow: sel.IsAlive && _registry != null));
            return;
        }

        // The left tabbed panel: the active tab's body.
        var (update, draw) = _pipelines();
        var selected = FirstSelected();
        var nodes = EntitySceneTree.Build(MaterializeScene());

        // Only build the scene catalog when the Scenes tab is showing — the provider scans the
        // levels dir (filesystem IO), so it must not run every frame on the Entities/Systems tabs.
        var catalog = _shellState.ActiveLeftTab == EditorPanelTab.Scenes
            ? _sceneCatalog()
            : (IReadOnlyList<SceneCatalogEntry>)Array.Empty<SceneCatalogEntry>();
        _rows.AddRange(EditorPanelModel.Build(
            _state, _shellState.ActiveLeftTab, update, draw, nodes, SceneLabel, selected,
            ProjectInfo(), catalog, _isDirty()));
    }

    /// <summary>The Project-tab info, with the root middle-truncated to the panel's char budget so a
    /// long absolute path stays legible (head + tail visible).</summary>
    private EditorProjectInfo ProjectInfo()
    {
        var info = _projectInfo();
        // A crude char budget from the strip width (the exact pixel width varies with the font; a
        // middle ellipsis degrades gracefully if the estimate is off). The Scenes tab lives in the
        // LEFT strip now.
        var budget = Math.Max(8, _shellState.LeftWidthPt / 7);
        return info with { ProjectRoot = EditorPanelModel.MiddleEllipsis(info.ProjectRoot, budget) is { Length: > 0 } t
            ? t
            : info.ProjectRoot };
    }

    private List<Entity> MaterializeScene()
    {
        var list = new List<Entity>();
        foreach (var e in _sceneSet.GetEntities())
            list.Add(e);
        // Fold the camera rig back in (it is infra-tagged, so _sceneSet excluded it) — the ONE editor
        // infrastructure entity the tree shows, so the designer can select + inspect the authored camera.
        foreach (var e in _cameraRigSet.GetEntities())
            if (e.IsAlive && !list.Contains(e))
                list.Add(e);
        return list;
    }

    private Entity FirstSelected()
    {
        foreach (var e in _selectedSet.GetEntities())
            if (e.IsAlive) return e;
        return default;
    }

    /// <summary>An entity's tree label: its <c>EntityInfoComponent</c> name (or type), else a stable
    /// panel-local id.</summary>
    private string SceneLabel(Entity e)
    {
        if (e.Has<EntityInfoComponent>())
        {
            var info = e.Get<EntityInfoComponent>();
            if (!string.IsNullOrWhiteSpace(info.Name)) return info.Name;
            if (!string.IsNullOrWhiteSpace(info.Type)) return info.Type;
        }
        // The UX2-E camera rig has no EntityInfoComponent but is folded into the tree (MaterializeScene):
        // name it "Camera" so the designer recognizes the authored-camera row (and the Inspector title).
        if (e.Has<CameraRigComponent>()) return "Camera";
        if (!_displayIds.TryGetValue(e, out var id))
        {
            id = _nextDisplayId++;
            _displayIds[e] = id;
        }
        return $"Entity {id}";
    }

    // ---- Interaction -------------------------------------------------------

    private void HandleInteraction(Rectangle panel, Rectangle header, Rectangle body, float scale, GameState state)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);

            // Scrollbar-thumb drag owns its own presses (runs even when the cursor left the panel so
            // a fast drag keeps tracking); it shares the ONE ActiveDrag token with splitters/palette.
            HandleScrollbarDrag(body, in input, scale);

            // A drag (this scrollbar, a splitter, or the palette's scrollbar) owns the pointer this
            // frame — stand down so the drag never also fires a tab / row click (pre-mortem #3).
            if (_shellState.ActiveDrag != ShellDragKind.None) return;

            if (!panel.Contains(point)) return;

            if (input.ScrollWheelDelta != 0 && body.Contains(point))
                _scroll = SystemsPanelLayout.ClampScroll(
                    _scroll + SystemsPanelLayout.ScrollLines(input.ScrollWheelDelta), _rows.Count, body, scale);

            // Right-click inside the panel opens its context menu (UX2-D §4): the overlay resolves the
            // active tab (Entities → Add Empty Entity + the row's entity items; Scenes → Create Empty
            // Scene…) and opens at the cursor. Consume the right-press so it does not ALSO reach a
            // later system (e.g. the palette's right-click disarm). Only when a handler is wired (the
            // left strip); the Inspector panel ignores right-clicks.
            if (input.RightButtonPressed && ContextMenuRequested != null)
            {
                ContextMenuRequested(state);
                input.RightButtonPressed = false;
                input.RightButton = false;
                cursor.NotifyChanged<CursorInputComponent>();
                return;
            }

            if (!input.LeftButtonReleased) return;

            // Header band: LeftTabs → a click switches the active tab; RightInspector → the header is
            // the title band, consumed (no-op) so it never leaks to a row.
            if (header.Contains(point))
            {
                if (_role == EditorPanelRole.LeftTabs)
                {
                    var rects = ComputeTabRects(header, scale);
                    for (var i = 0; i < _tabs.Count; i++)
                        if (rects[i].Contains(point)) { SetActiveTab(_tabs[i].Tab); return; }
                }
                return;
            }

            // A click on the scrollbar track (not a thumb press) is consumed, not passed to a row.
            if (EditorScrollbar.NeedsScrollbar(_rows.Count, SystemsPanelLayout.VisibleLineCount(body, scale)) &&
                EditorScrollbar.Track(body, scale).Contains(point))
                return;

            // Rows.
            var visible = SystemsPanelLayout.VisibleLineCount(body, scale);
            for (var i = 0; i < _rows.Count; i++)
            {
                var vi = i - _scroll;
                if (vi < 0 || vi >= visible) continue;
                var line = SystemsPanelLayout.LineRect(body, vi, scale);
                if (!line.Contains(point)) continue;
                HandleClick(_rows[i], line, point, scale, state);
                return;
            }
            // A left-release inside the Inspector body but on no row cancels an inline edit / drops
            // filter focus (PF-A: "clicking elsewhere cancels").
            if (_role == EditorPanelRole.RightInspector) { _editing = null; _filterFocused = false; }
            return;
        }
    }

    /// <summary>This panel's scrollbar-thumb drag lifecycle (its own <see cref="_scrollDrag"/> token —
    /// Left/Right): claim on a thumb press, track the thumb to the cursor while held (and on the
    /// release edge), and release the shared token the frame AFTER (button fully up) so the release
    /// never also clicks a row.</summary>
    private void HandleScrollbarDrag(Rectangle body, in CursorInputComponent input, float scale)
    {
        // Clear a finished drag (button fully up).
        if (_shellState.ActiveDrag == _scrollDrag &&
            !input.LeftButton && !input.LeftButtonReleased)
            _shellState.ActiveDrag = ShellDragKind.None;

        var total = _rows.Count;
        var visible = SystemsPanelLayout.VisibleLineCount(body, scale);
        var track = EditorScrollbar.Track(body, scale);

        // Continue / finalise the owned drag.
        if (_shellState.ActiveDrag == _scrollDrag && (input.LeftButton || input.LeftButtonReleased))
        {
            var thumbTop = input.ScreenPosition.Y - _shellState.DragGrabPixel;
            _scroll = EditorScrollbar.ScrollFromThumbTop(track, total, visible, thumbTop, scale);
            return;
        }

        // Claim on a thumb press.
        if (_shellState.ActiveDrag == ShellDragKind.None && input.LeftButtonPressed &&
            EditorScrollbar.NeedsScrollbar(total, visible))
        {
            var thumb = EditorScrollbar.Thumb(track, total, visible, _scroll, scale);
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (thumb.Contains(point))
            {
                _shellState.ActiveDrag = _scrollDrag;
                _shellState.DragGrabPixel = input.ScreenPosition.Y - thumb.Y; // grab offset within the thumb
            }
        }
    }

    private Rectangle[] ComputeTabRects(Rectangle tabStrip, float scale)
    {
        var widths = new int[_tabs.Count];
        for (var i = 0; i < _tabs.Count; i++)
            widths[i] = EditorChromeLayout.TabWidth(MeasureLabel(_tabs[i].Label) * scale, scale);
        return EditorChromeLayout.TabRow(tabStrip, widths, scale);
    }

    /// <summary>A label's width in LOGICAL points (already <c>LabelScale</c>-scaled), matching the
    /// chrome builder's measure seam. A null font (layout-only tests) falls back to a char estimate.</summary>
    private float MeasureLabel(string label) =>
        (_font?.MeasureString(label).Width ?? label.Length * 8f) * EditorChromeBuilder.LabelScale;

    private void HandleClick(PanelRow row, Rectangle line, Point point, float scale, GameState state)
    {
        // PF-A: any Inspector click drops filter focus unless it hit the filter row, and cancels an open
        // inline edit unless it hit the row being edited ("clicking elsewhere cancels").
        if (_role == EditorPanelRole.RightInspector)
        {
            if (row.Kind != PanelRowKind.InspectorFilter) _filterFocused = false;
            var onEditingMember = _editing != null && row.Kind == PanelRowKind.InspectorMember
                && row.ComponentKey == _editing.ComponentKey && row.MemberName == _editing.Member;
            if (_editing != null && !onEditingMember) _editing = null;
            if (onEditingMember) return; // a click on the field being edited keeps it open
        }

        if (!row.Interactive) return;
        var onArrow = row.Collapsible && SystemsPanelLayout.ArrowRect(line, row.Depth, scale).Contains(point);

        switch (row.Kind)
        {
            case PanelRowKind.SectionHeader:
                ToggleSection(row.Section);
                break;
            case PanelRowKind.PipelineEntry:
                if (onArrow) ToggleGroupCollapsed(row.Entry!.Name);
                else TogglePipelineEnabled(row);
                break;
            case PanelRowKind.SceneEntity:
                if (onArrow) ToggleTreeEntity(row.Entity);
                else SelectEntity(row.Entity);
                break;
            case PanelRowKind.InspectorFilter:
                _filterFocused = true;
                break;
            case PanelRowKind.InspectorComponent:
                if (row.DeleteAffordance != InspectorDeleteAffordance.None
                    && SystemsPanelLayout.DeleteRect(line, scale).Contains(point))
                    DeleteComponentRow(row, state);
                else if (row.Collapsible)
                    ToggleInspectorComponentKey(row.ComponentKey!);
                break;
            case PanelRowKind.InspectorMember:
                BeginOrToggleEdit(row, state);
                break;
            case PanelRowKind.InspectorAddComponent:
                AddComponentRequested?.Invoke(state);
                break;
            case PanelRowKind.SceneCatalogEntry:
                // The dirty gate + confirm-on-switch lives in the ONE handler the overlay supplies
                // (pre-mortem #7): the panel just forwards the entry the row carries.
                if (row.CatalogEntry is { } entry) _selectScene?.Invoke(entry, state);
                break;
        }
    }

    private void TogglePipelineEnabled(PanelRow row)
    {
        if (row.Entry == null || row.Registrar == null) return;
        // Never let the panel disable itself — nor cascade itself off through an ancestor group.
        if (ContainsPanel(row.Entry)) return;
        // Gmail/Material click semantics: checked OR indeterminate → all off; unchecked → all on.
        row.Registrar.SetEnabled(row.Entry.Name, row.Entry.EnabledState == PipelineEnabledState.Off);
    }

    private bool ContainsPanel(EditorPipelineEntry entry)
    {
        if (!entry.IsGroup) return ReferenceEquals(entry.System, this);
        foreach (var child in entry.Children)
            if (ContainsPanel(child))
                return true;
        return false;
    }

    private void SelectEntity(Entity target)
    {
        if (!target.IsAlive) return;
        // Single-select, mirroring SelectionSystem: clear the previous tag, set the new one.
        List<Entity>? toClear = null;
        foreach (var e in _selectedSet.GetEntities())
            if (!e.Equals(target))
                (toClear ??= new List<Entity>()).Add(e);
        if (toClear != null)
            foreach (var e in toClear)
                if (e.IsAlive && e.Has<SelectedComponent>())
                    e.Remove<SelectedComponent>();
        if (!target.Has<SelectedComponent>())
            target.Set(new SelectedComponent());
    }

    /// <summary>The scene-tree entity whose row is under <paramref name="point"/> (device pixels), or
    /// <c>default</c> when the point is not over a <see cref="PanelRowKind.SceneEntity"/> row — the
    /// overlay uses this to build the Entities-panel context menu for the right-clicked row's entity
    /// (mirroring the left-click row hit-test). Reads the current <see cref="Rows"/> (rebuilt each frame
    /// before interaction), so it is valid during the <see cref="ContextMenuRequested"/> callback.</summary>
    public Entity EntityAtPoint(Point point)
    {
        var scale = _viewportManager.DevicePixelRatio;
        var panel = RegionRect(scale);
        var body = EditorChromeLayout.RegionBody(panel, scale);
        if (!body.Contains(point)) return default;
        var visible = SystemsPanelLayout.VisibleLineCount(body, scale);
        for (var i = 0; i < _rows.Count; i++)
        {
            var vi = i - _scroll;
            if (vi < 0 || vi >= visible) continue;
            if (SystemsPanelLayout.LineRect(body, vi, scale).Contains(point) &&
                _rows[i].Kind == PanelRowKind.SceneEntity)
                return _rows[i].Entity;
        }
        return default;
    }

    // ---- Public toggles (also the headless op-channel surface) -------------

    /// <summary>Sets the left strip's active tab (headless <c>panel:tab &lt;entities|systems|scenes&gt;</c>
    /// and the tab-bar clicks).</summary>
    public void SetActiveTab(EditorPanelTab tab) => _shellState.ActiveLeftTab = tab;

    /// <summary>Collapses/expands a section body (headless <c>panel:systems|entities</c>). Activates
    /// the tab that HOSTS the section first, so a section op issued while a different tab is showing
    /// still works.</summary>
    public void ToggleSection(EditorPanelSection section)
    {
        _shellState.ActiveLeftTab = EditorPanelModel.HostTab(section);
        switch (section)
        {
            case EditorPanelSection.Systems: _state.SystemsCollapsed = !_state.SystemsCollapsed; break;
            case EditorPanelSection.Entities: _state.EntitiesCollapsed = !_state.EntitiesCollapsed; break;
        }
    }

    /// <summary>Collapses/expands a pipeline group's children by its full name (headless
    /// <c>panel:group &lt;name&gt;</c>). A group lives in the Systems tab — activate it.</summary>
    public void ToggleGroupCollapsed(string groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return;
        _shellState.ActiveLeftTab = EditorPanelTab.Systems;
        if (!_state.CollapsedGroups.Add(groupName))
            _state.CollapsedGroups.Remove(groupName);
    }

    /// <summary>Collapses/expands a scene entity's subtree.</summary>
    public void ToggleTreeEntity(Entity entity)
    {
        if (!_state.CollapsedTreeEntities.Add(entity))
            _state.CollapsedTreeEntities.Remove(entity);
    }

    /// <summary>Shows/hides an Inspector component's member rows by its full type name (headless
    /// <c>panel:inspect &lt;typeName&gt;</c>). Accepts a short or full type name. The Inspector is the
    /// standalone right panel now (always shown), so this activates no tab.</summary>
    public void ToggleInspectorComponentKey(string componentKey)
    {
        if (string.IsNullOrEmpty(componentKey)) return;
        // Resolve a short name (e.g. "TransformComponent") to the current selection's full key.
        var key = ResolveInspectorKey(componentKey);
        if (key == null) return;
        if (!_state.ExpandedInspectorComponents.Add(key))
            _state.ExpandedInspectorComponents.Remove(key);
    }

    private string? ResolveInspectorKey(string componentKey)
    {
        var selected = FirstSelected();
        if (!selected.IsAlive) return null;
        foreach (var comp in ComponentInspector.Inspect(selected))
            if (string.Equals(comp.FullTypeName, componentKey, StringComparison.Ordinal) ||
                string.Equals(comp.TypeName, componentKey, StringComparison.Ordinal))
                return comp.FullTypeName;
        return null;
    }

    /// <summary>Selects the first scene entity whose <c>EntityInfoComponent</c> name or type matches
    /// (headless <c>panel:select &lt;name&gt;</c>). Returns whether one was found.</summary>
    public bool SelectEntityByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var e in _sceneSet.GetEntities())
        {
            if (!e.IsAlive || !e.Has<EntityInfoComponent>()) continue;
            var info = e.Get<EntityInfoComponent>();
            if (string.Equals(info.Name, name, StringComparison.Ordinal) ||
                string.Equals(info.Type, name, StringComparison.Ordinal))
            {
                _shellState.ActiveLeftTab = EditorPanelTab.Entities; // the tree lives in the Entities tab
                SelectEntity(e);
                return true;
            }
        }
        return false;
    }

    // ---- Editable Inspector (PF-A §3): keyboard, edits, add/remove, headless ops ----

    /// <summary>Reads the keyboard while the Inspector OWNS it (the filter is focused OR a member is being
    /// edited): typed chars edit the focused field, Backspace deletes, Enter commits an edit / unfocuses
    /// the filter, Escape cancels an edit / clears+unfocuses the filter. Advances the edge tracker every
    /// frame (even when not owning) so a key held across a focus change never fires as a stale press. The
    /// typed character set is the constrained lowercase set (letters/digits/<c>.</c>/<c>-</c>/<c>,</c>/
    /// space) shared with the dialog; arbitrary values arrive through the <c>inspector:edit</c> op.</summary>
    private void ReadInspectorKeyboard(GameState state)
    {
        var keys = _getKeyboardState();
        if (!OwnsKeyboard) { _prevKeys = keys; return; }

        foreach (var key in keys.GetPressedKeys())
        {
            if (_prevKeys.IsKeyDown(key)) continue; // only newly-pressed this frame
            switch (key)
            {
                case Keys.Enter: _prevKeys = keys; CommitEdit(state); return;
                case Keys.Escape: _prevKeys = keys; CancelKeyboard(); return;
                case Keys.Back: Backspace(); continue;
            }
            var c = InspectorKeyToChar(key);
            if (c != '\0') AppendChar(c);
        }
        _prevKeys = keys;

        // Live parse-validity feedback: the field shows Danger until valid or cancelled.
        if (_editing != null)
            _editing.Invalid = !InspectorValue.TryParse(_editing.MemberType, _editing.Field.Value, out _);
    }

    private void AppendChar(char c)
    {
        if (_editing != null) _editing.Field.Append(c);
        else if (_filterFocused) _state.InspectorFilter += c;
    }

    private void Backspace()
    {
        if (_editing != null) _editing.Field.Backspace();
        else if (_filterFocused && _state.InspectorFilter.Length > 0)
            _state.InspectorFilter = _state.InspectorFilter[..^1];
    }

    private void CancelKeyboard()
    {
        if (_editing != null) { _editing = null; return; }          // cancel the edit (no commit)
        if (_filterFocused) { _state.InspectorFilter = string.Empty; _filterFocused = false; } // Esc clears + unfocuses
    }

    /// <summary>Commits the open inline edit through an undoable <see cref="MemberEditCommand"/> when the
    /// text parses (else keeps the field open, marked invalid → Danger); with only the filter focused,
    /// Enter just unfocuses (keeping the search). Edit-guarded.</summary>
    private void CommitEdit(GameState state)
    {
        if (_editing == null) { _filterFocused = false; return; }
        if (!GuardEditMode(state)) { _editing = null; return; }
        if (InspectorValue.TryParse(_editing.MemberType, _editing.Field.Value, out var value))
        {
            var selected = FirstSelected();
            // Instance-children guardrail (PF-D): commit is refused on a prefab-owned child.
            if (PrefabGuards.IsPrefabOwned(selected)) { Logger.Warning(PrefabGuards.Refusal("Inspector edit")); _editing = null; return; }
            Push(MemberEditCommand.FromCurrent(selected, _editing.ComponentType, _editing.Member, value));
            _editing = null;
        }
        else
        {
            _editing.Invalid = true; // parse failure — stay open, shown in Danger
        }
    }

    /// <summary>Begins editing a member: a <b>bool</b> click toggles immediately (one undoable command),
    /// an <b>enum</b> click cycles to the next member, and a float/int/string/Vector2 opens an inline
    /// field seeded with the current value. Edit-guarded.</summary>
    private void BeginOrToggleEdit(PanelRow row, GameState state)
    {
        if (!row.MemberEditable || row.MemberType == null || row.MemberName == null || row.ComponentKey == null) return;
        if (!GuardEditMode(state)) return;
        var selected = FirstSelected();
        // Instance-children guardrail (PF-D): no inline toggle/edit on a prefab-owned child.
        if (PrefabGuards.IsPrefabOwned(selected))
        {
            Logger.Warning(PrefabGuards.Refusal("Inspector edit"));
            return;
        }
        var type = ComponentTypeByFullName(selected, row.ComponentKey);
        if (type == null) return;

        switch (InspectorValue.Kind(row.MemberType))
        {
            case InspectorValueKind.Bool:
                if (MemberEditCommand.TryReadMember(selected, type, row.MemberName, out var b))
                    Push(MemberEditCommand.FromCurrent(selected, type, row.MemberName, !(b is true)));
                break;
            case InspectorValueKind.Enum:
                if (MemberEditCommand.TryReadMember(selected, type, row.MemberName, out var e))
                    Push(MemberEditCommand.FromCurrent(selected, type, row.MemberName,
                        InspectorValue.NextEnumValue(row.MemberType, e)));
                break;
            default: // Float / Int / String / Vector2 → an inline field
                _filterFocused = false;
                _editing = new InlineEdit
                {
                    ComponentKey = row.ComponentKey,
                    ComponentType = type,
                    Member = row.MemberName,
                    MemberType = row.MemberType,
                };
                _editing.Field.Set(row.MemberValue ?? string.Empty);
                break;
        }
    }

    private void DeleteComponentRow(PanelRow row, GameState state)
    {
        if (row.ComponentKey == null) return;
        var selected = FirstSelected();
        var type = ComponentTypeByFullName(selected, row.ComponentKey);
        if (type != null) RemoveComponentType(selected, type, state);
    }

    /// <summary>Sets the Inspector filter text (headless <c>inspector:filter &lt;text&gt;</c>).</summary>
    public void SetInspectorFilter(string? text) => _state.InspectorFilter = text ?? string.Empty;

    /// <summary>Edits a member value through an undoable command (headless
    /// <c>inspector:edit &lt;Component.Member&gt; &lt;value&gt;</c>): resolves the component by registry
    /// key or type name on the current selection, parses the value for the member's type, and pushes a
    /// <see cref="MemberEditCommand"/>. Edit-guarded; loud on a miss / a bad parse.</summary>
    public void EditMember(string componentKey, string memberName, string rawValue, GameState state)
    {
        if (!GuardEditMode(state)) return;
        var selected = FirstSelected();
        // Instance-children guardrail (PF-D): a prefab-owned child's members are not editable (open the
        // prefab or Unpack); the instance ROOT is editable (its edits become overrides via the diff).
        if (PrefabGuards.IsPrefabOwned(selected))
        {
            Logger.Warning(PrefabGuards.Refusal("Inspector edit"));
            return;
        }
        var type = ResolveComponentKey(selected, componentKey);
        if (type == null)
        {
            Logger.Warning($"[level-editor] inspector:edit: the selection has no component '{componentKey}'.");
            return;
        }
        var member = MemberEditCommand.ResolveMember(type, memberName);
        if (member == null || !MemberEditCommand.IsWritable(member))
        {
            Logger.Warning($"[level-editor] inspector:edit: '{componentKey}.{memberName}' is not a writable member.");
            return;
        }
        var memberType = member is FieldInfo f
            ? f.FieldType
            : ((PropertyInfo)member).PropertyType;
        if (!InspectorValue.TryParse(memberType, rawValue, out var value))
        {
            Logger.Warning($"[level-editor] inspector:edit: '{rawValue}' is not a valid {memberType.Name}.");
            return;
        }
        Push(MemberEditCommand.FromCurrent(selected, type, memberName, value));
    }

    /// <summary>Adds a default component to the selection through an undoable
    /// <see cref="AddComponentCommand"/> (headless <c>inspector:add &lt;ComponentKey&gt;</c> + the popup
    /// pick). Refuses (loud) a present / structural type. Edit-guarded.</summary>
    public void AddComponent(string componentKey, GameState state)
    {
        if (!GuardEditMode(state)) return;
        var type = _registry?.TypeForKey(componentKey);
        if (type == null)
        {
            Logger.Warning($"[level-editor] inspector:add: no registered component '{componentKey}'.");
            return;
        }
        var selected = FirstSelected();
        if (!selected.IsAlive)
        {
            Logger.Warning("[level-editor] inspector:add: nothing is selected.");
            return;
        }
        // Instance-children guardrail (PF-D): cannot add components to a prefab-owned child.
        if (PrefabGuards.IsPrefabOwned(selected))
        {
            Logger.Warning(PrefabGuards.Refusal("Add component"));
            return;
        }
        if (EntityComponentReflection.Has(selected, type))
        {
            Logger.Warning($"[level-editor] inspector:add: the selection already has '{type.Name}'.");
            return;
        }
        if (IsStructuralType(type))
        {
            Logger.Warning($"[level-editor] inspector:add: '{type.Name}' is a structural component and cannot be added.");
            return;
        }
        Push(new AddComponentCommand(selected, type, InspectorComponentDefaults.Build(type, selected)));
    }

    /// <summary>Removes a component from the selection through an undoable
    /// <see cref="RemoveComponentCommand"/> (headless <c>inspector:remove &lt;ComponentKey&gt;</c>).
    /// Refuses <c>TransformComponent</c> + structural components (loud). Edit-guarded.</summary>
    public void RemoveComponent(string componentKey, GameState state)
    {
        var selected = FirstSelected();
        var type = _registry?.TypeForKey(componentKey) ?? ResolveComponentKey(selected, componentKey);
        if (type == null)
        {
            Logger.Warning($"[level-editor] inspector:remove: no component '{componentKey}'.");
            return;
        }
        RemoveComponentType(selected, type, state);
    }

    private void RemoveComponentType(Entity selected, Type type, GameState state)
    {
        if (!GuardEditMode(state)) return;
        // Instance-children guardrail (PF-D): covers both the headless remove and the inline × click on a
        // prefab-owned child. The instance ROOT stays editable (removal on it is a v1 no-op on reload).
        if (PrefabGuards.IsPrefabOwned(selected))
        {
            Logger.Warning(PrefabGuards.Refusal("Remove component"));
            return;
        }
        if (type == typeof(TransformComponent))
        {
            Logger.Warning("[level-editor] Remove refused: TransformComponent is the entity's single spatial component and cannot be removed.");
            return;
        }
        if (IsStructuralType(type))
        {
            Logger.Warning($"[level-editor] Remove refused: '{type.Name}' is a structural component and cannot be removed.");
            return;
        }
        var cmd = RemoveComponentCommand.Create(selected, type);
        if (cmd != null) Push(cmd);
    }

    /// <summary>The "+ Add component" candidate list for the current selection (registered MINUS present
    /// MINUS structural/never-addable) — the overlay turns these into the filterable popup's items.</summary>
    public IReadOnlyList<InspectorAddCandidates.Candidate> AddComponentCandidates()
    {
        var selected = FirstSelected();
        if (_registry == null || !selected.IsAlive) return Array.Empty<InspectorAddCandidates.Candidate>();
        var present = new HashSet<Type>();
        foreach (var comp in ComponentInspector.Inspect(selected))
            if (comp.Type != null) present.Add(comp.Type);
        return InspectorAddCandidates.Derive(_registry.RegisteredComponents(), present, _registry.IsStructural);
    }

    /// <summary>The per-component delete affordance for the Inspector (PF-A §3): structural → none,
    /// Transform → guarded (refuses), else deletable.</summary>
    private InspectorDeleteAffordance DeleteAffordanceFor(ComponentInspector.ComponentInfo comp)
    {
        var type = comp.Type;
        if (type == null) return InspectorDeleteAffordance.None;
        if (type == typeof(TransformComponent)) return InspectorDeleteAffordance.Guarded;
        if (IsStructuralType(type)) return InspectorDeleteAffordance.None;
        return InspectorDeleteAffordance.Deletable;
    }

    /// <summary>Whether a type is structural (never designer add/deletable): the registry-marked set
    /// (ChildOf / SceneEntityId / future prefab markers), with the two known types hardcoded so the rule
    /// holds even in a no-registry unit test.</summary>
    private bool IsStructuralType(Type type) =>
        type == typeof(SceneEntityIdComponent) || type == typeof(ChildOfComponent)
        || (_registry != null && _registry.IsStructural(type));

    private Type? ComponentTypeByFullName(Entity selected, string fullTypeName)
    {
        if (!selected.IsAlive) return null;
        foreach (var comp in ComponentInspector.Inspect(selected))
            if (comp.Type != null && string.Equals(comp.FullTypeName, fullTypeName, StringComparison.Ordinal))
                return comp.Type;
        return null;
    }

    /// <summary>Resolves a component key/name on the selection to its CLR type: a registry key first,
    /// else a full or short type name matched against the selection's attached components.</summary>
    private Type? ResolveComponentKey(Entity selected, string key)
    {
        var byKey = _registry?.TypeForKey(key);
        if (byKey != null) return byKey;
        if (!selected.IsAlive) return null;
        foreach (var comp in ComponentInspector.Inspect(selected))
            if (comp.Type != null &&
                (string.Equals(comp.FullTypeName, key, StringComparison.Ordinal) ||
                 string.Equals(comp.TypeName, key, StringComparison.Ordinal)))
                return comp.Type;
        return null;
    }

    private void Push(IEditorCommand? command)
    {
        if (command != null) _history?.Push(command);
    }

    private static bool GuardEditMode(GameState state)
    {
        if (state.RunMode == RunMode.Edit) return true;
        Logger.Warning("[level-editor] Inspector editing is an editing action — pause the transport first.");
        return false;
    }

    /// <summary>Poll-based key → char for the filter + inline edit fields — the constrained lowercase set
    /// (letters, digits, <c>.</c>, <c>-</c>, <c>,</c>, space) that covers numbers, Vector2 <c>"x, y"</c>,
    /// and simple identifiers. Arbitrary values (mixed case, other punctuation) go through the
    /// <c>inspector:edit</c> op.</summary>
    private static char InspectorKeyToChar(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => (char)('0' + (key - Keys.D0)),
        >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (key - Keys.NumPad0)),
        >= Keys.A and <= Keys.Z => (char)('a' + (key - Keys.A)),
        Keys.OemPeriod or Keys.Decimal => '.',
        Keys.OemMinus or Keys.Subtract => '-',
        Keys.OemComma => ',',
        Keys.Space => ' ',
        _ => '\0',
    };

    // ---- Rendering (pooled visuals) ----------------------------------------

    /// <summary>Positions the right-strip tab bar (persistent widgets): each tab's fill (active =
    /// <c>Bg1</c> merging into the body, inactive = <c>Bg0</c> with a hover fade), its label
    /// (active <c>Text0</c> / inactive <c>Text1</c>), and the active tab's 3pt <c>Accent</c>
    /// underline.</summary>
    private void PositionTabs(Rectangle tabStrip, float scale, float dt)
    {
        var rects = ComputeTabRects(tabStrip, scale);
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        for (var i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var rect = rects[i];
            tab.Bounds = rect;
            var active = _shellState.ActiveLeftTab == tab.Tab;
            var hover = !active && CursorOver(rect) && !_shellState.IsDragging;
            tab.HoverProgress = EditorTheme.AdvanceHover(tab.HoverProgress, hover, dt);

            var fill = active ? EditorTheme.Bg1 : Color.Lerp(EditorTheme.Bg0, EditorTheme.Bg2, tab.HoverProgress);
            SetMeshAt(tab.Fill, new FilledRectangleMeshGenerator(rect, fill).Generate(), EditorTheme.Depths.Button);

            if (active)
                SetMeshAt(tab.Underline,
                    new FilledRectangleMeshGenerator(EditorChromeLayout.TabUnderline(rect, scale), EditorTheme.Accent).Generate(),
                    EditorTheme.Depths.TabUnderline);
            else
                ClearMesh(tab.Underline);

            var labelPos = new Vector2(
                rect.X + EditorChromeLayout.Px(EditorChromeLayout.TabPaddingX, scale),
                rect.Y + (rect.Height - labelHeight) / 2f);
            SetText(tab.LabelEntity, tab.Label, labelPos, scale, active ? EditorTheme.Text0 : EditorTheme.Text1);
        }
    }

    /// <summary>Positions the dedicated Inspector panel's slim header title (the panel-header
    /// framework's right-region header — a fixed "Inspector" label, no tabs).</summary>
    private void PositionTitle(Rectangle header, float scale)
    {
        if (!_titleLabel.IsAlive) return;
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var labelPos = new Vector2(
            header.X + EditorChromeLayout.Px(EditorChromeLayout.TabPaddingX, scale) + EditorChromeLayout.Px(EditorChromeLayout.SplitterThickness, scale),
            header.Y + (header.Height - labelHeight) / 2f);
        SetText(_titleLabel, EditorPanelModel.InspectorTitle, labelPos, scale, EditorTheme.Text0);
    }

    /// <summary>Draws the slim scrollbar (Border track + BorderStrong thumb) when the rows overflow
    /// the body's visible window, and hides it (empties the meshes) when they fit.</summary>
    private void PositionScrollbar(Rectangle body, float scale)
    {
        var total = _rows.Count;
        var visible = SystemsPanelLayout.VisibleLineCount(body, scale);
        if (!EditorScrollbar.NeedsScrollbar(total, visible))
        {
            ClearMesh(_scrollTrack);
            ClearMesh(_scrollThumb);
            return;
        }

        var track = EditorScrollbar.Track(body, scale);
        var thumb = EditorScrollbar.Thumb(track, total, visible, _scroll, scale);
        SetMeshAt(_scrollTrack, new FilledRectangleMeshGenerator(track, EditorTheme.Border).Generate(),
            EditorTheme.Depths.Scrollbar);
        SetMeshAt(_scrollThumb, new FilledRectangleMeshGenerator(thumb, EditorTheme.BorderStrong).Generate(),
            EditorTheme.Depths.Scrollbar);
    }

    private void PositionVisuals(Rectangle body, float scale)
    {
        var visible = SystemsPanelLayout.VisibleLineCount(body, scale);
        EnsurePool(visible);
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var hoveredRow = HoveredRow(body, scale, visible);

        for (var i = 0; i < _pool.Count; i++)
        {
            var visual = _pool[i];
            var rowIndex = _scroll + i;
            if (i >= visible || rowIndex >= _rows.Count)
            {
                ParkAll(visual);
                continue;
            }

            var row = _rows[rowIndex];
            var line = SystemsPanelLayout.LineRect(body, i, scale);
            ConfigureVisual(visual, row, line, scale, labelHeight, rowIndex == hoveredRow);
        }
    }

    private int HoveredRow(Rectangle body, float scale, int visible)
    {
        if (_shellState.IsDragging) return -1;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!body.Contains(point)) return -1;
            for (var vi = 0; vi < visible; vi++)
                if (SystemsPanelLayout.LineRect(body, vi, scale).Contains(point))
                    return _scroll + vi;
            return -1;
        }
        return -1;
    }

    private bool CursorOver(Rectangle rect)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            return rect.Contains(new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y));
        }
        return false;
    }

    private void ConfigureVisual(RowVisual visual, PanelRow row, Rectangle line, float scale,
        float labelHeight, bool hovered)
    {
        // Row background + selected accent bar — screen-baked meshes on the Editor target (identity
        // WorldMatrix, like the arrows). Selected scene row = AccentSoft fill + a 3pt Accent left bar;
        // a hovered interactive row = Bg3 fill (INSTANT — a pooled row must not fade, or the highlight
        // smears across scroll as the pool repurposes rows). Otherwise both meshes are emptied.
        var selectedRow = (row.Kind == PanelRowKind.SceneEntity || row.Kind == PanelRowKind.SceneCatalogEntry)
                          && row.Selected;
        if (selectedRow)
        {
            SetMeshAt(visual.BgFill, new FilledRectangleMeshGenerator(line, EditorTheme.AccentSoft).Generate(),
                EditorTheme.Depths.RowFill);
            var bar = new Rectangle(line.X, line.Y, Math.Max(1, EditorChromeLayout.Px(3, scale)), line.Height);
            SetMeshAt(visual.AccentBar, new FilledRectangleMeshGenerator(bar, EditorTheme.Accent).Generate(),
                EditorTheme.Depths.RowAccentBar);
        }
        else if (hovered && row.Interactive)
        {
            SetMeshAt(visual.BgFill, new FilledRectangleMeshGenerator(line, EditorTheme.Bg3).Generate(),
                EditorTheme.Depths.RowFill);
            ClearMesh(visual.AccentBar);
        }
        else
        {
            ClearMesh(visual.BgFill);
            ClearMesh(visual.AccentBar);
        }

        // Arrow (disclosure) — a filled triangle MESH (not a font glyph), so the indicator never
        // depends on the BitmapFont's Unicode coverage. Right-pointing ▸ collapsed, down ▾ expanded.
        if (row.Collapsible)
        {
            var arrow = SystemsPanelLayout.ArrowRect(line, row.Depth, scale);
            var tri = SystemsPanelLayout.ArrowTriangle(arrow, row.Expanded);
            SetMeshAt(visual.Arrow,
                new FilledTriangleMeshGenerator(tri[0], tri[1], tri[2], RowColor(row)).Generate(),
                EditorTheme.Depths.Label);
        }
        else
        {
            ClearMesh(visual.Arrow);
        }

        // Checkbox + minus bar — only for pipeline rows.
        if (row.HasCheckbox)
        {
            var box = SystemsPanelLayout.CheckboxRect(line, row.Depth, scale);
            Place(visual.Checkbox, new Vector2(box.X, box.Y));
            Resize(visual.Checkbox, box);
            SetFill(visual.Checkbox, row.CheckboxState == PipelineEnabledState.Off
                ? Color.Transparent            // "no fill" — allowlisted; the A==0 mesh is skipped
                : EditorTheme.Success);        // checkbox-on stays "on/enabled" green

            if (row.ShowMinusBar)
            {
                var bar = SystemsPanelLayout.MinusBarRect(box, scale);
                Place(visual.MinusBar, new Vector2(bar.X, bar.Y));
                Resize(visual.MinusBar, bar);
                SetFill(visual.MinusBar, EditorTheme.Bg1); // dark bar reads against the Success checkbox
            }
            else
            {
                Park(visual.MinusBar);
            }
        }
        else
        {
            Park(visual.Checkbox);
            Park(visual.MinusBar);
        }

        // PF-A editable-Inspector extras: the delete × (component rows), the type-colored value / inline
        // edit field (member rows), and the filter field background. Parks them for every other row.
        ConfigureInspectorExtras(visual, row, line, scale, labelHeight, hovered);

        // Label. The filter row draws its text (or placeholder) INSIDE the field; every other row draws
        // its label at the normal position in its kind color.
        if (row.Kind == PanelRowKind.InspectorFilter)
        {
            var field = SystemsPanelLayout.InspectorFieldRect(line, row.Depth, scale);
            var hasText = !string.IsNullOrEmpty(_state.InspectorFilter);
            var shown = hasText ? _state.InspectorFilter : "Filter";
            if (_filterFocused && _caretVisible) shown = (hasText ? _state.InspectorFilter : string.Empty) + "|";
            var pos = new Vector2(field.X + EditorChromeLayout.Px(6, scale), field.Y + (field.Height - labelHeight) / 2f);
            SetText(visual.Label, shown, pos, scale, hasText ? EditorTheme.Text0 : EditorTheme.TextMuted);
        }
        else
        {
            var labelPos = row.HasCheckbox
                ? SystemsPanelLayout.LabelPosition(line, labelHeight, row.Depth, scale)
                : SystemsPanelLayout.LabelPositionNoCheckbox(line, labelHeight, row.Depth, scale);
            SetText(visual.Label, row.Label, labelPos, scale, RowColor(row));
        }
    }

    /// <summary>Configures the editable-Inspector's per-row extras (PF-A §3): the filter field background,
    /// the component delete × mesh, and the member value / inline edit field. Parks all three for every
    /// non-applicable row (and every left-panel row).</summary>
    private void ConfigureInspectorExtras(RowVisual visual, PanelRow row, Rectangle line, float scale,
        float labelHeight, bool hovered)
    {
        Park(visual.FieldBox);
        Park(visual.ValueLabel);
        ClearMesh(visual.DeleteGlyph);

        switch (row.Kind)
        {
            case PanelRowKind.InspectorFilter:
            {
                var field = SystemsPanelLayout.InspectorFieldRect(line, row.Depth, scale);
                PlaceField(visual.FieldBox, field, _filterFocused ? EditorTheme.Bg3 : EditorTheme.Bg2);
                break; // the text itself is drawn by the caller's filter-label branch
            }
            case PanelRowKind.InspectorComponent when row.DeleteAffordance != InspectorDeleteAffordance.None:
            {
                var box = SystemsPanelLayout.DeleteRect(line, scale);
                // Deletable: Border at rest → Danger on row hover. Guarded (Transform): a static muted ×
                // (it refuses on click with a status hint).
                var color = row.DeleteAffordance == InspectorDeleteAffordance.Guarded
                    ? EditorTheme.TextMuted
                    : hovered ? EditorTheme.Danger : EditorTheme.Border;
                var mesh = new CompositeMeshGenerator();
                foreach (var tri in SystemsPanelLayout.CrossTriangles(box, MathF.Max(1.5f, 1.5f * scale)))
                    mesh.Add(new FilledTriangleMeshGenerator(tri[0], tri[1], tri[2], color));
                SetMeshAt(visual.DeleteGlyph, mesh.Generate(), EditorTheme.Depths.Label);
                break;
            }
            case PanelRowKind.InspectorMember:
            {
                var valueRect = SystemsPanelLayout.MemberValueRect(line, row.Depth, scale);
                var pos = new Vector2(valueRect.X + EditorChromeLayout.Px(6, scale),
                    valueRect.Y + (valueRect.Height - labelHeight) / 2f);
                var editing = _editing != null
                    && _editing.ComponentKey == row.ComponentKey && _editing.Member == row.MemberName;
                if (editing)
                {
                    PlaceField(visual.FieldBox, valueRect, EditorTheme.Bg3);
                    var text = _editing!.Field.Value + (_caretVisible ? "|" : string.Empty);
                    SetText(visual.ValueLabel, text, pos, scale,
                        _editing.Invalid ? EditorTheme.Danger : EditorTheme.Text0);
                }
                else
                {
                    SetText(visual.ValueLabel, row.MemberValue ?? string.Empty, pos, scale,
                        InspectorValue.ForRole(row.ValueRole));
                }
                break;
            }
        }
    }

    /// <summary>Positions + sizes a field-background box (a <see cref="SimpleButtonComponent"/>) and sets
    /// its fill — the filter field and the inline edit field.</summary>
    private static void PlaceField(Entity box, Rectangle rect, Color fill)
    {
        Place(box, new Vector2(rect.X, rect.Y));
        Resize(box, rect);
        SetFill(box, fill);
    }

    /// <summary>The label/arrow text color for a row, by kind (hover + selection are conveyed by the
    /// row background fill + accent bar now, so the label color is hover-independent — Text0 primary,
    /// Text1 subtitles/headers, TextMuted de-emphasized, TextDisabled for an off pipeline entry).</summary>
    private static Color RowColor(PanelRow row) => row.Kind switch
    {
        PanelRowKind.SectionHeader => EditorTheme.Text0,
        PanelRowKind.PipelineSubheader => EditorTheme.Text1,
        PanelRowKind.PipelineEntry => row.CheckboxState != PipelineEnabledState.Off
            ? EditorTheme.Text0
            : EditorTheme.TextDisabled,
        PanelRowKind.SceneEntity => EditorTheme.Text0,
        PanelRowKind.InspectorComponent => EditorTheme.Text0,
        // The member NAME part reads secondary (Text1); its VALUE is drawn separately, type-colored.
        PanelRowKind.InspectorMember => EditorTheme.Text1,
        // The "+ Add component" affordance is the primary action (Accent), like a primary dialog action.
        PanelRowKind.InspectorAddComponent => EditorTheme.Accent,
        // The filter row's label is set explicitly (text vs placeholder) — this fallback is unused.
        PanelRowKind.InspectorFilter => EditorTheme.Text0,
        // The current scene's dirty marker (● prefix) is drawn in Warning; a clean catalog row is Text0.
        PanelRowKind.SceneCatalogEntry => row.DirtyMarker ? EditorTheme.Warning : EditorTheme.Text0,
        _ => EditorTheme.TextMuted,
    };

    private void EnsurePool(int count)
    {
        while (_pool.Count < count)
            _pool.Add(new RowVisual
            {
                Label = CreateText(),
                Checkbox = CreateBox(SystemsPanelLayout.CheckboxSize, SystemsPanelLayout.CheckboxSize,
                    lineThickness: 1.5f, outline: EditorTheme.BorderStrong, depth: EditorTheme.Depths.Button),
                MinusBar = CreateBox(SystemsPanelLayout.MinusBarWidth, SystemsPanelLayout.MinusBarHeight,
                    lineThickness: 0f, outline: Color.Transparent, depth: EditorTheme.Depths.CheckboxMark),
                Arrow = CreateMesh(EditorTheme.Depths.Label),
                BgFill = CreateMesh(EditorTheme.Depths.RowFill),
                AccentBar = CreateMesh(EditorTheme.Depths.RowAccentBar),
                // PF-A editable Inspector: the value/edit text, the filter/edit field background, and the
                // component-row delete × mesh. Created for both roles (parked on non-Inspector rows).
                ValueLabel = CreateText(),
                FieldBox = CreateBox(SystemsPanelLayout.CheckboxSize, SystemsPanelLayout.CheckboxSize,
                    lineThickness: 1f, outline: EditorTheme.Border, depth: EditorTheme.Depths.Button),
                DeleteGlyph = CreateMesh(EditorTheme.Depths.Label),
            });
    }

    /// <summary>Creates a screen-baked MESH entity: a raw <see cref="DrawComponent"/> the panel bakes
    /// geometry into each frame (mirroring the gizmo overlays' screen-baked meshes) — identity
    /// <c>WorldMatrix</c>, native Editor target at <paramref name="depth"/>, no <c>VisibleComponent</c>
    /// (the chrome rule) and no <c>SimpleButtonComponent</c> (so <c>ButtonMeshPrepSystem</c> never
    /// touches it). Used for disclosure arrows, row background fills, and the selected-row accent bar.</summary>
    private Entity CreateMesh(float depth)
    {
        var mesh = _world.CreateEntity();
        mesh.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        mesh.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        mesh.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = depth,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });
        return mesh;
    }

    private static void SetMeshAt(Entity e, MeshData mesh, float depth)
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

    /// <summary>Hides a screen-baked mesh by emptying it (an invalid mesh is skipped by
    /// <c>MasterRenderSystem</c>) — the mesh analog of parking a text/box entity off-screen.</summary>
    private static void ClearMesh(Entity e)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Vertices = Array.Empty<VertexPositionColor>();
        dc.Indices = Array.Empty<int>();
    }

    private Entity CreateText()
    {
        var text = _world.CreateEntity();
        text.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        text.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        text.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Label,
            TextContent = string.Empty,
            Font = _font!,
            Color = EditorTheme.Text0,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        // NOTE: no VisibleComponent — chrome rule.
        return text;
    }

    private Entity CreateBox(int width, int height, float lineThickness, Color outline, float depth)
    {
        var box = _world.CreateEntity();
        box.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        box.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        box.Set(new SimpleButtonComponent
        {
            Size = new Vector2(width, height),
            LineThickness = lineThickness,
            Color = outline,
            FillColor = Color.Transparent, // ConfigureVisual sets it per frame
            Target = RenderTargetID.Editor,
            LayerDepth = depth,
        });
        // NOTE: no VisibleComponent (chrome rule), no ToolbarButtonComponent (not a toolbar button).
        return box;
    }

    private static void ParkAll(RowVisual visual)
    {
        Park(visual.Label);
        Park(visual.Checkbox);
        Park(visual.MinusBar);
        Park(visual.ValueLabel);
        Park(visual.FieldBox);
        // Screen-baked meshes carry their position in their vertices (identity matrix), so parking
        // the transform does nothing — empty the mesh to hide it.
        ClearMesh(visual.Arrow);
        ClearMesh(visual.BgFill);
        ClearMesh(visual.AccentBar);
        ClearMesh(visual.DeleteGlyph);
    }

    private static void Park(Entity entity) => Place(entity, SystemsPanelLayout.ParkedPosition);

    private static void Place(Entity entity, Vector2 position)
    {
        ref var transform = ref entity.Get<TransformComponent>();
        transform.Position = position;
        entity.NotifyChanged<TransformComponent>();
    }

    private static void Resize(Entity entity, Rectangle rect)
    {
        ref var visual = ref entity.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(rect.Width, rect.Height);
    }

    private static void SetFill(Entity entity, Color fill)
    {
        ref var visual = ref entity.Get<SimpleButtonComponent>();
        visual.FillColor = fill;
    }

    private static void SetText(Entity entity, string content, Vector2 position, float scale, Color color)
    {
        Place(entity, position);
        ref var text = ref entity.Get<DynamicTextComponent>();
        text.TextContent = content;
        text.Scale = EditorChromeBuilder.LabelScale * scale;
        text.Color = color;
    }

    public void Dispose()
    {
        foreach (var visual in _pool)
        {
            if (visual.Label.IsAlive) visual.Label.Dispose();
            if (visual.Checkbox.IsAlive) visual.Checkbox.Dispose();
            if (visual.MinusBar.IsAlive) visual.MinusBar.Dispose();
            if (visual.Arrow.IsAlive) visual.Arrow.Dispose();
            if (visual.BgFill.IsAlive) visual.BgFill.Dispose();
            if (visual.AccentBar.IsAlive) visual.AccentBar.Dispose();
            if (visual.ValueLabel.IsAlive) visual.ValueLabel.Dispose();
            if (visual.FieldBox.IsAlive) visual.FieldBox.Dispose();
            if (visual.DeleteGlyph.IsAlive) visual.DeleteGlyph.Dispose();
        }
        _pool.Clear();
        foreach (var tab in _tabs)
        {
            if (tab.Fill.IsAlive) tab.Fill.Dispose();
            if (tab.LabelEntity.IsAlive) tab.LabelEntity.Dispose();
            if (tab.Underline.IsAlive) tab.Underline.Dispose();
        }
        _tabs.Clear();
        if (_titleLabel.IsAlive) _titleLabel.Dispose();
        if (_scrollTrack.IsAlive) _scrollTrack.Dispose();
        if (_scrollThumb.IsAlive) _scrollThumb.Dispose();
        if (_stateEntity.IsAlive) _stateEntity.Dispose();
        _cursorSet.Dispose();
        _sceneSet.Dispose();
        _cameraRigSet.Dispose();
        _selectedSet.Dispose();
    }
}
