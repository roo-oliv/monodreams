#nullable enable
using DefaultEcs;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible editor mutation. ECS purity (per the directive): a command is <b>data</b> describing
/// the mutation plus an <see cref="Apply"/> / <see cref="Revert"/> pair that the editor's undo
/// machinery invokes — not a behavior-laden OO object. Concrete commands hold the minimal data they
/// need to re-do and un-do (a transform before/after, a serialized sub-graph snapshot, …) and keep
/// their bodies to applying that data to the <see cref="World"/>.
///
/// <para>The history (<c>EditorHistory</c>) owns the apply/revert sequencing — when an entry is
/// pushed it is already applied; <c>Undo</c> calls <see cref="Revert"/>, <c>Redo</c> re-calls
/// <see cref="Apply"/>. A command must therefore be replayable: <see cref="Apply"/> after a
/// <see cref="Revert"/> must reproduce the same result.</para>
/// </summary>
public interface IEditorCommand
{
    /// <summary>Re-do the mutation (also the initial do, called by the history on push).</summary>
    void Apply(World world);

    /// <summary>Un-do the mutation, restoring the world to its pre-<see cref="Apply"/> state.</summary>
    void Revert(World world);
}
