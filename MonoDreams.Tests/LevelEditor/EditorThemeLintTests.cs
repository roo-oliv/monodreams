using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the editor-shell UI/UX design §1.3 <b>palette lint</b>: every color in the level-editor
/// module is an <c>EditorTheme</c> role, so the strict palette can never drift. This source-scan test
/// (the <c>SceneLint</c>/ship-lint pattern) asserts that <b>no <c>.cs</c> file under
/// <c>MonoDreams/level-editor/</c> — except <c>EditorTheme.cs</c> — constructs a raw color
/// (<c>new Color(</c>) or names an XNA color token (<c>Color.White</c>, <c>Color.Cyan</c>, …).
/// Adding a color to the module therefore means adding a role to <c>EditorTheme</c>, consciously.
///
/// Scans git-TRACKED module source (like <c>SceneLintTests</c>): the lint gates what the repo commits.
/// Line comments (<c>//</c> / <c>///</c>) are stripped before scanning so doc-comments that mention a
/// role are never false positives; the module has no block comments or strings carrying a color token
/// (verified by this test staying green), so those are deliberately not stripped.
/// </summary>
public class EditorThemeLintTests
{
    /// <summary>The one file allowed to name raw/XNA colors — the single source of the palette.</summary>
    private const string ThemeFile = "EditorTheme.cs";

    /// <summary>
    /// The tight allowlist of <c>Color.&lt;member&gt;</c> tokens that are NOT palette values, so they
    /// stay legal outside the theme:
    /// <list type="bullet">
    /// <item><c>Lerp</c> — blends colors that were ALREADY chosen (e.g. interpolating existing vertex
    /// colors while clipping a mesh in <c>OverlayMeshClip</c>); it introduces no new palette value.</item>
    /// <item><c>Transparent</c> — the "no fill" sentinel (A==0), which the premultiplied-alpha mesh
    /// rule explicitly blesses (a zero-alpha fill is skipped, not blown out) — used for checkbox-off /
    /// minus-bar / default box fills.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> AllowedColorMembers = new(StringComparer.Ordinal) { "Lerp", "Transparent" };

    // `Color.<Member>` where `Color` is the TYPE (not a `.Color` field access — the negative lookbehind
    // rejects a preceding word char or dot, so `sprite.Color` / `a.Color.R` never match).
    private static readonly Regex NamedColorToken = new(@"(?<![A-Za-z0-9_.])Color\.([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    // A raw color construction: `new Color(` (any whitespace). Target-typed `new(...)` is deliberately
    // NOT matched — it is used to reconstruct a Color from persisted bytes in the component
    // deserializer, which is data round-tripping, not a palette literal.
    private static readonly Regex RawColorCtor = new(@"new\s+Color\s*\(", RegexOptions.Compiled);

    [Fact]
    public void EveryModuleColorIsAThemeRole_NoRawColorsOutsideEditorTheme()
    {
        var files = TrackedModuleSources();
        Assert.NotEmpty(files); // the module has source, and EditorTheme.cs is excluded below

        var offenders = new List<string>();
        foreach (var file in files)
        {
            if (string.Equals(Path.GetFileName(file), ThemeFile, StringComparison.Ordinal)) continue;

            var lineNumber = 0;
            foreach (var raw in File.ReadLines(file))
            {
                lineNumber++;
                var code = StripLineComment(raw);
                if (code.Length == 0) continue;

                if (RawColorCtor.IsMatch(code))
                    offenders.Add($"{Path.GetFileName(file)}:{lineNumber}: raw 'new Color(' — add an EditorTheme role");

                foreach (Match m in NamedColorToken.Matches(code))
                {
                    var member = m.Groups[1].Value;
                    if (!AllowedColorMembers.Contains(member))
                        offenders.Add($"{Path.GetFileName(file)}:{lineNumber}: named token 'Color.{member}' — add an EditorTheme role");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Every level-editor color must be an EditorTheme role (design §1.3). Offenders:\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void TheThemeFileItselfIsScannedOut_AndExists()
    {
        // Sanity: EditorTheme.cs is present (so the exclusion is meaningful) and DOES name raw colors
        // (it is the one place allowed to) — proving the exclusion is load-bearing, not vacuous.
        var theme = Path.Combine(ModuleRoot(), "UI", ThemeFile);
        Assert.True(File.Exists(theme), $"expected the single-source palette at {theme}");
        var text = File.ReadAllText(theme);
        Assert.True(RawColorCtor.IsMatch(text) || NamedColorToken.IsMatch(text),
            "EditorTheme.cs is expected to be the one file that names raw/XNA colors");
    }

    /// <summary>Removes a line comment (<c>//</c> … EOL, which also covers <c>///</c> doc comments) so a
    /// comment mentioning a role is never a false positive. Returns the code portion of the line.</summary>
    private static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return (idx >= 0 ? line[..idx] : line);
    }

    /// <summary>Absolute paths of the git-tracked <c>*.cs</c> files under <c>MonoDreams/level-editor/</c>.
    /// Uses <c>git ls-files</c> (the repo's ship-lint idiom) so the gate covers committed module source.</summary>
    private static List<string> TrackedModuleSources()
    {
        var repoRoot = RepoRoot();
        var psi = new ProcessStartInfo("git", "ls-files -- MonoDreams/level-editor")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(rel => rel.EndsWith(".cs", StringComparison.Ordinal))
            .Select(rel => Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .ToList();
    }

    private static string ModuleRoot() =>
        Path.Combine(RepoRoot(), "MonoDreams", "level-editor");

    /// <summary>Walks up from the test base dir to the repo root (the directory containing
    /// <c>MonoDreams.Examples.Core</c>), mirroring <c>SceneLintTests.RepoPath</c>.</summary>
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "MonoDreams.Examples.Core")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return dir!;
    }
}
