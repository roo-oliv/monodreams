using System;
using System.Text;

namespace MonoDreams.Text;

/// <summary>
/// Deterministic string→string folds: the building blocks a game composes into a
/// <see cref="TextFacePolicy"/> so that content written for humans ("São Paulo", "prazo — hoje",
/// "1º andar", "carregando…") survives a bitmap face that was never exported with those glyphs.
///
/// Every fold here is PURE and face-independent — it does not look at what the font covers, it just
/// rewrites a well-known character into a plainer one. That is deliberate: a fold that consulted
/// coverage would produce different text on different faces and make a layout impossible to reason
/// about or diff. Binding a fold to a face is the policy's job
/// (<see cref="TextFacePolicyRegistry"/>), not the fold's.
///
/// Every fold is also IDENTITY-PRESERVING: when it changes nothing it returns the very same string
/// instance it was given. <c>TextPrepSystem</c> folds every text entity every frame, so a fold that
/// allocated unconditionally would churn one string per label per frame; scanning first and
/// returning the original keeps the steady state allocation-free.
///
/// Compose with <see cref="Chain"/>, in the order dash/ellipsis/ordinal → diacritics → case:
/// <code>
/// var mono = TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals,
///                           TextFold.StripDiacritics, TextFold.Upcase);
/// </code>
/// </summary>
public static class TextFold
{
    /// <summary>
    /// Every Unicode dash a text editor or a copy-paste introduces — em dash (U+2014), en dash
    /// (U+2013), figure dash, horizontal bar, hyphen, non-breaking hyphen and the minus sign
    /// (U+2212) — folded to the ASCII hyphen <c>'-'</c>, which practically every face carries.
    /// </summary>
    public static string Dashes(string text) => MapCharacters(text, DashMap);

