"""Step 5: re-typeset glyphs onto a true monospace grid; quantize colors;
render reconstruction next to the original for visual verification."""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)          # Icon/ascii — deliverables live here
BUILD = os.path.join(HERE, "build")   # intermediates (gitignored)
os.makedirs(BUILD, exist_ok=True)
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = BUILD
SRC = os.path.join(ROOT, "monodreams-ascii-draft.jpeg")

data = json.load(open(f"{OUT}/glyphs.json"))
glyphs = data["glyphs"]
NROWS = data["rows"]

# ---- grid: pitch/phase from yellow glyph centers (the well-gridded moon) ----
yc = np.array([g["cx"] for g in glyphs if g["rgb"][0] > g["rgb"][2]])
best = (0, None, None)
for px in np.arange(17.3, 18.01, 0.005):
    ang = np.mod(yc, px) / px * 2 * np.pi
    mx, my = np.cos(ang).mean(), np.sin(ang).mean()
    R = np.hypot(mx, my)
    if R > best[0]:
        ph = np.arctan2(my, mx) % (2 * np.pi) / (2 * np.pi) * px
        best = (R, px, ph)
R, PX, PHASE = best
print(f"moon grid: pitch={PX:.3f} phase={PHASE:.2f} R={R:.3f}")
X0 = PHASE
while X0 - PX > 0:
    X0 -= PX
cols_needed = int(np.ceil((1024 - X0) / PX))
print("X0:", round(X0, 2), "cols:", cols_needed)
NCOLS = cols_needed

# ---- color quantization: brightness tiers per hue family ----
for g in glyphs:
    g["hue"] = "Y" if g["rgb"][0] > g["rgb"][2] else "B"
    g["bri"] = max(g["rgb"])
palette = {}   # code -> rgb
codes = {}
for hue in ("Y", "B"):
    gs = [g for g in glyphs if g["hue"] == hue]
    bri = np.array([g["bri"] for g in gs])
    t1, t2 = np.percentile(bri, 33), np.percentile(bri, 66)
    for g in gs:
        tier = 0 if g["bri"] < t1 else (1 if g["bri"] < t2 else 2)
        g["tier"] = tier
    for tier in (0, 1, 2):
        sel = np.array([g["rgb"] for g in gs if g["tier"] == tier])
        # single-char codes: y/Y/G = dim/mid/bright gold, b/B/A = dim/mid/bright azure
        code = {0: hue.lower(), 1: hue, 2: ("G" if hue == "Y" else "A")}[tier]
        palette[code] = [int(v) for v in np.median(sel, axis=0)]
        for g in gs:
            if g["tier"] == tier:
                g["colcode"] = code
print("palette:", palette)

STYLES = ["regular", "bold", "italic", "bold-italic"]
SCODE = {"regular": "r", "bold": "b", "italic": "i", "bold-italic": "x"}

# ---- per-row layout with duplicate-preferred drops ----
grid_ch = [[" "] * NCOLS for _ in range(NROWS)]
grid_col = [[" "] * NCOLS for _ in range(NROWS)]
grid_sty = [[" "] * NCOLS for _ in range(NROWS)]
dropped = []
for k in range(NROWS):
    row = sorted((g for g in glyphs if g["row"] == k), key=lambda g: g["cx"])
    last = -1
    last_ch = None
    for g in row:
        exact = (g["cx"] - X0) / PX
        target = int(round(exact))
        col = max(target, last + 1)
        err = col - exact
        if col >= NCOLS or err > 1.6:
            if g["ch"] == last_ch or err > 2.4 or col >= NCOLS:
                dropped.append((k, g["ch"], round(err, 2)))
                continue
        grid_ch[k][col] = g["ch"]
        grid_col[k][col] = g["colcode"]
        grid_sty[k][col] = SCODE[g["style"]]
        last, last_ch = col, g["ch"]
print(f"dropped {len(dropped)} of {len(glyphs)} glyphs:",
      dropped[:20], "..." if len(dropped) > 20 else "")

txt = "\n".join("".join(r).rstrip() for r in grid_ch)
open(f"{OUT}/moon_waves.txt", "w").write(txt + "\n")
json.dump(dict(rows=NROWS, cols=NCOLS, palette=palette,
               chars=["".join(r) for r in grid_ch],
               colors=["".join(r) for r in grid_col],
               styles=["".join(r) for r in grid_sty]),
          open(f"{OUT}/moon_waves.json", "w"), ensure_ascii=False, indent=1)
print("saved moon_waves.txt / .json")
print(txt)

# ---- reconstruction render (same geometry as original) ----
CELL_W, CELL_H = PX, 23.347
img = Image.new("RGB", (1024, 1024), (5, 5, 8))
d = ImageDraw.Draw(img)
fonts = {}
for code, (path, idx) in {"r": ("/System/Library/Fonts/Menlo.ttc", 0),
                          "b": ("/System/Library/Fonts/Menlo.ttc", 1),
                          "i": ("/System/Library/Fonts/Menlo.ttc", 2),
                          "x": ("/System/Library/Fonts/Menlo.ttc", 3)}.items():
    fonts[code] = ImageFont.truetype(path, 19, index=idx)
baselines = data["baseline"]
for k in range(NROWS):
    for c in range(NCOLS):
        ch = grid_ch[k][c]
        if ch == " ":
            continue
        colr = tuple(palette[grid_col[k][c]])
        f = fonts[grid_sty[k][c]]
        x = X0 + c * PX + PX / 2
        y = baselines[k]
        d.text((x, y), ch, font=f, fill=colr, anchor="ms")
img.save(f"{OUT}/reconstruction.png")

orig = Image.open(SRC).convert("RGB")
side = Image.new("RGB", (2058, 1024), (0, 0, 0))
side.paste(orig, (0, 0))
side.paste(img, (1034, 0))
side.save(f"{OUT}/side_by_side.png")
print("saved reconstruction.png / side_by_side.png")
