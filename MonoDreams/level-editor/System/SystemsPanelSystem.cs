#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.Serialization;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor's <b>right-column panel</b>: a vertical stack of three collapsible
/// <see cref="PanelSection"/>s in the shell's right strip, each header toggling its whole body.
/// (The registrar entry / overlay property keep the historical <c>systemsPanel</c> name — the
/// SYSTEMS section is its original content — so no screen wiring changes; a rename is a documented
/// follow-up.)
///
/// <list type="number">
///   <item><b>SYSTEMS</b> — the live listing of EVERY <see cref="EditorPipelineRegistrar"/> entry of
///   BOTH pipelines (update + draw), in execution order, as a tree: groups render indented above
///   their children (<see cref="EditorPipelineEntry.Depth"/>) with <b>tri-state</b> checkboxes
///   (all/none/mixed — the mixed state is the Gmail/Material minus bar), and every row's checkbox
///   flips the entry through <see cref="EditorPipelineRegistrar.SetEnabled"/> (a master switch: off
///   in BOTH modes; group click = the Gmail cascade). <b>Group rows additionally carry a
///   collapse caret</b> (in the reserved left caret column) that hides/shows the group's children
///   — the documented Wave-8a deferral, now shipped.</item>
///   <item><b>SCENE</b> — the world's entities as a tree (<see cref="SceneTreeBuilder"/>: roots
///   first, <c>ChildOfComponent</c> descendants indented, editor-infrastructure hidden). A row click
///   selects the entity (<see cref="SelectedComponent"/>, single-select mirroring
///   <c>SelectionSystem</c>); the currently-selected entity's row is highlighted — two-way with the
///   viewport selection.</item>
///   <item><b>INSPECTOR</b> — the selected entity's attached components
///   (<see cref="ComponentInspector"/>, via DefaultEcs component reflection), each a collapsible row
///   that expands to its public member "name: value" rows (read-only).</item>
/// </list>
///
/// <para><b>One flat scroll.</b> The three sections concatenate into one flat line list scrolled as
/// a whole by the mouse wheel over the strip (the systems-panel scroll model, reused for the tree);
/// a collapsed section's / group's / component's hidden rows and every scrolled-out row are parked
/// off-screen (GPU-clipped) so nothing bleeds over the top/bottom bars.</para>
///
/// <para><b>Chrome, native pixels.</b> Rows are ordinary chrome entities on
/// <c>RenderTargetID.Editor</c> (checkboxes/marks are fill <see cref="SimpleButtonComponent"/> meshes,
/// labels + carets are <see cref="DynamicTextComponent"/>), laid out by the pure
/// <see cref="SystemsPanelLayout"/> inside <see cref="EditorChromeLayout.RightPanel"/> and hit-tested
/// against the cursor's raw <see cref="CursorInputComponent.ScreenPosition"/>. Per the chrome rule the
/// row entities carry <b>no</b> <c>VisibleComponent</c>, and (like the transport buttons) they are
/// live in BOTH transport states. Collapse/expand + component-expand state lives in the pure-data
/// <see cref="EditorPanelStateComponent"/> (ECS purity); the SYSTEMS rows are built once (registrar
/// entries are fixed after <c>Build()</c>), the dynamic SCENE + INSPECTOR rows rebuild only when the
/// entity set / selection / component set changes.</para>
///
/// <para><b>Self-protection.</b> The SYSTEMS row for this system's own entry — and any ancestor
/// group of it — ignores enable clicks: disabling the panel through the panel would stop its own
/// hit-test with no UI path back. (Collapse never disables a system, so it is always allowed.)</para>
///
/// <para><b>Headless.</b> The public <see cref="ToggleSection"/> / <see cref="ToggleGroup"/> /
/// <see cref="ToggleComponent"/> / <see cref="SelectEntityByLabel"/> methods let the editor-op channel
/// drive the sections/tree/inspector without a layout-dependent click (see
/// <c>EditorOverlay.DispatchNamedAction</c>'s <c>panel:</c> grammar).</para>
/// </summary>
public sealed class SystemsPanelSystem : ISystem<GameState>
{
    private const string SystemsHeader = "SYSTEMS";
    private const string SceneHeader = "SCENE";
    private const string InspectorHeader = "INSPECTOR";
    private const string UpdateSubHeader = "UPDATE";
    private const string DrawSubHeader = "DRAW";

