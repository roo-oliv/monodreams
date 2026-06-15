namespace MonoDreams.Dialogue;

/// <summary>
/// Published by <see cref="DialogueSystem"/> when the running Yarn script reaches a
/// <c>&lt;&lt;command&gt;&gt;</c>. <see cref="Command"/> is the raw command text with the
/// angle brackets stripped (e.g. <c>"emote npc happy"</c>). Game code subscribes to react
/// — trigger emotes, sound effects, set flags — without the dialogue stalling, because
/// <see cref="DialogueSystem"/> auto-advances past the command on the next frame.
/// </summary>
public readonly record struct DialogueCommandMessage(string Command);
