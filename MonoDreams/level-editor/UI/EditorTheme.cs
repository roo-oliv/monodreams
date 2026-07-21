#nullable enable
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// The <b>single source of every color and depth</b> in the level-editor module (chrome <i>and</i>
/// viewport overlays) — the strict warm-dark palette with a Claude-coral accent (editor-shell UI/UX
/// design §1). The de-facto palette that used to be scattered across <c>EditorChromeBuilder</c>,
/// <c>EditorDialogSystem</c>, <c>PalettePlacementSystem</c> and <c>EditorPanelSystem.RowColor</c>
/// lives here now; layout <b>metrics</b> stay in the layout classes (geometry ≠ style). A source-scan
/// test (<c>EditorThemeLintTests</c>) forbids any raw <c>new Color(</c> or named color token in the
/// module <b>outside this file</b>, so adding a color means adding a role here, consciously — this is
/// the ONLY file allowed to name a raw/XNA color.
///
/// <para><b>Semantic rule.</b> <see cref="Accent"/> = "this is selected / the primary thing to do";
/// <see cref="Success"/> = "this is on"; <see cref="Danger"/> = "this destroys something" — never
/// decorative.</para>
///
/// <para><b>The premultiplied-alpha rule (pre-mortem #1).</b> Editor mesh fills composite with
/// premultiplied alpha, so a partial-alpha RGB renders far brighter than intended (a 50%-grey reads
/// near-white). Every "translucent-looking" mesh fill here is therefore a <b>precomputed OPAQUE
/// blend</b> (<see cref="AccentSoft"/> is Accent blended into <see cref="Bg1"/>, NOT <c>Accent × α</c>).
/// The only alpha in this file is on SPRITE tints (<see cref="GhostTint"/>), where alpha is fine —
/// the opaque rule is a mesh-path rule.</para>
/// </summary>
public static class EditorTheme
{
    // ─── Backgrounds ────────────────────────────────────────────────────────────────────────────
    /// <summary>Dialog backdrop, tab-strip background, deepest chrome.</summary>
    public static readonly Color Bg0 = new(20, 19, 18);
    /// <summary>Panel bodies: right strip, bottom shelf, top bar, dialog panel.</summary>
    public static readonly Color Bg1 = new(30, 29, 27);
    /// <summary>Raised controls at rest: buttons, cards, fields.</summary>
    public static readonly Color Bg2 = new(45, 43, 40);
    /// <summary>Hovered controls / hovered rows.</summary>
    public static readonly Color Bg3 = new(58, 55, 51);
    /// <summary>Pressed controls.</summary>
    public static readonly Color Bg4 = new(70, 66, 60);
    /// <summary>Disabled control fill.</summary>
    public static readonly Color BgDisabled = new(36, 35, 33);

    // ─── Borders ────────────────────────────────────────────────────────────────────────────────
    /// <summary>Panel edges, splitter idle, scrollbar track.</summary>
    public static readonly Color Border = new(62, 58, 53);
    /// <summary>Control outlines, splitter hover/drag, scrollbar thumb.</summary>
    public static readonly Color BorderStrong = new(96, 90, 82);

    // ─── Text ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>Primary labels (ivory).</summary>
    public static readonly Color Text0 = new(240, 238, 230);
    /// <summary>Secondary labels, subtitles, headers.</summary>
    public static readonly Color Text1 = new(178, 172, 162);
    /// <summary>Placeholders, de-emphasized values.</summary>
    public static readonly Color TextMuted = new(122, 117, 108);
    /// <summary>Disabled labels.</summary>
    public static readonly Color TextDisabled = new(100, 96, 90);

    // ─── Semantic accents ───────────────────────────────────────────────────────────────────────
    /// <summary><b>Selection + primary action</b> (Claude coral): selected rows/cards edge+border,
    /// active-tab underline, primary dialog action, armed palette item.</summary>
    public static readonly Color Accent = new(217, 119, 87);
    /// <summary>Selected-row/card fill — a <b>precomputed OPAQUE blend of <see cref="Accent"/> into
    /// <see cref="Bg1"/></b>. MUST stay opaque: the mesh path composites premultiplied, so
    /// <c>Accent × alpha</c> would blow out near-white (pre-mortem #1). To retune, recompute the
    /// opaque blend — never lower the alpha.</summary>
    public static readonly Color AccentSoft = new(66, 45, 39);
    /// <summary><b>On/enabled</b> semantics: checkbox-on, Play affordance.</summary>
    public static readonly Color Success = new(107, 166, 113);
    /// <summary>Destructive-adjacent notes ("discards unsaved edits"), dirty marker.</summary>
    public static readonly Color Warning = new(224, 164, 88);
    /// <summary>Destructive actions (Discard &amp; Switch, Remove collider).</summary>
    public static readonly Color Danger = new(229, 72, 77);
    /// <summary>Informational status text.</summary>
    public static readonly Color Info = new(108, 169, 216);

