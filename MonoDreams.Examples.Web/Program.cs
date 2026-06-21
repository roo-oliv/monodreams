using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MonoDreams.Platform;

namespace MonoDreams.Examples.Web
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            // Install the web platform services BEFORE any engine type is constructed
            // (Logger, systems). PlatformServices.Current is a static holder defaulting to
            // desktop; the web head must override it first (foundation portability premise).
            PlatformServices.Current = new WebPlatformServices();

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");
            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            });
            await builder.Build().RunAsync();
        }
    }
}
