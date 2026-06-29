#nullable enable
using System;
using System.Text.Json;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// A registered (write, read) serializer pair for one component <see cref="Type"/>, keyed by a
/// stable string. Infrastructure, not a component — it carries behaviour (the read/write
/// delegates) and lives in the level-editor module's serialization service, never on an entity
/// (ECS purity: components stay pure data).
///
/// <list type="bullet">
///   <item><b>Write</b> reads the component off a live <see cref="Entity"/> and returns its
///   serialized fields as a <see cref="JsonElement"/> (a JSON object). The caller has already
///   checked the entity has the component.</item>
///   <item><b>Read</b> takes a previously written <see cref="JsonElement"/> and sets the
///   reconstructed component on a fresh <see cref="Entity"/>.</item>
/// </list>
///
/// The pair must round-trip: <c>Read(Write(e))</c> reproduces the component's serialized state.
/// A serializer persists SOURCE data only — e.g. <c>SpriteInfoComponent</c> writes its
/// <c>AssetKey</c> (never the live <c>Texture2D</c>) and the SOURCE sort fields
/// (<c>LayerDepth</c>/<c>YSortOffset</c>/<c>YSortDepthBias</c>), never the per-frame-derived
/// <c>DrawComponent.LayerDepth</c>.
/// </summary>
public sealed class ComponentSerializer
{
    /// <summary>The stable string key written to the scene file for this component type.</summary>
    public string Key { get; }

    /// <summary>The component CLR type this serializer handles.</summary>
    public Type ComponentType { get; }

    /// <summary>Reads the component off the given entity and returns its serialized fields.</summary>
    public Func<Entity, JsonElement> Write { get; }

    /// <summary>Reconstructs the component from the given JSON and sets it on the entity.</summary>
    public Action<Entity, JsonElement> Read { get; }

    public ComponentSerializer(string key, Type componentType, Func<Entity, JsonElement> write, Action<Entity, JsonElement> read)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        Write = write ?? throw new ArgumentNullException(nameof(write));
        Read = read ?? throw new ArgumentNullException(nameof(read));
    }
}
