using DefaultEcs;
using MonoDreams.LevelEditor.Undo;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the dirty-tracking signal UX-C added to <see cref="EditorHistory"/> (the missing
/// "unsaved changes" bit the Scenes-panel switch gate reads): the monotonic <c>EditVersion</c>,
/// <c>MarkSavePoint</c>, and <c>IsDirty</c> — including the documented conservative edge (undo back
/// to the save point still reads dirty) and Restart's <c>Clear</c> resetting clean.
/// </summary>
public class DirtyTrackingTests
{
    private sealed class IncrementCommand : IEditorCommand
    {
        private readonly int[] _box;
        public IncrementCommand(int[] box) { _box = box; }
        public void Apply(World world) => _box[0]++;
        public void Revert(World world) => _box[0]--;
    }

    [Fact]
    public void FreshHistory_IsClean()
    {
        using var world = new World();
        Assert.False(new EditorHistory(world).IsDirty);
    }

    [Fact]
    public void Push_MakesDirty_SavePoint_MakesClean_NextEditDirtyAgain()
    {
        using var world = new World();
        var h = new EditorHistory(world);
        var box = new[] { 0 };

        h.Push(new IncrementCommand(box));
        Assert.True(h.IsDirty);
        h.MarkSavePoint();
        Assert.False(h.IsDirty);
        h.Push(new IncrementCommand(box));
        Assert.True(h.IsDirty);
    }

    [Fact]
    public void UndoRedo_AdvanceEditVersion_SoBackToSavePointStillReadsDirty()
    {
        using var world = new World();
        var h = new EditorHistory(world);
        var box = new[] { 0 };

        h.Push(new IncrementCommand(box));
        h.MarkSavePoint();
        Assert.False(h.IsDirty);

        h.Undo(); // world is back at the save-point VALUE, but EditVersion advanced → conservatively dirty
        Assert.True(h.IsDirty);
        h.Redo();
        Assert.True(h.IsDirty);
    }

    [Fact]
    public void Transaction_CommitIsOneDirtyStep_MidTransactionIsNotYetDirty_CancelStaysClean()
    {
        using var world = new World();
        var h = new EditorHistory(world);
        var box = new[] { 0 };
        h.MarkSavePoint(); // clean baseline

        h.BeginTransaction();
        h.Push(new IncrementCommand(box));
        h.Push(new IncrementCommand(box));
        Assert.False(h.IsDirty);   // accumulating, nothing recorded yet
        h.CommitTransaction();
        Assert.True(h.IsDirty);    // one commit = one dirty step

        h.MarkSavePoint();
        h.BeginTransaction();
        h.Push(new IncrementCommand(box));
        h.CancelTransaction();     // an aborted drag records nothing
        Assert.False(h.IsDirty);
    }

    [Fact]
    public void Clear_ResetsClean_ButEditVersionIsMonotonic()
    {
        using var world = new World();
        var h = new EditorHistory(world);
        h.Push(new IncrementCommand(new[] { 0 }));
        Assert.True(h.IsDirty);

        var before = h.EditVersion;
        h.Clear(); // the transport's Restart rebuilds from disk → clean
        Assert.False(h.IsDirty);
        Assert.True(h.EditVersion > before); // never rewinds
    }
}
