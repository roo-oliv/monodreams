using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace MonoDreams.Web.Hosting
{
    /// <summary>
    /// Code-behind for <c>GameCanvas.razor</c>: the requestAnimationFrame-driven game loop shared
    /// by every web head. On first render it hands a .NET reference to host.js's initRenderJS, which
    /// starts a requestAnimationFrame loop calling back into <see cref="TickDotNet"/> every frame.
    /// The concrete <see cref="Game"/> is supplied per head via an injected <see cref="Func{Game}"/>
    /// (registered by <see cref="WebHost.RunAsync"/>), so this one component serves Examples.Web,
    /// Demos.Web, and any CLI-scaffolded head unchanged.
    /// </summary>
    public partial class GameCanvas
    {
        [Inject] private IJSRuntime JsRuntime { get; set; }
        [Inject] private Func<Game> GameFactory { get; set; }

        private Game _game;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);

            // The GL context exists once the canvas has rendered; hand host.js a reference to this
            // component so its requestAnimationFrame loop can tick us.
            if (firstRender)
                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
        }

        [JSInvokable]
        public void TickDotNet()
        {
            // Construct + Run the Game lazily on the first tick (GL is ready by then), then Tick it
            // every frame. Mirrors the KNI sample (nkast/WebGLxnaProj) and the Phase 0 spike.
            if (_game == null)
            {
                _game = GameFactory();
                _game.Run();
            }

            _game.Tick();
        }
    }
}
