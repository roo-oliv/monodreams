#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.UI;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Builds the Blender-style editor chrome (Wave 7) on the <b>Editor</b> render target — a native
/// window-resolution target composited 1:1 over the whole window (see
/// <c>RenderTargetID.Editor</c>) — so the shell is crisp and readable independent of the game's
/// virtual resolution. It creates: solid opaque panel backgrounds for the reserved margins (thin
/// global top bar + LEFT panel strip + right panel + bottom strip + the center region's Scene panel
/// header carved out of the game viewport — the same margins <c>ViewportManager.SetViewportInset</c>
/// reserves, via <see cref="EditorChromeLayout.ViewportInset"/>), the window top bar's editing
/// buttons, and the Scene header's transport buttons (UX2-B relocated Play/Pause + Restart off the
/// window bar), all sized in real pixels. This replaces the Wave-4b HUD-virtual toolbar (which was
/// authored at 800×600 and upscaled — blurry and low-contrast over light levels).
///
/// <para>Everything reuses the engine's UI/mesh primitives — panels and button fills/outlines are
/// <c>SimpleButtonComponent</c> meshes built by <c>ButtonMeshPrepSystem</c>, labels are
/// <c>DynamicTextComponent</c> — no ImGui, web-capable. Each button carries a
/// <see cref="ToolbarButtonComponent"/> whose <c>Bounds</c> are <b>physical screen pixels</b>;
/// <c>ToolbarSystem</c> hit-tests the cursor's raw <c>ScreenPosition</c> against them.</para>
///
/// <para><b>Resize.</b> Chrome lays out for a concrete window size; <see cref="Relayout"/>
/// recomputes every panel/button/label for a new size (the shell system calls it whenever the
/// window size changes while editing). <see cref="LaidOutWidth"/>/<see cref="LaidOutHeight"/>
/// record the last laid-out size so callers can detect staleness cheaply.</para>
///
/// <para><b>Text choice.</b> Labels use the screen's BitmapFont (PPMondwest, a 48px source) at
/// <see cref="LabelScale"/> = 1/3 → ≈16-point glyphs rendered directly at native resolution and
/// linear-filtered by the render pass — crisp at any window size because the chrome never gets
/// rescaled after rendering. On a HiDPI backbuffer (<c>Relayout</c>'s device-pixel-ratio) the
/// glyph scale multiplies by the DPR — e.g. 48px source → 32 device px at DPR 2, the same
/// 16-point physical size with double the pixel density (a 1.5 divisor instead of the integer 3;
/// strictly sharper than rendering 16 px and letting the OS upscale it). Limit: it is still a
/// downscaled bitmap font, not a vector font; a dedicated small font export per DPR bucket would
/// be marginally crisper (documented follow-up).</para>
/// </summary>
public sealed class EditorChromeBuilder
{
    /// <summary>Label scale: PPMondwest's 48px source at 1/3 ≈ 16px native-pixel labels (an exact
    /// integer divisor of the source size, minimizing downscale artifacts). A layout METRIC (geometry,
    /// not style) so it stays here; every color + depth lives in <see cref="EditorTheme"/>.</summary>
    public const float LabelScale = 1f / 3f;

    private readonly World _world;
    private readonly BitmapFont? _font;
    private readonly Func<string, float> _measureLabel;

