using System.Linq;
using MonoDreams.Text;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering-text premise "A face's glyph coverage is queryable" (issue #92): the
/// character table the engine already parsed out of the <c>.fnt</c> is what tells a game whether a
/// string will survive the draw path intact. Before this, the answer existed only as pixels that
/// never appeared — "São Paulo" rendered as "So Paulo" with no crash, no warning and no tofu box.
/// </summary>
public class GlyphCoverageTests
{
    private const string Face = "TestFace";

    [Fact]
    public void HasGlyph_AnswersFromTheFacesCharacterTable()
    {
        var font = TestBitmapFonts.WithGlyphs(Face, "Sao Pul");

        Assert.True(GlyphCoverage.HasGlyph(font, 'S'));
        Assert.True(GlyphCoverage.HasGlyph(font, (int)'a'));
        Assert.False(GlyphCoverage.HasGlyph(font, 'ã'));
        Assert.False(GlyphCoverage.HasGlyph(font, '—'));
    }

    [Fact]
    public void HasGlyph_NullFontCoversNothing()
    {
        Assert.False(GlyphCoverage.HasGlyph(null, 'a'));
        Assert.True(GlyphCoverage.Covers(null, "anything")); // nothing to report without a face
    }

    [Fact]
    public void Covers_IsFalseForTheStringThatWouldLoseALetter()
    {
        var font = TestBitmapFonts.WithGlyphs(Face, TestBitmapFonts.Ascii);

        Assert.True(GlyphCoverage.Covers(font, "Sao Paulo"));
        Assert.False(GlyphCoverage.Covers(font, "São Paulo")); // the ã silently vanishes today
        Assert.True(GlyphCoverage.Covers(font, string.Empty));
        Assert.True(GlyphCoverage.Covers(font, null));
    }

    [Fact]
    public void MissingCodepoints_AreDistinctAndInFirstAppearanceOrder()
    {
        var font = TestBitmapFonts.WithGlyphs(Face, TestBitmapFonts.Ascii);

        // "ã" three times, one em-dash, one cedilla, one ellipsis — reported as four DISTINCT
        // characters, in the order they first appear, so a log line or a content report reads like
        // the sentence does.
        var missing = GlyphCoverage.MissingCodepoints(font, "São, prazo — ação… não").ToList();

        Assert.Equal(new[] { (int)'ã', '—', 'ç', '…' }, missing);
    }

    [Fact]
    public void MissingCodepoints_IsEmptyForACoveredString()
    {
        var font = TestBitmapFonts.WithGlyphs(Face, TestBitmapFonts.Ascii);

        Assert.Empty(GlyphCoverage.MissingCodepoints(font, "Sao Paulo - acao..."));
        Assert.Empty(GlyphCoverage.MissingCodepoints(font, null));
    }

    [Fact]
    public void LayoutCharacters_AreNeverReportedAsMissing()
    {
        // MasterRenderSystem splits '\n' itself and never asks the font for it (see the multi-line
        // premise), so a face that lacks a newline glyph — every face — is not "missing" one.
        var font = TestBitmapFonts.WithGlyphs(Face, TestBitmapFonts.Ascii);

        Assert.True(GlyphCoverage.Covers(font, "line one\r\nline two"));
        Assert.Empty(GlyphCoverage.MissingCodepoints(font, "line one\r\nline two"));
        Assert.True(GlyphCoverage.IsLayoutCharacter('\n'));
        Assert.True(GlyphCoverage.IsLayoutCharacter('\r'));
        Assert.False(GlyphCoverage.IsLayoutCharacter('\t')); // a tab really does render nothing
    }

    [Fact]
    public void TryFindMissing_ReportsThePositionAndResumesPastIt()
    {
        var font = TestBitmapFonts.WithGlyphs(Face, TestBitmapFonts.Ascii);

        Assert.True(GlyphCoverage.TryFindMissing(font, "São — Paulo", 0, out var first, out var firstIndex));
        Assert.Equal('ã', (char)first);
        Assert.Equal(1, firstIndex);

        Assert.True(GlyphCoverage.TryFindMissing(font, "São — Paulo", firstIndex + 1, out var second, out var secondIndex));
        Assert.Equal('—', (char)second);
        Assert.Equal(4, secondIndex);

        Assert.False(GlyphCoverage.TryFindMissing(font, "São — Paulo", secondIndex + 1, out _, out var exhausted));
        Assert.Equal(-1, exhausted);
    }

    [Fact]
    public void AstralCodepoints_AreOneCharacter_NotTwoSurrogateHalves()
    {
        var withEmoji = TestBitmapFonts.WithGlyphs(Face, TestBitmapFonts.Ascii + "😀");
        var withoutEmoji = TestBitmapFonts.WithGlyphs(Face, TestBitmapFonts.Ascii);

        Assert.True(GlyphCoverage.Covers(withEmoji, "hi 😀"));

        var missing = GlyphCoverage.MissingCodepoints(withoutEmoji, "hi 😀").ToList();
        Assert.Equal(new[] { 0x1F600 }, missing);
    }

    [Fact]
    public void Describe_IsReadableAndGreppable()
    {
        Assert.Equal("'ã' (U+00E3)", GlyphCoverage.Describe('ã'));
        Assert.Equal("'—' (U+2014)", GlyphCoverage.Describe('—'));
        Assert.Equal("(U+0009)", GlyphCoverage.Describe('\t')); // a control character prints as its codepoint alone
        Assert.Equal("'😀' (U+01F600)", GlyphCoverage.Describe(0x1F600));
    }
}
