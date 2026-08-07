# Recipe — a `.ttf` into a font the engine can render

> Rasterise a TrueType face into the (`.png` + BMFont XML `.fnt`) pair the
> content pipeline already builds, so a font drop needs no new importer and no
> engine change.

The engine's text path is MonoGame.Extended's `BitmapFont`. There is no
documented route from a `.ttf` to something `DynamicTextComponent.Font` will
accept, and the obvious-looking route — MGCB's `.spritefont` — is a dead end.
This recipe is the conversion, done once at asset-drop time and committed
alongside the art.

## Why `.spritefont` is not an option

`DynamicTextComponent.Font` is a `MonoGame.Extended.BitmapFonts.BitmapFont`,
and `TextPrepSystem` measures and submits glyphs through that path
(`rendering-text` premise
["Text uses `BitmapFont`, not `SpriteFont`"](../../MonoDreams/rendering-text/docs/premises.md)).
MGCB's `.spritefont` route produces a `Microsoft.Xna.Framework.Graphics.SpriteFont`
instead — a type **nothing in the render path accepts**. The failure is a
compile error at the assignment to `Font`, and there is no cast that fixes it:
the two types share no interface and the renderer's measure/draw calls only
exist on one of them.

The importer on the other side (`BitmapFontImporter`) reads **BMFont
descriptors**, not TrueType outlines. So something has to rasterise the face.
Doing it up front — a generated `.png` + `.fnt` pair, committed — rather than at
build time is both the smaller change and the portable one: the content pipeline
stays as shipped, and the same pair is what the web head builds.

## What the recipe produces

| File | Role | MGCB entry | Content key |
|---|---|---|---|
| `<name>.png` | the texture page: every glyph shelf-packed, white on transparent | `/importer:TextureImporter` + `/processor:TextureProcessor` | `Fonts/<name>` |
| `<name>-fnt.fnt` | BMFont XML descriptor: `<info>` / `<common>` / `<pages>` / `<chars>` | `/importer:BitmapFontImporter` + `/processor:BitmapFontProcessor` | `Fonts/<name>-fnt` |

Both are ordinary content entries; both are built. The descriptor's
`<pages><page id="0" file="<name>.png"/></pages>` names the page beside it, so
the two files must stay in the same content folder and keep the names the
generator wrote. Loading is by the **descriptor's** key:

```csharp
var font = content.Load<BitmapFont>("Fonts/PixelOperator8-fnt");
```

`MonoDreams.Examples.Core/Content/Fonts/` and
`MonoDreams.Examples.Core/Content/Content.mgcb` are the working examples — every
shipped font in this repo is such a pair.

## The `-fnt` suffix is load-bearing, not cosmetic

**MGCB content keys drop the extension.** `Fonts/Kaph-Regular.png` and a
sibling `Fonts/Kaph-Regular.fnt` would both compile to the key
`Fonts/Kaph-Regular` — two assets, one key, one of them silently clobbering the
other in the build output. Naming the descriptor `<name>-fnt.fnt` keeps the keys
distinct (`Fonts/Kaph-Regular` for the page, `Fonts/Kaph-Regular-fnt` for the
font) and is why every font in the repo carries the suffix. Bake it into the
generator's output naming; do not leave it to whoever adds the `.mgcb` entry.

## Rasterise at the face's design size

A pixel face is designed on a grid — 8 px for `PixelOperator8`, 16 for a 16 px
face. Rasterise at exactly that size and scale by **whole numbers** in game
(`DynamicTextComponent.Scale = 2f`, `3f`, …). Rasterising at 12 or 20 to "get
bigger text" resamples a grid-aligned design onto a grid it was not drawn for,
which reintroduces the blur from the other direction — the rasteriser invents
in-between pixels that the thresholding step then has to guess about.

The design size is also what the `.fnt`'s `size`, `lineHeight` and `base` values
describe, so a wrong rasterisation size is baked into the metrics as well as the
atlas.

## Threshold the alpha — and threshold *before* measuring

FreeType antialiases by default. That is right for a display face and wrong for
a pixel font: grey edge pixels are fake pixels, and they survive every integer
upscale as a permanent smear. Render each glyph, then force every pixel to
**fully opaque or fully transparent** at an alpha threshold (128 works).

Threshold **before** measuring the glyph's ink bounding box. A faint antialiased
fringe is a non-zero alpha, so an unthresholded `getbbox()` inflates the box by
a pixel on each side — and every `xoffset` / `yoffset` / `width` / `height` in
the descriptor inherits the error. The symptom is text that looks a pixel loose
in a way no single glyph explains.

