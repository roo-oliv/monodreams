#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component;
using MonoDreams.LevelEditor.Serialization;

namespace MonoDreams.Examples.Serialization;

/// <summary>
/// Registers serializers for the <b>reference game's own components</b> on a
/// <see cref="ComponentSerializerRegistry"/> — the game-side half of full component serialization
/// (PS5). The engine ships serializers for its components
/// (<see cref="EngineComponentSerializers.RegisterEngineComponents"/>); a game registers its own
/// through this seam so a native <c>.mdscene</c> migrated from an LDtk/Blender level round-trips the
/// <b>same</b> world the parser + factories would have produced — reconstructed from components, never
/// by re-running factories.
///
/// <para>Call it on BOTH the editor's live registry (so the in-editor Load/Save round-trips these
/// components) AND the shipped game's native-reader registry (so a booted native scene reconstructs
/// them with no editor present). Both call sites live in the reference screen composition.</para>
///
/// <para>What it registers: <c>PlayerState</c> (a tag — the player identity the interaction/orb
/// systems query), <c>OrbitalMotion</c> (the orbit params for the player's orb sub-graph),
/// <c>StopMotionEffect</c> (the Blender character rotation cadence), and <c>DialogueZoneComponent</c>
/// (the NPC dialogue-trigger data). All are plain value components. Bodies route through
/// <see cref="CanonicalJson.SerializeToElement"/> so they obey the same null-omission / invariant-float
/// / sorted-key rules as the rest of the scene file.</para>
///
/// <para><b>Deliberately NOT registered here.</b> <c>NPCInteractionIcon</c> (it holds a live
/// <see cref="Entity"/> reference to a text-icon entity) and the icon's <c>DynamicTextComponent</c>
/// (it holds a live <c>BitmapFont</c> with no asset-key rehydration yet) are <b>runtime affordances</b>
/// derived from an NPC, not authored data — they are rebuilt at play time by
/// <c>NPCInteractionSystem</c>'s collaborators, so they are excluded from the scene the same way a
/// boundary's baked segment colliders are. Serializing an <see cref="Entity"/> handle or a live font is
/// out of scope for PS5 (see the level-editor roadmap: entity-reference serialization + font asset
/// keys).</para>
/// </summary>
public static class GameComponentSerializers
{
    // Stable component-type keys (the strings written to the scene file). Namespaced under "game.".
    public const string PlayerStateKey = "game.PlayerState";
    public const string OrbitalMotionKey = "game.OrbitalMotion";
    public const string StopMotionEffectKey = "game.StopMotionEffect";
    public const string DialogueZoneKey = "game.DialogueZone";

    /// <summary>Registers every reference-game serializer on <paramref name="registry"/>. Call once at
    /// init, after <see cref="EngineComponentSerializers.RegisterEngineComponents"/>.</summary>
    public static ComponentSerializerRegistry RegisterGameComponents(this ComponentSerializerRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        registry.Register(PlayerStateKey, typeof(PlayerState), WritePlayerState, ReadPlayerState);
        registry.Register(OrbitalMotionKey, typeof(OrbitalMotion), WriteOrbitalMotion, ReadOrbitalMotion);
        registry.Register(StopMotionEffectKey, typeof(StopMotionEffect), WriteStopMotionEffect, ReadStopMotionEffect);
        registry.Register(DialogueZoneKey, typeof(DialogueZoneComponent), WriteDialogueZone, ReadDialogueZone);
        return registry;
    }

    // ---- PlayerState (a tag component; no fields yet) ----

    private static JsonElement WritePlayerState(Entity e) => CanonicalJson.SerializeToElement(new { });
    private static void ReadPlayerState(Entity e, JsonElement json) => e.Set(new PlayerState());

    // ---- OrbitalMotion ----

    private sealed class OrbitalMotionDto
    {
        [JsonPropertyName("angle")] public float Angle { get; set; }
        [JsonPropertyName("radius")] public float Radius { get; set; }
        [JsonPropertyName("speed")] public float Speed { get; set; }
        [JsonPropertyName("centerOffset")] public float[] CenterOffset { get; set; } = { 0f, 0f };
    }

    private static JsonElement WriteOrbitalMotion(Entity e)
    {
        var m = e.Get<OrbitalMotion>();
        return CanonicalJson.SerializeToElement(new OrbitalMotionDto
        {
            Angle = m.Angle,
            Radius = m.Radius,
            Speed = m.Speed,
            CenterOffset = new[] { m.CenterOffset.X, m.CenterOffset.Y },
        });
    }

    private static void ReadOrbitalMotion(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<OrbitalMotionDto>()!;
        e.Set(new OrbitalMotion
        {
            Angle = dto.Angle,
            Radius = dto.Radius,
            Speed = dto.Speed,
            CenterOffset = new Vector2(dto.CenterOffset[0], dto.CenterOffset[1]),
        });
    }

    // ---- StopMotionEffect ----

    private sealed class StopMotionEffectDto
    {
        [JsonPropertyName("baseRotation")] public float BaseRotation { get; set; }
        [JsonPropertyName("offsetRadians")] public float OffsetRadians { get; set; }
        [JsonPropertyName("cycleDuration")] public float CycleDuration { get; set; } = 0.5f;
    }

    private static JsonElement WriteStopMotionEffect(Entity e)
    {
        var s = e.Get<StopMotionEffect>();
        return CanonicalJson.SerializeToElement(new StopMotionEffectDto
        {
            BaseRotation = s.BaseRotation,
            OffsetRadians = s.OffsetRadians,
            CycleDuration = s.CycleDuration,
        });
    }

    private static void ReadStopMotionEffect(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<StopMotionEffectDto>()!;
        e.Set(new StopMotionEffect
        {
            BaseRotation = dto.BaseRotation,
            OffsetRadians = dto.OffsetRadians,
            CycleDuration = dto.CycleDuration,
        });
    }

    // ---- DialogueZoneComponent ----

    private sealed class DialogueZoneDto
    {
        [JsonPropertyName("yarnNodeName")] public string? YarnNodeName { get; set; }
        [JsonPropertyName("npcName")] public string? NpcName { get; set; }
        [JsonPropertyName("oneTimeOnly")] public bool OneTimeOnly { get; set; }
        [JsonPropertyName("autoStart")] public bool AutoStart { get; set; }
        [JsonPropertyName("hasBeenTriggered")] public bool HasBeenTriggered { get; set; }
    }

    private static JsonElement WriteDialogueZone(Entity e)
    {
        var z = e.Get<DialogueZoneComponent>();
        return CanonicalJson.SerializeToElement(new DialogueZoneDto
        {
            YarnNodeName = z.YarnNodeName,
            NpcName = z.NpcName,
            OneTimeOnly = z.OneTimeOnly,
            AutoStart = z.AutoStart,
            HasBeenTriggered = z.HasBeenTriggered,
        });
    }

    private static void ReadDialogueZone(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<DialogueZoneDto>()!;
        e.Set(new DialogueZoneComponent(dto.YarnNodeName ?? "", dto.OneTimeOnly, dto.AutoStart, dto.NpcName)
        {
            HasBeenTriggered = dto.HasBeenTriggered,
        });
    }
}
