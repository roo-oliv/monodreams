namespace MonoDreams.Component;

/// <summary>
/// Marks an entity as <b>the scene camera</b> and carries the one piece of camera state that is not
/// already spatial: <see cref="Zoom"/>. Position and rotation come from the entity's
/// <see cref="TransformComponent"/> (one rotation, not two — CM pre-mortem #1), and the virtual
/// resolution stays render config on the <see cref="Camera"/> adapter, never scene data.
///
/// <para>The camera is an ordinary scene-owned root entity — <c>EntityInfoComponent("Camera")</c> +
/// <see cref="TransformComponent"/> + <see cref="CameraComponent"/>, tagged like everything else and
/// serialized in <c>entities[]</c>. <c>CameraSyncSystem</c> copies the camera entity's
/// <c>(WorldPosition, WorldRotation, Zoom)</c> into the shared <see cref="Camera"/> render adapter each
/// frame in Play; <c>CameraFollowSystem</c> lerps the camera entity's Transform toward its follow
/// target. There is exactly ONE camera entity per scene (the writer refuses a second; the reader ensures
/// one exists).</para>
/// </summary>
public class CameraComponent
{
    /// <summary>The camera zoom (world-units magnification). 1 = the virtual resolution fills the view;
    /// larger zooms in. The <see cref="Camera"/> adapter clamps the applied value to a sane floor.</summary>
    public float Zoom { get; set; } = 1f;
}
