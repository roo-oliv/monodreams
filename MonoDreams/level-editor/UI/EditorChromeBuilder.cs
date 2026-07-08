#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
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
    private Entity _leftSplitter, _rightSplitter, _bottomSplitter;
    private Entity _bottomTabFill, _bottomTabLabel, _bottomTabUnderline;
    private readonly List<Entity> _buttonEntities = new();
    private readonly List<Entity> _labelEntities = new();
    private readonly List<Entity> _headerButtonEntities = new();
    private readonly List<Entity> _headerLabelEntities = new();
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
    /// The window top bar's buttons (UX2-B: the thin GLOBAL bar) — the transport relocated to the
    /// Scene panel header (<see cref="HeaderButtons"/>), leaving the global editing actions: the
    /// transform tools, Save, Undo/Redo, Snap, the selection-context collider/order actions, the
    /// boundary tool, and Refresh. (The tools relocate into the Scene header in UX2-C, not now.)
    /// </summary>
    public static readonly (EditorToolbarAction action, string label)[] DefaultButtons =
    {
        (EditorToolbarAction.ToolMove, "Move"),
        (EditorToolbarAction.ToolRotate, "Rotate"),
        (EditorToolbarAction.ToolScale, "Scale"),
        (EditorToolbarAction.Save, "Save"),
        (EditorToolbarAction.Undo, "Undo"),
        (EditorToolbarAction.Redo, "Redo"),
        (EditorToolbarAction.ToggleSnap, "Snap"),
        // Island-authoring Slice 2: within-band ordering + collider authoring on the selection.
        (EditorToolbarAction.OrderForward, "Fwd"),
        (EditorToolbarAction.OrderBack, "Back"),
        (EditorToolbarAction.ColliderAddBox, "+Box"),
        (EditorToolbarAction.ColliderAddConvex, "+Poly"),
        (EditorToolbarAction.ColliderRemove, "-Col"),
        (EditorToolbarAction.VertexAdd, "+Vtx"),
        // Island-authoring Slice 3: the freeform boundary tool (a radio with the transform tools).
        (EditorToolbarAction.ToolBoundary, "Bound"),
        (EditorToolbarAction.RefreshCatalog, "Refresh"),
    };

    /// <summary>
    /// The Scene panel header's buttons (UX2-B: the transport relocated off the window bar) — the
    /// Play/Pause single toggle (its label <c>ToolbarSystem</c> swaps with the state, sized here for
    /// the wider "Pause") and Restart. They dispatch in BOTH transport states (<c>IsTransport</c>),
    /// unlike the window bar's editing actions. Same <c>ToolbarButtonComponent</c>/<c>ToolbarSystem</c>
    /// machinery — laid out in the header rect, dispatch unchanged.
    /// </summary>
    public static readonly (EditorToolbarAction action, string label)[] HeaderButtons =
    {
        (EditorToolbarAction.PlayPause, "Pause"),
        (EditorToolbarAction.Restart, "Restart"),
    };

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

        // Region splitters (the shell recolours them per hover/drag) and the bottom shelf's single
        // static "Assets" tab (marks the terrain: the same tab strip as the left strip, one tab).
        _leftSplitter = CreateFill(EditorTheme.Border, EditorTheme.Depths.Splitter);
        _rightSplitter = CreateFill(EditorTheme.Border, EditorTheme.Depths.Splitter);
        _bottomSplitter = CreateFill(EditorTheme.Border, EditorTheme.Depths.Splitter);
        _bottomTabFill = CreateFill(EditorTheme.Bg1, EditorTheme.Depths.Button); // active = merges into the shelf body
        _bottomTabUnderline = CreateFill(EditorTheme.Accent, EditorTheme.Depths.TabUnderline);
        _bottomTabLabel = CreateLabel("Assets");

        foreach (var (action, label) in _buttons)
        {
            var labelEntity = CreateLabel(label);
            _labelEntities.Add(labelEntity);
            _buttonEntities.Add(CreateButton(action, labelEntity));
        }

        // The Scene-header transport buttons — same ToolbarButtonComponent machinery, so the ONE
        // ToolbarSystem hit-tests + dispatches them alongside the window-bar buttons (dispatch
        // unchanged); IsTransport keeps them live in both transport states.
        foreach (var (action, label) in _headerButtons)
        {
            var labelEntity = CreateLabel(label);
            _headerLabelEntities.Add(labelEntity);
            _headerButtonEntities.Add(CreateButton(action, labelEntity));
        }

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

        // Splitters on the viewport-facing edges (recoloured by the shell each frame).
        PlacePanel(_leftSplitter,
            EditorChromeLayout.LeftSplitter(screenWidth, screenHeight, scale, leftWidthPt, bottomHeightPt));
        PlacePanel(_rightSplitter,
            EditorChromeLayout.RightSplitter(screenWidth, screenHeight, scale, rightWidthPt, bottomHeightPt));
        PlacePanel(_bottomSplitter,
            EditorChromeLayout.BottomSplitter(screenWidth, screenHeight, scale, bottomHeightPt));

        // The bottom shelf's static "Assets" tab in its tab strip (below the splitter).
        LayoutBottomTab(bottomBar, scale);

        // The window top bar's editing buttons, then the Scene header's transport buttons — both
        // through the same button-row layout (the header anchors inside the carved-out header rect).
        LayoutButtonRow(_buttonEntities, _labelEntities, _buttons,
            EditorChromeLayout.ButtonRow(MeasureWidths(_buttons, scale), scale), scale);
        LayoutButtonRow(_headerButtonEntities, _headerLabelEntities, _headerButtons,
            EditorChromeLayout.ButtonRowIn(sceneHeader, MeasureWidths(_headerButtons, scale), scale), scale);

        LaidOutWidth = screenWidth;
        LaidOutHeight = screenHeight;
        LaidOutScale = scale;
        LaidOutLeftWidthPt = leftWidthPt;
        LaidOutRightWidthPt = rightWidthPt;
        LaidOutBottomHeightPt = bottomHeightPt;
    }

    /// <summary>Per-button pixel widths for a button set (label width measured + scaled + padded).</summary>
    private int[] MeasureWidths((EditorToolbarAction action, string label)[] buttons, float scale)
    {
        var widths = new int[buttons.Length];
        for (var i = 0; i < buttons.Length; i++)
            widths[i] = EditorChromeLayout.ButtonWidth(_measureLabel(buttons[i].label) * scale, scale);
        return widths;
    }

    /// <summary>Positions a button set (button visual bounds + label) into the given laid-out rects.</summary>
    private void LayoutButtonRow(List<Entity> buttonEntities, List<Entity> labelEntities,
        (EditorToolbarAction action, string label)[] buttons, Rectangle[] rects, float scale)
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
            // Label glyphs scale with the DPR: same physical size, denser pixels (see the class
            // doc's Text choice — at scale 1 this is the historical 1/3 downscale).
            ref var text = ref labelEntities[i].Get<DynamicTextComponent>();
            text.Scale = LabelScale * scale;
            PlaceEntity(labelEntities[i],
                new Vector2(bounds.X + EditorChromeLayout.Px(EditorChromeLayout.ButtonPaddingX, scale),
                    bounds.Y + labelOffsetY));
        }
    }

    /// <summary>Positions the bottom shelf's single static "Assets" tab (fill + label + active-accent
    /// underline) in the shelf's tab strip, sized to its label — the same tab geometry the right
    /// strip's tabs use, so the two strips read consistently.</summary>
    private void LayoutBottomTab(Rectangle bottomBar, float scale)
    {
        var strip = EditorChromeLayout.TabStrip(bottomBar, scale);
        var labelWidthPx = _measureLabel("Assets") * scale;
        var tabWidth = EditorChromeLayout.TabWidth(labelWidthPx, scale);
        var tab = EditorChromeLayout.TabRow(strip, new[] { tabWidth }, scale)[0];

        PlacePanel(_bottomTabFill, tab);
        PlacePanel(_bottomTabUnderline, EditorChromeLayout.TabUnderline(tab, scale));

        var labelHeight = (_font?.LineHeight ?? 48f) * LabelScale * scale;
        ref var text = ref _bottomTabLabel.Get<DynamicTextComponent>();
        text.Scale = LabelScale * scale;
        PlaceEntity(_bottomTabLabel, new Vector2(
            tab.X + EditorChromeLayout.Px(EditorChromeLayout.TabPaddingX, scale),
            tab.Y + (tab.Height - labelHeight) / 2f));
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

    private Entity CreateButton(EditorToolbarAction action, Entity labelEntity)
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
