#nullable enable
using System;
using System.Globalization;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// The pure, world-free application of the viewport-overlay edits (UX3-D §3/§7) — shared by BOTH the
/// headless op channel (<c>overlay:grid on|off</c> / <c>overlay:spacing &lt;n&gt;</c> /
/// <c>overlay:outline on|off</c> / <c>overlay:camera on|off</c>) and the Overlays dropdown menu (the
/// <c>overlay/*</c> action-id paths), so the two drive the exact same state through one code path
/// (mirrors <c>CameraNav</c> / <c>GizmoTransform</c> — logic separated from a system, unit-testable).
///
/// <para><b>One spacing value.</b> The grid spacing IS the gizmo snap step
/// (<see cref="GizmoStateComponent.GridStep"/>) — there is no second copy. Both the <c>spacing</c> op
/// and the menu's spacing presets write that single authoritative field, so "the displayed grid is the
/// grid things snap to" holds by construction (see the viewport-overlays premise).</para>
/// </summary>
public static class ViewportOverlayOps
{
    /// <summary>The op-channel prefix (<c>overlay:grid on</c>, …).</summary>
    public const string OpPrefix = "overlay:";

    // The menu action-id paths (the menu:pick grammar + the overlay's DispatchMenuAction map).
    public const string GridTogglePath = "overlay/grid";
    public const string OutlineTogglePath = "overlay/outline";
    public const string CameraTogglePath = "overlay/camera";
    public const string SpacingSubmenuPath = "overlay/spacing";
    public const string SpacingPathPrefix = SpacingSubmenuPath + "/";

    /// <summary>The spacing presets the "Grid Spacing ▸" submenu offers (world units) — each writes the
    /// shared snap step.</summary>
    public static readonly float[] SpacingPresets = { 8f, 16f, 32f, 64f };

    /// <summary>The menu path for a spacing preset (e.g. <c>overlay/spacing/16</c>).</summary>
    public static string SpacingPath(float preset) => SpacingPathPrefix + FormatSpacing(preset);

    /// <summary>
    /// Applies an <c>overlay:*</c> op-string to the settings + the shared grid step. Returns
    /// <c>false</c> (the caller logs) when the verb/argument is unrecognized. The <c>spacing</c> verb
    /// writes <see cref="GizmoStateComponent.GridStep"/> (the ONE grid quantum), never a copy.
    /// </summary>
    public static bool TryApplyOp(string op,
        ref ViewportOverlaySettingsComponent settings, ref GizmoStateComponent gizmo)
    {
        if (op == null || !op.StartsWith(OpPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = op.Substring(OpPrefix.Length).Trim();
        var space = rest.IndexOf(' ');
        var verb = (space < 0 ? rest : rest.Substring(0, space)).ToLowerInvariant();
        var arg = space < 0 ? string.Empty : rest.Substring(space + 1).Trim();
        switch (verb)
        {
            case "grid": return TryParseOnOff(arg, out var g) && SetTrue(ref settings.ShowGrid, g);
            case "outline": return TryParseOnOff(arg, out var o) && SetTrue(ref settings.OutlineSelected, o);
            case "camera": return TryParseOnOff(arg, out var c) && SetTrue(ref settings.ShowCameraGlyph, c);
            case "spacing":
                if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n <= 0f)
                    return false;
                gizmo.GridStep = n; // the single authoritative grid quantum
                return true;
            default: return false;
        }
    }

    /// <summary>
    /// Applies an Overlays-menu action-id <paramref name="path"/> — a toggle FLIPS its setting, a
    /// spacing preset SETS the shared grid step. Returns <c>false</c> when the path is not an
    /// <c>overlay/*</c> path (the caller falls through to its other menu actions).
    /// </summary>
    public static bool TryApplyMenuPath(string path,
        ref ViewportOverlaySettingsComponent settings, ref GizmoStateComponent gizmo)
    {
        switch (path)
        {
            case GridTogglePath: settings.ShowGrid = !settings.ShowGrid; return true;
            case OutlineTogglePath: settings.OutlineSelected = !settings.OutlineSelected; return true;
            case CameraTogglePath: settings.ShowCameraGlyph = !settings.ShowCameraGlyph; return true;
        }
        if (path != null && path.StartsWith(SpacingPathPrefix, StringComparison.Ordinal)
            && float.TryParse(path.Substring(SpacingPathPrefix.Length),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0f)
        {
            gizmo.GridStep = n; // same single authoritative field the op writes
            return true;
        }
        return false;
    }

    private static bool SetTrue(ref bool field, bool value)
    {
        field = value;
        return true;
    }

    private static bool TryParseOnOff(string arg, out bool value)
    {
        switch (arg.Trim().ToLowerInvariant())
        {
            case "on": case "true": case "1": case "yes": value = true; return true;
            case "off": case "false": case "0": case "no": value = false; return true;
            default: value = false; return false;
        }
    }

    private static string FormatSpacing(float v) =>
        v == (int)v
            ? ((int)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString(CultureInfo.InvariantCulture);
}
