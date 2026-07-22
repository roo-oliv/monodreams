using System;
using System.Collections.Generic;
using System.IO;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Screen;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// TD: the Demos host adopts the Examples screen-bound-scenes pattern (UX-C) — the six demo screens each
/// declare a <c>BoundSceneId</c> (the launcher / demo selector included, per the user), so the editor's
/// Scenes panel lists them as scenes and a demo Save lands <c>&lt;id&gt;.mdscene</c> in the DEMOS project
/// tree. These are pure, host-agnostic checks of the <see cref="SceneCatalog"/> merge and the
/// <see cref="EditorOverlay.SceneFilePath"/> write target under a Demos-shaped project context; the wiring
/// is exercised end-to-end by <c>DemosEditorOverlayTests</c>. The literal ids mirror the demo screens'
/// <c>BoundSceneId</c> constants (MonoDreams.Tests does not reference MonoDreams.Demos).
/// </summary>
public class DemosSceneWiringTests
{
    /// <summary>The six Demos <c>(screenName, boundSceneId, displayName)</c> bindings <c>Game1</c> registers.</summary>
    private static readonly (string Screen, string Scene, string Label)[] DemoBindings =
    {
        ("demos.launcher", "launcher", "Launcher"),
        ("demos.camera", "camera-demo", "Camera Demo"),
        ("demos.physics", "physics-demo", "Physics Demo"),
        ("demos.dialogue", "dialogue-demo", "Dialogue Demo"),
        ("demos.ui", "ui-demo", "UI Demo"),
        ("demos.audio", "audio-demo", "Audio Demo"),
    };

    private static IReadOnlyList<(string Name, ScreenInfo Info)> DemoScreens() =>
        Array.ConvertAll(DemoBindings, b => (b.Screen, new ScreenInfo(b.Label, b.Scene)));

    [Fact]
    public void SceneCatalog_WithTheSixDemoBindings_ResolvedProject_ListsSixNamedScenes()
    {
        var entries = SceneCatalog.Build(
            DemoScreens(),
            sceneIds: Array.Empty<string>(), // no scene files yet — the bindings ALONE populate the panel
            currentScreenName: "demos.launcher",
            currentSceneId: "launcher",
            projectResolved: true);

        // The six demos (the "(no scenes)" bug is gone), one named entry each, in registration order.
        Assert.Equal(6, entries.Count);
        for (var i = 0; i < DemoBindings.Length; i++)
        {
            Assert.Equal(DemoBindings[i].Scene, entries[i].SceneId);
            Assert.Equal(DemoBindings[i].Label, entries[i].Label);
            Assert.Equal(DemoBindings[i].Screen, entries[i].ScreenName);
        }
        Assert.True(entries[0].IsCurrent); // the launcher is the current scene
        Assert.DoesNotContain(entries, e => e.SceneId == EditorOverlay.DefaultSceneId); // never "untitled"
    }

    [Fact]
    public void FirstSave_OfADemoScene_TargetsTheDemosProjectLevelsTree_NotExamples()
    {
        // A Demos-shaped resolved context (env var → the Demos content root), mirroring a co-located run.
        const string demosRoot = "/repo/MonoDreams.Demos";
        var manifestPath = Path.Combine(demosRoot, "Content", GameProject.FileName);
        var ctx = EditorProjectContext.Resolve(
            baseDirectory: Path.Combine(demosRoot, "bin", "Debug", "net8.0") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: n => n == EditorProjectContext.ProjectRootVariable ? demosRoot : null,
            fileExists: p => p == manifestPath,
            readAllText: _ => CanonicalJson.Serialize(new GameProject { StartScene = "launcher" }));

        Assert.True(ctx.Resolved);
        Assert.Equal(Path.Combine(demosRoot, "Content"), ctx.ProjectRoot);

        // A demo Save targets <DemosRoot>/Content/Levels/<id>.mdscene — the Demos source tree, never
        // Examples'. (SceneSourceWriteTests proves the write itself via IPlatformServices; this pins the
        // Demos-tree TARGET the resolved context yields — fix 2.6.)
        Assert.Equal(
            Path.Combine(demosRoot, "Content", "Levels", "physics-demo" + SceneWriter.SceneFileExtension),
            EditorOverlay.SceneFilePath(ctx, "physics-demo"));
    }
}
