using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Swaps the (mesh) cursor's silhouette based on what it hovers. Each frame it hit-tests every
/// <see cref="FocusableComponent"/> that declares a non-Default
/// <see cref="FocusableComponent.HoverCursor"/> against the cursor position (mirroring
/// <c>DropdownSystem.ContainsCursor</c>: <c>Rectangle(WorldPosition, FocusableComponent.Size)</c> vs
/// the cursor's world/virtual position by the focusable's <see cref="FocusableComponent.Target"/>),
/// finds the topmost (last in iteration) hovered one, and writes its requested
/// <see cref="CursorType"/> onto <see cref="CursorControllerComponent.Type"/>. When the type changes
/// it swaps the cursor entity's mesh <c>DrawComponent</c> to the matching
/// <see cref="CursorMeshLibraryComponent"/> entry (falling back to the Default = arrow entry).
///
/// <para>Reusable + ECS-pure: any focusable opts into a custom hover cursor purely as data
/// (<see cref="FocusableComponent.HoverCursor"/>), and any mesh cursor opts in by carrying a
/// <see cref="CursorMeshLibraryComponent"/>. A cursor without the library is left untouched (the
/// type is still recorded, but no mesh swap happens), so the textured cursor path is unaffected.</para>
/// </summary>
public sealed class CursorHoverSystem : ISystem<GameState>
{
    private readonly EntitySet _focusables;
    private readonly EntitySet _cursors;

    public bool IsEnabled { get; set; } = true;

    public CursorHoverSystem(World world)
    {
        _focusables = world.GetEntities().With<FocusableComponent>().With<TransformComponent>().AsSet();
        _cursors = world.GetEntities()
            .With<CursorInputComponent>().With<CursorControllerComponent>().With<CursorMeshLibraryComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var cursors = _cursors.GetEntities();
        if (cursors.Length == 0) return;

        var cursorEntity = cursors[0];
        ref readonly var input = ref cursorEntity.Get<CursorInputComponent>();
        var world = input.WorldPosition;
        var virtualPos = input.VirtualPosition;

        // Topmost hovered focusable with a custom cursor wins (last match in iteration order).
        var hover = CursorType.Default;
        foreach (var e in _focusables.GetEntities())
        {
            ref readonly var f = ref e.Get<FocusableComponent>();
            if (f.HoverCursor == CursorType.Default) continue;
            if (f.Disabled || ControlDisabled(e)) continue;

            var wp = e.Get<TransformComponent>().WorldPosition;
            var pos = f.Target == RenderTargetID.HUD ? virtualPos : world;
            var bounds = new Rectangle((int)wp.X, (int)wp.Y, (int)f.Size.X, (int)f.Size.Y);
            if (bounds.Contains(pos)) hover = f.HoverCursor;
        }

        ref var controller = ref cursorEntity.Get<CursorControllerComponent>();
        if (controller.Type == hover) return; // no change → no mesh swap

        controller.Type = hover;

        ref readonly var library = ref cursorEntity.Get<CursorMeshLibraryComponent>();
        if (library.Meshes == null || !cursorEntity.Has<DrawComponent>()) return;

        if (!library.Meshes.TryGetValue(hover, out var mesh))
            library.Meshes.TryGetValue(CursorType.Default, out mesh); // fall back to the arrow

        if (mesh.IsValid)
            cursorEntity.Get<DrawComponent>().SetMeshData(mesh);
    }

    private static bool ControlDisabled(Entity e) =>
        e.Has<ButtonStateComponent>() && e.Get<ButtonStateComponent>().IsDisabled;

    public void Dispose()
    {
        _focusables.Dispose();
        _cursors.Dispose();
    }
}
