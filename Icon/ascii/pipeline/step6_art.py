"""Step 6: artistic grading pass over the extracted grid + all deliverables.

Waves get a flowing water treatment (sinusoidal ripple, depth falloff, crest
highlights); the moon gets a radial glow with soft gaussian 'craters' (color
only, plus a handful of @->% swaps in crater cores). No hard lines anywhere.
"""
import os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)          # Icon/ascii — deliverables live here
BUILD = os.path.join(HERE, "build")   # intermediates (gitignored)
os.makedirs(BUILD, exist_ok=True)
import json
import math
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

SCRATCH = BUILD
OUTDIR = ROOT


data = json.load(open(f"{SCRATCH}/moon_waves.json"))
NROWS, NCOLS = data["rows"], data["cols"]
chars = [list(r.ljust(NCOLS)) for r in data["chars"]]
colcodes = [list(r.ljust(NCOLS)) for r in data["colors"]]
stycodes = [list(r.ljust(NCOLS)) for r in data["styles"]]

def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def ramp(stops, t):
    t = max(0.0, min(1.0, t))
    n = len(stops) - 1
    x = t * n
    i = min(int(x), n - 1)
    return lerp(stops[i], stops[i + 1], x - i)


WATER = [(10, 28, 64), (24, 66, 132), (52, 116, 196), (98, 172, 238), (150, 214, 252)]
MOON = [(146, 98, 22), (196, 146, 40), (230, 186, 62), (248, 216, 100)]
CRATER_TINT = (198, 142, 54)
HALO = [(96, 74, 26), (140, 108, 36), (176, 138, 46)]

LETTERS = set("NnMmWwИиVvUuIli")

# ---- moon geometry from '@#%' cells ----
moon_cells = [(r, c) for r in range(NROWS) for c in range(NCOLS) if chars[r][c] in "@#%"]
rs = [r for r, _ in moon_cells]
cs = [c for _, c in moon_cells]
r0m, r1m, c0m, c1m = min(rs), max(rs), min(cs), max(cs)
AS = 0.7546  # cell aspect: width/height => x = c*AS keeps fields isotropic
craters = [  # (row frac, col frac, sigma_rows, depth)
    (0.28, 0.62, 2.4, 0.85),
    (0.52, 0.78, 1.9, 0.7),
    (0.70, 0.50, 2.9, 1.0),
    (0.42, 0.40, 1.5, 0.55),
]


def crater_k(r, c):
    k = 0.0
    for fr, fc, sg, depth in craters:
        cr = r0m + fr * (r1m - r0m)
        cc = c0m + fc * (c1m - c0m)
        d2 = ((r - cr) ** 2 + ((c - cc) * AS) ** 2) / (2 * sg * sg)
        k += depth * math.exp(-d2)
    return min(1.0, k)


def hue_of(r, c):
    code = colcodes[r][c]
    if code == " " or chars[r][c] == " ":
        return None
    return "Y" if code in "yYG" else "B"


hues = [[hue_of(r, c) for c in range(NCOLS)] for r in range(NROWS)]
brows = [r for r in range(NROWS) if any(h == "B" for h in hues[r])]
br0, br1 = (min(brows), max(brows)) if brows else (0, 1)

moon_center = ((r0m + r1m) / 2, (c0m + c1m) / 2)
rmax = max(math.hypot(r - moon_center[0], (c - moon_center[1]) * AS) for r, c in moon_cells)

TIER_L = {"y": 0.30, "Y": 0.62, "G": 1.0, "b": 0.30, "B": 0.62, "A": 1.0}


def tier_l(r, c):
    code = colcodes[r][c]
    return TIER_L.get(code, 0.62)


