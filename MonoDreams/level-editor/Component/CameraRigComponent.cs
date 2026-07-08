namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The <b>authored game-camera state</b> the editor materializes as a standalone entity — the "camera
/// rig" (UX2-E). The persisted scene form stays <c>scene.camera</c> (no format change); on every load
/// the editor re-syncs a rig entity from it, and on Save the writer reads <c>scene.camera</c> FROM the
/// rig — so the authored game camera is a thing the designer can see and move independently of the free
/// editor VIEW (the shared <c>Camera</c> the viewport looks through, driven by <c>CameraNavSystem</c>).
///
/// <para>The rig entity carries this component (the zoom + rotation half of the camera state) plus a
/// <c>TransformComponent</c> whose <c>Position</c> is the camera CENTRE (the other half — position lives
/// on the transform so the ordinary gizmo can move it via a <c>TransformEditCommand</c>). The camera's
/// VIRTUAL resolution is deliberately NOT stored here: it is immutable on the shared <c>Camera</c>
/// (rendering — "Camera.VirtualResolution is immutable"), and the rig's frustum glyph derives its
/// world-rect from the live camera's virtual size ÷ the rig zoom.</para>
///
/// <para><b>Never serialized, never scene membership.</b> Like <c>GizmoStateComponent</c> this is
/// transient editor state — it is NOT on the serializer registry, and the rig entity is NEVER tagged
/// <c>SceneObjectComponent</c>, so it never enters <c>entities[]</c> (pre-mortem #4). The rig carries
/// <c>EditorInfrastructureComponent</c> instead, so it survives a transport Restart and is hidden from
/// the Entities tree (it is picked in the viewport by its frustum border, and inspected like any
/// selected entity). It is not deletable — the delete command path refuses it loudly.</para>
/// </summary>
public struct CameraRigComponent
{
    /// <summary>The authored camera zoom (the multiplier the frustum world-rect divides the virtual
    /// resolution by). Mirrors <c>scene.camera.zoom</c> / the shared <c>Camera.Zoom</c>.</summary>
    public float Zoom;

    /// <summary>The authored camera rotation (radians). Mirrors <c>scene.camera.rotation</c> /
    /// <c>Camera.Rotation</c>. Round-tripped through the rig but not edited by the move-only gizmo this
    /// wave (rotation/zoom editing via the Inspector is a future wave).</summary>
    public float Rotation;

    public CameraRigComponent(float zoom, float rotation = 0f)
    {
        Zoom = zoom;
        Rotation = rotation;
    }
}
