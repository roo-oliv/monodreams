#nullable enable
using DefaultEcs;
using MonoDreams.Component.Draw;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible within-band ordering edit (island-authoring plan §4.2 — the "Bring forward /
/// Send back" actions): pure data — the entity plus the before/after of the two SOURCE sort
/// fields the nudge may touch, <c>SpriteInfoComponent.LayerDepth</c> (plain bands) and
/// <c>SpriteInfoComponent.YSortDepthBias</c> (Y-sorted bands). <see cref="Apply"/> writes the
/// after pair, <see cref="Revert"/> the before pair. Like every sort edit it targets the SOURCE
/// fields, never the per-frame-derived <c>DrawComponent.LayerDepth</c> — the next prep + Y-sort
/// frame re-derives the final depth, and the serializer persists these same source fields, so a
/// nudge survives save/load. A dead or sprite-less target is a safe no-op.
/// </summary>
public sealed class SpriteSortEditCommand(
    Entity entity,
    float beforeLayerDepth, float afterLayerDepth,
    float beforeYSortDepthBias, float afterYSortDepthBias) : IEditorCommand
{
    public void Apply(World world) => Write(afterLayerDepth, afterYSortDepthBias);
    public void Revert(World world) => Write(beforeLayerDepth, beforeYSortDepthBias);

    private void Write(float layerDepth, float ySortDepthBias)
    {
        if (!entity.IsAlive || !entity.Has<SpriteInfoComponent>()) return;
        ref var sprite = ref entity.Get<SpriteInfoComponent>();
        sprite.LayerDepth = layerDepth;
        sprite.YSortDepthBias = ySortDepthBias;
    }
}