cellcolor = [["" for _ in range(NCOLS)] for _ in range(NROWS)]
swaps = 0
for r in range(NROWS):
    for c in range(NCOLS):
        h = hues[r][c]
        if h is None:
            continue
        ch = chars[r][c]
        t0 = tier_l(r, c)
        if h == "B":
            depth = (r - br0) / max(1, br1 - br0)
            ripple = 0.5 + 0.5 * math.sin(c * AS * 0.52 + r * 0.85 + 1.6 * math.sin(r * 0.33 + c * 0.07))
            L = 0.18 + 0.50 * t0 + 0.16 * ripple - 0.10 * depth
            above_empty = r == 0 or chars[r - 1][c] == " "
            if above_empty and ch in LETTERS:
                L += 0.22  # crest highlight
            if ch not in LETTERS:
                L += 0.06  # spray/foam dots shimmer a bit
            rgb = ramp(WATER, L)
        else:
            d = math.hypot(r - moon_center[0], (c - moon_center[1]) * AS) / rmax
            if ch in "@#%":
                L = (1.0 - 0.38 * d ** 1.5)
                L *= 0.75 + 0.25 * t0
                k = crater_k(r, c)
                mottle = 0.5 + 0.5 * math.sin(0.9 * c * AS + 0.4 * r) * math.sin(0.5 * r - 0.25 * c * AS + 1.3)
                L *= (1.0 - 0.15 * k) * (0.96 + 0.06 * mottle)
                rgb = ramp(MOON, L)
                if k > 0.02:
                    rgb = lerp(rgb, CRATER_TINT, 0.30 * k)
                if ch == "@" and k > 0.62 and (r + 2 * c) % 3 == 0:
                    chars[r][c] = "%"
                    swaps += 1
            else:
                # halo / gradient edge marks: dim gold by tier + distance
                L = 0.25 + 0.5 * t0 - 0.15 * max(0.0, d - 1.0)
                rgb = ramp(HALO, L)
        cellcolor[r][c] = "#%02x%02x%02x" % rgb
print(f"crater @->% swaps: {swaps}")

# ---------- deliverable 1: plain txt ----------
txt = "\n".join("".join(row).rstrip() for row in chars)
open(f"{OUTDIR}/moon-waves.txt", "w").write(txt + "\n")

# ---------- deliverable 2: json source of truth ----------
doc = dict(
    title="MonoDreams — waves & waning moon (ASCII, extracted + art-graded)",
    generator="claude-code glyph extraction pipeline",
    rows=NROWS, cols=NCOLS,
    cell_aspect=AS,
    background="#000000",
    chars=["".join(r).rstrip() for r in chars],
    styles=[("".join(r)).rstrip() for r in (stycodes)],
    style_key={"r": "regular", "b": "bold", "i": "italic", "x": "bold-italic", " ": "empty"},
    colors=[[cellcolor[r][c] for c in range(NCOLS)] for r in range(NROWS)],
)
json.dump(doc, open(f"{OUTDIR}/moon-waves.json", "w"), ensure_ascii=False)

# ---------- deliverable 3: ANSI file + python renderer ----------
def ansi_lines():
    lines = []
    for r in range(NROWS):
        out, cur = [], None
        rowtxt = chars[r]
        for c in range(NCOLS):
            ch = rowtxt[c]
            if ch == " ":
                if cur is not None:
                    out.append("\x1b[0m"); cur = None
                out.append(" ")
                continue
            hexc = cellcolor[r][c] or "#888888"
            sty = stycodes[r][c]
            key = (hexc, sty)
            if key != cur:
                rr, gg, bb = int(hexc[1:3], 16), int(hexc[3:5], 16), int(hexc[5:7], 16)
                sgr = {"b": "1;", "i": "3;", "x": "1;3;"}.get(sty, "")
                out.append(f"\x1b[0;{sgr}38;2;{rr};{gg};{bb}m")
                cur = key
            out.append(ch)
        out.append("\x1b[0m")
        lines.append("".join(out).rstrip())
    return lines


open(f"{OUTDIR}/moon-waves.ans", "w").write("\n".join(ansi_lines()) + "\n")