    /// <summary>
    /// The single-character ellipsis <c>'…'</c> (U+2026) expanded to three ASCII periods
    /// <c>"..."</c>. This is the one fold that CHANGES THE LENGTH of the string — see the
    /// rendering-text premise on folding before the reveal slice for what that means for typewriter
    /// text.
    /// </summary>
    public static string Ellipsis(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var first = text.IndexOf(EllipsisCharacter);
        if (first < 0) return text;

        var builder = new StringBuilder(text.Length + 8);
        builder.Append(text, 0, first);
        for (var i = first; i < text.Length; i++)
        {
            if (text[i] == EllipsisCharacter) builder.Append("...");
            else builder.Append(text[i]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The Latin ordinal indicators <c>'º'</c> (U+00BA) and <c>'ª'</c> (U+00AA) — "1º andar",
    /// "2ª via" — folded to the plain letters <c>'o'</c> and <c>'a'</c>.
    /// </summary>
    public static string Ordinals(string text) => MapCharacters(text, OrdinalMap);

    /// <summary>
    /// Latin letters with diacritics folded to their unaccented base letter: "São" → "Sao",
    /// "ação" → "acao", "Kraków" → "Krakow". Covers Latin-1 Supplement (U+00C0–U+00FF) and Latin
    /// Extended-A (U+0100–U+017F), including stroked letters (Ø→O, Ł→L, Đ→D, Ħ→H, Ŧ→T, Ŀ→L).
    ///
    /// Letters that are NOT an accented form of an ASCII letter are left alone rather than
    /// transliterated: Æ, Ð, Þ, ß, Ĳ, Œ, Ŋ and their lowercase forms stay as they are (if the face
    /// lacks them, the missing-glyph warning still fires, which is the honest outcome — the engine
    /// will not silently invent "AE" for you).
    ///
    /// The tables are hardcoded rather than derived from <c>string.Normalize</c> on purpose:
    /// Unicode normalization needs ICU, which a size-trimmed WASM head may not ship, and a fold
    /// whose output depends on the platform's globalization mode is not deterministic.
    /// </summary>
    public static string StripDiacritics(string text) => MapCharacters(text, DiacriticMap);

    /// <summary>
    /// Culture-invariant upper-casing, for the caps-only faces that a pixel-art game inevitably
    /// picks up (a face whose <c>.fnt</c> carries no lowercase glyphs at all). Invariant, never
    /// current-culture: a Turkish locale must not turn "i" into "İ" and lose the glyph.
    /// </summary>
    public static string Upcase(string text) => MapCharacters(text, UpcaseMap);

    /// <summary>
    /// Composes folds left to right into a single fold, which is what a
    /// <see cref="TextFacePolicy"/> takes. The array is copied, so later mutation of the caller's
    /// array cannot change what a registered face folds to. Passing nothing yields a pass-through.
    /// </summary>
    public static Func<string, string> Chain(params Func<string, string>[] folds)
    {
        if (folds == null || folds.Length == 0) return Identity;
        if (folds.Length == 1) return folds[0] ?? Identity;

        var composed = (Func<string, string>[])folds.Clone();
        return text =>
        {
            foreach (var fold in composed)
            {
                if (fold != null) text = fold(text);
            }

            return text;
        };
    }

    /// <summary>The fold that changes nothing — what <see cref="Chain"/> returns for an empty chain.</summary>
    public static string Identity(string text) => text;

    private const char EllipsisCharacter = '…';

    // First entry maps U+00C0; '\0' means "not an accented ASCII letter — leave it alone".
    private const string Latin1Supplement =
        "AAAAAA\0CEEEEIIII\0NOOOOO\0OUUUUY\0\0aaaaaa\0ceeeeiiii\0nooooo\0ouuuuy\0y";

    // First entry maps U+0100.
    private const string LatinExtendedA =
        "AaAaAaCcCcCcCcDdDdEeEeEeEeEeGgGgGgGgHhHhIiIiIiIiIi\0\0JjKkkLlLlLlLlLlNnNnNn\0\0\0OoOoOo\0\0" +
        "RrRrRrSsSsSsSsTtTtTtUuUuUuUuUuUuWwYyYZzZzZzs";

    private static readonly Func<char, char> DashMap = MapDash;
    private static readonly Func<char, char> OrdinalMap = MapOrdinal;
    private static readonly Func<char, char> DiacriticMap = MapDiacritic;
    private static readonly Func<char, char> UpcaseMap = char.ToUpperInvariant;

    private static char MapDash(char character) => character switch
    {
        '‐' => '-', // hyphen
        '‑' => '-', // non-breaking hyphen
        '‒' => '-', // figure dash
        '–' => '-', // en dash
        '—' => '-', // em dash
        '―' => '-', // horizontal bar
        '−' => '-', // minus sign
        _ => character,
    };

    private static char MapOrdinal(char character) => character switch
    {
        'º' => 'o',
        'ª' => 'a',
        _ => character,
    };

    private static char MapDiacritic(char character)
    {
        var folded = character switch
        {
            >= 'À' and <= 'ÿ' => Latin1Supplement[character - 'À'],
            >= 'Ā' and <= 'ſ' => LatinExtendedA[character - 'Ā'],
            _ => character,
        };

        return folded == '\0' ? character : folded;
    }

    /// <summary>
    /// Applies a 1:1 character map, returning the ORIGINAL instance when the map changes nothing —
    /// the identity-preservation contract every fold here honors. The pre-scan costs one pass over
    /// an already-hot string and saves an allocation on the (overwhelmingly common) unchanged path.
    /// </summary>
    private static string MapCharacters(string text, Func<char, char> map)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var firstChange = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (map(text[i]) == text[i]) continue;
            firstChange = i;
            break;
        }

        if (firstChange < 0) return text;

        var buffer = text.ToCharArray();
        for (var i = firstChange; i < buffer.Length; i++) buffer[i] = map(buffer[i]);
        return new string(buffer);
    }
}
