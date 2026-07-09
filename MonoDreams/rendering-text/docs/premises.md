# rendering-text — premises

> Technical invariants the engine assumes about text rendering:
> `DynamicTextComponent`, `TextUpdateSystem`, and `TextPrepSystem`.
> Read this before changing any of those pieces or wiring text into a
> screen.

## Text uses `BitmapFont`, not `SpriteFont`

`DynamicTextComponent.Font` is a `MonoGame.Extended.BitmapFonts.BitmapFont`,
not a `Microsoft.Xna.Framework.Graphics.SpriteFont`. The module depends on
MonoGame.Extended for its bitmap-font implementation
(angle-bracket-fnt or BMFont), and `TextPrepSystem` calls
`font.MeasureString` and submits glyphs via the bitmap-font draw path.

**Why:** bitmap fonts give the framework control over the glyph atlas
and let it render at fractional sizes via `Scale` without the
SpriteFont rasterization artifacts. The MonoGame.Extended
implementation also exposes the measure / draw seam the text pipeline
needs.
**Breaks:** passing a `SpriteFont` (e.g. `content.Load<SpriteFont>(...)`)
fails to compile at the assignment to `Font`. A future change that
loosens the type to a common interface would need to update both prep
and measure paths.
**Tests:** none yet.
**Depends on:** —

## The reveal gate is scoped to revealing text

