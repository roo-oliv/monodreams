using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace MonoDreams.Spike.Web.Pages
{
    // Code-behind for the canvas page. Mirrors the KNI sample (nkast/WebGLxnaProj): the JS in
    // index.html calls initRenderJS once the canvas exists, then drives a requestAnimationFrame
    // loop that invokes TickDotNet every frame. We construct the Game lazily on the first tick
    // (the GL context is ready by then) and Tick() it thereafter.
    public partial class Index
    {
        private Game _game;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);

            if (firstRender)
            {
                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
            }
        }

        [JSInvokable]
        public void TickDotNet()
        {
            if (_game == null)
            {
                _game = new SpikeGame();
                _game.Run();
            }

            _game.Tick();
        }
    }
}
