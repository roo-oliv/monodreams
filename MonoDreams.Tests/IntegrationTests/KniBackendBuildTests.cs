using System.Diagnostics;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// Phase 2 (KNI dependency parity) build-contract protection.
///
/// MonoDreams' own source recompiles unchanged against either backend; the difference is
/// entirely in which precompiled third-party packages MSBuild resolves, gated by the
/// <c>$(MonoDreamsPlatform)</c> property (see <c>Directory.Build.props</c> and
/// <c>MonoDreams.csproj</c>). The web/KNI variant swaps MonoGame.Framework.DesktopGL →
/// nkast.Xna.Framework.*, MonoGame.Extended → KNI.Extended (+ content pipeline), the MonoGame
/// content pipeline → nkast.Xna.Framework.Content.Pipeline (which the Yarn importer/writer
/// compile against), and the vendored LDtkMonogame runtime recompiles against nkast.
///
/// These are build-time contracts, not runtime behaviour we can exercise in xUnit on this host
/// (there is no BlazorGL runtime here — the Phase 0 spike proved the web render path separately).
/// The protection is therefore a build assertion: invoke the web variant and require a clean
/// compile. This guards against a future change reintroducing a desktop-only API into engine
/// source or a precompiled dep losing its KNI variant.
///
/// Mechanics: the web build is sent to an isolated <c>--output</c> directory and the desktop build
/// is rebuilt in place afterwards, because a web restore writes a different project.assets.json
/// than desktop and the rest of the suite relies on the in-place desktop artifacts. The two builds
/// share one collection (no parallelism) so they don't race that in-place rebuild.
/// </summary>
[Collection("KniBackendBuild")]
public class KniBackendBuildTests
{
    /// <summary>
    /// The full engine core compiles against the KNI/BlazorGL backend (MonoGame.Extended →
    /// KNI.Extended runtime + content pipeline; Yarn importer/writer against the nkast pipeline;
    /// vendored LDtkMonogame runtime recompiled against nkast). Covers contract items
    /// "MonoGame.Extended→KNI.Extended", "Yarn importer compiles against nkast pipeline", and the
    /// web half of the LDtk vendoring item.
    /// </summary>
    [Fact]
    public void EngineCoreCompilesAgainstKniWebBackend()
        => AssertWebThenRestoreDesktop("MonoDreams/MonoDreams.csproj");

    /// <summary>
    /// The vendored LDtkMonogame runtime recompiles against the KNI backend on its own. Covers
    /// the "vendor LDtkMonogame ... build via $(MonoDreamsPlatform)" contract item directly.
    /// (Its desktop path is exercised end-to-end by <see cref="LDtkLevelTests"/>.)
    /// </summary>
    [Fact]
    public void VendoredLDtkRuntimeCompilesAgainstKniWebBackend()
        => AssertWebThenRestoreDesktop("MonoDreams/level-ldtk/vendor/LDtkMonogame/LDtk/LDtk.csproj");

    private static void AssertWebThenRestoreDesktop(string relativeCsproj)
    {
        var repoRoot = FindRepoRoot();
        var csproj = Path.Combine(repoRoot, relativeCsproj.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(csproj), $"Project not found: {csproj}");

        var isolatedBin = Path.Combine(Path.GetTempPath(), "monodreams_kniweb_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var (exitCode, output) = RunDotnet(repoRoot,
                $"build \"{csproj}\" -c Debug -p:MonoDreamsPlatform=web --output \"{isolatedBin}\" -v q -nologo");

            Assert.True(exitCode == 0,
                $"Expected '{relativeCsproj}' to compile against MonoDreamsPlatform=web with exit 0.\n{output}");
        }
        finally
        {
            try { if (Directory.Exists(isolatedBin)) Directory.Delete(isolatedBin, recursive: true); } catch { /* best effort */ }
            // The web restore left the in-place obj in web state; restore desktop so later tests
            // (and the desktop-targeting integration tests) see desktop artifacts again.
            RunDotnet(repoRoot, $"build \"{csproj}\" -c Debug -v q -nologo");
        }
    }

    private static (int exitCode, string output) RunDotnet(string workingDir, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "MonoDreams.Examples")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root (directory containing MonoDreams.Examples).");
    }
}
