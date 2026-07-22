"""Step 2: fit global row/column grid model, save debug overlay."""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)          # Icon/ascii — deliverables live here
BUILD = os.path.join(HERE, "build")   # intermediates (gitignored)
os.makedirs(BUILD, exist_ok=True)
import numpy as np
from PIL import Image

SRC = os.path.join(ROOT, "monodreams-ascii-draft.jpeg")
OUT = BUILD

img = Image.open(SRC).convert("RGB")
a = np.asarray(img).astype(np.float64)
lum = a.max(axis=2)
mask = lum > 45.0

# ---- rows ----
rowsum = mask.sum(axis=1)
inrow = rowsum > 2
bands, start = [], None
for y in range(len(inrow)):
    if inrow[y] and start is None:
        start = y
    elif not inrow[y] and start is not None:
        bands.append([start, y]); start = None
if start is not None:
    bands.append([start, len(inrow)])
merged = []
for b in bands:
    if merged and b[0] - merged[-1][1] <= 2:
        merged[-1][1] = b[1]
    else:
        merged.append(list(b))

full = [(i, b) for i, b in enumerate(merged) if b[1] - b[0] >= 12]
idxs = np.array([i for i, b in full], dtype=float)
bottoms = np.array([b[1] for i, b in full], dtype=float)
A = np.vstack([idxs, np.ones_like(idxs)]).T
(py, y0), *_ = np.linalg.lstsq(A, bottoms, rcond=None)
resid = bottoms - (y0 + py * idxs)
print(f"row model: baseline(k) = {y0:.2f} + k*{py:.3f}  | max|resid| = {np.abs(resid).max():.2f}")
NROWS = len(merged)
print("NROWS =", NROWS)

# ---- columns: ink-run centers per row band ----
centers_all = []
for (b0, b1) in merged:
    prof = mask[b0:b1, :].sum(axis=0)
    on = prof > 0
    runs, s = [], None
    for x in range(len(on)):
        if on[x] and s is None:
            s = x
        elif not on[x] and s is not None:
            runs.append((s, x)); s = None
    if s is not None:
        runs.append((s, len(on)))
    for (x0r, x1r) in runs:
        w = x1r - x0r
        if 2 <= w <= 22:  # single glyph; wider runs = touching glyphs, skip for fitting
            seg = prof[x0r:x1r].astype(float)
            c = x0r + (seg * np.arange(w)).sum() / seg.sum()
            centers_all.append(c)
centers_all = np.array(sorted(centers_all))
print(f"{len(centers_all)} glyph-run centers collected")

# robust pitch/offset fit: minimize wrapped deviation over candidate pitches
best = None
for px in np.arange(16.5, 18.01, 0.01):
    ph = np.mod(centers_all, px)
    # circular mean of phases
    ang = ph / px * 2 * np.pi
    mx, my = np.cos(ang).mean(), np.sin(ang).mean()
    R = np.hypot(mx, my)          # concentration: higher = better aligned comb
    if best is None or R > best[0]:
        phase = np.arctan2(my, mx) % (2 * np.pi) / (2 * np.pi) * px
        best = (R, px, phase)
R, px, x0 = best
print(f"column model: center(i) = {x0:.2f} + i*{px:.3f}   (concentration R={R:.3f})")

# residual check: snap each center to nearest column, report deviation
ii = np.round((centers_all - x0) / px)
dev = centers_all - (x0 + ii * px)
print(f"column snap: mean|dev|={np.abs(dev).mean():.2f}px  p95={np.percentile(np.abs(dev),95):.2f}px  max={np.abs(dev).max():.2f}px")
imin, imax = int(ii.min()), int(ii.max())
print(f"column index range: {imin}..{imax}  -> NCOLS={imax-imin+1}")

# ---- debug overlay: red = baseline-anchored row tops, green = column edges ----
ASC = 17
from PIL import ImageDraw
ov = img.copy()
dr = ImageDraw.Draw(ov)
for k in range(0, NROWS + 1):
    ybase = y0 + k * py
    dr.line([(0, ybase - ASC), (1023, ybase - ASC)], fill=(255, 0, 0), width=1)
i = imin
while x0 + (i - 0.5) * px < 1024:
    xx = x0 + (i - 0.5) * px
    if xx >= 0:
        dr.line([(xx, 0), (xx, 1023)], fill=(0, 255, 0), width=1)
    i += 1
ov.save(f"{OUT}/debug_grid.png")
print("saved debug_grid.png")
np.save(f"{OUT}/grid_params.npy", np.array([y0, py, x0, px, NROWS, imin, imax]))
