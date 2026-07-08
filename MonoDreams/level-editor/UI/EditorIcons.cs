#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// The editor toolbar's icon set (UX2-C): each icon is a pure list of line-segment / triangle
/// primitives authored in a <b>unit box</b> (<c>[0,1]²</c>, <c>u</c> right / <c>v</c> down like the
/// chrome's screen space) and instantiated into a pixel <see cref="Rectangle"/> — the
/// <c>SystemsPanelLayout.ArrowTriangle</c> disclosure-caret pattern generalized to a whole glyph set.
/// The shapes reference Lucide for their VISUAL language (move = cross-arrows, rotate = circular
/// arrow, save = floppy, …) but import nothing — they are procedural meshes so the editor stays
/// font-independent, DPR-crisp and theme-colored (the same font-free draw path the gizmo overlays and
/// disclosure arrows already use; pre-mortem #8 keeps every shape to ≤3 visual strokes so it reads at
/// ~16pt logical). Colours are NEVER named here (the palette lint): the caller passes an
/// <c>EditorTheme</c> role and the geometry bakes it into every vertex.
///
/// <para><b>Purity + DPR.</b> <see cref="Build"/> is a pure function of (icon, rect, color): every
/// vertex is <c>rect.TopLeft + unit·rect.Size</c> and every stroke thickness / arrowhead size is a
/// fraction of the rect, so scaling the rect (a HiDPI device-pixel-ratio bump) scales the whole mesh
/// proportionally — "DPR scaling is pure rect scaling". Undo/Redo and Restart/Refresh are exact
/// horizontal MIRRORS of one another (the same shape drawn with <c>u → 1-u</c>), so the orientation is
/// testable without a GraphicsDevice.</para>
///
/// <para><b>Adding an icon</b> is one <see cref="EditorIcon"/> value + one <c>case</c> in
/// <see cref="Build"/> (+ its pure test) — deliberately cheap so UX2-D/E/F (context-menu dots, the
/// camera frustum, the Scene|Game glyphs) drop in without touching this file's structure.</para>
/// </summary>
public static class EditorIcons
{
    /// <summary>The glyphs this wave ships. PlayPause resolves to <see cref="Play"/> or
    /// <see cref="Pause"/> by transport state (see <see cref="Resolve"/>); the rest map 1:1 from an
    /// <see cref="EditorToolbarAction"/> via <see cref="ForAction"/>.</summary>
    public enum EditorIcon
    {
        Move, Rotate, Scale, Boundary, Snap,
        Play, Pause, Restart,
        Save, Undo, Redo, Refresh,
    }

    /// <summary>The fraction of the smaller button dimension the glyph square occupies (centered) — the
    /// button keeps breathing room around the icon (an ~18pt glyph inside a ~30pt button).</summary>
    public const float DefaultGlyphFraction = 0.62f;

    /// <summary>Stroke thickness as a fraction of the glyph square's side (so it scales with the rect —
    /// pure DPR scaling). Tuned to read as a firm line at ~16pt without clogging the shape.</summary>
    private const float StrokeFraction = 0.085f;

    /// <summary>
    /// The static icon for a toolbar action, or <c>null</c> when the action has <b>no icon this wave</b>
    /// (the selection-context Order / collider / vertex buttons stay text until UX2-D relocates them) —
    /// a null result means "render this button with its text label, not an icon". PlayPause maps to
    /// <see cref="EditorIcon.Play"/> as its resting glyph; the running icon is chosen by
    /// <see cref="Resolve"/>.
    /// </summary>
    public static EditorIcon? ForAction(EditorToolbarAction action) => action switch
    {
        EditorToolbarAction.PlayPause => EditorIcon.Play,
        EditorToolbarAction.Restart => EditorIcon.Restart,
        EditorToolbarAction.ToolMove => EditorIcon.Move,
        EditorToolbarAction.ToolRotate => EditorIcon.Rotate,
        EditorToolbarAction.ToolScale => EditorIcon.Scale,
        EditorToolbarAction.ToolBoundary => EditorIcon.Boundary,
        EditorToolbarAction.ToggleSnap => EditorIcon.Snap,
        EditorToolbarAction.Save => EditorIcon.Save,
        EditorToolbarAction.Undo => EditorIcon.Undo,
        EditorToolbarAction.Redo => EditorIcon.Redo,
        EditorToolbarAction.RefreshCatalog => EditorIcon.Refresh,
        _ => null,
    };

    /// <summary>Whether an action renders as an icon button (has an icon) vs a text button.</summary>
    public static bool HasIcon(EditorToolbarAction action) => ForAction(action) != null;

