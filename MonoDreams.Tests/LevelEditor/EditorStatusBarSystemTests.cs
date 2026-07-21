using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX3-F <see cref="EditorStatusBarSystem"/> (design §5): the right side shows the scene id
/// + view mode with a Warning dirty-dot MESH gated on the dirty state; the left side shows the modal
/// readout while a transform is active, else the contextual selection + entity count. Font-null
/// (layout-only) so it runs headless — the label <c>TextContent</c> is set regardless of the font.
/// </summary>
public class EditorStatusBarSystemTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static ViewportManager Vm() =>
        new(null!, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900, DevicePixelRatio = 1f };

    private static bool HasLabel(World world, string substring)
    {
        using var set = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<DynamicTextComponent>().TextContent?.Contains(substring) == true)
                return true;
        return false;
    }

    private static bool HasDirtyDot(World world)
    {
        using var set = world.GetEntities().With<DrawComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            ref readonly var dc = ref e.Get<DrawComponent>();
            if (dc.Type == DrawElementType.Mesh && dc.Vertices is { Length: > 0 }) return true;
        }
        return false;
    }

    [Fact]
    public void Right_ShowsSceneAndMode_AndTheDirtyDotOnlyWhenDirty()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => new());
        var dirty = new[] { false };
        var sys = new EditorStatusBarSystem(world, Vm(), font: null, modal,
            sceneId: () => "island2", isDirty: () => dirty[0], activeKind: () => ViewportContextKind.Scene);

        sys.Update(Edit());
        Assert.True(HasLabel(world, "island2")); // PF-B: the Scene tab shows just the id (no run-state word)
        Assert.False(HasDirtyDot(world)); // clean → no dot

        dirty[0] = true;
        sys.Update(Edit());
        Assert.True(HasDirtyDot(world)); // dirty → the Warning dot mesh appears
    }

    [Fact]
    public void Right_ReflectsGameTab_ShowsRunState()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => new());
        var sys = new EditorStatusBarSystem(world, Vm(), font: null, modal,
            sceneId: () => "island2", isDirty: () => false, activeKind: () => ViewportContextKind.Game);

        // PF-B: the Game tab shows the id + the transport state (Edit → Paused).
        sys.Update(Edit());
        Assert.True(HasLabel(world, "island2  |  Paused"));
    }

    [Fact]
    public void Left_ShowsContextualStatus_WhenNoModal_ThenTheModalReadout_WhileActive()
    {
        using var world = new World();
        var history = new EditorHistory(world);

        var target = world.CreateEntity();
        target.Set(new TransformComponent(new Vector2(10, 20)));
        target.Set(new EntityInfoComponent("Prop", "Tree"));
        target.Set(new SelectedComponent());
        var cursor = world.CreateEntity();
        cursor.Set(new CursorInputComponent { WorldPosition = new Vector2(50, 50) });

        var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => new());
        var sys = new EditorStatusBarSystem(world, Vm(), font: null, modal,
            sceneId: () => "island2", isDirty: () => false, activeKind: () => ViewportContextKind.Scene);

        // No modal → contextual: the selection name + the entity count (Tree is the one non-infra entity).
        sys.Update(Edit());
        Assert.True(HasLabel(world, "Tree  |  1 entity"));

        // Enter grab + move 12 along X → the left side flips to the live modal readout.
        modal.Enter(EditorModalMode.Grab, Edit());
        modal.OpCursor(12f, 0f);
        sys.Update(Edit());
        Assert.True(HasLabel(world, "Move  dX 12.0"));
        Assert.True(HasLabel(world, StatusBarModel.ConfirmHint));
    }

    [Fact]
    public void Left_NoSelection_ShowsNoSelection()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => new());
        var sys = new EditorStatusBarSystem(world, Vm(), font: null, modal,
            sceneId: () => "s", isDirty: () => false, activeKind: () => ViewportContextKind.Scene);

        sys.Update(Edit());
        Assert.True(HasLabel(world, "No selection"));
    }
}
