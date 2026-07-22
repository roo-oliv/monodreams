# MonoDreams ASCII art — waves & waning moon

The MonoDreams brand artwork as **real Unicode text art**: two waves (blue)
and a waning moon (gold), extracted from an AI-generated raster draft
(`monodreams-ascii-draft.jpeg`) and then art-graded (flowing water ripple on
the waves, radial glow + soft gaussian craters on the moon).

## Deliverables

| File | What it is |
|---|---|
| `moon-waves.txt` / `monodreams-logo.txt` | Plain Unicode text (38×58 art; 46×58 with the MONODREAMS wordmark) |
| `moon-waves.json` / `monodreams-logo.json` | Source of truth: per-cell character + hex color + style (regular/bold/italic) |
| `moon-waves.ans` / `monodreams-logo.ans` | Truecolor ANSI — `cat monodreams-logo.ans` in any 24-bit terminal |
| `moon-waves.html` / `monodreams-logo.html` | Standalone dark page; `letter-spacing` calibrated to the source cell aspect (~0.755) |
| `moon-waves.png` / `monodreams-logo.png` | Rendered PNG with phosphor glow (Menlo, 4 styles). The logo PNG is the game splash / README image |
| `render_terminal.py` | Renders any of the JSONs in a terminal: `python3 render_terminal.py monodreams-logo.json` |

The wordmark letterforms are custom 5×6 blocks filled with the artwork's own
glyph vocabulary — MONO out of wave letters (`M N W и`), DREAMS out of moon
matter (`@ #`) — with the same color grading as the drawing.

## Regenerating (pipeline/)

Requires macOS (Menlo/Courier New fonts) and `pip install pillow numpy`.

```bash
cd pipeline
python3 step4_extract.py   # segment + classify every glyph from the JPEG
python3 step5_layout.py    # re-typeset onto a true monospace grid
python3 step6_art.py       # artistic grading -> writes ../moon-waves.*
python3 step7_logo.py      # + MONODREAMS wordmark -> writes ../monodreams-logo.*
```

The pipeline is deterministic: rerunning it reproduces the committed
deliverables byte-for-byte. `step1–3` are grid-analysis diagnostics kept for
reference; intermediates land in `pipeline/build/` (gitignored).

Extraction notes: glyphs are segmented by connected components (the AI draft
is NOT on a true grid — the moon sits on a perfect 17.66px pitch but the wave
letters are proportionally packed at ~12–15px), classified by template
matching against real Menlo/Courier New glyphs in 4 styles (bbox-normalized
shapes for letters, baseline-anchored for punctuation, letter case from
measured ink height, N↔И mirror disambiguation by diagonal dominance), then
re-typeset onto the moon's grid dropping only consecutive duplicate letters.

A copy of `monodreams-logo.png` is bundled as game content
(`MonoDreams.Examples.Core/Content/Logo/`) for the boot splash screen.
