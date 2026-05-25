# text-dynamic-reveal — premises

> Technical invariants the engine assumes about the dynamic-reveal
> (typewriter) text block. **Today this block is a placeholder** — its
> source directory ships with no files; the reveal logic
> (`DynamicTextComponent` + `TextUpdateSystem`) currently lives inside
> `rendering-text`. The block is reserved so that a future refactor can
> split static and dynamic-reveal paths without renaming the public
> surface.

## This block is reserved; its implementation ships in `rendering-text` today

The dynamic-reveal functionality — the `IsRevealed` flag,
`RevealingSpeed`, `RevealStartTime`, and `VisibleCharacterCount` fields
on `DynamicTextComponent`, plus `TextUpdateSystem` that advances them —
all currently live in `rendering-text`. Installing this block on top
of `rendering-text` is a no-op today; the manifest declares the
dependency so a future split can swap the dependency direction without
breaking consumers.

**Why:** the eventual split lets a game pull static-label support
without the reveal-animation cost (the `Update` system, the
`RevealStartTime` math). Reserving the block name now means future
consumers can already declare `dependencies: ["text-dynamic-reveal"]`
in their block manifests and the dependency resolver won't be
confused when the split happens.
**Breaks:** if a game writes code that imports from a
`MonoDreams.TextDynamicReveal` namespace expecting it to exist today,
the build fails — the namespace is not yet populated. Consumers
should depend on the block (so package metadata reflects intent) but
import from `MonoDreams.Component.Draw` and `MonoDreams.System.Draw`
(where the types actually live).
**Tests:** none yet.
**Depends on:** rendering-text — "Text uses `BitmapFont`, not
`SpriteFont`"; rendering-text — "Static labels need `IsRevealed = true`
and `VisibleCharacterCount` saturated".

## Open questions

- **When does the split happen?** The intended trigger is the first
  consumer that wants static text without the reveal animation cost
  (e.g. a localization tool that just measures strings). Until then
  the split is pure refactor with no observable behaviour change.
- **Field ownership after the split** — will the dynamic-reveal
  fields (`RevealingSpeed`, `RevealStartTime`, `IsRevealed`,
  `VisibleCharacterCount`) leave `DynamicTextComponent` and live on a
  separate `RevealComponent`? Or will `DynamicTextComponent` itself
  move into this block and `rendering-text` ship a new
  `StaticTextComponent`? Both options are open; whichever lands, the
  block name in the manifest should not need to change.

## Aspirational direction

- Split `DynamicTextComponent` such that the static-text fields
  (`TextContent`, `Font`, `Color`, `Scale`, `LayerDepth`, `Target`)
  live in `rendering-text` and the reveal-animation fields live in
  this block.
- Move `TextUpdateSystem` into this block, leaving `TextPrepSystem`
  in `rendering-text`.
- Cross-block premise: once split, `rendering-text`'s "static labels
  need `IsRevealed = true`" premise becomes obsolete; static labels
  would render directly from the static component without consulting
  a reveal flag.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- This block is reserved; its implementation ships in `rendering-text` today
