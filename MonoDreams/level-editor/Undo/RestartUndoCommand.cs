#nullable enable
using System;
using DefaultEcs;
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// The transport Restart's undo entry: an accidental Restart no longer eats unsaved edits — the
/// pre-restart world is captured as a <see cref="SceneData"/> snapshot (data only, the
/// context-stack convention) and this ONE command is pushed onto the just-cleared history
/// (<c>EditorHistory.PushApplied</c> — the restart already ran).
/// <list type="bullet">
///   <item><b>Revert (Ctrl+Z)</b> — tear the restarted world down and restore the snapshot through
///   the reader (the same sweep/rebuild/restore delegates a tab switch uses), bringing every
///   unsaved edit back. The history marks dirty, so the recovered edits read as unsaved.</item>
///   <item><b>Apply (redo)</b> — re-run the restart's teardown + reload-from-disk, reproducing the
///   restarted state (the replayability contract).</item>
/// </list>
/// The history was CLEARED before this entry (older commands reference the disposed entities), so
/// Restart-undo is exactly one level deep — enough to recover from the accident, never a general
/// time machine across restarts.
/// </summary>
public sealed class RestartUndoCommand : IEditorCommand
{
    private readonly SceneData _preRestartWorld;
    private readonly Action _reloadFromDisk;
    private readonly Action<SceneData> _restoreSnapshot;

    public RestartUndoCommand(SceneData preRestartWorld, Action reloadFromDisk, Action<SceneData> restoreSnapshot)
    {
        _preRestartWorld = preRestartWorld ?? throw new ArgumentNullException(nameof(preRestartWorld));
        _reloadFromDisk = reloadFromDisk ?? throw new ArgumentNullException(nameof(reloadFromDisk));
        _restoreSnapshot = restoreSnapshot ?? throw new ArgumentNullException(nameof(restoreSnapshot));
    }

    public void Apply(World world) => _reloadFromDisk();

    public void Revert(World world) => _restoreSnapshot(_preRestartWorld);
}
