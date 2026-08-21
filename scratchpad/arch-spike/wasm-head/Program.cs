using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MonoDreams.ArchSpike;

namespace MonoDreams.ArchSpike.WasmHead;

/// <summary>
/// KNI/BlazorGL WebAssembly head of the wave-0 Arch target proof (issue #119, contract item 2,
/// WASM leg).
///
/// No root component and no canvas on purpose: the rendering half of the web story is already
/// proven by <c>scratchpad/blazor-spike/</c>. What is unproven — and what this head measures — is
/// whether Arch itself survives the WASM runtime and the Blazor publish trimmer while sitting in
/// the same bundle as the KNI backend. So the head boots Blazor, touches KNI types so the trimmer
/// keeps the BlazorGL backend in the bundle, runs the shared <see cref="ArchExercise"/>, and hands
/// the report to the page (and to the browser console).
///
/// The page title becomes ARCH-WASM PASS / ARCH-WASM FAIL, which is what an automated driver reads.
/// </summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        var host = builder.Build();

        var report = new StringBuilder();
        var failures = 0;

        try
        {
            report.AppendLine(DescribeKniBackend());
        }
        catch (Exception ex)
        {
            failures++;
            report.AppendLine("   [FAIL] KNI backend probe threw: " + ex);
        }

        var (archFailures, archReport) = ArchExercise.Run();
        failures += archFailures;
        report.Append(archReport);

        var text = report.ToString();
        Console.WriteLine(text);

        var js = host.Services.GetRequiredService<IJSRuntime>();
        await js.InvokeVoidAsync("spikeReport", failures, text);

        await host.RunAsync();
    }

    /// <summary>
    /// Reads the KNI/BlazorGL assemblies out of the running bundle. Every line here is also a
    /// static reference the Blazor trimmer has to honour, which is precisely why the probe exists:
    /// without it a publish could drop the KNI backend entirely and the head would still "pass",
    /// proving nothing about Arch coexisting with it.
    /// </summary>
    private static string DescribeKniBackend()
    {
        var lines = new StringBuilder();
        lines.AppendLine("== KNI/BlazorGL backend present in this bundle ==");
        lines.AppendLine();

        var frameworkAssembly = typeof(Microsoft.Xna.Framework.Vector2).Assembly.GetName();
        var gameAssembly = typeof(Microsoft.Xna.Framework.Game).Assembly.GetName();

        lines.AppendLine($"   {"Vector2 assembly",-52} : {frameworkAssembly.Name} {frameworkAssembly.Version}");
        lines.AppendLine($"   {"Game assembly",-52} : {gameAssembly.Name} {gameAssembly.Version}");

        // Exercise a KNI value type so the reference is a real use, not just a typeof.
        var a = new Microsoft.Xna.Framework.Vector2(3f, 4f);
        var b = new Microsoft.Xna.Framework.Vector2(1f, 2f);
        var sum = a + b;
        lines.AppendLine($"   {"Vector2(3,4) + Vector2(1,2)",-52} : {sum}");
        lines.AppendLine($"   {"Vector2(3,4).Length()",-52} : {a.Length()}");

        return lines.ToString();
    }
}
