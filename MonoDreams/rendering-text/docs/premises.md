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

## A face's glyph coverage is queryable

A `BitmapFont` renders only the characters its `.fnt` was exported with, and the bitmap draw path
renders anything else as **nothing at all** — no crash, no tofu box, no gap of the right width. That
character table is exposed through `MonoDreams.Text.GlyphCoverage`: `HasGlyph(font, c)`,
`Covers(font, text)`, `TryFindMissing(font, text, startIndex, …)` (allocation-free, the form the
per-frame path uses) and `MissingCodepoints(font, text)` (distinct, first-appearance order).
Queries walk full Unicode codepoints, so an astral character counts once rather than as two
surrogate halves, and they never report the characters the ENGINE lays out — `'\n'` and the `'\r'`
before it (`GlyphCoverage.IsLayoutCharacter`). All of it is pure and needs no `GraphicsDevice`, so
content checks and tests can ask the same question the renderer asks.

**Why:** partial coverage is the norm for bitmap faces (a pixel face with no diacritics, a mono face
with no em-dash, a caps-only face with no lowercase), and the resulting bug is invisible at dev
time: test strings happen to be covered, and real content — a name, a translation, an ellipsis —
silently loses letters ("São Paulo" → "So Paulo"). A question nobody can ask is a bug nobody can
find.
**Breaks:** reporting `'\n'` as a missing glyph makes every multi-line label warn forever (the
engine splits lines itself — see the multi-line premise). Walking `char` instead of codepoints
reports one astral character twice, as two undecodable halves.
**Tests:** `MonoDreams.Tests/Rendering/GlyphCoverageTests.cs`.
**Depends on:** this file — "Text uses `BitmapFont`, not `SpriteFont`"; "Multi-line text is laid out
by the engine, not the font backend".

## Folds are pure, deterministic and identity-preserving

The shipped fold building blocks in `MonoDreams.Text.TextFold` — `Dashes` (every Unicode dash → `-`),
`Ellipsis` (`…` → `...`), `Ordinals` (`º`/`ª` → `o`/`a`), `StripDiacritics` (Latin-1 Supplement +
Latin Extended-A → base letters) and `Upcase` (invariant) — are `string → string` functions that
**never look at a font**. Composing them is `TextFold.Chain(...)`, which copies its array. Two
properties are contractual: a fold produces the same output on every platform (the diacritic tables
are hardcoded rather than derived from `string.Normalize`, which needs ICU that a size-trimmed WASM
head may not ship), and a fold that changes nothing returns **the same string instance** it was
given. `StripDiacritics` folds accents only; letters that are not an accented ASCII letter (`Æ`, `Ð`,
`Þ`, `ß`, `Œ`, `Ŋ`) are left alone rather than transliterated.

**Why:** binding coverage into the fold would make the same string render differently per face and
per platform — impossible to diff, impossible to reason about; the face binding belongs in the
policy, not in the fold. And `TextPrepSystem` folds every text entity **every frame**, so a fold
that allocated unconditionally would churn one string per label per frame; scanning first and
returning the original keeps the steady state allocation-free.
**Breaks:** a fold that consults the font (or the current culture — `ToUpper()` in a Turkish locale
turns `i` into `İ` and loses the glyph) makes text non-reproducible. A fold that always allocates
adds per-frame garbage proportional to the number of labels on screen. Transliterating `ß` → `ss`
inside `StripDiacritics` would silently change string lengths and hide a genuinely missing glyph.
**Tests:** `MonoDreams.Tests/Rendering/TextFoldTests.cs`.
**Depends on:** —

## Per-face folds run before the reveal slice and before layout

