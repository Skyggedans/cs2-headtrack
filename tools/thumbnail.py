#!/usr/bin/env python3
"""The mod page's thumbnail on Paradox Mods: src/HeadTrack/Properties/Thumbnail.png.

A head in profile with sideways arrows, sight lines from the eye through the
corners of a monitor, and a dusk city in the monitor, with the name on top.
Square, because the page crops thumbnails to a square, and drawn to read at
128 px, the size mod lists show.

Usage: python3 -I tools/thumbnail.py [out.png]   (default: the mod's Thumbnail.png)
Needs only PIL and the DejaVu Sans fonts; draws at 3x and downsamples, and
gives the same file on every run.
"""
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "src", "HeadTrack", "Properties", "Thumbnail.png")
N = 1024  # final size
S = 3  # supersampling factor
W = N * S
FONTS = "/usr/share/fonts/dejavu-sans-fonts/"

BG_TOP = (12, 17, 28)
BG_BOT = (26, 36, 55)
CYAN = (128, 214, 255)  # arrows, sight lines, "Track"
HEAD_BACK = (160, 172, 198)
HEAD_FRONT = (228, 234, 244)
BODY = (23, 31, 51)  # near buildings
LIT = (255, 212, 138)  # their windows


def s(v):
    """Final-pixel length -> supersampled pixels."""
    return int(round(v * S))


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * t * (t * (t * 6 - 15) + 10)


def ramp(stops, t):
    if t <= stops[0][0]:
        return stops[0][1]
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        if t <= p1:
            return lerp(c0, c1, (t - p0) / (p1 - p0))
    return stops[-1][1]


def gradient(size, stops, mode="RGB", vertical=True):
    w, h = size
    n = h if vertical else w
    strip = Image.new(mode, (1, n) if vertical else (n, 1))
    vals = [ramp(stops, i / max(1, n - 1)) for i in range(n)]
    strip.putdata([v[0] for v in vals] if mode == "L" else vals)
    return strip.resize((w, h), Image.NEAREST)


