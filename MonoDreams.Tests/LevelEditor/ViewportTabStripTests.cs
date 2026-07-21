using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PF-B <b><see cref="ViewportTabStripSystem"/></b> — the header tab strip that replaced the
/// Scene/Game mode toggle (premise "The editor toolbar's buttons drive the same shared editor instances …"
/// + "The viewport context stack …"). It is descriptor-driven (renders
/// <see cref="EditorShellStateComponent.ViewportTabs"/>), draws the active tab with a <see cref="EditorTheme.Bg1"/>
/// fill + <see cref="EditorTheme.Accent"/> underline, shows a ▶ play marker on the Game tab and a <c>×</c> on
/// closable tabs, and hit-tests the cursor's <c>ScreenPosition</c> → <c>SwitchToTab</c> / <c>CloseTab</c> by
/// slot (close taking priority over the body). Font-null (layout-only) so it runs headless.
/// </summary>
public class ViewportTabStripTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static ViewportManager Vm(int w = 1600, int h = 900, float dpr = 1f) =>
        new(null!, 800, 600) { ScreenWidth = w, ScreenHeight = h, DevicePixelRatio = dpr };

    private sealed class Harness
    {
        public readonly World World = new();
        public readonly EditorShellStateComponent Shell = new();
        public readonly ViewportTabStripSystem Strip;
        public readonly Entity Cursor;
        public readonly List<int> Switched = new();
        public readonly List<int> Closed = new();
        public bool Suppressed;

        public Harness(float dpr = 1f)
        {
            var vm = Vm(dpr: dpr);
            Cursor = World.CreateEntity();
            Cursor.Set(new CursorInputComponent());
            Strip = new ViewportTabStripSystem(World, vm, label => label.Length * 6f, Shell,
                switchToTab: (i, _) => Switched.Add(i),
                closeTab: (i, _) => Closed.Add(i),
                isInputSuppressed: () => Suppressed);
        }

        public void SetTabs(int active, params (ViewportContextKind kind, string label, bool closable)[] tabs)
        {
            var list = new List<ViewportTabDescriptor>();
            foreach (var (kind, label, closable) in tabs)
                list.Add(new ViewportTabDescriptor(kind, label.ToLowerInvariant(), label, closable));
            Shell.ViewportTabs = list;
            Shell.ActiveViewportTab = active;
        }

        public Entity TabAtSlot(int slot)
        {
            using var set = World.GetEntities().With<ViewportTabComponent>().AsSet();
            foreach (var e in set.GetEntities())
                if (e.Get<ViewportTabComponent>().Slot == slot) return e;
            return default;
        }

        public void ClickAt(Point p)
        {
            ref var input = ref Cursor.Get<CursorInputComponent>();
            input.ScreenPosition = new Vector2(p.X, p.Y);
            input.LeftButtonReleased = true;
        }
    }

    private static bool MeshEmpty(Entity? mesh) =>
        mesh is { IsAlive: true } m && m.Get<DrawComponent>().Vertices is { Length: 0 };

    private static bool MeshFilled(Entity? mesh) =>
        mesh is { IsAlive: true } m && m.Get<DrawComponent>().Vertices is { Length: > 0 };

    // ─────────────── Render: descriptor-driven, active underline, Game ▶ + closable × ────────────────

    [Fact]
    public void Strip_RendersDescriptors_ActiveUnderlined_GamePlayMarker_ClosableCross()
    {
        var h = new Harness();
        h.SetTabs(active: 1,
            (ViewportContextKind.Scene, "Scene", false),
            (ViewportContextKind.Game, "Game", true));
        h.Strip.Update(Play()); // live even while Playing

        var scene = h.TabAtSlot(0);
        var game = h.TabAtSlot(1);

        // Active (Game) tab: Bg1 fill + a non-empty accent underline; the Scene tab is inactive.
        Assert.Equal(EditorTheme.Bg1, game.Get<SimpleButtonComponent>().FillColor);
        Assert.True(MeshFilled(game.Get<ViewportTabComponent>().UnderlineEntity));
        Assert.True(MeshEmpty(scene.Get<ViewportTabComponent>().UnderlineEntity));
        Assert.NotEqual(EditorTheme.Bg1, scene.Get<SimpleButtonComponent>().FillColor);

        // The Game tab shows a ▶ play marker + a × (closable); the Scene tab shows neither.
        Assert.True(MeshFilled(game.Get<ViewportTabComponent>().PlayMarkerEntity));
        Assert.True(MeshFilled(game.Get<ViewportTabComponent>().CloseEntity));
        Assert.False(game.Get<ViewportTabComponent>().CloseBounds.IsEmpty);
        Assert.True(MeshEmpty(scene.Get<ViewportTabComponent>().PlayMarkerEntity));
        Assert.True(MeshEmpty(scene.Get<ViewportTabComponent>().CloseEntity));
        Assert.True(scene.Get<ViewportTabComponent>().CloseBounds.IsEmpty); // Scene is not closable
    }

    [Fact]
    public void Strip_IsDescriptorDriven_OneTabThenTwo_ParksExtras()
    {
        var h = new Harness();

        h.SetTabs(active: 0, (ViewportContextKind.Scene, "Scene", false));
        h.Strip.Update(Edit());
        Assert.False(h.TabAtSlot(0).Get<ViewportTabComponent>().Bounds.IsEmpty); // Scene rendered
        // Every other pool slot is parked (Slot -1, Empty bounds) — never hit-tests.
        var active0 = ActiveSlotCount(h);
        Assert.Equal(1, active0);

        // Append a Game descriptor (what the stack does on Play) — the SAME renderer now shows 2 tabs.
        h.SetTabs(active: 1,
            (ViewportContextKind.Scene, "Scene", false),
            (ViewportContextKind.Game, "Game", true));
        h.Strip.Update(Edit());
        Assert.Equal(2, ActiveSlotCount(h));
        Assert.False(h.TabAtSlot(1).Get<ViewportTabComponent>().Bounds.IsEmpty);
    }

    private static int ActiveSlotCount(Harness h)
    {
        var n = 0;
        using var set = h.World.GetEntities().With<ViewportTabComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ViewportTabComponent>().Slot >= 0) n++;
        return n;
    }

    // ─────────────── Hit-test: body → switch, × → close (close takes priority), DPR-2 ────────────────

    [Fact]
    public void Strip_ClickTabBody_SwitchesToThatSlot()
    {
        var h = new Harness();
        h.SetTabs(active: 1,
            (ViewportContextKind.Scene, "Scene", false),
            (ViewportContextKind.Game, "Game", true));
        h.Strip.Update(Play());

        // Click the Scene tab body (its centre, away from any × since Scene has none).
        var scene = h.TabAtSlot(0).Get<ViewportTabComponent>().Bounds;
        h.ClickAt(scene.Center);
        h.Strip.Update(Play());

        Assert.Equal(new[] { 0 }, h.Switched);
        Assert.Empty(h.Closed);
    }

    [Fact]
    public void Strip_ClickClose_ClosesThatSlot_TakesPriorityOverBody()
    {
        var h = new Harness();
        h.SetTabs(active: 1,
            (ViewportContextKind.Scene, "Scene", false),
            (ViewportContextKind.Game, "Game", true));
        h.Strip.Update(Play());

        // The × is INSIDE the Game tab body — clicking it must close, not switch.
        var close = h.TabAtSlot(1).Get<ViewportTabComponent>().CloseBounds;
        h.ClickAt(close.Center);
        h.Strip.Update(Play());

        Assert.Equal(new[] { 1 }, h.Closed);
        Assert.Empty(h.Switched);
    }

    [Fact]
    public void Strip_SuppressedDuringShellDrag_DoesNotDispatch()
    {
        var h = new Harness { Suppressed = true };
        h.SetTabs(active: 1,
            (ViewportContextKind.Scene, "Scene", false),
            (ViewportContextKind.Game, "Game", true));
        h.Strip.Update(Edit());

        h.ClickAt(h.TabAtSlot(0).Get<ViewportTabComponent>().Bounds.Center);
        h.Strip.Update(Edit());

        Assert.Empty(h.Switched); // a drag releasing over a tab must not fire it
        Assert.Empty(h.Closed);
    }

    [Fact]
    public void Strip_AtDpr2_TabsAreWithinTheScaledSceneHeader_AndDoubledHeight()
    {
        var h = new Harness(dpr: 2f);
        h.SetTabs(active: 1,
            (ViewportContextKind.Scene, "Scene", false),
            (ViewportContextKind.Game, "Game", true));
        h.Strip.Update(Edit());

        var header = EditorChromeLayout.SceneHeader(1600, 900, 2f,
            h.Shell.LeftWidthPt, h.Shell.RightWidthPt);
        var scene = h.TabAtSlot(0).Get<ViewportTabComponent>().Bounds;
        var game = h.TabAtSlot(1).Get<ViewportTabComponent>().Bounds;
        Assert.True(header.Contains(scene), $"Scene tab {scene} escapes the DPR-2 header {header}");
        Assert.True(header.Contains(game), $"Game tab {game} escapes the DPR-2 header {header}");
        Assert.Equal(scene.Right, game.Left); // adjacent
        Assert.Equal(EditorChromeLayout.Px(EditorChromeLayout.ButtonHeight, 2f), scene.Height); // DPR-2 height
    }

    // ─────────────── Play spawns + activates the Game tab (the strip picks up what the stack wrote) ───

    [Fact]
    public void Play_SpawnsAndActivatesTheGameTab_TheStripRendersIt()
    {
        var h = new Harness();
        // A transport driving a stack that writes to the SAME shell state the strip reads (the overlay's
        // wiring). Seams unwired → EnterGame still pushes the Game descriptor (a graceful no-snapshot).
        var history = new EditorHistory(h.World);
        var transport = new EditorTransport(h.World, history, h.Shell, "island");
        var state = Edit();

        transport.Play(state); // Play from the Scene tab spawns + activates the Game tab AND auto-plays

        Assert.Equal(ViewportContextKind.Game, transport.ActiveContextKind);
        Assert.Equal(RunMode.Play, state.RunMode);
        Assert.Equal(2, h.Shell.ViewportTabs.Count);
        Assert.Equal(1, h.Shell.ActiveViewportTab);

        h.Strip.Update(state);
        Assert.Equal(2, ActiveSlotCount(h));
        Assert.Equal(EditorTheme.Bg1, h.TabAtSlot(1).Get<SimpleButtonComponent>().FillColor); // Game active
    }
}