    // ─── Sprite tints (the SPRITE path — alpha is fine here, unlike mesh fills) ────────────────────
    /// <summary>The placement-ghost sprite tint: <c>White × 0.55</c>. This is a sprite color, so its
    /// alpha is legitimate (the opaque rule is for the MESH path only).</summary>
    public static readonly Color GhostTint = Color.White * 0.55f;
    /// <summary>The neutral (no-modulation) sprite tint — full color — for placed props and palette
    /// thumbnails. A role (not a bare <c>Color.White</c>) so the module keeps one color source.</summary>
    public static readonly Color NeutralTint = Color.White;

    // ─── Viewport overlays (gizmo / proxy / boundary / trigger) ────────────────────────────────────
    // Migrated from the XNA named colors UNCHANGED (this file is the one place allowed to name them,
    // so the migration is provably byte-identical to the pre-theme rendered values).
    /// <summary>The interactive collider-proxy outline — the design's "current gizmo cyan".</summary>
    public static readonly Color OverlayAccent = Color.Cyan;
    /// <summary>The gizmo selection-highlight outline.</summary>
    public static readonly Color OverlaySelection = Color.Yellow;
    /// <summary>The move gizmo's X arm.</summary>
    public static readonly Color GizmoAxisX = Color.OrangeRed;
    /// <summary>The move gizmo's Y arm.</summary>
    public static readonly Color GizmoAxisY = Color.LimeGreen;
    /// <summary>The gizmo's centre handle knob / box-proxy centre disc.</summary>
    public static readonly Color GizmoHandle = Color.White;
    /// <summary>The rotate gizmo ring.</summary>
    public static readonly Color GizmoRotate = Color.DeepSkyBlue;
    /// <summary>The scale gizmo handle + box-proxy resize squares.</summary>
    public static readonly Color GizmoScale = Color.Gold;
    /// <summary>A committed boundary's outline.</summary>
    public static readonly Color OverlayBoundary = Color.Aqua;
    /// <summary>The in-progress boundary lay preview line.</summary>
    public static readonly Color OverlayBoundaryPreview = Color.Orange;
    /// <summary>The scene camera entity's frustum glyph (bounds + X) — CM. A cool light-steel-blue,
    /// distinct from the warm props and the other overlay accents (cyan proxy / yellow selection / aqua
    /// boundary), so "the camera is over there" reads at a glance. A NEW role (no pre-theme value to
    /// preserve), authored here like every other overlay color.</summary>
    public static readonly Color CameraGlyph = new(158, 190, 228);
    /// <summary>The world-space reference grid's MINOR lines (UX3-D §3) — a subtle warm dark that
    /// reads under content (drawn beneath everything at <see cref="Depths.Grid"/>). Between
    /// <see cref="Bg2"/> and <see cref="Bg3"/> on the warm-dark ramp: visible over a light level, quiet
    /// over a dark one — the deliberate "subtle" tradeoff (documented, no pre-theme value to preserve).</summary>
    public static readonly Color GridMinor = new(52, 50, 46);
    /// <summary>The world-space reference grid's MAJOR (every-5th) lines (UX3-D §3) — the stronger
    /// cadence line, near <see cref="Border"/> so it reads a step above <see cref="GridMinor"/> without
    /// competing with content.</summary>
    public static readonly Color GridMajor = new(84, 79, 72);
    /// <summary>A trigger zone's Edit-only outline (amber).</summary>
    public static readonly Color OverlayTrigger = new(255, 196, 64);
    /// <summary>The armed-trigger placement ghost (the trigger tint, dimmed). A sprite/overlay tint
    /// preserved from the pre-theme <c>OutlineColor × 0.7</c>.</summary>
    public static readonly Color OverlayTriggerGhost = OverlayTrigger * 0.7f;
    /// <summary>The missing-asset placeholder texture fill (unmistakable magenta).</summary>
    public static readonly Color PlaceholderMagenta = Color.Magenta;

    // ─── Interaction-state helpers (the shared hover-fade + fill recipe) ───────────────────────────
    /// <summary>Framerate-independent hover-fade speed — the engine's <c>ButtonVisualSystem</c>
    /// recipe (<c>Lerp(current, target, clamp(speed·dt))</c>).</summary>
    public const float HoverFadeSpeed = 18f;

    /// <summary>Advances a per-widget hover progress toward its target (1 = hovered, 0 = not) with the
    /// standard framerate-independent ease. Store the returned value back on the widget's own
    /// component/struct — NEVER on a pooled-row entity (pooled rows highlight instantly instead).</summary>
    public static float AdvanceHover(float current, bool hovered, float dt) =>
        MathHelper.Lerp(current, hovered ? 1f : 0f, MathHelper.Clamp(HoverFadeSpeed * dt, 0f, 1f));

