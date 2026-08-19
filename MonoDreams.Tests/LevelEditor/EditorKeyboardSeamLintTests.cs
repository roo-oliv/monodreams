using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// The editor overlay reads the keyboard at ONE injected seam. Every editor system that reads the
/// hardware keyboard exposes an optional <c>Func&lt;KeyboardState&gt;? getKeyboardState</c> whose default
/// is <see cref="Microsoft.Xna.Framework.Input.Keyboard.GetState"/> — convenient for a windowed game and
/// invisible when it is wrong. <c>EditorOverlay</c> therefore takes one <c>readKeyboard</c> parameter and
/// must hand it to <b>every</b> such system it constructs; a host that needs input determinism (the demos'
/// byte-identity precheck passes <c>DemoKeyboard.Read</c>) then pins the whole editor surface by
/// construction.
///
/// <para>This is a source scan (the <c>EditorThemeLintTests</c> idiom) rather than a behavioural test
/// because the failure it guards is silent: the overlay's readers are woven <c>RunNormally</c>, so a
/// missed one is inert exactly until an editor panel/menu/dialog is open — a run stays green while it
/// samples the developer's keyboard, and the first red byte-diff is blamed on the change under test.</para>
/// </summary>
public class EditorKeyboardSeamLintTests
{
    /// <summary>The overlay's own seam parameter — the ONE thing every construction below must forward.</summary>
    private const string OverlaySeamParameter = "readKeyboard";

    /// <summary>The parameter name every keyboard-reading editor system exposes.</summary>
    private const string SystemSeamParameter = "getKeyboardState";

    /// <summary>The seam forwarded by VALUE — <c>getKeyboardState: readKeyboard</c>. Accepting the label
    /// alone would pass <c>getKeyboardState: Keyboard.GetState</c>, which reinstates the engine default
    /// this lint exists to forbid while reading as compliant.</summary>
    private static readonly Regex SeamForwarded = new(
        $@"\b{SystemSeamParameter}\s*:\s*{OverlaySeamParameter}\b");

    [Fact]
    public void EveryKeyboardReaderTheOverlayBuilds_IsHandedTheOverlaysOneSeam()
    {
        var overlay = StripLineComments(File.ReadAllText(OverlayPath()));

        Assert.Contains(
            $"Func<KeyboardState>? {OverlaySeamParameter}", overlay, StringComparison.Ordinal);

        var seamTypes = KeyboardReadingSystems();
        Assert.True(
            seamTypes.Count >= 5,
            $"found only {seamTypes.Count} level-editor system(s) exposing a '{SystemSeamParameter}' seam " +
            "— the scan, not the wiring, is what broke (the panels, the dialog, the context menu, the modal " +
            "transform and the shortcut system all own one).");

        var checkedConstructions = 0;
        var offenders = new List<string>();

        foreach (var type in seamTypes.OrderBy(name => name, StringComparer.Ordinal))
        {
            foreach (Match match in Regex.Matches(overlay, $@"\bnew\s+{Regex.Escape(type)}\s*\("))
            {
                checkedConstructions++;
                var arguments = ArgumentsAt(overlay, match.Index + match.Length - 1);
                // The argument's VALUE, not merely its label: `getKeyboardState: Keyboard.GetState`
                // carries the label while restoring the very default the seam exists to replace.
                if (SeamForwarded.IsMatch(arguments)) continue;
                offenders.Add($"EditorOverlay.cs:{LineOf(overlay, match.Index)}: new {type}(…) without " +
                              $"{SystemSeamParameter}: {OverlaySeamParameter}");
            }
        }

        Assert.True(
            checkedConstructions >= 6,
            $"only {checkedConstructions} keyboard-reading system construction(s) found in EditorOverlay — " +
            "the overlay builds both panels, the dialog, the context menu, the modal transform and the " +
            "shortcut system, so a lower count means this lint stopped seeing them.");

        Assert.True(
            offenders.Count == 0,
            "Every keyboard-reading editor system the overlay constructs must be handed the overlay's ONE " +
            $"'{OverlaySeamParameter}' seam — the parameter defaults to Keyboard.GetState inside the engine, " +
            "so an omission reads the hardware while the composing host believes input is pinned. " +
            "Offenders:\n" + string.Join("\n", offenders));
    }

    /// <summary>The shortcut system owns the chord tracker, so its seam must reach it too — otherwise the
    /// overlay pins the system while the tracker underneath it keeps reading the hardware.</summary>
    [Fact]
    public void TheShortcutSystemForwardsItsSeamToTheChordTracker()
    {
        var source = StripLineComments(File.ReadAllText(
            Path.Combine(ModuleRoot(), "System", "EditorShortcutSystem.cs")));

        var construction = Regex.Match(source, @"\bnew\s+KeyChordTracker\s*\(");
        Assert.True(construction.Success, "EditorShortcutSystem is expected to build the chord tracker");

        var arguments = ArgumentsAt(source, construction.Index + construction.Length - 1);
        Assert.Contains(SystemSeamParameter, arguments, StringComparison.Ordinal);
    }

    /// <summary>Level-editor system types whose constructor exposes the keyboard seam — read from source so
    /// a system added later is covered without touching this test.</summary>
    private static HashSet<string> KeyboardReadingSystems()
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(ModuleRoot(), "*.cs", SearchOption.AllDirectories))
        {
            var source = StripLineComments(File.ReadAllText(path));
            if (!source.Contains($"Func<KeyboardState>? {SystemSeamParameter}", StringComparison.Ordinal)) continue;

            foreach (Match match in Regex.Matches(source, @"\b(?:sealed\s+|public\s+|partial\s+)*class\s+(\w+)"))
                types.Add(match.Groups[1].Value);
        }

        return types;
    }

    private static string ModuleRoot() =>
        Path.Combine(GameTestRunner.RepoRoot(), "MonoDreams", "level-editor");

    private static string OverlayPath() =>
        Path.Combine(ModuleRoot(), "Composition", "EditorOverlay.cs");

    private static string StripLineComments(string source) => Regex.Replace(source, @"//[^\n]*", "");

    private static int LineOf(string source, int index) => source.Take(index).Count(c => c == '\n') + 1;

    /// <summary>The text between the parenthesis at <paramref name="openParenIndex"/> and its MATCHING
    /// close, so a nested lambda argument is read whole.</summary>
    private static string ArgumentsAt(string source, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < source.Length; i++)
        {
            if (source[i] == '(') depth++;
            else if (source[i] == ')' && --depth == 0) return source[(openParenIndex + 1)..i];
        }

        return source[(openParenIndex + 1)..];
    }
}
