#nullable enable
using DefaultEcs;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible edit to the camera RIG's authored zoom (UX2-E) — the command the gizmo emits when the
/// rig's <b>Scale</b> tool is dragged. The rig's Scale gesture edits <see cref="CameraRigComponent.Zoom"/>,
/// NOT <c>TransformComponent.Scale</c> (a bigger frustum means a LOWER zoom), so it cannot ride
/// <see cref="TransformEditCommand"/>. Pure data: the rig entity plus the before/after zoom;
/// <see cref="Apply"/> writes the after, <see cref="Revert"/> the before.
///
/// <para>Pushed per drag frame inside the history's coalescing transaction (exactly like
/// <see cref="TransformEditCommand"/> / <see cref="ColliderEditCommand"/>), so a whole zoom drag is one
/// undo step; <see cref="FromCurrent"/> reads the live (last-frame) zoom as the "before", so the
/// composite's revert chain walks back to the pre-drag zoom in one undo. A dead or rig-less target is a
/// safe no-op — the write-back mirrors the other edit commands' guards.</para>
/// </summary>
public sealed class CameraZoomEditCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly float _beforeZoom, _afterZoom;

    public CameraZoomEditCommand(Entity entity, float beforeZoom, float afterZoom)
    {
        _entity = entity;
        _beforeZoom = beforeZoom;
        _afterZoom = afterZoom;
    }

    /// <summary>Builds a command from the rig's <b>current</b> <see cref="CameraRigComponent.Zoom"/> as
    /// the "before" and <paramref name="afterZoom"/> as the "after" — the coalescing-transaction path
    /// (mirrors <see cref="TransformEditCommand.FromCurrent"/>).</summary>
    public static CameraZoomEditCommand FromCurrent(Entity entity, float afterZoom)
    {
        var before = entity.Get<CameraRigComponent>().Zoom;
        return new CameraZoomEditCommand(entity, before, afterZoom);
    }

    public void Apply(World world) => Write(_afterZoom);
    public void Revert(World world) => Write(_beforeZoom);

    private void Write(float zoom)
    {
        if (!_entity.IsAlive || !_entity.Has<CameraRigComponent>()) return;
        // CameraRigComponent is a struct; Get<T>() returns a ref, so this mutates it in place — the
        // glyph + the selection border-pick both re-read the live zoom each frame, no NotifyChanged.
        _entity.Get<CameraRigComponent>().Zoom = zoom;
    }
}
