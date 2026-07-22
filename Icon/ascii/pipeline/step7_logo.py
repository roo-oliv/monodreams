"""Step 7: logo + wordmark lockup. Art (from moon-waves.json) + 'MONODREAMS'
in custom 5x6 block letterforms textured with the artwork's own glyphs:
MONO in wave-matter (blue), DREAMS in moon-matter (gold)."""
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

art = json.load(open(f"{OUTDIR}/moon-waves.json"))
NCOLS = art["cols"]

LETTERS = {
    "M": ["X...X", "XX.XX", "X.X.X", "X...X", "X...X", "X...X"],
    "O": [".XXX.", "X...X", "X...X", "X...X", "X...X", ".XXX."],
    "N": ["X...X", "XX..X", "X.X.X", "X..XX", "X...X", "X...X"],
    "D": ["XXXX.", "X...X", "X...X", "X...X", "X...X", "XXXX."],
    "R": ["XXXX.", "X...X", "XXXX.", "X.X..", "X..X.", "X...X"],
    "E": ["XXXX", "X...", "XXX.", "X...", "X...", "XXXX"],
    "A": [".XXX.", "X...X", "XXXXX", "X...X", "X...X", "X...X"],
    "S": [".XXXX", "X....", ".XXX.", "....X", "....X", "XXXX."],
}
WORD = "MONODREAMS"
BLUE_N = 4  # first 4 letters are MONO


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

# compose the 6-row wordmark band
widths = [len(LETTERS[ch][0]) for ch in WORD]
total = sum(widths) + (len(WORD) - 1)
assert total == NCOLS, f"wordmark width {total} != grid {NCOLS}"
H = 6
wm_ch = [[" "] * NCOLS for _ in range(H)]
wm_col = [["" for _ in range(NCOLS)] for _ in range(H)]
wm_sty = [[" "] * NCOLS for _ in range(H)]

WAVE_CAPS = "MNWИ"
WAVE_LOW = "mwи"
x = 0
for li, letter in enumerate(WORD):
    shape = LETTERS[letter]
    lw = len(shape[0])
    is_blue = li < BLUE_N
    for r in range(H):
        for c in range(lw):
            if shape[r][c] != "X":
                continue
            gc = x + c
            hsh = (r * 7 + gc * 13 + li * 5) % 10
            if is_blue:
                # wave-matter, but stroke continuity first: M/N dominant,
                # W and и as rare texture accents
                ch = ("M" if hsh < 5 else "N" if hsh < 8 else "W" if hsh == 8 else "и")
                ripple = 0.5 + 0.5 * math.sin(gc * 0.55 + r * 0.9)
                L = 0.58 + 0.08 * ripple + 0.20 * (1 - r / (H - 1))
                rgb = ramp(WATER, L)
            else:
                ch = "@" if hsh >= 2 else "#"
                # moon-matter: bright bell around the DREAMS block center
                block_c0 = sum(widths[:BLUE_N]) + BLUE_N
                block_w = NCOLS - block_c0
                u = (gc - block_c0) / max(1, block_w)
                bell = math.exp(-((u - 0.5) ** 2) / 0.18)
                mottle = 0.5 + 0.5 * math.sin(0.8 * gc + 0.6 * r)
                L = 0.55 + 0.33 * bell + 0.08 * mottle - 0.06 * (r / (H - 1))
                if ch == "#":
                    L -= 0.10
                rgb = ramp(MOON, L)
            wm_ch[r][gc] = ch
            wm_col[r][gc] = "#%02x%02x%02x" % rgb
            wm_sty[r][gc] = "b"
    x += lw + 1

# ---- combined document: art rows + 2 blank rows + wordmark ----
chars = [list(r.ljust(NCOLS)) for r in art["chars"]]
colors = [list(r) + [""] * (NCOLS - len(r)) for r in art["colors"]]
styles = [list(r.ljust(NCOLS)) for r in art["styles"]]
blank_ch = [" "] * NCOLS
for _ in range(2):
    chars.append(list(blank_ch))
    colors.append([""] * NCOLS)
    styles.append(list(blank_ch))
chars.extend(wm_ch)
colors.extend(wm_col)
styles.extend(wm_sty)
NROWS = len(chars)
print(f"combined grid: {NROWS} x {NCOLS}")

