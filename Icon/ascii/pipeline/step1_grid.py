"""Step 1: detect the character grid of the ASCII-art JPEG."""
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
lum = a.max(axis=2)  # max channel: catches dim blue and dim yellow alike
print("image size:", img.size, "lum range:", lum.min(), lum.max())

mask = lum > 45.0
print("ink fraction:", mask.mean())

rowsum = mask.sum(axis=1)
colsum = mask.sum(axis=0)


def autocorr_pitch(profile, lo, hi):
    p = profile - profile.mean()
    ac = np.correlate(p, p, mode="full")[len(p) - 1 :]
    ac /= ac[0]
    lag = lo + int(np.argmax(ac[lo:hi]))
    return lag, ac


ypitch, acy = autocorr_pitch(rowsum.astype(float), 8, 60)
xpitch, acx = autocorr_pitch(colsum.astype(float), 6, 40)
print("estimated y pitch:", ypitch, " x pitch:", xpitch)
print("top y autocorr lags:", sorted(range(8, 60), key=lambda l: -acy[l])[:8])
print("top x autocorr lags:", sorted(range(6, 40), key=lambda l: -acx[l])[:8])

# Row band segmentation: contiguous runs where rowsum > small threshold
thr = 2
inrow = rowsum > thr
bands = []
start = None
for y in range(len(inrow)):
    if inrow[y] and start is None:
        start = y
    elif not inrow[y] and start is not None:
        bands.append((start, y))
        start = None
if start is not None:
    bands.append((start, len(inrow)))
# merge bands separated by tiny gaps (<=2 px)
merged = []
for b in bands:
    if merged and b[0] - merged[-1][1] <= 2:
        merged[-1] = (merged[-1][0], b[1])
    else:
        merged.append(list(b))
print(f"row bands: {len(merged)}")
for i, (y0, y1) in enumerate(merged):
    print(f"  band {i:2d}: y {y0:4d}-{y1:4d}  h={y1-y0:3d}")
