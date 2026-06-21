using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// Character classes a <see cref="TextInputComponent"/> accepts. Masking is the
/// one "smart" feature this primitive ships with on purpose — richer behaviors
/// (formatting, placeholder text, validation / error states) are intentionally
/// left out so they can be layered on later without reworking the core.
public enum TextInputMask
{
    /// Accept any printable character the key map produces.
    None,
    /// Accept only digits 0-9.
    Numeric,
}

/// A minimal editable single-line text field. Pure data: <see cref="TextInputSystem"/>
/// inserts or deletes characters at the <see cref="CaretPosition"/> (honoring
/// <see cref="Mask"/> and <see cref="MaxLength"/>) while <see cref="Focused"/> is true,
/// mirrors <see cref="Text"/> onto the linked <see cref="TextEntity"/>'s
/// <c>DynamicTextComponent</c>, and — when <see cref="CaretEntity"/> is set — draws a
/// vertical white caret line at the insertion point. Focus itself is game-owned — set it
/// from your own click / interaction system, the same way button click dispatch is
/// game-owned (see the ui premises).
public struct TextInputComponent
{
    /// Current value. Null is treated as empty.
    public string Text;
    /// Maximum character count; 0 or less means unbounded.
    public int MaxLength;
    /// Which characters typing will accept.
    public TextInputMask Mask;
    /// When true, this field consumes the keyboard this frame.
    public bool Focused;
    /// The entity whose <c>DynamicTextComponent.TextContent</c> displays <see cref="Text"/>.
    public Entity TextEntity;
    /// Insertion point as an index into <see cref="Text"/> (0 = before the first character,
    /// <c>Text.Length</c> = after the last). Typing inserts here and advances it; Left/Right
    /// step it; Home/End jump to the ends. <see cref="TextInputSystem"/> clamps it into range
    /// whenever the value changes. Game code may set it directly — placing the caret when a
    /// field is focused (e.g. at the end of a pre-filled value) is a focus policy, hence
    /// game-owned like <see cref="Focused"/>. A field created with non-empty <see cref="Text"/>
    /// should set this to <c>Text.Length</c> so editing starts at the end, not the front.
    public int CaretPosition;
    /// Optional. When set to a live mesh entity (a <c>DrawComponent</c> of type Mesh plus a
    /// <c>TransformComponent</c>, parented under <see cref="TextEntity"/>), the system draws a
    /// vertical white caret line into it at <see cref="CaretPosition"/> while the field is
    /// <see cref="Focused"/>, and clears the mesh otherwise. Leave it at <c>default</c> to opt
    /// out of caret rendering — the editing logic works either way.
    public Entity CaretEntity;
    /// Optional hint shown on the linked text entity while <see cref="Text"/> is empty (focused or
    /// not). Null/empty disables it. <see cref="TextInputSystem"/> swaps the displayed string and,
    /// when <see cref="TextColor"/> is set, the color between value and placeholder.
    public string Placeholder;
    /// Color for the placeholder string. Applied only when it has alpha and a placeholder shows.
    public Color PlaceholderColor;
    /// The normal value color, restored when the field has text. Set this alongside
    /// <see cref="Placeholder"/> so the system can swap back from <see cref="PlaceholderColor"/>.
    /// Left at default (alpha 0) the system leaves the linked text's color alone — back-compat for
    /// fields that manage their own color (e.g. the demos' number-input rows).
    public Color TextColor;
}

/// Published by <see cref="TextInputSystem"/> on the frame an input's
/// <see cref="TextInputComponent.Text"/> changes. Carries the input entity and its
/// new value so game code can react (parse a number, run a search, …) without
/// polling every field each frame.
public readonly record struct TextInputChanged(Entity Input, string Text);
