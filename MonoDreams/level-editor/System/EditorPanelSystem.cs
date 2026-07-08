#nullable enable
using System;
using System.Collections.Generic;
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
using MonoDreams.LevelEditor.Inspector;
using MonoDreams.LevelEditor.UI;
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

    private readonly EntitySet _cursorSet;
    private readonly EntitySet _sceneSet;
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

    public bool IsEnabled { get; set; } = true;

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
        EditorPanelStateComponent? panelState = null)
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
        _shellState = shellState ?? new EditorShellStateComponent();

        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        // Scene candidates: entities with a transform, minus the editor's own machinery (chrome,
        // gizmo overlays, proxies, ghost, cursor, the gizmo/panel state entities) — hidden by default.
        _sceneSet = world.GetEntities()
            .With<TransformComponent>().Without<EditorInfrastructureComponent>().AsSet();
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
        // panel does not own the Inspector's expand set.
        if (_role == EditorPanelRole.RightInspector) SyncInspectorSelection();

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
            _rows.AddRange(EditorPanelModel.BuildInspector(_state, inspector, inspectorTitle));
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
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
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
            case PanelRowKind.InspectorComponent:
                if (row.Collapsible) ToggleInspectorComponentKey(row.ComponentKey!);
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

        // Label.
        var labelPos = row.HasCheckbox
            ? SystemsPanelLayout.LabelPosition(line, labelHeight, row.Depth, scale)
            : SystemsPanelLayout.LabelPositionNoCheckbox(line, labelHeight, row.Depth, scale);
        SetText(visual.Label, row.Label, labelPos, scale, RowColor(row));
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
        PanelRowKind.InspectorMember => EditorTheme.TextMuted,
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
        // Screen-baked meshes carry their position in their vertices (identity matrix), so parking
        // the transform does nothing — empty the mesh to hide it.
        ClearMesh(visual.Arrow);
        ClearMesh(visual.BgFill);
        ClearMesh(visual.AccentBar);
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
        _selectedSet.Dispose();
    }
}
