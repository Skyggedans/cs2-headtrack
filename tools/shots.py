#!/usr/bin/env python3
"""Screenshot helpers for checking the HeadTrack mod.

  shots.py small IN.png OUT.jpg [--width 1280]     downscale for viewing
  shots.py shift A.png B.png [--grid 6x4]          per-patch image shift A -> B (px)
  shots.py same A.png B.png [--box x0 y0 x1 y1]    are two images (or a box of them) identical?

"shift" estimates, for a grid of patches, how far the image content moved
between two screenshots (phase correlation, sub-pixel by a parabola fit), so
one can check that the window plane stays put while nearer content moves
against the head and farther content with it. Needs numpy and Pillow.
"""

import argparse
import sys

import numpy as np
from PIL import Image


def load_gray(path, width=None):
    image = Image.open(path).convert("L")
    if width and image.width > width:
        image = image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
    return np.asarray(image, dtype=np.float64)


def phase_shift(a, b):
    """The (dx, dy) that moves patch a onto patch b, and the correlation peak."""
    window = np.outer(np.hanning(a.shape[0]), np.hanning(a.shape[1]))
    fa = np.fft.fft2((a - a.mean()) * window)
    fb = np.fft.fft2((b - b.mean()) * window)
    cross = fb * np.conj(fa)
    cross /= np.maximum(np.abs(cross), 1e-12)
    corr = np.real(np.fft.ifft2(cross))
    peak = np.unravel_index(np.argmax(corr), corr.shape)
    h, w = corr.shape

    def refine(index, axis):
        n = corr.shape[axis]
        i = peak[axis]
        prev = corr[(i - 1) % n, peak[1]] if axis == 0 else corr[peak[0], (i - 1) % n]
        here = corr[peak]
        nxt = corr[(i + 1) % n, peak[1]] if axis == 0 else corr[peak[0], (i + 1) % n]
        denom = prev - 2 * here + nxt
        offset = 0.5 * (prev - nxt) / denom if denom != 0 else 0.0
        value = i + offset
        return value - n if value > n / 2 else value

    dy = refine(peak[0], 0)
    dx = refine(peak[1], 1)
    return dx, dy, float(corr[peak])


def cmd_small(args):
    image = Image.open(args.input).convert("RGB")
    if image.width > args.width:
        image = image.resize((args.width, round(image.height * args.width / image.width)), Image.LANCZOS)
    image.save(args.output, quality=88)
    print(f"{args.output}: {image.width}x{image.height}")


def cmd_shift(args):
    a = load_gray(args.a, args.width)
    b = load_gray(args.b, args.width)
    if a.shape != b.shape:
        sys.exit(f"sizes differ: {a.shape} vs {b.shape}")
    cols, rows = (int(v) for v in args.grid.split("x"))
    h, w = a.shape
    ph, pw = h // rows, w // cols
    scale = (Image.open(args.a).width / w) if args.width else 1.0
    print(f"patch shifts A -> B in original pixels (grid {cols}x{rows}, '.' = low confidence)")
    for r in range(rows):
        cells = []
        for c in range(cols):
            pa = a[r * ph:(r + 1) * ph, c * pw:(c + 1) * pw]
            pb = b[r * ph:(r + 1) * ph, c * pw:(c + 1) * pw]
            dx, dy, peak = phase_shift(pa, pb)
            mark = " " if peak > args.min_peak else "."
            cells.append(f"{dx * scale:+7.1f},{-dy * scale:+6.1f}{mark}")
        print("  ".join(cells))


def cmd_same(args):
    a = np.asarray(Image.open(args.a).convert("RGB"), dtype=np.int16)
    b = np.asarray(Image.open(args.b).convert("RGB"), dtype=np.int16)
    if args.box:
        x0, y0, x1, y1 = args.box
        a = a[y0:y1, x0:x1]
        b = b[y0:y1, x0:x1]
    if a.shape != b.shape:
        sys.exit(f"sizes differ: {a.shape} vs {b.shape}")
    diff = np.abs(a - b)
    print(f"max diff {int(diff.max())}, differing pixels {int((diff.max(axis=2) > 0).sum())} of {diff.shape[0] * diff.shape[1]}")
    return 0 if diff.max() == 0 else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    small = sub.add_parser("small")
    small.add_argument("input")
    small.add_argument("output")
    small.add_argument("--width", type=int, default=1280)
    shift = sub.add_parser("shift")
    shift.add_argument("a")
    shift.add_argument("b")
    shift.add_argument("--grid", default="6x4")
    shift.add_argument("--width", type=int, default=1920, help="analyse at this width (0 = full size)")
    shift.add_argument("--min-peak", type=float, default=0.08)
    same = sub.add_parser("same")
    same.add_argument("a")
    same.add_argument("b")
    same.add_argument("--box", type=int, nargs=4)
    args = parser.parse_args()
    return {"small": cmd_small, "shift": cmd_shift, "same": cmd_same}[args.command](args) or 0


if __name__ == "__main__":
    sys.exit(main())
