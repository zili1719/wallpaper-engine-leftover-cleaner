"""Generate the application icon for Wallpaper Engine Leftover Cleaner.

Design: dark slate rounded square, a wallpaper "frame" outline, and a trash
can silhouette overlapping the lower-right — reads correctly down to 16px.
"""
from __future__ import annotations

import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "icon.ico")
PNG_OUT = os.path.join(ROOT, "assets", "icon-256.png")

S = 1024  # master size, downscaled later
BG_TOP = (23, 26, 33)
BG_BOTTOM = (42, 71, 94)
FRAME = (232, 238, 244)
ACCENT = (110, 200, 255)
TRASH = (255, 186, 72)


def rounded_rect(draw: ImageDraw.ImageDraw, box, radius, fill):
    draw.rounded_rectangle(box, radius=radius, fill=fill)


def build() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # vertical gradient background inside a rounded square
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        r = int(BG_TOP[0] + (BG_BOTTOM[0] - BG_TOP[0]) * t)
        g = int(BG_TOP[1] + (BG_BOTTOM[1] - BG_TOP[1]) * t)
        b = int(BG_TOP[2] + (BG_BOTTOM[2] - BG_TOP[2]) * t)
        gd.line([(0, y), (S, y)], fill=(r, g, b, 255))

    mask = Image.new("L", (S, S), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    # wallpaper frame (outlined rectangle, slightly left/up)
    fx0, fy0, fx1, fy1 = int(S * 0.17), int(S * 0.20), int(S * 0.72), int(S * 0.70)
    lw = int(S * 0.045)
    draw.rounded_rectangle((fx0, fy0, fx1, fy1), radius=int(S * 0.045),
                           outline=FRAME, width=lw)

    # a little sun / accent dot inside the frame
    r = int(S * 0.045)
    cx, cy = fx0 + int(S * 0.13), fy0 + int(S * 0.13)
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=ACCENT)

    # mountain stroke inside the frame
    pts = [
        (fx0 + int(S * 0.03), fy1 - int(S * 0.06)),
        (fx0 + int(S * 0.18), fy0 + int(S * 0.34)),
        (fx0 + int(S * 0.30), fy1 - int(S * 0.14)),
        (fx0 + int(S * 0.40), fy0 + int(S * 0.30)),
        (fx1 - int(S * 0.03), fy1 - int(S * 0.06)),
    ]
    draw.line(pts, fill=ACCENT, width=int(S * 0.030), joint="curve")

    # trash can, lower right, partially over the frame
    tx0, ty0 = int(S * 0.56), int(S * 0.55)
    tx1, ty1 = int(S * 0.86), int(S * 0.90)
    body_top = ty0 + int(S * 0.06)

    # lid
    draw.rounded_rectangle(
        (tx0 - int(S * 0.015), ty0, tx1 + int(S * 0.015), ty0 + int(S * 0.055)),
        radius=int(S * 0.020), fill=TRASH)
    # handle
    draw.rounded_rectangle(
        (tx0 + int(S * 0.085), ty0 - int(S * 0.045), tx1 - int(S * 0.085), ty0 + int(S * 0.010)),
        radius=int(S * 0.014), fill=TRASH)
    # body (trapezoid)
    draw.polygon(
        [(tx0, body_top),
         (tx1, body_top),
         (tx1 - int(S * 0.030), ty1),
         (tx0 + int(S * 0.030), ty1)],
        fill=TRASH)
    # ribs
    for k in (0.32, 0.50, 0.68):
        x = tx0 + int((tx1 - tx0) * k)
        draw.line([(x, body_top + int(S * 0.045)), (x, ty1 - int(S * 0.035))],
                  fill=(23, 26, 33), width=int(S * 0.022))

    return img


def main() -> int:
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    master = build()
    master.resize((256, 256), Image.LANCZOS).save(PNG_OUT, "PNG")
    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    master.save(OUT, format="ICO", sizes=sizes)
    print("wrote", OUT)
    print("wrote", PNG_OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