doc = dict(
    title="MonoDreams — logo lockup (waves & moon + MONODREAMS wordmark)",
    generator="claude-code glyph extraction pipeline",
    rows=NROWS, cols=NCOLS,
    cell_aspect=art["cell_aspect"],
    background=art["background"],
    chars=["".join(r).rstrip() for r in chars],
    styles=["".join(r).rstrip() for r in styles],
    style_key=art["style_key"],
    colors=colors,
)
json.dump(doc, open(f"{OUTDIR}/monodreams-logo.json", "w"), ensure_ascii=False)
open(f"{OUTDIR}/monodreams-logo.txt", "w").write(
    "\n".join("".join(r).rstrip() for r in chars) + "\n")

# ---- ANSI ----
SGR = {"b": "1;", "i": "3;", "x": "1;3;"}
lines = []
for r in range(NROWS):
    out, cur = [], None
    for c in range(NCOLS):
        ch = chars[r][c]
        if ch == " ":
            if cur is not None:
                out.append("\x1b[0m"); cur = None
            out.append(" ")
            continue
        hexc = colors[r][c] or "#888888"
        sty = styles[r][c]
        if (hexc, sty) != cur:
            rr, gg, bb = int(hexc[1:3], 16), int(hexc[3:5], 16), int(hexc[5:7], 16)
            out.append(f"\x1b[0;{SGR.get(sty, '')}38;2;{rr};{gg};{bb}m")
            cur = (hexc, sty)
        out.append(ch)
    out.append("\x1b[0m")
    lines.append("".join(out).rstrip())
open(f"{OUTDIR}/monodreams-logo.ans", "w").write("\n".join(lines) + "\n")

# ---- HTML ----
def row_html(r):
    spans, cur, buf = [], None, []
    def flush():
        if not buf:
            return
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
    for c in range(NCOLS):
        ch = chars[r][c]
        key = None if ch == " " else (colors[r][c], styles[r][c])
        if key != cur:
            flush(); buf = []; cur = key
        buf.append({"&": "&amp;", "<": "&lt;", ">": "&gt;"}.get(ch, ch))
    flush()
    return "".join(spans).rstrip()


html = f"""<!doctype html>
<meta charset="utf-8">
<title>MonoDreams — logo</title>
<style>
  html,body {{ margin:0; background:{art['background']}; min-height:100vh;
               display:flex; align-items:center; justify-content:center; }}
  pre {{ font-family: Menlo, Consolas, 'DejaVu Sans Mono', monospace;
        font-size: clamp(6px, 1.55vw, 15px);
        line-height: 1.0;
        letter-spacing: 0.155em;
        text-shadow: 0 0 14px rgba(120,140,255,.18), 0 0 3px rgba(255,220,120,.10);
        margin: 4vh 2vw; }}
</style>
<pre>{chr(10).join(row_html(r) for r in range(NROWS))}</pre>
"""
open(f"{OUTDIR}/monodreams-logo.html", "w").write(html)

# ---- PNG with glow ----
PX, PY, X0, YB0 = 17.62, 23.347, 7.01, 60.03
Wpx = 1024
Hpx = int(math.ceil(YB0 + (NROWS - 1) * PY + 66))
img = Image.new("RGB", (Wpx, Hpx), (0, 0, 0))
d = ImageDraw.Draw(img)
fonts = {k: ImageFont.truetype("/System/Library/Fonts/Menlo.ttc", 19, index=i)
         for k, i in {"r": 0, "b": 1, "i": 2, "x": 3}.items()}
for r in range(NROWS):
    for c in range(NCOLS):
        ch = chars[r][c]
        if ch == " ":
            continue
        hexc = colors[r][c] or "#888888"
        rgbv = tuple(int(hexc[i:i + 2], 16) for i in (1, 3, 5))
        f = fonts.get(styles[r][c], fonts["r"])
        d.text((X0 + c * PX + PX / 2, YB0 + r * PY), ch, font=f, fill=rgbv, anchor="ms")
glow = img.filter(ImageFilter.GaussianBlur(4)).point(lambda v: int(v * 0.9))
ImageChops.screen(img, glow).save(f"{OUTDIR}/monodreams-logo.png")
Image.open(f"{OUTDIR}/monodreams-logo.png").save(f"{SCRATCH}/logo_render.png")
print("logo deliverables written")
for r in chars[-8:]:
    print("".join(r).rstrip())
