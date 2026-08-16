using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Swaps the (mesh) cursor's silhouette based on what it hovers. It performs NO hit-test of its
/// own: it reads <see cref="PointerPickComponent"/> off the cursor entity — the ONE pointer pick
/// <see cref="UIFocusSystem"/> publishes, the same resolution focus and click act on — takes the
/// picked entity's requested <see cref="FocusableComponent.HoverCursor"/> (Default when nothing is
/// picked) and writes it onto <see cref="CursorControllerComponent.Type"/>. When the type changes
/// it swaps the cursor entity's mesh <c>DrawComponent</c> to the matching
/// <see cref="CursorMeshLibraryComponent"/> entry (falling back to the Default = arrow entry).
///
/// <para><b>Pipeline placement.</b> After <see cref="UIFocusSystem"/>, which publishes the pick. A
/// screen that registers this system without one gets no pick and therefore no swap (the resting
/// arrow), which is the documented graceful degradation — see the ui premise "There is ONE pointer
/// pick".</para>
///
/// <para>Reusable + ECS-pure: any focusable opts into a custom hover cursor purely as data
/// (<see cref="FocusableComponent.HoverCursor"/>), and any mesh cursor opts in by carrying a
/// <see cref="CursorMeshLibraryComponent"/>. A cursor without the library is left untouched (the
/// type is still recorded, but no mesh swap happens), so the textured cursor path is unaffected.</para>
/// </summary>
public sealed class CursorHoverSystem : ISystem<GameState>
{
    private readonly EntitySet _cursors;

    public bool IsEnabled { get; set; } = true;

    public CursorHoverSystem(World world)
    {
        _cursors = world.GetEntities()
            .With<CursorControllerComponent>().With<CursorMeshLibraryComponent>().With<PointerPickComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var cursors = _cursors.GetEntities();
        if (cursors.Length == 0) return;

        var cursorEntity = cursors[0];

        // The pick is only refreshed while its owner runs, so re-check it names a live focusable.
        var picked = cursorEntity.Get<PointerPickComponent>().Hovered;
        var hover = picked.IsAlive && picked.Has<FocusableComponent>()
            ? picked.Get<FocusableComponent>().HoverCursor
            : CursorType.Default;

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

    public void Dispose()
    {
        _cursors.Dispose();
    }
}
