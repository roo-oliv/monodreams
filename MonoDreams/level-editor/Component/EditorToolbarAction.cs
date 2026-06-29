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
}
