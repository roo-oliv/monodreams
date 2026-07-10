#nullable enable
using System;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The <b>one-way importer</b> that turns a world produced by a legacy loader (the LDtk
/// parser, now <b>import-only</b> — PS5) into a native <c>.mdscene</c> the game then owns and boots.
/// It runs the parser <b>once</b> against a source level, then captures the resulting world: it tags
/// every scene-content root with <see cref="SceneObjectComponent"/> (the save-root marker the writer's
/// membership closure keys on) and hands the world to the canonical <see cref="SceneWriter"/>, which
/// serializes each tagged root plus its <c>ChildOf</c> closure into byte-stable native JSON.
///
/// <para><b>Why tag on import.</b> The LDtk parser never sets <see cref="SceneObjectComponent"/>
/// (it is transient editor state), so a freshly parsed world has zero save-roots and a straight
/// <see cref="SceneWriter.Save"/> would write an empty scene. The importer promotes the parsed content
/// to save-roots so the closure — and thus the serialized scene — matches what the parser produced.
/// Reconstruction on load is by components through the registry, never by re-running the parser: this
/// is exactly why every component a factory/parser sets needs a registered serializer (engine ones via
/// <see cref="EngineComponentSerializers.RegisterEngineComponents"/>; the game's own via its
/// registration seam), and why the reference factories set <c>SpriteInfoComponent.AssetKey</c> so the
/// texture re-loads by content key on the native reader.</para>
///
/// <para><b>What counts as content.</b> A content root is a top-level entity (no live <c>ChildOf</c>
/// parent) that is neither editor infrastructure (<see cref="EditorInfrastructureComponent"/> — a
/// gizmo/panel/chrome/cursor entity) nor a bake product (<see cref="BakedProductComponent"/> — e.g. a
/// boundary's segment colliders, which the writer already excludes and which regenerate on load).
/// Runtime-derived affordances that hold unserializable handles (a live <see cref="Entity"/> reference
/// or a live font) are excluded the same way — mark them <see cref="BakedProductComponent"/> so they
/// are rebuilt at play time rather than baked into the file.</para>
///
/// <para>It is infrastructure, not a component (ECS purity): a one-shot, dev/editor-time operation that
/// never runs per frame. An editor toolbar "Import LDtk level" action and a headless
/// dev/export op both drive this same core.</para>
/// </summary>
public sealed class LevelImporter
{
    private readonly SceneWriter _writer;

    public LevelImporter(SceneWriter writer)
        => _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <summary>
    /// Tags every scene-content root in <paramref name="world"/> with <see cref="SceneObjectComponent"/>
    /// so the writer's membership closure captures it and its <c>ChildOf</c> descendants. A root is a
    /// top-level entity (no live parent) that is not editor infrastructure and not a bake product.
    /// Returns the number of roots newly tagged (idempotent — an already-tagged root is left as is).
    /// </summary>
    public static int TagContentRoots(World world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));

        var tagged = 0;
        // Top-level entities only: a ChildOf descendant is pulled into its ancestor's closure by the
        // writer, so tagging it too would be redundant (the writer dedups) — and a child of an
        // excluded root should stay excluded, not become an independent save-root.
        using var roots = world.GetEntities().Without<ChildOfComponent>().AsSet();
        foreach (var e in roots.GetEntities())
        {
            if (e.Has<EditorInfrastructureComponent>()) continue; // gizmo/panel/chrome/cursor
            if (e.Has<BakedProductComponent>()) continue;         // regenerated on load, never serialized
            if (e.Has<SceneObjectComponent>()) continue;          // already a save-root
            e.Set(new SceneObjectComponent());
            tagged++;
        }

        return tagged;
    }

    /// <summary>
    /// Tags the content (see <see cref="TagContentRoots"/>) and builds the native <see cref="SceneData"/>
    /// for <paramref name="world"/> through the canonical <see cref="SceneWriter"/> (stable ids +
    /// deterministic ordering). <paramref name="camera"/> / <paramref name="layers"/> are optional
    /// scene metadata (camera state, layer banding).
    /// </summary>
    public SceneData Import(World world, Camera? camera = null, DrawLayerMap? layers = null)
    {
        TagContentRoots(world);
        return _writer.BuildScene(world, camera, layers);
    }

    /// <summary>Imports (<see cref="Import"/>) and canonical-serializes to a byte-stable JSON string —
    /// the native <c>.mdscene</c> bytes.</summary>
    public string ImportToJson(World world, Camera? camera = null, DrawLayerMap? layers = null)
        => CanonicalJson.Serialize(Import(world, camera, layers));

    /// <summary>
    /// Imports and writes the native scene to <paramref name="filePath"/> through
    /// <see cref="SceneWriter.Save"/> (into the project source tree, via <c>IPlatformServices</c>).
    /// Returns the path written, or <c>null</c> when refused (null/empty path). Used by the headless
    /// export op that produces the committed migrated levels.
    /// </summary>
    public string? ImportToFile(World world, string? filePath, Camera? camera = null, DrawLayerMap? layers = null)
    {
        TagContentRoots(world);
        return _writer.Save(world, filePath, camera, layers);
    }
}
