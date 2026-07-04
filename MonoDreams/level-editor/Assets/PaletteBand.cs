#nullable enable

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// One layer band the palette's band selector offers (island-authoring plan §3.3) —
/// <b>screen-supplied</b>: the game screen builds a small list of these from ITS
/// <c>DrawLayerMap</c> (e.g. Ground → <c>Background</c>, Ground detail → <c>Tiles</c>,
/// Props/actors → <c>Characters</c> y-sorted, Overhead → <c>Foreground</c>) and hands it to the
/// <c>EditorOverlay</c>, exactly the way it supplies <c>EditorInputBindings</c> and the toolbar
/// dispatch. The <c>level-editor</c> module never references a game's layer enum.
/// </summary>
/// <param name="Name">The selector label (e.g. <c>"Props"</c>).</param>
/// <param name="LayerDepth">The band's SOURCE layer depth
/// (<c>DrawLayerMap.GetDepth(...)</c>) written to <c>SpriteInfoComponent.LayerDepth</c>.</param>
/// <param name="YSorted">Whether the band is Y-sorted — <see cref="SpritePropFactory"/> then
/// applies the feet-origin convention (Origin = bottom-center, YSortOffset = 0).</param>
public readonly record struct PaletteBand(string Name, float LayerDepth, bool YSorted);
