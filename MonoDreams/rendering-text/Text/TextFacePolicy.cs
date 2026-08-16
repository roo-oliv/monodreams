using System;

namespace MonoDreams.Text;

/// <summary>
/// What the text pipeline must do with the strings drawn in ONE font face: the fold applied before
/// glyph layout, and whether characters the face still cannot render may be dropped silently.
///
/// A policy is immutable and holds no per-frame state — register it once per face on a
/// <see cref="TextFacePolicyRegistry"/> and the registry does the rest.
/// </summary>
public sealed class TextFacePolicy
{
    /// <summary>
    /// The policy every face gets until a game registers one: no fold, and missing glyphs are
    /// reported LOUDLY (once per face+character) rather than dropped in silence.
    /// </summary>
    public static readonly TextFacePolicy Default = new();

    /// <summary>
    /// Creates a policy. <paramref name="fold"/> is a pure string→string transform — compose one
    /// from the <see cref="TextFold"/> building blocks with <see cref="TextFold.Chain"/>, or pass
    /// null for "render what the game wrote".
    /// <paramref name="silentDrop"/> is the EXPLICIT opt-in to the engine's old behavior: with it
    /// set, characters the face lacks vanish from the rendered string with no warning. Set it only
    /// where dropping is a deliberate, tested outcome (typically after a fold that already covers
    /// the content), because it is also what silences the coverage scan for this face.
    /// </summary>
    public TextFacePolicy(Func<string, string> fold = null, bool silentDrop = false)
    {
        Fold = fold;
        SilentDrop = silentDrop;
    }

    /// <summary>The transform applied to a string before it is laid out; null means "no fold".</summary>
    public Func<string, string> Fold { get; }

    /// <summary>
    /// When true, this face drops uncovered characters without a word — no warning, and no
    /// per-frame coverage scan either. Default false: the engine is loud about dropped glyphs.
    /// </summary>
    public bool SilentDrop { get; }
}