renderer = '''#!/usr/bin/env python3
"""Render moon-waves.json as truecolor ANSI in a terminal.

Usage: python3 render_terminal.py [path/to/moon-waves.json]
"""
import json, os, sys

path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "moon-waves.json")
doc = json.load(open(path))
SGR = {"b": "1;", "i": "3;", "x": "1;3;"}
for r, row in enumerate(doc["chars"]):
    out, cur = [], None
    styles = doc["styles"][r] if r < len(doc["styles"]) else ""
    colors = doc["colors"][r]
    for c, ch in enumerate(row):
        if ch == " ":
            if cur is not None:
                out.append("\\x1b[0m"); cur = None
            out.append(" ")
            continue
        hexc = colors[c] if c < len(colors) and colors[c] else "#888888"
        sty = styles[c] if c < len(styles) else "r"
        if (hexc, sty) != cur:
            rr, gg, bb = int(hexc[1:3], 16), int(hexc[3:5], 16), int(hexc[5:7], 16)
            out.append("\\x1b[0;%s38;2;%d;%d;%dm" % (SGR.get(sty, ""), rr, gg, bb))
            cur = (hexc, sty)
        out.append(ch)
    out.append("\\x1b[0m")
    print("".join(out))
'''
open(f"{OUTDIR}/render_terminal.py", "w").write(renderer)

# ---------- deliverable 4: standalone HTML ----------
html_rows = []
for r in range(NROWS):
    spans, cur, buf = [], None, []
    for c in range(NCOLS):
        ch = chars[r][c]
        if ch == " ":
            key = None
        else:
            key = (cellcolor[r][c], stycodes[r][c])
        if key != cur:
            if buf:
                if cur is None:
                    spans.append("".join(buf))
                else:
                    col_, sty_ = cur
                    st = f"color:{col_}"
                    if sty_ in ("b", "x"):
                        st += ";font-weight:700"
                    if sty_ in ("i", "x"):
                        st += ";font-style:italic"
                    spans.append(f'<span style="{st}">' + "".join(buf) + "</span>")
                buf = []
            cur = key
        esc = {"&": "&amp;", "<": "&lt;", ">": "&gt;"}.get(ch, ch)
        buf.append(esc)
    if buf:
        if cur is None:
            spans.append("".join(buf))
        else:
            col_, sty_ = cur
            st = f"color:{col_}"
            if sty_ in ("b", "x"):
                st += ";font-weight:700"
            if sty_ in ("i", "x"):
                st += ";font-style:italic"
            spans.append(f'<span style="{st}">' + "".join(buf) + "</span>")
    html_rows.append("".join(spans).rstrip())

html = f"""<!doctype html>
<meta charset="utf-8">
<title>MonoDreams — waves &amp; waning moon</title>
<style>
  html,body {{ margin:0; background:#000000; min-height:100vh;
               display:flex; align-items:center; justify-content:center; }}
  pre {{ font-family: Menlo, Consolas, 'DejaVu Sans Mono', monospace;
        font-size: clamp(6px, 1.55vw, 15px);
        line-height: 1.0;
        letter-spacing: 0.155em;   /* matches the source cell aspect ~0.755 */
        text-shadow: 0 0 14px rgba(120,140,255,.18), 0 0 3px rgba(255,220,120,.10);
        margin: 4vh 2vw; }}
</style>
<pre>{chr(10).join(html_rows)}</pre>
"""
open(f"{OUTDIR}/moon-waves.html", "w").write(html)

# ---------- deliverable 5: final PNG (with soft glow) ----------
PX, PY, X0 = 17.62, 23.347, 7.01
W = H = 1024
img = Image.new("RGB", (W, H), (0, 0, 0))
d = ImageDraw.Draw(img)
fonts = {k: ImageFont.truetype("/System/Library/Fonts/Menlo.ttc", 19, index=i)
         for k, i in {"r": 0, "b": 1, "i": 2, "x": 3}.items()}
ybase0 = 60.03
for r in range(NROWS):
    for c in range(NCOLS):
        ch = chars[r][c]
        if ch == " ":
            continue
        hexc = cellcolor[r][c] or "#888888"
        rgbv = tuple(int(hexc[i:i + 2], 16) for i in (1, 3, 5))
        f = fonts.get(stycodes[r][c], fonts["r"])
        d.text((X0 + c * PX + PX / 2, ybase0 + r * PY), ch, font=f, fill=rgbv, anchor="ms")
glow = img.filter(ImageFilter.GaussianBlur(4))
glow = glow.point(lambda v: int(v * 0.9))
final = ImageChops.screen(img, glow)
final.save(f"{OUTDIR}/moon-waves.png")
final.save(f"{SCRATCH}/art_render.png")
print("deliverables written to", OUTDIR)
print(txt[:400])
