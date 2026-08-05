#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// One trigger-zone type the palette offers (island-authoring plan §5.3) — <b>screen-supplied</b>,
/// exactly like <see cref="PaletteBand"/>: the game screen builds a small list (EvidenceSpot /
/// TalkZone / Exit …) and hands it to the <c>EditorOverlay</c>; the <c>level-editor</c> module never
/// references a game's zone taxonomy.
///
/// <para>Placing a trigger creates a <c>Passive</c> box collider whose <b>identity rides
/// <c>EntityInfoComponent</c></b> — <c>Type</c> = <see cref="Prefix"/> (the category a game system
/// pattern-matches on, e.g. <c>"evidence"</c>) and <c>Name</c> = the auto-numbered instance id
/// (e.g. <c>"evidence_01"</c>, unique in the scene). No new component: the trigger IS a Passive
/// collider + an identity string, exactly what a game reaction system (the <c>ZoneDialogueTriggerSystem</c>
/// precedent) subscribes to collision messages and reads. Renaming is done by editing the saved
/// JSON (banked decision 3 — no free-text widget yet).</para>
/// </summary>
/// <param name="Prefix">The identity category, written to <c>EntityInfoComponent.Type</c> and used
/// to auto-number instances (e.g. <c>"evidence"</c> → <c>"evidence_01"</c>).</param>
/// <param name="Label">The palette button label (e.g. <c>"Evidence"</c>).</param>
/// <param name="Size">The default zone box size in world units (centered on the placement point).</param>
public readonly record struct TriggerType(string Prefix, string Label, Vector2 Size)
{
    /// <summary>The default trigger zone size when a screen does not specify one (a 48×48 world box).</summary>
    public static readonly Vector2 DefaultSize = new(48f, 48f);

    /// <summary>Builds a trigger type with the <see cref="DefaultSize"/> box.</summary>
    public TriggerType(string prefix, string label) : this(prefix, label, DefaultSize) { }

    /// <summary>The collision layers the placed box participates in, or null for the collider's
    /// default (all layers). A game whose contact systems pattern-match on layers should scope its
    /// zones (a solid platform box on the solid layers; a pure MARKER on an empty array — a box
    /// that collides with nothing, selectable in the editor, inert in play).</summary>
    public int[]? ActiveLayers { get; init; }

    /// <summary>An optional game hook invoked on the freshly-built trigger entity — the seam that
    /// attaches game components (a spawn marker, a zone tag) so one palette click authors a fully
    /// functional game object. Runs after the standard stack (EntityInfo + Transform + BoxCollider);
    /// it round-trips only if the components it sets have registered serializers.</summary>
    public Action<Entity>? Configure { get; init; }
}