    /// <summary>The icon to draw for an action given the live transport state: the Play/Pause toggle
    /// shows <see cref="EditorIcon.Pause"/> while Playing and <see cref="EditorIcon.Play"/> while Paused
    /// (the icon analog of the old label swap); every other action is state-independent.</summary>
    public static EditorIcon? Resolve(EditorToolbarAction action, bool playing) =>
        action == EditorToolbarAction.PlayPause
            ? (playing ? EditorIcon.Pause : EditorIcon.Play)
            : ForAction(action);

    /// <summary>The centered glyph square inside a button's bounds (a fraction of the smaller side, so
    /// it is square and doubles with the button on a HiDPI backbuffer). Pure — testable directly.</summary>
    public static Rectangle CenteredIconRect(Rectangle button, float glyphFraction = DefaultGlyphFraction)
    {
        var side = (int)MathF.Round(Math.Min(button.Width, button.Height) * glyphFraction);
        if (side < 1) side = 1;
        return new Rectangle(
            button.X + (button.Width - side) / 2,
            button.Y + (button.Height - side) / 2,
            side, side);
    }

    /// <summary>Builds the icon's triangle-list mesh inside <paramref name="rect"/>, every vertex in
    /// <paramref name="color"/>. Pure geometry (no GraphicsDevice) — the toolbar bakes the result into a
    /// screen-baked <c>DrawComponent</c> exactly like a disclosure arrow.</summary>
    public static MeshData Build(EditorIcon icon, Rectangle rect, Color color)
    {
        var pen = new Pen(rect, color, StrokeFraction);
        switch (icon)
        {
            case EditorIcon.Move: Move(pen); break;
            case EditorIcon.Rotate: Rotate(pen); break;
            case EditorIcon.Scale: Scale(pen); break;
            case EditorIcon.Boundary: Boundary(pen); break;
            case EditorIcon.Snap: Snap(pen); break;
            case EditorIcon.Save: Save(pen); break;
            case EditorIcon.Play: Play(pen); break;
            case EditorIcon.Pause: Pause(pen); break;
            case EditorIcon.Restart: CircularArrow(pen); break;   // ↺
            case EditorIcon.Refresh: pen.Mirror = true; CircularArrow(pen); break; // ↻ = the mirror
            case EditorIcon.Undo: AngularArrow(pen); break;        // ↶
            case EditorIcon.Redo: pen.Mirror = true; AngularArrow(pen); break;     // ↷ = the mirror
            default: throw new ArgumentOutOfRangeException(nameof(icon), icon, "Unknown editor icon.");
        }
        return pen.ToMesh();
    }

    // ── The glyphs (unit-box authored; Pen maps to pixels + applies the mirror + bakes the color) ─────

    /// <summary>Four-way move arrows: a plus with an arrowhead on each arm (Lucide "move").</summary>
    private static void Move(Pen p)
    {
        p.Line(0.5f, 0.18f, 0.5f, 0.82f);   // vertical arm
        p.Line(0.18f, 0.5f, 0.82f, 0.5f);   // horizontal arm
        p.Arrow(0.5f, 0.06f, 0f, -1f);      // up
        p.Arrow(0.5f, 0.94f, 0f, 1f);       // down
        p.Arrow(0.06f, 0.5f, -1f, 0f);      // left
        p.Arrow(0.94f, 0.5f, 1f, 0f);       // right
    }

    /// <summary>The rotate tool: a ~290° circular arrow (a spin), gap + arrowhead near the top-right.</summary>
    private static void Rotate(Pen p)
    {
        p.Arc(0.5f, 0.5f, 0.30f, 120f, 120f + 290f, 16);
        // Tangent at the end (increasing angle sweeps clockwise in v-down space): point the head along it.
        p.ArrowAtAngle(0.5f, 0.5f, 0.30f, 120f + 290f, forward: true, 0.13f);
    }

    /// <summary>Scale: a small square with a diagonal resize arrow pushing out of its far corner.</summary>
    private static void Scale(Pen p)
    {
        // Square outline, lower-left.
        p.Line(0.16f, 0.5f, 0.5f, 0.5f);
        p.Line(0.5f, 0.5f, 0.5f, 0.84f);
        p.Line(0.5f, 0.84f, 0.16f, 0.84f);
        p.Line(0.16f, 0.84f, 0.16f, 0.5f);
        // Diagonal arrow toward the top-right corner.
        p.Line(0.5f, 0.5f, 0.82f, 0.18f);
        p.Arrow(0.88f, 0.12f, 0.7071f, -0.7071f);
    }