def soft(shape, blur, scale=4, size=(W, W)):
    """A blurred L mask: shape(draw, k) draws in final px times k, at 1/scale res."""
    small = Image.new("L", (size[0] // scale, size[1] // scale), 0)
    shape(ImageDraw.Draw(small), S / scale)
    small = small.filter(ImageFilter.GaussianBlur(blur * S / scale))
    return small.resize(size, Image.BICUBIC)


def scaled(mask, a):
    return mask.point(lambda v: int(v * a))


def spline(points, steps=14, alpha=0.5):
    """Open centripetal Catmull-Rom through points."""
    pts = [points[0]] + list(points) + [points[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]

        def nxt(t, a, b):
            return t + max(1e-6, math.hypot(b[0] - a[0], b[1] - a[1])) ** alpha

        t0 = 0.0
        t1 = nxt(t0, p0, p1)
        t2 = nxt(t1, p1, p2)
        t3 = nxt(t2, p2, p3)
        for k in range(steps):
            t = t1 + (t2 - t1) * k / steps

            def mix(a, b, ta, tb):
                if tb - ta < 1e-9:
                    return a
                wa, wb = (tb - t) / (tb - ta), (t - ta) / (tb - ta)
                return (wa * a[0] + wb * b[0], wa * a[1] + wb * b[1])

            a1, a2, a3 = mix(p0, p1, t0, t1), mix(p1, p2, t1, t2), mix(p2, p3, t2, t3)
            b1, b2 = mix(a1, a2, t0, t2), mix(a2, a3, t1, t3)
            out.append(mix(b1, b2, t1, t2))
    out.append(points[-1])
    return out


# ---------------------------------------------------------------- layout (final px)
TITLE_SIZE = 148
TITLE_TOP = 96  # top of the capitals

MON = (450, 350, 964, 678)  # monitor, outer edge of the rim
RIM, BEZEL = 7, 12
SCR = (MON[0] + RIM + BEZEL, MON[1] + RIM + BEZEL, MON[2] - RIM - BEZEL, MON[3] - RIM - BEZEL)
STAND_X = (MON[0] + MON[2]) / 2
BASE_Y = 812

HEAD_H = 250  # crown to chin
CROWN = (214, 424)
TILT = -5.0  # degrees: the head turned up a little toward the screen
FADE = (730, 990)  # the bust fades out between these heights

# Profile in head heights from the crown, facing right (+x), y down.
PROFILE = [
    (-0.60, 2.40), (-0.61, 1.90), (-0.57, 1.56), (-0.45, 1.31), (-0.32, 1.15),
    (-0.27, 1.00), (-0.29, 0.87), (-0.37, 0.74), (-0.445, 0.57), (-0.47, 0.39),
    (-0.41, 0.18), (-0.24, 0.04), (0.00, 0.00), (0.20, 0.03), (0.33, 0.11),
    (0.405, 0.25), (0.425, 0.38), (0.408, 0.452), (0.475, 0.54), (0.565, 0.628),
    (0.545, 0.664), (0.468, 0.678), (0.482, 0.728), (0.458, 0.762), (0.476, 0.798),
    (0.425, 0.848), (0.455, 0.915), (0.41, 0.98), (0.32, 1.005), (0.20, 1.03),
    (0.155, 1.10), (0.16, 1.22), (0.22, 1.33), (0.36, 1.48), (0.47, 1.68),
    (0.50, 1.95), (0.50, 2.40),
]
EYE = (0.322, 0.468)
EAR = (-0.075, 0.535)
PIVOT = (0.0, 1.0)


def head_point(p):
    """Profile point -> final px, the head (not the bust) tilted up."""
    x, y = p
    w = 1.0 if y < 0.85 else max(0.0, 1.0 - (y - 0.85) / 0.35)
    a = math.radians(TILT) * w
    dx, dy = x - PIVOT[0], y - PIVOT[1]
    x = PIVOT[0] + dx * math.cos(a) - dy * math.sin(a)
    y = PIVOT[1] + dx * math.sin(a) + dy * math.cos(a)
    return (CROWN[0] + x * HEAD_H, CROWN[1] + y * HEAD_H)


# ---------------------------------------------------------------- the city
# Near buildings: left edge, right edge (fractions of the screen width), top
# (fraction of the screen height), crown style.
NEAR = [
    (-0.02, 0.115, 0.44, "box"),
    (0.13, 0.235, 0.20, "antenna"),
    (0.25, 0.365, 0.50, None),
    (0.455, 0.565, 0.33, "steps"),
    (0.58, 0.665, 0.56, None),
    (0.68, 0.80, 0.25, "antenna"),
    (0.815, 0.90, 0.47, "box"),
    (0.915, 1.02, 0.37, "steps"),
]


def make_city(w, h):
    """The dusk city in the monitor; w, h in supersampled px."""
    rnd = random.Random(5)
    k = w / 470.0  # one city unit is about one final px
    img = gradient((w, h), [
        (0.00, (34, 52, 116)),
        (0.28, (76, 94, 170)),
        (0.46, (164, 122, 164)),
        (0.58, (238, 150, 112)),
        (0.68, (255, 190, 132)),
        (1.00, (255, 204, 150)),
    ])
    d = ImageDraw.Draw(img, "RGBA")
    for _ in range(18):  # early stars
        x, y = rnd.uniform(0, w), rnd.uniform(0, 0.30 * h)
        r = rnd.uniform(0.9, 1.7) * k
        d.ellipse([x - r, y - r, x + r, y + r], fill=(235, 240, 255, rnd.randint(80, 190)))

    # The setting sun, in the gap between the towers, and its glow.
    sun = (0.41 * w, 0.655 * h)
    glow = Image.new("L", (w // 4, h // 4), 0)
    gx, gy = sun[0] / 4, sun[1] / 4
    ImageDraw.Draw(glow).ellipse([gx - 34 * k, gy - 20 * k, gx + 34 * k, gy + 20 * k], fill=255)
    glow = glow.filter(ImageFilter.GaussianBlur(12 * k)).resize((w, h), Image.BICUBIC)
    img.paste((255, 222, 170), (0, 0), scaled(glow, 0.75))
    d = ImageDraw.Draw(img, "RGBA")
    r = 21 * k
    d.ellipse([sun[0] - r, sun[1] - r, sun[0] + r, sun[1] + r], fill=(255, 236, 196))

    def ridge(base, amp, color, seed):
        g = random.Random(seed)
        ph = [g.uniform(0, 6.3) for _ in range(3)]
        pts = [(0, h)]
        for i in range(0, 121):
            t = i / 120
            y = base - amp * (0.55 * math.sin(2.2 * math.pi * t + ph[0])
                              + 0.30 * math.sin(5.1 * math.pi * t + ph[1])
                              + 0.15 * math.sin(11.3 * math.pi * t + ph[2]))
            pts.append((w * t, y))
        pts.append((w, h))
        d.polygon(pts, fill=color)

    ridge(0.60 * h, 0.075 * h, (128, 112, 164), 3)  # far hills
    ridge(0.68 * h, 0.045 * h, (96, 90, 142), 8)  # nearer hills

    # Far skyline, low and hazy.
    x = -4 * k
    while x < w:
        bw = rnd.uniform(14, 30) * k
        top = h - rnd.uniform(0.18, 0.33) * h
        d.rectangle([x, top, x + bw, h], fill=(66, 70, 118))
        x += bw + rnd.uniform(1, 5) * k

    # Near buildings with lit windows.
    for x0, x1, top, crown in NEAR:
        bx0, bx1, by = x0 * w, x1 * w, top * h
        bw = bx1 - bx0
        d.rectangle([bx0, by, bx1, h], fill=BODY)
        if crown == "steps":
            d.rectangle([bx0 + bw * 0.16, by - 12 * k, bx1 - bw * 0.16, by], fill=BODY)
            d.rectangle([bx0 + bw * 0.34, by - 24 * k, bx1 - bw * 0.34, by - 12 * k], fill=BODY)
        elif crown == "antenna":
            cx = (bx0 + bx1) / 2
            d.rectangle([cx - 1.3 * k, by - 26 * k, cx + 1.3 * k, by], fill=BODY)
            d.ellipse([cx - 2.6 * k, by - 29 * k, cx + 2.6 * k, by - 24 * k], fill=(255, 110, 96))
        elif crown == "box":
            d.rectangle([bx0 + bw * 0.55, by - 10 * k, bx0 + bw * 0.85, by], fill=BODY)
        pitch = 13 * k
        cols = max(2, int((bw - 10 * k) // pitch))
        left = bx0 + (bw - cols * pitch) / 2 + 3.5 * k
        wy = by + 10 * k
        while wy < h - 8 * k:
            dark_row = rnd.random() < 0.12
            for c in range(cols):
                if not dark_row and rnd.random() < 0.46:
                    wx = left + c * pitch
                    tone = LIT if rnd.random() < 0.8 else (255, 236, 200)
                    d.rectangle([wx, wy, wx + 6 * k, wy + 8 * k], fill=tone)
            wy += 16 * k

    # Warm street haze along the bottom.
    haze = gradient((w, h), [(0, (0,)), (0.80, (0,)), (1, (90,))], "L")
    img.paste((255, 176, 110), (0, 0), haze)
    return img


# ---------------------------------------------------------------- drawing helpers
def arrow(d, x0, x1, y, width, head, color):
    """Horizontal arrow from x0 to x1 (final px), filled head at x1."""
    sgn = 1 if x1 > x0 else -1
    shaft_end = x1 - sgn * head * 0.8
    d.line([(s(x0), s(y)), (s(shaft_end), s(y))], fill=color, width=s(width))
    r = width / 2
    d.ellipse([s(x0 - r), s(y - r), s(x0 + r), s(y + r)], fill=color)
    d.polygon([(s(x1), s(y)), (s(x1 - sgn * head), s(y - head * 0.62)),
               (s(x1 - sgn * head), s(y + head * 0.62))], fill=color)


def main():
    img = gradient((W, W), [(0.0, BG_TOP), (1.0, BG_BOT)])

    # Light from the screen spilling into the room, and a pool of it below.
    cx, cy = (SCR[0] + SCR[2]) / 2, (SCR[1] + SCR[3]) / 2
    spill = soft(lambda d, k: d.ellipse([(cx - 330) * k, (cy - 240) * k, (cx + 330) * k, (cy + 240) * k], fill=255), 110)
    img.paste((60, 94, 150), (0, 0), scaled(spill, 0.38))
    pool = soft(lambda d, k: d.ellipse([(cx - 330) * k, (BASE_Y - 34) * k, (cx + 330) * k, (BASE_Y + 64) * k], fill=255), 36)
    img.paste((74, 98, 144), (0, 0), scaled(pool, 0.42))
    # The edge of the desk the monitor stands on, caught by the screen light.
    edge = soft(lambda d, k: d.rectangle([(cx - 250) * k, (BASE_Y + 1) * k, (cx + 250) * k, (BASE_Y + 3) * k], fill=255), 0.6, scale=1)
    edge = ImageChops.multiply(edge, gradient((W, W), [(0, (0,)), ((cx - 250) / N, (0,)), (cx / N, (255,)), ((cx + 250) / N, (0,)), (1, (0,))], "L", vertical=False))
    img.paste((120, 146, 190), (0, 0), scaled(edge, 0.55))

    # Vignette.
    vig = soft(lambda d, k: d.ellipse([-140 * k, -100 * k, (N + 140) * k, (N + 160) * k], fill=255), 160)
    img.paste((5, 8, 14), (0, 0), scaled(ImageChops.invert(vig), 0.6))

    # Monitor: shadow, stand, rim, bezel, screen.
    sh = soft(lambda d, k: d.rounded_rectangle([(MON[0] + 4) * k, (MON[1] + 16) * k, (MON[2] + 4) * k, (MON[3] + 24) * k], radius=24 * k, fill=255), 22)
    img.paste((4, 6, 12), (0, 0), scaled(sh, 0.55))
    base = soft(lambda d, k: d.ellipse([(STAND_X - 150) * k, (BASE_Y - 8) * k, (STAND_X + 150) * k, (BASE_Y + 22) * k], fill=255), 10)
    img.paste((4, 6, 12), (0, 0), scaled(base, 0.7))
    # Stand: shaded from the top down, a lit edge on the foot.
    stand = Image.new("L", (W, W), 0)
    sd = ImageDraw.Draw(stand)
    neck = [(STAND_X - 32, MON[3] - 10), (STAND_X + 32, MON[3] - 10), (STAND_X + 50, BASE_Y - 10), (STAND_X - 50, BASE_Y - 10)]
    sd.polygon([(s(x), s(y)) for x, y in neck], fill=255)
    sd.rounded_rectangle([s(STAND_X - 118), s(BASE_Y - 16), s(STAND_X + 118), s(BASE_Y + 2)], radius=s(9), fill=255)
    shade = gradient((W, W), [(0, (66, 80, 108)), (MON[3] / N, (66, 80, 108)), (BASE_Y / N, (44, 54, 76)), (1, (44, 54, 76))])
    img.paste(shade, (0, 0), stand)
    d = ImageDraw.Draw(img)
    d.line([(s(STAND_X - 108), s(BASE_Y - 15)), (s(STAND_X + 108), s(BASE_Y - 15))], fill=(92, 108, 138), width=s(2))
    # Rim, lit from above, and the dark bezel.
    ring = Image.new("L", (W, W), 0)
    ImageDraw.Draw(ring).rounded_rectangle([s(MON[0]), s(MON[1]), s(MON[2]), s(MON[3])], radius=s(22), fill=255)
    rim_fill = gradient((W, W), [(0, (214, 222, 236)), (MON[1] / N, (214, 222, 236)), (MON[3] / N, (156, 168, 192)), (1, (156, 168, 192))])
    img.paste(rim_fill, (0, 0), ring)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([s(MON[0] + RIM), s(MON[1] + RIM), s(MON[2] - RIM), s(MON[3] - RIM)], radius=s(16), fill=(15, 20, 32))
    sw, shh = s(SCR[2] - SCR[0]), s(SCR[3] - SCR[1])
    city = make_city(sw, shh)
    # Glass: a faint sheen from the top left.
    sheen = Image.new("L", (sw, shh), 0)
    ImageDraw.Draw(sheen).polygon([(0, 0), (sw * 0.40, 0), (sw * 0.16, shh), (0, shh)], fill=255)
    sheen = ImageChops.multiply(sheen, gradient((sw, shh), [(0, (255,)), (1, (40,))], "L"))
    city.paste((255, 255, 255), (0, 0), scaled(sheen, 0.06))
    mask = Image.new("L", (sw, shh), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, sw - 1, shh - 1], radius=s(8), fill=255)
    img.paste(city, (s(SCR[0]), s(SCR[1])), mask)

    # The head and bust: a silhouette faded out at the bottom.
    pts = [head_point(p) for p in spline(PROFILE, steps=18)]
    sil = Image.new("L", (W, W), 0)
    ImageDraw.Draw(sil).polygon([(s(x), s(y)) for x, y in pts], fill=255)
    fade = Image.new("L", (1, W))
    fade.putdata([int(255 * (1 - smooth((y / S - FADE[0]) / (FADE[1] - FADE[0])))) for y in range(W)])
    sil = ImageChops.multiply(sil, fade.resize((W, W), Image.NEAREST))
    eye = head_point(EYE)

    # Sight lines: a faint cone from the eye onto the screen, under the head.
    corners = [(SCR[0], SCR[1]), (SCR[0], SCR[3]), (SCR[2], SCR[1]), (SCR[2], SCR[3])]
    cone = Image.new("L", (W, W), 0)
    ImageDraw.Draw(cone).polygon([(s(eye[0]), s(eye[1])), (s(SCR[0]), s(SCR[1])), (s(SCR[0]), s(SCR[3]))], fill=255)
    cone = ImageChops.multiply(cone, gradient((W, W), [(0, (0,)), (eye[0] / N, (24,)), (SCR[0] / N, (78,)), (1, (78,))], "L", vertical=False))
    img.paste(CYAN, (0, 0), cone)

    # Head fill: lit from the screen side, shaded toward the back.
    x0 = min(x for x, _ in pts)
    x1 = max(x for x, _ in pts)
    fill = gradient((W, W), [(0, HEAD_BACK), (x0 / N, HEAD_BACK), (x1 / N, HEAD_FRONT), (1, HEAD_FRONT)], vertical=False)
    img.paste(fill, (0, 0), sil)
    # Ear: a faint shape with a darker rim opening toward the face.
    ex, ey = head_point(EAR)
    ew, eh = 0.068 * HEAD_H, 0.125 * HEAD_H
    ear = Image.new("L", (W, W), 0)
    ed = ImageDraw.Draw(ear)
    ed.ellipse([s(ex - ew), s(ey - eh), s(ex + ew), s(ey + eh)], fill=70)
    ed.arc([s(ex - ew), s(ey - eh), s(ex + ew), s(ey + eh)], start=70, end=335, fill=210, width=s(5))
    ed.arc([s(ex - ew * 0.45), s(ey - eh * 0.5), s(ex + ew * 0.55), s(ey + eh * 0.35)], start=110, end=300, fill=150, width=s(3.5))
    ear = ear.rotate(-12 - TILT, resample=Image.BICUBIC, center=(s(ex), s(ey))).filter(ImageFilter.GaussianBlur(s(0.7)))
    ear = ImageChops.multiply(ear, sil)
    img.paste((128, 140, 168), (0, 0), scaled(ear, 0.55))
    # Rim light on the face from the screen.
    rim = ImageChops.subtract(sil, ImageChops.offset(sil, -s(4), 0))
    rim = rim.filter(ImageFilter.GaussianBlur(s(1.2)))
    img.paste((255, 238, 220), (0, 0), scaled(rim, 0.85))

    d = ImageDraw.Draw(img, "RGBA")
    # Sight lines through the four corners.
    for c in corners:
        far = c[0] > SCR[0] + 1
        d.line([(s(eye[0]), s(eye[1])), (s(c[0]), s(c[1]))], fill=CYAN + ((64,) if far else (170,)), width=s(2.5 if far else 3))
    for c in corners:
        r = 5
        d.ellipse([s(c[0] - r), s(c[1] - r), s(c[0] + r), s(c[1] + r)], fill=CYAN + (230,))
    er = 8
    d.ellipse([s(eye[0] - er), s(eye[1] - er), s(eye[0] + er), s(eye[1] + er)], fill=(22, 30, 46))
    gx, gy, gr = eye[0] + 2.6, eye[1] - 2.6, 2.2  # a glint of the screen
    d.ellipse([s(gx - gr), s(gy - gr), s(gx + gr), s(gy + gr)], fill=(196, 234, 255))

    # Arrows: the head moves sideways. Heavy enough to survive at 128 px.
    ax = CROWN[0] + 0.04 * HEAD_H
    ay = CROWN[1] - 62
    arrow(d, ax - 22, ax - 140, ay, 25, 60, CYAN)
    arrow(d, ax + 22, ax + 140, ay, 25, 60, CYAN)

    # Title, two-tone, each half with a slight vertical sheen.
    bold = ImageFont.truetype(FONTS + "DejaVuSans-Bold.ttf", s(TITLE_SIZE))
    a, b = "Head", "Track"
    tx = (W - bold.getlength(a + b)) / 2
    ty = s(TITLE_TOP) - bold.getbbox("H", anchor="ls")[1]  # baseline
    for text, x, top, bottom in ((a, tx, (250, 252, 255), (214, 224, 240)),
                                 (b, tx + bold.getlength(a), (150, 226, 255), (98, 192, 248))):
        m = Image.new("L", (W, W), 0)
        ImageDraw.Draw(m).text((x, ty), text, font=bold, fill=255, anchor="ls")
        g = gradient((W, W), [(0, top), (s(TITLE_TOP) / W, top), (ty / W, bottom), (1, bottom)])
        img.paste(g, (0, 0), m)

    img.resize((N, N), Image.LANCZOS).save(OUT, optimize=True)


if __name__ == "__main__":
    main()
