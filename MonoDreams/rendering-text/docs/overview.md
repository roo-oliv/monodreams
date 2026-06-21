# rendering-text — overview

Bitmap-font text rendering with built-in support for both static labels and animated typewriter reveal. One component (`DynamicTextComponent`), two systems (`TextUpdateSystem` for animation, `TextPrepSystem` for layout), one render path through the standard `DrawComponent` pipeline. Install for any game that displays text — menus, HUDs, dialogue, in-world labels.

## Purpose

Text rendering in MonoGame normally means picking between `SpriteFont` (no anti-aliasing control, baked sizes) or hand-rolling a bitmap-font reader. This module standardizes on MonoGame.Extended's `BitmapFont` and wraps it in the ECS pipeline so text entities behave like any other renderable — they have a `TransformComponent`, parent into hierarchies, get culled by `CullingSystem`, and submit through `MasterRenderSystem`. The single `DynamicTextComponent` models both static labels (fixed text that always shows) and animated reveal text (typewriter effect with per-second character speed) via the `IsRevealed` / `VisibleCharacterCount` fields — the cost of one extra field is paid to avoid two parallel text component types.

## What ships

### Components

- `DynamicTextComponent` — `Font` (BitmapFont), `TextContent`, `Color`, `Scale`, `LayerDepth`, plus reveal-animation fields: `IsRevealed`, `RevealingSpeed`, `RevealStartTime`, `VisibleCharacterCount`

### Systems

- `TextUpdateSystem` — per frame: advances `VisibleCharacterCount` for animating entries and flips `IsRevealed = true` when complete. Early-exits on already-revealed entries
- `TextPrepSystem` — runs after `TextUpdateSystem`: reads `TransformComponent.WorldPosition` + the visible substring and writes them into `DrawComponent` for `MasterRenderSystem` to draw

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

## Cross-module dependencies

- `rendering` — text draws use `DrawComponent`, ride `MasterRenderSystem`, and respect render targets.

## Extension points

- **Custom fonts.** Any `MonoGame.Extended.BitmapFonts.BitmapFont` works. Load via the content pipeline like any other content.
- **Per-character effects (future).** `TextPrepSystem` currently submits the whole visible substring as one draw call. Per-glyph rendering (per-character color, animation, outlines) is on the aspirational direction list — see premises.
- **Word wrap (game-side).** This module has no wrap logic. Pre-wrap your strings with `\n` before assigning to `TextContent`, or build wrap support as a game-side pre-prep system.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (BitmapFont vs SpriteFont, static-label saturation requirement, update→prep order)
- Related modules: `rendering` (the renderer; provides `DrawComponent` and the prep-system base class), `dialogue` (uses `DynamicTextComponent` for its dialogue-line reveal)
