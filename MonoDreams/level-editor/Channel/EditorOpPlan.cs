#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonoDreams.Platform;

namespace MonoDreams.LevelEditor.Channel;

/// <summary>
/// A scripted plan of editor operations that drives the real editor systems with <b>no real mouse</b>
/// — the headless editor-op channel (Wave 5, contract item 15). It is the editor analogue of
/// <c>InputReplayPlan</c>: where the input replay scripts keyboard <see cref="MonoDreams.Input.AInputState"/>
/// edges over time, this scripts cursor motion + button edges, the transport controls
/// (<see cref="EditorOpKind.Play"/> / <see cref="EditorOpKind.Pause"/> /
/// <see cref="EditorOpKind.Restart"/>), and toolbar actions, so a <c>GameTestRunner</c>-style test
/// can run select → gizmo-drag → undo → save end-to-end.
///
/// <para>The driver that consumes this plan (<c>EditorOpReplaySystem</c>) <b>holds the session open</b>
/// until the op queue drains, so the editor-op run is not killed early by the input replay's
/// auto-exit-on-drain. Each op carries a <see cref="EditorOp.Frame"/> index; ops are applied when the
/// frame counter reaches that index (multiple ops may share a frame).</para>
/// </summary>
public sealed class EditorOpPlan
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The ops, in ascending <see cref="EditorOp.Frame"/> order (the driver sorts defensively).</summary>
    [JsonPropertyName("ops")]
    public List<EditorOp> Ops { get; set; } = new();

    /// <summary>
    /// Frames to keep running after the last op is applied, so the final edit gets a settle frame (e.g.
    /// HierarchySystem propagating a gizmo move) before the driver requests exit. Default 1.
    /// </summary>
    [JsonPropertyName("tailFrames")]
    public int TailFrames { get; set; } = 1;

    /// <summary>Shared deserialize options (case-insensitive, string enums).</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Attempts to load the editor-op plan from <c>editor_op_plan.json</c> in the given directory
    /// (mirrors <c>InputReplayPlan.TryLoad</c>). Returns <c>null</c> if the file is absent or unparseable
    /// — so a normal run (no editor-op file) is unaffected.
    /// </summary>
    public static EditorOpPlan? TryLoad(string debugDirectory)
    {
        var filePath = PlatformServices.Current.CombinePath(debugDirectory, "editor_op_plan.json");
        if (!PlatformServices.Current.FileExists(filePath)) return null;
        try
        {
            var json = PlatformServices.Current.ReadAllText(filePath);
            return JsonSerializer.Deserialize<EditorOpPlan>(json, Options);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>The kind of a scripted <see cref="EditorOp"/>.</summary>
public enum EditorOpKind
{
    /// <summary>Set the cursor's world + virtual position (no button change).</summary>
    MoveCursor,
    /// <summary>Set the cursor position and assert the left button is <b>down</b> (press edge if it was up).</summary>
    LeftDown,
    /// <summary>Set the cursor position and assert the left button is <b>up</b> (release edge if it was down).</summary>
    LeftUp,
    /// <summary>Transport: resume the game (Playing = <c>RunMode.Play</c>; the shell stays composed).</summary>
    Play,
    /// <summary>Transport: freeze the game and hand the scene to the editing tools (Paused = <c>RunMode.Edit</c>).</summary>
    Pause,
    /// <summary>Transport: rebuild the scene from the original load request and land Paused
    /// (unsaved edits discarded) — requires a transport bound to the driver.</summary>
    Restart,
    /// <summary>Fire a toolbar action by name (Save / Load / Undo / Redo / ToolMove / ToolRotate / ToolScale / ToggleSnap).</summary>
    ToolbarAction,
}

/// <summary>One scripted editor operation, applied on the frame named by <see cref="Frame"/>.</summary>
public sealed class EditorOp
{
    [JsonPropertyName("frame")]
    public int Frame { get; set; }

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EditorOpKind Kind { get; set; }

    /// <summary>Cursor X in world units (for MoveCursor / LeftDown / LeftUp). Also the virtual X.</summary>
    [JsonPropertyName("x")]
    public float X { get; set; }

    /// <summary>Cursor Y in world units (for MoveCursor / LeftDown / LeftUp). Also the virtual Y.</summary>
    [JsonPropertyName("y")]
    public float Y { get; set; }

    /// <summary>The toolbar action name for <see cref="EditorOpKind.ToolbarAction"/> (e.g. "Save").</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }
}
