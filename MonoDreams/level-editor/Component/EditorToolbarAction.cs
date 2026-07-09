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

    /// <summary>Open the Save dialog (Save Scene / Save Project / Save Backup As…). There is no Load
    /// action — a scene is opened by selecting it in the Scenes panel (UX-C/UX-D).</summary>
    Save,

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

    /// <summary>Enter the freeform boundary tool (island-authoring §5.2): clicks lay polyline
    /// vertices, Enter / double-click commits, Escape cancels (<c>BoundaryToolSystem.BeginBoundary</c>).
    /// A radio with the transform tools over <c>EditorToolMode</c>. Headless aliases:
    /// <c>boundary:begin</c> / <c>boundary:commit</c> / <c>boundary:cancel</c>.</summary>
    ToolBoundary,

    /// <summary>Re-scan the asset drop folder and rebuild the palette live (island-authoring
    /// Slice 4), so a newly-dropped PNG appears without restarting the editor
    /// (<c>PalettePlacementSystem.Refresh</c> — invalidates the texture cache too). Headless alias:
    /// the plain enum name <c>RefreshCatalog</c>.</summary>
    RefreshCatalog,

    /// <summary>The Scene-panel header's <b>Entity ▾</b> dropdown button (UX2-D §4): opens the entity
    /// context menu anchored below the button, acting on the current selection (the discoverable twin of
    /// the viewport right-click — one model, two anchors). An editing action (Paused/Edit only). The
    /// overlay maps it to <c>OpenContextMenu(EntityHeader)</c>.</summary>
    EntityMenu,

    /// <summary>The Scene-panel header's <b>Camera view</b> nav-corner button (UX2-E §6, right corner):
    /// snaps the free editor VIEW to the authored camera rig (<c>Camera := rig state</c>) — the
    /// back-to-camera-view affordance. An editing action (Paused/Edit only). Headless alias:
    /// <c>view:camera</c>. The overlay maps it to <c>EditorCameraRig.SnapViewToRig</c>.</summary>
    CameraView,

    /// <summary>The <b>Scene</b> segment of the Scene-panel header's <c>[Scene | Game]</c> mode toggle
    /// (UX2-F §5): exit the Game-mode sandbox back to editing the real scene (no-op when already in
    /// Scene mode). A mode-toggle action — dispatches in BOTH transport states. Headless alias:
    /// <c>mode:scene</c>. The overlay maps it to <c>EditorTransport.ExitToSceneMode</c>.</summary>
    ModeScene,

    /// <summary>The <b>Game</b> segment of the <c>[Scene | Game]</c> mode toggle (UX2-F §5): enter the
    /// Game-mode sandbox — snapshot the scene, look through the game camera (no-op when already in Game
    /// mode). A mode-toggle action — dispatches in BOTH transport states. Headless alias:
    /// <c>mode:game</c>. The overlay maps it to <c>EditorTransport.EnterGameMode</c>.</summary>
    ModeGame,

    /// <summary>The Scene-panel header's <b>Overlays</b> dropdown button (UX3-D §3, the two-overlapping-
    /// circles icon): opens the viewport-overlays menu anchored below it (Grid toggle, Grid Spacing ▸
    /// presets, Outline Selected toggle, Camera toggle — Blender's per-viewport Overlays dropdown). An
    /// editing action (Paused/Edit only). The overlay maps it to
    /// <c>OpenContextMenu(OverlaysHeader)</c>.</summary>
    Overlays,
}

/// <summary>Classification helpers over <see cref="EditorToolbarAction"/>.</summary>
public static class EditorToolbarActionExtensions
{
    /// <summary>Whether the action is a transport control (Play/Pause / Restart) — dispatched in
    /// BOTH modes, unlike the editing actions which are Paused (Edit) only.</summary>
    public static bool IsTransport(this EditorToolbarAction action) =>
        action is EditorToolbarAction.PlayPause or EditorToolbarAction.Restart;

    /// <summary>Whether the action is a <c>[Scene | Game]</c> mode-toggle segment (UX2-F). Like the
    /// transport it dispatches in BOTH transport states — exiting the sandbox must work while Playing —
    /// and it renders tab-style (not as a button) so it is excluded from the icon/label button path.</summary>
    public static bool IsModeToggle(this EditorToolbarAction action) =>
        action is EditorToolbarAction.ModeScene or EditorToolbarAction.ModeGame;

    /// <summary>
    /// Whether this button reads as ACTIVE given the shared gizmo state (UX2-C icon tinting): the
    /// transform tools are a radio over <see cref="GizmoStateComponent.Tool"/> (only while the coarse
    /// <see cref="GizmoStateComponent.Mode"/> is <see cref="EditorToolMode.SelectTransform"/>), Boundary
    /// is active while <see cref="EditorToolMode.Boundary"/> owns the modality, and the Snap toggle is
    /// active while <see cref="GizmoStateComponent.SnapEnabled"/>. Pure — the toolbar maps "active" to a
    /// theme role (radio tools → <c>Accent</c>, Snap → <c>Success</c>).
    /// </summary>
    public static bool IsActiveIn(this EditorToolbarAction action, GizmoStateComponent gizmo) => action switch
    {
        EditorToolbarAction.ToolMove =>
            gizmo.Mode == EditorToolMode.SelectTransform && gizmo.Tool == GizmoTool.Move,
        EditorToolbarAction.ToolRotate =>
            gizmo.Mode == EditorToolMode.SelectTransform && gizmo.Tool == GizmoTool.Rotate,
        EditorToolbarAction.ToolScale =>
            gizmo.Mode == EditorToolMode.SelectTransform && gizmo.Tool == GizmoTool.Scale,
        EditorToolbarAction.ToolBoundary => gizmo.Mode == EditorToolMode.Boundary,
        EditorToolbarAction.ToggleSnap => gizmo.SnapEnabled,
        _ => false,
    };
}
