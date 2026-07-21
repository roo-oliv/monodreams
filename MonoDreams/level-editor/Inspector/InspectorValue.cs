#nullable enable
using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.UI;

namespace MonoDreams.LevelEditor.Inspector;

/// <summary>The editable kinds the DevTools-grade Inspector supports (PF-A §3). Everything else is
/// <see cref="ReadOnly"/> — rendered muted, never edited.</summary>
public enum InspectorValueKind
{
    /// <summary>Not editable in v1 — rendered read-only in <c>TextMuted</c> (DevTools grey).</summary>
    ReadOnly,
    Float,
    Int,
    String,
    /// <summary>A boolean — clicking the value TOGGLES it (no inline field), one undoable step.</summary>
    Bool,
    /// <summary>A <see cref="Microsoft.Xna.Framework.Vector2"/> — an inline <c>"x, y"</c> field.</summary>
    Vector2,
    /// <summary>An enum — clicking the value CYCLES to the next member (no inline field), undoable.</summary>
    Enum,
}

/// <summary>The DevTools "syntax coloring" intent roles a member value renders in (PF-A §3), mapped to
/// <see cref="EditorTheme"/> roles by <see cref="InspectorValue.ForRole"/>. Documented mapping: numbers
/// <c>Info</c>, strings <c>Warning</c>, <c>true</c> <c>Success</c> / <c>false</c> <c>Danger</c>, enums
/// <c>Accent</c>, null/default <c>TextMuted</c>.</summary>
public enum InspectorValueRole
{
    /// <summary>Numbers (float / int / Vector2) → <c>Info</c>.</summary>
    Number,
    /// <summary>A non-empty string → <c>Warning</c> (warm).</summary>
    Text,
    /// <summary><c>true</c> → <c>Success</c>.</summary>
    True,
    /// <summary><c>false</c> → <c>Danger</c>.</summary>
    False,
    /// <summary>An enum value → <c>Accent</c>.</summary>
    EnumValue,
    /// <summary>null / empty / an unsupported (read-only) member → <c>TextMuted</c>.</summary>
    Muted,
}

/// <summary>
/// Pure classification + invariant-culture parsing + type-color mapping for the editable Inspector
/// (PF-A §3). World-free and GraphicsDevice-free (only static <see cref="EditorTheme"/> color roles),
/// so the whole matrix is unit-testable directly.
/// </summary>
public static class InspectorValue
{
    /// <summary>The editable kind of a member's declared CLR <paramref name="type"/> (v1: float / int /
    /// string / bool / Vector2 / enum; everything else <see cref="InspectorValueKind.ReadOnly"/>). A
    /// null type is read-only.</summary>
    public static InspectorValueKind Kind(Type? type)
    {
        if (type == null) return InspectorValueKind.ReadOnly;
        if (type == typeof(float)) return InspectorValueKind.Float;
        if (type == typeof(int)) return InspectorValueKind.Int;
        if (type == typeof(string)) return InspectorValueKind.String;
        if (type == typeof(bool)) return InspectorValueKind.Bool;
        if (type == typeof(Vector2)) return InspectorValueKind.Vector2;
        if (type.IsEnum) return InspectorValueKind.Enum;
        return InspectorValueKind.ReadOnly;
    }

    /// <summary>Whether a member of <paramref name="type"/> can be edited inline (a supported kind).</summary>
    public static bool IsEditable(Type? type) => Kind(type) != InspectorValueKind.ReadOnly;

    /// <summary>
    /// Parses <paramref name="raw"/> into a boxed value of the member's <paramref name="type"/>,
    /// culture-invariantly (so a comma-decimal locale never changes the grammar). Returns false — and
    /// leaves <paramref name="value"/> null — when the text is not a valid value of that type (the
    /// inline field then stays open, shown in <c>Danger</c>). <see cref="InspectorValueKind.Vector2"/>
    /// parses <c>"x, y"</c>; <see cref="InspectorValueKind.Bool"/> accepts <c>true</c>/<c>false</c> and
    /// <c>1</c>/<c>0</c>; an enum parses by member name (case-insensitive).
    /// </summary>
    public static bool TryParse(Type type, string? raw, out object? value)
    {
        value = null;
        var text = (raw ?? string.Empty).Trim();
        switch (Kind(type))
        {
            case InspectorValueKind.String:
                value = raw ?? string.Empty; // a string keeps its exact text (not trimmed)
                return true;
            case InspectorValueKind.Float:
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    value = f;
                    return true;
                }
                return false;
            case InspectorValueKind.Int:
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    value = i;
                    return true;
                }
                return false;
            case InspectorValueKind.Bool:
                if (bool.TryParse(text, out var b)) { value = b; return true; }
                if (text == "1") { value = true; return true; }
                if (text == "0") { value = false; return true; }
                return false;
            case InspectorValueKind.Vector2:
                var parts = text.Split(',');
                if (parts.Length == 2
                    && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    value = new Vector2(x, y);
                    return true;
                }
                return false;
            case InspectorValueKind.Enum:
                try
                {
                    value = Enum.Parse(type, text, ignoreCase: true);
                    return true;
                }
                catch
                {
                    return false;
                }
            default:
                return false; // ReadOnly
        }
    }

    /// <summary>The next enum member after <paramref name="current"/> (wrapping) — the click-to-cycle
    /// behavior for an enum member. A value not found in the set restarts at the first member.</summary>
    public static object NextEnumValue(Type enumType, object? current)
    {
        var values = Enum.GetValues(enumType);
        if (values.Length == 0) return current!;
        var idx = current == null ? -1 : Array.IndexOf(values, current);
        return values.GetValue((idx + 1) % values.Length)!;
    }

    /// <summary>The DevTools syntax-color role for a member's live value (PF-A §3). Pure — the color
    /// mapping the premise documents; a null / empty / read-only member is <see cref="InspectorValueRole.Muted"/>.</summary>
    public static InspectorValueRole Role(Type? type, object? value)
    {
        if (value == null) return InspectorValueRole.Muted;
        switch (Kind(type))
        {
            case InspectorValueKind.Bool:
                return value is true ? InspectorValueRole.True : InspectorValueRole.False;
            case InspectorValueKind.Enum:
                return InspectorValueRole.EnumValue;
            case InspectorValueKind.Float:
            case InspectorValueKind.Int:
            case InspectorValueKind.Vector2:
                return InspectorValueRole.Number;
            case InspectorValueKind.String:
                return ((string)value).Length == 0 ? InspectorValueRole.Muted : InspectorValueRole.Text;
            default:
                return InspectorValueRole.Muted; // unsupported member → DevTools grey
        }
    }

    /// <summary>Resolves a color <see cref="InspectorValueRole"/> to its <see cref="EditorTheme"/> role
    /// color (the ONE palette source; no raw colors here — lint-safe).</summary>
    public static Color ForRole(InspectorValueRole role) => role switch
    {
        InspectorValueRole.Number => EditorTheme.Info,
        InspectorValueRole.Text => EditorTheme.Warning,
        InspectorValueRole.True => EditorTheme.Success,
        InspectorValueRole.False => EditorTheme.Danger,
        InspectorValueRole.EnumValue => EditorTheme.Accent,
        _ => EditorTheme.TextMuted,
    };
}
