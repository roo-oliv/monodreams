using System.Text.Json;
using MonoDreams.Cli.Commands;
using MonoDreams.LevelEditor.Serialization; // source-linked into the CLI (SceneData / CanonicalJson / ColliderMigration)

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Contract protection for the <c>monodreams migrate-colliders</c> command (<see cref="Runner.RunMigrateColliders"/>):
/// it migrates version-1 native scenes/prefabs to the version-2 colliders-as-entities shape, honours
/// <c>--dry-run</c>, recurses a directory, reports a per-file summary, and exits 2 on a missing path. The
/// migration TRANSFORM itself is proven in the engine's <c>ColliderMigrationTests</c>; these tests protect
/// the command wiring + output + exit codes (the source-linked <see cref="ColliderMigration"/> guarantees
/// byte-canonical parity with the engine writer).
/// </summary>
[Collection("Console (non-parallel: swaps Console.Out)")]
public class MigrateCollidersCommandTests
{
    private const string Box = "core.BoxCollider";
    private const string Xf = "core.Transform";
    private const string Ei = "core.EntityInfo";

    private static JsonElement El(object v) => CanonicalJson.SerializeToElement(v);

    /// <summary>A canonical version-1 scene with a body entity carrying an embedded box (bounds) — the
    /// pre-CE shape the migrator rewrites.</summary>
    private static string V1SceneWithBoxOnBody()
    {
        var scene = new SceneData { Version = 1 };
        var e = new SceneEntityData { Id = 0 };
        e.Components[Ei] = El(new { type = "Wall", name = "wall" });
        e.Components["core.RigidBody"] = El(new
        {
            mass = 1f, gravityActive = true, gravityFactor = 1f, isKinematic = false,
            freezeRotation = false, freezePositionX = false, freezePositionY = false,
        });
        e.Components[Box] = El(new { bounds = new[] { -8, -8, 16, 16 }, activeLayers = new[] { -1 }, passive = true, enabled = true });
        e.Components[Xf] = El(new { position = new[] { 10f, 20f }, rotation = 0f, scale = new[] { 1f, 1f }, origin = new[] { 0f, 0f } });
        scene.Entities.Add(e);
        return CanonicalJson.Serialize(scene);
    }

    private static (string Output, int ExitCode) RunCaptured(Action body)
    {
        var previousOut = Console.Out;
        var previousExit = Environment.ExitCode;
        Environment.ExitCode = 0;
        var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            body();
            return (sw.ToString(), Environment.ExitCode);
        }
        finally
        {
            Console.SetOut(previousOut);
            Environment.ExitCode = previousExit;
        }
    }

    [Fact]
    public void MigrateColliders_OnV1File_MigratesToVersion2_ReportsChildAdded()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            var path = Path.Combine(dir, "level.mdscene");
            File.WriteAllText(path, V1SceneWithBoxOnBody());

            var (output, exit) = RunCaptured(() => Runner.RunMigrateColliders(path, dryRun: false));

            Assert.Equal(0, exit);
            Assert.Contains("migrated", output);
            Assert.Contains("collider child entity", output);

            var migrated = CanonicalJson.Deserialize<SceneData>(File.ReadAllText(path))!;
            Assert.Equal(2, migrated.Version);
            Assert.Equal(2, migrated.Entities.Count);                 // body + new collider child
            Assert.False(migrated.Entities[0].Components.ContainsKey(Box)); // box stripped from the body
            Assert.True(migrated.Entities[1].Components.ContainsKey(Box));   // box on the child
            Assert.Equal(0, migrated.Entities[1].Parent);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MigrateColliders_DryRun_LeavesFileUntouched()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            var path = Path.Combine(dir, "level.mdscene");
            var original = V1SceneWithBoxOnBody();
            File.WriteAllText(path, original);

            var (output, exit) = RunCaptured(() => Runner.RunMigrateColliders(path, dryRun: true));

            Assert.Equal(0, exit);
            Assert.Contains("dry-run", output);
            Assert.Contains("would migrate", output);
            Assert.Equal(original, File.ReadAllText(path)); // nothing written
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MigrateColliders_Directory_RecursesAndMigratesBothExtensions()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "nested"));
            File.WriteAllText(Path.Combine(dir, "a.mdscene"), V1SceneWithBoxOnBody());
            File.WriteAllText(Path.Combine(dir, "nested", "b.mdprefab"), V1SceneWithBoxOnBody());
            File.WriteAllText(Path.Combine(dir, "ignore.txt"), "not a scene");

            var (output, exit) = RunCaptured(() => Runner.RunMigrateColliders(dir, dryRun: false));

            Assert.Equal(0, exit);
            Assert.Contains("2 scanned", output);                    // only the two native files
            Assert.Equal(2, CanonicalJson.Deserialize<SceneData>(File.ReadAllText(Path.Combine(dir, "a.mdscene")))!.Version);
            Assert.Equal(2, CanonicalJson.Deserialize<SceneData>(File.ReadAllText(Path.Combine(dir, "nested", "b.mdprefab")))!.Version);
            Assert.Equal("not a scene", File.ReadAllText(Path.Combine(dir, "ignore.txt")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MigrateColliders_AlreadyVersion2_ReportsNoChange()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            var scene = new SceneData(); // version 2 by default
            var e = new SceneEntityData { Id = 0 };
            e.Components[Ei] = El(new { type = "Prop", name = "p" });
            e.Components[Xf] = El(new { position = new[] { 0f, 0f }, rotation = 0f, scale = new[] { 1f, 1f }, origin = new[] { 0f, 0f } });
            scene.Entities.Add(e);
            var v2 = CanonicalJson.Serialize(scene);

            var path = Path.Combine(dir, "current.mdscene");
            File.WriteAllText(path, v2);

            var (output, exit) = RunCaptured(() => Runner.RunMigrateColliders(path, dryRun: false));

            Assert.Equal(0, exit);
            Assert.Contains("already version 2", output);
            Assert.Equal(v2, File.ReadAllText(path)); // byte-identical no-op
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MigrateColliders_MissingPath_ExitsCode2()
    {
        var missing = Path.Combine(Path.GetTempPath(), "md-missing-" + Guid.NewGuid().ToString("N"), "nope.mdscene");
        var (output, exit) = RunCaptured(() => Runner.RunMigrateColliders(missing, dryRun: false));
        Assert.Equal(2, exit);
    }

    [Fact]
    public void MigrateColliders_UnparseableFile_ExitsCode2()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            var path = Path.Combine(dir, "broken.mdscene");
            File.WriteAllText(path, "{ this is not valid json");

            var (output, exit) = RunCaptured(() => Runner.RunMigrateColliders(path, dryRun: false));
            Assert.Equal(2, exit);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