    // Disclosure carets (Unicode triangles; cosmetic — a font lacking them renders a blank caret box,
    // never a crash, and the caret column click still works). Swap here to change the look.
    private const string CaretExpanded = "▾";  // ▾
    private const string CaretCollapsed = "▸"; // ▸

    private readonly World _world;
    private readonly ViewportManager _viewportManager;
    private readonly BitmapFont? _font;
    private readonly Func<(EditorPipelineRegistrar? Update, EditorPipelineRegistrar? Draw)> _pipelines;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _allEntities;

    private enum LineKind { SectionHeader, SubHeader, PipelineEntry, SceneEntity, InspectorComponent, InspectorMember, Info }

    /// <summary>One panel line and the chrome entities that render it.</summary>
    private sealed class Line
    {
        public required LineKind Kind;
        public required string Label;
        public int Depth;
        public PanelSection Section;                 // SectionHeader
        public EditorPipelineEntry? Entry;           // PipelineEntry
        public EditorPipelineRegistrar? Registrar;   // PipelineEntry
        public Entity SceneEntity;                   // SceneEntity
        public string? ComponentKey;                 // InspectorComponent (Type.FullName)
        public Entity LabelEntity;
        public Entity CaretEntity;    // collapsible rows only
        public Entity CheckboxEntity; // PipelineEntry only
        public Entity MarkEntity;     // PipelineEntry groups only (minus bar)
    }

    private Line _systemsHeaderLine = null!;
    private Line _sceneHeaderLine = null!;
    private Line _inspectorHeaderLine = null!;
    private readonly List<Line> _pipelineLines = new();
    private readonly List<Line> _sceneLines = new();
    private readonly List<Line> _inspectorLines = new();

    private EditorPanelStateComponent _state = null!;
    private Entity _stateEntity;
    private bool _headersBuilt;
    private bool _pipelineBuilt;
    private int _scroll;

    // Dirty tracking for the dynamic (SCENE + INSPECTOR) sections — rebuilt only when their source
    // changed, not every frame (avoids chrome-entity churn). The `Built` flags force the first
    // build (a signature could coincide with the zero-initialized field).
    private int _sceneSignature;
    private bool _sceneBuilt;
    private Entity _lastSelected;
    private int _inspectorSignature;
    private bool _inspectorBuilt;

    private int _displayedCount;

    public bool IsEnabled { get; set; } = true;

    /// <summary>The current line-scroll offset (whole lines). Exposed for tests/tooling.</summary>
    public int ScrollOffset => _scroll;

    /// <summary>How many lines are currently displayed (after section/group/component collapse) —
    /// the count the scroll model clamps against. Exposed for tests/tooling.</summary>
    public int DisplayedLineCount => _displayedCount;

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
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _allEntities = world.GetEntities().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        // Transport model: the panel stays interactive in BOTH transport states.

        EnsureState();
        EnsureHeaders();
        if (!_pipelineBuilt) TryBuildPipeline();
        RebuildDynamicIfDirty();

        var scale = _viewportManager.DevicePixelRatio;
        var panel = EditorChromeLayout.RightPanel(
            _viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);

        var displayed = ComputeDisplayed();
        _scroll = SystemsPanelLayout.ClampScroll(_scroll, displayed.Count, panel, scale);

        // Interaction first (a click may collapse/expand/select and reshape the display this frame).
        var changed = HandleInteraction(panel, scale, displayed);
        if (changed)
        {
            RebuildDynamicIfDirty(); // a selection click changes the inspector's component set
            displayed = ComputeDisplayed();
            _scroll = SystemsPanelLayout.ClampScroll(_scroll, displayed.Count, panel, scale);
        }

