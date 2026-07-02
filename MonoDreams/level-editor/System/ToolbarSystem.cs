#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor toolbar's interaction system. In <see cref="RunMode.Edit"/> it hit-tests the cursor
/// against every <see cref="ToolbarButtonComponent"/> and, on a click (left button released over a
/// button), hands the button's <see cref="EditorToolbarAction"/> to a dispatch callback the screen
/// supplies — which wires Save → <c>SceneWriter</c>, Load → publish <c>LoadSceneRequest</c>,
/// Undo/Redo → <c>EditorHistory</c>, and the tool/snap actions → the shared
/// <see cref="GizmoStateComponent"/>. It also tracks per-button hover and tints the button fill.
///
/// <para><b>Native screen-space hit-test (Wave 7).</b> The toolbar lives on the Editor render
/// target — native window resolution, composited 1:1 — so button <c>Bounds</c> are physical screen
/// pixels and the system tests the cursor's raw <see cref="CursorInputComponent.ScreenPosition"/>
/// (hardware pixels, set before any letterbox/camera mapping) against them. The chrome sits in the
/// viewport-inset margins where the virtual mapping is null, so <c>VirtualPosition</c> (the old
/// HUD hit-test coordinate) would be frozen/stale there; <c>ScreenPosition</c> is always live.</para>
///
/// <para><b>Game-agnostic.</b> Like <c>EditorModeToggleSystem</c> takes a predicate, this takes an
/// <c>Action&lt;EditorToolbarAction&gt;</c> so <c>level-editor</c> needs no game type; the screen
/// owns the concrete <c>SceneWriter</c> / history / camera / layers and supplies the dispatch.
/// Edit-guarded (inert in Play), registered RunNormally.</para>
///
/// <para><b>Hidden in Play.</b> Chrome entities render only through the Editor chrome pass
/// (<c>EditorChromeRenderSystem</c>), which contributes nothing outside Edit — so this system no
/// longer blanks meshes/labels per entity (the Wave-4b HUD workaround); visibility is owned by
/// the pass, interactivity by this system's Edit guard.</para>
/// </summary>
[With(typeof(ToolbarButtonComponent), typeof(TransformComponent))]
public sealed class ToolbarSystem : AEntitySetSystem<GameState>
{
    private readonly EntitySet _cursorSet;
    private readonly Action<EditorToolbarAction> _dispatch;

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

        // Edit-guarded: inert clicks in Play (the chrome pass already hides the visuals there).
        if (state.RunMode != RunMode.Edit) return;

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            _active = true;
            _cursorPoint = input.ScreenPosition; // native-pixel hit-test (Editor target chrome)
            _clicked = input.LeftButtonReleased;  // a click = press then release over the button
            return;
        }
    }

    protected override void Update(GameState state, in Entity entity)
    {
        if (!_active) return;

        ref var button = ref entity.Get<ToolbarButtonComponent>();
        var over = button.Bounds.Contains(_cursorPoint);
        button.IsHovered = over;

        // Hover tint on the engine button fill (the mesh is rebuilt by ButtonMeshPrepSystem, so
        // the tint shows on the next prep — one frame, imperceptible).
        if (entity.Has<SimpleButtonComponent>())
        {
            ref var visual = ref entity.Get<SimpleButtonComponent>();
            visual.FillColor = over ? EditorChromeBuilder.ButtonHoverFill : EditorChromeBuilder.ButtonFill;
        }

        if (over && _clicked)
            _dispatch(button.Action);
    }

    public override void Dispose()
    {
        _cursorSet.Dispose();
        base.Dispose();
    }
}
