#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX2-D context-menu wave (editor-shell-ui-ux-2 §4): the pure menu model + layout, the
/// <see cref="EditorContextMenuSystem"/> modality (open consumes the pointer, dialog-open suppresses,
/// Escape / click-away close), the menu → editor-command wiring (Order fires, Delete snapshots + undo
/// restores), Add Empty Entity (undoable, tagged), and Create Empty Scene (name-collision refusal +
/// the canonical empty-world write). Pure/logic tests — systems built with <c>font: null</c> and a
/// headless <see cref="ViewportManager"/>; the ONE write test swaps <see cref="PlatformServices.Current"/>
/// for an in-memory fake (never the real Content.mgcb).
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class EditorContextMenuTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static ViewportManager Vm() =>
        new(null!, 800, 600) { ScreenWidth = 800, ScreenHeight = 600, DevicePixelRatio = 1f };

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    private static void SetCursorScreen(Entity cursor, Point screen, bool leftReleased = false, bool leftDown = false)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(screen.X, screen.Y);
        input.LeftButton = (leftDown || leftReleased) && !leftReleased;
        input.LeftButtonReleased = leftReleased;
        input.LeftButtonPressed = false;
    }

    private static Point Center(Rectangle r) => new(r.Center.X, r.Center.Y);

    // ═══ Pure menu model ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EntityMenu_HasOrderSubmenu_PrefabActions_AndDangerDelete()
    {
        // Order ▸ | Collider ▸ | --- | Create Prefab from Selection… | Unpack Prefab (Danger) | --- | Delete (Danger).
        var items = EditorContextMenuModel.EntityMenu(hasSelection: true);
        Assert.Equal(7, items.Count);
        Assert.Equal(EditorMenuItemKind.Submenu, items[0].Kind);
        Assert.NotNull(items[0].Submenu);
        Assert.Equal(2, items[0].Submenu!.Count);
        Assert.Equal(EditorContextMenuModel.OrderForwardPath, items[0].Submenu![0].Path);
        Assert.Equal(EditorContextMenuModel.OrderBackPath, items[0].Submenu![1].Path);
        // Collider ▸ Add Box / Add Polygon (colliders-as-entities: a child collider entity) / Add
        // Vertex / Remove Collider (Danger) — WS folded the retired window-bar +Vtx / -Col text
        // buttons into the SAME submenu, so all four collider verbs live here.
        Assert.Equal(EditorMenuItemKind.Submenu, items[1].Kind);
        Assert.Equal(EditorContextMenuModel.AddColliderSubmenuPath, items[1].Path);
        Assert.Equal(4, items[1].Submenu!.Count);
        Assert.Equal(EditorContextMenuModel.AddColliderBoxPath, items[1].Submenu![0].Path);
        Assert.Equal(EditorContextMenuModel.AddColliderPolygonPath, items[1].Submenu![1].Path);
        Assert.Equal(EditorContextMenuModel.AddVertexPath, items[1].Submenu![2].Path);
        Assert.Equal(EditorContextMenuModel.RemoveColliderPath, items[1].Submenu![3].Path);
        Assert.True(items[1].Submenu![3].Danger); // Remove Collider is destructive
        Assert.Equal(EditorMenuItemKind.Separator, items[2].Kind);
        Assert.Equal(EditorContextMenuModel.CreatePrefabFromSelectionPath, items[3].Path);
        Assert.True(items[3].Enabled); // enabled with a selection
        Assert.Equal(EditorContextMenuModel.UnpackPrefabPath, items[4].Path);
        Assert.True(items[4].Danger);
        Assert.Equal(EditorMenuItemKind.Separator, items[5].Kind);
        Assert.Equal(EditorContextMenuModel.DeletePath, items[6].Path);
        Assert.True(items[6].Danger);
    }

    [Fact]
    public void EntityMenu_UnpackEnabledOnlyForAPrefabInstance()
    {
        Assert.False(EditorContextMenuModel.EntityMenu(hasSelection: true, isPrefabInstance: false)[4].Enabled);
        Assert.True(EditorContextMenuModel.EntityMenu(hasSelection: true, isPrefabInstance: true)[4].Enabled);
    }

    [Fact]
    public void PrefabCardMenu_HasEditAndDangerDelete_WithIdSuffixedPaths()
    {
        var items = EditorContextMenuModel.PrefabCardMenu("npc-boldo");
        Assert.Equal(EditorContextMenuModel.PrefabEditPathPrefix + "npc-boldo", items[0].Path);
        Assert.Equal(EditorContextMenuModel.PrefabDeletePathPrefix + "npc-boldo", items[2].Path);
        Assert.True(items[2].Danger);
    }

    [Fact]
    public void PrefabShelfMenu_IsCreateEmptyPrefab()
    {
        var items = EditorContextMenuModel.PrefabShelfMenu();
        Assert.Single(items);
        Assert.Equal(EditorContextMenuModel.CreateEmptyPrefabPath, items[0].Path);
    }

    [Fact]
    public void EntityMenu_SelectionGatedItemsDisabled_WhenNoSelection()
    {
        var items = EditorContextMenuModel.EntityMenu(hasSelection: false);
        Assert.False(items[0].Enabled);            // Order submenu
        Assert.False(items[0].Submenu![0].Enabled); // Bring Forward
        Assert.False(items[1].Enabled);            // Collider submenu
        Assert.False(items[1].Submenu![0].Enabled); // Collider ▸ Add Box
        Assert.False(items[3].Enabled);            // Create Prefab from Selection
        Assert.False(items[4].Enabled);            // Unpack (also needs an instance)
        Assert.False(items[6].Enabled);            // Delete
    }

    [Fact]
    public void EntityMenu_ForALayerSelection_LeadsWithTheLayerVerbs_KeepingTheColliderSubmenu()
    {
        var items = EditorContextMenuModel.EntityMenu(hasSelection: true, isLayer: true);

        // The layer verbs come FIRST (rename + reorder), separated from the generic entity items.
        Assert.Equal(EditorContextMenuModel.RenameLayerPath, items[0].Path);
        Assert.Equal(EditorContextMenuModel.LayerUpPath, items[1].Path);
        Assert.Equal(EditorContextMenuModel.LayerDownPath, items[2].Path);
        Assert.Equal(EditorMenuItemKind.Separator, items[3].Kind);
        // …then the unchanged entity items, collider submenu included.
        Assert.Equal(EditorContextMenuModel.OrderSubmenuPath, items[4].Path);
        Assert.Equal(EditorContextMenuModel.AddColliderSubmenuPath, items[5].Path);
        Assert.Equal(EditorContextMenuModel.AddColliderBoxPath, items[5].Submenu![0].Path);
        Assert.Equal(EditorContextMenuModel.DeletePath, items[^1].Path);

        // A non-layer selection is byte-identical to the pre-wave menu (no layer verbs).
        var plain = EditorContextMenuModel.EntityMenu(hasSelection: true);
        Assert.Equal(EditorContextMenuModel.OrderSubmenuPath, plain[0].Path);
        Assert.DoesNotContain(plain, i => i.Path == EditorContextMenuModel.RenameLayerPath);
    }

    [Fact]
    public void EntityMenu_EditAutotileRules_AppearsExactlyForAPaintLayerSelection()
    {
        // WS: the jump into the Autotile Rules workspace is a LAYER verb, and only an INDEXED (paint)
        // layer has rule sets to edit — offering it on a sprites layer would open an editor bound to
        // nothing.
        var paint = EditorContextMenuModel.EntityMenu(hasSelection: true, isLayer: true, isPaintLayer: true);
        Assert.Equal(EditorContextMenuModel.EditLayerRulesPath, paint[3].Path);
        Assert.Equal(EditorMenuItemKind.Separator, paint[4].Kind); // still below the layer-verb block
        Assert.True(paint[3].Enabled);

        // A non-paint layer, and a plain entity, never show it.
        Assert.DoesNotContain(EditorContextMenuModel.EntityMenu(hasSelection: true, isLayer: true),
            i => i.Path == EditorContextMenuModel.EditLayerRulesPath);
        Assert.DoesNotContain(EditorContextMenuModel.EntityMenu(hasSelection: true),
            i => i.Path == EditorContextMenuModel.EditLayerRulesPath);
        // The isPaintLayer flag alone (no layer) adds nothing — it only qualifies the layer block.
        Assert.DoesNotContain(EditorContextMenuModel.EntityMenu(hasSelection: true, isPaintLayer: true),
            i => i.Path == EditorContextMenuModel.EditLayerRulesPath);
    }

    [Fact]
    public void AddMenu_IsEmptyEntityThenTheLayerCreators()
    {
        // The Entities-panel toolbar's + Add dropdown (HP): what can be born into the scene — an
        // empty entity, or a layer by KIND (pixel-art wave: Entity Layer | Indexed Layer).
        var items = EditorContextMenuModel.AddMenu();
        Assert.Equal(4, items.Count);
        Assert.Equal(EditorContextMenuModel.AddEmptyPath, items[0].Path);
        Assert.Equal(EditorMenuItemKind.Separator, items[1].Kind);
        Assert.Equal(EditorContextMenuModel.NewSpritesLayerPath, items[2].Path);
        Assert.Equal(EditorContextMenuModel.NewPaintLayerPath, items[3].Path);
        Assert.All(items, i => Assert.True(i.Enabled)); // nothing here is selection-gated
    }

    [Fact]
    public void EntitiesPanelMenu_WithRow_IncludesEntityItemsAboveSeparator()
    {
        var items = EditorContextMenuModel.EntitiesPanelMenu(hasRowEntity: true);
        Assert.Equal(EditorMenuItemKind.Submenu, items[0].Kind);   // Order ▸
        Assert.Equal(EditorContextMenuModel.DeletePath, items[1].Path);
        Assert.Equal(EditorMenuItemKind.Separator, items[2].Kind);
        Assert.Equal(EditorContextMenuModel.AddEmptyPath, items[3].Path);
    }

    [Fact]
    public void EntitiesPanelMenu_NoRow_IsAddEmptyPlusTheLayerCreators()
    {
        // Layers wave: the Entities tree IS the layers panel, so its background menu carries the
        // layer creators below Add Empty Entity (separated) — both kinds (pixel-art wave).
        var items = EditorContextMenuModel.EntitiesPanelMenu(hasRowEntity: false);
        Assert.Equal(4, items.Count);
        Assert.Equal(EditorContextMenuModel.AddEmptyPath, items[0].Path);
        Assert.Equal(EditorMenuItemKind.Separator, items[1].Kind);
        Assert.Equal(EditorContextMenuModel.NewSpritesLayerPath, items[2].Path);
        Assert.Equal(EditorContextMenuModel.NewPaintLayerPath, items[3].Path);
    }

    [Fact]
    public void ScenesPanelMenu_IsCreateEmptyScene()
    {
        var items = EditorContextMenuModel.ScenesPanelMenu();
        Assert.Single(items);
        Assert.Equal(EditorContextMenuModel.CreateScenePath, items[0].Path);
    }

    [Fact]
    public void FindByPath_SearchesSubmenus()
    {
        var items = EditorContextMenuModel.EntityMenu(hasSelection: true);
        Assert.NotNull(EditorContextMenuModel.FindByPath(items, EditorContextMenuModel.OrderForwardPath));
        Assert.NotNull(EditorContextMenuModel.FindByPath(items, EditorContextMenuModel.DeletePath));
        Assert.Null(EditorContextMenuModel.FindByPath(items, "nonexistent"));
    }

    // ═══ Pure menu layout ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MenuHeight_CountsRowsAndShorterSeparators()
    {
        var items = EditorContextMenuModel.EntityMenu(hasSelection: true); // 5 item rows + 2 separators
        var h = EditorContextMenuLayout.MenuHeight(items, 1f);
        var expected = EditorContextMenuLayout.VerticalPadding * 2
                       + 5 * EditorContextMenuLayout.ItemHeight
                       + 2 * EditorContextMenuLayout.SeparatorHeight;
        Assert.Equal(expected, h);
    }

    [Fact]
    public void MenuRect_ClampsToTheWindow()
    {
        var items = EditorContextMenuModel.EntityMenu(hasSelection: true);
        // Anchor at the bottom-right corner → the box shifts fully inside the window.
        var rect = EditorContextMenuLayout.MenuRect(new Point(799, 599), items, 800, 600, 1f);
        Assert.True(rect.Right <= 800);
        Assert.True(rect.Bottom <= 600);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
    }

    [Fact]
    public void SubmenuRect_OpensRight_FlipsLeftWhenNoRoom()
    {
        var items = EditorContextMenuModel.EntityMenu(hasSelection: true);
        var sub = items[0].Submenu!;

        // Room on the right: submenu opens to the right of the parent menu.
        var menu = EditorContextMenuLayout.MenuRect(new Point(100, 100), items, 800, 600, 1f);
        var parentItem = EditorContextMenuLayout.ItemRect(menu, items, 0, 1f);
        var right = EditorContextMenuLayout.SubmenuRect(menu, parentItem, sub, 800, 600, 1f);
        Assert.True(right.X >= menu.Right);

        // No room on the right (menu pinned to the right edge): submenu flips to the left.
        var menu2 = EditorContextMenuLayout.MenuRect(new Point(799, 100), items, 800, 600, 1f);
        var parentItem2 = EditorContextMenuLayout.ItemRect(menu2, items, 0, 1f);
        var left = EditorContextMenuLayout.SubmenuRect(menu2, parentItem2, sub, 800, 600, 1f);
        Assert.True(left.Right <= menu2.Left + 1);
    }

    // ═══ Menu system: open / close / pick / modality ════════════════════════════════════════════════

    [Fact]
    public void OpenAt_SetsOpen_Close_Clears()
    {
        using var world = new World();
        MakeCursor(world);
        using var menu = new EditorContextMenuSystem(world, Vm(), null, (_, _) => { });

        Assert.False(menu.IsOpen);
        menu.OpenAt(EditorContextMenuModel.EntityMenu(true), new Point(100, 100));
        Assert.True(menu.IsOpen);
        Assert.Equal(7, menu.Items.Count); // Order / Collider / --- / Create Prefab / Unpack / --- / Delete

        menu.Close();
        Assert.False(menu.IsOpen);
        Assert.Empty(menu.Items);
    }

    [Fact]
    public void Pick_DispatchesTheLeafPath_AndCloses()
    {
        using var world = new World();
        MakeCursor(world);
        string? dispatched = null;
        using var menu = new EditorContextMenuSystem(world, Vm(), null, (p, _) => dispatched = p);

        menu.OpenAt(EditorContextMenuModel.EntityMenu(true), new Point(100, 100));
        menu.Pick(EditorContextMenuModel.OrderForwardPath, Edit()); // a SUBMENU leaf, found by path

        Assert.Equal(EditorContextMenuModel.OrderForwardPath, dispatched);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void Pick_DisabledItem_DoesNothing()
    {
        using var world = new World();
        MakeCursor(world);
        var dispatched = 0;
        using var menu = new EditorContextMenuSystem(world, Vm(), null, (_, _) => dispatched++);

        menu.OpenAt(EditorContextMenuModel.EntityMenu(hasSelection: false), new Point(100, 100)); // all disabled
        menu.Pick(EditorContextMenuModel.DeletePath, Edit());

        Assert.Equal(0, dispatched);
        Assert.True(menu.IsOpen); // disabled pick keeps the menu open
    }

    [Fact]
    public void Blocked_RefusesToOpen()
    {
        using var world = new World();
        MakeCursor(world);
        var blocked = true;
        using var menu = new EditorContextMenuSystem(world, Vm(), null, (_, _) => { }, isBlocked: () => blocked);

        menu.OpenAt(EditorContextMenuModel.ScenesPanelMenu(), new Point(100, 100));
        Assert.False(menu.IsOpen); // dialog open / drag owns the pointer → menus never open

        blocked = false;
        menu.OpenAt(EditorContextMenuModel.ScenesPanelMenu(), new Point(100, 100));
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void OpenMenu_ItemClick_Dispatches_AndConsumesTheCursor()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        string? dispatched = null;
        using var menu = new EditorContextMenuSystem(world, vm, null, (p, _) => dispatched = p,
            getKeyboardState: () => new KeyboardState());

        var items = EditorContextMenuModel.EntityMenu(true);
        menu.OpenAt(items, new Point(100, 100));
        var menuRect = EditorContextMenuLayout.MenuRect(new Point(100, 100), items, 800, 600, 1f);
        var deleteRect = EditorContextMenuLayout.ItemRect(menuRect, items, 6, 1f); // Delete (index 6 with Collider)
        SetCursorScreen(cursor, Center(deleteRect), leftReleased: true);

        menu.Update(Edit());

        Assert.Equal(EditorContextMenuModel.DeletePath, dispatched);
        Assert.False(menu.IsOpen);
        // Consumed: the release edge is cleared so no downstream system acts on the same click.
        ref readonly var input = ref cursor.Get<CursorInputComponent>();
        Assert.False(input.LeftButtonReleased);
    }

    [Fact]
    public void ClickAway_ClosesWithoutDispatch()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var dispatched = 0;
        using var menu = new EditorContextMenuSystem(world, Vm(), null, (_, _) => dispatched++,
            getKeyboardState: () => new KeyboardState());

        menu.OpenAt(EditorContextMenuModel.EntityMenu(true), new Point(100, 100));
        SetCursorScreen(cursor, new Point(700, 500), leftReleased: true); // far outside the menu box
        menu.Update(Edit());

        Assert.Equal(0, dispatched);
        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void Escape_ClosesTheMenu()
    {
        using var world = new World();
        MakeCursor(world);
        var escape = false;
        using var menu = new EditorContextMenuSystem(world, Vm(), null, (_, _) => { },
            getKeyboardState: () => escape ? new KeyboardState(Keys.Escape) : new KeyboardState());

        menu.OpenAt(EditorContextMenuModel.EntityMenu(true), new Point(100, 100));
        escape = true;
        menu.Update(Edit());

        Assert.False(menu.IsOpen);
    }

    [Fact]
    public void HoverSubmenuParent_OpensSubmenu_ThenItemClickDispatches()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        string? dispatched = null;
        using var menu = new EditorContextMenuSystem(world, vm, null, (p, _) => dispatched = p,
            getKeyboardState: () => new KeyboardState());

        var items = EditorContextMenuModel.EntityMenu(true);
        menu.OpenAt(items, new Point(100, 100));
        var menuRect = EditorContextMenuLayout.MenuRect(new Point(100, 100), items, 800, 600, 1f);
        var orderRect = EditorContextMenuLayout.ItemRect(menuRect, items, 0, 1f); // Order ▸

        // Frame 1: hover the submenu parent → the submenu opens.
        SetCursorScreen(cursor, Center(orderRect));
        menu.Update(Edit());
        Assert.Equal(0, menu.OpenSubmenuIndex);

        // Frame 2: click "Send Backward" (submenu index 1).
        var sub = items[0].Submenu!;
        var parentItem = EditorContextMenuLayout.ItemRect(menuRect, items, 0, 1f);
        var subRect = EditorContextMenuLayout.SubmenuRect(menuRect, parentItem, sub, 800, 600, 1f);
        var backRect = EditorContextMenuLayout.ItemRect(subRect, sub, 1, 1f);
        SetCursorScreen(cursor, Center(backRect), leftReleased: true);
        menu.Update(Edit());

        Assert.Equal(EditorContextMenuModel.OrderBackPath, dispatched);
        Assert.False(menu.IsOpen);
    }

    // ═══ UX3-D: Overlays dropdown — checkable Toggle items ═══════════════════════════════════════════

    [Fact]
    public void OverlaysMenu_HasGridToggle_SpacingSubmenu_OutlineToggle_CameraToggle()
    {
        var items = EditorContextMenuModel.OverlaysMenu(
            showGrid: true, gridSpacing: 16f, outlineSelected: false, showCameraGlyph: true);

        Assert.Equal(4, items.Count);
        Assert.Equal(EditorMenuItemKind.Toggle, items[0].Kind);
        Assert.Equal(ViewportOverlayOps.GridTogglePath, items[0].Path);
        Assert.True(items[0].Checked); // grid on → checked

        Assert.Equal(EditorMenuItemKind.Submenu, items[1].Kind);
        Assert.Equal(4, items[1].Submenu!.Count); // 8 / 16 / 32 / 64
        Assert.False(items[1].Submenu![0].Checked); // 8
        Assert.True(items[1].Submenu![1].Checked);  // 16 = the current spacing
        Assert.False(items[1].Submenu![2].Checked); // 32
        Assert.Equal(EditorMenuItemKind.Action, items[1].Submenu![1].Kind); // presets are Actions (close on click)

        Assert.Equal(EditorMenuItemKind.Toggle, items[2].Kind);
        Assert.False(items[2].Checked); // outline off
        Assert.Equal(EditorMenuItemKind.Toggle, items[3].Kind);
        Assert.True(items[3].Checked);  // camera on
    }

    [Fact]
    public void ToggleItem_Click_DispatchesWithoutClosing_AndRefreshesTheCheckInPlace()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var settings = ViewportOverlaySettingsComponent.Default; // grid off
        var gizmo = GizmoStateComponent.Default;
        IReadOnlyList<EditorMenuItem> Build() => EditorContextMenuModel.OverlaysMenu(
            settings.ShowGrid, gizmo.GridStep, settings.OutlineSelected, settings.ShowCameraGlyph);

        using var menu = new EditorContextMenuSystem(world, Vm(), null,
            (path, _) => ViewportOverlayOps.TryApplyMenuPath(path, ref settings, ref gizmo),
            getKeyboardState: () => new KeyboardState());

        var items0 = Build();
        menu.OpenAt(items0, new Point(100, 100), rebuild: Build);
        var menuRect = EditorContextMenuLayout.MenuRect(new Point(100, 100), items0, 800, 600, 1f);
        var gridRow = EditorContextMenuLayout.ItemRect(menuRect, items0, 0, 1f); // the Grid toggle
        SetCursorScreen(cursor, Center(gridRow), leftReleased: true);

        menu.Update(Edit());

        Assert.True(settings.ShowGrid);      // the click dispatched (flipped the setting)
        Assert.True(menu.IsOpen);            // a Toggle does NOT close the menu (Blender behavior)
        Assert.True(menu.Items[0].Checked);  // the check refreshed in place
    }

    [Fact]
    public void Pick_SpacingPresetAction_SetsTheSharedStep_AndCloses()
    {
        using var world = new World();
        MakeCursor(world);
        var settings = ViewportOverlaySettingsComponent.Default;
        var gizmo = GizmoStateComponent.Default; // GridStep 16
        IReadOnlyList<EditorMenuItem> Build() => EditorContextMenuModel.OverlaysMenu(
            settings.ShowGrid, gizmo.GridStep, settings.OutlineSelected, settings.ShowCameraGlyph);

        using var menu = new EditorContextMenuSystem(world, Vm(), null,
            (path, _) => ViewportOverlayOps.TryApplyMenuPath(path, ref settings, ref gizmo));

        menu.OpenAt(Build(), new Point(100, 100), rebuild: Build);
        menu.Pick(ViewportOverlayOps.SpacingPath(32f), Edit());

        Assert.Equal(32f, gizmo.GridStep); // wrote the ONE shared grid step
        Assert.False(menu.IsOpen);         // an Action item closes the menu
    }

    [Fact]
    public void CheckRect_SitsInTheLeftGutter_BeforeTheLabel()
    {
        var row = new Rectangle(10, 20, EditorContextMenuLayout.MenuWidth, EditorContextMenuLayout.ItemHeight);
        var check = EditorContextMenuLayout.CheckRect(row, 1f);
        Assert.True(check.Left >= row.Left);
        // The check stays inside the label's left inset, so labels align across checkable + plain rows.
        Assert.True(check.Right <= row.Left + EditorContextMenuLayout.TextInsetX);
        Assert.True(check.Height <= row.Height);
    }

    // ═══ Menu → editor-command wiring (order fires; delete snapshots + undo) ═════════════════════════

    private static (EditorCommandSystem commands, EditorHistory history, SceneSerializer serializer) NewCommands(
        World world, DrawLayerMap? layers = null)
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);
        var history = new EditorHistory(world);
        var commands = new EditorCommandSystem(world, history, serializer, layers);
        return (commands, history, serializer);
    }

    private static Entity SelectedSprite(World world, float layerDepth)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(new Vector2(10, 10)));
        e.Set(new SpriteInfoComponent { Size = new Vector2(32, 32), Target = RenderTargetID.Main, LayerDepth = layerDepth });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
        e.Set(new SceneObjectComponent());
        e.Set(new SelectedComponent());
        return e;
    }

    /// <summary>Wires a menu whose dispatch maps the action-id paths to a real
    /// <see cref="EditorCommandSystem"/> (the shape the overlay's DispatchMenuAction uses), so a
    /// <c>menu:pick</c> exercises the command end-to-end. WS: the four Collider ▸ verbs route here
    /// too — the entity menu is their ONLY surface now that the window bar's +Box/+Poly/-Col/+Vtx text
    /// buttons are retired, so the wiring is what keeps them reachable at all.</summary>
    private static EditorContextMenuSystem MenuOver(World world, EditorCommandSystem commands,
        Action<GameState>? editLayerRules = null) =>
        new(world, Vm(), null, (path, s) =>
        {
            switch (path)
            {
                case EditorContextMenuModel.OrderForwardPath: commands.BringForward(s); break;
                case EditorContextMenuModel.OrderBackPath: commands.SendBack(s); break;
                case EditorContextMenuModel.DeletePath: commands.DeleteSelection(s); break;
                case EditorContextMenuModel.AddColliderBoxPath: commands.AddBoxCollider(s); break;
                case EditorContextMenuModel.AddColliderPolygonPath: commands.AddConvexCollider(s); break;
                case EditorContextMenuModel.AddVertexPath: commands.AddVertex(s); break;
                case EditorContextMenuModel.RemoveColliderPath: commands.RemoveCollider(s); break;
                case EditorContextMenuModel.EditLayerRulesPath: editLayerRules?.Invoke(s); break;
            }
        });

    [Fact]
    public void MenuPick_OrderForward_NudgesTheSelectedSpritesSourceDepth()
    {
        using var world = new World();
        var layers = DrawLayerMap.FromEnum<Band>();
        var bandDepth = layers.GetDepth(Band.Ground);
        var (commands, _, _) = NewCommands(world, layers);
        var sprite = SelectedSprite(world, bandDepth);
        using var menu = MenuOver(world, commands);

        menu.OpenAt(EditorContextMenuModel.EntityMenu(true), new Point(100, 100));
        menu.Pick(EditorContextMenuModel.OrderForwardPath, Edit());

        Assert.Equal(bandDepth + EditorCommandSystem.OrderStep, sprite.Get<SpriteInfoComponent>().LayerDepth, 6);
    }

    [Fact]
    public void MenuPick_Delete_IsTheSnapshottingCommand_UndoRestores()
    {
        using var world = new World();
        var (commands, history, _) = NewCommands(world);
        var sprite = SelectedSprite(world, 0.5f);
        using var menu = MenuOver(world, commands);

        menu.OpenAt(EditorContextMenuModel.EntityMenu(true), new Point(100, 100));
        menu.Pick(EditorContextMenuModel.DeletePath, Edit());

        Assert.False(sprite.IsAlive);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(1, CountSprites(world)); // the sub-graph is reconstructed from the snapshot
    }

    // ═══ WS: the Collider ▸ submenu is the ONLY surface for the four collider verbs ═════════════════

    private static Entity SelectedProp(World world)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(new Vector2(50, 50)));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 32, 48),
            Size = new Vector2(32, 48),
            Origin = new Vector2(16, 48),
            Target = RenderTargetID.Main,
        });
        e.Set(new SceneObjectComponent());
        e.Set(new SelectedComponent());
        return e;
    }

    private static Entity SingleWith<T>(World world)
    {
        Entity found = default;
        using var set = world.GetEntities().With<T>().AsSet();
        foreach (var e in set.GetEntities()) found = e;
        return found;
    }

    [Fact]
    public void MenuPick_ColliderAddBoxAndAddPolygon_CreateChildColliderEntities_Undoable()
    {
        using var world = new World();
        var (commands, history, _) = NewCommands(world);
        var prop = SelectedProp(world);
        using var menu = MenuOver(world, commands);
        var items = EditorContextMenuModel.EntityMenu(hasSelection: true);

        menu.OpenAt(items, new Point(100, 100));
        menu.Pick(EditorContextMenuModel.AddColliderBoxPath, Edit()); // a SUBMENU leaf, found by path

        var box = SingleWith<BoxColliderComponent>(world);
        Assert.True(box.IsAlive);
        Assert.Equal(prop, box.Get<ChildOfComponent>().Parent); // a CHILD collider entity, not a component
        Assert.Equal(new Vector2(32, 12), box.Get<BoxColliderComponent>().Size); // the sprite footprint
        Assert.False(prop.Has<BoxColliderComponent>());
        Assert.Equal(1, history.Count);
        Assert.False(menu.IsOpen); // an Action leaf closes the menu

        // Add Polygon acts on the NEW selection (the box child) — the same command the retired +Poly fired.
        menu.OpenAt(items, new Point(100, 100));
        menu.Pick(EditorContextMenuModel.AddColliderPolygonPath, Edit());
        var poly = SingleWith<ConvexColliderComponent>(world);
        Assert.True(poly.IsAlive);
        Assert.Equal(2, history.Count);

        history.Undo();
        history.Undo();
        using var boxes = world.GetEntities().With<BoxColliderComponent>().AsSet();
        using var polys = world.GetEntities().With<ConvexColliderComponent>().AsSet();
        Assert.Equal(0, boxes.Count);
        Assert.Equal(0, polys.Count);
    }

    [Fact]
    public void MenuPick_ColliderAddVertex_SplitsTheLongestEdgeOfTheSelectedConvexCollider()
    {
        using var world = new World();
        var (commands, history, _) = NewCommands(world);

        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(Vector2.Zero));
        collider.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(30, 0), new Vector2(15, 10),
        }));
        collider.Set(new SelectedComponent());
        using var menu = MenuOver(world, commands);

        menu.OpenAt(EditorContextMenuModel.EntityMenu(hasSelection: true), new Point(100, 100));
        menu.Pick(EditorContextMenuModel.AddVertexPath, Edit());

        Assert.Equal(new[]
        {
            new Vector2(0, 0), new Vector2(15, 0), new Vector2(30, 0), new Vector2(15, 10),
        }, collider.Get<ConvexColliderComponent>().ModelVertices);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(3, collider.Get<ConvexColliderComponent>().ModelVertices.Length);
    }

    [Fact]
    public void MenuPick_ColliderRemove_DeletesTheSelectedColliderEntity_Undoable()
    {
        using var world = new World();
        var (commands, history, _) = NewCommands(world);

        var collider = world.CreateEntity();
        collider.Set(new EntityInfoComponent("BoxCollider"));
        collider.Set(new TransformComponent(new Vector2(7, 9)));
        collider.Set(new BoxColliderComponent(new Vector2(3, 4)));
        collider.Set(new SelectedComponent());
        using var menu = MenuOver(world, commands);

        menu.OpenAt(EditorContextMenuModel.EntityMenu(hasSelection: true), new Point(100, 100));
        menu.Pick(EditorContextMenuModel.RemoveColliderPath, Edit());

        Assert.False(collider.IsAlive);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(new Vector2(3, 4), SingleWith<BoxColliderComponent>(world).Get<BoxColliderComponent>().Size);
    }

    [Fact]
    public void MenuPick_EditAutotileRules_OpensTheWorkspaceBoundToThatPaintLayer()
    {
        using var world = new World();
        var (commands, _, _) = NewCommands(world);
        var shell = new EditorShellStateComponent();
        var history = new EditorHistory(world);

        var data = new TileGridComponent { CellSize = 32f };
        data.Values.Add(new TilePaintValue { Id = 1, Name = "Rock", TilesetKey = "file:a.png", TileSize = 32 });
        data.Values.Add(new TilePaintValue { Id = 2, Name = "Sand" });
        var layer = world.CreateEntity();
        layer.Set(new TransformComponent(Vector2.Zero));
        layer.Set(new EntityInfoComponent("Layer", "Terrain"));
        layer.Set(new SceneLayerComponent { Order = 0 });
        layer.Set(data);
        layer.Set(new SelectedComponent());

        // The texture seam is never touched here (no Update, so nothing lays out).
        var textures = new FileAssetTextureLoader(
            openStream: _ => null, decode: _ => null, createPlaceholder: () => null);
        using var rules = new AutotileRuleEditorSystem(world, Vm(), textures, font: null, shell, history);
        using var menu = MenuOver(world, commands, editLayerRules: _ => rules.Open(layer));

        Assert.False(rules.IsOpen);
        Assert.Equal(EditorWorkspace.LevelEditor, shell.ActiveWorkspace);

        var items = EditorContextMenuModel.EntityMenu(hasSelection: true, isLayer: true, isPaintLayer: true);
        menu.OpenAt(items, new Point(100, 100));
        menu.Pick(EditorContextMenuModel.EditLayerRulesPath, Edit());

        Assert.Equal(EditorWorkspace.AutotileRules, shell.ActiveWorkspace);
        Assert.True(rules.IsOpen);
        Assert.Equal(layer, rules.CurrentLayer);
        Assert.Equal((byte)1, rules.CurrentValueId); // the first rule set with a bound sheet
        Assert.False(menu.IsOpen);
    }

    // ═══ Add Empty Entity (undoable, tagged root) ═══════════════════════════════════════════════════

    [Fact]
    public void AddEmptyEntity_CreatesTaggedRootAtViewCentre_Undoable()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(320, 240) { Position = new Vector2(128, 64) };
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var history = new EditorHistory(world);
        var commands = new EditorCommandSystem(world, history, new SceneSerializer(registry),
            camera: camera);

        commands.AddEmptyEntity(Edit());

        var created = SingleTaggedRoot(world);
        Assert.True(created.Has<TransformComponent>());
        Assert.Equal(new Vector2(128, 64), created.Get<TransformComponent>().Position);
        Assert.Equal("Empty", created.Get<EntityInfoComponent>().Type);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(0, CountTaggedRoots(world)); // undo of a create = delete
    }

    // ═══ Create Empty Scene (dialog: collision refusal + accept) ════════════════════════════════════

    private static EditorDialogSystem NewCreateSceneDialog(World world,
        Func<string, bool> nameExists, Action<string, GameState> create)
    {
        MakeCursor(world);
        return new EditorDialogSystem(world, Vm(), null,
            onSaveScene: _ => { }, onSaveProject: _ => { }, onSaveBackup: (_, _) => { },
            onSceneNameExists: nameExists, onCreateScene: create);
    }

    [Fact]
    public void CreateScene_RefusesExistingName_KeepsDialogOpen_AndDoesNotCreate()
    {
        using var world = new World();
        string? created = null;
        var dialog = NewCreateSceneDialog(world, id => id == "taken", (id, _) => created = id);

        dialog.OpenCreateScene();
        dialog.SetName("taken");
        dialog.ConfirmCreateScene(Edit());

        Assert.Equal(EditorDialogMode.CreateScene, dialog.Mode); // stayed open (loud refusal)
        Assert.Null(created);
    }

    [Fact]
    public void CreateScene_AcceptsNewName_ClosesAndInvokesWithSanitizedId()
    {
        using var world = new World();
        string? created = null;
        var dialog = NewCreateSceneDialog(world, _ => false, (id, _) => created = id);

        dialog.OpenCreateScene();
        dialog.SetName("My Level!!"); // sanitizes to "MyLevel"
        dialog.ConfirmCreateScene(Edit());

        Assert.Equal(EditorDialogMode.None, dialog.Mode);
        Assert.Equal("MyLevel", created);
    }

    [Fact]
    public void CreateScene_EmptyNameAfterSanitize_KeepsOpen_AndDoesNotCreate()
    {
        using var world = new World();
        string? created = null;
        var dialog = NewCreateSceneDialog(world, _ => false, (id, _) => created = id);

        dialog.OpenCreateScene();
        dialog.SetName("***"); // sanitizes to empty
        dialog.ConfirmCreateScene(Edit());

        Assert.Equal(EditorDialogMode.CreateScene, dialog.Mode);
        Assert.Null(created);
    }

    // ═══ Create Empty Scene: the canonical empty-world write (what CreateEmptyScene serializes) ══════

    [Fact]
    public void EmptyScene_WritesCanonicalBytes_EmptyEntities_OnFakeFileSystem()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World(); // no SceneObjectComponent roots ⇒ empty entities[]
            var camera = new MonoDreams.Component.Camera(320, 240) { Position = new Vector2(10, 20) };
            var layers = DrawLayerMap.FromEnum<Band>();
            var registry = new ComponentSerializerRegistry();
            registry.RegisterEngineComponents();
            var writer = new SceneWriter(new SceneSerializer(registry));

            const string target = "/proj/Content/Levels/fresh.mdscene";
            var scene = writer.BuildScene(world, layers);
            Assert.Empty(scene.Entities);
            var saved = writer.Save(scene, target);

            Assert.Equal(target, saved);
            Assert.True(fake.Files.ContainsKey(target));
            // Canonical + byte-stable: re-serializing the same empty scene is byte-identical.
            Assert.Equal(fake.Files[target], CanonicalJson.Serialize(scene));
            Assert.Contains("\"entities\": []", fake.Files[target]);
        });
    }

    // ---- helpers ----

    private enum Band { Ground, Detail, Props }

    private static int CountSprites(World world)
    {
        using var set = world.GetEntities().With<SpriteInfoComponent>().AsSet();
        var n = 0;
        foreach (var _ in set.GetEntities()) n++;
        return n;
    }

    private static int CountTaggedRoots(World world)
    {
        using var set = world.GetEntities().With<SceneObjectComponent>().AsSet();
        var n = 0;
        foreach (var _ in set.GetEntities()) n++;
        return n;
    }

    private static Entity SingleTaggedRoot(World world)
    {
        using var set = world.GetEntities().With<SceneObjectComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        throw new InvalidOperationException("no tagged root");
    }

    private static void WithPlatform(InMemoryPlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    // ═══ PF-A: the filterable "+ Add component" popup ═══════════════════════════════════════════════

    [Fact]
    public void FilterablePopup_NarrowsItemsLive_CaseInsensitive_AndPicksByPath()
    {
        using var world = new World();
        MakeCursor(world);
        string? dispatched = null;
        using var menu = new EditorContextMenuSystem(world, Vm(), null, (p, _) => dispatched = p);

        var items = new[]
        {
            new EditorMenuItem { Kind = EditorMenuItemKind.Action, Label = "Alpha", Path = "add-component:a" },
            new EditorMenuItem { Kind = EditorMenuItemKind.Action, Label = "Beta", Path = "add-component:b" },
            new EditorMenuItem { Kind = EditorMenuItemKind.Action, Label = "Alfredo", Path = "add-component:c" },
        };
        menu.OpenFiltered(items, new Point(50, 50));
        Assert.True(menu.IsOpen);
        Assert.Equal(3, menu.Items.Count);

        menu.SetFilter("AL"); // narrows live (case-insensitive substring on the label)
        Assert.Equal(2, menu.Items.Count); // Alpha + Alfredo
        Assert.All(menu.Items, i => Assert.Contains("al", i.Label, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("AL", menu.FilterValue);

        menu.Pick("add-component:a", Edit()); // a pick from the narrowed set dispatches + closes
        Assert.Equal("add-component:a", dispatched);
        Assert.False(menu.IsOpen);
    }

    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/scene/";
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) { Files[suggestedFileName] = contents; return suggestedFileName; }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }
}
