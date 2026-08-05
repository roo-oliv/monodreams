#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.LevelEditor.UI;

namespace MonoDreams.LevelEditor.System;

/// <summary>Where a screen-space HUD member was authored (the restore snapshot) — added by
/// <see cref="HudPreviewSystem"/> on first touch, never serialized (an editor-session artifact).</summary>
public struct HudPreviewStashComponent
{
    /// <summary>The authored position in VIRTUAL-resolution HUD coordinates.</summary>
    public Vector2 VirtualPosition;

    /// <summary>The authored text scale.</summary>
    public float TextScale;

    /// <summary>The authored render target (HUD).</summary>
    public RenderTargetID Target;
}

/// <summary>
/// The Edit-mode <b>HUD preview</b>: members of a SCREEN-SPACE scene layer
/// (<see cref="SceneLayerComponent.ScreenSpace"/> — the game's "HUD" grouping) are authored in
/// virtual-resolution coordinates on the HUD render pass, which is composited over the whole game
/// viewport — correct in Play, but in Edit the free view pans away while the HUD stays glued to the
/// pane, reading as chrome. This system re-projects them <b>into the CAMERA entity's frame</b>
/// while Paused: target flipped to Main, position mapped from virtual → the camera frustum's world
/// rect, text scale divided by the camera zoom — so the HUD text sits INSIDE the camera glyph,
/// exactly where the game will show it, and pans/zooms with the world like any authored content.
///
/// <para>Entering Play (or hiding the layer) restores the stashed authored values, so the REAL HUD
/// pass renders untouched — the plain game never composes this system at all. Weave after the
/// game's HUD-content systems (their frame's text lands before projection), before the draw prep.</para>
/// </summary>
public sealed class HudPreviewSystem : ISystem<GameState>
{
    private readonly ViewportManager _viewportManager;
    private readonly EntitySet _members;
    private readonly EntitySet _cameras;

    public bool IsEnabled { get; set; } = true;

    public HudPreviewSystem(World world, ViewportManager viewportManager)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _members = world.GetEntities()
            .With<ChildOfComponent>()
            .With<DynamicTextComponent>()
            .With<TransformComponent>()
            .AsSet();
        _cameras = world.GetEntities()
            .With<CameraComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var hasCamera = TryGetCameraFrame(out var camCenter, out var zoom);
        var preview = state.RunMode == RunMode.Edit && hasCamera;
        var virtualSize = new Vector2(_viewportManager.VirtualWidth, _viewportManager.VirtualHeight);

        foreach (var member in _members.GetEntities())
        {
            var layer = global::MonoDreams.System.Level.SceneLayerSystem.OwningLayer(member);
            var isHudMember = layer.IsAlive && layer.Get<SceneLayerComponent>().ScreenSpace;

            if (!isHudMember || !preview)
            {
                Restore(member);
                continue;
            }

            ref var text = ref member.Get<DynamicTextComponent>();
            ref var transform = ref member.Get<TransformComponent>();

            // Stash the authored (virtual-space) values once; the projection below re-derives from
            // the stash every frame, so it never compounds.
            if (!member.Has<HudPreviewStashComponent>())
                member.Set(new HudPreviewStashComponent
                {
                    VirtualPosition = transform.Position,
                    TextScale = text.Scale,
                    Target = text.Target,
                });
            var stash = member.Get<HudPreviewStashComponent>();

            if (!layer.Get<SceneLayerComponent>().Visible)
            {
                // The eye toggle hides the preview (the game's own pass honors it on the next
                // restore only in Edit — Play HUD visibility is gameplay's business).
                transform.Position = SystemsPanelLayout.ParkedPosition;
                member.NotifyChanged<TransformComponent>();
                continue;
            }

            // Virtual → the camera frame's world rect: the frustum covers virtual/zoom world units
            // centred on the camera entity; text glyphs shrink by the same factor.
            var topLeft = camCenter - virtualSize / (2f * zoom);
            text.Target = RenderTargetID.Main;
            text.Scale = stash.TextScale / zoom;
            transform.Position = topLeft + stash.VirtualPosition / zoom;
            member.NotifyChanged<TransformComponent>();
        }
    }

    /// <summary>Restores a member's authored HUD-space values and drops the stash (idempotent).</summary>
    private static void Restore(in Entity member)
    {
        if (!member.Has<HudPreviewStashComponent>()) return;
        var stash = member.Get<HudPreviewStashComponent>();
        ref var text = ref member.Get<DynamicTextComponent>();
        ref var transform = ref member.Get<TransformComponent>();
        text.Target = stash.Target;
        text.Scale = stash.TextScale;
        transform.Position = stash.VirtualPosition;
        member.NotifyChanged<TransformComponent>();
        member.Remove<HudPreviewStashComponent>();
    }

    /// <summary>The scene camera entity's frame: world centre + zoom. False when no camera exists
    /// (a bare world) — the preview then leaves the HUD in its authored pass.</summary>
    private bool TryGetCameraFrame(out Vector2 center, out float zoom)
    {
        foreach (var cam in _cameras.GetEntities())
        {
            center = cam.Get<TransformComponent>().WorldPosition;
            zoom = MathF.Max(0.05f, cam.Get<CameraComponent>().Zoom);
            return true;
        }
        center = Vector2.Zero;
        zoom = 1f;
        return false;
    }

    public void Dispose()
    {
        _members.Dispose();
        _cameras.Dispose();
    }
}