    private (EditorToolbarAction action, string label)[] _buttons =
        Array.Empty<(EditorToolbarAction, string)>();
    private (EditorToolbarAction action, string label)[] _headerButtons =
        Array.Empty<(EditorToolbarAction, string)>();
    private Entity _topBar, _leftPanel, _rightPanel, _bottomBar, _sceneHeader;
    private Entity _statusBar, _statusBarBorder; // UX3-F: the window-bottom status strip + its top rule
    private Entity _leftSplitter, _rightSplitter, _bottomSplitter;
    // PF-D: the bottom shelf's tab strip (Assets | Prefabs) is now interactive and OWNED by
    // PalettePlacementSystem (it renders + hit-tests the tabs in the shelf's tab-strip band); the old
    // static single "Assets" tab was retired from the chrome builder.
    private Entity _entityMenuCaret; // UX2-D: the ▾ caret mesh beside the header "Entity" text button
    private Entity _cameraViewButton; // UX2-E: the right-corner "Camera view" nav button (icon)
    private Entity _saveButton;       // PF-F: the Save icon button in the Scene header (left of camera-view)
    private readonly List<Entity> _buttonEntities = new();
    private readonly List<Entity> _headerButtonEntities = new();
    // TB-A: the transport cluster (Play/Pause, Restart) — laid out in the tool row's right cluster beside
    // camera-view + Save, not with the left tool cluster.
    private readonly List<Entity> _headerTransportEntities = new();
    private bool _built;

    /// <summary>The window size the chrome was last laid out for (0 until <see cref="Build"/>).</summary>
    public int LaidOutWidth { get; private set; }

    /// <summary>See <see cref="LaidOutWidth"/>.</summary>
    public int LaidOutHeight { get; private set; }

    /// <summary>The device-pixel-ratio the chrome was last laid out for (see
    /// <see cref="Relayout"/>; 0 until <see cref="Build"/>).</summary>
    public float LaidOutScale { get; private set; }

    /// <summary>The left strip width (logical points) the chrome was last laid out for — the shell
    /// system relayouts when the runtime <c>LeftWidthPt</c> changes (a splitter drag).</summary>
    public int LaidOutLeftWidthPt { get; private set; }

    /// <summary>The right strip width (logical points) the chrome was last laid out for — the shell
    /// system relayouts when the runtime <c>RightWidthPt</c> changes (a splitter drag).</summary>
    public int LaidOutRightWidthPt { get; private set; }

    /// <summary>The bottom shelf height (logical points) the chrome was last laid out for.</summary>
    public int LaidOutBottomHeightPt { get; private set; }

    /// <summary>The left strip's right-edge splitter visual — the shell system recolours it (idle
    /// <c>Border</c> / hovered-or-dragging <c>BorderStrong</c>) each frame.</summary>
    public Entity LeftSplitter => _leftSplitter;

    /// <summary>The right strip's left-edge splitter visual — the shell system recolours it (idle
    /// <c>Border</c> / hovered-or-dragging <c>BorderStrong</c>) each frame.</summary>
    public Entity RightSplitter => _rightSplitter;

    /// <summary>The bottom shelf's top-edge splitter visual (see <see cref="RightSplitter"/>).</summary>
    public Entity BottomSplitter => _bottomSplitter;

    public EditorChromeBuilder(World world, BitmapFont font)
        : this(world, label => font.MeasureString(label).Width * LabelScale)
    {
        _font = font ?? throw new ArgumentNullException(nameof(font));
    }

