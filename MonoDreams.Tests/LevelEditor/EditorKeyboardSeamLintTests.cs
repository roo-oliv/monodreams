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
/// <para>Forwarding is checked by VALUE, never by label, and the same rule reaches the ONE nested reader
/// (<c>EditorShortcutSystem</c>'s <c>KeyChordTracker</c>, whose own parameter carries the same default).
/// Because those scans enumerate the systems that already expose the seam, a third check bans any keyboard
/// read in the module that is not a declared seam's default — that is the one that sees a reader ADDED
/// outside the seam rather than a scan that broke.</para>
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

    /// <summary>The seam reaching a NESTED reader by VALUE: the enclosing system's own
    /// <c>getKeyboardState</c> parameter, passed positionally
    /// (<c>new KeyChordTracker(commandIsMeta, getKeyboardState)</c>) or under its own label
    /// (<c>getKeyboardState: getKeyboardState</c>). The identifier counts only where it is NOT immediately
    /// followed by <c>:</c> — a trailing colon makes the occurrence the argument's LABEL, and
    /// <c>getKeyboardState: null</c> / <c>getKeyboardState: Keyboard.GetState</c> both carry that label
    /// while handing the nested reader straight back to the hardware default. The lookbehind keeps a
    /// qualified or renamed carrier (<c>other.getKeyboardState</c>, <c>_getKeyboardState</c>) from
    /// counting: only the constructor parameter this lint can actually trace is accepted.</summary>
    private static readonly Regex SeamPassedByValue = new(
        $@"(?<![\w.]){SystemSeamParameter}\b(?!\s*:)");

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
        Assert.True(
            SeamPassedByValue.IsMatch(arguments),
            $"EditorShortcutSystem builds its KeyChordTracker as 'new KeyChordTracker({arguments.Trim()})', " +
            $"which never hands the tracker the system's own '{SystemSeamParameter}' parameter as a VALUE. " +
            "The tracker's own parameter defaults to Keyboard.GetState (KeyChordTracker.cs), so the label " +
            $"alone — '{SystemSeamParameter}: null', '{SystemSeamParameter}: Keyboard.GetState' — leaves the " +
            "editor chords (Delete, ⌘Z, G/S/R) sampling the developer's keyboard while the overlay above " +
            "reports the whole surface as pinned.");
    }

    /// <summary>The lint above is only as strong as its predicate, and the predicate it replaced accepted
    /// the argument's LABEL. This pins the predicate itself against the exact mutations that must red —
    /// a seam-shaped argument whose value is null or the hardware default.</summary>
    [Fact]
    public void TheNestedSeamPredicate_TakesTheValue_NotTheLabel()
    {
        foreach (var accepted in new[]
                 {
                     $"commandIsMeta, {SystemSeamParameter}",
                     $"commandIsMeta, {SystemSeamParameter}: {SystemSeamParameter}",
                     $"commandIsMeta,\n            {SystemSeamParameter}",
                 })
            Assert.True(
                SeamPassedByValue.IsMatch(accepted),
                $"the nested-seam predicate rejected a legitimate forward: 'new KeyChordTracker({accepted})'");

        foreach (var rejected in new[]
                 {
                     "commandIsMeta",
                     $"commandIsMeta, {SystemSeamParameter}: null",
                     $"commandIsMeta, {SystemSeamParameter}: Keyboard.GetState",
                     $"commandIsMeta, {SystemSeamParameter} : Keyboard.GetState",
                 })
            Assert.False(
                SeamPassedByValue.IsMatch(rejected),
                $"the nested-seam predicate accepted 'new KeyChordTracker({rejected})' — the construction " +
                "reads as compliant while the tracker falls back to Keyboard.GetState, which is the exact " +
                "silent hardware read this lint exists to forbid.");
    }

    /// <summary>The forwarding lint above enumerates the systems that already OPTED IN (it collects types
    /// from files carrying the seam declaration), so its floors detect a broken scan — never an ADDED
    /// reader. A level-editor system with a bare <c>var keys = Keyboard.GetState();</c> is matched by
    /// nothing: it is not a demo source, so the demos' raw-read ban never sees it either. This closes that
    /// hole from the other side — every keyboard read under the module must be the DEFAULT of a seam
    /// (<c>getKeyboardState ?? Keyboard.GetState</c>), and any file owning one must declare that seam in
    /// the exact shape <see cref="KeyboardReadingSystems"/> reads, so a new reader either lands inside BOTH
    /// lints or fails here.</summary>
    [Fact]
    public void EveryKeyboardReadInTheModule_IsTheDefaultOfADeclaredSeam()
    {
        var seamDefaults = 0;
        var offenders = new List<string>();

        foreach (var path in Directory.GetFiles(ModuleRoot(), "*.cs", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var source = StripLineComments(File.ReadAllText(path));
            var reads = Regex.Matches(source, @"\bKeyboard\s*\.\s*GetState\b");
            if (reads.Count == 0) continue;

            foreach (Match read in reads)
            {
                var line = RawLine(source, read.Index);
                if (Regex.IsMatch(line, $@"\b{SystemSeamParameter}\s*\?\?\s*Keyboard\s*\.\s*GetState\b"))
                {
                    seamDefaults++;
                    continue;
                }

                offenders.Add($"{Path.GetFileName(path)}:{LineOf(source, read.Index)}: {line.Trim()} " +
                              $"— a keyboard read that is not the '{SystemSeamParameter}' seam's default");
            }

            if (!source.Contains($"Func<KeyboardState>? {SystemSeamParameter}", StringComparison.Ordinal))
                offenders.Add($"{Path.GetFileName(path)}: reads the keyboard but declares no " +
                              $"'Func<KeyboardState>? {SystemSeamParameter}' parameter, so it is invisible to " +
                              $"{nameof(EveryKeyboardReaderTheOverlayBuilds_IsHandedTheOverlaysOneSeam)}");
        }

        Assert.True(
            seamDefaults >= 4,
            $"found only {seamDefaults} '{SystemSeamParameter} ?? Keyboard.GetState' seam default(s) under " +
            "MonoDreams/level-editor — the scan, not the wiring, is what broke (the two panels share one " +
            "class; the dialog, the context menu and the modal transform own the rest, while the shortcut " +
            "system forwards its nullable straight to the chord tracker).");

        Assert.True(
            offenders.Count == 0,
            "Every hardware keyboard read in the level-editor module must be the default of an injectable " +
            $"'{SystemSeamParameter}' seam — an inline Keyboard.GetState() cannot be pinned by the overlay's " +
            "one readKeyboard parameter, so under a byte-identity run (MONODREAMS_EDITOR=1 + an op plan) it " +
            "samples the developer's keyboard in one run of two and the pixel diff is blamed on the change " +
            "under test. Offenders:\n" + string.Join("\n", offenders));
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

    /// <summary>The whole source line containing <paramref name="index"/> — what a per-read rule is
    /// evaluated against, so the read and the seam it defaults must sit together.</summary>
    private static string RawLine(string source, int index)
    {
        var start = source.LastIndexOf('\n', Math.Min(index, source.Length - 1)) + 1;
        var end = source.IndexOf('\n', index);
        return end < 0 ? source[start..] : source[start..end];
    }

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
