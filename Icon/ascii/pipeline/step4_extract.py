"""Step 4 v2: segment every glyph; classify with two paths:
  - LARGE (ink height >= 10px): bbox-normalized shape match, then case
    (upper/lower) decided by ink height — captures the AI's height nuance.
  - SMALL: baseline-anchored match (distinguishes '.' vs '·' vs '-' vs ':').
Outputs glyphs.json."""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)          # Icon/ascii — deliverables live here
BUILD = os.path.join(HERE, "build")   # intermediates (gitignored)
os.makedirs(BUILD, exist_ok=True)
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from collections import Counter

SRC = os.path.join(ROOT, "monodreams-ascii-draft.jpeg")
OUT = BUILD

FONTS = [
    ("menlo", "regular", "/System/Library/Fonts/Menlo.ttc", 0),
    ("menlo", "bold", "/System/Library/Fonts/Menlo.ttc", 1),
    ("menlo", "italic", "/System/Library/Fonts/Menlo.ttc", 2),
    ("menlo", "bold-italic", "/System/Library/Fonts/Menlo.ttc", 3),
    ("courier", "regular", "/System/Library/Fonts/Supplemental/Courier New.ttf", 0),
    ("courier", "bold", "/System/Library/Fonts/Supplemental/Courier New Bold.ttf", 0),
    ("courier", "italic", "/System/Library/Fonts/Supplemental/Courier New Italic.ttf", 0),
    ("courier", "bold-italic", "/System/Library/Fonts/Supplemental/Courier New Bold Italic.ttf", 0),
]
LARGE_CHARS = "NnMmWwИиVvUuIli:@#%*+=±‡"
SMALL_CHARS = ".·:,'-=+*~"
BLUE_ALLOWED = set("NnMmWwИиVvUuIli:.·-',~")
YELLOW_ALLOWED = set("@#%*+=±‡-:.·',")
BOX = 26          # large-path canvas
GS = 22           # large-path glyph max dimension after resize
SCANVAS, SBASE = 24, 18   # small-path canvas / baseline row
CASE_H = 12.6     # ink height >= this => uppercase (same-shape pairs only)
# Only pairs whose upper/lower forms share the same letterform get their case
# decided by ink height; N/n, M/m etc. differ in shape, so the template wins.
HEIGHT_PAIRS = {"w": "W", "W": "w", "и": "И", "И": "и", "v": "V", "V": "v",
                "u": "U", "U": "u"}
UPPER = set("NMWИVUI")


def blur(x):
    k = np.array([1.0, 2.0, 1.0]) / 4.0
    for _ in range(2):
        x = np.apply_along_axis(lambda r: np.convolve(r, k, mode="same"), 1, x)
        x = np.apply_along_axis(lambda c: np.convolve(c, k, mode="same"), 0, x)
    return x


def soft(x):
    return np.clip((x - 0.30) / 0.35, 0.0, 1.0)


