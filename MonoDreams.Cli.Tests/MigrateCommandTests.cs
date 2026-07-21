using System.Text.Json;
using MonoDreams.Cli.Commands;
using MonoDreams.LevelEditor.Serialization; // source-linked into the CLI (SceneData / CanonicalJson / *Migration)

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Contract protection for the umbrella <c>monodreams migrate</c> command (<see cref="Runner.RunMigrate"/>):
/// it applies every lift in version order (v1→v2 colliders, then v2→v3 camera), so a v1 file goes STRAIGHT
/// to v3 in one pass; it honours <c>--dry-run</c>, recurses a directory, reports a per-file summary of which
/// lifts ran, is idempotent (a v3 file is a no-op), and exits 2 on a missing / unparseable path. The
/// TRANSFORMS themselves are proven in the engine's <c>CameraMigrationTests</c> / <c>SceneMigrationTests</c>;
/// these tests protect the command wiring + output + exit codes (the source-linked
/// <see cref="SceneMigration"/> guarantees byte-canonical parity with the engine writer).
/// </summary>
[Collection("Console (non-parallel: swaps Console.Out)")]
public class MigrateCommandTests
{
    private const string Box = "core.BoxCollider";
    private const string Cam = "core.Camera";
    private const string Xf = "core.Transform";
    private const string Ei = "core.EntityInfo";

    private static JsonElement El(object v) => CanonicalJson.SerializeToElement(v);

    /// <summary>A canonical version-1 scene with a body entity carrying an embedded box (bounds) AND a
    /// legacy <c>camera</c> block — both lifts apply, straight to v3.</summary>
    private static string V1SceneWithBoxAndCameraBlock()
    {
        var scene = new SceneData { Version = 1 };
        scene.Camera = new SceneCameraData { Position = new[] { 100f, 50f }, Zoom = 2f, Rotation = 0f };
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
    public void Migrate_OnV1File_MigratesStraightToVersion3_ReportsBothLifts()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            var path = Path.Combine(dir, "level.mdscene");
            File.WriteAllText(path, V1SceneWithBoxAndCameraBlock());

            var (output, exit) = RunCaptured(() => Runner.RunMigrate(path, dryRun: false));

            Assert.Equal(0, exit);
            Assert.Contains("migrated", output);
            Assert.Contains("version 3", output);
            Assert.Contains("colliders v1→v2", output);          // "v1→v2"
            Assert.Contains("camera v2→v3", output);             // "v2→v3"
            Assert.Contains("camera block lifted", output);

            var migrated = CanonicalJson.Deserialize<SceneData>(File.ReadAllText(path))!;
            Assert.Equal(3, migrated.Version);
            Assert.Null(migrated.Camera);
            Assert.Contains(migrated.Entities, e => e.Components.ContainsKey(Cam));  // camera entity present
            Assert.False(migrated.Entities[0].Components.ContainsKey(Box));          // box moved to a child
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Migrate_DryRun_LeavesFileUntouched()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            var path = Path.Combine(dir, "level.mdscene");
            var original = V1SceneWithBoxAndCameraBlock();
            File.WriteAllText(path, original);

            var (output, exit) = RunCaptured(() => Runner.RunMigrate(path, dryRun: true));

            Assert.Equal(0, exit);
            Assert.Contains("dry-run", output);
            Assert.Contains("would migrate", output);
            Assert.Equal(original, File.ReadAllText(path)); // nothing written
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Migrate_Directory_RecursesBothExtensions_PrefabGetsNoCamera()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "nested"));
            File.WriteAllText(Path.Combine(dir, "a.mdscene"), V1SceneWithBoxAndCameraBlock());
            File.WriteAllText(Path.Combine(dir, "nested", "b.mdprefab"), V1SceneWithBoxAndCameraBlock());
            File.WriteAllText(Path.Combine(dir, "ignore.txt"), "not a scene");

            var (output, exit) = RunCaptured(() => Runner.RunMigrate(dir, dryRun: false));

            Assert.Equal(0, exit);
            Assert.Contains("2 scanned", output);                    // only the two native files

            var sceneOut = CanonicalJson.Deserialize<SceneData>(File.ReadAllText(Path.Combine(dir, "a.mdscene")))!;
            Assert.Equal(3, sceneOut.Version);
            Assert.Contains(sceneOut.Entities, e => e.Components.ContainsKey(Cam)); // scene gets a camera entity

            // The prefab is bumped to v3 but NEVER gets a camera entity (a prefab is a class).
            var prefabOut = CanonicalJson.Deserialize<SceneData>(File.ReadAllText(Path.Combine(dir, "nested", "b.mdprefab")))!;
            Assert.Equal(3, prefabOut.Version);
            Assert.DoesNotContain(prefabOut.Entities, e => e.Components.ContainsKey(Cam));

            Assert.Equal("not a scene", File.ReadAllText(Path.Combine(dir, "ignore.txt")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Migrate_AlreadyV3_ReportsNoChange_Idempotent()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            // Migrate a v1 file to v3, then migrate the RESULT again — the second run is a byte-identical no-op.
            var path = Path.Combine(dir, "current.mdscene");
            File.WriteAllText(path, V1SceneWithBoxAndCameraBlock());
            RunCaptured(() => Runner.RunMigrate(path, dryRun: false));
            var afterFirst = File.ReadAllText(path);

            var (output, exit) = RunCaptured(() => Runner.RunMigrate(path, dryRun: false));

            Assert.Equal(0, exit);
            Assert.Contains("already current", output);
            Assert.Equal(afterFirst, File.ReadAllText(path)); // byte-identical no-op
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Migrate_MissingPath_ExitsCode2()
    {
        var missing = Path.Combine(Path.GetTempPath(), "md-missing-" + Guid.NewGuid().ToString("N"), "nope.mdscene");
        var (_, exit) = RunCaptured(() => Runner.RunMigrate(missing, dryRun: false));
        Assert.Equal(2, exit);
    }

    [Fact]
    public void Migrate_UnparseableFile_ExitsCode2()
    {
        var dir = CliTestSupport.NewTempDir("migrate");
        try
        {
            var path = Path.Combine(dir, "broken.mdscene");
            File.WriteAllText(path, "{ this is not valid json");

            var (_, exit) = RunCaptured(() => Runner.RunMigrate(path, dryRun: false));
            Assert.Equal(2, exit);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
