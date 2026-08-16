using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Demos;
using MonoDreams.Demos.Screens;
using MonoDreams.Demos.UI;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Renderer;
using MonoDreams.Screen;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Cursor;
using MonoDreams.System.Draw;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Demo.Ui;

/// UI module demo. A screen-space showcase of the ui module: the auto-layout solver, the
/// button variants + states, keyboard/pointer focus navigation, text input, checkbox / toggle,
/// and tabbed sections. Everything lives on the Main target (camera centred at the origin, zoom 1)
/// so a single <see cref="VisibleComponent"/> toggle shows/hides a tab's body, and the module's
/// <see cref="UIFocusSystem"/> drives a shared focus highlight across mouse and keyboard.
///
/// Sections (tabs): <b>Layout</b> visualizes auto-layout containers (toggle the bounds overlay);
/// <b>Buttons</b> shows primary/secondary/tertiary/link + disabled buttons, a text input with a
/// placeholder, a checkbox and a toggle, all navigable with WASD/arrows + Tab and the mouse;
/// <b>Windows</b> shows a dropdown, a type-to-filter combobox, and a mouse-wheel scroll view (a
/// dedicated Scroll render target composited by <c>RenderLayer.Overlay</c>); <b>Dialogs</b> opens a
/// modal dialog whose group-100 focus trap is driven by the screen's active-group accessor;
/// <b>Panels</b> shows the exclusive panel-group primitive twice on one component
/// (<see cref="PanelGroupComponent"/>) — a sub-tab bar and a paged settings menu — whose inactive
/// panels are PARKED off-screen by <see cref="PanelGroupSystem"/> rather than hidden.
public class UiDemoScreen : IGameScreen
{
    private const int TabLayout = 0;
    private const int TabButtons = 1;
    private const int TabWindows = 2;
    private const int TabDialogs = 3;
    private const int TabPanels = 4;

    private const float TabBarY = -250f;   // world Y of the tab header row (just under the HUD header)
    private const float ContentTop = -150f; // world Y where each tab's body begins

    // Focus-group ids for the overlay stack. Base UI is group 0; each overlay traps focus by
    // raising the active group (see ComputeActiveGroup) to its own id while open.
    private const int GroupDialog = 100;
    private const int GroupDropdown = 200;
    private const int GroupCombobox = 300;

    // ── Windows-tab trio layout (issue 10) ──────────────────────────────────────────────────────
    // The dropdown, combobox, and scroll view are laid out as ONE horizontal group, CENTERED on the
    // screen (world x = 0) at a single content Y, with a small gap between them. All three positions
    // derive from these compile-time constants so the ctor (which computes _scrollVirtualBounds before
    // Load() runs) and the builders agree EXACTLY on the scroll box's world position and size — the
    // CRITICAL SYNC INVARIANT (see rendering premises — Scroll render target).
    private const float WindowsContentY = ContentTop + 30f;   // shared content Y for the trio
    private const float WindowsGap = 16f;                       // gap between the three widgets

    // Fixed slot widths used to centre the trio. The dropdown auto-widths to its selection (issue 13)
    // but grows to the RIGHT from its left-anchored slot; this slot is wide enough for the widest
    // option ("Elderberry" + chevron) so it never collides with the combobox.
    private const float DropdownSlotW = 170f;
    private const float ComboFieldW = 240f;

    // Scroll viewport, in Main world space (centred-origin). The box marks the viewport on Main;
    // the Scroll render target is sized to it and composited by RenderLayer.Overlay.
    private const int ScrollViewW = 360;
    // Viewport height shortened (issue 11) so Row 6 is clipped ~in half: 5.5 rows visible at 40px each
    // = 220px (ScrollRowCount=12 rows of content → clearly more to scroll). Feeds the Scroll render
    // target size, _scrollVirtualBounds height, the box chrome, and ScrollViewComponent.ViewportHeight.
    private const int ScrollViewH = 220;
    private const float ScrollRowH = 40f;
    private const int ScrollRowCount = 12;

    // Left X of each trio widget (centred-origin world space). The group total is
    // DropdownSlotW + gap + ComboFieldW + gap + ScrollViewW; left edge = -total/2.
    private const float TrioTotalW = DropdownSlotW + WindowsGap + ComboFieldW + WindowsGap + ScrollViewW;
    private const float DropdownX = -TrioTotalW / 2f;
    private const float ComboX = DropdownX + DropdownSlotW + WindowsGap;
    private const float ScrollX = ComboX + ComboFieldW + WindowsGap;

    // Top-left of the scroll viewport box in Main world space (centred-origin). Derived from the trio
    // constants above. Used both for the overlay virtual rect (constructor) and for placing the box
    // chrome — both reference THIS value so they can never drift apart.
    private static readonly Vector2 ScrollBoxWorldPos = new(ScrollX, WindowsContentY);

    // Scrollbar geometry (issue 12): a thin vertical track inset on the RIGHT inner edge of the box.
    private const int ScrollbarW = 10;        // track/thumb width
    private const int ScrollbarInset = 2;     // gap from the box's inner edge
    private const float ScrollbarMinThumb = 24f; // minimum thumb height so it stays grabbable

    // Button/label metrics shared by MakeButton and the dropdown/combobox auto-width (issues 13, 14).
    private const float ButtonTextScale = 0.18f;  // MakeButton's label scale
    private const float ButtonPadX = 16f, ButtonPadY = 10f;
    private const float ItemPadX = 16f;           // popup option horizontal padding (matches MakeButton)
    private const float ChevronSize = 12f;        // down-chevron icon size (issue 14)
    private const float ChevronGap = 8f;          // gap between the trigger label and the chevron

    // ── Panels tab (exclusive panel groups) ─────────────────────────────────────────────────────
    // Two PanelGroupComponents stacked vertically: a sub-tab bar (headers + three overlapping
    // panels) on top, a paged settings menu (prev/next/close + three overlapping pages) below.
    // Every panel of a group sits at the SAME position — only one is ever on screen; the rest are
    // parked off-screen by PanelGroupSystem, alive and fully laid out.
    private const float PanelsHeaderY = ContentTop - 10f;   // sub-tab header row
    private const int PanelCardW = 440, PanelCardH = 160;
    private static readonly Vector2 PanelCardPos = new(-PanelCardW / 2f, ContentTop + 50f);
    private const float PanelsPagerY = ContentTop + 260f;    // prev / next / close row (label above it)
    private const int PanelPageW = 440, PanelPageH = 150;
    private static readonly Vector2 PanelPagePos = new(-PanelPageW / 2f, PanelsPagerY + 60f);
    private const int PanelPageCount = 3;

    /// <summary>The scene id this demo is bound to (TD/UX-C): its editor Save writes
    /// <c>ui-demo.mdscene</c> and the Scenes panel lists it as a scene.</summary>
    public const string BoundSceneId = "ui-demo";

    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly ViewportManager _viewportManager;
    private readonly SpriteBatch _spriteBatch;
    private readonly World _world;
    private readonly Dictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly BitmapFont _font;
    private readonly ButtonTheme _theme = ButtonTheme.Default;

    // Shared per-action nav input, fed by UiNavInputSystem and consumed by UIFocusSystem.
    private readonly UiInputState _up = new(), _down = new(), _left = new(), _right = new();
    private readonly UiInputState _next = new(), _prev = new(), _activate = new();

    private ScreenController? _screenController;
    private Entity _tabBar;
    private bool _showBounds = true;     // Layout tab: bounds-overlay toggle state
    private int _tabIndex = 0;           // start on Layout (section "a"): the auto-layout showcase

    // The universal editor overlay (null when editorEnabled is false) and the retained pipeline
    // registries the editor's systems panel binds to (see DemoEditor).
    private readonly bool _editorEnabled;
    private readonly EditorSession _session;
    private readonly EditorProjectContext? _projectContext;
    private readonly DrawLayerMap _layers = DemoEditor.CreateLayers();
    private readonly EditorPipelineRegistrar _updatePipeline = new();
    private readonly EditorPipelineRegistrar _drawPipeline = new();
    private DemoEditor? _editor;

    // Layout-tab handles, captured in BuildLayoutTab and driven each frame by Tick():
    // the root (re-centred in the content area), the two example containers (shared Gap +
    // animated cross-axis alignment), and the numeric gap field.
    private Entity _layoutRoot;          // the AutoLayoutBuilder root (row + column)
    private Entity _layoutRowContainer;  // Horizontal example container (cross axis = vertical)
    private Entity _layoutColContainer;  // Vertical example container (cross axis = horizontal)
    private Entity _gapField;            // numeric text input controlling both containers' Gap
    private int _layoutGap = DefaultLayoutGap; // last valid parsed gap, shared by both containers
    private const int DefaultLayoutGap = 16;   // starting / fallback gap
    private const int MinLayoutGap = 0, MaxLayoutGap = 80;

    // Windows-tab widgets (built once; visibility driven by TabSystem / the overlay systems).
    private Entity _dropdown;            // carries DropdownComponent
    private Entity _dropdownTrigger;     // the trigger button (auto-widths on selection — issue 13)
    private Entity _dropdownTriggerLabel; // the trigger's value text (updated on selection)
    private Entity _dropdownChevron;     // the trigger's down-chevron icon (issue 14), repositioned on resize
    private string[] _dropdownOptions = [];
    private Entity _combobox;            // carries ComboboxComponent
    private Entity _comboInput;          // the combobox's text-input field
    private Entity _comboDropdown;       // the combobox's DropdownComponent entity
    private string[] _comboOptions = [];

    // Combobox windowing (issue 15): show up to ComboVisibleCount option rows at a time even though
    // there are more options. The window/scroll/drag state lives on the ComboboxComponent itself.
    private const int ComboVisibleCount = 5;     // max option rows visible at once
    private const float ComboItemH = 34f;
    private float _comboPanelLeftX;              // panel left edge in world X (for repositioning items)
    private float _comboPanelTopY;              // panel top edge in world Y (first item row anchor)
    private float _comboPanelW;

    // Dialogs-tab widget. The dialog is built once and toggled by DialogSystem via IsOpen.
    private Entity _dialog;              // carries DialogComponent
    private Entity _openDialogButton;    // the "Open dialog" trigger; hidden while the dialog is open
    private Entity _openDialogLabel;     // its label child

    // Panels-tab widgets: TWO exclusive panel groups on the same component (a sub-tab bar and a
    // paged settings menu). The screen only ever writes PanelGroupComponent.Active — PanelGroupSystem
    // owns the parking. The selected index of each group is remembered here so leaving the Panels tab
    // (Active = None, the closed-menu state) and coming back restores the same panel.
    private Entity _panelTabs;           // carries PanelGroupComponent (sub-tab bar)
    private Entity[] _panelTabHeaders = [];
    private int _panelTabIndex;          // remembered sub-tab selection
    private Entity _settingsPages;       // carries PanelGroupComponent (paged settings menu)
    private Entity _settingsPageLabel;   // "Page 2 / 3" (or "Menu closed")
    private Entity _settingsToggleLabel; // the Close/Open button's label
    private int _settingsPageIndex;      // remembered page
    private bool _settingsMenuOpen = true;

    // Scroll plumbing: a dedicated render target + its overlay rect; driven by ScrollViewComponent.
    private RenderTarget2D? _scrollTarget;
    private Entity _scrollView;          // carries ScrollViewComponent
    private Rectangle _scrollVirtualBounds;

    public ISystem<GameState> UpdateSystem { get; }
    public ISystem<GameState> DrawSystem { get; }
    public World World => _world;

