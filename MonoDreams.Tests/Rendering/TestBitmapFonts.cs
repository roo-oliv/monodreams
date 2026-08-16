using System.Collections.Generic;
using System.Linq;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Builds synthetic <see cref="BitmapFont"/> faces with an EXACT, declared glyph table — the moral
/// equivalent of a hand-written <c>.fnt</c>, minus the atlas. Nothing here touches a
/// <c>GraphicsDevice</c>: the glyph-coverage queries and the fold pipeline only read the character
/// table (the same table a real font's <c>.fnt</c> fills), so a partially-covered face is
/// expressible in one line of test code.
///
/// The characters carry a null texture region on purpose — measuring or drawing one of these fonts
/// would dereference it, and neither belongs in a unit test of coverage/folding.
/// </summary>
internal static class TestBitmapFonts
{
    /// <summary>The characters a plain ASCII bitmap face would ship — no accents, no typography.</summary>
    public const string Ascii =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?'\"()[]{}-+*/=_@#$%&<>|\\^~`";

    /// <summary>A face covering exactly the codepoints of <paramref name="glyphs"/> (duplicates ignored).</summary>
    public static BitmapFont WithGlyphs(string face, string glyphs) =>
        WithGlyphs(face, Codepoints(glyphs));

    /// <summary>A face covering exactly <paramref name="codepoints"/> (duplicates ignored).</summary>
    public static BitmapFont WithGlyphs(string face, IEnumerable<int> codepoints)
    {
        var characters = codepoints
            .Distinct()
            // (codepoint, textureRegion, xOffset, yOffset, xAdvance)
            .Select(codepoint => new BitmapFontCharacter(codepoint, null!, 0, 0, 8))
            .ToList();

        return new BitmapFont(face, size: 8, lineHeight: 10, characters);
    }

    /// <summary>Enumerates full Unicode codepoints, so an astral character counts once, not twice.</summary>
    private static IEnumerable<int> Codepoints(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
                continue;
            }

            yield return text[i];
        }
    }
}