    /// <summary>Test seam: inject the (already <see cref="LabelScale"/>-scaled) label-width
    /// measure so layout is unit-testable without a BitmapFont/GraphicsDevice (labels then carry
    /// a null font and are not rendered — layout only).</summary>
    public EditorChromeBuilder(World world, Func<string, float> measureLabel)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _measureLabel = measureLabel ?? throw new ArgumentNullException(nameof(measureLabel));
    }

    /// <summary>
    /// The window top bar's buttons (UX2-B/-C: the thin GLOBAL bar). UX2-C relocated the transform-tool
    /// cluster (Move/Rotate/Scale/Boundary/Snap) into the Scene panel header (<see cref="HeaderButtons"/>),
    /// leaving the global editing actions: <b>Save / Undo / Redo</b> (icon buttons; the <c>label</c> is
    /// their tooltip) plus the still-text collider/vertex authoring actions and <b>Refresh</b> (icon).
    /// <b>UX2-D relocated the within-band Order (<c>Fwd</c>/<c>Back</c>) buttons OFF the window bar into
    /// the entity context menus</b> (the actions + dispatch stay — the menus fire them); the
    /// collider/vertex text buttons REMAIN here this phase (their future home is a follow-up).
    /// </summary>
    public static readonly (EditorToolbarAction action, string label)[] DefaultButtons =
    {
        // PF-F: Save relocated OFF the window bar into the Scene panel header (right cluster, beside the
        // camera-view button) — ONE Save affordance, context-aware (Save Scene / Save Prefab). See
        // _saveButton below.
        (EditorToolbarAction.Undo, "Undo"),
        (EditorToolbarAction.Redo, "Redo"),
        // Island-authoring Slice 2: collider authoring (text — no icon this wave). Order relocated (UX2-D).
        (EditorToolbarAction.ColliderAddBox, "+Box"),
        (EditorToolbarAction.ColliderAddConvex, "+Poly"),
        (EditorToolbarAction.ColliderRemove, "-Col"),
        (EditorToolbarAction.VertexAdd, "+Vtx"),
        (EditorToolbarAction.RefreshCatalog, "Refresh"),
    };

    /// <summary>
    /// The Scene panel header's <b>tool row LEFT cluster</b> (TB-A row 2, left): the tool cluster
    /// (Move/Rotate/Scale/Boundary/Snap — a radio over <c>GizmoState</c>) then the Overlays + Entity ▾
    /// dropdowns. Icon buttons whose <c>label</c> is the hover tooltip (Entity stays a text button + caret).
    /// TB-A relocated the transport (Play/Pause + Restart) OUT of this array into
    /// <see cref="HeaderTransportButtons"/> — the far-right cluster beside Save (design §3); these are
    /// Paused-only editing actions. Same <c>ToolbarButtonComponent</c>/<c>ToolbarSystem</c> machinery — the
    /// ONE toolbar system hit-tests + dispatches every header button.
    /// </summary>
    public static readonly (EditorToolbarAction action, string label)[] HeaderButtons =
    {
        (EditorToolbarAction.ToolMove, "Move"),
        (EditorToolbarAction.ToolRotate, "Rotate"),
        (EditorToolbarAction.ToolScale, "Scale"),
        (EditorToolbarAction.ToolBoundary, "Boundary"),
        (EditorToolbarAction.ToggleSnap, "Snap to grid"),
        // UX3-D: the "Overlays" dropdown (an ICON button — two overlapping circles; the label is its
        // tooltip) — Blender's per-viewport Overlays menu, opened below the button.
        (EditorToolbarAction.Overlays, "Overlays"),
        // UX2-D: the fixed "Entity" dropdown (a TEXT button + a small ▾ caret mesh) — the discoverable
        // twin of the viewport right-click, acting on the current selection.
        (EditorToolbarAction.EntityMenu, "Entity"),
    };

    /// <summary>
    /// The Scene panel header's <b>transport cluster</b> (TB-A row 2, far right): the Play/Pause single
    /// toggle (its icon <c>ToolbarSystem</c> swaps with the state) and Restart. They join the camera-view +
    /// Save buttons in one right-anchored cluster (design §3: <c>camera-view · Play/Pause · Restart ·
    /// Save</c>). The transport dispatches in BOTH transport states (<c>IsTransport</c>) — they are how the
    /// designer leaves either state.
    /// </summary>
    public static readonly (EditorToolbarAction action, string label)[] HeaderTransportButtons =
    {
        (EditorToolbarAction.PlayPause, "Play"),
        (EditorToolbarAction.Restart, "Restart"),
    };

    /// <summary>Extra width (logical points) reserved past the <c>Entity</c> label for its ▾ caret.</summary>
    private const int EntityCaretAllowance = 16;

    /// <summary>
    /// Builds the chrome entities (three panels + the toolbar buttons) laid out for the given
    /// window size, and returns the button entities in order. Call once; use
    /// <see cref="Relayout"/> for size changes.
    /// </summary>
    public IReadOnlyList<Entity> Build(int screenWidth, int screenHeight,
        (EditorToolbarAction action, string label)[]? buttons = null,
        (EditorToolbarAction action, string label)[]? headerButtons = null)
    {
        if (_built) throw new InvalidOperationException("Editor chrome is already built.");
        _built = true;
        _buttons = buttons ?? DefaultButtons;
        _headerButtons = headerButtons ?? HeaderButtons;

        _topBar = CreatePanel(EditorTheme.Bg1);
        _leftPanel = CreatePanel(EditorTheme.Bg1);
        _rightPanel = CreatePanel(EditorTheme.Bg1);
        _bottomBar = CreatePanel(EditorTheme.Bg1);
        // The center region's Scene panel header band (UX2-B) — carved out of the game viewport, hosts
        // the transport now and later the tool cluster / menus / mode toggle / camera button.
        _sceneHeader = CreatePanel(EditorTheme.Bg1);
        // UX3-F: the window status bar strip (Bg0 band + a top Border rule) flush with the window
        // bottom, below the assets shelf. Part of the ONE viewport inset; its dynamic labels + dirty dot
        // are placed by EditorStatusBarSystem.
        _statusBar = CreatePanel(EditorTheme.Bg0);
        _statusBarBorder = CreateFill(EditorTheme.Border, EditorTheme.Depths.Splitter);

        // Region splitters (the shell recolours them per hover/drag). The bottom shelf's tab strip
        // (Assets | Prefabs) is rendered by PalettePlacementSystem now (PF-D), not here.
        _leftSplitter = CreateFill(EditorTheme.Border, EditorTheme.Depths.Splitter);
        _rightSplitter = CreateFill(EditorTheme.Border, EditorTheme.Depths.Splitter);
        _bottomSplitter = CreateFill(EditorTheme.Border, EditorTheme.Depths.Splitter);

        // The window top bar's buttons (icon where an icon exists, else a text label).
        CreateButtons(_buttons, _buttonEntities);

        // The Scene-header tool cluster (left) + the transport cluster (right) — same ToolbarButtonComponent
        // machinery, so the ONE ToolbarSystem hit-tests + dispatches them alongside the window-bar buttons
        // (dispatch unchanged); IsTransport keeps the transport live in both transport states.
        CreateButtons(_headerButtons, _headerButtonEntities);
        CreateButtons(HeaderTransportButtons, _headerTransportEntities);

        // UX2-D: the "Entity" dropdown's ▾ caret — a screen-baked triangle mesh (font-independent, the
        // disclosure-arrow pattern) baked + positioned each Relayout beside the Entity text button.
        if (HasHeaderAction(EditorToolbarAction.EntityMenu)) _entityMenuCaret = CreateIconMesh();

        // UX2-E: the fixed Scene-header nav-corner button — a right-anchored icon button (the
        // back-to-camera-view affordance), separate from the left-anchored header row so it stays in the
        // corner. The ONE ToolbarSystem hit-tests + dispatches it (and bakes its Camera glyph) like any
        // other ToolbarButtonComponent; it is an editing action (Paused-only), dimmed while Playing.
        _cameraViewButton = CreateButton(
            EditorToolbarAction.CameraView, labelEntity: null, iconEntity: CreateIconMesh(), tooltip: "Camera view");

        // PF-F: the Save icon button — relocated into the Scene header's right cluster, just LEFT of the
        // camera-view button (ONE Save affordance; the window bar no longer carries it). The ONE
        // ToolbarSystem hit-tests + dispatches it (Save action) + bakes its floppy glyph + dims it on the
        // Game tab / unresolved project, and makes its tooltip context-aware (Save Scene / Save Prefab).
        _saveButton = CreateButton(
            EditorToolbarAction.Save, labelEntity: null, iconEntity: CreateIconMesh(), tooltip: "Save Scene");

        // PF-B/TB-A: the [Scene | Game] mode toggle is retired. The viewport TAB STRIP owns the header's
        // full-width TAB ROW (row 1) — its (dynamic) tab entities are owned + laid out each frame by the
        // dedicated ViewportTabStripSystem, not the chrome builder; the tools + transport live in the
        // TOOL ROW (row 2) below, so the two never overlap (no reserved-width offset needed anymore).

        Relayout(screenWidth, screenHeight);
        return _buttonEntities;
    }

    /// <summary>
    /// Recomputes every panel/button/label position and size for a new window size (native-pixel
    /// layout must track the window) and device-pixel ratio (<paramref name="scale"/> — metrics
    /// and label glyphs scale with it so the chrome keeps its physical size on a HiDPI
    /// backbuffer; see <c>EditorChromeLayout</c>). Idempotent for unchanged inputs.
    /// </summary>
    public void Relayout(int screenWidth, int screenHeight, float scale = 1f,
        int leftWidthPt = EditorChromeLayout.LeftPanelWidth,
        int rightWidthPt = EditorChromeLayout.RightPanelWidth,
        int bottomHeightPt = EditorChromeLayout.BottomBarHeight)
    {
        if (!_built) throw new InvalidOperationException("Build the editor chrome before Relayout.");

        var leftPanel = EditorChromeLayout.LeftPanel(screenWidth, screenHeight, scale, leftWidthPt, bottomHeightPt);
        var rightPanel = EditorChromeLayout.RightPanel(screenWidth, screenHeight, scale, rightWidthPt, bottomHeightPt);
        var bottomBar = EditorChromeLayout.BottomBar(screenWidth, screenHeight, scale, bottomHeightPt);
        var sceneHeader = EditorChromeLayout.SceneHeader(screenWidth, screenHeight, scale, leftWidthPt, rightWidthPt);
        PlacePanel(_topBar, EditorChromeLayout.TopBar(screenWidth, scale));
        PlacePanel(_leftPanel, leftPanel);
        PlacePanel(_rightPanel, rightPanel);
        PlacePanel(_bottomBar, bottomBar);
        PlacePanel(_sceneHeader, sceneHeader);

        // UX3-F: the status bar band + a 1px (DPR-scaled) top rule along its top edge.
        var statusBar = EditorChromeLayout.StatusBar(screenWidth, screenHeight, scale);
        PlacePanel(_statusBar, statusBar);
        var ruleThickness = Math.Max(1, EditorChromeLayout.Px(1, scale));
        PlacePanel(_statusBarBorder, new Rectangle(statusBar.X, statusBar.Y, statusBar.Width, ruleThickness));

        // Splitters on the viewport-facing edges (recoloured by the shell each frame).
        PlacePanel(_leftSplitter,
            EditorChromeLayout.LeftSplitter(screenWidth, screenHeight, scale, leftWidthPt, bottomHeightPt));
        PlacePanel(_rightSplitter,
            EditorChromeLayout.RightSplitter(screenWidth, screenHeight, scale, rightWidthPt, bottomHeightPt));
        PlacePanel(_bottomSplitter,
            EditorChromeLayout.BottomSplitter(screenWidth, screenHeight, scale, bottomHeightPt));

        // TB-A two-row header: the viewport tab strip owns the full-width TAB ROW (row 1, laid out by
        // ViewportTabStripSystem); the tools + transport live in the TOOL ROW (row 2).
        var toolRow = EditorChromeLayout.SceneHeaderToolRow(sceneHeader, scale);

        // The window top bar's editing buttons, then the Scene header's LEFT tool cluster (from the tool
        // row's left margin — the tabs are on their own row now, so no reservation offset).
        LayoutButtonRow(_buttonEntities,
            EditorChromeLayout.ButtonRow(MeasureWidths(_buttons, scale), scale), scale);
        LayoutButtonRow(_headerButtonEntities,
            EditorChromeLayout.ButtonRowIn(toolRow, MeasureWidths(_headerButtons, scale), scale), scale);
        LayoutEntityMenuCaret(scale);
        // The right-anchored transport cluster: camera-view · Play/Pause · Restart · Save (design §3).
        LayoutHeaderRightCluster(toolRow, scale);

        LaidOutWidth = screenWidth;
        LaidOutHeight = screenHeight;
        LaidOutScale = scale;
        LaidOutLeftWidthPt = leftWidthPt;
        LaidOutRightWidthPt = rightWidthPt;
        LaidOutBottomHeightPt = bottomHeightPt;
    }

    /// <summary>Per-button pixel widths for a button set: an icon button is a square (button height);
    /// a text button is its label width measured + scaled + padded.</summary>
    private int[] MeasureWidths((EditorToolbarAction action, string label)[] buttons, float scale)
    {
        var widths = new int[buttons.Length];
        for (var i = 0; i < buttons.Length; i++)
        {
            if (EditorIcons.HasIcon(buttons[i].action))
                widths[i] = EditorChromeLayout.Px(EditorChromeLayout.ButtonHeight, scale); // square icon button
            else
            {
                widths[i] = EditorChromeLayout.ButtonWidth(_measureLabel(buttons[i].label) * scale, scale);
                // The Entity dropdown reserves extra room past its label for the ▾ caret (UX2-D).
                if (buttons[i].action == EditorToolbarAction.EntityMenu)
                    widths[i] += EditorChromeLayout.Px(EntityCaretAllowance, scale);
            }
        }
        return widths;
    }

    private bool HasHeaderAction(EditorToolbarAction action)
    {
        foreach (var (a, _) in _headerButtons)
            if (a == action) return true;
        return false;
    }

    /// <summary>Bakes + positions the <c>Entity ▾</c> dropdown caret (UX2-D): a small down-pointing
    /// triangle MESH (the font-independent disclosure-arrow pattern) in the right portion of the Entity
    /// button's bounds, tinted <see cref="EditorTheme.Text1"/>. A no-op when the header has no Entity
    /// button. The button reserved room for it via <see cref="EntityCaretAllowance"/>.</summary>
    private void LayoutEntityMenuCaret(float scale)
    {
        if (!_entityMenuCaret.IsAlive) return;
        for (var i = 0; i < _headerButtons.Length && i < _headerButtonEntities.Count; i++)
        {
            if (_headerButtons[i].action != EditorToolbarAction.EntityMenu) continue;
            var bounds = _headerButtonEntities[i].Get<ToolbarButtonComponent>().Bounds;
            var size = EditorChromeLayout.Px(8, scale);
            var caret = new Rectangle(
                bounds.Right - EditorChromeLayout.Px(EntityCaretAllowance, scale) + (EditorChromeLayout.Px(EntityCaretAllowance, scale) - size) / 2,
                bounds.Y + (bounds.Height - size) / 2,
                size, size);
            var tri = SystemsPanelLayout.ArrowTriangle(caret, expanded: true); // down-pointing ▾
            BakeMesh(_entityMenuCaret, new FilledTriangleMeshGenerator(tri[0], tri[1], tri[2], EditorTheme.Text1).Generate());
            return;
        }
    }

    /// <summary>Positions the TB-A right-anchored transport cluster in the Scene header's tool row: the
    /// <b>camera-view · Play/Pause · Restart · Save</b> icon buttons (design §3), left-to-right, docked at
    /// the row's right corner. The ONE <c>ToolbarSystem</c> hit-tests + dispatches + bakes each glyph from
    /// the <c>Bounds</c> set here.</summary>
    private void LayoutHeaderRightCluster(Rectangle toolRow, float scale)
    {
        var iconW = EditorChromeLayout.Px(EditorChromeLayout.ButtonHeight, scale); // icon buttons are square
        var cluster = new List<Entity>();
        if (_cameraViewButton.IsAlive) cluster.Add(_cameraViewButton);
        cluster.AddRange(_headerTransportEntities); // Play/Pause then Restart
        if (_saveButton.IsAlive) cluster.Add(_saveButton);
        if (cluster.Count == 0) return;

        var widths = new int[cluster.Count];
        for (var i = 0; i < widths.Length; i++) widths[i] = iconW;
        var rects = EditorChromeLayout.SceneHeaderRightCluster(toolRow, widths, scale);
        for (var i = 0; i < cluster.Count; i++)
        {
            PlaceEntity(cluster[i], new Vector2(rects[i].X, rects[i].Y));
            cluster[i].Get<ToolbarButtonComponent>().Bounds = rects[i];
            cluster[i].Get<SimpleButtonComponent>().Size = new Vector2(rects[i].Width, rects[i].Height);
        }
    }

    private static void BakeMesh(Entity e, MeshData mesh)
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

    /// <summary>Positions a button set's visual bounds into the given laid-out rects. A text button also
    /// positions its label; an icon button carries no label — <c>ToolbarSystem</c> bakes its glyph mesh
    /// from the button <c>Bounds</c> each frame (identity-matrix screen-baked mesh).</summary>
    private void LayoutButtonRow(List<Entity> buttonEntities, Rectangle[] rects, float scale)
    {
        var labelHeight = (_font?.LineHeight ?? 48f) * LabelScale * scale;
        var labelOffsetY = (EditorChromeLayout.Px(EditorChromeLayout.ButtonHeight, scale) - labelHeight) / 2f;
        for (var i = 0; i < buttonEntities.Count && i < rects.Length; i++)
        {
            var bounds = rects[i];
            PlaceEntity(buttonEntities[i], new Vector2(bounds.X, bounds.Y));
            ref var button = ref buttonEntities[i].Get<ToolbarButtonComponent>();
            button.Bounds = bounds;
            ref var visual = ref buttonEntities[i].Get<SimpleButtonComponent>();
            visual.Size = new Vector2(bounds.Width, bounds.Height);
            // A text button positions its label; icon buttons have none (glyph baked from Bounds).
            if (visual.TextEntity is { IsAlive: true } label && label.Has<DynamicTextComponent>())
            {
                // Label glyphs scale with the DPR: same physical size, denser pixels (see the class
                // doc's Text choice — at scale 1 this is the historical 1/3 downscale).
                ref var text = ref label.Get<DynamicTextComponent>();
                text.Scale = LabelScale * scale;
                PlaceEntity(label,
                    new Vector2(bounds.X + EditorChromeLayout.Px(EditorChromeLayout.ButtonPaddingX, scale),
                        bounds.Y + labelOffsetY));
            }
        }
    }

    // NOTE: chrome entities deliberately carry NO VisibleComponent. It is only load-bearing on
    // the Main render pass (the Editor pass renders every matching entity), and its presence
    // would pull mesh chrome into MeshPrepSystem's query — which overwrites DrawComponent
    // .WorldMatrix with the transform's world matrix, double-offsetting meshes whose vertices
    // ButtonMeshPrepSystem already bakes at absolute pixel positions (WorldMatrix = Identity).

    private Entity CreatePanel(Color color)
    {
        // A panel is a fill-only SimpleButtonComponent mesh (no outline) — the engine's
        // FilledRectangle-style primitive, built by ButtonMeshPrepSystem. Opaque per the
        // premultiplied-alpha mesh rule ("UI fills must be opaque").
        var panel = _world.CreateEntity();
        panel.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        panel.Set(new TransformComponent(Vector2.Zero));
        panel.Set(new SimpleButtonComponent
        {
            Size = Vector2.One,
            LineThickness = 0f,
            Color = color,
            FillColor = color,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Panel,
        });
        return panel;
    }

    /// <summary>A fill-only <c>SimpleButtonComponent</c> mesh at an arbitrary depth (splitters, tab
    /// fills, tab underlines) — like <see cref="CreatePanel"/> but not pinned to the panel depth.</summary>
    private Entity CreateFill(Color color, float depth)
    {
        var fill = _world.CreateEntity();
        fill.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        fill.Set(new TransformComponent(Vector2.Zero));
        fill.Set(new SimpleButtonComponent
        {
            Size = Vector2.One,
            LineThickness = 0f,
            Color = color,
            FillColor = color,
            Target = RenderTargetID.Editor,
            LayerDepth = depth,
        });
        return fill;
    }

    private Entity CreateLabel(string label)
    {
        var text = _world.CreateEntity();
        text.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        text.Set(new TransformComponent(Vector2.Zero));
        text.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Label,
            TextContent = label,
            Font = _font!,
            Color = EditorTheme.Text0,
            Scale = LabelScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
        });
        return text;
    }

    /// <summary>Creates each button of a set into <paramref name="into"/> in order: an
    /// action with an icon (<see cref="EditorIcons.HasIcon"/>) becomes an ICON button (a screen-baked
    /// glyph mesh + the label as its hover tooltip); an action without one stays a TEXT button (its
    /// label rendered, no tooltip — the Order/collider/vertex actions until UX2-D relocates them).</summary>
    private void CreateButtons((EditorToolbarAction action, string label)[] buttons, List<Entity> into)
    {
        foreach (var (action, label) in buttons)
        {
            if (EditorIcons.HasIcon(action))
                into.Add(CreateButton(action, labelEntity: null, iconEntity: CreateIconMesh(), tooltip: label));
            else
                into.Add(CreateButton(action, labelEntity: CreateLabel(label), iconEntity: null, tooltip: null));
        }
    }

    /// <summary>A screen-baked ICON mesh entity: a raw <see cref="DrawComponent"/> the
    /// <c>ToolbarSystem</c> refills each frame with the glyph geometry + state colour — identity
    /// <c>WorldMatrix</c>, native Editor target at the label depth, no <c>VisibleComponent</c> (chrome
    /// rule) and no <c>SimpleButtonComponent</c> (so <c>ButtonMeshPrepSystem</c> never touches it). The
    /// disclosure-arrow mesh pattern, applied to the icon set.</summary>
    private Entity CreateIconMesh()
    {
        var icon = _world.CreateEntity();
        icon.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        icon.Set(new TransformComponent(Vector2.Zero));
        icon.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Label,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });
        return icon;
    }

    private Entity CreateButton(EditorToolbarAction action, Entity? labelEntity, Entity? iconEntity, string? tooltip)
    {
        var button = _world.CreateEntity();
        button.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        button.Set(new TransformComponent(Vector2.Zero));
        button.Set(new SimpleButtonComponent
        {
            Size = Vector2.One, // Relayout sets the real size
            LineThickness = 1.5f,
            Color = EditorTheme.BorderStrong,
            FillColor = EditorTheme.Bg2,
            TextEntity = labelEntity,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Button,
        });
        button.Set(new ToolbarButtonComponent
        {
            Action = action,
            Bounds = Rectangle.Empty, // Relayout fills it
            IsHovered = false,
            IsActive = false,
            IconEntity = iconEntity,
            Tooltip = tooltip,
        });
        return button;
    }

    private static void PlacePanel(Entity panel, Rectangle rect)
    {
        PlaceEntity(panel, new Vector2(rect.X, rect.Y));
        ref var visual = ref panel.Get<SimpleButtonComponent>();
        visual.Size = new Vector2(rect.Width, rect.Height);
    }

    private static void PlaceEntity(Entity entity, Vector2 position)
    {
        // Chrome entities are standalone (no parent), so WorldPosition derives from Position.
        ref var transform = ref entity.Get<TransformComponent>();
        transform.Position = position;
        entity.NotifyChanged<TransformComponent>();
    }
}
