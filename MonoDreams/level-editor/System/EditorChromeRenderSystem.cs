#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System.Draw;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor chrome's render pass (Wave 7): a screen-space <see cref="MasterRenderSystem"/>
/// (null camera) over <see cref="RenderTargetID.Editor"/> into a render target at <b>native
/// window resolution</b>, recreated whenever the window size changes. The screen composites
/// <see cref="CurrentTarget"/> 1:1 over the whole window via <c>RenderLayer.Native</c>, ABOVE the
/// game layers — chrome pixels are screen pixels, never rescaled, so panels and labels stay crisp
/// regardless of the game's virtual resolution.
///
/// <para><b>Always on while the editor is composed (transport model).</b> The shell never
/// collapses — the chrome renders in BOTH transport states (Paused and Playing), since the
/// transport buttons and the systems panel stay interactive while the game runs in the inset
/// viewport. Only disabling the system (e.g. from the systems panel) blanks the chrome: then the
/// pass does not run and <see cref="CurrentTarget"/> is null, which makes <c>FinalDrawSystem</c>
/// skip the layer entirely — no per-entity blanking. This system does not create a parallel
/// renderer: the actual drawing is the one game-agnostic <see cref="MasterRenderSystem"/>; this
/// wrapper only owns the native target's resize lifecycle.</para>
/// </summary>
public sealed class EditorChromeRenderSystem : ISystem<GameState>
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ViewportManager _viewportManager;
    private readonly MasterRenderSystem _pass;
    private RenderTarget2D _target;
    private bool _visible;

    public bool IsEnabled { get; set; } = true;

    public EditorChromeRenderSystem(
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        World world,
        ViewportManager viewportManager)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _target = CreateTarget();
        _pass = new MasterRenderSystem(spriteBatch, graphicsDevice, world,
            RenderTargetID.Editor, _target); // null camera = screen-space
    }

    /// <summary>
    /// The chrome target to composite this frame, or null when the chrome is hidden (the system
    /// was disabled) so the final-draw layer is skipped. Read via a provider
    /// (<c>RenderLayer.Native</c>) because the target is recreated on resize.
    /// </summary>
    public RenderTarget2D? CurrentTarget => _visible ? _target : null;

    public void Update(GameState state)
    {
        if (!IsEnabled)
        {
            _visible = false;
            return;
        }

        EnsureTargetMatchesWindow();
        _pass.Update(state);
        _visible = true;
    }

    private void EnsureTargetMatchesWindow()
    {
        var width = Math.Max(1, _viewportManager.ScreenWidth);
        var height = Math.Max(1, _viewportManager.ScreenHeight);
        if (_target.Width == width && _target.Height == height) return;

        _target.Dispose();
        _target = new RenderTarget2D(_graphicsDevice, width, height);
        _pass.Destination = _target;
    }

    private RenderTarget2D CreateTarget() => new(
        _graphicsDevice,
        Math.Max(1, _viewportManager.ScreenWidth),
        Math.Max(1, _viewportManager.ScreenHeight));

    public void Dispose()
    {
        _pass.Dispose();
        _target.Dispose();
    }
}