    /// <summary>Boundary: a closed pentagon outline (the freeform region tool).</summary>
    private static void Boundary(Pen p)
    {
        var pts = Polygon(0.5f, 0.52f, 0.36f, 5, -90f);
        for (var i = 0; i < pts.Length; i++)
            p.Line(pts[i].u, pts[i].v, pts[(i + 1) % pts.Length].u, pts[(i + 1) % pts.Length].v);
    }

    /// <summary>Snap-to-grid: a 2×2 grid (a tic-tac-toe hash), the "grid" affordance.</summary>
    private static void Snap(Pen p)
    {
        p.Line(0.38f, 0.16f, 0.38f, 0.84f); // verticals
        p.Line(0.62f, 0.16f, 0.62f, 0.84f);
        p.Line(0.16f, 0.38f, 0.84f, 0.38f); // horizontals
        p.Line(0.16f, 0.62f, 0.84f, 0.62f);
    }

    /// <summary>Save — a floppy disk: a body outline, the top shutter, and the label plate (Lucide
    /// "save"). Recognisably "save" without importing the asset.</summary>
    private static void Save(Pen p)
    {
        // Body outline (square).
        p.Line(0.18f, 0.18f, 0.82f, 0.18f);
        p.Line(0.82f, 0.18f, 0.82f, 0.82f);
        p.Line(0.82f, 0.82f, 0.18f, 0.82f);
        p.Line(0.18f, 0.82f, 0.18f, 0.18f);
        // The metal shutter (top).
        p.FillQuad(0.36f, 0.18f, 0.64f, 0.38f);
        // The label plate (bottom).
        p.FillQuad(0.34f, 0.54f, 0.66f, 0.78f);
    }

    /// <summary>Play ▶ — a filled right-pointing triangle.</summary>
    private static void Play(Pen p) => p.Tri(0.28f, 0.16f, 0.28f, 0.84f, 0.82f, 0.5f);

    /// <summary>Pause ⏸ — two filled vertical bars.</summary>
    private static void Pause(Pen p)
    {
        p.FillQuad(0.30f, 0.18f, 0.45f, 0.82f);
        p.FillQuad(0.55f, 0.18f, 0.70f, 0.82f);
    }

    /// <summary>Restart ↺ / Refresh ↻ (the mirror): a ~260° circular arrow with a gap + arrowhead —
    /// the reload glyph. Distinct from <see cref="Rotate"/> (different sweep/gap) and from each other
    /// (Refresh is the exact horizontal mirror).</summary>
    private static void CircularArrow(Pen p)
    {
        p.Arc(0.5f, 0.5f, 0.29f, -55f, -55f + 260f, 15);
        p.ArrowAtAngle(0.5f, 0.5f, 0.29f, -55f, forward: false, 0.13f);
    }

    /// <summary>Undo ↶ / Redo ↷ (the mirror): an angular arrow — a short shaft that hooks down-left with
    /// an arrowhead, shallower than the circular arrows so it reads as a directional "step".</summary>
    private static void AngularArrow(Pen p)
    {
        // A shallow ~150° arc curving from the right down to the left, arrowhead on the LEFT (mirrored
        // for Redo → arrowhead on the right).
        p.Arc(0.5f, 0.42f, 0.30f, 20f, 20f + 150f, 12);
        p.ArrowAtAngle(0.5f, 0.42f, 0.30f, 20f + 150f, forward: true, 0.13f);
    }

    // ── Unit-box helpers ──────────────────────────────────────────────────────────────────────────

