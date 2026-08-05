#nullable enable
using System;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.EntityFactory;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.EntityFactory;

/// <summary>
/// The generic prefab spawn factory: the "of course, via code" half of the prefab model. Registered
/// on <c>EntitySpawnSystem</c> under the <see cref="IdentifierPrefix"/> convention (a PREFIX channel,
/// not an exact identifier), so game code — or any parser — spawns a full linked prefab instance
/// through the existing spawn plumbing:
///
/// <code>world.Publish(new EntitySpawnRequest("prefab:npc-boldo", worldPosition));</code>
///
/// <para>It reconstructs the instance through the SAME <see cref="PrefabExpander"/> the scene reader
/// and live propagation use (ONE expansion implementation): the root + prefab-owned children, textures
/// rehydrated, the transient <c>DrawComponent</c> restored, the <see cref="PrefabInstanceComponent"/>
/// marker stamped. It then places the root at the request's position and tags it
/// <see cref="SceneObjectComponent"/> so a spawned instance is a first-class, savable scene object in
/// the editor (a harmless transient tag in a shipped game).</para>
///
/// <para><b>Unknown id → warn-and-drop.</b> An identifier without the prefix, an empty id, or a prefab
/// that does not resolve logs a <see cref="Logger.Warning(string)"/> and drops the spawn — the factory's
/// loud-warning convention (mirrors level-loading's "Unregistered factory identifiers log a warning and
/// silently drop the spawn"). The scene reader, by contrast, fails LOUD on a missing prefab (a file it
/// cannot honor aborts the load); the factory's channel is fire-and-forget, so it warns instead.</para>
/// </summary>
public sealed class PrefabFactory : IEntityFactory
{
    /// <summary>The identifier prefix this factory is registered under. A request identifier
    /// <c>"prefab:&lt;id&gt;"</c> spawns an instance of prefab <c>&lt;id&gt;</c>.</summary>
    public const string IdentifierPrefix = "prefab:";

    private readonly PrefabExpander _expander;

    public PrefabFactory(PrefabExpander expander) =>
        _expander = expander ?? throw new ArgumentNullException(nameof(expander));

    /// <summary>Builds the full identifier for a prefab id (<c>"prefab:" + id</c>) — the string a caller
    /// publishes on an <see cref="EntitySpawnRequest"/>.</summary>
    public static string Identifier(string prefabId) => IdentifierPrefix + prefabId;

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var identifier = request.Identifier ?? "";
        if (!identifier.StartsWith(IdentifierPrefix, StringComparison.Ordinal))
        {
            Logger.Warning($"[level-editor] PrefabFactory received identifier '{identifier}' without the " +
                           $"'{IdentifierPrefix}' prefix; dropping the spawn.");
            return default;
        }

        var prefabId = identifier.Substring(IdentifierPrefix.Length);
        if (string.IsNullOrEmpty(prefabId))
        {
            Logger.Warning($"[level-editor] PrefabFactory received an empty prefab id ('{identifier}'); " +
                           "dropping the spawn.");
            return default;
        }

        Entity root;
        try
        {
            root = _expander.Instantiate(world, prefabId);
        }
        catch (Exception ex)
        {
            // Unknown / malformed / cyclic prefab: the expander fails loud; the factory channel
            // warns-and-drops (its convention) rather than tearing down the caller.
            Logger.Warning($"[level-editor] PrefabFactory could not spawn prefab '{prefabId}': {ex.Message}");
            return default;
        }

        // Place the instance root at the requested world position. Transform.Position is local, and the
        // prefab root's is normalized to origin, so this is the instance's placement.
        if (root.Has<TransformComponent>())
            root.Get<TransformComponent>().Position = request.Position;
        else
            root.Set(new TransformComponent(request.Position));

        // A spawned instance is a first-class scene object (savable in the editor; inert at runtime).
        root.Set(new SceneObjectComponent());
        return root;
    }
}
