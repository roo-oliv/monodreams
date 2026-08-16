using MonoDreams.State;

namespace MonoDreams.System;

/// <summary>
/// Implemented by a system that owns <b>transient entities</b> — entities it created and is the only
/// thing that can dispose — so the pipeline can tell it when it <b>stops being run</b>.
/// <see cref="GatedSystem"/> calls <see cref="Suspend"/> exactly once on the frame the gate stops
/// forwarding to the child (the run mode entered a mode the child's <see cref="EditTimeBehavior"/>
/// excludes, or the gate's own <see cref="GatedSystem.IsEnabled"/> was switched off — the systems
/// panel's master toggle), and not again until the child has run at least once more.
///
/// <para><b>Why the hook exists.</b> For a stateless system "stop running it" and "its output stops"
/// are the same thing. For one that owns entities they are not: a frozen system never gets another
/// <c>Update</c> in which to clean up, while the draw stack — <c>RunNormally</c> by policy, since a
/// frozen renderer is a black screen — keeps drawing whatever it left behind, forever. A tooltip, a
/// drag ghost, a damage number or a hover highlight that outlives its owner is exactly that bug.</para>
///
/// <para><b>Not a policy declaration.</b> The system still learns nothing about run modes; the
/// decision to run stays data on the gate (foundation premise "Edit-time behaviour is a per-system
/// policy honoured by <c>GatedSystem</c>"). It only learns that it is no longer being run — the same
/// callback for every reason a gate can stop. Implementations must therefore be <b>idempotent</b>,
/// mode-agnostic, and leave the system ready for a later <c>Update</c> to rebuild from scratch.</para>
///
/// <para><b>Scope.</b> The gate forwards to its immediate child, so a suspendable system must be
/// registered as its own pipeline entry. A DefaultEcs composite (<c>SequentialSystem</c> /
/// <c>ParallelSystem</c>) does not expose its children, so a suspendable system buried inside a
/// <b>gated group</b> is not reached when that group freezes — register it with its own gate.</para>
/// </summary>
public interface ISuspendableSystem
{
    /// <summary>
    /// Tear down whatever this system would otherwise leave on screen (or in the world) now that the
    /// pipeline has stopped running it. Called by <see cref="GatedSystem"/> on the running →
    /// not-running edge; must be idempotent and safe to call in any <see cref="RunMode"/>.
    /// </summary>
    void Suspend(GameState state);
}
