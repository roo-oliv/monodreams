#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonoDreams.Platform;

namespace MonoDreams.Debug.Input;

/// <summary>
/// A scripted plan of <b>pointer</b> commands — the mouse-first sibling of
/// <c>InputReplayPlan</c>. Where the input replay speaks a gamepad vocabulary (named actions:
/// Jump, Grab, Interact) and can never say "move to (412, 300) and click", this plan scripts
/// cursor motion, button clicks, the scroll wheel and typed text, and gates the stages on
/// observable conditions (<see cref="PointerCommandKind.WaitUntil"/>) so a scenario never races
/// the game it is driving.
///
/// <para><b>Gating.</b> Same philosophy as the input replay: file-gated (drop
/// <c>pointer_replay.json</c> into the debug directory, which <c>MONODREAMS_DEBUG_DIR</c>
/// relocates), absent file → no driver → a normal run is byte-identical; deterministic
/// (frame-counted, never wall-clock); everything logged through <c>Logger</c>; and the run
/// auto-exits when the plan drains.</para>
///
/// <para><b>Coordinates are authoring space</b> — the virtual-resolution coordinates the game's UI
/// is laid out in, NOT window pixels — so a script survives a window resize or a resolution
/// change. <c>PointerReplaySystem</c> derives world coordinates from them through the screen's
/// camera, exactly as <c>CursorPositionSystem</c> does for a real mouse.</para>
/// </summary>
public sealed class PointerReplayPlan
{
    /// <summary>The file a driver loads from the debug directory.</summary>
    public const string FileName = "pointer_replay.json";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The commands, executed strictly in order (see <see cref="PointerCommand"/>).</summary>
    [JsonPropertyName("commands")]
    public List<PointerCommand> Commands { get; set; } = new();

    /// <summary>
    /// Frames to keep running after the last command completes, so the final click gets settle frames
    /// (hierarchy propagation, a screen transition, one more render) before the driver requests exit.
    /// Default 2.
    /// </summary>
    [JsonPropertyName("tailFrames")]
    public int TailFrames { get; set; } = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        // Enum members are written/read as names (case-insensitively), so a hand-written plan can say
        // "waitUntil" and a serialized one "WaitUntil".
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Attempts to load <c>pointer_replay.json</c> from the given directory (mirrors
    /// <c>InputReplayPlan.TryLoad</c>). Returns <c>null</c> when the file is absent or unparseable —
    /// so a normal run, which has no such file, composes no driver at all.
    /// </summary>
    public static PointerReplayPlan? TryLoad(string debugDirectory)
    {
        var filePath = PlatformServices.Current.CombinePath(debugDirectory, FileName);
        if (!PlatformServices.Current.FileExists(filePath)) return null;

        try
        {
            return JsonSerializer.Deserialize<PointerReplayPlan>(
                PlatformServices.Current.ReadAllText(filePath), Options);
        }
        catch (Exception ex)
        {
            MonoDreams.State.Logger.Error($"[pointer] Failed to load {FileName}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>The kind of a scripted <see cref="PointerCommand"/>.</summary>
public enum PointerCommandKind
{
    /// <summary>Move the pointer to (<c>x</c>, <c>y</c>) in authoring space. One frame.</summary>
    Move,

    /// <summary>
    /// Press and release <c>button</c> (default left), optionally moving to (<c>x</c>, <c>y</c>) first.
    /// Holds the button down for <c>hold</c> frames (default 1) before releasing, so a consumer acting
    /// on the press edge AND one acting on the release edge both observe it. Takes <c>hold</c> + 1 frames.
    /// </summary>
    Click,

    /// <summary>
    /// Pulse the scroll wheel by <c>delta</c> raw wheel units for one frame (MonoGame reports 120 per
    /// detent, which is what consumers like <c>CameraNavSystem</c> divide by). One frame.
    /// </summary>
    Wheel,

    /// <summary>
    /// Type <c>text</c> as synthesized key presses, one character every two frames (press frame, gap
    /// frame) so an edge-triggered reader sees each character — including repeats. Reaches any system
    /// wired to the driver's keyboard seam (e.g. the <c>ui</c> module's <c>TextInputSystem</c>).
    /// Supported characters are the ones a <c>Keys</c> value can carry: <c>a-z</c>, <c>0-9</c> and
    /// space; anything else is logged and skipped.
    /// </summary>
    Type,

    /// <summary>
    /// Block until an observable condition holds — the command that keeps a script from racing the
    /// game ("don't click Submit until the dialog exists"). Exactly one predicate per command:
    /// <c>entity</c> (an entity whose <c>EntityInfoComponent</c> type or name matches exists),
    /// <c>log</c> (a log line containing this substring has been written since the driver started),
    /// or <c>frames</c> (that many frames elapse). Gives up after <c>timeoutFrames</c> with an
    /// <c>ERROR</c> line and moves on, so a stuck script still ends the run instead of hanging it.
    /// </summary>
    WaitUntil,

    /// <summary>
    /// Write <c>text</c> to the log as a stage marker, so screenshots and log lines correlate per
    /// stage. One frame.
    /// </summary>
    Label,
}

/// <summary>Which mouse button a <see cref="PointerCommandKind.Click"/> drives.</summary>
public enum PointerButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// One scripted pointer command. Deliberately flat (like <c>InputReplayCommand</c> and
/// <c>EditorOp</c>): the fields a kind ignores are simply absent from its JSON.
/// </summary>
public sealed class PointerCommand
{
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PointerCommandKind Kind { get; set; }

    /// <summary>Target X in authoring (virtual-resolution) space — <c>move</c> and <c>click</c>.</summary>
    [JsonPropertyName("x")]
    public float? X { get; set; }

    /// <summary>Target Y in authoring (virtual-resolution) space — <c>move</c> and <c>click</c>.</summary>
    [JsonPropertyName("y")]
    public float? Y { get; set; }

    /// <summary>Which button a <c>click</c> drives. Default left.</summary>
    [JsonPropertyName("button")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PointerButton Button { get; set; } = PointerButton.Left;

    /// <summary>Frames a <c>click</c> holds the button down before releasing. Default 1, minimum 1.</summary>
    [JsonPropertyName("hold")]
    public int Hold { get; set; } = 1;

    /// <summary>Raw scroll-wheel units for a <c>wheel</c> command (120 per detent).</summary>
    [JsonPropertyName("delta")]
    public int Delta { get; set; }

    /// <summary>The characters to <c>type</c>, or the marker a <c>label</c> writes.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary><c>waitUntil</c> predicate: an entity whose <c>EntityInfoComponent</c> type or name
    /// equals this exists in the world.</summary>
    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    /// <summary><c>waitUntil</c> predicate: a log line containing this substring has been written
    /// since the driver was constructed (case-insensitive).</summary>
    [JsonPropertyName("log")]
    public string? Log { get; set; }

    /// <summary><c>waitUntil</c> predicate: this many frames elapse.</summary>
    [JsonPropertyName("frames")]
    public int? Frames { get; set; }

    /// <summary>Frames a <c>waitUntil</c> waits before giving up (logging an error and moving on).
    /// Default 600 — ten seconds at 60 fps.</summary>
    [JsonPropertyName("timeoutFrames")]
    public int TimeoutFrames { get; set; } = 600;
}
