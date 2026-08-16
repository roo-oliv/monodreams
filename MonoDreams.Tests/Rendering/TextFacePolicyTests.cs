using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System.Draw;
using MonoDreams.Text;
using MonoGame.Extended.BitmapFonts;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering-text premises "Per-face folds run before the reveal slice and before layout"
/// and "A dropped glyph is reported once per face + character; silence is opt-in" (issue #92).
///
/// The failure this replaces: the bitmap draw path renders a character the face lacks as NOTHING —
/// a word quietly loses a letter, at dev time nobody notices because the test strings happen to be
/// covered, and in production "São Paulo" ships as "So Paulo". The fix is two halves that must hold
/// together: the per-face fold rewrites what it can BEFORE layout, and whatever is left over is
/// logged loudly — once, not once per frame — unless the face explicitly opted into silent drop.
///
/// The class joins the PlatformServices collection because one test swaps
/// <see cref="PlatformServices.Current"/> and drives the process-global <see cref="Logger"/> to
/// observe the emitted line.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class TextFacePolicyTests
{
    private const string AsciiFace = "AsciiFace";
    private const string MonoFace = "MonoCapsFace";

    private static BitmapFont AsciiFont(string face = AsciiFace) =>
        TestBitmapFonts.WithGlyphs(face, TestBitmapFonts.Ascii);

    // ─── folding ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fold_IsPerFace()
    {
        var display = AsciiFont();
        var mono = TestBitmapFonts.WithGlyphs(MonoFace, "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,-");
        var faces = new TextFacePolicyRegistry()
            .Register(display, new TextFacePolicy(TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis)))
            .Register(mono, new TextFacePolicy(TextFold.Chain(
                TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals,
                TextFold.StripDiacritics, TextFold.Upcase)));

        Assert.Equal("São - hoje...", faces.Fold(display, "São — hoje…"));
        Assert.Equal("SAO - HOJE...", faces.Fold(mono, "São — hoje…"));
    }

    [Fact]
    public void Fold_IsAPassThrough_ForAnUnregisteredFace()
    {
        var faces = new TextFacePolicyRegistry();
        const string text = "São — hoje…";

        Assert.Same(text, faces.Fold(AsciiFont(), text));
        Assert.Same(TextFacePolicy.Default, faces.For(AsciiFont()));
        Assert.Null(TextFacePolicy.Default.Fold);
        Assert.False(TextFacePolicy.Default.SilentDrop); // loud by default
    }

    [Fact]
    public void Fold_ReturnsTheSameInstance_WhenTheFoldChangesNothing()
    {
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.Chain(TextFold.StripDiacritics, TextFold.Upcase)));

        const string plain = "ALREADY PLAIN";
        Assert.Same(plain, faces.Fold(font, plain));
    }

    [Fact]
    public void Policies_AreKeyedByFaceName_SoAContentReloadKeepsThem()
    {
        // Every screen loads its own BitmapFont instance from the same .fnt; keying by instance
        // would silently drop the policy on the second screen.
        var loadedOnce = AsciiFont();
        var loadedAgain = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(loadedOnce, new TextFacePolicy(TextFold.StripDiacritics));

        Assert.NotSame(loadedOnce, loadedAgain);
        Assert.Equal("Sao Paulo", faces.Fold(loadedAgain, "São Paulo"));
    }

    [Fact]
    public void Policies_CanBeRegisteredByFaceName_BeforeTheFontIsLoaded()
    {
        var faces = new TextFacePolicyRegistry()
            .Register(AsciiFace, new TextFacePolicy(TextFold.Upcase));

        Assert.Equal("SÃO", faces.Fold(AsciiFont(), "São"));
    }

    // ─── the loud warning ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MissingGlyph_IsWarnedOncePerFaceAndCharacter_NotOncePerFrame()
    {
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry();

        // Frame 1: two characters this face cannot render.
        Assert.Equal(2, faces.WarnOnMissingGlyphs(font, "São — Paulo"));
        Assert.True(faces.HasWarned(AsciiFace, 'ã'));
        Assert.True(faces.HasWarned(AsciiFace, '—'));

        // Frames 2..60: not a word more, however many times the label is prepped.
        for (var frame = 0; frame < 60; frame++)
        {
            Assert.Equal(0, faces.WarnOnMissingGlyphs(font, "São — Paulo"));
        }

        // NEW characters on the same face are still worth hearing about (the 'ã' is not repeated).
        Assert.Equal(2, faces.WarnOnMissingGlyphs(font, "ação…"));
    }

    [Fact]
    public void TheSameCharacter_IsWarnedAgain_ForADifferentFace()
    {
        var faces = new TextFacePolicyRegistry();

        Assert.Equal(1, faces.WarnOnMissingGlyphs(AsciiFont(), "São"));
        Assert.Equal(1, faces.WarnOnMissingGlyphs(AsciiFont("OtherFace"), "São"));
        Assert.True(faces.HasWarned("OtherFace", 'ã'));
        Assert.False(faces.HasWarned("NeverSeenFace", 'ã'));
    }

    [Fact]
    public void SilentDrop_IsTheExplicitOptIn_AndSilencesTheScan()
    {
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.StripDiacritics, silentDrop: true));

        // The em-dash survives the diacritic fold and would still be dropped — silently, on purpose.
        Assert.Equal(0, faces.WarnOnMissingGlyphs(font, "Sao — Paulo"));
        Assert.False(faces.HasWarned(AsciiFace, '—'));
    }

    [Fact]
    public void AFoldThatCoversTheContent_LeavesNothingToWarnAbout()
    {
        // The whole point of the pipeline: fold first, and the "drop" that remains is nothing.
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.Chain(
                TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals, TextFold.StripDiacritics)));

        var folded = faces.Fold(font, "São Paulo — 1º andar…");

        Assert.Equal("Sao Paulo - 1o andar...", folded);
        Assert.True(GlyphCoverage.Covers(font, folded));
        Assert.Equal(0, faces.WarnOnMissingGlyphs(font, folded));
    }

    [Fact]
    public void ResetWarnings_MakesTheFaceLoudAgain()
    {
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry();

        Assert.Equal(1, faces.WarnOnMissingGlyphs(font, "São"));
        Assert.Equal(0, faces.WarnOnMissingGlyphs(font, "São"));

        faces.ResetWarnings();

        Assert.False(faces.HasWarned(AsciiFace, 'ã'));
        Assert.Equal(1, faces.WarnOnMissingGlyphs(font, "São"));
    }

    [Fact]
    public void NothingIsWarnedFor_NullFontsEmptyTextOrLayoutCharacters()
    {
        var faces = new TextFacePolicyRegistry();

        Assert.Equal(0, faces.WarnOnMissingGlyphs(null, "São"));
        Assert.Equal(0, faces.WarnOnMissingGlyphs(AsciiFont(), string.Empty));
        Assert.Equal(0, faces.WarnOnMissingGlyphs(AsciiFont(), null));
        Assert.Equal(0, faces.WarnOnMissingGlyphs(AsciiFont(), "line one\r\nline two"));
    }

    // ─── what TextPrepSystem renders ──────────────────────────────────────────────────────────────

    [Fact]
    public void PrepFoldsBeforeTheRevealSlice()
    {
        // "carregando…" is 11 characters raw and 13 folded, and TextUpdateSystem measures the reveal
        // in RAW characters. A mid-reveal count of 6 proves the ORDER: folding first slices the
        // FOLDED string, at the count re-expressed in folded characters (6/11 of 13 → 7,
        // "carrega"); slicing first would have folded a 6-character raw prefix into "carreg".
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.Ellipsis));

        Assert.True(TextPrepSystem.TryGetVisibleText(faces, font, revealingSpeed: 20f,
            visibleCharacterCount: 6, "carregando…", out var visible));
        Assert.Equal("carrega", visible);
    }

    [Theory]
    [InlineData(11)]          // TextUpdateSystem's clamp: maxChars == the RAW length, where IsRevealed latches
    [InlineData(13)]          // a hypothetical count in folded space is equally saturating
    [InlineData(int.MaxValue)] // the int.MaxValue saturation used by static chrome labels
    public void AGrowingFold_StillFinishesFullyRevealed(int visibleCharacterCount)
    {
        // The regression: TextUpdateSystem caps VisibleCharacterCount at the RAW length (11) and
        // latches IsRevealed there — the count NEVER reaches 13. Slicing the folded string with the
        // raw count therefore rendered "carregando." forever, not "slightly early". The count is
        // mapped into folded space, so a finished raw reveal shows the whole folded string.
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.Ellipsis));

        Assert.True(TextPrepSystem.TryGetVisibleText(faces, font, revealingSpeed: 20f,
            visibleCharacterCount, "carregando…", out var visible));
        Assert.Equal("carregando...", visible);
    }

    [Fact]
    public void TheRevealUnderAGrowingFold_NeverBlanksAndNeverGoesBackwards()
    {
        // DialogueSystem reveals at RevealingSpeed = 20, so every count from 1 to the raw length is
        // a frame someone sees: each must show at least one character, never fewer than the frame
        // before, and the last one must be the whole folded string.
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.Ellipsis));
        const string raw = "carregando…";

        var previous = 0;
        for (var count = 1; count <= raw.Length; count++)
        {
            Assert.True(TextPrepSystem.TryGetVisibleText(faces, font, revealingSpeed: 20f,
                visibleCharacterCount: count, raw, out var visible));
            Assert.InRange(visible.Length, Math.Max(1, previous), "carregando...".Length);
            previous = visible.Length;
        }

        Assert.Equal("carregando...".Length, previous);
    }

    [Fact]
    public void AFoldThatChangesNothing_LeavesTheRevealCountAlone()
    {
        // The common case: no fold applied (or a fold that matched nothing) must slice exactly where
        // the raw count says, with no scaling arithmetic in the way.
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.StripDiacritics));

        Assert.True(TextPrepSystem.TryGetVisibleText(faces, font, revealingSpeed: 20f,
            visibleCharacterCount: 3, "carregando", out var visible));
        Assert.Equal("car", visible);
        Assert.Equal(3, TextPrepSystem.ScaleRevealCount(3, "carregando", "carregando"));
    }

    [Fact]
    public void AShrinkingFold_MapsDownWithoutBlankingAStartedReveal()
    {
        // No shipped fold shrinks a string, but a game-supplied one may. The map must not return 0
        // for a reveal that has started (which TryGetVisibleText would render as nothing at all).
        Assert.Equal(1, TextPrepSystem.ScaleRevealCount(1, "abcdefghij", "ab"));
        Assert.Equal(2, TextPrepSystem.ScaleRevealCount(10, "abcdefghij", "ab"));
    }

    [Fact]
    public void PrepRendersTheFoldedContent_ForStaticText()
    {
        var font = AsciiFont();
        var faces = new TextFacePolicyRegistry()
            .Register(font, new TextFacePolicy(TextFold.StripDiacritics));

        Assert.True(TextPrepSystem.TryGetVisibleText(faces, font, revealingSpeed: 0f,
            visibleCharacterCount: 0, "São Paulo", out var visible));
        Assert.Equal("Sao Paulo", visible);
    }

    [Fact]
    public void PrepWithoutARegistry_RendersTheContentUnchanged()
    {
        Assert.True(TextPrepSystem.TryGetVisibleText(null, AsciiFont(), revealingSpeed: 0f,
            visibleCharacterCount: 0, "São Paulo", out var visible));
        Assert.Equal("São Paulo", visible);
    }

    // ─── the line that actually reaches the log ───────────────────────────────────────────────────

    [Fact]
    public void TheWarningReachesTheLog_Once_WithFaceCharacterAndContext()
    {
        var fake = new CapturingPlatformServices();
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            Logger.Shutdown();
            Logger.Initialize("logdir");

            var font = AsciiFont();
            var faces = new TextFacePolicyRegistry();
            for (var frame = 0; frame < 10; frame++)
            {
                faces.WarnOnMissingGlyphs(font, "São Paulo");
            }

            Logger.Shutdown();
        }
        finally
        {
            Logger.Shutdown();
            PlatformServices.Current = previous;
        }

        var warnings = fake.ConsoleLines.Where(line => line.Contains("[ WARN]")).ToList();
        var warning = Assert.Single(warnings);
        Assert.Contains($"face '{AsciiFace}'", warning);
        Assert.Contains("'ã' (U+00E3)", warning);   // greppable: the character AND its codepoint
        Assert.Contains("DROPPED", warning);        // says what happens, not just what is missing
        Assert.Contains("São Paulo", warning);      // says where it happened
    }

    /// <summary>
    /// Captures both <see cref="Logger"/> sinks. Mirrors the fake in
    /// <c>LoggerInterpolationTests</c> / <c>PlatformServicesTests</c>.
    /// </summary>
    private sealed class CapturingPlatformServices : IPlatformServices
    {
        public string BaseDirectory => "/fake/base/";
        public List<string> ConsoleLines { get; } = new();
        public StringWriter LogWriter { get; } = new();

        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => false;
        public string ReadAllText(string path) => throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) => suggestedFileName;
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) => ConsoleLines.Add(line);
        public void RunBackground(Action work) => work();
    }
}
