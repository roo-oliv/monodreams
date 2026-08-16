#nullable enable
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;

namespace MonoDreams.UI;

/// <summary>
/// Entry point for creating Figma-like auto layout UI hierarchies.
/// </summary>
/// <example>
/// <code>
/// var layout = new AutoLayoutBuilder(world, viewportManager);
///
/// layout.CreateRoot(ScreenAnchor.Center)
///     .Direction(LayoutDirection.Vertical)
///     .Gap(40)
///     .AlignCross(CrossAxisAlignment.Center)
///     .AddText("Select Level", font, darkBrown, scale: 0.2f)
///     .AddChild(row => row
///         .Direction(LayoutDirection.Horizontal)
///         .Gap(50)
///         .AddButton("Level 1", font, 0, "Level_0", isClickable: true, buttonStyle)
///         .AddButton("Level 2", font, 1, "Level_1", isClickable: false, buttonStyle)
///         .AddButton("Level 3", font, 2, "Level_2", isClickable: false, buttonStyle)
///     )
///     .Build();
/// </code>
/// </example>
public class AutoLayoutBuilder
{
    private readonly World _world;
    private readonly ViewportManager _viewportManager;

    /// <summary>
    /// Creates a new AutoLayoutBuilder.
    /// </summary>
    /// <param name="world">The DefaultEcs world to create entities in.</param>
    /// <param name="viewportManager">The viewport manager for screen dimensions.</param>
    public AutoLayoutBuilder(World world, ViewportManager viewportManager)
    {
        _world = world;
        _viewportManager = viewportManager;
    }

    /// <summary>
    /// Creates a root container anchored to the screen.
    /// This is the starting point for building a UI layout.
    /// <para>Every root created this way shares ONE implicit solver container and therefore
    /// STACKS with the screen's other anchored roots. For a panel that must sit at a position of
    /// its own — a HUD corner widget, a toolbar, a sticky note — use
    /// <see cref="CreatePinnedRoot"/> instead.</para>
    /// </summary>
    /// <param name="anchor">Where to anchor the root container on the screen.</param>
    /// <param name="renderTarget">Which render target to draw to (default: Main).</param>
    /// <returns>A ContainerBuilder for configuring the root container.</returns>
    public ContainerBuilder CreateRoot(
        ScreenAnchor anchor = ScreenAnchor.Center,
        RenderTargetID renderTarget = RenderTargetID.Main)
    {
        return new ContainerBuilder(
            _world,
            parentBuilder: null,
            isRoot: true,
            anchor: anchor,
            renderTarget: renderTarget);
    }

    /// <summary>
    /// Creates a root container PINNED at an arbitrary screen position: it is solved on its own
    /// (no stacking against the screen's other roots) and placed at
    /// <paramref name="anchor"/> + <paramref name="position"/>.
    /// Build as many as the screen needs — several independent panels, each at its own spot.
    /// </summary>
    /// <example>
    /// <code>
    /// // A monitor pinned 32 px in from the top-left, and a taskbar hugging the bottom edge.
    /// layout.CreatePinnedRoot(new Vector2(32, 32))
    ///     .Direction(LayoutDirection.Vertical)
    ///     .AddSlot(...)
    ///     .Build();
    ///
    /// layout.CreatePinnedRoot(Vector2.Zero, ScreenAnchor.BottomCenter)
    ///     .Direction(LayoutDirection.Horizontal)
    ///     .AddSlot(...)
    ///     .Build();
    /// </code>
    /// </example>
    /// <param name="position">Offset from <paramref name="anchor"/> in layout pixels (X right,
    /// Y down).</param>
    /// <param name="anchor">The screen reference point the offset is measured from
    /// (default: TopLeft, i.e. plain screen coordinates).</param>
    /// <param name="renderTarget">Which render target to draw to (default: Main).</param>
    /// <returns>A ContainerBuilder for configuring the pinned root container.</returns>
    /// <remarks>
    /// The screen's pipeline must run <see cref="PinnedLayoutRootSystem"/> after
    /// <see cref="AutoLayoutSystem"/> and before <c>HierarchySystem</c>; without it a pinned root
    /// renders at its bare anchor, ignoring <paramref name="position"/>.
    /// </remarks>
    public ContainerBuilder CreatePinnedRoot(
        Vector2 position,
        ScreenAnchor anchor = ScreenAnchor.TopLeft,
        RenderTargetID renderTarget = RenderTargetID.Main)
    {
        return new ContainerBuilder(
            _world,
            parentBuilder: null,
            isRoot: true,
            anchor: anchor,
            renderTarget: renderTarget,
            pinOffset: position);
    }

    /// <summary>
    /// The AUTHORING screen width from the viewport manager — the space UI is laid out in
    /// (== the virtual width in a single-space game).
    /// </summary>
    public int LayoutWidth => _viewportManager.LayoutWidth;

    /// <summary>
    /// The AUTHORING screen height from the viewport manager.
    /// </summary>
    public int LayoutHeight => _viewportManager.LayoutHeight;
}