`TextPrepSystem` slices `TextContent` by `VisibleCharacterCount` **only when a reveal is
configured** (`DynamicTextComponent.RevealingSpeed > 0` — the typewriter animation
`TextUpdateSystem` advances). **Static (non-revealing, `RevealingSpeed ≤ 0`) text renders its
FULL `TextContent` regardless of the count**; only genuinely empty/null content renders nothing.
A revealing text whose reveal has not started (count ≤ 0) still renders nothing. The decision is
the pure, font-free `TextPrepSystem.TryGetVisibleText(revealingSpeed, visibleCharacterCount,
textContent, out visibleText)`. A static label therefore no longer needs `IsRevealed = true` or a
saturated `VisibleCharacterCount` to be visible — a bare
`new DynamicTextComponent { Font = …, TextContent = "Hello" }` renders in full (setting
`IsRevealed = true` merely short-circuits `TextUpdateSystem`'s per-frame work).

**Why:** the healer `TextUpdateSystem` (which saturates the count to the full length for
non-revealing text) is woven in the game-logic group, which is **`Freeze`-gated in `Edit`** — so
in the level editor a pooled/reassigned chrome label (panel rows, inspector rows, tooltips) kept a
STALE `VisibleCharacterCount` and rendered truncated ("Dialogu", "Enti") or, when created empty,
blank; entering Play un-froze the healer and healed the counts, so the bug was intermittent.
Scoping the gate to revealing text makes a static label independent of the count, so it renders
correctly whether or not the healer ran — the fix is at the framework level, not per-label. The
same component still models both static labels and animated reveal text without a discriminator
field; `RevealingSpeed` IS the discriminator.
**Breaks:** re-applying the count gate to non-revealing text reintroduces the blank/truncated
editor-chrome bug (UX2-G). Conversely, dropping the count gate for revealing text
(`RevealingSpeed > 0`) makes a dialogue line appear all-at-once instead of typing in. A site that
hid a NON-revealing label by setting `VisibleCharacterCount = 0` while keeping `TextContent` no
longer hides it — hide such a label by clearing `TextContent` or parking it off-screen instead
(`PalettePlacementSystem` parks off-screen; the old count-0 hides were removed with this fix).
**Tests:** `MonoDreams.Tests/Rendering/TextPrepSystemTests.cs` (static stale-low-count renders full;
static count-0 non-empty renders full — the blank-tooltip case; revealing mid-reveal respects the
count; revealing not-started renders nothing; count-beyond-length clamps; empty/null renders
nothing).
**Depends on:** foundation — the run-state model (`TextUpdateSystem` is `Freeze`-gated in `Edit`);
this file — "Text pipeline order: `TextUpdateSystem` → `TextPrepSystem` → `MasterRenderSystem`".

## Text pipeline order: `TextUpdateSystem` → `TextPrepSystem` → `MasterRenderSystem`

`TextUpdateSystem` advances `VisibleCharacterCount` based on
`(GameState.TotalTime - RevealStartTime) * RevealingSpeed` and flips
`IsRevealed = true` when complete. `TextPrepSystem` then slices the
text and writes the visible substring into `DrawComponent.Text`.
`MasterRenderSystem` submits the substring to the bitmap-font draw
path. Each stage depends on the previous one's output for the same
frame.

**Why:** the split lets text *animation* (update) run in the logic
phase, where game systems may also react to "this line just
finished revealing" (e.g. `DialogueSystem.UpdateLinePhase`), and lets
text *rendering* (prep) run in the draw phase where positions and
transforms are final.
**Breaks:** running `TextPrepSystem` before `TextUpdateSystem` uses
last frame's `VisibleCharacterCount` — the reveal animation lags by
one frame. Skipping `TextUpdateSystem` entirely means the count
never advances and animated reveal text stays at zero characters.
Note that this pipeline dependency is now scoped to **revealing** text:
non-revealing (static) text does not depend on `TextUpdateSystem` at
all — `TextPrepSystem` renders its full content regardless of the count
(see "The reveal gate is scoped to revealing text"), so freezing the
healer in the editor affects only animated reveal text.
**Tests:** none yet.
**Depends on:** rendering — "Rendering systems run last in the pipeline".

## `TextPrepSystem` writes the world-transformed position

`TextPrepSystem` reads `TransformComponent.WorldPosition` /
`WorldRotation` / `WorldScale` and writes them into `DrawComponent`
(combined with `DynamicTextComponent.Scale`). Pixel-perfect rendering
rounds the position to integers (controlled by the
`pixelPerfectRendering` constructor flag).

**Why:** text rendered at fractional pixel coordinates produces
sub-pixel blur on bitmap fonts. The pixel-perfect toggle lets the
game pick between sharp text (rounded position) and smooth motion
(fractional position). Reading the *world* transform means parented
text (e.g. dialogue text under a dialogue box) tracks its parent
without extra wiring.
**Breaks:** mutating `TransformComponent.Position` after
`TextPrepSystem` ran (or before `HierarchySystem` propagated dirty
flags) makes the text render at a stale position for the frame.
**Tests:** none yet.
**Depends on:** foundation — "`HierarchySystem` must run ahead of any
system reading WorldPosition".

## Multi-line text is laid out by the engine, not the font backend; `LineSpacing` sets leading

`MasterRenderSystem` renders `'\n'`-separated text as one `DrawString` per line,
advancing the baseline by `Font.LineHeight * Scale.Y * LineSpacing`. It does **not** pass
the whole multi-line string to the bitmap font's own newline handling.
`DynamicTextComponent.LineSpacing` (a multiplier, default-treated as
`DynamicTextComponent.DefaultLineSpacing` — currently **1.15** — when `≤ 0`, carried onto
`DrawComponent.LineSpacing` by `TextPrepSystem`) configures the leading; the reveal animation
slices the wrapped string transparently, so embedded `\n` just advance instantly. The single
`DefaultLineSpacing` constant is the one place the engine-wide default lives — both
`TextPrepSystem` and `MasterRenderSystem` resolve `≤ 0` to it.

**Why:** MonoGame.Extended's `BitmapFont.DrawString` advances newlines by the font's raw
`LineHeight` and ignores the per-draw `scale` — so scaled-down multi-line text collided
(lines drew on top of each other). Laying lines out ourselves makes leading scale-correct
and configurable, and a default of 1.15 gives wrapped dialogue/labels comfortable breathing
room. Any code that stacks text by hand (e.g. `DialogueSystem.ShowOptions`) must multiply its
line height by `DefaultLineSpacing` so its stacking advance equals the per-line render advance
(`Font.LineHeight * scale * leading`).
**Breaks:** bypassing the per-line loop (reverting to a single `DrawString` of the whole
string) reintroduces the overlap at non-unity scale. Setting `LineSpacing` on the text but
not mirroring it in any hand-rolled vertical stacking (e.g. dialogue options) — including the
implicit default — desynchronises the two (options overlap or gap). Rotation is applied per
line in unrotated offset space — fine for axis-aligned text, approximate for rotated
multi-line text (not used today).
**Tests:** none yet.
**Depends on:** rendering — "Three render targets, two behaviors".

## `DynamicTextComponent.Underline` strokes a per-line underline in the text's own color

`DynamicTextComponent.Underline` (default `false`, a no-op) is carried onto
`DrawComponent.Underline` by `TextPrepSystem`. When true, `MasterRenderSystem`'s text
branch draws, under each rendered line, a thin filled bar (a 1×1 white pixel scaled to the
line width × a small thickness) tinted with the text's `Color`, positioned at the line's
bottom (`Position.Y + Font.LineHeight * Scale.Y - thickness`) and spanning the line's
rendered width (`Font.MeasureString(line).Width * Scale.X`). It scales with the text and is
drawn in the same depth-sorted SpriteBatch pass as the glyphs, so it shares their layer
depth. The text `Color` is opaque, satisfying the opaque-fill convention.

**Why:** an underline is the canonical "this is a link" affordance, and deriving it from
the same per-line layout the renderer already computes (width via `MeasureString`, bottom
via the scaled line height) keeps it consistent with the multi-line leading rules without a
separate entity or mesh. Defaulting to `false` keeps every existing label byte-for-byte
unchanged.
**Breaks:** a future per-glyph text path (see Open questions) would need to recompute the
underline span per glyph run rather than per line. Tinting the underline with a partial-
alpha color is not the issue here (text uses straight-alpha `AlphaBlend`, not the mesh
path's premultiplied alpha), but keeping it the text color is what makes it read as part
of the text.
**Tests:** none yet (exercised by the `ui` demo's Link button label).
**Depends on:** "Multi-line text is laid out by the engine, not the font backend;
`LineSpacing` sets leading".

## Open questions

- **Per-glyph layout** — `TextPrepSystem` currently submits the whole
  visible substring as one draw call (one `DrawComponent.Text`). A
  per-glyph path would let per-character color, animation, or
  outline effects work, but isn't implemented today.
- **Word wrap** — there is no wrapping logic in this module. Text
  longer than its container clips at the right edge of the bitmap
  font's rendering. UI layouts that need wrapping have to
  pre-wrap the string and insert `\n` themselves (e.g. `DialogueSystem.WrapText`);
  the engine then lays the resulting lines out with correct leading (see the
  multi-line premise above).

## Aspirational direction

- Split the dynamic-reveal portion of `DynamicTextComponent` /
  `TextUpdateSystem` into a dedicated module, leaving static labels in
  this module alone. (A `text-dynamic-reveal` module was reserved for
  this earlier but removed — recreate when the split actually lands.)
- Per-glyph rendering with per-character color/animation hooks.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Text uses `BitmapFont`, not `SpriteFont`
- Text pipeline order: `TextUpdateSystem` → `TextPrepSystem` → `MasterRenderSystem`
- `TextPrepSystem` writes the world-transformed position
- Multi-line text is laid out by the engine, not the font backend; `LineSpacing` sets leading
