# text-dynamic-reveal — overview

A reserved block name for the eventual split of typewriter/reveal text from static label rendering. **Today this block ships no source files of its own** — the reveal logic (`DynamicTextComponent`'s reveal fields + `TextUpdateSystem`) currently lives inside `rendering-text`. The block exists so manifests can already declare `dependencies: ["text-dynamic-reveal"]` ahead of the planned refactor.

## Purpose

The intent behind reserving this block is to let games eventually pull static-label support without paying for the animation cost (`TextUpdateSystem`'s per-frame work, the `RevealStartTime` math, the extra component fields). The split hasn't happened yet because no consumer has needed static text without reveal — but reserving the block name now means future package metadata can already reflect intent, and the dependency resolver won't be retrofitted when the split lands.

Installing this block today is a no-op beyond pulling in `rendering-text`.

## What ships

Nothing. The block has no source files; its `block.json` declares the dependency on `rendering-text` and acts as a placeholder.

The reveal functionality currently provided by `rendering-text`:
- `DynamicTextComponent.IsRevealed`, `RevealingSpeed`, `RevealStartTime`, `VisibleCharacterCount`
- `TextUpdateSystem`

When the split happens, those fields/types will migrate into this block; `rendering-text` will retain only static-label support.

## Pipeline wiring

Today: install `rendering-text` and follow its wiring. This block adds nothing.

Typical reveal usage (lives in `rendering-text` today):
```csharp
entity.Set(new DynamicTextComponent {
    TextContent = "Hello world",
    RevealingSpeed = 30f,        // characters per second
    RevealStartTime = gameState.Total,
    IsRevealed = false,
    VisibleCharacterCount = 0,
    // ... font, color, scale, etc.
});
```
`TextUpdateSystem` (in `rendering-text`) advances `VisibleCharacterCount` based on `(GameState.Total - RevealStartTime) * RevealingSpeed` and flips `IsRevealed = true` when the count reaches the string length.

## Cross-block dependencies

- `rendering-text` — every line of code that today implements the reveal effect lives there.

## Extension points

None today. After the split, the natural extension would be alternative reveal-animation curves (ease-in, accelerating, per-glyph stagger) and per-character effects (color flicker on reveal, position jitter) registered against the same component.

## See also

- [Premises](premises.md) — explains the reserved state and what will move when the split happens
- Related blocks: `rendering-text` (where the reveal code lives today), `dialogue` (the canonical consumer of the reveal effect — dialogue lines reveal character-by-character)