`TextPrepSystem` holds a `TextFacePolicyRegistry` (`FacePolicies`, optional third constructor
argument) that maps a **face name** — `BitmapFont.Face`, not the font instance — to a
`TextFacePolicy` (a fold plus a `SilentDrop` flag). Per entity, per frame, the system folds the FULL
`TextContent` through that face's fold first, and only then applies the reveal slice, the
`MeasureString` and the write into `DrawComponent.Text` — so the typewriter, the measured size and
the drawn glyphs all describe the same string. The two ends of the reveal therefore measure
different strings — `TextUpdateSystem` advances `VisibleCharacterCount` against the RAW
`TextContent.Length` and **clamps it there** (latching `IsRevealed` at the cap), while prep slices
the FOLDED string — so the count is re-expressed in folded characters before the slice by
`TextPrepSystem.ScaleRevealCount`: proportional mid-reveal, **saturating to the whole folded string
once the raw reveal is finished** (count ≥ raw length — which is also exactly what
`DialogueSystem`'s skip-reveal assigns), and never zero once the reveal has started. The composed
decision is the pure static `TextPrepSystem.TryGetVisibleText(facePolicies, font, revealingSpeed,
visibleCharacterCount, textContent, out visibleText)`. Keying by face name (not instance) is what
lets a policy survive a content reload, since every screen loads its own `BitmapFont` object from
the same `.fnt`.

**Why:** folding after the slice would measure and draw a string the reveal never saw, and folding
after layout would measure glyphs that are not the ones rendered. Registering policies per face
rather than per entity means a game states its font's limits once, at boot, instead of at every
label. The count map exists because the raw count is CAPPED at the raw length: without it, a fold
that grows the string could never reach that string's end.
**Breaks:** slicing the folded string with the RAW count truncates a grown string **permanently**,
not early. `Ellipsis` — the one shipped fold that changes length, and it only ever grows — folds
`"carregando…"` (11) to `"carregando..."` (13); `TextUpdateSystem` stops the count at 11 and latches
`IsRevealed`, so the label renders `"carregando."` for the rest of its life. Mid-reveal the map is
still an approximation (a proportional one), so a typewriter over expanded content types its extra
characters a touch unevenly; a game that needs an exact typewriter over expanded content should
pre-fold its `TextContent`. For the same reason, game-side code that measures the RAW `TextContent`
itself —
`DialogueSystem.WrapText`, any hand-rolled column fitting — measures a string the renderer may not
draw, so with a length-changing fold its wrap points drift by a character; pre-fold before wrapping
when exact wrapping matters. Keying policies by `BitmapFont` instance instead of face name loses
them on the second screen that loads the same font.
**Tests:** `MonoDreams.Tests/Rendering/TextFacePolicyTests.cs`.
**Depends on:** this file — "The reveal gate is scoped to revealing text"; "Text pipeline order:
`TextUpdateSystem` → `TextPrepSystem` → `MasterRenderSystem`".

## A dropped glyph is reported once per face + character; silence is opt-in

When `TextPrepSystem` is about to hand the renderer a string the face cannot fully render, it calls
`TextFacePolicyRegistry.WarnOnMissingGlyphs`, which logs one `Logger.Warning` per **face +
codepoint** — naming the face, the character and its `U+XXXX` codepoint, and quoting the string it
first appeared in. Never once per frame: the registry remembers what it already said (`HasWarned`,
`ResetWarnings`), so a missing glyph costs one line per session, not sixty per second. Silence is an
**explicit opt-in**: `new TextFacePolicy(fold, silentDrop: true)` suppresses both the warning and
the per-frame coverage scan for that face, and is the way a game states "these drops are deliberate
and tested". A face with no registered policy is loud.

**Why:** the old behavior was silence by accident. A logged warning is also machine-checkable — an
agent running `GameTestRunner` can assert on it — whereas a missing pixel inside a word is not.
**Breaks:** warning per frame floods the log and the console sink (which the web head writes to
unconditionally) and makes the log unreadable at 60 lines/second per label. Making silence the
default restores the original invisible-corruption bug. Sharing one registry across screens is what
keeps "once" meaning once per session: a screen that constructs its own `TextPrepSystem` without
passing the shared registry gets its own warn-once ledger and repeats the line.
**Tests:** `MonoDreams.Tests/Rendering/TextFacePolicyTests.cs` (warn-once across 60 frames, per-face
keying, the `SilentDrop` opt-in, and the line that actually reaches the `Logger` sinks).
**Depends on:** foundation — the `Logger` contract (`[wallclock] [GT gametime] [LEVEL] message`).

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
