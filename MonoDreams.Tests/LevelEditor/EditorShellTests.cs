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

        var topBar = EditorChromeLayout.TopBar(w);
        var rightPanel = EditorChromeLayout.RightPanel(w, h);
        var bottomBar = EditorChromeLayout.BottomBar(w, h);

        // Top bar: full width, exactly the top margin tall.
        Assert.Equal(new Rectangle(0, 0, w, top), topBar);
        // Bottom strip: full width, exactly the bottom margin tall, flush with the window bottom.
        Assert.Equal(new Rectangle(0, h - bottom, w, bottom), bottomBar);
        // Right panel: exactly the right margin wide, spanning between the two bars.
        Assert.Equal(new Rectangle(w - right, top, right, h - top - bottom), rightPanel);
        // No left strip reserved today.
        Assert.Equal(0, left);
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

        using var buttons = world.GetEntities().With<ToolbarButtonComponent>().AsSet();
        var count = 0;
        foreach (var button in buttons.GetEntities())
        {
            count++;
            ref readonly var tb = ref button.Get<ToolbarButtonComponent>();
            ref readonly var visual = ref button.Get<SimpleButtonComponent>();

            // Bounds are physical pixels inside the top bar, and the visual matches them.
            Assert.True(EditorChromeLayout.TopBar(1600).Contains(tb.Bounds));
            Assert.Equal(tb.Bounds.Width, (int)visual.Size.X);
            Assert.Equal(tb.Bounds.Height, (int)visual.Size.Y);
            Assert.Equal(RenderTargetID.Editor, visual.Target);
        }
        Assert.Equal(EditorChromeBuilder.DefaultButtons.Length, count);
        Assert.Equal(1600, chrome.LaidOutWidth);
        Assert.Equal(900, chrome.LaidOutHeight);
    }

    [Fact]
    public void ChromeBuilder_PanelsAreOpaqueAndCoverTheMargins()
    {
        using var world = new World();
        BuiltChrome(world);

        // Three panels (fill-only meshes) + the buttons all carry SimpleButtonComponent; panels
        // are the ones without a ToolbarButtonComponent.
        using var all = world.GetEntities().With<SimpleButtonComponent>().Without<ToolbarButtonComponent>().AsSet();
        var sizes = new List<Vector2>();
        foreach (var panel in all.GetEntities())
        {
            ref readonly var visual = ref panel.Get<SimpleButtonComponent>();
            Assert.Equal(RenderTargetID.Editor, visual.Target);
            Assert.Equal(byte.MaxValue, visual.FillColor.A); // opaque — readable over any level
            sizes.Add(visual.Size);
        }
        Assert.Equal(3, sizes.Count);
        Assert.Contains(new Vector2(1600, EditorChromeLayout.TopBarHeight), sizes);
        Assert.Contains(new Vector2(EditorChromeLayout.RightPanelWidth,
            900 - EditorChromeLayout.TopBarHeight - EditorChromeLayout.BottomBarHeight), sizes);
        Assert.Contains(new Vector2(1600, EditorChromeLayout.BottomBarHeight), sizes);
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

        var (left, top, right, bottom) = EditorChromeLayout.ViewportInset(2f);
        Assert.Equal((0, 88, 560, 336), (left, top, right, bottom)); // bottom = BottomBarHeight(168) × 2

        Assert.Equal(new Rectangle(0, 0, w, 88), EditorChromeLayout.TopBar(w, 2f));
        Assert.Equal(new Rectangle(w - 560, 88, 560, h - 88 - 336), EditorChromeLayout.RightPanel(w, h, 2f));
        Assert.Equal(new Rectangle(0, h - 336, w, 336), EditorChromeLayout.BottomBar(w, h, 2f));

        // Button row: margins/gaps/heights double; widths are caller-scaled.
        var rects = EditorChromeLayout.ButtonRow(new[] { 100, 120 }, 2f);
        Assert.Equal(new Rectangle(20, (88 - 60) / 2, 100, 60), rects[0]);
        Assert.Equal(rects[0].Right + 16, rects[1].X);
    }

    [Fact]
    public void ChromeLayout_DefaultScale_IsThePreDprLayout()
    {
        // Byte-identical back-compat: scale 1 reproduces the point constants unscaled. (The bottom
        // value tracks BottomBarHeight, raised 24 → 104 for the asset palette strip, then 104 → 168
        // for the redesigned asset cards — icon on top, label on the bottom, FW3.)
        Assert.Equal((0, 44, 280, 168), EditorChromeLayout.ViewportInset());
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

        // The inset the shell applied is the DPR-scaled one (88-px top bar, not 44).
        Assert.True(vm.HasViewportInset);
        Assert.True(vm.DestinationRectangle.Y >= 88);

        // Every button renders inside the scaled top bar, and a device-pixel click at its centre
        // hits: bounds (hit-test) == visual size/position (render), in the same device space.
        using var buttons = world.GetEntities().With<ToolbarButtonComponent>().AsSet();
        var dispatched = new List<EditorToolbarAction>();
        using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a));
        var topBar = EditorChromeLayout.TopBar(3840, 2f);
        Entity first = default;
        foreach (var button in buttons.GetEntities())
        {
            ref readonly var tb = ref button.Get<ToolbarButtonComponent>();
            Assert.True(topBar.Contains(tb.Bounds), $"button {tb.Bounds} escapes the scaled top bar");
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