        PositionLines(displayed, panel, scale);
        ReflectState(displayed, panel, scale);
        _displayedCount = displayed.Count;
    }

    // ---- state + build ----

    private void EnsureState()
    {
        if (_stateEntity.IsAlive) return;
        _state = new EditorPanelStateComponent();
        _stateEntity = _world.CreateEntity();
        _stateEntity.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        _stateEntity.Set(_state);
    }

    private void EnsureHeaders()
    {
        if (_headersBuilt) return;
        _systemsHeaderLine = MakeSectionHeader(PanelSection.Systems, SystemsHeader);
        _sceneHeaderLine = MakeSectionHeader(PanelSection.Scene, SceneHeader);
        _inspectorHeaderLine = MakeSectionHeader(PanelSection.Inspector, InspectorHeader);
        _headersBuilt = true;
    }

    private Line MakeSectionHeader(PanelSection section, string label) => new()
    {
        Kind = LineKind.SectionHeader,
        Section = section,
        Label = label,
        LabelEntity = CreateLabel(label, EditorChromeBuilder.HeaderLabelColor),
        CaretEntity = CreateCaret(),
    };

    private void TryBuildPipeline()
    {
        var (update, draw) = _pipelines();
        if (update == null || draw == null) return; // screen not bound yet

        BuildPipelineSection(UpdateSubHeader, update);
        BuildPipelineSection(DrawSubHeader, draw);
        _pipelineBuilt = true;
    }

    private void BuildPipelineSection(string subHeader, EditorPipelineRegistrar registrar)
    {
        _pipelineLines.Add(new Line
        {
            Kind = LineKind.SubHeader,
            Label = subHeader,
            LabelEntity = CreateLabel(subHeader, EditorChromeBuilder.HeaderLabelColor),
        });
        foreach (var entry in registrar.Entries) // flattened pre-order: a group, then its children
        {
            _pipelineLines.Add(new Line
            {
                Kind = LineKind.PipelineEntry,
                Label = LineLabel(entry),
                Depth = entry.Depth,
                Entry = entry,
                Registrar = registrar,
                LabelEntity = CreateLabel(LineLabel(entry), EditorChromeBuilder.LabelColor),
                CheckboxEntity = CreateCheckbox(),
                MarkEntity = entry.IsGroup ? CreateMinusBar() : default,
                CaretEntity = entry.IsGroup ? CreateCaret() : default,
            });
        }
    }

    // ---- dynamic sections (SCENE + INSPECTOR) ----

    private void RebuildDynamicIfDirty()
    {
        var sceneSig = ComputeSceneSignature();
        if (!_sceneBuilt || sceneSig != _sceneSignature)
        {
            RebuildScene();
            _sceneSignature = sceneSig;
            _sceneBuilt = true;
        }

        var selected = CurrentSelection();
        var inspectorSig = ComputeInspectorSignature(selected);
        if (!_inspectorBuilt || selected != _lastSelected || inspectorSig != _inspectorSignature)
        {
            RebuildInspector(selected);
            _lastSelected = selected;
            _inspectorSignature = inspectorSig;
            _inspectorBuilt = true;
        }
    }

    private void RebuildScene()
    {
        DisposeLines(_sceneLines);
        var rows = SceneTreeBuilder.Build(_allEntities.GetEntities().ToArray());
        if (rows.Count == 0)
        {
            _sceneLines.Add(MakeInfoLine("(no scene entities)"));
            return;
        }
        foreach (var row in rows)
            _sceneLines.Add(new Line
            {
                Kind = LineKind.SceneEntity,
                Label = row.Label,
                Depth = row.Depth,
                SceneEntity = row.Entity,
                LabelEntity = CreateLabel(row.Label, EditorChromeBuilder.LabelColor),
            });
    }

    private void RebuildInspector(Entity selected)
    {
        DisposeLines(_inspectorLines);
        if (!selected.IsAlive)
        {
            _inspectorLines.Add(MakeInfoLine("(no selection)"));
            return;
        }
        var components = ComponentInspector.Inspect(selected);
        if (components.Count == 0)
        {
            _inspectorLines.Add(MakeInfoLine("(no components)"));
            return;
        }
        foreach (var component in components)
        {
            _inspectorLines.Add(new Line
            {
                Kind = LineKind.InspectorComponent,
                Label = component.TypeName,
                Depth = 0,
                ComponentKey = component.Type.FullName ?? component.TypeName,
                LabelEntity = CreateLabel(component.TypeName, EditorChromeBuilder.LabelColor),
                CaretEntity = CreateCaret(),
            });
            foreach (var member in component.Members)
            {
                var text = $"{member.Name}: {member.Value}";
                _inspectorLines.Add(new Line
                {
                    Kind = LineKind.InspectorMember,
                    Label = text,
                    Depth = 1,
                    ComponentKey = component.Type.FullName ?? component.TypeName,
                    LabelEntity = CreateLabel(text, EditorChromeBuilder.DisabledLabelColor),
                });
            }
        }
    }

    private Line MakeInfoLine(string label) => new()
    {
        Kind = LineKind.Info,
        Label = label,
        LabelEntity = CreateLabel(label, EditorChromeBuilder.DisabledLabelColor),
    };

    /// <summary>A cheap structural signature of the visible scene entities: count + a rolling hash
    /// of their identities, so add/remove of an entity forces a rebuild but a steady world does not.</summary>
    private int ComputeSceneSignature()
    {
        var hash = 17;
        foreach (var e in _allEntities.GetEntities())
        {
            if (!e.IsAlive || e.Has<EditorInfrastructureComponent>()) continue;
            hash = hash * 31 + e.GetHashCode();
        }
        return hash;
    }

    /// <summary>A cheap signature of the selected entity's component set (count + type-token hash),
    /// so adding/removing a component while selected (e.g. a toolbar +Box) rebuilds the inspector.</summary>
    private int ComputeInspectorSignature(Entity selected)
    {
        if (!selected.IsAlive) return 0;
        var reader = new ComponentSignatureReader();
        selected.ReadAllComponents(reader);
        return reader.Hash;
    }

    private Entity CurrentSelection()
    {
        foreach (var e in _selectedSet.GetEntities())
            if (e.IsAlive) return e;
        return default;
    }

    // ---- display filtering (collapse) ----

    private List<Line> ComputeDisplayed()
    {
        var displayed = new List<Line>();

        displayed.Add(_systemsHeaderLine);
        if (!_state.SystemsCollapsed)
            foreach (var line in _pipelineLines)
                if (!PipelineLineHidden(line))
                    displayed.Add(line);

        displayed.Add(_sceneHeaderLine);
        if (!_state.SceneCollapsed)
            displayed.AddRange(_sceneLines);

        displayed.Add(_inspectorHeaderLine);
        if (!_state.InspectorCollapsed)
            foreach (var line in _inspectorLines)
                if (line.Kind != LineKind.InspectorMember || _state.IsComponentExpanded(line.ComponentKey!))
                    displayed.Add(line);

        return displayed;
    }

    /// <summary>Whether a pipeline line is hidden because one of its ancestor groups is collapsed.</summary>
    private bool PipelineLineHidden(Line line)
    {
        if (line.Entry == null) return false; // sub-headers always show when SYSTEMS is expanded
        for (var ancestor = line.Entry.Parent; ancestor != null; ancestor = ancestor.Parent)
            if (_state.IsGroupCollapsed(ancestor.Name))
                return true;
        return false;
    }

    // ---- interaction ----

    /// <summary>Scroll + hover + click over the strip. Returns true if a click reshaped the display
    /// (collapse/expand/select) so the caller re-lays out this frame.</summary>
    private bool HandleInteraction(Rectangle panel, float scale, List<Line> displayed)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!panel.Contains(point)) return false;

            if (input.ScrollWheelDelta != 0)
                _scroll = SystemsPanelLayout.ClampScroll(
                    _scroll + SystemsPanelLayout.ScrollLines(input.ScrollWheelDelta), displayed.Count, panel, scale);

            if (!input.LeftButtonReleased) return false;

            var visible = SystemsPanelLayout.VisibleLineCount(panel, scale);
            for (var vi = 0; vi < visible; vi++)
            {
                var index = vi + _scroll;
                if (index < 0 || index >= displayed.Count) break;
                var rect = SystemsPanelLayout.LineRect(panel, vi, scale);
                if (!rect.Contains(point)) continue;
                return DispatchClick(displayed[index], rect, point, scale);
            }
            return false;
        }
        return false;
    }

    /// <summary>Dispatches a click on a displayed line. Returns true if it reshaped the display.</summary>
    private bool DispatchClick(Line line, Rectangle rect, Point point, float scale)
    {
        switch (line.Kind)
        {
            case LineKind.SectionHeader:
                _state.ToggleSection(line.Section);
                return true;

            case LineKind.PipelineEntry:
                // A group's caret (in the reserved left column) collapses its children; anything
                // else on the row is the enable-toggle (the historical Gmail semantics), so an
                // existing center-of-row click keeps toggling enable.
                if (line.Entry!.IsGroup &&
                    SystemsPanelLayout.CaretRect(rect, line.Depth, scale).Contains(point))
                {
                    _state.ToggleGroup(line.Entry.Name);
                    return true;
                }
                ToggleEntryEnabled(line);
                return false;

            case LineKind.SceneEntity:
                SelectEntity(line.SceneEntity);
                return true; // selection changed → inspector rebuild

            case LineKind.InspectorComponent:
                _state.ToggleComponent(line.ComponentKey!);
                return true;

            default:
                return false; // SubHeader / InspectorMember / Info
        }
    }

    private void ToggleEntryEnabled(Line line)
    {
        if (line.Entry == null || line.Registrar == null) return;
        // Never let the panel disable itself — nor cascade itself off through an ancestor group.
        if (ContainsPanel(line.Entry)) return;
        // Gmail/Material click semantics: checked OR indeterminate → all off; unchecked → all on.
        line.Registrar.SetEnabled(line.Entry.Name, line.Entry.EnabledState == PipelineEnabledState.Off);
    }

    /// <summary>Selects <paramref name="entity"/> in the viewport by setting <see cref="SelectedComponent"/>
    /// (single-select, clearing any prior selection — mirroring <c>SelectionSystem</c>). Integrates
    /// both ways: a tree-row click selects in the viewport, and the picked entity is highlighted here.</summary>
    public void SelectEntity(Entity entity)
    {
        if (!entity.IsAlive) return;
        List<Entity>? toClear = null;
        foreach (var e in _selectedSet.GetEntities())
            (toClear ??= new List<Entity>()).Add(e);
        if (toClear != null)
            foreach (var e in toClear)
                if (e.IsAlive && e != entity && e.Has<SelectedComponent>())
                    e.Remove<SelectedComponent>();
        if (!entity.Has<SelectedComponent>())
            entity.Set(new SelectedComponent());
    }

    private bool ContainsPanel(EditorPipelineEntry entry)
    {
        if (!entry.IsGroup) return ReferenceEquals(entry.System, this);
        foreach (var child in entry.Children)
            if (ContainsPanel(child))
                return true;
        return false;
    }

    // ---- headless op surface (see EditorOverlay.DispatchNamedAction "panel:" grammar) ----

    /// <summary>Toggles a top-level section's collapse (headless).</summary>
    public void ToggleSection(PanelSection section)
    {
        EnsureState();
        _state.ToggleSection(section);
    }

    /// <summary>Toggles a SYSTEMS group's collapse by its full registrar name (headless).</summary>
    public void ToggleGroup(string fullName)
    {
        EnsureState();
        _state.ToggleGroup(fullName);
    }

    /// <summary>Toggles an INSPECTOR component's member expansion by its full type name (headless).</summary>
    public void ToggleComponent(string typeFullName)
    {
        EnsureState();
        _state.ToggleComponent(typeFullName);
    }

    /// <summary>Selects the first scene entity whose tree label equals <paramref name="label"/>
    /// (headless — a layout-independent stand-in for a tree-row click). No-op on no match.</summary>
    public bool SelectEntityByLabel(string label)
    {
        foreach (var e in _allEntities.GetEntities())
        {
            if (!e.IsAlive || e.Has<EditorInfrastructureComponent>()) continue;
            if (SceneTreeBuilder.LabelFor(e) == label)
            {
                SelectEntity(e);
                return true;
            }
        }
        return false;
    }

    // ---- layout + rendering ----

    private void PositionLines(List<Line> displayed, Rectangle panel, float scale)
    {
        // A cheap reposition-only-when-something-changed gate would be unsafe here (the displayed
        // set reshapes on collapse/selection), so reposition every frame: park all, place visible.
        ParkAll();

        var visible = SystemsPanelLayout.VisibleLineCount(panel, scale);
        var labelHeight = (_font?.LineHeight ?? 48f) * EditorChromeBuilder.LabelScale * scale;

        for (var vi = 0; vi < visible; vi++)
        {
            var index = vi + _scroll;
            if (index < 0 || index >= displayed.Count) break;
            var line = displayed[index];
            var rect = SystemsPanelLayout.LineRect(panel, vi, scale);
            PositionLine(line, rect, labelHeight, scale);
        }
    }

    private void PositionLine(Line line, Rectangle rect, float labelHeight, float scale)
    {
        switch (line.Kind)
        {
            case LineKind.SectionHeader:
                Place(line.CaretEntity, TopLeft(SystemsPanelLayout.CaretRect(rect, 0, scale)));
                PlaceLabel(line.LabelEntity, SystemsPanelLayout.ContentLabelPosition(rect, labelHeight, 0, scale), scale);
                break;

            case LineKind.SubHeader:
                PlaceLabel(line.LabelEntity, SystemsPanelLayout.HeaderPosition(rect, labelHeight, scale), scale);
                break;

            case LineKind.PipelineEntry:
                var checkbox = SystemsPanelLayout.CheckboxRect(rect, line.Depth, scale);
                Place(line.CheckboxEntity, new Vector2(checkbox.X, checkbox.Y));
                Resize(line.CheckboxEntity, checkbox);
                if (line.MarkEntity.IsAlive)
                {
                    var bar = SystemsPanelLayout.MinusBarRect(checkbox, scale);
                    Place(line.MarkEntity, new Vector2(bar.X, bar.Y));
                    Resize(line.MarkEntity, bar);
                }
                if (line.CaretEntity.IsAlive)
                    Place(line.CaretEntity, TopLeft(SystemsPanelLayout.CaretRect(rect, line.Depth, scale)));
                PlaceLabel(line.LabelEntity, SystemsPanelLayout.LabelPosition(rect, labelHeight, line.Depth, scale), scale);
                break;

            case LineKind.InspectorComponent:
                Place(line.CaretEntity, TopLeft(SystemsPanelLayout.CaretRect(rect, 0, scale)));
                PlaceLabel(line.LabelEntity, SystemsPanelLayout.ContentLabelPosition(rect, labelHeight, 0, scale), scale);
                break;

            case LineKind.SceneEntity:
            case LineKind.InspectorMember:
            case LineKind.Info:
                PlaceLabel(line.LabelEntity, SystemsPanelLayout.ContentLabelPosition(rect, labelHeight, line.Depth, scale), scale);
                break;
        }
    }

    private void ReflectState(List<Line> displayed, Rectangle panel, float scale)
    {
        var selected = CurrentSelection();
        var hovered = HoveredLine(displayed, panel, scale);

        for (var i = 0; i < displayed.Count; i++)
        {
            var line = displayed[i];
            var isHovered = i == hovered;
            switch (line.Kind)
            {
                case LineKind.SectionHeader:
                    SetLabelColor(line.LabelEntity, isHovered ? Color.White : EditorChromeBuilder.HeaderLabelColor);
                    SetCaret(line.CaretEntity, _state.IsCollapsed(line.Section));
                    break;

                case LineKind.SubHeader:
                    SetLabelColor(line.LabelEntity, EditorChromeBuilder.HeaderLabelColor);
                    break;

                case LineKind.PipelineEntry:
                    ReflectPipelineEntry(line, isHovered);
                    break;

                case LineKind.SceneEntity:
                    var isSelected = line.SceneEntity == selected && selected.IsAlive;
                    SetLabelColor(line.LabelEntity,
                        isHovered ? Color.White
                        : isSelected ? EditorChromeBuilder.SelectedLabelColor
                        : EditorChromeBuilder.LabelColor);
                    break;

                case LineKind.InspectorComponent:
                    SetLabelColor(line.LabelEntity, isHovered ? Color.White : EditorChromeBuilder.LabelColor);
                    SetCaret(line.CaretEntity, !_state.IsComponentExpanded(line.ComponentKey!));
                    break;

                case LineKind.InspectorMember:
                case LineKind.Info:
                    SetLabelColor(line.LabelEntity, EditorChromeBuilder.DisabledLabelColor);
                    break;
            }
        }
    }

    private void ReflectPipelineEntry(Line line, bool isHovered)
    {
        var entry = line.Entry!;
        var enabledState = entry.EnabledState;
        ref var box = ref line.CheckboxEntity.Get<SimpleButtonComponent>();
        box.FillColor = enabledState == PipelineEnabledState.Off
            ? Color.Transparent
            : EditorChromeBuilder.CheckboxOnFill;
        if (line.MarkEntity.IsAlive)
        {
            ref var mark = ref line.MarkEntity.Get<SimpleButtonComponent>();
            mark.FillColor = enabledState == PipelineEnabledState.Mixed
                ? EditorChromeBuilder.CheckboxMixedMark
                : Color.Transparent;
        }
        if (line.CaretEntity.IsAlive)
            SetCaret(line.CaretEntity, _state.IsGroupCollapsed(entry.Name));

        var color = isHovered
            ? Color.White
            : enabledState != PipelineEnabledState.Off
                ? EditorChromeBuilder.LabelColor
                : EditorChromeBuilder.DisabledLabelColor;
        SetLabelColor(line.LabelEntity, color);
    }

    private int HoveredLine(List<Line> displayed, Rectangle panel, float scale)
    {
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
            if (!panel.Contains(point)) return -1;
            var visible = SystemsPanelLayout.VisibleLineCount(panel, scale);
            for (var vi = 0; vi < visible; vi++)
            {
                var index = vi + _scroll;
                if (index < 0 || index >= displayed.Count) break;
                if (SystemsPanelLayout.LineRect(panel, vi, scale).Contains(point)) return index;
            }
            return -1;
        }
        return -1;
    }

    // ---- entity helpers ----

    private Entity CreateLabel(string label, Color color)
    {
        var text = _world.CreateEntity();
        text.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        text.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        text.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.LabelDepth,
            TextContent = label,
            Font = _font!,
            Color = color,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        return text;
    }

    private Entity CreateCaret()
    {
        var caret = _world.CreateEntity();
        caret.Set(new EditorInfrastructureComponent());
        caret.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        caret.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.LabelDepth,
            TextContent = CaretExpanded,
            Font = _font!,
            Color = EditorChromeBuilder.HeaderLabelColor,
            Scale = EditorChromeBuilder.LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        return caret;
    }

    private Entity CreateCheckbox()
    {
        var box = _world.CreateEntity();
        box.Set(new EditorInfrastructureComponent());
        box.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        box.Set(new SimpleButtonComponent
        {
            Size = new Vector2(SystemsPanelLayout.CheckboxSize, SystemsPanelLayout.CheckboxSize),
            LineThickness = 1.5f,
            Color = EditorChromeBuilder.ButtonOutline,
            FillColor = Color.Transparent,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.ButtonDepth,
        });
        return box;
    }

    private Entity CreateMinusBar()
    {
        var bar = _world.CreateEntity();
        bar.Set(new EditorInfrastructureComponent());
        bar.Set(new TransformComponent(SystemsPanelLayout.ParkedPosition));
        bar.Set(new SimpleButtonComponent
        {
            Size = new Vector2(SystemsPanelLayout.MinusBarWidth, SystemsPanelLayout.MinusBarHeight),
            LineThickness = 0f,
            Color = Color.Transparent,
            FillColor = Color.Transparent,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorChromeBuilder.CheckboxMarkDepth,
        });
        return bar;
    }

    private static void SetCaret(Entity caret, bool collapsed)
    {
        if (!caret.IsAlive) return;
        ref var text = ref caret.Get<DynamicTextComponent>();
        text.TextContent = collapsed ? CaretCollapsed : CaretExpanded;
    }

    private static void SetLabelColor(Entity label, Color color)
    {
        if (!label.IsAlive) return;
        ref var text = ref label.Get<DynamicTextComponent>();
        text.Color = color;
    }

    private static Vector2 TopLeft(Rectangle rect) => new(rect.X, rect.Y);

    private void ParkAll()
    {
        ParkLine(_systemsHeaderLine);
        ParkLine(_sceneHeaderLine);
        ParkLine(_inspectorHeaderLine);
        foreach (var line in _pipelineLines) ParkLine(line);
        foreach (var line in _sceneLines) ParkLine(line);
        foreach (var line in _inspectorLines) ParkLine(line);
    }

    private static void ParkLine(Line? line)
    {
        if (line == null) return;
        if (line.LabelEntity.IsAlive) Park(line.LabelEntity);
        if (line.CaretEntity.IsAlive) Park(line.CaretEntity);
        if (line.CheckboxEntity.IsAlive) Park(line.CheckboxEntity);
        if (line.MarkEntity.IsAlive) Park(line.MarkEntity);
    }

    private static void Park(Entity entity) => Place(entity, SystemsPanelLayout.ParkedPosition);

    private static void Place(Entity entity, Vector2 position)
    {
        if (!entity.IsAlive) return;
        ref var transform = ref entity.Get<TransformComponent>();
        transform.Position = position;
        entity.NotifyChanged<TransformComponent>();
    }

    private static void PlaceLabel(Entity label, Vector2 position, float scale)
    {
        Place(label, position);
        if (!label.IsAlive) return;
        ref var text = ref label.Get<DynamicTextComponent>();
        text.Scale = EditorChromeBuilder.LabelScale * scale;
    }

    private static void Resize(Entity entity, Rectangle rect)
    {
        if (!entity.IsAlive) return;
        ref var visual = ref entity.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(rect.Width, rect.Height);
    }

    private static void DisposeLines(List<Line> lines)
    {
        foreach (var line in lines)
        {
            if (line.LabelEntity.IsAlive) line.LabelEntity.Dispose();
            if (line.CaretEntity.IsAlive) line.CaretEntity.Dispose();
            if (line.CheckboxEntity.IsAlive) line.CheckboxEntity.Dispose();
            if (line.MarkEntity.IsAlive) line.MarkEntity.Dispose();
        }
        lines.Clear();
    }

    // ---- pipeline row labels (unchanged from Wave 8a) ----

    /// <summary>An entry row's label: a child shows its LOCAL name (indentation conveys the group)
    /// and repeats the policy tag only when its declared policy differs from its parent's.</summary>
    public static string LineLabel(EditorPipelineEntry entry)
    {
        var name = entry.Parent == null ? entry.Name : entry.LocalName;
        var tag = entry.Parent != null && entry.Policy == entry.Parent.Policy
            ? string.Empty
            : PolicySuffix(entry.Policy);
        return name + tag;
    }

    /// <summary>The policy tag rendered after an entry's name (<c>RunNormally</c> renders untagged).</summary>
    public static string PolicySuffix(EditTimeBehavior policy) => policy switch
    {
        EditTimeBehavior.Freeze => " [freeze]",
        EditTimeBehavior.RunPartial => " [partial]",
        EditTimeBehavior.RuntimeEditable => " [editable]",
        _ => string.Empty,
    };

    public void Dispose()
    {
        var headers = new[] { _systemsHeaderLine, _sceneHeaderLine, _inspectorHeaderLine };
        foreach (var line in headers)
        {
            if (line == null) continue;
            if (line.LabelEntity.IsAlive) line.LabelEntity.Dispose();
            if (line.CaretEntity.IsAlive) line.CaretEntity.Dispose();
        }
        DisposeLines(_pipelineLines);
        DisposeLines(_sceneLines);
        DisposeLines(_inspectorLines);
        if (_stateEntity.IsAlive) _stateEntity.Dispose();
        _cursorSet.Dispose();
        _selectedSet.Dispose();
        _allEntities.Dispose();
    }

    /// <summary>Cheap <see cref="IComponentReader"/> that hashes an entity's component-type set (for
    /// the inspector's rebuild-when-changed signature).</summary>
    private sealed class ComponentSignatureReader : IComponentReader
    {
        public int Hash = 17;
        public void OnRead<T>(in T component, in Entity componentOwner) => Hash = Hash * 31 + typeof(T).GetHashCode();
    }
}
