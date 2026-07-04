namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The discrete actions the editor toolbar can fire. Each toolbar button carries one of these on its
/// <see cref="ToolbarButtonComponent"/>; on a click, <c>ToolbarSystem</c> hands the action to a
/// dispatch callback the screen wires to the concrete behaviour (Save through <c>SceneWriter</c>,
/// Load by publishing <c>LoadSceneRequest</c>, Undo/Redo on the <c>EditorHistory</c>, the tool/snap
/// actions mutating the shared <see cref="GizmoStateComponent"/>).
///
/// <para>The enum lives in <c>level-editor</c> (not the game) so the toolbar system and its tests are
/// engine-side; the screen supplies the wiring, keeping <c>level-editor</c> free of any game type.</para>
/// </summary>
public enum EditorToolbarAction
{
    /// <summary>The transport's Play/Pause toggle (one button — its label reflects the state):
    /// Playing = <c>RunMode.Play</c> with the shell still composed, Paused = <c>RunMode.Edit</c>.
    /// Dispatches in BOTH modes (it is how you leave either state).</summary>
    PlayPause,

    /// <summary>The transport's Restart: rebuild the scene from the original load request and land
    /// Paused; unsaved edits are discarded. Dispatches in BOTH modes.</summary>
    Restart,

    /// <summary>Select the move tool (sets <see cref="GizmoStateComponent.Tool"/> to <see cref="GizmoTool.Move"/>).</summary>
    ToolMove,

    /// <summary>Select the rotate tool.</summary>
    ToolRotate,

    /// <summary>Select the scale tool.</summary>
    ToolScale,

    /// <summary>Save the current scene (the screen calls <c>SceneWriter.Save</c>).</summary>
    Save,

    /// <summary>Load a scene (the screen publishes a <c>LoadSceneRequest</c>).</summary>
    Load,

    /// <summary>Undo the most recent edit (the screen calls <c>EditorHistory.Undo</c>).</summary>
    Undo,

    /// <summary>Redo the most recently undone edit (the screen calls <c>EditorHistory.Redo</c>).</summary>
    Redo,

    /// <summary>Toggle grid-snap (flips <see cref="GizmoStateComponent.SnapEnabled"/>).</summary>
    ToggleSnap,

    /// <summary>Within-band ordering (island-authoring §4.2): nudge the selection's SOURCE sort
    /// toward the front of its layer band (the screen calls
    /// <c>EditorCommandSystem.BringForward</c>). Headless alias: <c>order:forward</c>.</summary>
    OrderForward,

    /// <summary>Nudge the selection's SOURCE sort toward the back of its band
    /// (<c>EditorCommandSystem.SendBack</c>). Headless alias: <c>order:back</c>.</summary>
    OrderBack,

    /// <summary>Add the footprint-default box collider to the selection (island-authoring §5.1;
    /// <c>EditorCommandSystem.AddBoxCollider</c>). Headless alias: <c>collider:addBox</c>.</summary>
    ColliderAddBox,

    /// <summary>Add the default polygon collider (footprint hexagon) to the selection
    /// (<c>EditorCommandSystem.AddConvexCollider</c>). Headless alias: <c>collider:addConvex</c>.</summary>
    ColliderAddConvex,

    /// <summary>Remove the selection's collider(s) — the bound one when a proxy is selected
    /// (<c>EditorCommandSystem.RemoveCollider</c>). Headless alias: <c>collider:remove</c>.</summary>
    ColliderRemove,

    /// <summary>Insert a vertex into the selection's convex collider (edge midpoint —
    /// <c>EditorCommandSystem.AddVertex</c>). Headless alias: <c>collider:addVertex</c>.</summary>
    VertexAdd,
}

/// <summary>Classification helpers over <see cref="EditorToolbarAction"/>.</summary>
public static class EditorToolbarActionExtensions
{
    /// <summary>Whether the action is a transport control (Play/Pause / Restart) — dispatched in
    /// BOTH modes, unlike the editing actions which are Paused (Edit) only.</summary>
    public static bool IsTransport(this EditorToolbarAction action) =>
        action is EditorToolbarAction.PlayPause or EditorToolbarAction.Restart;
}
