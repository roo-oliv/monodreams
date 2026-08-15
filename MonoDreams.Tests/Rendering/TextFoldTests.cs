using MonoDreams.Text;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering-text premise "Folds are pure, deterministic and identity-preserving"
/// (issue #92). These are the shipped building blocks a game composes into a per-face policy so
/// human-authored content ("São Paulo", "prazo — hoje", "1º andar", "carregando…") survives a
/// bitmap face that was exported without those glyphs. Two properties are load-bearing and both are
/// asserted here: the folds are FACE-INDEPENDENT (same input, same output, everywhere — including a
/// WASM head with no ICU), and they return the SAME string instance when they change nothing, which
/// is what lets <c>TextPrepSystem</c> fold every label every frame without allocating.
/// </summary>
public class TextFoldTests
{
    [Fact]
    public void Dashes_FoldEveryUnicodeDashToAscii()
    {
        Assert.Equal("prazo - hoje", TextFold.Dashes("prazo — hoje"));   // em dash U+2014
        Assert.Equal("2020-2024", TextFold.Dashes("2020–2024"));         // en dash U+2013
        Assert.Equal("-5 C", TextFold.Dashes("−5 C"));                   // minus sign U+2212
        Assert.Equal("a-b-c-d", TextFold.Dashes("a‐b‑c‒d"));             // hyphen, nb-hyphen, figure dash
    }

    [Fact]
    public void Ellipsis_ExpandsToThreePeriods()
    {
        Assert.Equal("carregando...", TextFold.Ellipsis("carregando…"));
        Assert.Equal("a...b...", TextFold.Ellipsis("a…b…"));
    }

    [Fact]
    public void Ordinals_FoldToPlainLetters()
    {
        Assert.Equal("1o andar, 2a via", TextFold.Ordinals("1º andar, 2ª via"));
    }

    [Fact]
    public void StripDiacritics_FoldsAccentedLatinToItsBaseLetter()
    {
        Assert.Equal("Sao Paulo", TextFold.StripDiacritics("São Paulo"));
        Assert.Equal("acao, informacao, pao", TextFold.StripDiacritics("ação, informação, pão"));
        Assert.Equal("Krakow, Cesky, Istanbul", TextFold.StripDiacritics("Kraków, Český, İstanbul"));
        Assert.Equal("AEIOU aeiou CcNn", TextFold.StripDiacritics("ÁÊÍÕÜ àéîõü ÇçÑñ"));
        Assert.Equal("Lodz, Ostergotland", TextFold.StripDiacritics("Łódź, Östergötland")); // stroked letters too
    }

    [Fact]
    public void StripDiacritics_LeavesNonAccentedLettersAlone()
    {
        // Æ, Ð, Þ, ß, Œ are letters in their own right, not accented ASCII. The engine refuses to
        // invent "AE"/"ss" for them — if the face lacks them the missing-glyph warning says so.
        Assert.Equal("Æ Ð Þ ß Œ", TextFold.StripDiacritics("Æ Ð Þ ß Œ"));
    }

    [Fact]
    public void Upcase_IsInvariant_NotCurrentCulture()
    {
        Assert.Equal("SAO PAULO", TextFold.Upcase("Sao Paulo"));
        Assert.Equal("ACAO", TextFold.Upcase("acao"));
        // Invariant casing keeps 'i' → 'I'; a Turkish current culture would produce 'İ' and lose the glyph.
        Assert.Equal("I", TextFold.Upcase("i"));
    }

    [Fact]
    public void EveryFold_ReturnsTheSameInstance_WhenItChangesNothing()
    {
        // The per-frame contract: TextPrepSystem folds every text entity every frame, so an
        // already-plain string must not allocate a copy.
        const string plain = "ALREADY PLAIN 123";
        Assert.Same(plain, TextFold.Dashes(plain));
        Assert.Same(plain, TextFold.Ellipsis(plain));
        Assert.Same(plain, TextFold.Ordinals(plain));
        Assert.Same(plain, TextFold.StripDiacritics(plain));
        Assert.Same(plain, TextFold.Upcase(plain));
        Assert.Same(plain, TextFold.Identity(plain));

        var chain = TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals,
            TextFold.StripDiacritics, TextFold.Upcase);
        Assert.Same(plain, chain(plain));
    }

    [Fact]
    public void EveryFold_PassesNullAndEmptyThrough()
    {
        Assert.Null(TextFold.Dashes(null));
        Assert.Null(TextFold.Ellipsis(null));
        Assert.Null(TextFold.Ordinals(null));
        Assert.Null(TextFold.StripDiacritics(null));
        Assert.Null(TextFold.Upcase(null));
        Assert.Equal(string.Empty, TextFold.Dashes(string.Empty));
        Assert.Equal(string.Empty, TextFold.StripDiacritics(string.Empty));
    }

    [Fact]
    public void Chain_AppliesFoldsLeftToRight()
    {
        // The caps-only mono face of the NFs, Please! catalogue: no typography, no diacritics, no
        // lowercase. One chain turns real Brazilian-Portuguese copy into something it can render.
        var mono = TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals,
            TextFold.StripDiacritics, TextFold.Upcase);

        Assert.Equal("SAO PAULO - 1O ANDAR... EMISSAO", mono("São Paulo — 1º andar… emissão"));
    }

    [Fact]
    public void Chain_OfNothing_IsAPassThrough()
    {
        var empty = TextFold.Chain();
        const string text = "São Paulo";
        Assert.Same(text, empty(text));
    }

    [Fact]
    public void Chain_CopiesItsFolds_SoLaterMutationCannotChangeARegisteredPolicy()
    {
        var folds = new Func<string, string>[] { TextFold.StripDiacritics, TextFold.Upcase };
        var chain = TextFold.Chain(folds);
        folds[0] = _ => "hijacked";

        Assert.Equal("SAO", chain("São"));
    }
}
