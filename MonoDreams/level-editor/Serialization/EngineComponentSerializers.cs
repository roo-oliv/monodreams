#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// Registers the serializers the engine ships for its own serializable components. Centralized
/// here (rather than colocated with each component) because this module already depends on
/// foundation / rendering / collision / physics, so it can see all the engine components in one
/// place; the trade-off is a slight modularity cost (a new serializable engine component must add
/// its serializer here). Registration is explicit and greppable, called once at module/screen init.
///
/// <para>What it registers, and the deliberate exclusions:</para>
/// <list type="bullet">
///   <item><c>TransformComponent</c> — position / rotation / scale / origin (not the cached world
///   matrix, which is derived).</item>
///   <item><c>SpriteInfoComponent</c> — <c>AssetKey</c> (never the live <c>Texture2D</c>),
///   Source / Size / Color / Origin / Offset / Target, the mirror flags
///   <c>FlipHorizontally</c> / <c>FlipVertically</c> (written only when true, so pre-flip scenes stay
///   byte-identical), and the SOURCE sort fields
///   <c>LayerDepth</c> / <c>YSortOffset</c> / <c>YSortDepthBias</c>. Never the per-frame-derived
///   <c>DrawComponent.LayerDepth</c>.</item>
///   <item><c>SpriteAnimationComponent</c> — the AUTHORED clip only: the frame list (each frame's
///   <c>assetKey</c> — omitted when null, i.e. an atlas animation that keeps the sprite's texture —
///   <c>source</c> and <c>duration</c>), <c>defaultFrameDuration</c>, <c>loop</c>, <c>playing</c> and
///   <c>speed</c>. Never the RUNTIME playback state <c>Time</c> / <c>FrameIndex</c>, which
///   <c>SpriteAnimationSystem</c> rewrites every frame: persisting them would bake one arbitrary
///   playback moment into the file and break the <c>load → save</c> byte fixed point. A loaded clip
///   therefore always starts from frame 0.</item>
///   <item><c>EntityInfoComponent</c> — type + name.</item>
///   <item><c>BoxColliderComponent</c> / <c>ConvexColliderComponent</c> — collider shape + layers +
///   flags (world vertices and broad-phase AABB are derived and recomputed by detection).</item>
///   <item><c>RigidBodyComponent</c> / <c>VelocityComponent</c> — physics source state.</item>
///   <item><c>CameraFollowTargetComponent</c> — camera-follow tuning (damping / max-distance /
///   active) plus the optional world-space clamp <c>Bounds</c> (omitted when the camera follows
///   freely). A player/NPC entity set by the reference factories carries it, so a migrated native
///   scene reconstructs its camera-follow behaviour without re-running the factory.</item>
///   <item><c>CameraComponent</c> — the scene camera marker + zoom (CM). Position/rotation come from the
///   entity's Transform; the camera entity is an ordinary <c>SceneObjectComponent</c> root, so it saves
///   in <c>entities[]</c> like everything else. Exactly one per scene.</item>
///   <item><c>SceneLayerComponent</c> — the designer's scene layer (order / visible / locked /
///   screenSpace). The layer's NAME is its <c>EntityInfoComponent</c>, its members are its
///   <c>ChildOf</c> children, and its members' final draw depths are derived per frame by
///   <c>SceneLayerSystem</c> — so only the authored layer fields persist.</item>
///   <item><c>ChildOfComponent</c> — registered as the structural parent link (captured as
///   <see cref="SceneEntityData.Parent"/>, not a component body).</item>
/// </list>
///
/// <para>Deliberately NOT registered (opt-out): <c>VisibleComponent</c> and <c>ColliderTagComponent</c>
/// (engine tags re-derived by <c>CullingSystem</c> / detection), and <c>DrawComponent</c> (its sprite
/// fields are re-prepped from <c>SpriteInfoComponent</c> and its <c>LayerDepth</c> is per-frame-derived;
/// straightforward mesh-Draw parameters can be added in a later wave when the editor authors meshes).</para>
/// </summary>
public static class EngineComponentSerializers
{
    // Stable component-type keys (the strings written to the scene file). Kept short + namespaced.
    public const string TransformKey = "core.Transform";
    public const string SpriteInfoKey = "core.SpriteInfo";
    public const string SpriteAnimationKey = "core.SpriteAnimation";
    public const string EntityInfoKey = "core.EntityInfo";
    public const string BoxColliderKey = "core.BoxCollider";
    public const string ConvexColliderKey = "core.ConvexCollider";
    public const string RigidBodyKey = "core.RigidBody";
    public const string VelocityKey = "core.Velocity";
    public const string CameraFollowTargetKey = "core.CameraFollowTarget";
    public const string CameraKey = "core.Camera";
    public const string ChildOfKey = "core.ChildOf";
    public const string BoundaryKey = "core.Boundary";
    public const string SceneLayerKey = "core.SceneLayer";

