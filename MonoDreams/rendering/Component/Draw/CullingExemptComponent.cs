namespace MonoDreams.Component.Draw;

/// <summary>
/// Tags a sprite whose VISIBILITY IS OWNED BY ITS PRODUCER, so <c>CullingSystem</c> skips it: the
/// producer adds <see cref="VisibleComponent"/> itself and takes responsibility for the entity only
/// existing when it should draw.
///
/// <para>The case it exists for is a streamed tile chunk: <c>TileGridBakeSystem</c> only bakes the
/// chunks the camera's view covers and disposes them when they leave, so every live tile is inside
/// the view by construction — bounds-testing thousands of them every frame re-derives a fact the
/// streamer already guarantees. Do NOT put this on anything whose position moves independently of
/// its producer's bookkeeping; culling is the right default and this is the narrow exception.</para>
/// </summary>
public struct CullingExemptComponent;