    public UiDemoScreen(GraphicsDevice graphicsDevice, ContentManager content, MonoDreams.Component.Camera camera,
        ViewportManager viewportManager, SpriteBatch spriteBatch, bool editorEnabled = false,
        EditorSession session = null, EditorProjectContext projectContext = null)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _camera = camera;
        _viewportManager = viewportManager;
        _spriteBatch = spriteBatch;
        _editorEnabled = editorEnabled;
        _session = session;
        _projectContext = projectContext;
        _renderTargets = new Dictionary<RenderTargetID, RenderTarget2D>
        {
            { RenderTargetID.Main, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.UI, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
            { RenderTargetID.HUD, new RenderTarget2D(graphicsDevice, viewportManager.VirtualWidth, viewportManager.VirtualHeight) },
        };
        _font = content.Load<BitmapFont>("Fonts/UAV-OSD-Sans-Mono-72-White-fnt");

        // Scroll viewport render target — sized exactly to the on-screen viewport box (no camera;
        // the Scroll pass derives its projection from this target's pixel size).
        _scrollTarget = new RenderTarget2D(graphicsDevice, ScrollViewW, ScrollViewH);
        // Overlay virtual rect = Main world box rect + (VirtualWidth/2, VirtualHeight/2) (camera at
        // origin, zoom 1). Computed here (deterministic) so CreateDrawSystem can capture it. Must match
        // the box position in BuildScrollView.
        _scrollVirtualBounds = new Rectangle(
            (int)(ScrollBoxWorldPos.X + viewportManager.VirtualWidth / 2f),
            (int)(ScrollBoxWorldPos.Y + viewportManager.VirtualHeight / 2f),
            ScrollViewW, ScrollViewH);

        camera.Position = Vector2.Zero;
        camera.Zoom = 1f;

        _world = new World();
        UpdateSystem = CreateUpdateSystem();
        DrawSystem = CreateDrawSystem();

        // Bind the retained pipeline registries onto the overlay — the seam the editor's systems
        // panel enumerates/toggles at runtime.
        if (_editor != null)
        {
            _editor.Overlay.BindPipelines(_updatePipeline, _drawPipeline);
            EditorOverlay.LogComposition(nameof(UiDemoScreen), _updatePipeline, _drawPipeline);
        }
    }

    public void Load(ScreenController screenController, ContentManager content)
    {
        _screenController = screenController;
        _world.Subscribe<DemoButtonClicked>(OnChromeClicked);
        _world.Subscribe<UIFocusActivated>(OnActivated);
        // Combobox (issue 15): open the option list + CLEAR the field the moment it gains focus, so the
        // query starts empty (all options shown) instead of inheriting the prior selection.
        _world.Subscribe<FocusChanged>(OnFocusChanged);

        // Mesh cursor + a hover-cursor library (arrow at rest, hand over links). CursorHoverSystem
        // swaps the cursor mesh when the pointer enters a focusable whose HoverCursor is non-Default.
        var arrowMesh = ShapeBuilder.Arrow(26f, Color.Black, Color.White).Generate();
        var handMesh = ShapeBuilder.Hand(26f, Color.Black, Color.White).Generate();
        var cursor = MonoDreams.Cursor.Cursor.CreateMesh(_world, arrowMesh, RenderTargetID.HUD);
        cursor.Set(new CursorMeshLibraryComponent
        {
            Meshes = new Dictionary<CursorType, MeshData>
            {
                { CursorType.Default, arrowMesh },
                { CursorType.Hand, handMesh },
            },
        });

        BuildContent();

        if (_editor != null)
        {
            // TD split seam: the code-content rebuild re-runs the header + tab content (all disposed by
            // the sweep; the cursor survives). The demo's systems read the screen's entity FIELDS
            // dynamically, and each Build* reassigns them, so a rebuild re-wires cleanly. Closing the Game
            // tab restores the UI showcase instead of a blank screen.
            _editor.Overlay.Transport.RebuildCodeContent = BuildContent;
            _editor.BindScene(_screenController!, _world, _content.RootDirectory, DemoScreens.Ui, BoundSceneId);
        }
    }

    /// <summary>Builds (or rebuilds) the demo's code-owned content — the header + the five tabs — which the
    /// sweep disposes (the cursor survives). Runs once from <c>Load</c> and again as the TD
    /// <see cref="EditorTransport.RebuildCodeContent"/>. Each <c>Build*</c> reassigns the screen's entity
    /// fields, so the field-reading systems re-wire to the fresh entities on a rebuild.</summary>
    private void BuildContent()
    {
        DemoHeader.Build(
            _world, _viewportManager, _font,
            title: "ui",
            descriptionLines: new[]
            {
                "Navigate focus with arrows, WASD, TAB or mouse.",
                "Explore layout options, buttons, dialogs, and more.",
            });

        BuildTabBar();
        BuildLayoutTab();
        BuildButtonsTab();
        BuildWindowsTab();
        BuildDialogsTab();
        BuildPanelsTab();

        _tabBar.Get<TabBarComponent>().Active = _tabIndex;
    }

    public int ActiveTab => _tabBar.IsAlive ? _tabBar.Get<TabBarComponent>().Active : _tabIndex;

    /// The focus group UIFocusSystem scopes navigation to this frame: the topmost open overlay's
    /// group traps focus inside it. Dialog (100) is most-modal, then dropdown (200), then the
    /// combobox dropdown (300); otherwise the base UI (0). The overlay systems own only show/hide —
    /// this accessor owns the modal focus policy (game-side, per the ui premises).
    private int ComputeActiveGroup()
    {
        if (_dialog.IsAlive && _dialog.Get<DialogComponent>().IsOpen) return GroupDialog;
        if (_dropdown.IsAlive && _dropdown.Get<DropdownComponent>().IsOpen) return GroupDropdown;
        // Combobox (issue 15): do NOT raise the active group while its list is open. Keeping the
        // active group at 0 leaves the INPUT FIELD focused so the user can always keep typing; the
        // option list is selectable by mouse click and filtered live by typing. Trapping focus in the
        // item group (the old GroupCombobox behavior) pulled focus off the input — the reported bug.
        return 0;
    }

    /// Per-frame screen tick: drives the layout-bounds overlay (only on the Layout tab, when the
    /// toggle is on), recentres the layout-root in the content area, applies the shared gap from the
    /// numeric field, and animates both example containers' cross-axis alignment in a 1.5s loop.
    /// Called by <see cref="UiDemoTickSystem"/> (after AutoLayoutSystem, before HierarchySystem, so
    /// transform/layout writes here propagate to children this frame).
    public void Tick(GameState state)
    {
        LayoutDebugSystem.Enabled = ActiveTab == TabLayout && _showBounds;

        TickLayoutTab(state);

        // The scroll view only responds (and shows content) while the Windows tab is active; when
        // disabled the system parks the content offscreen so the Scroll target renders empty.
        if (_scrollView.IsAlive)
            _scrollView.Get<ScrollViewComponent>().Enabled = ActiveTab == TabWindows;

        // Close the Windows-tab overlays when leaving that tab so an open dropdown/combobox (e.g.
        // after a keyboard tab-switch, which is not an outside-click) never bleeds onto another tab.
        if (ActiveTab != TabWindows)
        {
            if (_dropdown.IsAlive) _dropdown.Get<DropdownComponent>().IsOpen = false;
            if (_comboDropdown.IsAlive) _comboDropdown.Get<DropdownComponent>().IsOpen = false;
        }

        TickPanelsTab();

        // Hide the "Open dialog" trigger while the dialog is open so the modal scrim covers a clean
        // screen. Runs after TabSystem (which re-shows tab content each frame), so this override wins.
        var dialogOpen = _dialog.IsAlive && _dialog.Get<DialogComponent>().IsOpen;
        if (_openDialogButton.IsAlive && _dialog.IsAlive)
        {
            SetVisible(_openDialogButton, !dialogOpen && ActiveTab == TabDialogs);
            SetVisible(_openDialogLabel, !dialogOpen && ActiveTab == TabDialogs);
        }

        // The tab bar lives on Main at button depth (0.95), above the dialog scrim (0.80) — since
        // ButtonMeshPrepSystem fixes button depth, the cleanest way to keep the modal covering a
        // clean screen is to hide the tab headers while the dialog is open (the dialog's own buttons
        // render above the scrim). The always-on-top HUD header stays, standard for a modal.
        if (_tabBar.IsAlive)
        {
            foreach (var header in _tabBar.Get<TabBarComponent>().Tabs)
            {
                if (!header.IsAlive) continue;
                SetVisible(header, !dialogOpen);
                if (header.Has<SimpleButtonComponent>() && header.Get<SimpleButtonComponent>().TextEntity is { } label)
                    SetVisible(label, !dialogOpen);
            }
        }
    }

    /// The whole Panels tab, from the screen's side: write each group's active member and paint the
    /// pager's captions. Nothing here moves a panel — <see cref="PanelGroupSystem"/> (registered right
    /// after this tick, before <c>HierarchySystem</c>) parks the inactive members and restores the
    /// active one. Leaving the tab sets both groups to <see cref="PanelGroupComponent.None"/>, so
    /// every panel parks and no focusable inside one survives as a keyboard-nav target; the
    /// remembered indices bring the same panels back on return.
    private void TickPanelsTab()
    {
        var onPanels = ActiveTab == TabPanels;

        if (_panelTabs.IsAlive)
        {
            _panelTabs.Get<PanelGroupComponent>().Active = onPanels ? _panelTabIndex : PanelGroupComponent.None;
            for (var i = 0; i < _panelTabHeaders.Length; i++)
            {
                var header = _panelTabHeaders[i];
                if (header.IsAlive && header.Has<ButtonStateComponent>())
                    header.Get<ButtonStateComponent>().IsActive = i == _panelTabIndex;
            }
        }

        if (!_settingsPages.IsAlive) return;

        _settingsPages.Get<PanelGroupComponent>().Active =
            onPanels && _settingsMenuOpen ? _settingsPageIndex : PanelGroupComponent.None;
        SetCenteredText(_settingsPageLabel, _settingsMenuOpen
            ? $"Page {_settingsPageIndex + 1} / {PanelPageCount}"
            : "Menu closed (no active member)", PanelsPagerY - 34f);
        SetTriggerLabel(_settingsToggleLabel, _settingsMenuOpen ? "Close menu" : "Open menu");
    }

    /// Sets an unparented label's text and re-centres it on the screen's vertical midline (world
    /// x = 0), so a caption that changes width stays centred.
    private void SetCenteredText(Entity entity, string text, float y)
    {
        if (!entity.IsAlive || !entity.Has<DynamicTextComponent>()) return;
        ref var label = ref entity.Get<DynamicTextComponent>();
        if (label.TextContent == text) return;
        label.TextContent = text;
        entity.Get<TransformComponent>().Position =
            new Vector2(-_font.MeasureString(text).Width * label.Scale / 2f, y);
    }

    private static void SetVisible(Entity e, bool show)
    {
        if (!e.IsAlive) return;
        var has = e.Has<VisibleComponent>();
        if (show && !has) e.Set<VisibleComponent>();
        else if (!show && has) e.Remove<VisibleComponent>();
    }