    /// <summary>
    /// The fill for an interactive control given its state, in priority order: disabled →
    /// <see cref="BgDisabled"/>; selected/armed → <see cref="AccentSoft"/> (pair with an
    /// <see cref="Accent"/> border/edge at the call site); pressed → <see cref="Bg4"/>; otherwise the
    /// hover-faded blend from <see cref="Bg2"/> (idle) to <see cref="Bg3"/> (hovered). All results are
    /// opaque (both endpoints are opaque, so the lerp stays opaque — mesh-path safe).
    /// </summary>
    public static Color ControlFill(bool disabled, bool selected, bool pressed, float hoverProgress)
    {
        if (disabled) return BgDisabled;
        if (selected) return AccentSoft;
        if (pressed) return Bg4;
        return Color.Lerp(Bg2, Bg3, MathHelper.Clamp(hoverProgress, 0f, 1f));
    }

    /// <summary>
    /// The one Editor-target <b>depth stack</b>, declared in a single place so the chrome's paint
    /// order is legible end-to-end. Larger = nearer the viewer within a render pass. Overlays occupy
    /// the low band beneath the opaque panels (so panels cover them over the margins); the dialog band
    /// sits above everything (a modal covers the toolbar + panels).
    /// </summary>
    public static class Depths
    {
        /// <summary>The world-space reference grid (UX3-D §3) — the LOWEST overlay band, beneath the
        /// proxy/gizmo/glyph overlays and the opaque panels, so content and every other overlay draw
        /// over it (it is the backdrop reference, not a foreground mark).</summary>
        public const float Grid = 0.01f;
        /// <summary>Collider-proxy outlines — beneath the gizmo and the opaque panels.</summary>
        public const float ProxyOverlay = 0.02f;
        /// <summary>The camera-entity frustum glyph (CM) — above the proxy outlines, below the gizmo
        /// handles so the camera's move handle draws over its own frustum when it is selected.</summary>
        public const float CameraGlyph = 0.03f;
        /// <summary>Gizmo handles + selection outline — just above the proxy band.</summary>
        public const float GizmoOverlay = 0.04f;
        /// <summary>Opaque shell panel backgrounds (top bar, right strip, bottom shelf).</summary>
        public const float Panel = 0.1f;
        /// <summary>The region splitter lines (right strip / bottom shelf resize edges) — just above
        /// the panels they edge so they read over the panel fill.</summary>
        public const float Splitter = 0.12f;
        /// <summary>Right-strip row background fill (hover / selected) — above the panel, behind
        /// controls.</summary>
        public const float RowFill = 0.3f;
        /// <summary>The selected row's 3pt Accent left-edge bar — just above <see cref="RowFill"/> so
        /// it reads over the fill.</summary>
        public const float RowAccentBar = 0.31f;
        /// <summary>The scrollbar track + thumb — above the row fills, below the controls/labels.</summary>
        public const float Scrollbar = 0.35f;
        /// <summary>Buttons / cards / checkboxes / tab fills.</summary>
        public const float Button = 0.5f;
        /// <summary>The active tab's 3pt Accent underline — just above the tab fill, below labels.</summary>
        public const float TabUnderline = 0.52f;
        /// <summary>The indeterminate (mixed) checkbox minus bar — above the checkbox, below labels.</summary>
        public const float CheckboxMark = 0.55f;
        /// <summary>Palette card art thumbnails — above the card fill, below its label.</summary>
        public const float Thumbnail = 0.56f;
        /// <summary>Palette per-asset band-mark chip badge — above the thumbnail, below labels.</summary>
        public const float Chip = 0.58f;
        /// <summary>Labels, disclosure arrows + toolbar button icons (UX2-C) — the font-independent mesh
        /// glyphs share the label band, above their button/row fill.</summary>
        public const float Label = 0.6f;
        /// <summary>Modal dialog backdrop dimmer.</summary>
        public const float DialogBackdrop = 0.70f;
        /// <summary>Modal dialog panel.</summary>
        public const float DialogPanel = 0.74f;
        /// <summary>Modal dialog controls (buttons, field, rows).</summary>
        public const float DialogControl = 0.80f;
        /// <summary>Modal dialog labels (topmost of the dialog band).</summary>
        public const float DialogLabel = 0.86f;
        /// <summary>The hover tooltip box + outline (UX2-C) — above EVERYTHING, including the dialog, so
        /// a tooltip is never occluded by the modal it may hover over.</summary>
        public const float Tooltip = 0.90f;
        /// <summary>The hover tooltip's label — just above its box.</summary>
        public const float TooltipLabel = 0.92f;
        /// <summary>The context-menu box + item fills + separators (UX2-D) — a dedicated band ABOVE the
        /// tooltip so a popup menu is never occluded (the menu and the dialog are never open at once,
        /// so sharing the very top is safe).</summary>
        public const float MenuPanel = 0.94f;
        /// <summary>Context-menu item hover fills + separator lines — above the box, below the labels.</summary>
        public const float MenuControl = 0.96f;
        /// <summary>Context-menu item labels + submenu ▸ caret meshes (topmost of the menu band).</summary>
        public const float MenuLabel = 0.98f;
    }
}
