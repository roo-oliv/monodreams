#nullable enable
using System;
using System.IO;
using DefaultEcs;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the empty-save guard (UX-C §3.5, pre-mortem #4): the pure refusal predicate
/// <see cref="EditorOverlay.EmptySaveRefused"/> as an explicit truth table — including the
/// "never-loaded scene, existing file" case (file existence is deliberately NOT a factor: zero roots
/// + never-loaded refuses regardless) — plus the reader's <c>SceneWasLoaded</c> signal it reads
/// (starts false, flips true on the first successful load, so a deliberately-emptied loaded scene may
/// still save empty).
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class EmptySaveGuardTests
{
    [Theory]
    [InlineData(0, false, true)]   // zero roots + never loaded → REFUSED (even if the target file exists)
    [InlineData(0, true, false)]   // zero roots + a loaded-then-emptied scene → allowed (designer intent)
    [InlineData(1, false, false)]  // has content → allowed (never-loaded is irrelevant)
    [InlineData(5, true, false)]
    public void EmptySaveRefused_TruthTable(int sceneRootCount, bool sceneWasLoaded, bool refused) =>
        Assert.Equal(refused, EditorOverlay.EmptySaveRefused(sceneRootCount, sceneWasLoaded));

    [Fact]
    public void SceneReader_SceneWasLoaded_StartsFalse_FlipsTrueAfterALoad()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);

        // A minimal valid (empty) scene, produced through the canonical writer, then read back.
        using var src = new World();
        var sceneJson = CanonicalJson.Serialize(new SceneWriter(serializer).BuildScene(src));
        const string path = "/proj/Content/Levels/empty.mdscene";

        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = new ReadOnlyPlatform(path, sceneJson);
            using var world = new World();
            var reader = new SceneReaderSystem(world, serializer, content: null!, loadTexture: _ => null!);
            Assert.False(reader.SceneWasLoaded);

            world.Publish(new LoadSceneRequest(path, fromContent: false)); // host-filesystem read → ReadAllText

            Assert.True(reader.SceneWasLoaded); // a real load happened → the empty-save guard now permits empty
        }
        finally { PlatformServices.Current = previous; }
    }

    private sealed class ReadOnlyPlatform : IPlatformServices
    {
        private readonly string _path, _contents;
        public ReadOnlyPlatform(string path, string contents) { _path = path; _contents = contents; }
        public string BaseDirectory => "/";
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => path == _path;
        public string ReadAllText(string path) => path == _path ? _contents : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) => suggestedFileName;
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }
}
