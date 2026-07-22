"""Step 3: per-row and per-color pitch analysis of glyph runs."""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)          # Icon/ascii — deliverables live here
BUILD = os.path.join(HERE, "build")   # intermediates (gitignored)
os.makedirs(BUILD, exist_ok=True)
import numpy as np
from PIL import Image

SRC = os.path.join(ROOT, "monodreams-ascii-draft.jpeg")
img = Image.open(SRC).convert("RGB")
a = np.asarray(img).astype(np.float64)
lum = a.max(axis=2)
mask = lum > 45.0

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


def runs_of(band):
    b0, b1 = band
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
    return runs, prof


def color_of(band, x0r, x1r):
    b0, b1 = band
    patch = a[b0:b1, x0r:x1r]
    m = mask[b0:b1, x0r:x1r]
    if m.sum() == 0:
        return "?"
    rgb = patch[m].mean(axis=0)
    r, g, bl = rgb
    return "Y" if r > bl else "B"


def best_pitch(centers):
    if len(centers) < 4:
        return None, 0.0
    c = np.array(centers)
    best = (0, None)
    for px in np.arange(13.0, 20.01, 0.02):
        ang = np.mod(c, px) / px * 2 * np.pi
        R = np.hypot(np.cos(ang).mean(), np.sin(ang).mean())
        if R > best[0]:
            best = (R, px)
    return best[1], best[0]


print("row | nruns | med_gap_between_run_centers | fit pitch (R) | colors")
all_deltas = {"B": [], "Y": []}
for k, band in enumerate(merged):
    runs, prof = runs_of(band)
    centers, colors = [], []
    for (x0r, x1r) in runs:
        w = x1r - x0r
        seg = prof[x0r:x1r].astype(float)
        c = x0r + (seg * np.arange(w)).sum() / seg.sum()
        centers.append((c, w, color_of(band, x0r, x1r)))
    # deltas between adjacent narrow runs, per color
    ds = []
    for (c1, w1, col1), (c2, w2, col2) in zip(centers, centers[1:]):
        d = c2 - c1
        if d < 26 and w1 <= 22 and w2 <= 22:
            ds.append(d)
            if col1 == col2:
                all_deltas[col1].append(d)
    med = np.median(ds) if ds else float("nan")
    ncols = {}
    for _, _, col in centers:
        ncols[col] = ncols.get(col, 0) + 1
    fitp, R = best_pitch([c for c, w, _ in centers if w <= 22])
    print(f"{k:3d} | {len(runs):3d} | med_delta={med:6.2f} | pitch={fitp if fitp else 0:6.2f} (R={R:.2f}) | {ncols}")

for col in ("B", "Y"):
    d = np.array(all_deltas[col])
    if len(d):
        print(f"color {col}: n={len(d)} adjacent deltas, median={np.median(d):.2f}, mean={d.mean():.2f}, p25={np.percentile(d,25):.2f}, p75={np.percentile(d,75):.2f}")