    /// Layout-tab per-frame drivers (centre, gap, alignment animation). Runs every frame but only
    /// mutates the layout entities, which are inert off-tab (TabSystem hides them). AutoLayoutSystem
    /// has already laid out this frame; HierarchySystem runs after Tick, so the root transform we set
    /// here cascades to the children before they render.
    private void TickLayoutTab(GameState state)
    {
        // (Issue 4) Animate cross-axis alignment in a 1.5s-per-step, 3-step loop (4.5s full cycle):
        // 0 = Start, 1 = Center, 2 = End. For the Horizontal row this reads top/center/bottom; for
        // the Vertical column it reads left/center/right (cross axis differs by direction).
        var phase = (int)(state.TotalTime / 1.5f) % 3;
        var align = phase switch
        {
            1 => CrossAxisAlignment.Center,
            2 => CrossAxisAlignment.End,
            _ => CrossAxisAlignment.Start,
        };

        // (Issue 3) Shared Gap from the numeric field, clamped to a sane range; empty/invalid keeps
        // the last valid value. Applied to BOTH containers; AutoLayoutSystem re-lays-out next frame.
        if (_gapField.IsAlive && _gapField.Has<TextInputComponent>())
        {
            var text = _gapField.Get<TextInputComponent>().Text;
            if (int.TryParse(text, out var parsed))
                _layoutGap = MathHelper.Clamp(parsed, MinLayoutGap, MaxLayoutGap);
        }

        ApplyContainerTuning(_layoutRowContainer, align, _layoutGap);
        ApplyContainerTuning(_layoutColContainer, align, _layoutGap);

        // (Issue 2) Re-centre the layout-root in the content area (below the tab bar): horizontally
        // on screen centre, vertically between ContentTop and the bottom edge. AutoLayoutSystem wrote
        // the root transform this frame; we override it (HierarchySystem then propagates to children).
        if (_layoutRoot.IsAlive && _layoutRoot.Has<LayoutSlotComponent>() && _layoutRoot.Has<TransformComponent>())
        {
            ref readonly var slot = ref _layoutRoot.Get<LayoutSlotComponent>();
            var w = slot.ComputedWidth;
            var h = slot.ComputedHeight;
            // Centre on the screen's vertical midline (world y = 0; camera at the origin) so the demo
            // sits at the centre of the screen, not just below it (issue 2).
            const float centerY = 0f;
            _layoutRoot.Get<TransformComponent>().Position = new Vector2(-w / 2f, centerY - h / 2f);
        }
    }

    private static void ApplyContainerTuning(Entity container, CrossAxisAlignment align, int gap)
    {
        if (!container.IsAlive || !container.Has<LayoutSlotComponent>()) return;
        var node = container.Get<LayoutSlotComponent>().Node;
        node.AlignItems = align;
        node.Gap = gap;
    }

    public void GoBackToLauncher() => _screenController?.LoadScreen(DemoScreens.Launcher);

    // ─── tab bar ───────────────────────────────────────────────────────────────

    private void BuildTabBar()
    {
        var labels = new[] { "Layout", "Buttons", "Windows", "Dialogs", "Panels" };
        var made = new (Entity entity, Vector2 size)[labels.Length];
        var total = 0f;
        const float gap = 14f;
        for (var i = 0; i < labels.Length; i++)
        {
            made[i] = MakeButton($"tab.{i}", labels[i], ButtonVariant.Tertiary,
                tabIndex: i, contentTab: -1, alwaysVisible: true);
            total += made[i].size.X + (i > 0 ? gap : 0);
        }

        // Centre the header row horizontally on the screen.
        var x = -total / 2f;
        foreach (var (entity, size) in made)
        {
            entity.Get<TransformComponent>().Position = new Vector2(x, TabBarY);
            x += size.X + gap;
        }

        var tabs = new Entity[made.Length];
        for (var i = 0; i < made.Length; i++) tabs[i] = made[i].entity;

        _tabBar = _world.CreateEntity();
        _tabBar.Set(new TabBarComponent { Tabs = tabs, Active = _tabIndex });
    }

    // ─── Layout tab (auto-layout showcase) ───────────────────────────────────────

    private void BuildLayoutTab()
    {
        // Two auto-layout examples built with the AutoLayoutBuilder: a horizontal row with a gap,
        // and a vertical stack. Enable the bounds overlay (LayoutDebugSystem) to see each
        // container outlined. The coloured boxes are the laid-out content.
        Entity Box(Color color, float w, float h)
        {
            var e = _world.CreateEntity();
            e.Set(new TransformComponent(Vector2.Zero));
            var draw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.5f };
            draw.SetMeshData(new FilledRoundedRectangleMeshGenerator(
                new Rectangle(0, 0, (int)w, (int)h), 8f, color));
            e.Set(draw);
            e.Set(new TabContentComponent { TabIndex = TabLayout });
            return e;
        }

        var rowBoxes = new[]
        {
            (Box(DemoPalette.SkyBlue, 60, 60), new Vector2(60, 60)),
            (Box(DemoPalette.Olive, 60, 90), new Vector2(60, 90)),
            (Box(DemoPalette.Crimson, 60, 50), new Vector2(60, 50)),
        };
        var colBoxes = new[]
        {
            (Box(DemoPalette.TextSelected, 140, 34), new Vector2(140, 34)),
            (Box(DemoPalette.SkyBlue, 100, 34), new Vector2(100, 34)),
            (Box(DemoPalette.Olive, 170, 34), new Vector2(170, 34)),
        };

        // Names used both for the bounds overlay and to recover the container entities below.
        const string rowName = "row (cross-align animates)";
        const string colName = "column (cross-align animates)";

        // The row's cross axis is vertical: a FIXED height taller than its tallest box (90)
        // leaves room for the Start/Center/End animation to read as top/center/bottom.
        const float rowFixedHeight = 90f + 12f * 2f + 40f; // tallest box + padding + slack
        // The column's cross axis is horizontal: a FIXED width wider than its widest box (170)
        // leaves room for the Start/Center/End animation to read as left/center/right.
        const float colFixedWidth = 170f + 12f * 2f + 60f; // widest box + padding + slack

        _layoutRoot = new AutoLayoutBuilder(_world, _viewportManager)
            .CreateRoot(ScreenAnchor.Center, RenderTargetID.Main)
            .Name("layout-root")
            .Direction(LayoutDirection.Horizontal)
            .Gap(48)
            .Padding(16)
            .AlignCross(CrossAxisAlignment.Center)
            .AddContainer(row => row
                .Name(rowName)
                .Direction(LayoutDirection.Horizontal)
                .Gap(_layoutGap)
                .Padding(12)
                .Height(rowFixedHeight) // fixed cross size so vertical alignment is visible
                .AlignCross(CrossAxisAlignment.Start)
                .AddSlot(s => s.Attach(rowBoxes[0].Item1).MeasureWith(_ => rowBoxes[0].Item2))
                .AddSlot(s => s.Attach(rowBoxes[1].Item1).MeasureWith(_ => rowBoxes[1].Item2))
                .AddSlot(s => s.Attach(rowBoxes[2].Item1).MeasureWith(_ => rowBoxes[2].Item2)))
            .AddContainer(col => col
                .Name(colName)
                .Direction(LayoutDirection.Vertical)
                .Gap(_layoutGap)
                .Padding(12)
                .Width(colFixedWidth) // fixed cross size so horizontal alignment is visible
                .AlignCross(CrossAxisAlignment.Start)
                .AddSlot(s => s.Attach(colBoxes[0].Item1).MeasureWith(_ => colBoxes[0].Item2))
                .AddSlot(s => s.Attach(colBoxes[1].Item1).MeasureWith(_ => colBoxes[1].Item2))
                .AddSlot(s => s.Attach(colBoxes[2].Item1).MeasureWith(_ => colBoxes[2].Item2)))
            .Build();

        // Recover the two example container entities by their layout-node name. AddContainer doesn't
        // surface child entities, so we query the slot set once after Build (the layout tree is a pure
        // C# tree keyed by the entity's LayoutSlotComponent.Node — see the ui premises).
        _layoutRowContainer = FindLayoutContainerByName(rowName);
        _layoutColContainer = FindLayoutContainerByName(colName);

        // Content-area chrome: the bounds checkbox sits TOP-LEFT (left edge, just under the tab bar),
        // with the numeric Gap field directly below it. The camera is centred at the origin, so the
        // left edge is -VirtualWidth/2; leave a margin in from the edge / down from the tab bar.
        const float edgeMargin = 24f;
        var leftX = -_viewportManager.VirtualWidth / 2f + edgeMargin;

        var (check, _) = MakeCheckbox("chk.bounds", "show layout bounds", _showBounds,
            tabIndex: 10, contentTab: TabLayout);
        check.Get<TransformComponent>().Position = new Vector2(leftX, ContentTop);