    /// <summary>Registers every engine serializer on <paramref name="registry"/>. Call once at init.</summary>
    public static void RegisterEngineComponents(this ComponentSerializerRegistry registry)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));

        registry.Register(TransformKey, typeof(TransformComponent), WriteTransform, ReadTransform);
        registry.Register(SpriteInfoKey, typeof(SpriteInfoComponent), WriteSpriteInfo, ReadSpriteInfo);
        registry.Register(SpriteAnimationKey, typeof(SpriteAnimationComponent), WriteSpriteAnimation, ReadSpriteAnimation);
        registry.Register(EntityInfoKey, typeof(EntityInfoComponent), WriteEntityInfo, ReadEntityInfo);
        registry.Register(BoxColliderKey, typeof(BoxColliderComponent), WriteBoxCollider, ReadBoxCollider);
        registry.Register(ConvexColliderKey, typeof(ConvexColliderComponent), WriteConvexCollider, ReadConvexCollider);
        registry.Register(RigidBodyKey, typeof(RigidBodyComponent), WriteRigidBody, ReadRigidBody);
        registry.Register(VelocityKey, typeof(VelocityComponent), WriteVelocity, ReadVelocity);
        registry.Register(CameraFollowTargetKey, typeof(CameraFollowTargetComponent), WriteCameraFollowTarget, ReadCameraFollowTarget);
        registry.Register(CameraKey, typeof(CameraComponent), WriteCamera, ReadCamera);
        registry.Register(BoundaryKey, typeof(LevelEditor.Component.BoundaryComponent), WriteBoundary, ReadBoundary);
        registry.Register(SceneLayerKey, typeof(MonoDreams.Component.Level.SceneLayerComponent), WriteSceneLayer, ReadSceneLayer);

        // The structural parent link is captured as SceneEntityData.Parent, not a component body —
        // register it so a parented entity never trips the unregistered-component warning.
        registry.Register(ChildOfKey, typeof(ChildOfComponent), WriteChildOfStub, ReadChildOfStub);
        registry.RegisterStructuralParentLink<ChildOfComponent>();

        // The persisted stable scene-local id is captured as SceneEntityData.Id (a dedicated
        // structural field, like the parent link), never a component body — mark it so a stamped root
        // is silently skipped by the component discoverer rather than tripping the warning.
        registry.MarkStructurallyCaptured<LevelEditor.Component.SceneEntityIdComponent>();

        // The linked-instance marker is captured as SceneEntityData.Prefab (the compact entry's `prefab`
        // field), never a component body — mark it structurally-captured so a stamped instance root is
        // silently skipped by the component discoverer (no unregistered warning) and hidden by the
        // editable Inspector (never an addable/removable row).
        registry.MarkStructurallyCaptured<LevelEditor.Component.PrefabInstanceComponent>();
    }

    // ---- TransformComponent (source spatial state; world matrix is derived) ----

    private sealed class TransformDto
    {
        [JsonPropertyName("position")] public float[] Position { get; set; } = { 0f, 0f };
        [JsonPropertyName("rotation")] public float Rotation { get; set; }
        [JsonPropertyName("scale")] public float[] Scale { get; set; } = { 1f, 1f };
        [JsonPropertyName("origin")] public float[] Origin { get; set; } = { 0f, 0f };
    }

    private static JsonElement WriteTransform(Entity e)
    {
        var t = e.Get<TransformComponent>();
        return CanonicalJson.SerializeToElement(new TransformDto
        {
            Position = Vec(t.Position),
            Rotation = t.Rotation,
            Scale = Vec(t.Scale),
            Origin = Vec(t.Origin),
        });
    }

    private static void ReadTransform(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<TransformDto>()!;
        e.Set(new TransformComponent(ToVec(dto.Position), dto.Rotation, ToVec(dto.Scale), ToVec(dto.Origin)));
    }

    // ---- SpriteInfoComponent (AssetKey, NOT the live Texture2D; SOURCE sort fields) ----

    private sealed class SpriteInfoDto
    {
        [JsonPropertyName("assetKey")] public string? AssetKey { get; set; }
        [JsonPropertyName("source")] public int[] Source { get; set; } = { 0, 0, 0, 0 };
        [JsonPropertyName("size")] public float[] Size { get; set; } = { 0f, 0f };
        [JsonPropertyName("color")] public byte[] Color { get; set; } = { 255, 255, 255, 255 };
        [JsonPropertyName("origin")] public float[] Origin { get; set; } = { 0f, 0f };
        [JsonPropertyName("offset")] public float[] Offset { get; set; } = { 0f, 0f };
        [JsonPropertyName("target")] public RenderTargetID Target { get; set; }
        // Mirror flags (rendering — "Sprite facing/orientation is a flip flag, not mirrored art"). Nullable
        // and written ONLY when true (CanonicalJson omits nulls): every pre-flip `.mdscene` — including the
        // committed reference levels the byte-level fixed-point tests pin — stays byte-identical, and an
        // absent key reads back as false.
        [JsonPropertyName("flipHorizontally")] public bool? FlipHorizontally { get; set; }
        [JsonPropertyName("flipVertically")] public bool? FlipVertically { get; set; }
        // SOURCE sort fields — never the per-frame-derived DrawComponent.LayerDepth.
        [JsonPropertyName("layerDepth")] public float LayerDepth { get; set; }
        [JsonPropertyName("ySortOffset")] public float YSortOffset { get; set; }
        [JsonPropertyName("ySortDepthBias")] public float YSortDepthBias { get; set; }
    }

    private static JsonElement WriteSpriteInfo(Entity e)
    {
        var s = e.Get<SpriteInfoComponent>();
        return CanonicalJson.SerializeToElement(new SpriteInfoDto
        {
            AssetKey = s.AssetKey, // the content key, never the live Texture2D
            Source = Rect(s.Source),
            Size = Vec(s.Size),
            Color = Col(s.Color),
            Origin = Vec(s.Origin),
            Offset = Vec(s.Offset),
            Target = s.Target,
            FlipHorizontally = s.FlipHorizontally ? true : null, // omit-when-false: keeps pre-flip scenes byte-identical
            FlipVertically = s.FlipVertically ? true : null,
            LayerDepth = s.LayerDepth,
            YSortOffset = s.YSortOffset,
            YSortDepthBias = s.YSortDepthBias,
        });
    }

    private static void ReadSpriteInfo(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<SpriteInfoDto>()!;
        // SpriteSheet stays null here: Wave 3's reader rehydrates it from AssetKey via ContentManager.Load.
        e.Set(new SpriteInfoComponent
        {
            AssetKey = dto.AssetKey,
            Source = ToRect(dto.Source),
            Size = ToVec(dto.Size),
            Color = ToCol(dto.Color),
            Origin = ToVec(dto.Origin),
            Offset = ToVec(dto.Offset),
            Target = dto.Target,
            FlipHorizontally = dto.FlipHorizontally ?? false, // absent key ⇒ unflipped
            FlipVertically = dto.FlipVertically ?? false,
            LayerDepth = dto.LayerDepth,
            YSortOffset = dto.YSortOffset,
            YSortDepthBias = dto.YSortDepthBias,
        });
    }

    // ---- SpriteAnimationComponent (the AUTHORED clip; never the runtime Time / FrameIndex) ----
    // SpriteAnimationSystem rewrites Time and FrameIndex every frame from the elapsed game time, so
    // they are derived playback state, exactly like DrawComponent.LayerDepth: writing them would bake
    // one arbitrary playback moment into the file and make `load → save` depend on WHEN the save
    // happened. Persisting only the authored clip keeps the byte fixed point and starts a loaded
    // animation from frame 0.

    private sealed class SpriteAnimationFrameDto
    {
        // Null (an atlas animation that keeps the sprite's current texture) is omitted by CanonicalJson.
        [JsonPropertyName("assetKey")] public string? AssetKey { get; set; }
        [JsonPropertyName("source")] public int[] Source { get; set; } = { 0, 0, 0, 0 };
        [JsonPropertyName("duration")] public float Duration { get; set; }
    }

    private sealed class SpriteAnimationDto
    {
        [JsonPropertyName("frames")] public SpriteAnimationFrameDto[] Frames { get; set; } = Array.Empty<SpriteAnimationFrameDto>();
        [JsonPropertyName("defaultFrameDuration")] public float DefaultFrameDuration { get; set; } = 0.12f;
        [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
        [JsonPropertyName("playing")] public bool Playing { get; set; } = true;
        [JsonPropertyName("speed")] public float Speed { get; set; } = 1f;
    }

    private static JsonElement WriteSpriteAnimation(Entity e)
    {
        var a = e.Get<SpriteAnimationComponent>();
        var frames = a.Frames ?? Array.Empty<SpriteAnimationFrame>();
        return CanonicalJson.SerializeToElement(new SpriteAnimationDto
        {
            Frames = frames.Select(f => new SpriteAnimationFrameDto
            {
                AssetKey = f.AssetKey, // the content key, never a live Texture2D
                Source = Rect(f.Source),
                Duration = f.Duration,
            }).ToArray(),
            DefaultFrameDuration = a.DefaultFrameDuration,
            Loop = a.Loop,
            Playing = a.Playing,
            Speed = a.Speed,
            // Time / FrameIndex deliberately absent — runtime playback state, see the note above.
        });
    }

    private static void ReadSpriteAnimation(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<SpriteAnimationDto>()!;
        // Time = 0 and FrameIndex = -1 come from the component's field initializers, so a loaded clip
        // starts from frame 0 (the system applies it on first sight).
        e.Set(new SpriteAnimationComponent
        {
            Frames = (dto.Frames ?? Array.Empty<SpriteAnimationFrameDto>()).Select(f => new SpriteAnimationFrame
            {
                AssetKey = f.AssetKey,
                Source = f.Source is { Length: 4 } s ? ToRect(s) : Rectangle.Empty,
                Duration = f.Duration,
            }).ToArray(),
            DefaultFrameDuration = dto.DefaultFrameDuration,
            Loop = dto.Loop,
            Playing = dto.Playing,
            Speed = dto.Speed,
        });
    }

    // ---- EntityInfoComponent ----

    private sealed class EntityInfoDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private static JsonElement WriteEntityInfo(Entity e)
    {
        var info = e.Get<EntityInfoComponent>();
        return CanonicalJson.SerializeToElement(new EntityInfoDto { Type = info.Type, Name = info.Name });
    }

    private static void ReadEntityInfo(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<EntityInfoDto>()!;
        e.Set(new EntityInfoComponent(dto.Type ?? "", dto.Name));
    }

    // ---- BoxColliderComponent (centered size + layers + flags; world rect derived from Transform) ----
    // The on-disk field is "size" (float[2]); the pose (former Bounds.Location offset) lives on the
    // collider entity's Transform. The reader is deliberately STRICT — it reads only "size", with NO
    // legacy "bounds" tolerance, so a version-1 collider can never silently half-load. The gate that
    // stops a version-1 collider file is SceneVersionGuard (fail-loud on file read); the committed v1
    // content was rewritten to version 2 by `monodreams migrate-colliders` (ColliderMigration).

    private sealed class BoxColliderDto
    {
        [JsonPropertyName("size")] public float[] Size { get; set; } = { 0f, 0f };
        [JsonPropertyName("activeLayers")] public int[] ActiveLayers { get; set; } = { -1 };
        [JsonPropertyName("passive")] public bool Passive { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    }

    private static JsonElement WriteBoxCollider(Entity e)
    {
        var c = e.Get<BoxColliderComponent>();
        return CanonicalJson.SerializeToElement(new BoxColliderDto
        {
            Size = Vec(c.Size),
            ActiveLayers = SortedLayers(c.ActiveLayers), // a HashSet has no stable order — sort for byte-stable output
            Passive = c.Passive,
            Enabled = c.Enabled,
        });
    }

    private static void ReadBoxCollider(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<BoxColliderDto>()!;
        e.Set(new BoxColliderComponent(ToVec(dto.Size), new HashSet<int>(dto.ActiveLayers), dto.Passive, dto.Enabled));
    }

    // ---- ConvexColliderComponent (model vertices + layers + flags; world verts/AABB derived) ----

    private sealed class ConvexColliderDto
    {
        [JsonPropertyName("modelVertices")] public float[][] ModelVertices { get; set; } = Array.Empty<float[]>();
        [JsonPropertyName("activeLayers")] public int[] ActiveLayers { get; set; } = { -1 };
        [JsonPropertyName("passive")] public bool Passive { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("ignoreTransformRotation")] public bool IgnoreTransformRotation { get; set; }
    }

    private static JsonElement WriteConvexCollider(Entity e)
    {
        var c = e.Get<ConvexColliderComponent>();
        return CanonicalJson.SerializeToElement(new ConvexColliderDto
        {
            ModelVertices = c.ModelVertices.Select(Vec).ToArray(),
            ActiveLayers = SortedLayers(c.ActiveLayers), // a HashSet has no stable order — sort for byte-stable output
            Passive = c.Passive,
            Enabled = c.Enabled,
            IgnoreTransformRotation = c.IgnoreTransformRotation,
        });
    }

    private static void ReadConvexCollider(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<ConvexColliderDto>()!;
        var verts = dto.ModelVertices.Select(ToVec).ToArray();
        e.Set(new ConvexColliderComponent(verts, new HashSet<int>(dto.ActiveLayers), dto.Passive, dto.Enabled, dto.IgnoreTransformRotation));
    }

    // ---- RigidBodyComponent ----

    private sealed class RigidBodyDto
    {
        [JsonPropertyName("mass")] public float Mass { get; set; } = 1f;
        [JsonPropertyName("gravityActive")] public bool GravityActive { get; set; } = true;
        [JsonPropertyName("gravityFactor")] public float GravityFactor { get; set; } = 1f;
        [JsonPropertyName("isKinematic")] public bool IsKinematic { get; set; }
        [JsonPropertyName("freezeRotation")] public bool FreezeRotation { get; set; }
        [JsonPropertyName("freezePositionX")] public bool FreezePositionX { get; set; }
        [JsonPropertyName("freezePositionY")] public bool FreezePositionY { get; set; }
    }

    private static JsonElement WriteRigidBody(Entity e)
    {
        var r = e.Get<RigidBodyComponent>();
        return CanonicalJson.SerializeToElement(new RigidBodyDto
        {
            Mass = r.Mass,
            GravityActive = r.Gravity.active,
            GravityFactor = r.Gravity.factor,
            IsKinematic = r.IsKinematic,
            FreezeRotation = r.FreezeRotation,
            FreezePositionX = r.FreezePositionX,
            FreezePositionY = r.FreezePositionY,
        });
    }

    private static void ReadRigidBody(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<RigidBodyDto>()!;
        var r = new RigidBodyComponent(dto.Mass, dto.IsKinematic, dto.FreezeRotation, gravityActive: dto.GravityActive, gravityScale: dto.GravityFactor)
        {
            FreezeRotation = dto.FreezeRotation,
            FreezePositionX = dto.FreezePositionX,
            FreezePositionY = dto.FreezePositionY,
        };
        e.Set(r);
    }

    // ---- VelocityComponent ----

    private sealed class VelocityDto
    {
        [JsonPropertyName("current")] public float[] Current { get; set; } = { 0f, 0f };
        [JsonPropertyName("last")] public float[] Last { get; set; } = { 0f, 0f };
    }

    private static JsonElement WriteVelocity(Entity e)
    {
        var v = e.Get<VelocityComponent>();
        return CanonicalJson.SerializeToElement(new VelocityDto { Current = Vec(v.Current), Last = Vec(v.Last) });
    }

    private static void ReadVelocity(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<VelocityDto>()!;
        e.Set(new VelocityComponent(ToVec(dto.Current)) { Last = ToVec(dto.Last) });
    }

    // ---- CameraFollowTargetComponent (camera-follow tuning + optional world-space bounds) ----

    private sealed class CameraFollowTargetDto
    {
        [JsonPropertyName("dampingX")] public float DampingX { get; set; } = 5f;
        [JsonPropertyName("dampingY")] public float DampingY { get; set; } = 5f;
        [JsonPropertyName("maxDistanceX")] public float MaxDistanceX { get; set; } = 100f;
        [JsonPropertyName("maxDistanceY")] public float MaxDistanceY { get; set; } = 100f;
        [JsonPropertyName("isActive")] public bool IsActive { get; set; } = true;
        // Optional world-space clamp rectangle (x, y, width, height). Omitted (null) when the camera
        // follows freely — CanonicalJson's WhenWritingNull keeps the field out of the file entirely.
        [JsonPropertyName("bounds")] public int[]? Bounds { get; set; }
    }

    private static JsonElement WriteCameraFollowTarget(Entity e)
    {
        var c = e.Get<CameraFollowTargetComponent>();
        return CanonicalJson.SerializeToElement(new CameraFollowTargetDto
        {
            DampingX = c.DampingX,
            DampingY = c.DampingY,
            MaxDistanceX = c.MaxDistanceX,
            MaxDistanceY = c.MaxDistanceY,
            IsActive = c.IsActive,
            Bounds = c.Bounds is { } b ? new[] { b.X, b.Y, b.Width, b.Height } : null,
        });
    }

    private static void ReadCameraFollowTarget(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<CameraFollowTargetDto>()!;
        e.Set(new CameraFollowTargetComponent
        {
            DampingX = dto.DampingX,
            DampingY = dto.DampingY,
            MaxDistanceX = dto.MaxDistanceX,
            MaxDistanceY = dto.MaxDistanceY,
            IsActive = dto.IsActive,
            Bounds = dto.Bounds is { Length: 4 } b ? new Rectangle(b[0], b[1], b[2], b[3]) : null,
        });
    }

    // ---- CameraComponent (the scene camera marker + zoom; position/rotation are on the Transform) ----
    // The camera is an ordinary scene root: EntityInfo("Camera") + Transform + this. Only the zoom lives
    // here — the CM one-rotation rule keeps rotation on the Transform, and the virtual resolution stays
    // render config on the Camera adapter, never scene data. Exactly one camera entity per scene (the
    // SceneWriter refuses a second; the SceneReaderSystem ensures one exists on load).

    private sealed class CameraDto
    {
        [JsonPropertyName("zoom")] public float Zoom { get; set; } = 1f;
    }

    private static JsonElement WriteCamera(Entity e) =>
        CanonicalJson.SerializeToElement(new CameraDto { Zoom = e.Get<CameraComponent>().Zoom });

    private static void ReadCamera(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<CameraDto>()!;
        e.Set(new CameraComponent { Zoom = dto.Zoom });
    }

    // ---- BoundaryComponent (freeform boundary polyline; the segment colliders are baked, never serialized) ----

    private sealed class BoundaryDto
    {
        [JsonPropertyName("points")] public float[][] Points { get; set; } = Array.Empty<float[]>();
        [JsonPropertyName("thickness")] public float Thickness { get; set; } =
            LevelEditor.Component.BoundaryComponent.DefaultThickness;
    }

    private static JsonElement WriteBoundary(Entity e)
    {
        var b = e.Get<LevelEditor.Component.BoundaryComponent>();
        return CanonicalJson.SerializeToElement(new BoundaryDto
        {
            Points = (b.Points ?? Array.Empty<Vector2>()).Select(Vec).ToArray(),
            Thickness = b.Thickness,
        });
    }

    private static void ReadBoundary(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<BoundaryDto>()!;
        e.Set(new LevelEditor.Component.BoundaryComponent(dto.Points.Select(ToVec).ToArray(), dto.Thickness));
    }

    // ---- SceneLayerComponent (the designer's layer: order/visibility/lock; name = EntityInfo) ----

    private sealed class SceneLayerDto
    {
        [JsonPropertyName("order")] public int Order { get; set; }
        [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
        [JsonPropertyName("locked")] public bool Locked { get; set; }
        [JsonPropertyName("screenSpace")] public bool ScreenSpace { get; set; }
    }

    private static JsonElement WriteSceneLayer(Entity e)
    {
        var layer = e.Get<MonoDreams.Component.Level.SceneLayerComponent>();
        return CanonicalJson.SerializeToElement(new SceneLayerDto
        {
            Order = layer.Order,
            Visible = layer.Visible,
            Locked = layer.Locked,
            ScreenSpace = layer.ScreenSpace,
        });
    }

    private static void ReadSceneLayer(Entity e, JsonElement json)
    {
        var dto = json.Deserialize<SceneLayerDto>()!;
        e.Set(new MonoDreams.Component.Level.SceneLayerComponent
        {
            Order = dto.Order,
            Visible = dto.Visible,
            Locked = dto.Locked,
            ScreenSpace = dto.ScreenSpace,
        });
    }

    // ---- ChildOfComponent: structural-link stub (body is empty; the link is SceneEntityData.Parent) ----
    // The discoverer skips this type before Write is ever called, and the reader wires the parent
    // graph from SceneEntityData.Parent (two-pass create-then-link). These stubs exist only so the
    // type counts as "registered" and to fail loud if a malformed file lists it as a component body.

    private static JsonElement WriteChildOfStub(Entity e) => JsonSerializer.SerializeToElement(new { });

    private static void ReadChildOfStub(Entity e, JsonElement json)
        => throw new InvalidOperationException(
            $"'{ChildOfKey}' must not appear as a component body; the parent link is the entity's " +
            "'parent' index field. This indicates a malformed scene file.");

    // ---- Primitive <-> JSON-array helpers (compact, explicit, deterministic) ----

    /// <summary>The collider's active layers, sorted ascending. <c>ActiveLayers</c> is a
    /// <see cref="HashSet{Int32}"/> — its enumeration order is an unspecified implementation detail, so
    /// serializing it raw would let a load→save cycle churn the array; sorting (a set is order-agnostic)
    /// makes the output byte-stable.</summary>
    private static int[] SortedLayers(HashSet<int> layers)
    {
        var arr = layers.ToArray();
        Array.Sort(arr);
        return arr;
    }

    private static float[] Vec(Vector2 v) => new[] { v.X, v.Y };
    private static Vector2 ToVec(float[] a) => new(a[0], a[1]);
    private static int[] Rect(Rectangle r) => new[] { r.X, r.Y, r.Width, r.Height };
    private static Rectangle ToRect(int[] a) => new(a[0], a[1], a[2], a[3]);
    private static byte[] Col(Color c) => new[] { c.R, c.G, c.B, c.A };
    private static Color ToCol(byte[] a) => new(a[0], a[1], a[2], a[3]);
}
