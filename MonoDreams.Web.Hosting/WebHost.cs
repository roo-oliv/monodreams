using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using MonoDreams.Platform;

namespace MonoDreams.Web.Hosting
{
    /// <summary>
    /// The Blazor WebAssembly bootstrap shared by every web head. A head's entire
    /// <c>Program.Main</c> becomes one line:
    /// <code>static Task Main(string[] args) =&gt; WebHost.RunAsync(args, () =&gt; new WebGame());</code>
    /// </summary>
    public static class WebHost
    {
        /// <summary>
        /// Installs the web <see cref="IPlatformServices"/>, builds the Blazor WASM host with
        /// <see cref="GameCanvas"/> as the root component, registers the head's Game factory, and runs.
        /// </summary>
        /// <param name="args">The host args passed to <c>Program.Main</c>.</param>
        /// <param name="gameFactory">Creates the head's concrete <see cref="Game"/> (e.g. <c>() =&gt; new WebGame()</c>).
        /// Invoked once, on the first animation frame, by <see cref="GameCanvas"/>.</param>
        public static async Task RunAsync(string[] args, Func<Game> gameFactory)
        {
            if (gameFactory == null) throw new ArgumentNullException(nameof(gameFactory));

            // Install the web platform services BEFORE any engine type is constructed (Logger,
            // systems). PlatformServices.Current is a static holder defaulting to desktop; the web
            // host must override it first (foundation portability premise).
            PlatformServices.Current = new WebPlatformServices();

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<GameCanvas>("#app");
            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            });
            // GameCanvas resolves this to construct the head's Game on the first frame.
            builder.Services.AddSingleton(gameFactory);

            await builder.Build().RunAsync();
        }
    }
}
