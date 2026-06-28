using System.Threading.Tasks;
using MonoDreams.Web.Hosting;

namespace MonoDreams.Demos.Web
{
    internal class Program
    {
        // All the Blazor WASM host wiring lives in the shared MonoDreams.Web.Hosting layer; this
        // head only supplies its Game. See WebHost.RunAsync.
        private static Task Main(string[] args) => WebHost.RunAsync(args, () => new WebGame());
    }
}
