using DefaultEcs.System;
using MonoDreams.State;

namespace MonoDreams.System;

/// <summary>
/// A run-state decorator: wraps a child <see cref="ISystem{GameState}"/> with an
/// <see cref="EditTimeBehavior"/> policy and, each frame, consults
/// <see cref="GameState.RunMode"/> to decide whether to forward the call to the child.
///
/// This is the single mechanism by which the in-game level editor freezes the game
/// pipeline without forking it (see <c>docs/CORE_TENETS.md</c> — "The editor is part of
/// the game"). The gate is <b>opt-in</b>: only systems a screen explicitly wraps change
/// behaviour with the run mode. Because <see cref="GameState.RunMode"/> defaults to
/// <see cref="RunMode.Play"/>, every existing (ungated) screen is byte-identical.
///
/// Policy semantics:
/// <list type="bullet">
///   <item><see cref="EditTimeBehavior.RunNormally"/> — always run the child.</item>
///   <item><see cref="EditTimeBehavior.Freeze"/> — run only in
///   <see cref="RunMode.Play"/>; skip in <see cref="RunMode.Edit"/>.</item>
///   <item><see cref="EditTimeBehavior.RunPartial"/> /
///   <see cref="EditTimeBehavior.RuntimeEditable"/> — reserved; for now they run in both
///   modes (the finer partial semantics are a later-wave concern).</item>
/// </list>
///
/// <see cref="IsEnabled"/> composes with the policy and the child's own
/// <see cref="ISystem{T}.IsEnabled"/>: the child runs only when the gate is enabled,
/// the policy admits the current run mode, and the child itself is enabled. This lets a
/// gated <c>CameraFollowSystem</c>, say, still be toggled off via its own flag
/// independently of the run mode.
/// </summary>
public sealed class GatedSystem : ISystem<GameState>
{
    private readonly ISystem<GameState> _child;
    private readonly EditTimeBehavior _policy;

    /// <summary>The wrapped child system.</summary>
    public ISystem<GameState> Child => _child;

    /// <summary>The edit-time policy this gate enforces.</summary>
    public EditTimeBehavior Policy => _policy;

    /// <summary>
    /// Whether this gate itself is enabled. When <c>false</c> the child never runs,
    /// regardless of run mode or policy (mirrors <see cref="ISystem{T}.IsEnabled"/>).
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public GatedSystem(ISystem<GameState> child, EditTimeBehavior policy)
    {
        _child = child;
        _policy = policy;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        if (!ShouldRun(_policy, state.RunMode)) return;
        // The child honors its own IsEnabled internally (per the ISystem contract), so we
        // forward unconditionally once the gate + policy admit this frame.
        _child.Update(state);
    }

    /// <summary>
    /// Pure policy decision: should a child with <paramref name="policy"/> run in
    /// <paramref name="mode"/>? Exposed for direct unit testing of the gating table.
    /// </summary>
    public static bool ShouldRun(EditTimeBehavior policy, RunMode mode) => policy switch
    {
        EditTimeBehavior.Freeze => mode == RunMode.Play,
        // RunNormally, and (for Wave 1) the reserved RunPartial / RuntimeEditable, run in both modes.
        _ => true,
    };

    public void Dispose() => _child.Dispose();
}
