#nullable enable
using System.Text;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// A minimal single-line editable text field model for the editor's Save dialog — pure data +
/// mutators, no rendering, no input source. It is the editor's <b>first keyboard-capturing chrome
/// widget</b>: <c>EditorDialogSystem</c> feeds it the characters typed this frame (and the
/// headless <c>dialog:name</c> op sets it directly), then mirrors <see cref="Value"/> onto the
/// dialog's <c>DynamicTextComponent</c>. Kept deliberately caret-less (append / backspace / set /
/// clear only) — a scene name is a short identifier typed at the end, so no mid-string caret is
/// needed, which also makes the model trivially unit-testable without a keyboard or GraphicsDevice.
///
/// <para>The value is <b>not</b> sanitized as you type (so backspacing feels natural); the safe
/// file id is derived once on confirm via <see cref="Sanitize"/> — letters, digits, <c>-</c> and
/// <c>_</c> survive; everything else is stripped.</para>
/// </summary>
public sealed class EditorTextField
{
    private readonly StringBuilder _builder = new();

    /// <summary>The current raw text (never null).</summary>
    public string Value => _builder.ToString();

    /// <summary>True while the field holds no characters.</summary>
    public bool IsEmpty => _builder.Length == 0;

    /// <summary>Replaces the whole value (null → empty). Used by <c>OpenSave(defaultName)</c> to
    /// prefill the current scene id and by the headless <c>dialog:name</c> op.</summary>
    public void Set(string? text)
    {
        _builder.Clear();
        if (!string.IsNullOrEmpty(text)) _builder.Append(text);
    }

    /// <summary>Appends one typed character at the end.</summary>
    public void Append(char c) => _builder.Append(c);

    /// <summary>Appends a run of characters at the end (a no-op for null/empty).</summary>
    public void Append(string? text)
    {
        if (!string.IsNullOrEmpty(text)) _builder.Append(text);
    }

    /// <summary>Removes the last character (a no-op when empty) — the Backspace key.</summary>
    public void Backspace()
    {
        if (_builder.Length > 0) _builder.Length--;
    }

    /// <summary>Empties the field.</summary>
    public void Clear() => _builder.Clear();

    /// <summary>
    /// Reduces an arbitrary typed name to a safe scene file id: keeps ASCII letters, digits,
    /// <c>-</c> and <c>_</c>, strips everything else (spaces, path separators, dots, punctuation),
    /// and trims leading/trailing <c>-</c>/<c>_</c>. Returns <see cref="string.Empty"/> when nothing
    /// survives — the caller then refuses the save (a scene must have a non-empty id). Pure and
    /// static so it is unit-testable and shared by the confirm path + tests.
    /// </summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_')
                sb.Append(c);
        return sb.ToString().Trim('-', '_');
    }
}
