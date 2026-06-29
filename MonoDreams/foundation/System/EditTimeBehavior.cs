namespace MonoDreams.System;

/// <summary>
/// Per-system edit-time policy: how a system should behave when the engine is in
/// <see cref="MonoDreams.State.RunMode.Edit"/>. A <see cref="GatedSystem"/> pairs a
/// child system with one of these policies and consults
/// <see cref="MonoDreams.State.GameState.RunMode"/> each frame to decide whether to
/// run the child.
///
/// The interaction matrix (which engine systems take which policy in the level editor)
/// lives in <c>docs/CORE_TENETS.md</c> ("The editor is part of the game") and in the
/// <c>foundation</c> run-state premise; in short: render / input / cursor and
/// <c>HierarchySystem</c> stay <see cref="RunNormally"/>, while game logic / physics /
/// camera-follow take <see cref="Freeze"/>.
/// </summary>
public enum EditTimeBehavior
{
    /// <summary>
    /// Run in every mode (Play and Edit). The default for systems that must stay live
    /// while editing — rendering, input, cursor, and <c>HierarchySystem</c> (editor
    /// edits to a transform must still propagate to world space).
    /// </summary>
    RunNormally,

    /// <summary>
    /// Run only in <see cref="MonoDreams.State.RunMode.Play"/>; skipped in
    /// <see cref="MonoDreams.State.RunMode.Edit"/>. For systems whose effect would fight
    /// the editor — movement, velocity, physics, collision, AI/dialogue, camera-follow.
    /// </summary>
    Freeze,

    /// <summary>
    /// Reserved for a later wave: a system that runs in both modes but with reduced /
    /// partial work in Edit (e.g. a physics commit reused only to keep transforms
    /// consistent). For now this behaves like <see cref="RunNormally"/> — runs in both
    /// modes. The finer "partial" semantics are deliberately deferred.
    /// </summary>
    RunPartial,

    /// <summary>
    /// Reserved for a later wave: a system that remains editable/interactive at edit time
    /// (the editor mutates the data it owns). For now this behaves like
    /// <see cref="RunNormally"/> — runs in both modes. The finer semantics are deferred.
    /// </summary>
    RuntimeEditable,
}
