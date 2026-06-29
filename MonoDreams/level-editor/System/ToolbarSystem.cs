#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The engine-native editor toolbar's interaction system (Wave 4b). In <see cref="RunMode.Edit"/> it
/// hit-tests the cursor against every <see cref="ToolbarButtonComponent"/> and, on a click (left
/// button released over a button), hands the button's <see cref="EditorToolbarAction"/> to a dispatch
/// callback the screen supplies — which wires Save → <c>SceneWriter</c>, Load → publish
/// <c>LoadSceneRequest</c>, Undo/Redo → <c>EditorHistory</c>, and the tool/snap actions → the shared
/// <see cref="GizmoStateComponent"/>. It also tracks per-button hover for the visual tint.
///
/// <para><b>Screen-space hit-test.</b> The toolbar is built on the UI/HUD render target, so its
/// buttons are positioned in virtual-resolution space; the system tests the cursor's
/// <see cref="CursorInputComponent.VirtualPosition"/> (the letterbox-scaled, pre-camera coordinate)
/// against <see cref="ToolbarButtonComponent.Bounds"/> — moving the world camera never desyncs the
/// click. This mirrors the Examples <c>ButtonInteractionSystem</c> click precedent, on the editor's
/// own button tag.</para>
///
/// <para><b>Game-agnostic.</b> Like <c>EditorModeToggleSystem</c> takes a predicate, this takes an
/// <c>Action&lt;EditorToolbarAction&gt;</c> so <c>level-editor</c> needs no game type; the screen
/// owns the concrete <c>SceneWriter</c> / history / camera / layers and supplies the dispatch.
/// Edit-guarded (inert in Play), registered RunNormally.</para>
///
/// <para><b>Hidden in Play.</b> The matrix says the toolbar is hidden in Play and active in Edit. The
/// HUD render pass does not cull on <c>VisibleComponent</c>, so the system instead blanks each
/// button's <c>DrawComponent</c> mesh (and its label text) in Play and restores it in Edit, so the
/// toolbar appears only while editing — without a parallel render path.</para>
/// </summary>
[With(typeof(ToolbarButtonComponent), typeof(TransformComponent))]
public sealed class ToolbarSystem : AEntitySetSystem<GameState>
{
    private readonly EntitySet _cursorSet;
    private readonly Action<EditorToolbarAction> _dispatch;

    private bool _editing;
    private bool _active;
    private Vector2 _cursorPoint;
    private bool _clicked;

    public ToolbarSystem(World world, Action<EditorToolbarAction> dispatch)
        : base(world.GetEntities().With<ToolbarButtonComponent>().With<TransformComponent>().AsSet())
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    protected override void PreUpdate(GameState state)
    {
        _active = false;
        _clicked = false;
        _editing = state.RunMode == RunMode.Edit;

        if (!_editing) return; // Edit-guarded: inert clicks in Play (visibility still toggled in Update)

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            _active = true;
            _cursorPoint = input.VirtualPosition; // screen-space hit-test (UI/HUD target)
            _clicked = input.LeftButtonReleased;   // a click = press then release over the button
            return;
        }
    }

    protected override void Update(GameState state, in Entity entity)
    {
        // Toolbar visibility tracks the run mode (hidden in Play, shown in Edit).
        SetButtonShown(entity, _editing);

        if (!_active) return;

        ref var button = ref entity.Get<ToolbarButtonComponent>();
        var over = button.Bounds.Contains(_cursorPoint);
        button.IsHovered = over;

        if (over && _clicked)
            _dispatch(button.Action);
    }

    /// <summary>
    /// Shows or hides a toolbar button. On the HUD target the render pass ignores
    /// <c>VisibleComponent</c>, so in Play we blank the button's outline mesh (and its label's
    /// <c>DynamicTextComponent</c>) so the HUD pass draws nothing; in Edit, <c>ButtonMeshPrepSystem</c>
    /// (which runs after this system) rebuilds the outline and the text content is restored.
    /// </summary>
    private static void SetButtonShown(in Entity entity, bool shown)
    {
        if (!shown && entity.Has<DrawComponent>())
        {
            ref var dc = ref entity.Get<DrawComponent>();
            dc.Vertices = global::System.Array.Empty<Microsoft.Xna.Framework.Graphics.VertexPositionColor>();
            dc.Indices = global::System.Array.Empty<int>();
        }

        // Toggle the label text on the button's referenced text entity. TextPrepSystem renders
        // nothing when VisibleCharacterCount <= 0, so 0 hides the label in Play; int.MaxValue (the
        // build-time value) shows the whole label in Edit.
        if (entity.Has<SimpleButtonComponent>())
        {
            var textEntity = entity.Get<SimpleButtonComponent>().TextEntity;
            if (textEntity is { IsAlive: true } te && te.Has<DynamicTextComponent>())
            {
                ref var text = ref te.Get<DynamicTextComponent>();
                text.VisibleCharacterCount = shown ? int.MaxValue : 0;
            }
        }
    }

    public override void Dispose()
    {
        _cursorSet.Dispose();
        base.Dispose();
    }
}
