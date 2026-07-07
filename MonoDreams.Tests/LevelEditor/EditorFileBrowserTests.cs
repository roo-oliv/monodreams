#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure <see cref="EditorFileBrowser"/> navigation model (FW2 navigator): it lists a
/// directory's subfolders + <c>.mdscene</c> files (filtering non-scene files, classifying folders vs
/// files), descends into a subfolder, climbs <b>bounded at the project root</b> (never escaping into
/// the OS filesystem), and resolves a scene id to an absolute path under the current dir. All driven
/// by an <b>injected fake dir listing</b> — no real filesystem.
/// </summary>
public class EditorFileBrowserTests
{
    private static string Norm(string p) => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // A synthetic project tree: root (the up-boundary = the dir holding game.mdproj) is <base>/Content;
    // it opens at <base>/Content/Levels; a nested props/ folder holds one more scene.
    private static EditorFileBrowser Make(out string root, out string levels, out string props)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "mdbrowser");
        root = Path.Combine(baseDir, "Content");
        levels = Path.Combine(root, "Levels");
        props = Path.Combine(levels, "props");
        var tree = new Dictionary<string, RawDirectory>(StringComparer.OrdinalIgnoreCase)
        {
            [Norm(root)] = new(true, new[] { "Levels" }, new[] { "game.mdproj" }, null),
            [Norm(levels)] = new(true, new[] { "props" }, new[] { "island.mdscene", "arena.mdscene", "notes.txt" }, null),
            [Norm(props)] = new(true, Array.Empty<string>(), new[] { "tree.mdscene" }, null),
        };
        var r = root; var l = levels;
        return new EditorFileBrowser(dir =>
            tree.TryGetValue(Norm(dir), out var d)
                ? d
                : new RawDirectory(true, Array.Empty<string>(), Array.Empty<string>(), "Empty folder."));
    }

    private static BrowserRoots Roots(string root, string initial) => new(true, root, initial, null);

    [Fact]
    public void Open_ListsScenes_FiltersNonSceneFiles_AndClassifiesFolders()
    {
        var browser = Make(out var root, out var levels, out _);
        browser.Open(Roots(root, levels));

        Assert.True(browser.Resolved);
        Assert.Equal(Norm(levels), Norm(browser.CurrentDir!));
        Assert.Equal(new[] { "props" }, browser.Directories);          // subfolder
        Assert.Equal(new[] { "arena", "island" }, browser.Files);       // .mdscene ids, sorted; notes.txt filtered
        Assert.Equal(3, browser.EntryCount);                            // 1 folder + 2 files
        Assert.True(browser.IsDirectory(0));                            // folders precede files
        Assert.False(browser.IsDirectory(1));
    }

    [Fact]
    public void Enter_DescendsIntoASubfolder()
    {
        var browser = Make(out var root, out var levels, out var props);
        browser.Open(Roots(root, levels));

        Assert.True(browser.Enter("props"));
        Assert.Equal(Norm(props), Norm(browser.CurrentDir!));
        Assert.Equal(new[] { "tree" }, browser.Files);
        Assert.Empty(browser.Directories);
    }

    [Fact]
    public void Enter_UnknownFolder_IsANoOp()
    {
        var browser = Make(out var root, out var levels, out _);
        browser.Open(Roots(root, levels));

        Assert.False(browser.Enter("does-not-exist"));
        Assert.Equal(Norm(levels), Norm(browser.CurrentDir!));
    }

    [Fact]
    public void Up_ClimbsButIsBoundedAtTheProjectRoot()
    {
        var browser = Make(out var root, out var levels, out _);
        browser.Open(Roots(root, levels));

        Assert.True(browser.CanGoUp);          // Levels is below the root
        browser.Up();
        Assert.Equal(Norm(root), Norm(browser.CurrentDir!)); // climbed to the root (Content)

        Assert.False(browser.CanGoUp);         // at the root boundary
        browser.Up();                          // a no-op — never escapes above the project root
        Assert.Equal(Norm(root), Norm(browser.CurrentDir!));
    }

    [Fact]
    public void FilePath_ResolvesTheSceneUnderTheCurrentDir()
    {
        var browser = Make(out var root, out var levels, out _);
        browser.Open(Roots(root, levels));

        Assert.Equal(Norm(Path.Combine(levels, "island.mdscene")), Norm(browser.FilePath("island")!));

        browser.Enter("props");
        Assert.Equal(Norm(Path.Combine(levels, "props", "hut.mdscene")), Norm(browser.FilePath("hut")!));
    }

    [Fact]
    public void Breadcrumb_ShowsThePathFromTheRootLeaf()
    {
        var browser = Make(out var root, out var levels, out _);
        browser.Open(Roots(root, levels));
        Assert.Equal(new[] { "Content", "Levels" }, browser.Breadcrumb);

        browser.Enter("props");
        Assert.Equal(new[] { "Content", "Levels", "props" }, browser.Breadcrumb);
        Assert.Equal("Content / Levels / props", browser.BreadcrumbText);
    }

    [Fact]
    public void Unresolved_ShowsTheMessage_AndListsNothing()
    {
        var browser = Make(out _, out _, out _);
        browser.Open(new BrowserRoots(false, null, null, "No project root resolved. Set MONODREAMS_PROJECT_ROOT."));

        Assert.False(browser.Resolved);
        Assert.Equal("No project root resolved. Set MONODREAMS_PROJECT_ROOT.", browser.Message);
        Assert.Equal(0, browser.EntryCount);
        Assert.False(browser.CanGoUp);
        Assert.Null(browser.FilePath("island"));
    }
}
