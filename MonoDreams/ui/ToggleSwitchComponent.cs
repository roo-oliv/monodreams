using DefaultEcs;
using MonoDreams.Draw;

namespace MonoDreams.UI;

/// Two-state toggle rendered as a checkbox. A small system
/// (<see cref="ToggleSwitchSystem"/>) shows the linked <see cref="CheckmarkEntity"/>'s
/// mesh while <see cref="On"/> is true and empties it while false — the box itself is a
/// static sibling mesh. Click handling is game-side: typically pair this with a
/// <see cref="SimpleButtonComponent"/> on the same entity so the existing hit-test path
/// fires, then flip <see cref="On"/>.
public struct ToggleSwitchComponent
{
    public bool On;
    /// Child entity rendering the checkmark. Its <c>DrawComponent</c> mesh is filled from
    /// <see cref="CheckmarkMesh"/> when on and emptied when off (UI/HUD targets always
    /// render, so visibility is toggled by the mesh contents, not <c>VisibleComponent</c>).
    public Entity CheckmarkEntity;
    /// The "checked" mesh, applied to <see cref="CheckmarkEntity"/> when <see cref="On"/>.
    public MeshData CheckmarkMesh;
}