    /// <summary>The <paramref name="sides"/> vertices of a regular polygon in unit coords (center +
    /// radius + start angle), used for the boundary glyph.</summary>
    private static (float u, float v)[] Polygon(float cx, float cy, float r, int sides, float startDeg)
    {
        var pts = new (float u, float v)[sides];
        for (var i = 0; i < sides; i++)
        {
            var a = (startDeg + i * 360f / sides) * MathF.PI / 180f;
            pts[i] = (cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }
        return pts;
    }

    /// <summary>
    /// Accumulates an icon's triangle list in pixel space. All shape code authors in the unit box and
    /// calls through <see cref="P"/>, which maps unit→pixel AND applies the optional horizontal
    /// <see cref="Mirror"/> (<c>u → 1-u</c>) — so a mirrored icon is the exact reflection of the
    /// original, and every point (including stroke offsets and arrowheads) scales purely with the rect.
    /// </summary>
    private sealed class Pen
    {
        private readonly Rectangle _rect;
        private readonly Color _color;
        private readonly float _thickness;
        private readonly float _side;
        private readonly List<VertexPositionColor> _vertices = new();
        private readonly List<int> _indices = new();
        private int _offset;

        /// <summary>Horizontal mirror (<c>u → 1-u</c>): draws the exact reflection (Redo, Refresh).</summary>
        public bool Mirror;

        public Pen(Rectangle rect, Color color, float strokeFraction)
        {
            _rect = rect;
            _color = color;
            _side = Math.Min(rect.Width, rect.Height);
            _thickness = MathF.Max(1f, _side * strokeFraction);
        }

        private Vector2 P(float u, float v)
        {
            if (Mirror) u = 1f - u;
            return new Vector2(_rect.Left + u * _rect.Width, _rect.Top + v * _rect.Height);
        }

        /// <summary>A thick line between two unit-box points.</summary>
        public void Line(float u1, float v1, float u2, float v2) =>
            LineMeshGenerator.AddLine(_vertices, _indices, P(u1, v1), P(u2, v2), _thickness, _color, ref _offset);

        /// <summary>A filled triangle from three unit-box points.</summary>
        public void Tri(float u1, float v1, float u2, float v2, float u3, float v3)
        {
            _vertices.Add(new VertexPositionColor(new Vector3(P(u1, v1), 0), _color));
            _vertices.Add(new VertexPositionColor(new Vector3(P(u2, v2), 0), _color));
            _vertices.Add(new VertexPositionColor(new Vector3(P(u3, v3), 0), _color));
            _indices.Add(_offset); _indices.Add(_offset + 1); _indices.Add(_offset + 2);
            _offset += 3;
        }

        /// <summary>An axis-aligned filled quad spanning the unit-box rectangle (float-precise, so it
        /// scales/mirrors exactly — unlike an int-rect generator).</summary>
        public void FillQuad(float u1, float v1, float u2, float v2)
        {
            _vertices.Add(new VertexPositionColor(new Vector3(P(u1, v1), 0), _color));
            _vertices.Add(new VertexPositionColor(new Vector3(P(u2, v1), 0), _color));
            _vertices.Add(new VertexPositionColor(new Vector3(P(u2, v2), 0), _color));
            _vertices.Add(new VertexPositionColor(new Vector3(P(u1, v2), 0), _color));
            _indices.Add(_offset); _indices.Add(_offset + 1); _indices.Add(_offset + 2);
            _indices.Add(_offset); _indices.Add(_offset + 2); _indices.Add(_offset + 3);
            _offset += 4;
        }

        /// <summary>An arrowhead (filled triangle) whose tip is at the unit point, pointing along the
        /// unit direction (<paramref name="dx"/>,<paramref name="dy"/>).</summary>
        public void Arrow(float tipU, float tipV, float dx, float dy, float size = 0.14f)
        {
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            dx /= len; dy /= len;
            var px = -dy; var py = dx;          // perpendicular
            var baseU = tipU - dx * size; var baseV = tipV - dy * size;
            var half = size * 0.6f;
            Tri(tipU, tipV,
                baseU + px * half, baseV + py * half,
                baseU - px * half, baseV - py * half);
        }

        /// <summary>An arc's thick polyline (unit-box center/radius, degrees).</summary>
        public void Arc(float cx, float cy, float r, float startDeg, float endDeg, int segments)
        {
            var prev = OnCircle(cx, cy, r, startDeg);
            for (var i = 1; i <= segments; i++)
            {
                var a = startDeg + (endDeg - startDeg) * i / segments;
                var next = OnCircle(cx, cy, r, a);
                Line(prev.u, prev.v, next.u, next.v);
                prev = next;
            }
        }

        /// <summary>An arrowhead at the arc endpoint <paramref name="deg"/>, pointing along the tangent
        /// (<paramref name="forward"/> = the direction of increasing angle).</summary>
        public void ArrowAtAngle(float cx, float cy, float r, float deg, bool forward, float size)
        {
            var rad = deg * MathF.PI / 180f;
            var end = OnCircle(cx, cy, r, deg);
            // Tangent to a v-down circle at increasing angle is (-sin, cos).
            var tx = -MathF.Sin(rad); var ty = MathF.Cos(rad);
            if (!forward) { tx = -tx; ty = -ty; }
            Arrow(end.u + tx * size * 0.5f, end.v + ty * size * 0.5f, tx, ty, size);
        }

        private static (float u, float v) OnCircle(float cx, float cy, float r, float deg)
        {
            var rad = deg * MathF.PI / 180f;
            return (cx + r * MathF.Cos(rad), cy + r * MathF.Sin(rad));
        }

        public MeshData ToMesh() => new(_vertices.ToArray(), _indices.ToArray());
    }
}
