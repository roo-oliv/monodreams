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

/// <summary>
/// The editor's <b>right-strip panel</b> (extends the Wave-8 systems panel): a single vertically
/// scrolling stack of three collapsible <b>sections</b> — <b>Systems</b> (every
/// <see cref="EditorPipelineRegistrar"/> entry of both pipelines, in order, with per-<b>group</b>
/// collapse and the live tri-state enabled toggle), <b>Scene</b> (the world's entities as a
/// parent/child tree, selectable both ways with the viewport), and <b>Inspector</b> (the selected
/// entity's attached components, each expandable to its member values). It keeps the entry name
/// <c>editor.systemsPanel</c> and the overlay's <c>SystemsPanel</c> hook so every screen weaves it
/// unchanged.
///
/// <para><b>Sections + groups collapse.</b> Each section header toggles its whole body; a Systems
/// group row's arrow toggles its children; a Scene entity row's arrow toggles its subtree; an
/// Inspector component row toggles its member rows. In-session state lives in the pure-data
/// <see cref="EditorPanelStateComponent"/> on an editor-infra entity (ECS purity), and the flat row
/// list is assembled purely by <see cref="EditorPanelModel.Build"/> — the panel's rendering is a
/// thin pooled projection of that model.</para>
///
/// <para><b>Two-way selection.</b> A Scene row click sets <c>SelectedComponent</c> on that entity
/// (the same tag <c>SelectionSystem</c> sets from a viewport click); the currently-selected entity
/// is highlighted in the tree and drives the Inspector. Chrome clicks are <c>OutsideViewport</c>,
/// so <c>SelectionSystem</c> never clobbers a tree selection.</para>
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
    private readonly Func<(EditorPipelineRegistrar? Update, EditorPipelineRegistrar? Draw)> _pipelines;

    private readonly EntitySet _cursorSet;
    private readonly EntitySet _sceneSet;
    private readonly EntitySet _selectedSet;
    private readonly Entity _stateEntity;
    private readonly EditorPanelStateComponent _state;

    // Stable display ids for entities with no EntityInfo name (a panel-local render detail).
    private readonly Dictionary<Entity, int> _displayIds = new();
    private int _nextDisplayId = 1;

    private readonly List<PanelRow> _rows = new();
    private readonly List<RowVisual> _pool = new();
    private int _scroll;

    public bool IsEnabled { get; set; } = true;

    /// <summary>The current line-scroll offset (whole lines). Exposed for tests/tooling.</summary>
    public int ScrollOffset => _scroll;

    /// <summary>The pure-data panel state (collapse/expand flags). Exposed for tests.</summary>
    public EditorPanelStateComponent State => _state;

    /// <summary>The last-built flat row list (post-collapse). Exposed for tests.</summary>
    public IReadOnlyList<PanelRow> Rows => _rows;

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
        Func<(EditorPipelineRegistrar? Update, EditorPipelineRegistrar? Draw)> pipelines)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _font = font; // null = layout-only (tests run no text prep, mirroring EditorChromeBuilder's seam)
        _pipelines = pipelines ?? throw new ArgumentNullException(nameof(pipelines));

        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        // Scene candidates: entities with a transform, minus the editor's own machinery (chrome,
        // gizmo overlays, proxies, ghost, cursor, the gizmo/panel state entities) — hidden by default.
        _sceneSet = world.GetEntities()
            .With<TransformComponent>().Without<EditorInfrastructureComponent>().AsSet();
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();

        _stateEntity = world.CreateEntity();
        _stateEntity.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        _state = new EditorPanelStateComponent();
        _stateEntity.Set(_state);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        // Live in BOTH transport states — inspecting the pipeline/scene while Playing is the point.

        var scale = _viewportManager.DevicePixelRatio;
        var panel = EditorChromeLayout.RightPanel(
            _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);

        SyncInspectorSelection();

        // Build the flat rows for hit-testing, handle clicks/scroll (which may mutate collapse state
        // or the selection), then rebuild so the visuals reflect the post-click state this same frame.
        BuildRows();
        HandleInteraction(panel, scale);
        BuildRows();

        _scroll = SystemsPanelLayout.ClampScroll(_scroll, _rows.Count, panel, scale);
        PositionVisuals(panel, scale);
    }

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
        var (update, draw) = _pipelines();
        var selected = FirstSelected();

        _rows.Clear();
        var nodes = EntitySceneTree.Build(MaterializeScene());
        IReadOnlyList<ComponentInspector.ComponentInfo>? inspector = null;
        string? inspectorTitle = null;
        if (selected.IsAlive)
        {
            inspector = ComponentInspector.Inspect(selected);
            inspectorTitle = SceneLabel(selected);
        }

        _rows.AddRange(EditorPanelModel.Build(
            _state, update, draw, nodes, SceneLabel, selected, inspector, inspectorTitle));
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

    private void HandleInteraction(Rectangle panel, float scale)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!panel.Contains(point)) return;

            if (input.ScrollWheelDelta != 0)
                _scroll = SystemsPanelLayout.ClampScroll(
                    _scroll + SystemsPanelLayout.ScrollLines(input.ScrollWheelDelta), _rows.Count, panel, scale);

            if (!input.LeftButtonReleased) return;

            var visible = SystemsPanelLayout.VisibleLineCount(panel, scale);
            for (var i = 0; i < _rows.Count; i++)
            {
                var vi = i - _scroll;
                if (vi < 0 || vi >= visible) continue;
                var line = SystemsPanelLayout.LineRect(panel, vi, scale);
                if (!line.Contains(point)) continue;
                HandleClick(_rows[i], line, point, scale);
                return;
            }
            return;
        }
    }

    private void HandleClick(PanelRow row, Rectangle line, Point point, float scale)
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

    /// <summary>Collapses/expands a section body (headless <c>panel:systems|scene|inspector</c>).</summary>
    public void ToggleSection(EditorPanelSection section)
    {
        switch (section)
        {
            case EditorPanelSection.Systems: _state.SystemsCollapsed = !_state.SystemsCollapsed; break;
            case EditorPanelSection.Scene: _state.SceneCollapsed = !_state.SceneCollapsed; break;
            case EditorPanelSection.Inspector: _state.InspectorCollapsed = !_state.InspectorCollapsed; break;
        }
    }

    /// <summary>Collapses/expands a pipeline group's children by its full name (headless
    /// <c>panel:group &lt;name&gt;</c>).</summary>
    public void ToggleGroupCollapsed(string groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return;
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
    /// <c>panel:inspect &lt;typeName&gt;</c>). Accepts a short or full type name.</summary>
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
                SelectEntity(e);
                return true;
            }
        }
        return false;
    }

    // ---- Rendering (pooled visuals) ----------------------------------------

    private void PositionVisuals(Rectangle panel, float scale)
    {
        var visible = SystemsPanelLayout.VisibleLineCount(panel, scale);
        EnsurePool(visible);
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;
        var hoveredRow = HoveredRow(panel, scale, visible);

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
            var line = SystemsPanelLayout.LineRect(panel, i, scale);
            ConfigureVisual(visual, row, line, scale, labelHeight, rowIndex == hoveredRow);
        }
    }

    private int HoveredRow(Rectangle panel, float scale, int visible)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!panel.Contains(point)) return -1;
            for (var vi = 0; vi < visible; vi++)
                if (SystemsPanelLayout.LineRect(panel, vi, scale).Contains(point))
                    return _scroll + vi;
            return -1;
        }
        return -1;
    }

    private void ConfigureVisual(RowVisual visual, PanelRow row, Rectangle line, float scale,
        float labelHeight, bool hovered)
    {
        // Row background + selected accent bar — screen-baked meshes on the Editor target (identity
        // WorldMatrix, like the arrows). Selected scene row = AccentSoft fill + a 3pt Accent left bar;
        // a hovered interactive row = Bg3 fill (INSTANT — a pooled row must not fade, or the highlight
        // smears across scroll as the pool repurposes rows). Otherwise both meshes are emptied.
        var selectedRow = row.Kind == PanelRowKind.SceneEntity && row.Selected;
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
        if (_stateEntity.IsAlive) _stateEntity.Dispose();
        _cursorSet.Dispose();
        _sceneSet.Dispose();
        _selectedSet.Dispose();
    }
}
