#!/usr/bin/env python3
"""Generate the app icons (needs Pillow: pip install pillow).

    python3 scripts/make-icons.py

Writes public/icons/: 192 and 512 (the home-screen icons), a maskable 512 (Android crops it to
whatever shape the launcher uses, so the mark sits well inside a safe zone), and the 180 px icon
iOS wants. Drawn at four times the size and shrunk, so the edges are smooth rather than jagged.
"""
import os
from PIL import Image, ImageDraw

TEAL_TOP = (22, 160, 132)
TEAL_BOT = (10, 112, 92)
OUT = os.path.join(os.path.dirname(__file__), '..', 'public', 'icons')


def mark(size, inset, rounded):
    s = size * 4
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))

    # Vertical gradient background.
    grad = Image.new('RGBA', (s, s))
    px = grad.load()
    for y in range(s):
        t = y / (s - 1)
        row = tuple(int(TEAL_TOP[i] + (TEAL_BOT[i] - TEAL_TOP[i]) * t) for i in range(3)) + (255,)
        for x in range(s):
            px[x, y] = row

    mask = Image.new('L', (s, s), 0)
    d = ImageDraw.Draw(mask)
    if rounded:
        d.rounded_rectangle((0, 0, s - 1, s - 1), radius=int(s * 0.22), fill=255)
    else:
        d.rectangle((0, 0, s, s), fill=255)
    img.paste(grad, (0, 0), mask)

    # A heartbeat line, the same mark as the favicon, inside the safe area.
    d = ImageDraw.Draw(img)
    a = inset * s
    w = s - 2 * a
    pts = [(0.00, 0.55), (0.26, 0.55), (0.38, 0.76), (0.56, 0.18), (0.68, 0.62), (1.00, 0.62)]
    line = [(a + x * w, a + y * w) for x, y in pts]
    th = int(s * 0.07)
    d.line(line, fill=(255, 255, 255, 255), width=th, joint='curve')
    for x, y in (line[0], line[-1]):                      # round the ends
        d.ellipse((x - th / 2, y - th / 2, x + th / 2, y + th / 2), fill=(255, 255, 255, 255))

    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    mark(192, 0.20, True).save(os.path.join(OUT, 'icon-192.png'))
    mark(512, 0.20, True).save(os.path.join(OUT, 'icon-512.png'))
    mark(512, 0.30, False).save(os.path.join(OUT, 'maskable-512.png'))     # full bleed, mark in the middle 60%
    mark(180, 0.20, False).save(os.path.join(OUT, 'apple-touch-icon.png'))  # iOS rounds the corners itself
    print('wrote', sorted(os.listdir(OUT)))


if __name__ == '__main__':
    main()
