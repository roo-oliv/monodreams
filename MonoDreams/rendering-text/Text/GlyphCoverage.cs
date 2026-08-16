using System;
using System.Collections.Generic;
using System.Globalization;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Text;

/// <summary>
/// Read-only glyph-coverage queries over a <see cref="BitmapFont"/> — "does this face have this
/// character?" and "which characters of this string would it drop?".
///
/// A bitmap face only carries the glyphs its <c>.fnt</c> was exported with, and the bitmap-font draw
/// path renders a character it does not know as NOTHING — no crash, no tofu box, no warning. A face
/// without "ã" renders "São Paulo" as "So Paulo"; a face without an em-dash turns "prazo — hoje" into
/// "prazo  hoje". The coverage the engine already parsed (the font's character table) is what turns
/// that invisible failure into an answerable question, and this class is the seam that answers it:
/// <see cref="TextFold"/> folds a string into the face's covered subset, and
/// <see cref="TextFacePolicyRegistry.WarnOnMissingGlyphs"/> logs whatever is left over.
///
/// All queries are pure, allocation-free (except <see cref="MissingCodepoints"/>, which builds a
/// list) and need no <c>GraphicsDevice</c>, so they are usable from tools, tests and content checks
/// as well as from the render pipeline.
/// </summary>
public static class GlyphCoverage
{
    /// <summary>
    /// Whether <paramref name="font"/> carries a glyph for the Unicode <paramref name="codepoint"/>.
    /// A null font covers nothing.
    /// </summary>
    public static bool HasGlyph(BitmapFont font, int codepoint) =>
        font != null && font.TryGetCharacter(codepoint, out _);

    /// <summary>
    /// Whether <paramref name="font"/> carries a glyph for <paramref name="character"/>. Surrogate
    /// halves are never covered on their own — use the <see cref="HasGlyph(BitmapFont,int)"/>
    /// overload with a full codepoint for astral characters.
    /// </summary>
    public static bool HasGlyph(BitmapFont font, char character) => HasGlyph(font, (int)character);

    /// <summary>
    /// Whether every renderable character of <paramref name="text"/> has a glyph in
    /// <paramref name="font"/>. Layout characters (see <see cref="IsLayoutCharacter"/>) are never
    /// counted — the engine lays those out itself. Null/empty text is trivially covered, and a null
    /// font means "no face to check": these queries report NOTHING for it rather than declaring
    /// every character missing, so a tool walking unloaded content does not produce a flood of
    /// findings about a font it never had.
    /// </summary>
    public static bool Covers(BitmapFont font, string text) =>
        !TryFindMissing(font, text, 0, out _, out _);

    /// <summary>
    /// Finds the first character at or after <paramref name="startIndex"/> that
    /// <paramref name="font"/> cannot render. Allocation-free — this is the form the per-frame
    /// render path uses. <paramref name="index"/> is the index into <paramref name="text"/> where
    /// the offending character starts (its high surrogate, for an astral codepoint).
    /// </summary>
    public static bool TryFindMissing(BitmapFont font, string text, int startIndex,
        out int codepoint, out int index)
    {
        codepoint = 0;
        index = -1;
        if (font == null || string.IsNullOrEmpty(text)) return false;

        for (var i = Math.Max(0, startIndex); i < text.Length; i += CharWidthAt(text, i))
        {
            var current = CodepointAt(text, i);
            if (IsLayoutCharacter(current) || font.TryGetCharacter(current, out _)) continue;

            codepoint = current;
            index = i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Every codepoint of <paramref name="text"/> that <paramref name="font"/> cannot render —
    /// DISTINCT and in first-appearance order, so a sentence that repeats "ã" four times reports it
    /// once. Layout characters are excluded. Returns an empty list when the face covers the string
    /// (or when either argument is null/empty).
    /// </summary>
    public static IReadOnlyList<int> MissingCodepoints(BitmapFont font, string text)
    {
        if (font == null || string.IsNullOrEmpty(text)) return Array.Empty<int>();

        List<int> missing = null;
        for (var i = 0; i < text.Length; i += CharWidthAt(text, i))
        {
            var codepoint = CodepointAt(text, i);
            if (IsLayoutCharacter(codepoint) || font.TryGetCharacter(codepoint, out _)) continue;

            missing ??= new List<int>();
            if (!missing.Contains(codepoint)) missing.Add(codepoint);
        }

        return (IReadOnlyList<int>)missing ?? Array.Empty<int>();
    }

    /// <summary>
    /// Characters the ENGINE lays out rather than the font: <c>'\n'</c> (MasterRenderSystem splits
    /// lines itself — see the rendering-text premise on multi-line layout) and the <c>'\r'</c> that
    /// precedes it on Windows-authored content. They are never reported as missing glyphs, whether
    /// or not the face happens to carry a character for them.
    /// </summary>
    public static bool IsLayoutCharacter(int codepoint) => codepoint == '\n' || codepoint == '\r';

    /// <summary>
    /// A human- and grep-readable rendering of a codepoint for log lines and test assertions:
    /// <c>'ã' (U+00E3)</c>, or just <c>(U+0009)</c> for a control character that would print as
    /// garbage.
    /// </summary>
    public static string Describe(int codepoint)
    {
        var hex = "U+" + codepoint.ToString(codepoint > 0xFFFF ? "X6" : "X4", CultureInfo.InvariantCulture);
        if (codepoint > 0xFFFF) return $"'{char.ConvertFromUtf32(codepoint)}' ({hex})";
        var character = (char)codepoint;
        return char.IsControl(character) || char.IsSurrogate(character)
            ? $"({hex})"
            : $"'{character}' ({hex})";
    }

    /// <summary>The full codepoint starting at <paramref name="index"/>, decoding surrogate pairs.</summary>
    private static int CodepointAt(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(text[index], text[index + 1])
            : text[index];

    /// <summary>How many <c>char</c>s the codepoint starting at <paramref name="index"/> occupies.</summary>
    private static int CharWidthAt(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? 2
            : 1;
}
