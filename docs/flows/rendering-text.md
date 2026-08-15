---
flow: rendering-text
covers:
  - MonoDreams/rendering-text/**
sensitive: false
---

# Text reveal & layout

Text is a renderable like any other: a `DynamicTextComponent` plus a `TransformComponent`,
fed through the unified `DrawComponent` stack. Each frame the same string travels three
stages, each consuming the previous stage's output. `TextUpdateSystem` (logic phase) advances
`VisibleCharacterCount` for animating ("typewriter") entries — `floor((TotalTime -
RevealStartTime) * RevealingSpeed)`, clamped to the content length — and flips `IsRevealed =
true` once the whole string is shown; revealed or instant-reveal entries early-exit with the
counter saturated to the full length. `TextPrepSystem` (render-prep phase, after
`HierarchySystem`) then *folds* the content through the face's `TextFacePolicy` (a per-face
`string → string` transform registered on the `TextFacePolicyRegistry` it was constructed with),
*slices* `foldedContent[..VisibleCharacterCount]`, reads the entity's
**world** transform, and writes a single `DrawComponent` of `Type = Text` carrying that visible
substring, the `BitmapFont`, world position/rotation, the combined scale, the underline flag,
and the resolved `LineSpacing`. Finally `MasterRenderSystem` (in `rendering`) renders that text
element on its `Target`: it splits the substring on `'\n'` and emits one `DrawString` per line,
advancing the baseline by `Font.LineHeight * Scale.Y * LineSpacing` — the engine lays out
multi-line leading itself, it does not hand the whole string to the font backend's newline
advance. The split between *update* (animation, where game systems can react to "this line just
finished") and *prep* (layout, where transforms are final) is the load-bearing seam.

## Entities & lifecycle

A text entity carries `DynamicTextComponent` + `TransformComponent`; `TextPrepSystem` adds/owns
its `DrawComponent`. Two reveal modes, one component:

1. **Static label** — created with `IsRevealed = true` and `VisibleCharacterCount` saturated
   (`int.MaxValue` or the text length). `TextUpdateSystem` re-asserts the saturated count and
   early-exits; the text always shows in full. `LevelSelectionScreen.cs` is the canonical pattern.
2. **Animated reveal** — created with `IsRevealed = false`, a positive `RevealingSpeed`, and
   `RevealStartTime` either set to "now" or left `float.NaN` (the system lazy-initializes it to
   the current `TotalTime` on first update). The counter climbs each frame until it reaches the
   length, then `IsRevealed` latches true.

Created by two paths: directly on a screen (static labels), and by `dialogue` —
`DialogueSystem.ShowLine` sets a fresh `DynamicTextComponent` (reveal mode, `RevealStartTime =
NaN`), then drives the *same* fields: `UpdateLinePhase` can force-complete a reveal on the
interact press by writing `VisibleCharacterCount = TextContent.Length; IsRevealed = true`. The
un-enumerated writer is the classic bug source — any external mutation of these fields competes
with `TextUpdateSystem` within the frame.

## Invariants

Authoritative list in [`MonoDreams/rendering-text/docs/premises.md`](../../MonoDreams/rendering-text/docs/premises.md);
the ones this flow's ordering leans on:

- Stage order `TextUpdateSystem → TextPrepSystem → MasterRenderSystem` holds within a frame —
  prep reads the count update wrote; render reads the substring prep wrote.
- `TextPrepSystem` reads `TransformComponent.WorldPosition` (not local), so `HierarchySystem`
  must have propagated parented text's transform before prep runs.
- Static labels must set `IsRevealed = true` **and** saturate `VisibleCharacterCount`; a
  default-constructed component (`VisibleCharacterCount = 0`) renders empty, even with `Font`/`TextContent` set.
- `Font` is a MonoGame.Extended `BitmapFont`, not a `SpriteFont`; prep measures and slices through it.
- Multi-line leading is the engine's job, applied identically in prep (carries `LineSpacing` onto
  the `DrawComponent`, `≤ 0` → `DefaultLineSpacing`) and in `MasterRenderSystem` (per-line advance).
- The per-face fold runs **before** the reveal slice and before `MeasureString`, so the typewriter,
  the measured size and the drawn glyphs all describe the same string; policies are keyed by
  `BitmapFont.Face` (a name, not an instance) so they survive a per-screen content reload.
- `VisibleCharacterCount` is produced in RAW characters (and clamped at the raw length) but spent on
  the FOLDED string, so prep re-expresses it with `TextPrepSystem.ScaleRevealCount` — otherwise a
  length-growing fold (`…` → `...`) leaves the tail permanently unrevealed.
- Whatever the fold left uncovered is warned about once per **face + character** — never per frame —
  unless that face's policy opted into `SilentDrop`.

## Load-bearing quantities

- `VisibleCharacterCount` — count of leading characters shown, `int`, clamped to
  `[0, TextContent.Length]`. The slice index; `0` renders nothing, `>= Length` is fully revealed.
- `RevealingSpeed` — characters per second; `≤ 0` means *instant* (saturate + latch), not "stop".
- `(TotalTime - RevealStartTime) * RevealingSpeed` — elapsed seconds × chars/s, floored to the
  target count. `RevealStartTime` is in `GameState.TotalTime` seconds; `NaN` triggers lazy init.
- `Scale` (effective) — `Transform.WorldScale * (DynamicText.Scale > 0 ? Scale : 0.5f)`; a zero
  `Scale` silently falls back to `0.5`, it does not vanish.
- `LineSpacing` — multiplier on `Font.LineHeight`; `≤ 0` resolves to `DefaultLineSpacing` (1.15).
  The per-line baseline advance is `Font.LineHeight * Scale.Y * LineSpacing`.

## Failure modes

- **Invisible static label** — `IsRevealed`/`VisibleCharacterCount` left at struct defaults;
  prep slices to an empty substring and renders nothing. Highest-frequency real bug; symmetric
  with the missing-`Visible` bug in `rendering`.
- **One-frame-late reveal** — `TextPrepSystem` ordered before `TextUpdateSystem`; prep slices on
  last frame's count and the typewriter lags a frame. Survives casual testing.
- **Stale text position** — mutating `Transform.Position` after prep ran, or before
  `HierarchySystem` propagated world transforms, draws the text at last frame's place.
- **Multi-line overlap at non-unity scale** — reverting the per-line loop to one `DrawString`
  of the whole string, or setting `LineSpacing` in prep but not mirroring it in hand-rolled
  vertical stacking (e.g. dialogue options), desyncs the advance and lines collide or gap.
- **Wrong font type** — assigning a `SpriteFont` to `Font` fails to compile; loosening the type
  without updating both the measure and the draw seams breaks layout silently.
- **Word that loses a letter** — the face has no glyph for a character ("São Paulo" → "So Paulo").
  The bitmap draw path skips it: no crash, no tofu box, correct-looking layout. Now audible as a
  one-per-face+character `Logger.Warning`; fixable with a fold; suppressible only by an explicit
  `SilentDrop` policy. A per-frame warning (dropping the warn-once ledger, or building a fresh
  registry per screen instead of sharing one) floods the log and the web head's console sink.
