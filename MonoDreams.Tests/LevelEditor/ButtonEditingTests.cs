using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component.UI;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.Screens;
using MonoDreams.Examples.System.UI;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the TB-B menu-button hierarchy rework: a menu button is ONE root entity (transform +
/// SimpleButtonComponent + behavior) with the label as a <c>ChildOf</c> child, so selecting / moving /
/// G / S operate on the root and the label follows through the ordinary hierarchy. These exercise the
/// REAL systems in the REAL pipeline order (gizmo / modal in the update pipeline, then
/// <see cref="HierarchySystem"/>), including the modal-order reader that used to drop the child (the
/// gizmo-vs-G divergence — now fixed at the foundation level, see
/// <see cref="HierarchyDirtyPropagationTests"/>), the layout-vs-manual-placement decision (a moved
/// button sticks because it is slot CONTENT, not a slot), and that Play-mode interaction still recolors
/// the label child and dispatches.
/// </summary>
public class ButtonEditingTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    /// <summary>The TB-B button shape: a selected root (transform + SimpleButtonComponent) with a
    /// DynamicText label as a ChildOf child at <paramref name="labelLocal"/>.</summary>
    private static (Entity root, Entity label) MakeButton(
        World world, Vector2 rootPos, Vector2 labelLocal, Vector2 size, bool selected = true)
    {
        var root = world.CreateEntity();
        root.Set(new TransformComponent(rootPos));

        var label = world.CreateEntity();
        label.Set(new TransformComponent(labelLocal));
        label.SetParent(root);
        label.Set(new DynamicTextComponent { Target = RenderTargetID.Main, TextContent = "x" });
        label.Set<VisibleComponent>();

        root.Set(new SimpleButtonComponent
        {
            Size = size, Target = RenderTargetID.Main, Color = Color.White,
            LineThickness = 2f, TextEntity = label,
        });
        root.Set<VisibleComponent>();
        if (selected) root.Set(new SelectedComponent());
        return (root, label);
    }

    private static Entity MakeCursor(World world, Vector2 world0)
    {
        var c = world.CreateEntity();
        c.Set(new CursorControllerComponent(CursorType.Default));
        c.Set(new CursorInputComponent { WorldPosition = world0, VirtualPosition = world0 });
        return c;
    }

    // ── Item 3: the moved button's label child follows under the GIZMO, modal G, and modal S ──

    [Fact]
    public void GizmoMove_OnButtonRoot_LabelChildFollows()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        var (root, label) = MakeButton(world, new Vector2(100, 100), new Vector2(18, 18), new Vector2(80, 30));
        var gs = GizmoStateComponent.Default; gs.Tool = GizmoTool.Move; world.CreateEntity().Set(gs);
        var cursor = MakeCursor(world, new Vector2(100, 100)); // press on the move handle (the pivot)
        cursor.Get<CursorInputComponent>().LeftButton = cursor.Get<CursorInputComponent>().LeftButtonPressed = true;

        using var gizmo = new GizmoSystem(world, camera, history);
        using var hierarchy = new HierarchySystem(world);
        hierarchy.Update(Edit());
        _ = label.Get<TransformComponent>().WorldPosition; // prime the child cache

        gizmo.Update(Edit()); // grab the handle

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = input.VirtualPosition = new Vector2(150, 120); // drag +50/+20
        gizmo.Update(Edit());     // apply the move
        hierarchy.Update(Edit()); // propagate to the label child

        Assert.Equal(new Vector2(150, 120), root.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(168, 138), label.Get<TransformComponent>().WorldPosition); // followed
    }

    /// <summary>The parity test in the real pipeline order: modal G edits the root EARLY, then a
    /// system reads the root's WorldPosition (as ButtonMeshPrepSystem does for the outline mesh) BEFORE
    /// HierarchySystem — the exact sequence that used to move the mesh but freeze the label. The label
    /// child must follow.</summary>
    [Fact]
    public void ModalGrab_OnButtonRoot_LabelChildFollows_EvenWithMeshPrepReadBeforeHierarchy()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (root, label) = MakeButton(world, new Vector2(10, 20), new Vector2(18, 18), new Vector2(80, 30));
        var cursor = MakeCursor(world, new Vector2(50, 50));
        var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => new());

        using var hierarchy = new HierarchySystem(world);
        hierarchy.Update(Edit());
        _ = label.Get<TransformComponent>().WorldPosition; // prime

        modal.Enter(EditorModalMode.Grab, Edit()); // entry cursor (50,50)
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = input.VirtualPosition = new Vector2(62, 46.5f); // delta (12, -3.5)
        cursor.NotifyChanged<CursorInputComponent>();
        modal.Update(Edit()); // live edit → root.Position = (22, 16.5)

        // The modal-order reader: ButtonMeshPrepSystem reads the root's WorldPosition (clears IsDirty).
        _ = root.Get<TransformComponent>().WorldPosition;
        hierarchy.Update(Edit()); // now propagate to the label child

        Assert.Equal(new Vector2(22f, 16.5f), root.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(40f, 34.5f), label.Get<TransformComponent>().WorldPosition); // 22+18, 16.5+18
    }

    [Fact]
    public void ModalScale_OnButtonRoot_LabelChildFollows()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (root, label) = MakeButton(world, Vector2.Zero, new Vector2(10, 0), new Vector2(80, 30));
        var cursor = MakeCursor(world, new Vector2(10, 0)); // 10 units from the pivot (origin)
        var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => new());

        using var hierarchy = new HierarchySystem(world);
        hierarchy.Update(Edit());
        _ = label.Get<TransformComponent>().WorldPosition; // prime — baseline world (10, 0)

        modal.Enter(EditorModalMode.Scale, Edit());
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = input.VirtualPosition = new Vector2(20, 0); // 20 out → factor 2
        cursor.NotifyChanged<CursorInputComponent>();
        modal.Update(Edit());

        _ = root.Get<TransformComponent>().WorldPosition; // intervening read
        hierarchy.Update(Edit());

        Assert.Equal(new Vector2(2f, 2f), root.Get<TransformComponent>().Scale);
        // The label rode the root's scale about the shared pivot: local (10,0) → world (20,0).
        Assert.Equal(new Vector2(20f, 0f), label.Get<TransformComponent>().WorldPosition);
    }

    // ── Item 4: the layout-vs-manual-placement decision — a moved button STICKS (it is slot CONTENT) ──

    /// <summary>A menu button is attached to an AutoLayout slot as CONTENT (its root becomes a ChildOf
    /// child of the slot the builder creates). AutoLayoutSystem writes only SLOT transforms, so a manual
    /// move of the button root edits its LOCAL offset under the slot and STICKS across a layout re-run —
    /// the chosen behavior (b): the layout owns the slot anchor, the manual offset composes on top.</summary>
    [Fact]
    public void ManualMove_OnLayoutSlotContent_Sticks_AcrossAutoLayoutRerun()
    {
        using var world = new World();
        var vm = new ViewportManager(null, 800, 600);
        var (root, _) = MakeButton(world, Vector2.Zero, new Vector2(18, 18), new Vector2(100, 40), selected: false);

        new AutoLayoutBuilder(world, vm)
            .CreateRoot(ScreenAnchor.Center)
            .AddSlot(slot => slot.Attach(root).MeasureWith(_ => new Vector2(100, 40)))
            .Build();

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);

        void LayoutFrame()
        {
            intrinsic.Update(Edit());
            layout.Update(Edit());
            hierarchy.Update(Edit());
        }

        LayoutFrame();
        var laidOut = root.Get<TransformComponent>().WorldPosition;

        // Manual move, as a gizmo/modal edit does — the button root is slot content, not a slot.
        root.Get<TransformComponent>().Position += new Vector2(50, 30);

        LayoutFrame(); // the solver re-runs — it must NOT snap the button back.

        Assert.Equal(laidOut + new Vector2(50, 30), root.Get<TransformComponent>().WorldPosition);
    }

    // ── Item 5: Play-mode interaction is unbroken with the root+child shape ──

    /// <summary>The new root+child button still hovers, recolors its LABEL CHILD (via
    /// SimpleButtonComponent.TextEntity → the child), and dispatches on click — now through the
    /// menu's real interaction pair (UIFocusSystem's pick + ButtonInteractionSystem's action, issue
    /// #115). The pickable surface, like the behaviour, is all on the ROOT, so the root+child shape
    /// needed no interaction-system change.</summary>
    [Fact]
    public void NewShapeButton_HoverRecolorsLabelChild_AndClickDispatches_InPlay()
    {
        using var world = new World();
        var (root, label) = MakeButton(world, Vector2.Zero, new Vector2(18, 18), new Vector2(100, 40), selected: false);
        root.Set(new FocusableComponent { Size = new Vector2(100, 40), Target = RenderTargetID.Main });
        root.Set(new LevelSelector
        {
            LevelName = "Level_0", TargetScreen = null, IsClickable = true, IsHovered = false,
            DefaultColor = Color.Black, HoveredColor = Color.OrangeRed, DisabledColor = Color.Gray,
        });

        var cursor = MakeCursor(world, new Vector2(10, 10)); // inside the 100×40 quad at origin
        cursor.Get<CursorInputComponent>().Delta = new Vector2(1, 1);  // the pointer moved onto it
        cursor.Get<CursorInputComponent>().LeftButtonReleased = true;  // the click fires on release

        ScreenTransitionRequest? published = null;
        world.Subscribe((in ScreenTransitionRequest r) => published = r);

        using var focus = MonoDreams.Tests.Ui.MenuInteraction.Focus(world);
        using var interaction = new ButtonInteractionSystem(world);
        MonoDreams.Tests.Ui.MenuInteraction.Tick(focus, interaction, Play());

        Assert.Equal(Color.OrangeRed, label.Get<DynamicTextComponent>().Color); // the child recolored
        Assert.NotNull(published);
        Assert.Equal(ScreenName.Game, published!.Value.ScreenName);
        Assert.Equal("Level_0", published.Value.LevelIdentifier);
    }
}