def norm_box(arr):
    """ink array (float, any size) -> BOX x BOX normalized shape canvas"""
    ys, xs = np.nonzero(arr > 0.12 * arr.max())
    if len(ys) == 0:
        return None
    sub = arr[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = sub.shape
    s = GS / max(h, w)
    tw, th = max(1, int(round(w * s))), max(1, int(round(h * s)))
    im = Image.fromarray(np.clip(sub / sub.max() * 255, 0, 255).astype(np.uint8))
    im = im.resize((tw, th), Image.BILINEAR)
    a = np.zeros((BOX, BOX))
    oy, ox = (BOX - th) // 2, (BOX - tw) // 2
    a[oy:oy + th, ox:ox + tw] = np.asarray(im).astype(np.float64) / 255.0
    return blur(soft(a))


# ---------- LARGE templates (shape space) ----------
# Rendered at ~the image's native glyph scale (font 21 => cap ~15px) so the
# raster acquires the same blur profile the JPEG glyphs get when norm_box
# upscales them; a crisp 44px render matches poorly against JPEG mush.
large_tpl = []
for fam, style, path, idx in FONTS:
    font = ImageFont.truetype(path, 21, index=idx)
    for ch in LARGE_CHARS:
        im = Image.new("L", (48, 48), 0)
        ImageDraw.Draw(im).text((12, 6), ch, font=font, fill=255)
        arr = np.asarray(im).astype(np.float64)
        if arr.max() <= 0:
            continue
        nb = norm_box(arr)
        if nb is None:
            continue
        n = np.linalg.norm(nb)
        large_tpl.append((ch, fam, style, nb / n))
TL = np.stack([t[3].ravel() for t in large_tpl])

# ---------- SMALL templates (baseline space) ----------
small_tpl = []
for fam, style, path, idx in FONTS:
    font = ImageFont.truetype(path, 22, index=idx)
    asc, desc = font.getmetrics()
    for ch in SMALL_CHARS:
        im = Image.new("L", (SCANVAS * 2, SCANVAS * 2), 0)
        ImageDraw.Draw(im).text((SCANVAS // 2, SBASE - asc), ch, font=font, fill=255)
        arr = np.asarray(im).astype(np.float64)
        if arr.max() == 0:
            continue
        ys, xs = np.nonzero(arr > 40)
        w = arr[arr > 40]
        cx = (xs * w).sum() / w.sum()
        arr = np.roll(arr, int(round(SCANVAS / 2 - cx)), axis=1)[:SCANVAS, :SCANVAS]
        a = blur(soft(arr / arr.max()))
        n = np.linalg.norm(a)
        if n == 0:
            continue
        small_tpl.append((ch, fam, style, a / n))
TS = np.stack([t[3].ravel() for t in small_tpl])
print(f"{len(large_tpl)} large + {len(small_tpl)} small templates")

# ---------- image / rows ----------
img = Image.open(SRC).convert("RGB")
rgb = np.asarray(img).astype(np.float64)
lum = rgb.max(axis=2)
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
full = [(i, b) for i, b in enumerate(merged) if b[1] - b[0] >= 12]
idxs = np.array([i for i, _ in full], dtype=float)
bottoms = np.array([b[1] for _, b in full], dtype=float)
A = np.vstack([idxs, np.ones_like(idxs)]).T
(py, y0), *_ = np.linalg.lstsq(A, bottoms, rcond=None)


def baseline_of(k):
    b0, b1 = merged[k]
    return float(b1) if b1 - b0 >= 12 else float(y0 + py * k)


def segment_row(k):
    b0, b1 = merged[k]
    prof = mask[b0:b1, :].sum(axis=0).astype(float)
    on = prof > 0
    runs, s = [], None
    for x in range(len(on)):
        if on[x] and s is None:
            s = x
        elif not on[x] and s is not None:
            runs.append([s, x]); s = None
    if s is not None:
        runs.append([s, len(on)])
    mg = []
    for r in runs:
        # cap 15: an M(13) + thin I(3) must NOT merge; a split glyph still does
        if mg and r[0] - mg[-1][1] <= 2 and (r[1] - mg[-1][0]) <= 15:
            mg[-1][1] = r[1]
        else:
            mg.append(list(r))
    out = []
    for x0r, x1r in mg:
        w = x1r - x0r
        if w <= 21:
            out.append((x0r, x1r))
            continue
        m = mask[b0:b1, x0r:x1r]
        c = rgb[b0:b1, x0r:x1r][m].mean(axis=0)
        pitch = 17.6 if c[0] > c[2] else 13.0
        n = max(2, int(round(w / pitch)))
        sm = blur(np.vstack([prof[x0r:x1r]] * 3))[1]
        cuts = [0]
        for i in range(1, n):
            tgt = int(round(i * w / n))
            lo, hi = max(cuts[-1] + 4, tgt - 5), min(w - 4, tgt + 6)
            if lo >= hi:
                continue
            cuts.append(lo + int(np.argmin(sm[lo:hi])))
        cuts.append(w)
        for a_, b_ in zip(cuts, cuts[1:]):
            if b_ - a_ >= 3:
                out.append((x0r + a_, x0r + b_))
    return out


ITALIC_MARGIN = 0.02

# diagonal-band masks over the BOX canvas, for N <-> И mirror discrimination
_rr, _cc = np.mgrid[0:BOX, 0:BOX]
D_MAIN = (np.abs(_rr - _cc) <= 3).astype(float)          # TL->BR ('N')
D_ANTI = (np.abs(_rr + _cc - (BOX - 1)) <= 3).astype(float)  # BL->TR ('И')


def mirror_feature(q):
    d1 = float((q * D_MAIN).sum())
    d2 = float((q * D_ANTI).sum())
    return (d2 - d1) / (d1 + d2 + 1e-9)


def bar_family(ch, m, w_box, h_box):
    """Structural re-read for the JPEG-mushy {* + = ± ‡} family (yellow).
    m: ink mask of the glyph (band rows x run cols)."""
    ys = np.nonzero(m.any(axis=1))[0]
    xs = np.nonzero(m.any(axis=0))[0]
    sub = m[ys.min():ys.max() + 1, xs.min():xs.max() + 1].astype(float)
    rp = sub.sum(axis=1)
    rp_s = np.convolve(rp, [0.25, 0.5, 0.25], mode="same")
    thr = 0.45 * rp_s.max()
    peaks = 0
    i = 0
    while i < len(rp_s):
        if rp_s[i] > thr:
            peaks += 1
            while i < len(rp_s) and rp_s[i] > thr:
                i += 1
        i += 1
    cp = sub.sum(axis=0)
    mid = len(cp) // 2
    center = float(cp[max(0, mid - 1):mid + 2].max()) / (float(cp.max()) + 1e-9)
    if peaks >= 2 and center >= 0.75 and h_box > w_box:
        return "‡"
    if peaks >= 2 and center < 0.75 and w_box >= 1.05 * h_box:
        return "="
    if peaks == 1 and center >= 0.75 and (rp_s > 0.6 * rp_s.max()).sum() <= 4:
        return "+"
    return ch


def match(T, tpl_list, q, allowed, shifts=(-1, 0, 1)):
    """Best template among `allowed` chars; italic styles must beat the best
    upright candidate by ITALIC_MARGIN to win (JPEG wobble fakes slant)."""
    ok = np.array([t[0] in allowed for t in tpl_list])
    upright = np.array([("italic" not in t[2]) for t in tpl_list])
    best = (-1.0, None)
    best_up = (-1.0, None)
    for dy in shifts:
        for dx in shifts:
            qq = np.roll(np.roll(q, dy, axis=0), dx, axis=1)
            n = np.linalg.norm(qq)
            if n < 1e-9:
                continue
            sc = T @ (qq.ravel() / n)
            sc[~ok] = -2.0
            j = int(np.argmax(sc))
            if sc[j] > best[0]:
                best = (float(sc[j]), j)
            scu = sc.copy()
            scu[~upright] = -2.0
            ju = int(np.argmax(scu))
            if scu[ju] > best_up[0]:
                best_up = (float(scu[ju]), ju)
    if best[1] is not None and "italic" in tpl_list[best[1]][2]:
        if best_up[1] is not None and best[0] - best_up[0] < ITALIC_MARGIN:
            return best_up
    return best


glyphs = []
for k in range(len(merged)):
    b0, b1 = merged[k]
    bl = baseline_of(k)
    for (x0r, x1r) in segment_row(k):
        m = mask[b0:b1, x0r:x1r]
        npix = int(m.sum())
        if npix < 4:
            continue
        col = np.percentile(rgb[b0:b1, x0r:x1r][m], 88, axis=0)  # vivid ink color
        allowed = YELLOW_ALLOWED if col[0] > col[2] else BLUE_ALLOWED
        prof = m.sum(axis=0).astype(float)
        cx = x0r + (prof * np.arange(x1r - x0r)).sum() / prof.sum()
        ys = np.nonzero(m.any(axis=1))[0]
        h_ink = int(ys.max() - ys.min() + 1)
        glyph_lum = lum[b0:b1, max(0, x0r - 1):x1r + 1].copy()
        gm = mask[b0:b1, max(0, x0r - 1):x1r + 1]
        glyph_lum[~gm] *= 0.35  # damp JPEG glow outside the mask
        if h_ink >= 10:
            q = norm_box(glyph_lum)
            score, j = match(TL, large_tpl, q, allowed)
            ch, fam, style, _ = large_tpl[j]
            if ch in "NnИи":  # mirror check: diagonal orientation is decisive
                f = mirror_feature(q)
                if f > 0.10 and ch in "Nn":
                    ch = "И" if h_ink >= CASE_H else "и"
                elif f < -0.10 and ch in "Ии":
                    ch = "N"
            if ch in HEIGHT_PAIRS:  # same-shape pairs: case by ink height
                want_upper = h_ink >= CASE_H
                if (ch in UPPER) != want_upper:
                    ch = HEIGHT_PAIRS[ch]
            if col[0] > col[2] and ch in "*+=±‡" and h_ink <= 13:
                ch = bar_family(ch, m, x1r - x0r, h_ink)
            path = "L"
        else:
            top = int(round(bl)) - SBASE
            left = int(round(cx)) - SCANVAS // 2
            patch = np.zeros((SCANVAS, SCANVAS))
            ys0, ys1 = max(0, top), min(1024, top + SCANVAS)
            xs0, xs1 = max(0, left), min(1024, left + SCANVAS)
            patch[ys0 - top:ys1 - top, xs0 - left:xs1 - left] = lum[ys0:ys1, xs0:xs1]
            gx0 = max(0, x0r - 2 - left)
            gx1 = min(SCANVAS, x1r + 2 - left)
            patch[:, :gx0] = 0
            patch[:, gx1:] = 0
            if patch.max() <= 0:
                continue
            q = blur(soft(patch / patch.max()))
            score, j = match(TS, small_tpl, q, allowed, shifts=(-2, -1, 0, 1, 2))
            ch, fam, style, _ = small_tpl[j]
            if col[0] > col[2] and ch in "*+=":
                ch = bar_family(ch, m, x1r - x0r, h_ink)
            path = "S"
        glyphs.append(dict(row=k, cx=float(cx), x0=int(x0r), x1=int(x1r),
                           npix=npix, h=h_ink, path=path,
                           rgb=[round(float(v), 1) for v in col],
                           ch=ch, family=fam, style=style,
                           score=round(score, 4)))

print(f"{len(glyphs)} glyphs classified")
hb = Counter(g["h"] for g in glyphs if g["rgb"][2] > g["rgb"][0] and g["h"] >= 9)
print("blue tall-glyph ink-height histogram:", sorted(hb.items()))
print("char histogram:", Counter(g["ch"] for g in glyphs).most_common())
print("style histogram:", Counter((g["family"], g["style"]) for g in glyphs).most_common())
by_col = Counter(("Y" if g["rgb"][0] > g["rgb"][2] else "B", g["ch"]) for g in glyphs)
print("yellow chars:", [(c, n) for (col, c), n in by_col.most_common() if col == "Y"][:15])
print("blue chars:  ", [(c, n) for (col, c), n in by_col.most_common() if col == "B"][:15])
print("mean score:", round(float(np.mean([g["score"] for g in glyphs])), 4),
      "| lowest 10:", sorted(round(g["score"], 3) for g in glyphs)[:10])
with open(f"{OUT}/glyphs.json", "w") as f:
    json.dump(dict(rows=len(merged), bands=merged,
                   baseline=[baseline_of(k) for k in range(len(merged))],
                   glyphs=glyphs), f)
print("saved glyphs.json")
