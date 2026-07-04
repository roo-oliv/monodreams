# Content/Island — the island asset drop folder

This folder holds the **placeholder art packs for the island investigation game**
(island-authoring plan, `docs/level-editor/island-authoring-plan.md`). It is
**gitignored** (only this MANIFEST is committed): the repo is public and most
itch.io packs do not permit redistribution — every checkout downloads its own
copies.

## How it works

- The in-game editor (`--editor` / `MONODREAMS_EDITOR=1`) **scans this folder at
  startup** into the asset palette (the shell's bottom strip): one palette entry
  per PNG, recursively, plus one entry per named region of any sliced sheet
  (see *Sprite sheets* below).
- Textures are loaded **at runtime** (`Texture2D.FromStream` over the content
  stream) — there is **no MGCB content build** for these files. Drop a PNG,
  rebuild (a copy-only build), restart the editor, and it is in the palette.
- The desktop head (`MonoDreams.Examples.Desktop.csproj`) copies
  `Content/Island/**` raw into the output `Content/Island/` directory.
- Placed entities serialize `AssetKey = "file:Island/<path>.png"` (+ an optional
  `#region` suffix). A scene referencing a file this checkout does not have
  loads with a **loud warning + a visible magenta placeholder box** — never an
  invisible entity. Download the pack named below and restart to fix it.
- When art finalizes, assets graduate into MGCB content and the `file:` keys
  flip to content keys — a mechanical, greppable migration (see the
  `level-editor` premises).

## Folder layout (organize freely; folders group the palette)

```
Content/Island/
  ground/      large soft-edged ground patches (grass, sand, dirt)
  roads/       road segments / worn-path blobs
  props/       trees, stones, tufts, flowers, furniture...
  buildings/   building sprites (visual base at the bottom edge)
```

## Packs to download

> Fill this list in as you adopt packs — name + URL + the subfolder you
> extracted into, so a fresh checkout can reproduce your setup.

| Pack | URL | Extracted into |
|---|---|---|
| _(none yet — add the itch.io packs you pick here)_ | | |

## Sprite sheets (slice sidecars)

A sheet PNG becomes individual palette entries via a sidecar JSON next to it,
named `<image>.png.slices.json`:

```json
{
  "regions": [
    { "name": "trunk",  "x": 0,  "y": 0, "w": 32, "h": 48 },
    { "name": "crown",  "x": 32, "y": 0, "w": 48, "h": 48 }
  ]
}
```

Each region is its own palette entry (`file:Island/props/sheet.png#trunk`). A
sheet with a sidecar contributes its regions only. Hand-written or AI-written;
packs that ship individual PNGs need nothing.

## Art spec (for the art you will draw later — plan §2.3)

- Ground patches: large irregular pieces, soft/rough alpha edges, ~256–512 px.
- Roads: straight / gentle-curve / end-fork segment pieces, same soft edges.
- Props & buildings: transparent PNGs with the **visual base at the bottom
  edge** — the editor places y-sorted props with a feet origin (bottom-center),
  so the sprite "stands" where you click.
- Keep one pixels-per-world-unit density (virtual resolution is 800×600; a
  player ~48–64 px tall implies the rest).