        // Numeric gap field below the checkbox: controls the shared Gap of BOTH containers (Tick reads
        // it each frame). Prefilled with the default so it starts sensible and editing begins at the end.
        var (gap, _) = MakeTextInput("txt.gap", placeholder: "gap",
            width: 120f, tabIndex: 11, contentTab: TabLayout);
        ref var gapInput = ref gap.Get<TextInputComponent>();
        gapInput.Mask = TextInputMask.Numeric;
        gapInput.Text = _layoutGap.ToString();
        gapInput.CaretPosition = gapInput.Text.Length;
        gap.Get<TransformComponent>().Position = new Vector2(leftX, ContentTop + 44f);
        _gapField = gap;
    }

    /// Finds the (single) container entity whose LayoutSlotComponent.Node carries the given name.
    /// Used by BuildLayoutTab to recover the row/column containers the AutoLayoutBuilder created.
    private Entity FindLayoutContainerByName(string name)
    {
        using var set = _world.GetEntities().With<LayoutSlotComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<LayoutSlotComponent>().Node.Name == name)
                return e;
        return default;
    }

    // ─── Buttons & inputs tab ────────────────────────────────────────────────────

    private void BuildButtonsTab()
    {
        // Row 1: the four variants.
        var primary = MakeButton("btn.primary", "Primary", ButtonVariant.Primary, 20, TabButtons);
        var secondary = MakeButton("btn.secondary", "Secondary", ButtonVariant.Secondary, 21, TabButtons);
        var tertiary = MakeButton("btn.tertiary", "Tertiary", ButtonVariant.Tertiary, 22, TabButtons);
        var link = MakeButton("btn.link", "Link", ButtonVariant.Link, 23, TabButtons);
        // The Link variant reads as a hyperlink: underline its label and show the hand cursor on hover.
        if (_lastButtonText.IsAlive && _lastButtonText.Has<DynamicTextComponent>())
            _lastButtonText.Get<DynamicTextComponent>().Underline = true;
        link.entity.Get<FocusableComponent>().HoverCursor = CursorType.Hand;

        PlaceRow(new[] { primary, secondary, tertiary, link }, centerX: 0f, y: ContentTop, gap: 18f);

        // Row 2: a disabled button plus an icon button and an icon-only button, centred together as
        // ONE row with a consistent gap (no skewed gap between left and right groups).
        var disabled = MakeButton("btn.disabled", "Disabled", ButtonVariant.Primary, 24, TabButtons, disabled: true);
        var iconBtn = MakeIconButton("btn.icon", "Starred", ButtonVariant.Secondary, 28, TabButtons,
            DemoPalette.TextSelected, iconSize: 22f, iconOnly: false);
        var iconOnly = MakeIconButton("btn.icononly", "", ButtonVariant.Tertiary, 29, TabButtons,
            DemoPalette.TextSelected, iconSize: 24f, iconOnly: true);
        PlaceRow(new[] { disabled, iconBtn, iconOnly }, centerX: 0f, y: ContentTop + 70f, gap: 18f);

        // Row 3: text input with placeholder.
        var (input, inputSize) = MakeTextInput("txt.name", placeholder: "type your name…",
            width: 280f, tabIndex: 25, contentTab: TabButtons);
        input.Get<TransformComponent>().Position = new Vector2(-inputSize.X / 2f, ContentTop + 150f);

        // Row 4: checkbox + toggle.
        var (check, checkSize) = MakeCheckbox("chk.demo", "enable feature", initiallyOn: true,
            tabIndex: 26, contentTab: TabButtons);
        var (toggle, toggleSize) = MakeCheckbox("tgl.demo", "sound on", initiallyOn: false,
            tabIndex: 27, contentTab: TabButtons);
        check.Get<TransformComponent>().Position = new Vector2(-230f, ContentTop + 230f);
        toggle.Get<TransformComponent>().Position = new Vector2(40f, ContentTop + 230f);
    }

    // ─── Windows tab (dropdown + combobox + scroll view) ─────────────────────────

    private void BuildWindowsTab()
    {
        // The three demos are laid out as one CENTERED horizontal group at WindowsContentY, with
        // DropdownX / ComboX / ScrollX (compile-time consts) as their left edges (issue 10).

        // (a) Dropdown — a trigger button that opens a popup of ~5 options. Left-anchored at DropdownX;
        // the trigger auto-widths to its selection (issue 13) and carries a chevron ICON (issue 14).
        _dropdownOptions = new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry" };
        _dropdown = BuildDropdown(
            id: "dd", baseTab: TabWindows, group: GroupDropdown, options: _dropdownOptions,
            triggerLabel: "Fruit", triggerPos: new Vector2(DropdownX, WindowsContentY),
            tabIndexBase: 30, out _dropdownTriggerLabel);

        // (b) Combobox — a text field that filters a scrollable dropdown of ~12 options (issue 15).
        _comboOptions = new[]
        {
            "Red", "Orange", "Yellow", "Green", "Blue", "Indigo",
            "Violet", "Crimson", "Teal", "Amber", "Magenta", "Lime",
        };
        BuildCombobox(
            id: "cb", baseTab: TabWindows, group: GroupCombobox, options: _comboOptions,
            inputPos: new Vector2(ComboX, WindowsContentY), tabIndexBase: 40,
            out _combobox, out _comboInput, out _comboDropdown);

        // (c) Scroll view — a bordered box on Main marks the viewport; the rows live on the Scroll
        // target, parented under a ContentRoot, and scroll by mouse wheel + a real scrollbar (issue 12).
        BuildScrollView(ScrollBoxWorldPos);
    }

    /// Builds a dropdown: a Secondary trigger button (base group 0, always part of the Windows tab)
    /// plus a popup = a rounded panel background + N option buttons (group <paramref name="group"/>).
    /// Panel + items + item labels go into Overlay; items go into Items. Returns the dropdown entity
    /// and (via out) the trigger's label text entity (updated on selection).
    private Entity BuildDropdown(
        string id, int baseTab, int group, string[] options, string triggerLabel, Vector2 triggerPos,
        int tabIndexBase, out Entity triggerLabelEntity)
    {
        // Trigger — stays visible + focusable in the base group; it is tagged for the Windows tab.
        var (trigger, triggerSize) = MakeButton($"{id}.trigger", triggerLabel, ButtonVariant.Secondary,
            tabIndex: tabIndexBase, contentTab: baseTab, group: 0);
        trigger.Get<TransformComponent>().Position = triggerPos;
        triggerLabelEntity = _lastButtonText;
        _dropdownTrigger = trigger;

        // Down-chevron ICON to the right of the label (issue 14): a sibling mesh child, vertically
        // centred, repositioned on selection by ResizeDropdownTrigger (issue 13). Persists across
        // selections (its mesh never changes). Tagged for the tab like the trigger.
        var chevron = _world.CreateEntity();
        chevron.Set(new TransformComponent(Vector2.Zero)); // positioned by ResizeDropdownTrigger
        chevron.SetParent(trigger);
        var chevDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.97f };
        chevDraw.SetMeshData(ShapeBuilder.Chevron(ChevronSize, 2.5f, _theme.For(ButtonVariant.Secondary).Normal.Text));
        chevron.Set(chevDraw);
        chevron.Set(new TabContentComponent { TabIndex = baseTab });
        _dropdownChevron = chevron;

        // Auto-width the trigger to fit "Fruit" + chevron + padding at the initial build (issue 13).
        ResizeDropdownTrigger(triggerLabel);

        // Popup panel background just under the trigger — sized to the WIDEST OPTION label (issue 13),
        // independent of the (auto-widening) trigger, so no option overflows.
        const float itemH = 34f, pad = 4f;
        var panelW = WidestLabelWidth(options) + ItemPadX * 2f;
        var panelH = options.Length * itemH + pad * 2f;
        var panelPos = new Vector2(triggerPos.X, triggerPos.Y + trigger.Get<SimpleButtonComponent>().Size.Y + 4f);

        var panel = _world.CreateEntity();
        panel.Set(new TransformComponent(panelPos));
        var panelDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.90f };
        panelDraw.SetMeshData(ShapeBuilder.Panel(
            new Rectangle(0, 0, (int)panelW, (int)panelH), DemoPalette.DarkBgSecondary, DemoPalette.TextLight, 2f));
        panel.Set(panelDraw);

        var overlay = new List<Entity> { panel };
        var items = new Entity[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            var (item, _) = MakeButton($"{id}.item.{i}", options[i], ButtonVariant.Tertiary,
                tabIndex: tabIndexBase + 1 + i, contentTab: baseTab, group: group, tag: false);
            // Force a uniform item width inside the panel.
            ref var btn = ref item.Get<SimpleButtonComponent>();
            btn.Size = new Vector2(panelW - pad * 2f, itemH);
            item.Get<FocusableComponent>().Size = new Vector2(panelW - pad * 2f, itemH);
            item.Get<TransformComponent>().Position = panelPos + new Vector2(pad, pad + i * itemH);
            items[i] = item;
            overlay.Add(item);
            overlay.Add(_lastButtonText);
        }

        var entity = _world.CreateEntity();
        entity.Set(new DropdownComponent
        {
            IsOpen = false, Group = group, Trigger = trigger, Items = items,
            Overlay = overlay.ToArray(), SelectedIndex = 0,
        });
        return entity;
    }

    /// Builds a combobox (issue 15): a text-input field wired to a filtered, WINDOWED, scrollable
    /// option list. The field is the dropdown's Trigger (opened on focus by the screen). The option
    /// buttons are in GROUP 0 (NOT a trapped overlay group) so the INPUT keeps keyboard focus while
    /// the list is open — the user can always keep typing — while the visible options stay
    /// mouse-clickable. ComboboxSystem filters the options against the live query every frame and,
    /// because <see cref="ComboboxComponent.MaxVisible"/> is set, WINDOWS them: it shows up to
    /// MaxVisible matches at a time, repositioned into fixed row slots from <c>PanelTopLeft</c>, and
    /// drives the popup's scrollbar. DropdownSystem still owns show/hide + outside-click close; the
    /// screen clears the field on open and fills it on selection.
    private void BuildCombobox(
        string id, int baseTab, int group, string[] options, Vector2 inputPos, int tabIndexBase,
        out Entity combobox, out Entity input, out Entity dropdownEntity)
    {
        // The filter field.
        var (field, fieldSize) = MakeTextInput($"{id}.input", placeholder: "filter colors…",
            width: ComboFieldW, tabIndex: tabIndexBase, contentTab: baseTab);
        field.Get<TransformComponent>().Position = inputPos;
        input = field;

        // A LIMITED-HEIGHT popup just under the field: it shows up to ComboVisibleCount rows even
        // though there are more options, and carries its own scrollbar in a right-edge gutter.
        const float pad = 4f;
        var itemH = ComboItemH;
        var gutter = ScrollbarW + ScrollbarInset * 2;     // reserve the right edge for the scrollbar
        var panelW = fieldSize.X;
        var itemW = panelW - pad * 2f - gutter;
        var panelH = ComboVisibleCount * itemH + pad * 2f;
        var panelPos = new Vector2(inputPos.X, inputPos.Y + fieldSize.Y + 4f);
        var firstRow = panelPos + new Vector2(pad, pad);  // world pos of row slot 0 (PanelTopLeft)

        var panel = _world.CreateEntity();
        panel.Set(new TransformComponent(panelPos));
        var panelDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.90f };
        panelDraw.SetMeshData(ShapeBuilder.Panel(
            new Rectangle(0, 0, (int)panelW, (int)panelH), DemoPalette.DarkBgSecondary, DemoPalette.TextLight, 2f));
        panel.Set(panelDraw);

        var items = new Entity[options.Length];
        var labels = new Entity[options.Length];
        var overlay = new List<Entity> { panel };
        for (var i = 0; i < options.Length; i++)
        {
            // Group 0 (not GroupCombobox): the active group stays 0 while the combobox is open so the
            // INPUT keeps keyboard focus; ComboboxSystem positions the visible window of matches into
            // the row slots, and the screen handles item clicks via OnActivated.
            var (item, _) = MakeButton($"{id}.item.{i}", options[i], ButtonVariant.Tertiary,
                tabIndex: tabIndexBase + 1 + i, contentTab: baseTab, group: 0, tag: false);
            ref var btn = ref item.Get<SimpleButtonComponent>();
            btn.Size = new Vector2(itemW, itemH);
            item.Get<FocusableComponent>().Size = new Vector2(itemW, itemH);
            item.Get<TransformComponent>().Position = firstRow; // repositioned each frame by ComboboxSystem
            items[i] = item;
            labels[i] = _lastButtonText;
            overlay.Add(item);
            overlay.Add(_lastButtonText);
        }

        // Scrollbar chrome (track + thumb): opaque Main-target fills in the right gutter, in the
        // Overlay so DropdownSystem shows/hides them with the list. ComboboxSystem sizes + moves the
        // thumb from the filtered match count + the current window.
        var trackX = panelPos.X + panelW - ScrollbarW - ScrollbarInset;
        var trackY = panelPos.Y + ScrollbarInset;
        var trackH = panelH - ScrollbarInset * 2;
        var trackWorldBounds = new Rectangle((int)trackX, (int)trackY, ScrollbarW, (int)trackH);

        var track = _world.CreateEntity();
        track.Set(new TransformComponent(new Vector2(trackX, trackY)));
        var trackDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.91f };
        trackDraw.SetMeshData(new FilledRectangleMeshGenerator(new Rectangle(0, 0, ScrollbarW, (int)trackH), DemoPalette.DarkBg));
        track.Set(trackDraw);
        overlay.Add(track);

        var thumb = _world.CreateEntity();
        thumb.Set(new TransformComponent(new Vector2(trackX, trackY))); // X fixed; Y + height driven by ComboboxSystem
        var thumbDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.93f };
        thumbDraw.SetMeshData(new FilledRectangleMeshGenerator(new Rectangle(0, 0, ScrollbarW, (int)itemH), DemoPalette.TextLight));
        thumb.Set(thumbDraw);
        overlay.Add(thumb);

        dropdownEntity = _world.CreateEntity();
        dropdownEntity.Set(new DropdownComponent
        {
            IsOpen = false, Group = 0, Trigger = field, Items = items,
            Overlay = overlay.ToArray(), SelectedIndex = 0,
        });

        combobox = _world.CreateEntity();
        combobox.Set(new ComboboxComponent
        {
            Input = field, DropdownEntity = dropdownEntity, ItemLabels = options, ItemLabelEntities = labels,
            MaxVisible = ComboVisibleCount, ItemHeight = itemH, PanelTopLeft = firstRow,
            ScrollbarThumb = thumb, TrackWorldBounds = trackWorldBounds, ThumbColor = DemoPalette.TextLight,
        });
    }

    /// Builds the scroll view: a bordered box on Main (tagged for the Windows tab) marking the
    /// viewport, plus ScrollRowCount rows on the Scroll target parented under a ContentRoot.
    private void BuildScrollView(Vector2 boxWorldPos)
    {
        // Bordered viewport box on Main (visible chrome; the actual content renders into the Scroll
        // target composited over this rect).
        var box = _world.CreateEntity();
        box.Set(new TransformComponent(boxWorldPos));
        var boxDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.5f };
        boxDraw.SetMeshData(new RectangleOutlineMeshGenerator(
            new Rectangle(0, 0, ScrollViewW, ScrollViewH), 2f, DemoPalette.TextLight));
        box.Set(boxDraw);
        box.Set(new TabContentComponent { TabIndex = TabWindows });
        box.Remove<VisibleComponent>(); // TabSystem owns its visibility

        // ContentRoot — all rows parent under it; ScrollViewSystem drives its Y to -Offset.
        var contentRoot = _world.CreateEntity();
        contentRoot.Set(new TransformComponent(Vector2.Zero));

        var contentHeight = ScrollRowCount * ScrollRowH;
        // Rows stop short of the scrollbar gutter on the right so the bar reads as separate chrome.
        var rowW = ScrollViewW - ScrollbarW - ScrollbarInset * 2;
        for (var i = 0; i < ScrollRowCount; i++)
        {
            var rowY = i * ScrollRowH;
            // Row background (alternating opaque tints — premultiplied-alpha rule: opaque fills only).
            var rowBg = _world.CreateEntity();
            rowBg.Set(new TransformComponent(new Vector2(0f, rowY)));
            rowBg.SetParent(contentRoot);
            var rowColor = i % 2 == 0 ? DemoPalette.DarkBgSecondary : DemoPalette.DarkBg;
            var rowDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Scroll, LayerDepth = 0.3f };
            rowDraw.SetMeshData(new FilledRectangleMeshGenerator(
                new Rectangle(0, 0, rowW, (int)ScrollRowH - 2), rowColor));
            rowBg.Set(rowDraw);
            rowBg.Set<VisibleComponent>();

            var rowLabel = _world.CreateEntity();
            rowLabel.Set(new TransformComponent(new Vector2(12f, rowY + 8f)));
            rowLabel.SetParent(contentRoot);
            rowLabel.Set(new DynamicTextComponent
            {
                Target = RenderTargetID.Scroll, LayerDepth = 0.4f, TextContent = $"Row {i + 1}",
                Font = _font, Color = DemoPalette.TextLight, Scale = 0.2f,
                IsRevealed = true, VisibleCharacterCount = int.MaxValue,
            });
            rowLabel.Set<VisibleComponent>();
        }

        // ── Scrollbar chrome (issue 12): a track on the RIGHT inner edge of the box + a thumb. Both
        // are opaque Main-target mesh fills (premultiplied-alpha rule), tagged for the Windows tab so
        // TabSystem shows/hides them with the box. ScrollViewSystem hit-tests in Main world space and
        // drives the thumb's Y from Offset. The thumb mesh is baked once at the computed height.
        var trackX = boxWorldPos.X + ScrollViewW - ScrollbarW - ScrollbarInset;
        var trackY = boxWorldPos.Y + ScrollbarInset;
        var trackH = ScrollViewH - ScrollbarInset * 2;
        var trackWorldBounds = new Rectangle((int)trackX, (int)trackY, ScrollbarW, trackH);

        var track = _world.CreateEntity();
        track.Set(new TransformComponent(new Vector2(trackX, trackY)));
        var trackDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.55f };
        trackDraw.SetMeshData(new FilledRectangleMeshGenerator(
            new Rectangle(0, 0, ScrollbarW, trackH), DemoPalette.DarkBgSecondary));
        track.Set(trackDraw);
        track.Set(new TabContentComponent { TabIndex = TabWindows });
        track.Remove<VisibleComponent>();

        // Thumb height = ViewportHeight/ContentHeight * trackHeight, clamped to a grabbable minimum.
        var thumbH = MathHelper.Max(ScrollbarMinThumb, (ScrollViewH / contentHeight) * trackH);
        var thumb = _world.CreateEntity();
        thumb.Set(new TransformComponent(new Vector2(trackX, trackY))); // Y driven by the system
        var thumbDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.6f };
        thumbDraw.SetMeshData(new FilledRectangleMeshGenerator(
            new Rectangle(0, 0, ScrollbarW, (int)thumbH), DemoPalette.TextLight));
        thumb.Set(thumbDraw);
        thumb.Set(new TabContentComponent { TabIndex = TabWindows });
        thumb.Remove<VisibleComponent>();

        _scrollView = _world.CreateEntity();
        _scrollView.Set(new ScrollViewComponent
        {
            Offset = 0f, ContentHeight = contentHeight, ViewportHeight = ScrollViewH, ViewportWidth = ScrollViewW,
            ContentRoot = contentRoot, Enabled = false, ViewportVirtualBounds = _scrollVirtualBounds,
            ScrollbarTrack = track, ScrollbarThumb = thumb, TrackWorldBounds = trackWorldBounds, ThumbHeight = thumbH,
        });
    }

    // ─── Dialogs tab (modal dialog) ──────────────────────────────────────────────

    private void BuildDialogsTab()
    {
        // The "Open dialog" trigger lives on the Dialogs tab in the base group, placed high (just
        // below the tab bar) so it never overlaps the centred dialog panel. Hidden while open (Tick).
        var (open, openSize) = MakeButton("dlg.open", "Open dialog", ButtonVariant.Primary,
            tabIndex: 50, contentTab: TabDialogs, group: 0);
        open.Get<TransformComponent>().Position = new Vector2(-openSize.X / 2f, ContentTop + 10f);
        _openDialogButton = open;
        _openDialogLabel = _lastButtonText;

        // The dialog itself: backdrop + panel + title + OK/Cancel. Not tab-tagged — DialogSystem
        // toggles it by IsOpen so it can open over any tab.
        var content = new List<Entity>();
        var vw = _viewportManager.VirtualWidth;
        var vh = _viewportManager.VirtualHeight;

        // Full-screen opaque backdrop (world rect centred at origin spans [-vw/2..vw/2]).
        var backdrop = _world.CreateEntity();
        backdrop.Set(new TransformComponent(new Vector2(-vw / 2f, -vh / 2f)));
        // Opaque near-black scrim (premultiplied-alpha rule: no partial alpha on the mesh path). At
        // depth 0.80 it covers the Main background and sits below the panel (0.85+) and the dialog's
        // own buttons (0.95). The only Main tab content on the Dialogs tab — the Open-dialog button —
        // is hidden via Tick while open, so the scrim cleanly fills the screen. (The HUD header is on
        // its own always-on-top target, standard for a modal.)
        var backdropDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.80f };
        backdropDraw.SetMeshData(new FilledRectangleMeshGenerator(new Rectangle(0, 0, vw, vh), new Color(8, 10, 18)));
        backdrop.Set(backdropDraw);
        backdrop.Remove<VisibleComponent>();
        content.Add(backdrop);

        // Centred rounded panel.
        const int panelW = 420, panelH = 220;
        var panelPos = new Vector2(-panelW / 2f, -panelH / 2f);
        var panel = _world.CreateEntity();
        panel.Set(new TransformComponent(panelPos));
        var panelDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.85f };
        panelDraw.SetMeshData(new CompositeMeshGenerator()
            .Add(new FilledRoundedRectangleMeshGenerator(new Rectangle(0, 0, panelW, panelH), 16f, DemoPalette.DarkBgSecondary))
            .Add(new RoundedRectangleOutlineMeshGenerator(new Rectangle(0, 0, panelW, panelH), 16f, 2f, DemoPalette.TextLight)));
        panel.Set(panelDraw);
        panel.Remove<VisibleComponent>();
        content.Add(panel);

        // Title text.
        var title = _world.CreateEntity();
        const float titleScale = 0.26f;
        var titleMeasured = _font.MeasureString("Confirm action") * titleScale;
        title.Set(new TransformComponent(new Vector2(-titleMeasured.Width / 2f, panelPos.Y + 28f)));
        title.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main, LayerDepth = 0.90f, TextContent = "Confirm action", Font = _font,
            Color = DemoPalette.TextLight, Scale = titleScale, IsRevealed = true, VisibleCharacterCount = int.MaxValue,
        });
        title.Remove<VisibleComponent>();
        content.Add(title);

        // OK / Cancel buttons in the dialog group.
        var (ok, okSize) = MakeButton("dialog-confirm", "OK", ButtonVariant.Primary,
            tabIndex: 1, contentTab: TabDialogs, group: GroupDialog, tag: false);
        var okText = _lastButtonText;
        var (cancel, cancelSize) = MakeButton("dialog-cancel", "Cancel", ButtonVariant.Secondary,
            tabIndex: 2, contentTab: TabDialogs, group: GroupDialog, tag: false);
        var cancelText = _lastButtonText;

        var gap = 20f;
        var totalW = okSize.X + cancelSize.X + gap;
        var btnY = panelPos.Y + panelH - okSize.Y - 28f;
        ok.Get<TransformComponent>().Position = new Vector2(-totalW / 2f, btnY);
        cancel.Get<TransformComponent>().Position = new Vector2(-totalW / 2f + okSize.X + gap, btnY);
        content.Add(ok); content.Add(okText);
        content.Add(cancel); content.Add(cancelText);

        _dialog = _world.CreateEntity();
        _dialog.Set(new DialogComponent { IsOpen = false, Group = GroupDialog, Content = content.ToArray() });
    }

    // ─── Panels tab (exclusive panel groups: a tab bar AND a paged settings menu) ─

    /// Builds the Panels tab: TWO <see cref="PanelGroupComponent"/>s — a sub-tab bar (three
    /// overlapping cards) and a paged settings menu (three pages plus a Close action that leaves NO
    /// member active) — proving one primitive covers both shapes. Every member of a group is built at
    /// the SAME position, which is only legible because exactly one of them is ever on screen: the
    /// others are PARKED off-screen by <see cref="PanelGroupSystem"/>, still laid out, still
    /// measured, still carrying their widget state. The screen never moves a panel itself; it only
    /// writes <see cref="PanelGroupComponent.Active"/> (from <see cref="Tick"/>).
    private void BuildPanelsTab()
    {
        // ── (a) a tab bar: three headers switching three stacked cards ────────────────────────────
        string[] tabLabels = ["Overview", "Details", "Notes"];
        string[][] tabBody =
        [
            ["Three cards live at this exact position.", "Only this one is on screen."],
            ["The other two are PARKED off-screen:", "moved, never hidden or torn down."],
            ["So switching back is one transform write.", "No re-layout, no first-frame flicker."],
        ];

        var headers = new (Entity entity, Vector2 size)[tabLabels.Length];
        var cards = new Entity[tabLabels.Length];
        for (var i = 0; i < tabLabels.Length; i++)
        {
            headers[i] = MakeButton($"panel.tab.{i}", tabLabels[i], ButtonVariant.Tertiary,
                tabIndex: 60 + i, contentTab: TabPanels);

            cards[i] = MakePanelCard(tabLabels[i], tabBody[i], PanelCardPos, PanelCardW, PanelCardH);
            // A focusable inside the card: parked panels are inert, so keyboard nav can never walk
            // into the two cards the player cannot see (PanelGroupSystem gates them).
            var (action, actionSize) = MakeButton($"panel.act.{i}", $"{tabLabels[i]} action",
                ButtonVariant.Secondary, tabIndex: 63 + i, contentTab: TabPanels);
            action.SetParent(cards[i]);
            action.Get<TransformComponent>().Position =
                new Vector2(PanelCardW - actionSize.X - 20f, PanelCardH - actionSize.Y - 18f);
        }

        PlaceRow(headers, centerX: 0f, y: PanelsHeaderY, gap: 14f);

        _panelTabHeaders = new Entity[headers.Length];
        for (var i = 0; i < headers.Length; i++) _panelTabHeaders[i] = headers[i].entity;

        _panelTabs = _world.CreateEntity();
        _panelTabs.Set(new PanelGroupComponent { Members = cards, Active = _panelTabIndex });

        // ── (b) a paged settings menu on the SAME component ───────────────────────────────────────
        // Prev/Next walk the pages; Close/Open sets Active = None — a closed menu is a panel group
        // with no active member, not a special case (every page parks, the pager row stays live).
        string[] pageTitles = ["General", "Audio", "Video"];
        string[] pageOptions = ["remember window size", "mute when unfocused", "vertical sync"];
        var pages = new Entity[PanelPageCount];
        for (var i = 0; i < PanelPageCount; i++)
        {
            pages[i] = MakePanelCard($"{pageTitles[i]} settings",
                ["This page keeps its state while parked —", "tick a box, leave, come back."],
                PanelPagePos, PanelPageW, PanelPageH);

            var (option, optionSize) = MakeCheckbox($"settings.opt.{i}", pageOptions[i],
                initiallyOn: i == 0, tabIndex: 74 + i, contentTab: TabPanels);
            option.SetParent(pages[i]);
            option.Get<TransformComponent>().Position =
                new Vector2(PanelPageW - optionSize.X - 20f, PanelPageH - optionSize.Y - 14f);
        }

        _settingsPages = _world.CreateEntity();
        _settingsPages.Set(new PanelGroupComponent { Members = pages, Active = _settingsPageIndex });

        // Pager chrome: NOT members — it must stay on screen while every page is parked.
        var prev = MakeButton("settings.prev", "Prev", ButtonVariant.Secondary, 70, TabPanels);
        var next = MakeButton("settings.next", "Next", ButtonVariant.Secondary, 71, TabPanels);
        // Built with the wider of the two captions so swapping the text never overflows the box.
        var toggle = MakeButton("settings.toggle", "Close menu", ButtonVariant.Primary, 72, TabPanels);
        _settingsToggleLabel = _lastButtonText;
        PlaceRow([prev, next, toggle], centerX: 0f, y: PanelsPagerY, gap: 18f);

        _settingsPageLabel = MakePanelText($"Page 1 / {PanelPageCount}", default,
            new Vector2(0f, PanelsPagerY - 34f), 0.18f, DemoPalette.TextSelected);
    }

    /// One panel of a group: a root entity carrying nothing but a transform (what the park moves)
    /// with the card mesh + its text parented under it, so the whole panel rides one position.
    private Entity MakePanelCard(string title, string[] lines, Vector2 position, int width, int height)
    {
        var root = _world.CreateEntity();
        root.Set(new TransformComponent(position));

        var card = _world.CreateEntity();
        card.Set(new TransformComponent(Vector2.Zero));
        card.SetParent(root);
        var draw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.50f };
        draw.SetMeshData(new CompositeMeshGenerator()
            .Add(new FilledRoundedRectangleMeshGenerator(new Rectangle(0, 0, width, height), 12f, DemoPalette.DarkBgSecondary))
            .Add(new RoundedRectangleOutlineMeshGenerator(new Rectangle(0, 0, width, height), 12f, 2f, DemoPalette.TextLight)));
        card.Set(draw);
        card.Set(new TabContentComponent { TabIndex = TabPanels });

        MakePanelText(title, root, new Vector2(20f, 16f), 0.22f, DemoPalette.TextSelected);
        for (var i = 0; i < lines.Length; i++)
            MakePanelText(lines[i], root, new Vector2(20f, 52f + i * 22f), 0.15f, DemoPalette.TextLight);

        return root;
    }

    /// A Panels-tab label. With a live <paramref name="parent"/> the position is local to that panel
    /// (so it parks with it); without one the label is screen chrome and <paramref name="position"/>
    /// is a world position whose X centres the text.
    private Entity MakePanelText(string text, Entity parent, Vector2 position, float scale, Color color)
    {
        var entity = _world.CreateEntity();
        var placed = parent.IsAlive
            ? position
            : new Vector2(position.X - _font.MeasureString(text).Width * scale / 2f, position.Y);
        entity.Set(new TransformComponent(placed));
        if (parent.IsAlive) entity.SetParent(parent);
        entity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main, LayerDepth = 0.60f, TextContent = text, Font = _font,
            Color = color, Scale = scale, IsRevealed = true, VisibleCharacterCount = int.MaxValue,
        });
        entity.Set(new TabContentComponent { TabIndex = TabPanels });
        return entity;
    }

    // ─── widget builders ─────────────────────────────────────────────────────────

    /// Creates a button entity (outline + fill mesh via SimpleButtonComponent, state via
    /// ButtonStateComponent, focus via FocusableComponent) plus its centred label child. Header
    /// buttons (<paramref name="contentTab"/> &lt; 0) are always visible; content buttons are tagged
    /// for their tab and shown/hidden by TabSystem. Returns the button entity and its size.
    private (Entity entity, Vector2 size) MakeButton(
        string id, string label, ButtonVariant variant, int tabIndex, int contentTab,
        bool disabled = false, bool alwaysVisible = false, int group = 0, bool tag = true)
    {
        const float scale = ButtonTextScale;
        const float padX = ButtonPadX, padY = ButtonPadY;
        var measured = _font.MeasureString(label) * scale;
        var size = new Vector2(measured.Width + padX * 2f, measured.Height + padY * 2f);

        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(Vector2.Zero));

        var text = _world.CreateEntity();
        text.Set(new TransformComponent(new Vector2(padX, padY)));
        text.SetParent(entity);
        text.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.97f,
            TextContent = label,
            Font = _font,
            Color = _theme.For(variant).Normal.Text,
            Scale = scale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });

        var normal = _theme.For(variant).Normal;
        entity.Set(new SimpleButtonComponent
        {
            Size = size,
            LineThickness = variant == ButtonVariant.Secondary ? 2f : 0f,
            Color = normal.Outline,
            FillColor = normal.Fill,
            TextEntity = text,
            Target = RenderTargetID.Main,
            VisualScale = 1f,
        });
        entity.Set(new ButtonStateComponent { Id = id, Variant = variant, IsDisabled = disabled, VisualScale = 1f });
        entity.Set(new FocusableComponent
        {
            TabIndex = tabIndex, Group = group, Disabled = false, Size = size, Target = RenderTargetID.Main,
        });

        // Overlay/dialog content (tag = false) opts out of TabSystem tagging — its visibility and
        // focus-gate are owned by the Dropdown/Dialog systems instead.
        if (tag) Tag(entity, text, contentTab, alwaysVisible);
        _lastButtonText = text; // exposed for overlay builders that must include the label in Content/Overlay
        return (entity, size);
    }

    // The label child of the most-recently-created MakeButton — read immediately by overlay builders
    // (dropdown items, dialog buttons) that need to include the label in their toggled entity set.
    private Entity _lastButtonText;

    /// Creates a button with a mesh icon (a star) to the left of an optional label. With
    /// <paramref name="iconOnly"/> the label is omitted and the button is sized to the icon alone —
    /// the icon-cap case (reuses SimpleButtonComponent per the ui premise; the icon is a sibling
    /// mesh, so it keeps its baked color across hover/focus while the outline/fill/ring still track
    /// state). Tagged for its tab like the other content widgets.
    private (Entity entity, Vector2 size) MakeIconButton(
        string id, string label, ButtonVariant variant, int tabIndex, int contentTab,
        Color iconColor, float iconSize, bool iconOnly)
    {
        const float scale = 0.18f, padX = 16f, padY = 10f, gap = 8f;
        var labelMeasured = _font.MeasureString(label) * scale;
        var labelSize = iconOnly ? Vector2.Zero : new Vector2(labelMeasured.Width, labelMeasured.Height);
        var contentW = iconSize + (iconOnly ? 0f : gap + labelSize.X);
        var contentH = MathHelper.Max(iconSize, labelSize.Y);
        var size = new Vector2(contentW + padX * 2f, contentH + padY * 2f);

        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(Vector2.Zero));

        // Icon mesh child (a star), vertically centred at the left.
        var icon = _world.CreateEntity();
        icon.Set(new TransformComponent(new Vector2(padX, padY + (contentH - iconSize) / 2f)));
        icon.SetParent(entity);
        var iconDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.97f };
        iconDraw.SetMeshData(ShapeBuilder.Star(new Vector2(iconSize / 2f), iconSize * 0.5f, iconSize * 0.24f, 5, iconColor));
        icon.Set(iconDraw);

        Entity labelEntity = default;
        if (!iconOnly)
        {
            labelEntity = _world.CreateEntity();
            labelEntity.Set(new TransformComponent(new Vector2(padX + iconSize + gap, padY + (contentH - labelSize.Y) / 2f)));
            labelEntity.SetParent(entity);
            labelEntity.Set(new DynamicTextComponent
            {
                Target = RenderTargetID.Main, LayerDepth = 0.97f, TextContent = label, Font = _font,
                Color = _theme.For(variant).Normal.Text, Scale = scale, IsRevealed = true, VisibleCharacterCount = int.MaxValue,
            });
        }

        var normal = _theme.For(variant).Normal;
        entity.Set(new SimpleButtonComponent
        {
            Size = size, LineThickness = variant == ButtonVariant.Secondary ? 2f : 0f,
            Color = normal.Outline, FillColor = normal.Fill,
            TextEntity = iconOnly ? (Entity?)null : labelEntity, Target = RenderTargetID.Main, VisualScale = 1f,
        });
        entity.Set(new ButtonStateComponent { Id = id, Variant = variant, VisualScale = 1f });
        entity.Set(new FocusableComponent { TabIndex = tabIndex, Group = 0, Size = size, Target = RenderTargetID.Main });

        entity.Set(new TabContentComponent { TabIndex = contentTab });
        icon.Set(new TabContentComponent { TabIndex = contentTab });
        if (!iconOnly) labelEntity.Set(new TabContentComponent { TabIndex = contentTab });
        return (entity, size);
    }

    /// Creates a single-line text-input box: a focusable Secondary-styled box (its border highlights
    /// on focus via ButtonVisualSystem) carrying a TextInputComponent, with a value text + caret.
    private (Entity entity, Vector2 size) MakeTextInput(
        string id, string placeholder, float width, int tabIndex, int contentTab)
    {
        const float scale = 0.2f;
        const float pad = 8f;
        var height = _font.MeasureString("Ag").Height * scale + pad * 2f;
        var size = new Vector2(width, height);

        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(Vector2.Zero));

        var value = _world.CreateEntity();
        value.Set(new TransformComponent(new Vector2(pad, pad)));
        value.SetParent(entity);
        value.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main,
            LayerDepth = 0.97f,
            TextContent = placeholder,
            Font = _font,
            Color = DemoPalette.TextHover,
            Scale = scale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });

        var caret = _world.CreateEntity();
        caret.Set(new TransformComponent(Vector2.Zero));
        caret.SetParent(value);
        caret.Set(new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.98f });

        entity.Set(new SimpleButtonComponent
        {
            Size = size, LineThickness = 2f, Color = DemoPalette.TextLight,
            FillColor = DemoPalette.DarkBgSecondary, TextEntity = null, Target = RenderTargetID.Main, VisualScale = 1f,
        });
        entity.Set(new ButtonStateComponent { Id = id, Variant = ButtonVariant.Secondary, VisualScale = 1f });
        entity.Set(new FocusableComponent
        {
            TabIndex = tabIndex, Group = 0, Size = size, Target = RenderTargetID.Main,
        });
        entity.Set(new TextInputComponent
        {
            Text = string.Empty, MaxLength = 24, Mask = TextInputMask.None, Focused = false,
            TextEntity = value, CaretEntity = caret, CaretPosition = 0,
            Placeholder = placeholder, PlaceholderColor = DemoPalette.TextHover, TextColor = DemoPalette.TextLight,
        });

        // Tag every part for the tab; caret/value visibility is also driven by TextInputSystem/Toggle.
        Tag(entity, value, contentTab, alwaysVisible: false);
        caret.Set(new TabContentComponent { TabIndex = contentTab });
        return (entity, size);
    }

    /// Creates a checkbox row: a focusable hit box (ghost focus highlight) with a static box mesh,
    /// a checkmark toggled by ToggleSwitchSystem, and a label. Activation flips the toggle.
    private (Entity entity, Vector2 size) MakeCheckbox(
        string id, string label, bool initiallyOn, int tabIndex, int contentTab)
    {
        const float box = 30f, gap = 10f, scale = 0.18f;
        var measured = _font.MeasureString(label) * scale;
        var size = new Vector2(box + gap + measured.Width, MathHelper.Max(box, measured.Height));
        var boxRect = new Rectangle(0, 0, (int)box, (int)box);

        var entity = _world.CreateEntity();
        entity.Set(new TransformComponent(Vector2.Zero));

        var boxEntity = _world.CreateEntity();
        boxEntity.Set(new TransformComponent(new Vector2(0f, (size.Y - box) / 2f)));
        boxEntity.SetParent(entity);
        var boxDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.95f };
        boxDraw.SetMeshData(ShapeBuilder.Panel(boxRect, DemoPalette.DarkBgSecondary, DemoPalette.TextLight, 2f));
        boxEntity.Set(boxDraw);

        var checkMesh = ShapeBuilder.Checkmark(boxRect, 3f, DemoPalette.TextSelected).Generate();
        var check = _world.CreateEntity();
        check.Set(new TransformComponent(Vector2.Zero));
        check.SetParent(boxEntity);
        var checkDraw = new DrawComponent { Type = DrawElementType.Mesh, Target = RenderTargetID.Main, LayerDepth = 0.96f };
        if (initiallyOn) checkDraw.SetMeshData(checkMesh);
        check.Set(checkDraw);

        var labelEntity = _world.CreateEntity();
        labelEntity.Set(new TransformComponent(new Vector2(box + gap, (size.Y - measured.Height) / 2f)));
        labelEntity.SetParent(entity);
        labelEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Main, LayerDepth = 0.97f, TextContent = label, Font = _font,
            Color = DemoPalette.TextLight, Scale = scale, IsRevealed = true, VisibleCharacterCount = int.MaxValue,
        });

        // Hit box (transparent outline; Tertiary ghost shows a faint focus fill behind the row).
        // LayerDepth 0.40 pushes the highlight fill/ring BELOW the checkbox box (0.95) and checkmark
        // (0.96) so hovering the row never hides the decorations. Strict ordering, no equalities:
        // row-fill 0.40 < box 0.95 < checkmark 0.96 < label 0.97 (issue 8).
        entity.Set(new SimpleButtonComponent
        {
            Size = size, LineThickness = 0f, Color = Color.Transparent, FillColor = Color.Transparent,
            TextEntity = null, Target = RenderTargetID.Main, VisualScale = 1f, LayerDepth = 0.40f,
        });
        entity.Set(new ButtonStateComponent { Id = id, Variant = ButtonVariant.Tertiary, VisualScale = 1f });
        entity.Set(new FocusableComponent { TabIndex = tabIndex, Group = 0, Size = size, Target = RenderTargetID.Main });
        entity.Set(new ToggleSwitchComponent { On = initiallyOn, CheckmarkEntity = check, CheckmarkMesh = checkMesh });

        Tag(entity, labelEntity, contentTab, alwaysVisible: false);
        boxEntity.Set(new TabContentComponent { TabIndex = contentTab });
        check.Set(new TabContentComponent { TabIndex = contentTab });
        return (entity, size);
    }

    /// Tags a widget's primary + label entities for a tab (TabSystem shows/hides them), or marks
    /// header widgets always visible.
    private static void Tag(Entity primary, Entity label, int contentTab, bool alwaysVisible)
    {
        if (alwaysVisible)
        {
            primary.Set<VisibleComponent>();
            label.Set<VisibleComponent>();
            return;
        }
        primary.Set(new TabContentComponent { TabIndex = contentTab });
        label.Set(new TabContentComponent { TabIndex = contentTab });
    }

    /// Lays out a set of (entity, size) widgets in a horizontal row centred on <paramref name="centerX"/>.
    private static void PlaceRow((Entity entity, Vector2 size)[] items, float centerX, float y, float gap)
    {
        var total = 0f;
        foreach (var (_, size) in items) total += size.X;
        total += gap * (items.Length - 1);
        var x = centerX - total / 2f;
        foreach (var (entity, size) in items)
        {
            entity.Get<TransformComponent>().Position = new Vector2(x, y);
            x += size.X + gap;
        }
    }

    // ─── messages ────────────────────────────────────────────────────────────────

    private void OnChromeClicked(in DemoButtonClicked msg)
    {
        switch (msg.Id)
        {
            case DemoHeader.BackId: _screenController?.LoadScreen(DemoScreens.Launcher); break;
            case DemoHeader.ExitId: _screenController?.Game.Exit(); break;
        }
    }

    private void OnActivated(in UIFocusActivated msg)
    {
        // Any activated toggle/checkbox flips its bool; the bounds checkbox also drives the overlay.
        if (msg.Focused.IsAlive && msg.Focused.Has<ToggleSwitchComponent>())
        {
            ref var toggle = ref msg.Focused.Get<ToggleSwitchComponent>();
            toggle.On = !toggle.On;
            if (msg.Id == "chk.bounds") _showBounds = toggle.On;
        }

        // ── Dialog ────────────────────────────────────────────────────────────────
        if (msg.Id == "dlg.open" && _dialog.IsAlive)
            _dialog.Get<DialogComponent>().IsOpen = true;
        else if ((msg.Id == "dialog-confirm" || msg.Id == "dialog-cancel") && _dialog.IsAlive)
            _dialog.Get<DialogComponent>().IsOpen = false;

        // ── Panel groups (Panels tab) ───────────────────────────────────────────────
        // The click handler only remembers WHICH member should be active; TickPanelsTab writes it
        // onto the PanelGroupComponent and PanelGroupSystem does the parking. No screen code ever
        // moves, hides, or rebuilds a panel — that is the whole point of the primitive.
        const string panelTabPrefix = "panel.tab.";
        if (msg.Id != null && msg.Id.StartsWith(panelTabPrefix, StringComparison.Ordinal)
            && int.TryParse(msg.Id[panelTabPrefix.Length..], out var panelIndex)
            && panelIndex >= 0 && panelIndex < _panelTabHeaders.Length)
        {
            _panelTabIndex = panelIndex;
        }

        switch (msg.Id)
        {
            case "settings.prev":
                _settingsPageIndex = (_settingsPageIndex + PanelPageCount - 1) % PanelPageCount;
                _settingsMenuOpen = true;
                break;
            case "settings.next":
                _settingsPageIndex = (_settingsPageIndex + 1) % PanelPageCount;
                _settingsMenuOpen = true;
                break;
            case "settings.toggle":
                _settingsMenuOpen = !_settingsMenuOpen;
                break;
        }

        // ── Dropdown (Windows tab) ──────────────────────────────────────────────────
        if (_dropdown.IsAlive)
        {
            var dd = _dropdown.Get<DropdownComponent>();
            if (msg.Focused == dd.Trigger)
            {
                dd.IsOpen = true;
            }
            else
            {
                for (var i = 0; i < dd.Items.Length; i++)
                {
                    if (dd.Items[i] != msg.Focused) continue;
                    dd.SelectedIndex = i;
                    // Set the label + auto-width the trigger to fit it + the chevron (issues 13, 14).
                    ResizeDropdownTrigger(_dropdownOptions[i]);
                    dd.IsOpen = false;
                    break;
                }
            }
        }

        // ── Combobox (Windows tab) — item click fills the field with the chosen label + closes ──────
        // (Opening + clearing on focus is handled in OnFocusChanged; the input stays focused while
        // open, so clicking an item is the selection path.)
        if (_comboInput.IsAlive && _comboDropdown.IsAlive)
        {
            var cd = _comboDropdown.Get<DropdownComponent>();
            for (var i = 0; i < cd.Items.Length; i++)
            {
                if (cd.Items[i] != msg.Focused) continue;
                if (_comboInput.Has<TextInputComponent>())
                {
                    ref var input = ref _comboInput.Get<TextInputComponent>();
                    input.Text = _comboOptions[i];
                    input.CaretPosition = input.Text.Length;
                }
                cd.SelectedIndex = i;
                cd.IsOpen = false;
                break;
            }
        }
    }

    /// Combobox open + clear-on-focus (issue 15). When the filter field gains focus, open the option
    /// list and clear the field so the query starts empty (all options shown), not the prior selection.
    private void OnFocusChanged(in FocusChanged msg)
    {
        if (!_comboInput.IsAlive || !_comboDropdown.IsAlive) return;
        if (msg.Current != _comboInput) return;

        _comboDropdown.Get<DropdownComponent>().IsOpen = true;
        if (_comboInput.Has<TextInputComponent>())
        {
            ref var input = ref _comboInput.Get<TextInputComponent>();
            input.Text = string.Empty;
            input.CaretPosition = 0;
        }
        if (_combobox.IsAlive) _combobox.Get<ComboboxComponent>().WindowStart = 0;
    }

    /// The rendered width of the widest label in <paramref name="labels"/> at the button text scale.
    private float WidestLabelWidth(string[] labels)
    {
        var w = 0f;
        foreach (var l in labels) w = MathHelper.Max(w, _font.MeasureString(l).Width * ButtonTextScale);
        return w;
    }

    /// Sets the dropdown trigger's label text AND auto-widths the trigger to fit that text + the
    /// chevron icon + padding, repositioning the chevron and keeping the trigger left-anchored
    /// (it grows to the right) — issues 13 + 14. Used at build and on every selection.
    private void ResizeDropdownTrigger(string label)
    {
        SetTriggerLabel(_dropdownTriggerLabel, label);

        if (!_dropdownTrigger.IsAlive) return;

        var labelW = _font.MeasureString(label).Width * ButtonTextScale;
        var labelH = _font.MeasureString(label).Height * ButtonTextScale;
        // The trigger FILLS its reserved slot (DropdownSlotW) so the trio's 16px gaps stay stable
        // regardless of the current selection (issue 10), and only grows past the slot if a label is
        // somehow wider than it — so the selection never overflows the box (issue 13).
        var naturalW = labelW + ChevronGap + ChevronSize + ButtonPadX * 2f;
        var width = MathHelper.Max(naturalW, DropdownSlotW);
        var height = MathHelper.Max(labelH, ChevronSize) + ButtonPadY * 2f;
        var size = new Vector2(width, height);

        _dropdownTrigger.Get<SimpleButtonComponent>().Size = size;
        _dropdownTrigger.Get<FocusableComponent>().Size = size;

        // Label stays left (MakeButton placed it at (padX, padY)); the chevron is RIGHT-aligned inside
        // the trigger, the standard <select> look. The chevron mesh is centred on its origin.
        if (_dropdownChevron.IsAlive)
        {
            var cx = width - ButtonPadX - ChevronSize / 2f;
            var cy = height / 2f;
            _dropdownChevron.Get<TransformComponent>().Position = new Vector2(cx, cy);
        }
    }

    /// Updates a trigger's value-text entity content in place.
    private static void SetTriggerLabel(Entity textEntity, string text)
    {
        if (textEntity.IsAlive && textEntity.Has<DynamicTextComponent>())
            textEntity.Get<DynamicTextComponent>().TextContent = text;
    }

    // ─── pipeline ────────────────────────────────────────────────────────────────

    private SequentialSystem<GameState> CreateUpdateSystem()
    {
        var cursorInputSystem = new CursorInputSystem(_world, _viewportManager);

        // The editor overlay (see DemoEditor): built over THIS screen's world/camera/layers.
        _editor = DemoEditor.TryCreate(_editorEnabled, _world, _camera, _layers, _content,
            _graphicsDevice, _spriteBatch, _viewportManager, () => _screenController?.Game,
            session: _session, projectContext: _projectContext, sceneId: BoundSceneId);
        // The injected editor-op cursor must survive the hardware read (Wave 5 seam).
        if (_editor?.Overlay.HasEditorOpPlan == true) cursorInputSystem.SkipHardwareRead = true;

        // ---- Weave the update pipeline through the registrar. With the editor off every gate
        // is a pass-through in Play and the order matches the pre-editor screen exactly. ----
        var p = _updatePipeline;
        // Keyboard focus-nav input is Play-only, frozen with the widgets it drives.
        p.Add("demoInput", new UiNavInputSystem(_up, _down, _left, _right, _next, _prev, _activate),
            EditTimeBehavior.Freeze);
        p.Add("input", cursorInputSystem, EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.Add("editor.keys", _editor.Keys, EditTimeBehavior.RunNormally);
            p.Add("editor.sceneReader", _editor.Overlay.SceneReader, EditTimeBehavior.RunNormally);
            p.Add("editor.dialog", _editor.Overlay.Dialog, EditTimeBehavior.RunNormally);
            p.Add("editor.contextMenu", _editor.Overlay.Menu, EditTimeBehavior.RunNormally);
            // WS: the Autotile Rules workspace — after the modal input-owners (it stands down while a
            // dialog/menu owns the pointer) and before the shortcuts, whose gate ORs its IsOpen.
            p.Add("editor.rules", _editor.Overlay.RulesEditor, EditTimeBehavior.RunNormally);
            // The editor shortcut owner (UX3-E) — after the modal input-owners; inert while Playing.
            p.Add("editor.shortcuts", _editor.Overlay.Shortcuts, EditTimeBehavior.RunNormally);
            p.Add("editor.modal", _editor.Overlay.Modal, EditTimeBehavior.RunNormally); // UX3-F: G/S/R modal transforms
        }
        p.AddGroup("layout", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("intrinsicSizing", new IntrinsicSizingSystem(_world));
            g.Add("autoLayout", new AutoLayoutSystem(_world, _viewportManager));
        });
        // The whole widget interaction block freezes in Edit: a click/keystroke belongs to the
        // editor (selection / gizmo / chrome), never to focus nav, text input, tabs, or the
        // overlay widgets. The toolbar's Play transport button or the systems panel re-arms it.
        // One Freeze gate on the group.
        p.AddGroup("ui.interaction", EditTimeBehavior.Freeze, g =>
        {
            g.Add("buttons", new DemoButtonInteractionSystem(_world)); // the HUD header's back/exit chrome
            // Focus navigation, scoped to the topmost open overlay's group (modal trapping).
            g.Add("focus", new UIFocusSystem(
                _world, _up, _down, _left, _right, _next, _prev, _activate, ComputeActiveGroup));
            g.Add("buttonVisuals", new ButtonVisualSystem(_world, _theme));
            g.Add("toggles", new ToggleSwitchSystem(_world));
            g.Add("textInput", new TextInputSystem(_world));
            g.Add("tabs", new TabSystem(_world));
            g.Add("tick", new UiDemoTickSystem(this)); // sets ScrollViewComponent.Enabled before ScrollViewSystem reads it
            // Exclusive panel groups: AFTER the tick that writes each group's active member and
            // AFTER TabSystem (whose per-tab focus gate this refines for the panels' own
            // focusables), and — via the group's position in the pipeline — before HierarchySystem,
            // so a park reaches the panel's children in the same frame.
            g.Add("panelGroups", new PanelGroupSystem(_world));
            // Overlay widget systems (show/hide + focus-gate): mirror TabSystem; modal focus is the
            // ComputeActiveGroup accessor above, not these systems.
            g.Add("dialogs", new DialogSystem(_world));
            g.Add("dropdowns", new DropdownSystem(_world));
            g.Add("combobox", new ComboboxSystem(_world));
            g.Add("scrollView", new ScrollViewSystem(_world));
        });
        if (_editor != null)
        {
            p.Add("editor.commands", _editor.Overlay.EditorCommands, EditTimeBehavior.RunNormally);
            p.Add("editor.gizmo", _editor.Overlay.Gizmo, EditTimeBehavior.RunNormally);
            p.Add("editor.proxySync", _editor.Overlay.ProxySync, EditTimeBehavior.RunNormally);
        }
        p.Add("hierarchy", new HierarchySystem(_world), EditTimeBehavior.RunNormally);
        // After Hierarchy so the bounds overlay reads fresh WorldPositions.
        p.Add("layoutDebug", new LayoutDebugSystem(_world, _font, _camera, RenderTargetID.Main),
            EditTimeBehavior.RunNormally);
        if (_editor != null)
        {
            p.AddGroup("editor.toolbar", EditTimeBehavior.RunNormally, g =>
            {
                g.Add("meshPrep", _editor.Overlay.ToolbarMeshPrep);
                g.Add("clicks", _editor.Overlay.ToolbarClicks);
                g.Add("viewportTabs", _editor.Overlay.ViewportTabs); // PF-B: the viewport tab strip
                g.Add("workspaceTabs", _editor.Overlay.WorkspaceTabs); // WS: the top-bar workspace tab strip
            });
            p.Add("editor.systemsPanel", _editor.Overlay.SystemsPanel, EditTimeBehavior.RunNormally);
            p.Add("editor.cameraNav", _editor.Overlay.CameraNav, EditTimeBehavior.RunNormally);
            // TD/PF-F universal palette (composes with a resolved project; empty assetRoots is legal).
            if (_editor.Overlay.Palette != null)
                p.Add("editor.palette", _editor.Overlay.Palette, EditTimeBehavior.RunNormally);
        }
        p.Add("cursorPosition", new CursorPositionSystem(_world, _camera, _viewportManager),
            EditTimeBehavior.RunNormally);
        // After CursorPositionSystem (needs the cursor's fresh world/virtual position) and before
        // the draw pipeline: swaps the cursor mesh to the hand over a Link button, arrow otherwise.
        // Play-only cursor cosmetics (in Edit the OS pointer is the visible pointer).
        p.Add("cursorHover", new CursorHoverSystem(_world), EditTimeBehavior.Freeze);
        // Escape/shortcut handling would tear the screen down mid-editing — Play only.
        p.Add("demoShortcuts", new UiDemoShortcutSystem(this), EditTimeBehavior.Freeze);
        if (_editor != null)
        {
            p.Add("editor.shell", _editor.Overlay.Shell, EditTimeBehavior.RunNormally);
            p.Add("editor.statusBar", _editor.Overlay.StatusBar, EditTimeBehavior.RunNormally); // UX3-F: window status bar
            if (_editor.Overlay.EditorOpDriver != null)
                p.Add("editor.opDriver", _editor.Overlay.EditorOpDriver, EditTimeBehavior.RunNormally);
        }

        return p.Build();
    }

    private SequentialSystem<GameState> CreateDrawSystem()
    {
        var renderLayers = new List<RenderLayer>
        {
            RenderLayer.Main(_renderTargets[RenderTargetID.Main]),
            RenderLayer.UI(_renderTargets[RenderTargetID.UI]),
            RenderLayer.HUD(_renderTargets[RenderTargetID.HUD]),
            // The scroll viewport composites over the Main box at its virtual rect (after Main/UI/HUD).
            RenderLayer.Overlay(_scrollTarget!, _scrollVirtualBounds),
        };
        if (_editor != null)
            renderLayers.Add(_editor.Overlay.ChromeLayer);

        // ---- Weave the draw pipeline through the registrar (retained for the systems panel). ----
        var p = _drawPipeline;
        // With the editor composed, the sprite prep chain (cull → sprite prep → Y-sort) is added
        // so a native scene loaded while editing actually previews; the demo DrawLayerMap has no
        // Y-sorted layer, so YSortSystem passes depths through — documented graceful degradation.
        // (No demo entity carries SpriteInfoComponent, so CullingSystem never touches the
        // manually-toggled tab/overlay meshes.)
        p.AddGroup("drawPrep", EditTimeBehavior.RunNormally, g =>
        {
            if (_editorEnabled) g.Add("culling", new CullingSystem(_world, _camera));
            g.Add("spritePrep", new SpritePrepSystem(_world, _graphicsDevice, pixelPerfectRendering: false));
            if (_editorEnabled) g.Add("ySort", new YSortSystem(_world, _camera, _layers));
            g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering: false));
            g.Add("meshPrep", new MeshPrepSystem(_world));
            g.Add("buttonMeshPrep", new ButtonMeshPrepSystem(_world));
        });
        if (_editor != null)
        {
            p.Add("editor.selection", _editor.Overlay.Selection, EditTimeBehavior.RunNormally);
            p.Add("editor.overlayPrep", _editor.Overlay.OverlayPrep, EditTimeBehavior.RunNormally);
        }
        p.Add("renderMain", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Main, _renderTargets[RenderTargetID.Main], _camera), EditTimeBehavior.RunNormally);
        p.Add("renderUI", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.UI, _renderTargets[RenderTargetID.UI]), EditTimeBehavior.RunNormally);
        p.Add("renderHUD", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.HUD, _renderTargets[RenderTargetID.HUD]), EditTimeBehavior.RunNormally);
        // Scroll pass: renders every Scroll-target entity (the rows under ContentRoot) into the
        // scroll render target. No camera (screen-space, identity); projection from the target size.
        p.Add("renderScroll", new MasterRenderSystem(_spriteBatch, _graphicsDevice, _world,
            RenderTargetID.Scroll, _scrollTarget!), EditTimeBehavior.RunNormally);
        if (_editor != null)
            p.Add("editor.renderChrome", _editor.Overlay.ChromeRender, EditTimeBehavior.RunNormally);
        p.Add("finalDraw", new FinalDrawSystem(_spriteBatch, _graphicsDevice, _viewportManager, renderLayers),
            EditTimeBehavior.RunNormally);

        return p.Build();
    }

    public void Dispose()
    {
        UpdateSystem.Dispose();
        DrawSystem.Dispose();
        foreach (var rt in _renderTargets.Values) rt.Dispose();
        _scrollTarget?.Dispose();
        _world.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// Concrete per-action input state for the UI demo's focus navigation.
public sealed class UiInputState : AInputState { }

/// Maps the keyboard to the UI demo's navigation actions each frame: WASD/arrows → directional,
/// Tab / Shift-Tab → ordinal next/prev, Enter/Space → activate. Updates each AInputState once per
/// frame so UIFocusSystem reads clean edges.
public sealed class UiNavInputSystem : ISystem<GameState>
{
    private readonly AInputState _up, _down, _left, _right, _next, _prev, _activate;
    public bool IsEnabled { get; set; } = true;

    public UiNavInputSystem(AInputState up, AInputState down, AInputState left, AInputState right,
        AInputState next, AInputState prev, AInputState activate)
    {
        _up = up; _down = down; _left = left; _right = right; _next = next; _prev = prev; _activate = activate;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        var k = Keyboard.GetState();
        var shift = k.IsKeyDown(Keys.LeftShift) || k.IsKeyDown(Keys.RightShift);
        var tab = k.IsKeyDown(Keys.Tab);

        _up.Update(k.IsKeyDown(Keys.W) || k.IsKeyDown(Keys.Up), state);
        _down.Update(k.IsKeyDown(Keys.S) || k.IsKeyDown(Keys.Down), state);
        _left.Update(k.IsKeyDown(Keys.A) || k.IsKeyDown(Keys.Left), state);
        _right.Update(k.IsKeyDown(Keys.D) || k.IsKeyDown(Keys.Right), state);
        _next.Update(tab && !shift, state);
        _prev.Update(tab && shift, state);
        _activate.Update(k.IsKeyDown(Keys.Enter) || k.IsKeyDown(Keys.Space), state);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}

/// Forwards a per-frame tick to the screen (drives the layout-bounds overlay state).
public sealed class UiDemoTickSystem : ISystem<GameState>
{
    private readonly UiDemoScreen _screen;
    public bool IsEnabled { get; set; } = true;
    public UiDemoTickSystem(UiDemoScreen screen) => _screen = screen;
    public void Update(GameState state) { if (IsEnabled) _screen.Tick(state); }
    public void Dispose() => GC.SuppressFinalize(this);
}

/// Escape returns to the launcher.
public sealed class UiDemoShortcutSystem : ISystem<GameState>
{
    private readonly UiDemoScreen _screen;
    private KeyboardState _previous;
    public bool IsEnabled { get; set; } = true;

    public UiDemoShortcutSystem(UiDemoScreen screen)
    {
        _screen = screen;
        _previous = Keyboard.GetState();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        var current = Keyboard.GetState();
        if (current.IsKeyDown(Keys.Escape) && !_previous.IsKeyDown(Keys.Escape)) _screen.GoBackToLauncher();
        _previous = current;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
