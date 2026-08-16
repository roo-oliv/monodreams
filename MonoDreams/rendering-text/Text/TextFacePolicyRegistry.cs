using System;
using System.Collections.Generic;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Text;

/// <summary>
/// The per-face text policies of a game, plus the warn-once bookkeeping that keeps a missing glyph
/// from spamming the log 60 times a second. <c>TextPrepSystem</c> consults one of these on every
/// text entity, every frame: it folds the string through the face's <see cref="TextFacePolicy"/>
/// BEFORE glyph layout, then reports whatever the face still cannot render.
///
/// Faces are keyed by <see cref="BitmapFont.Face"/> — the face name inside the <c>.fnt</c>, not the
/// <see cref="BitmapFont"/> instance — so a policy survives a content reload (a new screen loading
/// the same font gets a new object) and so the warn-once key is the same "face + character" pair the
/// log line prints. The corollary: two sizes of the same face exported from the same source share one
/// policy, which is the intent — coverage and folding are properties of the FACE, not of a
/// particular pixel size.
///
/// Wire one registry at boot and pass it to every screen's <c>TextPrepSystem</c>; sharing the
/// instance is what makes "once per face+character" hold across screens rather than per screen.
/// Registration is expected at load time, and lookups happen on the update thread — the registry is
/// not synchronized.
///
/// <code>
/// var faces = new TextFacePolicyRegistry()
///     .Register(displayFont, new TextFacePolicy(TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals)))
///     .Register(monoFont, new TextFacePolicy(
///         TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals,
///                        TextFold.StripDiacritics, TextFold.Upcase),
///         silentDrop: true)); // the folds cover this face's content — dropping the rest is tested and intended
/// </code>
/// </summary>
public sealed class TextFacePolicyRegistry
{
    /// <summary>The face key used for a font whose <c>.fnt</c> carries no face name.</summary>
    public const string UnnamedFace = "<unnamed>";

    private readonly Dictionary<string, TextFacePolicy> _policies = new(StringComparer.Ordinal);
    private readonly HashSet<(string Face, int Codepoint)> _warned = new();

    /// <summary>
    /// Registers <paramref name="policy"/> for the face of <paramref name="font"/>, replacing any
    /// previous policy for that face. Returns this registry so registrations chain.
    /// </summary>
    public TextFacePolicyRegistry Register(BitmapFont font, TextFacePolicy policy) =>
        Register(FaceOf(font), policy);

    /// <summary>
    /// Registers <paramref name="policy"/> for a face by name — the form to use when the policy is
    /// declared before the font is loaded.
    /// </summary>
    public TextFacePolicyRegistry Register(string face, TextFacePolicy policy)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        _policies[string.IsNullOrEmpty(face) ? UnnamedFace : face] = policy;
        return this;
    }

    /// <summary>The policy for a font's face — <see cref="TextFacePolicy.Default"/> when unregistered. Never null.</summary>
    public TextFacePolicy For(BitmapFont font) => For(FaceOf(font));

    /// <summary>The policy for a face name — <see cref="TextFacePolicy.Default"/> when unregistered. Never null.</summary>
    public TextFacePolicy For(string face) =>
        _policies.TryGetValue(string.IsNullOrEmpty(face) ? UnnamedFace : face, out var policy)
            ? policy
            : TextFacePolicy.Default;

    /// <summary>
    /// Applies the face's fold to <paramref name="text"/>. Returns the SAME string instance when
    /// the face has no fold or the fold changes nothing — the per-frame call in
    /// <c>TextPrepSystem</c> must not allocate for text that needs no folding.
    /// </summary>
    public string Fold(BitmapFont font, string text)
    {
        if (_policies.Count == 0 || string.IsNullOrEmpty(text)) return text;

        var fold = For(font).Fold;
        if (fold == null) return text;

        return fold(text) ?? text;
    }

    /// <summary>
    /// Logs a warning — ONCE per face + character, never once per frame — for every character of
    /// <paramref name="text"/> that <paramref name="font"/> cannot render and will therefore drop
    /// on its way to the screen. Does nothing for a face whose policy opted into
    /// <see cref="TextFacePolicy.SilentDrop"/>. Returns how many characters were reported for the
    /// FIRST time by this call (0 once they have all been seen), which is what makes the warn-once
    /// contract assertable.
    /// </summary>
    public int WarnOnMissingGlyphs(BitmapFont font, string text)
    {
        if (font == null || string.IsNullOrEmpty(text)) return 0;
        if (For(font).SilentDrop) return 0;

        var face = FaceOf(font);
        var reported = 0;
        var index = 0;
        while (GlyphCoverage.TryFindMissing(font, text, index, out var codepoint, out var found))
        {
            index = found + (codepoint > 0xFFFF ? 2 : 1);
            if (!_warned.Add((face, codepoint))) continue;

            reported++;
            Logger.Warning(
                $"[rendering-text] face '{face}' has no glyph for {GlyphCoverage.Describe(codepoint)} — " +
                $"it is DROPPED from the rendered text (first seen in \"{Excerpt(text)}\"). Fold it for this " +
                "face (TextFold + TextFacePolicy) or opt into TextFacePolicy.SilentDrop.");
        }

        return reported;
    }

    /// <summary>Whether this registry has already warned about a face + codepoint pair.</summary>
    public bool HasWarned(string face, int codepoint) =>
        _warned.Contains((string.IsNullOrEmpty(face) ? UnnamedFace : face, codepoint));

    /// <summary>
    /// Forgets every warning issued so far, so a missing glyph is reported again. For a game that
    /// swaps its fonts at runtime (or a test that asserts the warning twice).
    /// </summary>
    public void ResetWarnings() => _warned.Clear();

    /// <summary>The registry key for a font: its <c>.fnt</c> face name, or <see cref="UnnamedFace"/>.</summary>
    public static string FaceOf(BitmapFont font) =>
        font == null || string.IsNullOrEmpty(font.Face) ? UnnamedFace : font.Face;

    /// <summary>
    /// A short, single-line quote of the offending string for the log — enough to find the label in
    /// the content, not enough to flood a line.
    /// </summary>
    private static string Excerpt(string text)
    {
        const int maxLength = 48;
        var single = text.Replace('\n', ' ').Replace('\r', ' ');
        return single.Length <= maxLength ? single : single.Substring(0, maxLength) + "…";
    }
}
