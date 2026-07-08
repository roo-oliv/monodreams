using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the level-editor premise "The editor shell insets the game viewport and renders its
/// chrome at native resolution" (Wave 7; transport model since the F1 retirement): the pure
/// chrome layout (panels cover exactly the reserved margins; the toolbar row sits inside the top
/// bar), the builder's native-pixel entities + relayout-on-resize, the toolbar's native
/// <c>ScreenPosition</c> hit-test (editing buttons dispatch while Paused and are inert while
/// Playing; the transport buttons dispatch in both — see <c>EditorTransportTests</c>), the shell
/// system's CONSTANT composition (viewport inset + cursor applied in both transport states +
/// dispose restore), and the margin-click guard (<c>OutsideViewport</c> presses never pick).
/// Pure logic — no GraphicsDevice: the builder takes the injected label-measure seam and the
/// <see cref="ViewportManager"/> never dereferences its <c>Game</c>.
/// </summary>
public class EditorShellTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    /// <summary>A deterministic label measure (already LabelScale-adjusted): 12 px per char.</summary>
    private static float Measure(string label) => label.Length * 12f;

    private static EditorChromeBuilder BuiltChrome(World world, int width = 1600, int height = 900)
    {
        var chrome = new EditorChromeBuilder(world, Measure);
        chrome.Build(width, height);
        return chrome;
    }

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    // ---- Pure layout: the panels cover exactly the reserved viewport-inset margins ----

    [Fact]
    public void ChromeLayout_PanelsCoverExactlyTheInsetMargins()
    {
        const int w = 1600, h = 900;
        var (left, top, right, bottom) = EditorChromeLayout.ViewportInset();
        var topBarH = EditorChromeLayout.TopBarHeight;

        var topBar = EditorChromeLayout.TopBar(w);
        var leftPanel = EditorChromeLayout.LeftPanel(w, h);
        var rightPanel = EditorChromeLayout.RightPanel(w, h);
        var bottomBar = EditorChromeLayout.BottomBar(w, h);
        var sceneHeader = EditorChromeLayout.SceneHeader(w, h);

        // UX2-B: left activates + the game-viewport top inset is the top bar PLUS the Scene header.
        Assert.Equal(EditorChromeLayout.LeftPanelWidth, left);
        Assert.Equal(topBarH + EditorChromeLayout.SceneHeaderHeight, top);

        // Top bar: full width, the (thin, global) bar height.
        Assert.Equal(new Rectangle(0, 0, w, topBarH), topBar);
        // Bottom strip: full width, exactly the bottom margin tall, flush with the window bottom.
        Assert.Equal(new Rectangle(0, h - bottom, w, bottom), bottomBar);
        // Left + right panels: exactly their margins wide, spanning top bar → bottom shelf.
        Assert.Equal(new Rectangle(0, topBarH, left, h - topBarH - bottom), leftPanel);
        Assert.Equal(new Rectangle(w - right, topBarH, right, h - topBarH - bottom), rightPanel);
        // The Scene panel header: the extra top inset, between the strips, below the top bar.
        Assert.Equal(new Rectangle(left, topBarH, w - left - right, EditorChromeLayout.SceneHeaderHeight), sceneHeader);
        Assert.Equal(top - topBarH, sceneHeader.Height); // the header IS the extra top inset
    }

    [Fact]
    public void ChromeLayout_ButtonRow_SitsInsideTheTopBar()
    {
        var rects = EditorChromeLayout.ButtonRow(new[] { 50, 60, 70 });

        var expectedY = (EditorChromeLayout.TopBarHeight - EditorChromeLayout.ButtonHeight) / 2;
        Assert.Equal(new Rectangle(EditorChromeLayout.RowMarginX, expectedY, 50, EditorChromeLayout.ButtonHeight), rects[0]);
        Assert.Equal(rects[0].Right + EditorChromeLayout.ButtonGap, rects[1].X);
        Assert.Equal(rects[1].Right + EditorChromeLayout.ButtonGap, rects[2].X);
        foreach (var r in rects)
            Assert.True(EditorChromeLayout.TopBar(1600).Contains(r), $"button {r} escapes the top bar");
    }

    // ---- Builder: native-pixel entities on the Editor target, relayout on resize ----

    [Fact]
    public void ChromeBuilder_BuildsButtonsWithNativePixelBounds_OnTheEditorTarget()
    {
        using var world = new World();
        var chrome = BuiltChrome(world);

        var topBar = EditorChromeLayout.TopBar(1600);
        var sceneHeader = EditorChromeLayout.SceneHeader(1600, 900);
        using var buttons = world.GetEntities().With<ToolbarButtonComponent>().AsSet();
        var windowCount = 0;
        var headerCount = 0;
        foreach (var button in buttons.GetEntities())
        {
            ref readonly var tb = ref button.Get<ToolbarButtonComponent>();
            ref readonly var visual = ref button.Get<SimpleButtonComponent>();

            // Every button is physical pixels in EITHER the window top bar (editing actions) or the
            // Scene panel header (transport — UX2-B), and the visual matches its bounds.
            if (topBar.Contains(tb.Bounds)) windowCount++;
            else if (sceneHeader.Contains(tb.Bounds)) headerCount++;
            else Assert.Fail($"button {tb.Bounds} escapes both the top bar and the Scene header");
            Assert.Equal(tb.Bounds.Width, (int)visual.Size.X);
            Assert.Equal(tb.Bounds.Height, (int)visual.Size.Y);
            Assert.Equal(RenderTargetID.Editor, visual.Target);
        }
        // The window bar carries the editing set; the Scene header carries the transport set PLUS the
        // UX2-E right-corner "Camera view" nav button and the UX2-F [Scene | Game] mode-toggle segments
        // (fixed header affordances, not part of HeaderButtons).
        Assert.Equal(EditorChromeBuilder.DefaultButtons.Length, windowCount);
        Assert.Equal(EditorChromeBuilder.HeaderButtons.Length + 1 + 2, headerCount);
        Assert.Equal(1600, chrome.LaidOutWidth);
        Assert.Equal(900, chrome.LaidOutHeight);
    }

    [Fact]
    public void ChromeBuilder_PanelsAreOpaqueAndCoverTheMargins()
    {
        using var world = new World();
        BuiltChrome(world);

        // The panels + the splitters + the bottom "Assets" tab fill/underline are all fill-only
        // meshes (SimpleButtonComponent without a ToolbarButtonComponent). Every one must be opaque
        // (the premultiplied-alpha mesh rule) and on the Editor target; the PANEL + Scene-header sizes
        // must be present among them (UX2-B added the left panel + the Scene header band).
        using var all = world.GetEntities().With<SimpleButtonComponent>().Without<ToolbarButtonComponent>().AsSet();
        var sizes = new List<Vector2>();
        foreach (var panel in all.GetEntities())
        {
            ref readonly var visual = ref panel.Get<SimpleButtonComponent>();
            Assert.Equal(RenderTargetID.Editor, visual.Target);
            Assert.Equal(byte.MaxValue, visual.FillColor.A); // opaque — readable over any level
            sizes.Add(visual.Size);
        }
        var innerH = 900 - EditorChromeLayout.TopBarHeight - EditorChromeLayout.BottomBarHeight;
        Assert.Contains(new Vector2(1600, EditorChromeLayout.TopBarHeight), sizes);                 // top bar
        Assert.Contains(new Vector2(EditorChromeLayout.LeftPanelWidth, innerH), sizes);             // left panel
        Assert.Contains(new Vector2(EditorChromeLayout.RightPanelWidth, innerH), sizes);            // right panel
        Assert.Contains(new Vector2(1600, EditorChromeLayout.BottomBarHeight), sizes);              // bottom shelf
        Assert.Contains(new Vector2(1600 - EditorChromeLayout.LeftPanelWidth - EditorChromeLayout.RightPanelWidth,
            EditorChromeLayout.SceneHeaderHeight), sizes);                                          // Scene header
    }

    [Fact]
    public void ChromeBuilder_Relayout_TracksTheNewWindowSize()
    {
        using var world = new World();
        var chrome = BuiltChrome(world, 1600, 900);

        chrome.Relayout(1920, 1080);

        Assert.Equal(1920, chrome.LaidOutWidth);
        Assert.Equal(1080, chrome.LaidOutHeight);

        // The right panel moved to the new right edge with the new inner height.
        using var panels = world.GetEntities().With<SimpleButtonComponent>().Without<ToolbarButtonComponent>().AsSet();
        var found = false;
        foreach (var panel in panels.GetEntities())
        {
            ref readonly var visual = ref panel.Get<SimpleButtonComponent>();
            if ((int)visual.Size.X != EditorChromeLayout.RightPanelWidth) continue;
            found = true;
            Assert.Equal(1920 - EditorChromeLayout.RightPanelWidth,
                (int)panel.Get<TransformComponent>().Position.X);
            Assert.Equal(1080 - EditorChromeLayout.TopBarHeight - EditorChromeLayout.BottomBarHeight,
                (int)visual.Size.Y);
        }
        Assert.True(found, "right panel not found after relayout");
    }

    // ---- Toolbar: native ScreenPosition hit-test; dispatch in Edit, inert in Play ----

    private static Entity MakeButton(World world, EditorToolbarAction action, Rectangle bounds)
    {
        var button = world.CreateEntity();
        button.Set(new TransformComponent(new Vector2(bounds.X, bounds.Y)));
        button.Set(new SimpleButtonComponent { Size = new Vector2(bounds.Width, bounds.Height) });
        button.Set(new ToolbarButtonComponent { Action = action, Bounds = bounds });
        return button;
    }

    [Fact]
    public void ToolbarSystem_NativeHitTest_DispatchesTheClickedButtonsAction()
    {
        using var world = new World();
        var button = MakeButton(world, EditorToolbarAction.Save, new Rectangle(100, 7, 80, 30));
        var cursor = MakeCursor(world);

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(120, 20); // physical pixels, inside the button
        input.LeftButtonReleased = true;

        var dispatched = new List<EditorToolbarAction>();
        using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a));
        toolbar.Update(Edit());

        Assert.Equal(new[] { EditorToolbarAction.Save }, dispatched);
        Assert.True(button.Get<ToolbarButtonComponent>().IsHovered);
    }

    [Fact]
    public void ToolbarSystem_ClickOutsideTheBounds_DoesNotDispatch()
    {
        using var world = new World();
        MakeButton(world, EditorToolbarAction.Save, new Rectangle(100, 7, 80, 30));
        var cursor = MakeCursor(world);

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(500, 500);
        input.LeftButtonReleased = true;

        var dispatched = new List<EditorToolbarAction>();
        using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a));
        toolbar.Update(Edit());

        Assert.Empty(dispatched);
    }

    [Fact]
    public void ToolbarSystem_WhilePlaying_EditingButtonsAreInert()
    {
        // Transport model: the toolbar stays live in Play, but the EDITING buttons (Save, tools,
        // undo…) dispatch only while Paused — a click belongs to the game while Playing. The
        // transport buttons' both-modes dispatch is covered in EditorTransportTests.
        using var world = new World();
        MakeButton(world, EditorToolbarAction.Save, new Rectangle(100, 7, 80, 30));
        var cursor = MakeCursor(world);

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(120, 20);
        input.LeftButtonReleased = true;

        var dispatched = new List<EditorToolbarAction>();
        using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a));
        toolbar.Update(Play());

        Assert.Empty(dispatched);
    }

    // ---- Shell system: run-mode sync of inset + cursor; resize; dispose restore ----

    private static (EditorShellSystem shell, ViewportManager vm, EditorChromeBuilder chrome,
        Func<bool?> osCursor) MakeShell(World world, int width = 1600, int height = 900)
    {
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = width, ScreenHeight = height };
        var chrome = new EditorChromeBuilder(world, Measure);
        chrome.Build(1, 1); // deliberately stale — the shell must relayout to the real size
        bool? osCursorVisible = null;
        var shell = new EditorShellSystem(world, vm, chrome, visible => osCursorVisible = visible);
        return (shell, vm, chrome, () => osCursorVisible);
    }

    [Fact]
    public void ShellSystem_SetsTheInset_RelayoutsChrome_AndSwapsToTheOsCursor()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var (shell, vm, chrome, osCursor) = MakeShell(world);
        using var _ = shell;

        shell.Update(Edit());

        Assert.True(vm.HasViewportInset);
        var (left, top, right, bottom) = EditorChromeLayout.ViewportInset();
        // The inset viewport lives inside the reserved margins.
        var dest = vm.DestinationRectangle;
        Assert.True(dest.X >= left && dest.Y >= top);
        Assert.True(dest.Right <= 1600 - right && dest.Bottom <= 900 - bottom);
        // Chrome was relayouted to the real window size.
        Assert.Equal(1600, chrome.LaidOutWidth);
        Assert.Equal(900, chrome.LaidOutHeight);
        // OS cursor shown, game cursor sprite hidden.
        Assert.True(osCursor());
        Assert.False(cursor.Get<CursorControllerComponent>().IsVisible);
    }

    [Fact]
    public void ShellSystem_StaysComposedWhilePlaying()
    {
        // Transport model: the shell never collapses — Playing runs the game inside the inset
        // viewport with the chrome (and the OS pointer) still up.
        using var world = new World();
        var cursor = MakeCursor(world);
        var (shell, vm, _, osCursor) = MakeShell(world);
        using var _1 = shell;

        shell.Update(Edit());
        shell.Update(Play());

        Assert.True(vm.HasViewportInset);
        Assert.True(osCursor());
        Assert.False(cursor.Get<CursorControllerComponent>().IsVisible);
    }

    [Fact]
    public void ShellSystem_ResizeWhileEditing_Relayouts()
    {
        using var world = new World();
        MakeCursor(world);
        var (shell, vm, chrome, _) = MakeShell(world);
        using var _1 = shell;

        shell.Update(Edit());
        vm.ScreenWidth = 1920;
        vm.ScreenHeight = 1080;
        shell.Update(Edit());

        Assert.Equal(1920, chrome.LaidOutWidth);
        Assert.Equal(1080, chrome.LaidOutHeight);
        Assert.True(vm.HasViewportInset);
    }

    [Fact]
    public void ShellSystem_ResizeWithDprChange_RecomputesChromeInsetAndMouseMapping()
    {
        // Resizable editor window (EF1): the host feeds a new ScreenWidth/Height AND a new
        // DevicePixelRatio (what OnWindowResize → ApplyEditorHiDpi does on a Retina resize). The
        // shell must relayout the chrome, re-apply the DPR-scaled viewport inset, and — because the
        // inset lives on the single ViewportManager — the mouse mapping must follow the new game
        // viewport (a point at its centre maps in; a point in the reserved top margin maps out).
        using var world = new World();
        MakeCursor(world);
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900, DevicePixelRatio = 1f };
        var chrome = new EditorChromeBuilder(world, Measure);
        chrome.Build(1, 1); // stale — the shell relayouts to the real size + scale
        using var shell = new EditorShellSystem(world, vm, chrome, null);

        shell.Update(Edit());
        Assert.Equal(1600, chrome.LaidOutWidth);
        Assert.Equal(1f, chrome.LaidOutScale);
        var (_, top1, _, _) = EditorChromeLayout.ViewportInset(1f);
        Assert.True(vm.DestinationRectangle.Y >= top1);
        var centreBefore = new Vector2(vm.DestinationRectangle.Center.X, vm.DestinationRectangle.Center.Y);
        Assert.NotNull(vm.ScaleMouseToVirtualCoordinates(centreBefore)); // inside the game viewport

        // Resize + HiDPI re-back: larger device backbuffer at 2× DPR.
        vm.DevicePixelRatio = 2f;
        vm.ScreenWidth = 3840;
        vm.ScreenHeight = 2160;
        shell.Update(Edit());

        // Chrome relayouted to the new size AND scale; the inset is the DPR-scaled top bar.
        Assert.Equal(3840, chrome.LaidOutWidth);
        Assert.Equal(2160, chrome.LaidOutHeight);
        Assert.Equal(2f, chrome.LaidOutScale);
        var (_, top2, right2, bottom2) = EditorChromeLayout.ViewportInset(2f);
        Assert.True(top2 > top1); // the margin scaled up with the DPR
        Assert.True(vm.HasViewportInset);
        var dest = vm.DestinationRectangle;
        Assert.True(dest.Y >= top2);
        Assert.True(dest.Right <= 3840 - right2 && dest.Bottom <= 2160 - bottom2);
        // Mouse mapping tracks the new inset viewport.
        Assert.NotNull(vm.ScaleMouseToVirtualCoordinates(new Vector2(dest.Center.X, dest.Center.Y)));
        Assert.Null(vm.ScaleMouseToVirtualCoordinates(new Vector2(dest.Center.X, top2 / 2))); // in the top margin → chrome
    }

    [Fact]
    public void ShellSystem_Dispose_ClearsTheInsetAndHidesTheOsCursor()
    {
        using var world = new World();
        MakeCursor(world);
        var (shell, vm, _, osCursor) = MakeShell(world);

        shell.Update(Edit());
        Assert.True(vm.HasViewportInset);

        shell.Dispose();

        // The ViewportManager + host Game outlive the screen — never leak the shell state.
        Assert.False(vm.HasViewportInset);
        Assert.False(osCursor());
    }

    // ---- Margin clicks are chrome clicks: a press with OutsideViewport never picks ----

    [Fact]
    public void Selection_PressOverTheChromeMargins_DoesNotPickOrClear()
    {
        using var world = new World();

        // A rendered sprite under the (stale) world point, and a pre-selected second sprite.
        var sprite = world.CreateEntity();
        sprite.Set(new TransformComponent(new Vector2(0, 0)));
        sprite.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 10, 10),
            Size = new Vector2(10, 10),
            Origin = Vector2.Zero,
            Target = RenderTargetID.Main,
        });
        sprite.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = 0.5f });
        sprite.Set(new VisibleComponent());

        var selectedBefore = world.CreateEntity();
        selectedBefore.Set(new SelectedComponent());

        var cursor = MakeCursor(world);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(5, 5); // stale — frozen at the last inside point
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        input.OutsideViewport = true; // the pointer is actually over the chrome

        using var selection = new SelectionSystem(world);
        selection.Update(Edit());

        // Neither picked the sprite nor cleared the existing selection.
        Assert.False(sprite.Has<SelectedComponent>());
        Assert.True(selectedBefore.Has<SelectedComponent>());
    }

    // ---- Device-pixel-ratio (HiDPI): point metrics scale; hit-test space == render space ----

    [Fact]
    public void ChromeLayout_AtDpr2_DoublesEveryPointMetric()
    {
        const int w = 3840, h = 2160; // a 1920×1080-point window on a 2× (Retina) backbuffer
        const int topBar2 = 44 * 2, header2 = 40 * 2, left2 = 240 * 2, right2 = 280 * 2, bottom2 = 168 * 2;

        var (left, top, right, bottom) = EditorChromeLayout.ViewportInset(2f);
        // left = LeftPanelWidth × 2; top = (TopBar 44 + SceneHeader 40) × 2; bottom = BottomBarHeight × 2.
        Assert.Equal((left2, topBar2 + header2, right2, bottom2), (left, top, right, bottom));

        Assert.Equal(new Rectangle(0, 0, w, topBar2), EditorChromeLayout.TopBar(w, 2f));
        Assert.Equal(new Rectangle(0, topBar2, left2, h - topBar2 - bottom2), EditorChromeLayout.LeftPanel(w, h, 2f));
        Assert.Equal(new Rectangle(w - right2, topBar2, right2, h - topBar2 - bottom2), EditorChromeLayout.RightPanel(w, h, 2f));
        Assert.Equal(new Rectangle(0, h - bottom2, w, bottom2), EditorChromeLayout.BottomBar(w, h, 2f));
        Assert.Equal(new Rectangle(left2, topBar2, w - left2 - right2, header2), EditorChromeLayout.SceneHeader(w, h, 2f));

        // Button row: margins/gaps/heights double; widths are caller-scaled.
        var rects = EditorChromeLayout.ButtonRow(new[] { 100, 120 }, 2f);
        Assert.Equal(new Rectangle(20, (topBar2 - 60) / 2, 100, 60), rects[0]);
        Assert.Equal(rects[0].Right + 16, rects[1].X);
    }

    [Fact]
    public void ChromeLayout_DefaultScale_IsThePreDprLayout()
    {
        // scale 1 reproduces the point constants unscaled. UX2-B: left activates (240) and the top
        // inset is the top bar (44) PLUS the Scene panel header (40) = 84; bottom tracks BottomBarHeight.
        Assert.Equal((240, 44 + 40, 280, 168), EditorChromeLayout.ViewportInset());
        Assert.Equal(EditorChromeLayout.TopBar(1600), EditorChromeLayout.TopBar(1600, 1f));
    }

    [Fact]
    public void SystemsPanelLayout_AtDpr2_ScalesRowsAndCheckboxes()
    {
        var panel = new Rectangle(3280, 88, 560, 2024);

        // Rows are 44 px tall (22 points × 2) inside the padded content area.
        var content = SystemsPanelLayout.ContentArea(panel, 2f);
        Assert.Equal(panel.X + 20, content.X);
        Assert.Equal(panel.Y + 16, content.Y);
        var line0 = SystemsPanelLayout.LineRect(panel, 0, 2f);
        Assert.Equal(44, line0.Height);
        Assert.Equal(SystemsPanelLayout.LineRect(panel, 1, 2f).Y, line0.Y + 44);

        // Checkbox 24×24 (12 points × 2), centered in the row.
        var checkbox = SystemsPanelLayout.CheckboxRect(line0, 0, 2f);
        Assert.Equal(24, checkbox.Width);
        Assert.Equal(24, checkbox.Height);
        Assert.Equal(line0.Y + (44 - 24) / 2, checkbox.Y);
    }

    [Fact]
    public void ShellAtDpr2_ChromeHitTestSpace_EqualsChromeRenderSpace()
    {
        // THE HiDPI invariant: ScreenPosition (device pixels — CursorInputSystem multiplies the
        // raw mouse by DevicePixelRatio), the chrome layout, and the backbuffer all share ONE
        // space. A pointer physically over a button must hit its Bounds at any DPR.
        using var world = new World();
        MakeCursor(world);
        var vm = new ViewportManager(null, 800, 600)
        {
            ScreenWidth = 3840, ScreenHeight = 2160, DevicePixelRatio = 2f,
        };
        var chrome = new EditorChromeBuilder(world, Measure);
        chrome.Build(1, 1); // stale — the shell relayouts with the real size AND scale
        using var shell = new EditorShellSystem(world, vm, chrome, null);

        shell.Update(Edit());
        Assert.Equal(2f, chrome.LaidOutScale);

        // The inset the shell applied is the DPR-scaled one: top bar (88) + Scene header (80) = 168.
        Assert.True(vm.HasViewportInset);
        Assert.True(vm.DestinationRectangle.Y >= (44 + 40) * 2);

        // Every button renders inside the scaled top bar OR the scaled Scene header, and a device-pixel
        // click at its centre hits: bounds (hit-test) == visual size/position (render), same device space.
        using var buttons = world.GetEntities().With<ToolbarButtonComponent>().AsSet();
        var dispatched = new List<EditorToolbarAction>();
        using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a));
        var topBar = EditorChromeLayout.TopBar(3840, 2f);
        var sceneHeader = EditorChromeLayout.SceneHeader(3840, 2160, 2f);
        Entity first = default;
        foreach (var button in buttons.GetEntities())
        {
            ref readonly var tb = ref button.Get<ToolbarButtonComponent>();
            Assert.True(topBar.Contains(tb.Bounds) || sceneHeader.Contains(tb.Bounds),
                $"button {tb.Bounds} escapes both the scaled top bar and Scene header");
            ref readonly var visual = ref button.Get<SimpleButtonComponent>();
            Assert.Equal(tb.Bounds.Width, (int)visual.Size.X);
            Assert.Equal(tb.Bounds.Height, (int)visual.Size.Y);
            if (first == default) first = button;
        }

        using var cursors = world.GetEntities().With<CursorInputComponent>().AsSet();
        foreach (var cursor in cursors.GetEntities())
        {
            ref var input = ref cursor.Get<CursorInputComponent>();
            var bounds = first.Get<ToolbarButtonComponent>().Bounds;
            input.ScreenPosition = new Vector2(bounds.Center.X, bounds.Center.Y);
            input.LeftButtonReleased = true;
        }
        toolbar.Update(Edit());
        Assert.Single(dispatched);
    }
}
