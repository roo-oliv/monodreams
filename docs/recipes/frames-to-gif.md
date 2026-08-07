# Recipe — raw captured frames into an animated GIF

> The other half of `MONODREAMS_SCREENSHOT=raw`. The engine writes exact pixels
> at full frame rate; assembling them into a shareable clip is a tooling job
> that lives outside the frame loop.

`ScreenshotCaptureSystem` in the [`debug`](../../MonoDreams/debug/docs/overview.md)
module has a raw mode (`CaptureFormat.Raw`) that dumps one uncompressed
RGBA8888 blob per frame, synchronously, allocating nothing per frame — so the
recording is of the game the player would have played rather than of a game
crawling behind a PNG encoder. **The engine ships no mp4/gif writer, and that is
a boundary rather than a gap:** an encoder inside the capture path would put a
codec's timing behaviour inside the loop being verified. This recipe is the
outside-the-loop half.

Read the [`debug` overview § Frame capture](../../MonoDreams/debug/docs/overview.md#frame-capture)
first for the env contract and the disk-cost table. Raw capture is a firehose —
~3.5 MiB per frame at 1280x720, ~220 MB/s at 60 fps — so
`MONODREAMS_SCREENSHOT_MAX_FRAMES` is a safety valve, not decoration.

## The filename contract

Raw frames are named so the directory is self-describing and needs no manifest
that could fall out of step with it. Verified against
`MonoDreams/debug/System/ScreenshotCaptureSystem.cs`:

```
raw_{counter:D6}_{width}x{height}_{gametimeMs:D8}.rgba
```

| Field | Meaning |
|---|---|
| `counter` | zero-based, six digits, zero-padded. Incremented **only after a successful write**, so the sequence is contiguous — a gap means frames were lost, not merely renamed. |
| `width`x`height` | the backbuffer geometry of *that* frame. It can change mid-run if the window is resized. |
| `gametimeMs` | total game time as an **integer millisecond count**, eight digits, zero-padded (`(int)MathF.Round(gameTime * 1000f)`). Integer rather than a formatted float on purpose: a decimal separator is culture-dependent and would make any indexing tool machine-specific. |
| contents | exactly `width * height * 4` bytes, RGBA8888, no header. |

The reference tool parses this with `^raw_(\d+)_(\d+)x(\d+)_(\d+)\.rgba$` and
sorts by `(millis, counter)`. **That regex matches what MonoDreams writes
today** — no adaptation needed. If a future engine change alters the name, the
regex (`FRAME_RE`) is the single place to follow it, and the byte-length check in
the loader is what will otherwise fail first.

Two refusals in the tool are worth keeping when you copy it:

- **One geometry per directory, or refuse.** Mixed sizes mean the window was
  resized mid-capture — two takes in one folder. Silently stretching the shorter
  one is a worse answer than saying so.
- **A frame whose byte length isn't `w * h * 4` is a truncated write**, i.e. the
  capture was killed mid-write. Say which file and stop; delete the last frame
  and retry.

## Three rules, each of which was a visible artefact first

### 1. Nearest-neighbour only when the reduction **factor** is whole

A whole factor picks the same source pixel in every frame, so the image is rock
stable: a stride of 3 samples columns 0, 3, 6, … in every frame of the take. A
**fractional** factor (1280 → 480, say) picks a *different* source pixel as the
phase drifts, so single-pixel details blink in and out between frames and the
whole image shimmers. Area-average those instead (Pillow's `BOX`): every output
pixel is a weighted mean of the source box, so it changes when the picture
changes and not otherwise.

**Test the factor, not divisibility of the dimensions.** This is the part that is
easy to get wrong and expensive to notice. 1280 / 3 = 426.67, so 1280 is not a
multiple of any 3x output width — a `width % out_w == 0` test therefore *rejects*
a 3x reduction and sends it down the area-average path, softening every pixel of
art the reduction existed to preserve. But a stride of 3 over 1280 columns picks
0, 3, 6, …, 1278 — **427 columns**, and exactly the nearest-neighbour reduction
that was wanted.

What the reference tool actually does (`plan_scale`) is the correct form: it
computes `factor = width / out_width` (or takes `--scale` directly) and takes the
strided path when

```python
factor >= 1.0 and abs(factor - round(factor)) < 1e-9
```

— a whole-factor test, with the output size derived by **ceil division**
(`-(-width // stride)` → 427, not 426). Its module docstring calls this "integer
downscales", which is loose wording for the same thing; the code and its
`plan_scale` docstring are the authority, and they test the factor. Symptom when
this is wrong: the GIF looks slightly out of focus and is roughly a third larger,
because blurring invents colours the palette then has to spend entries on.

### 2. One shared palette over the whole take, no dither

Quantising each frame on its own fits each frame its own palette, so flat regions
creep between slightly different colours as the animation runs — the "colour
crawl" that makes a pixel-art GIF look wet. Fit **one** palette over the whole
take and map every frame against it.

Fit it over a *spread* of frames, not the first one: a take that opens in a dark
room and ends in lava needs both colour populations, and a palette fitted to
frame 0 bands the second half. The reference tool stacks ~24 evenly-spaced frames
into one tall image so median cut sees them as a single colour population.

**Dither off**, for the same reason as rule 1: an ordered dither pattern that
moves between frames is noise that reads as motion.

### 3. Time-based sampling, not index decimation

Taking every Nth file assumes the capture was even. It never quite is — a GC, a
chunk bake, a window event — so decimation turns one hitch into a lasting
judder: every frame after the hitch is picked from the wrong moment. Instead, for
each output instant T, pick **the captured frame whose embedded game time is
closest to T**. That puts the wobble back where it belongs: one frame held
slightly longer, and everything after it back on time.

Because the captures are sorted and the wanted times only move forward, this is a
single walk over the list, not a scan per output frame.

**A GIF aside that belongs with the sampling:** GIF stores frame delays in
hundredths of a second, so only frame rates dividing 100 land on an exact delay —
**50 / 25 / 20 / 10**. Anything else is silently rounded by every viewer, which
makes playback speed subtly wrong and impossible to debug from the file. Say so
rather than rounding quietly.

## Usage sketch

```bash
python3 -m pip install pillow numpy

SCRATCH=/tmp/take
mkdir -p "$SCRATCH"

# 1. Capture. Release build, so the frame budget is the game's and not the
#    debugger's. Always cap the frame count — 600 frames is ten seconds.
MONODREAMS_DEBUG_DIR="$SCRATCH" \
MONODREAMS_SCREENSHOT=raw \
MONODREAMS_SCREENSHOT_MAX_FRAMES=600 \
dotnet run --project MyGame.Desktop -c Release

# 2. Assemble. --scale is the reduction FACTOR; keep it whole to stay sharp.
python3 tools/frames-to-gif.py "$SCRATCH" -o take.gif --fps 25 --scale 3
# 600 frames, 1280x720, 10.02s captured (59.8 fps average)
# 251 output frames at 427x240, nearest-neighbour (exact 1/3 reduction)

# 3. Delete the frames. They are gigabytes.
rm -f "$SCRATCH"/*.rgba
```

Useful options in the reference tool: `--width N` (output width, overrides
`--scale`), `--start S` / `--duration S` (trim the take in seconds of capture
time), `--colors N` (palette size, 2..256), `--palette-samples N` (frames the
shared palette is fitted over).

**Dependencies:** Pillow and numpy (`python3 -m pip install pillow numpy`).

## Staging the scene you want to film

Capturing is the easy half; getting the game into the situation worth filming on
the first frame is the other one. That pattern — an env-selected staging system
that arms a named scene a few frames into boot, so a take needs no twenty minutes
of play first — stays **game-side**: it manipulates game-specific items, bosses
and world generation, so it is not an engine primitive. What generalises is the
list of traps, and the reference repo's
[`docs/scene-staging.md`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/docs/scene-staging.md)
encodes them: stage *after* the world is built (spawners and deferred props run a
frame or more late), write authored inputs rather than derived maxima (a system
that reconciles a maximum every frame overwrites a poked one before it is drawn),
suppress any cinematic that takes the camera, keep the subject inside the ranges
the game's own systems gate on, and treat a mistyped scene name as an error that
stages nothing rather than a silent fallback.

Read it before building your own staging harness; it is the accumulated cost of
doing this without one.

## Adapt these

- **`--scale` default (3)** — in the reference game a 3x reduction exactly undoes
  its camera zoom, giving one output pixel per *art* pixel. Set yours to your own
  zoom, or pass `--width`.
- **`--fps` default (25)** and the `EXACT_FPS` set — keep the exactness check;
  change the default if your takes are slower or faster.
- **`--colors` (192) and `--palette-samples` (24)** — a take with two very
  different lighting regimes wants more samples; a flat one wants fewer colours
  and a smaller file.
- **`FRAME_RE`** — only if a future engine version changes the raw filename
  format. It matches today's.
- **Output paths** — the reference tool writes wherever `-o` says and reads
  wherever the capture landed; nothing game-specific there.

## Reference implementation

[`tools/frames-to-gif.py`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/tools/frames-to-gif.py)
in `roo-oliv/gmtk-2026gj`.

This is the implementation to **copy and adapt**, not a dependency to install.
The engine deliberately does not vendor the script — assembling a clip is a
tooling concern that must stay outside the capture path being verified, and each
game's zoom, palette and take length differ. What to change is listed under
"Adapt these" above.
