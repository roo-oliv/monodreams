#nullable enable
using DefaultEcs;
using MonoDreams.Examples.Component.Layout;
using MonoDreams.Renderer;
using RenderTargetID = MonoDreams.Component.Draw.RenderTargetID;

namespace MonoDreams.Examples.Layout;

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
    /// Gets the virtual screen width from the viewport manager.
    /// </summary>
    public int VirtualWidth => _viewportManager.VirtualWidth;

    /// <summary>
    /// Gets the virtual screen height from the viewport manager.
    /// </summary>
    public int VirtualHeight => _viewportManager.VirtualHeight;
}
