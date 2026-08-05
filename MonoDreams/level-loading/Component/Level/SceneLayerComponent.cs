#nullable enable
namespace MonoDreams.Component.Level;

/// <summary>
/// A scene LAYER — the designer-created organizational unit (Figma/Aseprite/LDtk model): an
/// ordinary scene ENTITY (one-data-model, like the camera) carrying this component; its NAME is
/// its <c>EntityInfoComponent.Name</c>, its members are its <c>ChildOf</c> children, and its KIND
/// is DERIVED from whatever else the layer entity carries — there is no kind enum to desync.
/// Today every layer is a <b>Sprites</b> layer (free placement); a later tile-paint wave adds the
/// paint-layer marker component, and a layer entity carrying it is a <b>Paint</b> layer
/// (paintable cells + autotile rules) by virtue of carrying it, not by declaring a kind.
///
/// <para><b>Draw order is derived, member data is stable.</b> Layers order back-to-front by
/// <see cref="Order"/>; each gets an equal slice of the draw-depth range, and a member sprite's
/// SOURCE <c>SpriteInfoComponent.LayerDepth</c> is reinterpreted as its WITHIN-layer position
/// (0 = back of the layer, 1 = front). <c>SceneLayerSystem</c> remaps the final
/// <c>DrawComponent.LayerDepth</c> per frame — so REORDERING layers never rewrites member data,
/// within-layer ordering nudges keep working, and entities on NO layer keep their authored depths
/// untouched (full backward compatibility).</para>
///
/// <para><see cref="Visible"/> hides the whole layer (editor AND game — the remap draws members
/// fully transparent); <see cref="Locked"/> is an editor-side guard (selection/placement skip the
/// layer's members; serialized so a locked background stays locked across sessions).</para>
/// </summary>
public sealed class SceneLayerComponent
{
    /// <summary>Back-to-front index (0 = furthest back). Ties break by name for determinism.</summary>
    public int Order;

    /// <summary>Whether the layer's members render (editor and game).</summary>
    public bool Visible = true;

    /// <summary>Editor-side: a locked layer's members are not selectable/editable/placed-into.</summary>
    public bool Locked;

    /// <summary>A SCREEN-SPACE layer (Blender-collections HUD grouping): its members are UI fixed
    /// over the CAMERA's frame, not world objects — they author in virtual-resolution coordinates
    /// on the HUD render pass. Excluded from the world draw-band slicing, so its members keep their
    /// own authored depths and the game's HUD pass is untouched.</summary>
    public bool ScreenSpace;
}
