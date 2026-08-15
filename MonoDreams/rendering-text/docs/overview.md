# rendering-text — overview

Bitmap-font text rendering with built-in support for both static labels and animated typewriter reveal. One component (`DynamicTextComponent`), two systems (`TextUpdateSystem` for animation, `TextPrepSystem` for layout), one render path through the standard `DrawComponent` pipeline. Install for any game that displays text — menus, HUDs, dialogue, in-world labels.

## Purpose

Text rendering in MonoGame normally means picking between `SpriteFont` (no anti-aliasing control, baked sizes) or hand-rolling a bitmap-font reader. This module standardizes on MonoGame.Extended's `BitmapFont` and wraps it in the ECS pipeline so text entities behave like any other renderable — they have a `TransformComponent`, parent into hierarchies, get culled by `CullingSystem`, and submit through `MasterRenderSystem`. The single `DynamicTextComponent` models both static labels (fixed text that always shows) and animated reveal text (typewriter effect with per-second character speed) via the `IsRevealed` / `VisibleCharacterCount` fields — the cost of one extra field is paid to avoid two parallel text component types.

## What ships

### Components

- `DynamicTextComponent` — `Font` (BitmapFont), `TextContent`, `Color`, `Scale`, `LayerDepth`, plus reveal-animation fields: `IsRevealed`, `RevealingSpeed`, `RevealStartTime`, `VisibleCharacterCount`

### Systems

- `TextUpdateSystem` — per frame: advances `VisibleCharacterCount` for animating entries and flips `IsRevealed = true` when complete. Early-exits on already-revealed entries
- `TextPrepSystem` — runs after `TextUpdateSystem`: folds the content for its face, reads `TransformComponent.WorldPosition` + the visible substring and writes them into `DrawComponent` for `MasterRenderSystem` to draw. Warns (once per face + character) about any glyph the face is about to drop

### Glyph coverage and folding (`MonoDreams.Text`)

- `GlyphCoverage` — pure queries over a face's parsed `.fnt` character table: `HasGlyph`, `Covers`, `TryFindMissing`, `MissingCodepoints`, `Describe`
- `TextFold` — shipped, deterministic fold building blocks: `Dashes`, `Ellipsis`, `Ordinals`, `StripDiacritics`, `Upcase`, composed with `Chain`
- `TextFacePolicy` — one face's fold + its `SilentDrop` opt-in
- `TextFacePolicyRegistry` — face name → policy, plus the warn-once ledger `TextPrepSystem` uses

## Pipeline wiring

Text rides the standard render pipeline:

1. **`TextUpdateSystem`** — animation phase (logic stage); advances reveal state per frame.
2. **`TextPrepSystem`** — render-prep phase (after `HierarchySystem`, before `MasterRenderSystem`); writes glyph state into `DrawComponent`.
3. **`MasterRenderSystem`** from `rendering` draws the text via the bitmap-font path.

**Static labels** still need the revealed fields set explicitly:
```csharp
entity.Set(new DynamicTextComponent {
    Font = font,
    TextContent = "Level 1",
    IsRevealed = true,
    VisibleCharacterCount = int.MaxValue,  // saturate so prep doesn't slice it short
    // ...
});
```
Default-constructed `DynamicTextComponent` has `VisibleCharacterCount = 0`, which renders an empty label. `LevelSelectionScreen.cs` in `MonoDreams.Examples` is the canonical pattern.

**Animated reveal:** set `IsRevealed = false`, `RevealingSpeed = 30f` (chars/sec), `RevealStartTime = gameState.Total`. `TextUpdateSystem` does the rest.

## Partial faces: coverage, folds, and the missing-glyph warning

A bitmap face only carries the glyphs its `.fnt` was exported with, and the draw path renders anything else as **nothing** — "São Paulo" ships as "So Paulo", "prazo — hoje" as "prazo  hoje". The module makes that failure visible and fixable in three steps.

**1. Ask what a face covers.** `GlyphCoverage` reads the character table the engine already parsed:

```csharp
GlyphCoverage.HasGlyph(font, 'ã');                  // false on an ASCII-only face
GlyphCoverage.Covers(font, "São Paulo");            // false — this string would lose a letter
GlyphCoverage.MissingCodepoints(font, copy);        // distinct, in first-appearance order
```

**2. Fold content into what the face can render.** Compose the shipped blocks once per face and register them; `TextPrepSystem` applies the fold before layout, so nothing at the call sites changes:

```csharp
var faces = new TextFacePolicyRegistry()
    .Register(displayFont, new TextFacePolicy(
        TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals)))
    .Register(monoCapsFont, new TextFacePolicy(
        TextFold.Chain(TextFold.Dashes, TextFold.Ellipsis, TextFold.Ordinals,
                       TextFold.StripDiacritics, TextFold.Upcase),
        silentDrop: true)); // this face's drops are deliberate and tested

g.Add("textPrep", new TextPrepSystem(_world, pixelPerfectRendering, faces));
```

Pass the **same** registry to every screen's `TextPrepSystem`: it carries the warn-once ledger, so sharing it is what makes "once per face + character" mean once per session.

**3. Hear about what is left.** With no registry at all the system still warns — loudly, once per face + character, never per frame:

```
[ WARN] [rendering-text] face 'PressStart2P' has no glyph for 'ã' (U+00E3) — it is DROPPED from the
rendered text (first seen in "São Paulo"). Fold it for this face (TextFold + TextFacePolicy) or opt
into TextFacePolicy.SilentDrop.
```

Silent dropping is still available — it is just an explicit, per-face decision now (`silentDrop: true`), which also skips the per-frame coverage scan for that face.

## Cross-module dependencies

- `rendering` — text draws use `DrawComponent`, ride `MasterRenderSystem`, and respect render targets.

## Extension points

- **Custom fonts.** Any `MonoGame.Extended.BitmapFonts.BitmapFont` works. Load via the content pipeline like any other content.
- **Custom folds.** A fold is any `Func<string, string>`, so a game can add its own (currency symbols, a per-language substitution table) and `TextFold.Chain` it together with the shipped blocks. Keep it pure, face-independent, and identity-preserving on the unchanged path — `TextPrepSystem` calls it every frame (see premises).
- **Per-character effects (future).** `TextPrepSystem` currently submits the whole visible substring as one draw call. Per-glyph rendering (per-character color, animation, outlines) is on the aspirational direction list — see premises.
- **Word wrap (game-side).** This module has no wrap logic. Pre-wrap your strings with `\n` before assigning to `TextContent`, or build wrap support as a game-side pre-prep system.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (BitmapFont vs SpriteFont, the reveal gate, update→prep order, glyph coverage / folds / the missing-glyph warning)
- Related modules: `rendering` (the renderer; provides `DrawComponent` and the prep-system base class), `dialogue` (uses `DynamicTextComponent` for its dialogue-line reveal)
