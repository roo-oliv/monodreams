using System.Text.RegularExpressions;
using MonoDreams.Cli.Manifest;
using MonoDreams.Cli.Resolver;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Issue #82: <c>collision</c> declares its <b>hard</b> <c>physics</c> dependency. The collision source
/// opens <c>MonoDreams.Component.Physics</c> (<c>ColliderBody.cs</c>,
/// <c>TransformCollisionResolutionSystem.cs</c>) for the <c>RigidBodyComponent</c>/<c>VelocityComponent</c>
/// body markers, so <c>monodreams add collision</c> must install <c>physics</c> too or the user's very
/// first <c>dotnet build</c> fails on a namespace from a module they never installed.
///
/// These are the cheap in-process guards (manifest text, resolver order, and a source scan that catches
/// the *next* undeclared cross-module <c>using</c> in this module). The end-to-end proof — scaffold, add,
/// <c>dotnet build</c> — is
/// <see cref="ScaffolderBuildTests.Init_ThenAddCollision_InstallsPhysicsAndBuilds"/>.
/// </summary>
public class CollisionModuleRegistryTests
{
    private static Registry LoadRegistry() => Registry.Load(CliTestSupport.FindRepoRoot());

    [Fact]
    public void Registry_DiscoversCollision_WithFoundationAndPhysicsDependencies()
    {
        var collision = LoadRegistry().GetModule("collision");

        Assert.Equal(new[] { "foundation", "physics" }, collision.Dependencies);
        Assert.True(collision.SupportsPlatform(Platform.Desktop));
        Assert.True(collision.SupportsPlatform(Platform.Web));
    }

    /// <summary>
    /// The manifest <c>description</c> is what <c>monodreams list</c> prints, and it used to advertise
    /// "soft-couples to physics" — the exact claim that told users the coupling was optional.
    /// </summary>
    [Fact]
    public void Description_DoesNotAdvertiseTheCouplingAsSoft()
    {
        var description = LoadRegistry().GetModule("collision").Description;

        Assert.DoesNotContain("soft", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("physics", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>postInstallNotes</c> is printed straight into the terminal by <c>monodreams add collision</c>,
    /// so a wrong claim there is shipped documentation. It used to say
    /// <c>TransformPhysicalCollisionResolutionSystem</c> "applies impulse separation" and "acts only on
    /// bodies that carry <c>RigidBodyComponent</c> and <c>VelocityComponent</c>" — both false: the
    /// subclass's only override admits a message when <c>Type == CollisionType.Physics</c> and then runs
    /// the base class's positional correction. This pins the notes to naming that real gate; the
    /// behaviour itself is pinned by
    /// <c>MonoDreams.Tests/Collision/PhysicalResolutionFilterTests.cs</c>.
    /// </summary>
    [Fact]
    public void PostInstallNotes_NameTheRealGateOnPhysicalResolution()
    {
        var notes = LoadRegistry().GetModule("collision").PostInstallNotes;

        Assert.NotNull(notes);
        Assert.Contains("CollisionType.Physics", notes, StringComparison.Ordinal);
    }

    // Platform is internal to the CLI assembly, so no [Theory] parameter — one Fact per backend.
    [Fact]
    public void Resolver_ResolvesCollisionForDesktop_WithPhysicsFirst() => AssertCollisionResolves(Platform.Desktop);

    [Fact]
    public void Resolver_ResolvesCollisionForWeb_WithPhysicsFirst() => AssertCollisionResolves(Platform.Web);

    private static void AssertCollisionResolves(Platform platform)
    {
        var registry = LoadRegistry();

        var resolved = DependencyResolver.Resolve(registry, new[] { "collision" }, Array.Empty<string>(), platform);

        Assert.Contains("collision", resolved);
        Assert.Contains("physics", resolved);
        Assert.Contains("foundation", resolved);
        Assert.True(resolved.IndexOf("physics") < resolved.IndexOf("collision"),
            "physics must be installed before the module that compiles against it");
    }

    /// <summary>
    /// Manifest honesty for this one module, checked at the source level: every engine namespace the
    /// collision sources <c>using</c> must be owned by a module inside collision's declared transitive
    /// dependency closure. Pre-#82 this failed on <c>MonoDreams.Component.Physics</c> (owned by
    /// <c>physics</c>, which the manifest did not declare) — the birth test for the bug.
    ///
    /// Scoped to <c>collision</c> on purpose: generalizing the check to every module (by actually
    /// compiling each one against its declared deps) is issue #83.
    /// </summary>
    [Fact]
    public void EveryEngineNamespaceCollisionImports_IsOwnedByADeclaredDependency()
    {
        var repoRoot = CliTestSupport.FindRepoRoot();
        var registry = Registry.Load(repoRoot);

        // The closure the CLI would actually install for `monodreams add collision`.
        var installed = DependencyResolver
            .Resolve(registry, new[] { "collision" }, Array.Empty<string>(), Platform.Desktop)
            .ToHashSet();

        var owners = NamespaceOwnersByModule(Path.Combine(repoRoot, "MonoDreams"));

        var offenders = new List<string>();
        foreach (var file in EngineSources(registry.GetModuleDir("collision")))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"^\s*using\s+(MonoDreams[\w\.]*)\s*;", RegexOptions.Multiline))
            {
                var ns = match.Groups[1].Value;
                if (!owners.TryGetValue(ns, out var owningModules)) continue; // not a module-owned namespace
                if (owningModules.Overlaps(installed)) continue;

                offenders.Add($"{Path.GetFileName(file)} imports '{ns}' (owned by: {string.Join(", ", owningModules.Order())})");
            }
        }

        Assert.True(offenders.Count == 0,
            "collision source imports engine namespaces no declared dependency owns — add the owning module to "
            + $"MonoDreams/collision/module.json:\n  {string.Join("\n  ", offenders)}");
    }

    /// <summary>Maps each <c>namespace</c> declared under <c>MonoDreams/</c> to the module(s) declaring it.</summary>
    private static Dictionary<string, HashSet<string>> NamespaceOwnersByModule(string modulesDir)
    {
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var moduleDir in Directory.EnumerateDirectories(modulesDir))
        {
            if (!File.Exists(Path.Combine(moduleDir, "module.json"))) continue;
            var module = Path.GetFileName(moduleDir);

            foreach (var file in EngineSources(moduleDir))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"^\s*namespace\s+([\w\.]+)", RegexOptions.Multiline))
                {
                    if (!owners.TryGetValue(match.Groups[1].Value, out var set))
                        owners[match.Groups[1].Value] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(module);
                }
            }
        }
        return owners;
    }

    /// <summary>A module's shipping C# sources — <c>demo/</c> is excluded from `add` unless --with-demo.</summary>
    private static IEnumerable<string> EngineSources(string moduleDir) =>
        Directory.EnumerateFiles(moduleDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Contains("demo"));
}