For a **display face** the inverse applies: keep the antialiasing, rasterise at
(or near) the size you will draw, and do not threshold. The recipe is the same
script with the threshold pass disabled.

## The metrics that are easy to get subtly wrong

- **BMFont's `yoffset` is measured down from the top of the line** — which is
  exactly what Pillow's `"la"` (left-ascender) anchor does. Draw at the anchor
  and the ink bbox *relative to the draw origin* **is** the offset pair. There
  is no ascent arithmetic to get wrong, so don't add any.
- **`lineHeight = ascent + descent` and `base = ascent`**, taken from the face's
  own metrics rather than measured off the atlas.
- **A one-pixel transparent gutter between packed glyphs.** Without it a
  neighbour's edge column can bleed into a sampled glyph edge under any
  filtering.
- **A character the face lacks must be reported, not shipped.** Rasterise a
  codepoint the face does not map to get its `.notdef` bitmap, then compare
  every requested character's mask against it and skip + print the matches.
  The reference tool probes with U+E000 — most text faces leave the private-use
  area unmapped — but icon fonts and some pixel faces DO map it, so verify
  against the face's cmap (e.g. with `fontTools`), or pick a codepoint the face
  verifiably lacks, before trusting the probe. Otherwise the atlas quietly
  ships tofu boxes that only surface on screen, in a string nobody tested.

## The web head takes the same pair

There is no per-platform font source. Content is built **per platform** (a
desktop `.xnb` is not a BlazorGL one), but the web build runs KNI's MGCB over
the same `.fnt` + `.png` with the same importer/processor pair — the asset is
untouched. On macOS/Linux the KNI content build needs the native-lib shim,
including staging `KNI.Extended.Content.Pipeline.dll` so the BitmapFont
importer's dependency probe resolves; see
[`docs/web-targeting.md`](../web-targeting.md) § "macOS / Linux native-lib shim".

## Usage sketch

```bash
python3 -m pip install Pillow

# Rasterise at the face's design size, straight into the game's content tree.
python3 tools/build-bitmap-font.py ~/Downloads/PixelOperator8.ttf \
    --size 8 --out-dir MyGame.Core/Content/Fonts
# PixelOperator8: N glyph(s) at 8px -> WxH page, lineHeight L, base B
#   MyGame.Core/Content/Fonts/PixelOperator8.png
#   MyGame.Core/Content/Fonts/PixelOperator8-fnt.fnt
#   content keys: Fonts/PixelOperator8  and  Fonts/PixelOperator8-fnt
```

Then add both to `Content.mgcb` (copy an existing font's two blocks — the page
uses `TextureImporter`/`TextureProcessor`, the descriptor
`BitmapFontImporter`/`BitmapFontProcessor`) and use it like any other font:

```csharp
var font = content.Load<BitmapFont>("Fonts/PixelOperator8-fnt");

entity.Set(new DynamicTextComponent
{
    Font = font,
    TextContent = "READY",
    Color = Color.White,
    Scale = 3f,                 // whole numbers only for a pixel face
    Target = RenderTargetID.HUD,
});
```

**Dependencies:** Pillow (`python3 -m pip install Pillow`) — the generator
itself needs nothing else. Building and loading the output is the game's
normal content stack (MGCB, MonoGame/KNI).

## Adapt these

The reference script is written for one game. When you copy it:

- **`--out-dir` default** — it defaults to that game's own
  `Gmtk2026.Core/Content/Fonts`. Point it at your content tree.
- **The character set** (`DEFAULT_CHARS`) — printable ASCII plus the handful of
  marks that game's strings use (`—–·×…°`). Add whatever yours needs; an absent
  character is skipped and reported, not substituted.
- **`ALPHA_THRESHOLD` (128), and whether to threshold at all** — drop the pass
  for a display face.
- **`ATLAS_MAX_WIDTH` (512) and `GLYPH_PAD` (1)** — a large face over a wide
  character set wants a bigger page.
- **The `-fnt` suffix** — keep it. It is the part of the naming that the content
  pipeline actually constrains.

## Reference implementation

[`tools/build-bitmap-font.py`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/tools/build-bitmap-font.py)
in `roo-oliv/gmtk-2026gj` (recipe validated against commit `26d3729`; the link
tracks `main`).

This is the implementation to **copy and adapt**, not a dependency to install.
The engine deliberately does not vendor the script: tooling of this shape ships
as a documented recipe, so each game owns a version tuned to its own content
tree, character set and face — the same ownership model as the modules
themselves. What to change is listed under "Adapt these" above.
