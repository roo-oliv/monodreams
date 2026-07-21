using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Collision;
using MonoDreams.Message;
using MonoDreams.Platform;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Collision-message classifier for the island Slice-3 collision tests, keyed on IDENTITY (the
/// <c>ZoneDialogueTriggerSystem</c> / <c>GameCollisionHelper</c> pattern) — NOT on the
/// <c>Passive</c> flag. In this engine <c>Passive</c> means "static / does not initiate a
/// collision" (the WallEntityFactory idiom): coastline segments, building footprints, AND trigger
/// zones are all passive static geometry, so the flag cannot tell a blocker from a sensor. The
/// game decides that by the target's <see cref="EntityInfoComponent"/> category: a known trigger
/// prefix (evidence / talkzone / exit) is a non-blocking sensor
/// (<see cref="CollisionType.Generic"/> — the physical resolver ignores it but the raw message
/// still fires with the identity); everything else is solid (<see cref="CollisionType.Physics"/>
/// — the resolver pushes the active player out).
/// </summary>
internal static class MilestoneCollision
{
    private static readonly HashSet<string> TriggerPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "evidence", "talkzone", "exit",
    };

    public static CollisionMessage Create(Entity colliderA, Entity colliderB, Entity bodyA, Entity bodyB,
        Vector2 contactPoint, Vector2 contactNormal, float contactTime, float penetrationDepth, int layer)
    {
        // Trigger-vs-solid identity lives on the COLLIDER entity (a trigger/zone is its own collider).
        var type = IsTrigger(colliderB) ? CollisionType.Generic : CollisionType.Physics;
        return new CollisionMessage(colliderA, colliderB, bodyA, bodyB, contactPoint, contactNormal, contactTime, penetrationDepth, layer, type);
    }

    private static bool IsTrigger(Entity e) =>
        e.IsAlive && e.Has<MonoDreams.Component.EntityInfoComponent>()
        && TriggerPrefixes.Contains(e.Get<MonoDreams.Component.EntityInfoComponent>().Type ?? "");
}

/// <summary>
/// In-memory <see cref="IPlatformServices"/> for the round-trip tests: <see cref="ExportScene"/>
/// stores JSON into a dictionary that <see cref="ReadAllText"/> serves back, so the writer→reader
/// file hop is a real serialize/deserialize with no disk.
/// </summary>
internal sealed class InMemoryPlatform : IPlatformServices
{
    public Dictionary<string, string> Files { get; } = new();
    public StringWriter LogWriter { get; } = new();
    public string BaseDirectory => "/island/";
    public string GetEnvironmentVariable(string name) => null;
    public string CombinePath(params string[] paths) => string.Join("/", paths);
    public bool FileExists(string path) => Files.ContainsKey(path);
    public string ReadAllText(string path) =>
        Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
    public void WriteAllText(string path, string contents) => Files[path] = contents;
    public void WriteAllBytes(string path, byte[] bytes) { }
    public string ExportScene(string suggestedFileName, string contents)
    {
        Files[suggestedFileName] = contents;
        return suggestedFileName;
    }
    public void CreateDirectory(string path) { }
    public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
    public void WriteLineToConsole(string line) { }
    public void RunBackground(Action work) => work();
}
