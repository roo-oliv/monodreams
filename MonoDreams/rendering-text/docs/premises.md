# rendering-text — premises

> Technical invariants the engine assumes about text rendering:
> `DynamicTextComponent`, `TextUpdateSystem`, and `TextPrepSystem`.
> Read this before changing any of those pieces or wiring text into a
> screen.

## Text uses `BitmapFont`, not `SpriteFont`

`DynamicTextComponent.Font` is a `MonoGame.Extended.BitmapFonts.BitmapFont`,
not a `Microsoft.Xna.Framework.Graphics.SpriteFont`. The block depends on
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

## Static labels need `IsRevealed = true` and `VisibleCharacterCount` saturated

For non-typewriter (static) labels, set `DynamicTextComponent.IsRevealed
= true` and `VisibleCharacterCount = int.MaxValue` (or the text length).
`TextUpdateSystem` early-exits when `IsRevealed` is true, but
`TextPrepSystem` slices the text by `VisibleCharacterCount`; a
default-constructed `DynamicTextComponent` has `VisibleCharacterCount =
0`, so the label renders empty even though `Font` and `TextContent`
are set.

**Why:** the same component models both static labels and animated
reveal text. The convention "static = already revealed, saturated
counter" lets one component handle both modes without a discriminator
field. `LevelSelectionScreen.cs` is the canonical pattern.
**Breaks:** the missed-`VisibleCharacterCount` bug — dev creates
`new DynamicTextComponent { Font = ..., TextContent = "Hello" }` and
stares at an invisible label. Symmetric with the missing-`Visible`
bug in `rendering`.
**Tests:** none yet.
**Depends on:** —

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

## Open questions

- **Per-glyph layout** — `TextPrepSystem` currently submits the whole
  visible substring as one draw call (one `DrawComponent.Text`). A
  per-glyph path would let per-character color, animation, or
  outline effects work, but isn't implemented today.
- **Word wrap** — there is no wrapping logic in this block. Text
  longer than its container clips at the right edge of the bitmap
  font's rendering. UI layouts that need wrapping have to
  pre-wrap the string and insert `\n` themselves.

## Aspirational direction

- Split the dynamic-reveal portion of `DynamicTextComponent` /
  `TextUpdateSystem` into a dedicated block, leaving static labels in
  this block alone. (A `text-dynamic-reveal` block was reserved for
  this earlier but removed — recreate when the split actually lands.)
- Per-glyph rendering with per-character color/animation hooks.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Text uses `BitmapFont`, not `SpriteFont`
- Static labels need `IsRevealed = true` and `VisibleCharacterCount` saturated
- Text pipeline order: `TextUpdateSystem` → `TextPrepSystem` → `MasterRenderSystem`
- `TextPrepSystem` writes the world-transformed position
