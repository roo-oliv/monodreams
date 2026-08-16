using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System.Cursor;

public class CursorPositionSystem(World world, MonoDreams.Component.Camera camera, ViewportManager viewportManager)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CursorControllerComponent>().With<CursorInputComponent>().With<TransformComponent>().With<DrawComponent>().AsSet())
{
    /// <summary>
    /// Mirrors <c>CursorInputSystem.SkipHardwareRead</c> for the DERIVATION half: an editor-op /
    /// replay channel that INJECTS World/Virtual positions + <c>OutsideViewport</c> directly sets
    /// this so the per-frame screen→virtual→world derivation (which would recompute them from the
    /// un-mapped injected <c>ScreenPosition</c> and clobber the injection with
    /// <c>OutsideViewport = true</c>) stands down. A real-mouse session leaves it false.
    /// </summary>
    public bool SkipDerivation { get; set; }

    protected override void Update(GameState state, in Entity entity)
    {
        if (SkipDerivation) return;
        ref var input = ref entity.Get<CursorInputComponent>();

        // First convert the screen position to AUTHORING (layout) coordinates — the space every
        // game number, UI bound and HUD position is written in. In a single-space game that is the
        // virtual resolution; under a two-space setup it is the layout resolution, so nothing here
        // (or downstream) moves when the render resolution does.
        var virtualPosition = viewportManager.MapMouse(input.ScreenPosition);

        // Track whether the pointer is outside the aspect-fit viewport (letterbox bars or the
        // editor shell's chrome margins) so world-space consumers can ignore clicks/scrolls there.
        input.OutsideViewport = !virtualPosition.HasValue;

        if (virtualPosition.HasValue)
        {
            // Always calculate world position for systems that need it (e.g., ButtonInteractionSystem),
            // and place the transform per render target — both through the shared pose rule, which an
            // injection channel (PointerReplaySystem) calls with the positions IT authored.
            MonoDreams.Cursor.Cursor.ApplyPose(entity, virtualPosition.Value,
                camera.VirtualScreenToWorld(virtualPosition.Value));
        }
        // else: Mouse is outside the viewport (in letterbox/pillarbox area) - keep previous position

        entity.NotifyChanged<CursorInputComponent>();
        entity.NotifyChanged<TransformComponent>();
    }
}
