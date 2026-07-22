#!/usr/bin/env python3
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
                out.append("\x1b[0m"); cur = None
            out.append(" ")
            continue
        hexc = colors[c] if c < len(colors) and colors[c] else "#888888"
        sty = styles[c] if c < len(styles) else "r"
        if (hexc, sty) != cur:
            rr, gg, bb = int(hexc[1:3], 16), int(hexc[3:5], 16), int(hexc[5:7], 16)
            out.append("\x1b[0;%s38;2;%d;%d;%dm" % (SGR.get(sty, ""), rr, gg, bb))
            cur = (hexc, sty)
        out.append(ch)
    out.append("\x1b[0m")
    print("".join(out))
