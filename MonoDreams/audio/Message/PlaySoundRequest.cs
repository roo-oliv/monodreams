namespace MonoDreams.Message;

/// <summary>
/// Fire-and-forget one-shot sound effect (button click, hit feedback). Publish via
/// <c>world.Publish(new PlaySoundRequest("Sounds/click"))</c>; <c>AudioSystem</c> starts
/// exactly one playback with the given parameters, lets it play to completion, and releases
/// the instance when it finishes. For playback that needs a lifecycle (loop, cut, pause),
/// use <c>AudioSourceComponent</c> on an entity instead.
/// </summary>
/// <param name="SoundKey">Content key of the <c>SoundEffect</c> to play (e.g. <c>"Sounds/click"</c>).</param>
/// <param name="Volume">Volume in [0, 1].</param>
/// <param name="Pitch">Pitch in [-1, 1] (octaves).</param>
/// <param name="Pan">Stereo pan in [-1, 1].</param>
public readonly record struct PlaySoundRequest(string SoundKey, float Volume = 1f, float Pitch = 0f, float Pan = 0f);
