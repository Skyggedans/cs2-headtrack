#!/usr/bin/env python3
"""Golden vectors for the head-coupled "window" projection.

An independent reference implementation of the window contract (Python 3 and
numpy, nothing else), derived from the contract and from Robert Kooima,
"Generalized Perspective Projection" (2009). It shares no code with the C#
implementation, so the C# tests compare against numbers that the code under
test did not produce.

    python3 tools/golden/window_projection.py        (from the repository root)

writes tests/HeadTrack.Core.Tests/Golden/window_projection.json. The cases are
fixed and every random number comes from a seeded generator, so a re-run
rewrites the same bytes. All self-checks run before the file is written: a
failed check raises and leaves the previous file untouched.

Conventions (Unity)
  World space is left-handed: x right, y up, z forward. A camera basis
  (r, u, f) satisfies np.cross(r, u) == f numerically, as Unity's
  Vector3.Cross(right, up) == forward. Quaternion.Euler(pitch, yaw, 0) gives
      f = (cos p sin y, -sin p, cos p cos y),  r = (cos y, 0, -sin y),  u = f x r
  with pitch > 0 looking down. View space looks down -z:
      x_v = (p - C).r,  y_v = (p - C).u,  z_v = -(p - C).f
  Matrix4x4.Frustum(l, r, b, t, n, f) is the OpenGL matrix
      | 2n/(r-l)  0         (r+l)/(r-l)    0          |
      | 0         2n/(t-b)  (t+b)/(t-b)    0          |
      | 0         0         -(f+n)/(f-n)   -2fn/(f-n) |
      | 0         0         -1             0          |
  and NDC = clip.xy / clip.w.

The window
  The vanilla camera (C, r, u, f, near n, far F, tangent extents
  tanL0 < 0 < tanR0, tanB0 < 0 < tanT0) cuts its frustum at depth D:
      W0 = C + D f + D ((tanR0 + tanL0)/2 r + (tanT0 + tanB0)/2 u)
      w  = D (tanR0 - tanL0)/2,   h = D (tanT0 - tanB0)/2
  The physical screen (Ws x Hs mm) is that rectangle, edge to edge; one screen
  millimetre is s = 2h/Hs world metres. An eye e in screen millimetres (origin
  at the screen centre, +x right, +y up, +z out of the screen) is the world
  point
      P = W0 + s (e.x r + e.y u - e.z f)
  and the head-coupled camera C' = P keeps the vanilla rotation, unless the
  caller overrides C' (lifts it above terrain, say). Whatever C' is, its
  extents are Kooima's for the window corners seen from pe = C' at the vanilla
  near plane, its far plane is F' = F + |C' - C|, and its matrix is
  Frustum(l, r, b, t, n, F'). The neutral eye, the one that puts C' on C, is
      e_n = (-z0 (tanR0 + tanL0)/2, -z0 (tanT0 + tanB0)/2, z0),   z0 = D/s,
  so z0 = Hs/(tanT0 - tanB0) whatever D, and e_n leaves the screen centre when
  the vanilla frustum is lens-shifted.

JSON
  A case has either "eye_mm" (C' comes from the eye) or "camera_override" (C'
  given); the other one is null. "matrix" holds the 16 entries row by row,
  m00, m01, m02, m03, m10, ..., as Unity's Matrix4x4[row, column]. Each probe
  has its NDC from the vanilla camera (C, vanilla matrix) and from the shifted
  one (C', shifted matrix).
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import numpy as np

GENERATOR = "tools/golden/window_projection.py"
OUTPUT = "tests/HeadTrack.Core.Tests/Golden/window_projection.json"
UNITS = "world metres, eye millimetres"
SEED = 20261020

NEAR = 1.0
FAR = 90000.0
RTOL = 1e-9          # every closeness check: |a - b| <= RTOL * max(1, |a|, |b|)
EYE_STEP_MM = 10.0   # the eye step of the parallax checks
PLANE_POINTS = 8     # random window-plane points checked per case
FAR_PROBE = 50.0     # the far probe sits 50 D down the vanilla axis

UHD = 3840 / 2160
ULTRAWIDE = 3440 / 1440


def symmetric(vertical_fov_deg, aspect):
    top = math.tan(math.radians(vertical_fov_deg) / 2)
    return (-aspect * top, aspect * top, -top, top)


def lens_shifted(tans, shift_x, shift_y):
    """Unity's lens shift: the frustum moves by shift times its full width / height."""
    left, right, bottom, top = tans
    dx = shift_x * (right - left)
    dy = shift_y * (top - bottom)
    return (left + dx, right + dx, bottom + dy, top + dy)


# The game's physical camera, from its projection matrix: m11 = 2.95639,
# m00 = 1.66297. Its nominal vertical FOV is 37.4296 deg; the entries give
# 2 atan(1/2.95639) = 37.376 deg, and the cases use the entries.
GAME = (-1 / 1.66297, 1 / 1.66297, -1 / 2.95639, 1 / 2.95639)

# name -> (tanL0, tanR0, tanB0, tanT0)
FRUSTA = {
    "fov30": symmetric(30.0, UHD),
    "fov37.4296": GAME,
    "fov60": symmetric(60.0, UHD),
    "fov37.4296+shift(0.08,-0.05)": lens_shifted(GAME, 0.08, -0.05),
    "fov30+shift(-0.22,0.18)": lens_shifted(symmetric(30.0, UHD), -0.22, 0.18),
    "uw-fov40+shift(0.1,0.12)": lens_shifted(symmetric(40.0, ULTRAWIDE), 0.10, 0.12),
}

# name -> (Ws, Hs) in millimetres
SCREENS = {
    "27in": (597.0, 336.0),
    "15.6in": (345.4, 194.3),
    "55in": (1217.6, 684.9),
    "34in-uw": (796.6, 333.5),
}
DEFAULT_SCREEN = "27in"


# ---------------------------------------------------------------------------
# Self-checks


class SelfCheckFailed(AssertionError):
    """A self-check failed; the generator writes nothing."""


class Checks:
    def __init__(self):
        self.passed = 0
        self.where = "setup"

    def that(self, condition, what):
        if not condition:
            raise SelfCheckFailed(f"[{self.where}] {what}")
        self.passed += 1

    def close(self, actual, expected, what, scale=None):
        a = np.asarray(actual, dtype=float)
        e = np.asarray(expected, dtype=float)
        if a.shape != e.shape:
            raise SelfCheckFailed(f"[{self.where}] {what}: shape {a.shape} != {e.shape}")
        if not (np.all(np.isfinite(a)) and np.all(np.isfinite(e))):
            raise SelfCheckFailed(f"[{self.where}] {what}: not finite: {a.tolist()} vs {e.tolist()}")
        if scale is None:
            scale = max(1.0, float(np.max(np.abs(a))), float(np.max(np.abs(e))))
        error = float(np.max(np.abs(a - e)))
        self.that(
            error <= RTOL * scale,
            f"{what}: |difference| {error:.3e} > {RTOL:g} x {scale:.6g}\n"
            f"    got      {a.tolist()}\n    expected {e.tolist()}",
        )


CHECK = Checks()


# ---------------------------------------------------------------------------
# Unity rotation, camera basis, projection matrix


def axis_angle(axis, degrees):
    """Quaternion.AngleAxis as (x, y, z, w)."""
    half = math.radians(degrees) / 2
    return np.array([*(np.asarray(axis, dtype=float) * math.sin(half)), math.cos(half)])


def quat_mul(a, b):
    """Hamilton product of (x, y, z, w) quaternions, as Quaternion * Quaternion."""
    av, aw = a[:3], a[3]
    bv, bw = b[:3], b[3]
    return np.array([*(aw * bv + bw * av + np.cross(av, bv)), aw * bw - av @ bv])


def quat_rotate(q, v):
    conjugate = np.array([-q[0], -q[1], -q[2], q[3]])
    return quat_mul(quat_mul(q, np.array([*v, 0.0])), conjugate)[:3]


def quaternion_euler(pitch, yaw, roll=0.0):
    """Quaternion.Euler(pitch, yaw, roll): roll about z, then pitch about x, then yaw about y."""
    return quat_mul(
        axis_angle((0, 1, 0), yaw),
        quat_mul(axis_angle((1, 0, 0), pitch), axis_angle((0, 0, 1), roll)),
    )


def camera_basis(yaw, pitch):
    """(right, up, forward) of Quaternion.Euler(pitch, yaw, 0)."""
    y, p = math.radians(yaw), math.radians(pitch)
    forward = np.array([math.cos(p) * math.sin(y), -math.sin(p), math.cos(p) * math.cos(y)])
    right = np.array([math.cos(y), 0.0, -math.sin(y)])
    up = np.cross(forward, right)
    CHECK.close(np.cross(right, up), forward, "cross(r, u) == f")
    CHECK.close(up, [math.sin(p) * math.sin(y), math.cos(p), math.sin(p) * math.cos(y)], "u = f x r in closed form")
    q = quaternion_euler(pitch, yaw)
    CHECK.close(quat_rotate(q, (1, 0, 0)), right, "Quaternion.Euler(pitch, yaw, 0) * right")
    CHECK.close(quat_rotate(q, (0, 1, 0)), up, "Quaternion.Euler(pitch, yaw, 0) * up")
    CHECK.close(quat_rotate(q, (0, 0, 1)), forward, "Quaternion.Euler(pitch, yaw, 0) * forward")
    rows = np.array([right, up, forward])
    CHECK.close(rows @ rows.T, np.eye(3), "orthonormal basis")
    CHECK.that(pitch <= 0 or forward[1] < 0, "pitch > 0 looks down")
    return right, up, forward


def frustum(left, right, bottom, top, near, far):
    """Matrix4x4.Frustum(left, right, bottom, top, near, far)."""
    return np.array([
        [2 * near / (right - left), 0.0, (right + left) / (right - left), 0.0],
        [0.0, 2 * near / (top - bottom), (top + bottom) / (top - bottom), 0.0],
        [0.0, 0.0, -(far + near) / (far - near), -2 * far * near / (far - near)],
        [0.0, 0.0, -1.0, 0.0],
    ])


def unit(v):
    return v / np.linalg.norm(v)


def view_of(p, position, basis):
    """Homogeneous view-space point: x_v = (p-C).r, y_v = (p-C).u, z_v = -(p-C).f."""
    right, up, forward = basis
    d = np.asarray(p, dtype=float) - position
    return np.array([d @ right, d @ up, -(d @ forward), 1.0])


def ndc_of(matrix, position, basis, p, what):
    clip = matrix @ view_of(p, position, basis)
    CHECK.that(clip[3] > 0, f"{what}: in front of the camera (clip.w = {clip[3]:.6g})")
    return clip[:2] / clip[3]


# ---------------------------------------------------------------------------
# The window


class Window:
    """A vanilla camera and the window its frustum cuts at depth D."""

    def __init__(self, position, yaw, pitch, tans, near, far, screen_mm, depth):
        self.C = np.asarray(position, dtype=float)
        self.r, self.u, self.f = camera_basis(yaw, pitch)
        self.basis = (self.r, self.u, self.f)
        self.tans = tuple(float(t) for t in tans)
        tan_l, tan_r, tan_b, tan_t = self.tans
        self.n, self.F = float(near), float(far)
        self.Ws, self.Hs = (float(x) for x in screen_mm)
        self.D = float(depth)
        D = self.D
        self.W0 = self.C + D * self.f + D * ((tan_r + tan_l) / 2 * self.r + (tan_t + tan_b) / 2 * self.u)
        self.w = D * (tan_r - tan_l) / 2
        self.h = D * (tan_t - tan_b) / 2
        self.s = 2 * self.h / self.Hs
        self.z0 = D / self.s
        # P(e_n) = C  <=>  s (e.x r + e.y u - e.z f) = C - W0 = -D f - D (cx r + cy u)
        self.neutral_eye = np.array([
            -self.z0 * (tan_r + tan_l) / 2,
            -self.z0 * (tan_t + tan_b) / 2,
            self.z0,
        ]) + 0.0
        self.pa = self.W0 - self.w * self.r - self.h * self.u  # lower left
        self.pb = self.W0 + self.w * self.r - self.h * self.u  # lower right
        self.pc = self.W0 - self.w * self.r + self.h * self.u  # upper left
        self.pd = self.W0 + self.w * self.r + self.h * self.u  # upper right
        self.vanilla = frustum(*(self.n * np.array(self.tans)), self.n, self.F)

    def eye_to_world(self, eye_mm):
        e = np.asarray(eye_mm, dtype=float)
        return self.W0 + self.s * (e[0] * self.r + e[1] * self.u - e[2] * self.f)

    def world_to_eye(self, p):
        offset = np.asarray(p, dtype=float) - self.W0
        return np.array([offset @ self.r, offset @ self.u, -(offset @ self.f)]) / self.s

    def kooima(self, pe):
        """Kooima's extents (l, r, b, t) of the window seen from pe, at the near plane."""
        vr = unit(self.pb - self.pa)
        vu = unit(self.pc - self.pa)
        # Kooima's vn = vr x vu points at the viewer in a right-handed world. Here
        # np.cross(r, u) = f points into the screen, so the viewer's side is vu x vr.
        vn = unit(np.cross(vu, vr))
        va, vb, vc = self.pa - pe, self.pb - pe, self.pc - pe
        d = -(va @ vn)
        n = self.n
        extents = np.array([(vr @ va) * n / d, (vr @ vb) * n / d, (vu @ va) * n / d, (vu @ vc) * n / d])
        return extents, (vr, vu, vn, d)

    def extents_alt(self, pe):
        """The same extents from the window centre: Delta = W0 - pe."""
        delta = self.W0 - pe
        d, cx, cy = delta @ self.f, delta @ self.r, delta @ self.u
        n = self.n
        return np.array([n * (cx - self.w) / d, n * (cx + self.w) / d, n * (cy - self.h) / d, n * (cy + self.h) / d])

    def shifted(self, pe):
        """Extents, far plane and matrix of the camera at pe."""
        extents, _ = self.kooima(pe)
        far = self.F + float(np.linalg.norm(pe - self.C))
        return extents, far, frustum(*extents, self.n, far)

    def window_ndc(self, origin, p):
        """Where the ray origin -> p crosses the window plane, in window units (NDC)."""
        ray = np.asarray(p, dtype=float) - origin
        along = ray @ self.f
        CHECK.that(along > 0, "probe ahead of the camera")
        hit = origin + ((self.W0 - origin) @ self.f / along) * ray
        return np.array([(hit - self.W0) @ self.r / self.w, (hit - self.W0) @ self.u / self.h])

    def probes(self):
        C, D, f = self.C, self.D, self.f
        return [
            ("window_lower_left", self.pa),
            ("window_lower_right", self.pb),
            ("window_upper_left", self.pc),
            ("window_upper_right", self.pd),
            ("window_centre", self.W0),
            ("axis_half_depth", C + 0.5 * D * f),
            ("axis_twice_depth", C + 2.0 * D * f),
            ("axis_far_50D", C + FAR_PROBE * D * f),
        ]


WINDOW_NDC = {
    "window_lower_left": (-1.0, -1.0),
    "window_lower_right": (1.0, -1.0),
    "window_upper_left": (-1.0, 1.0),
    "window_upper_right": (1.0, 1.0),
    "window_centre": (0.0, 0.0),
}
NEARER = ("axis_half_depth",)
FARTHER = ("axis_twice_depth", "axis_far_50D")


def kooima_full(pe, projection, vr, vu, vn):
    """Kooima's complete matrix P M^T T(-pe)."""
    rotation = np.eye(4)
    rotation[0, :3], rotation[1, :3], rotation[2, :3] = vr, vu, vn
    translation = np.eye(4)
    translation[:3, 3] = -pe
    return projection @ rotation @ translation


def check_frustum_matrix(matrix, extents, near, far, what):
    left, right, bottom, top = extents
    for x, ndc_x in ((left, -1.0), (right, 1.0)):
        for y, ndc_y in ((bottom, -1.0), (top, 1.0)):
            clip = matrix @ np.array([x, y, -near, 1.0])
            CHECK.close(clip[:3] / clip[3], [ndc_x, ndc_y, -1.0], f"{what}: near-plane corner ({x:.6g}, {y:.6g})")
    for depth, ndc_z in ((near, -1.0), (far, 1.0)):
        clip = matrix @ np.array([0.3 * depth, -0.2 * depth, -depth, 1.0])
        CHECK.close(clip[2] / clip[3], ndc_z, f"{what}: NDC z = {ndc_z:+g} at depth {depth:g}")


def check_vanilla(win):
    tan_l, tan_r, tan_b, tan_t = win.tans
    CHECK.that(tan_l < 0 < tan_r and tan_b < 0 < tan_t, "tanL0 < 0 < tanR0 and tanB0 < 0 < tanT0")
    aspect = (tan_r - tan_l) / (tan_t - tan_b)
    CHECK.that(abs(aspect / (win.Ws / win.Hs) - 1) < 2e-3, "vanilla aspect = screen aspect within 0.2 %")
    CHECK.close(win.z0, win.Hs / (tan_t - tan_b), "z0 = Hs / (tanT0 - tanB0), whatever D")
    CHECK.close(win.neutral_eye, win.world_to_eye(win.C), "neutral eye: closed form = C in screen mm",
                scale=max(1.0, win.z0))
    if tan_l == -tan_r and tan_b == -tan_t:
        CHECK.close(win.neutral_eye, [0.0, 0.0, win.z0], "symmetric frustum: neutral eye on the screen axis")
    else:
        CHECK.that(math.hypot(win.neutral_eye[0], win.neutral_eye[1]) > 1.0,
                   "lens-shifted frustum: neutral eye off the screen centre")
    camera = win.eye_to_world(win.neutral_eye)
    CHECK.close(camera, win.C, "the neutral eye puts C' on C", scale=max(1.0, float(np.max(np.abs(win.C))), win.D))
    extents, far, matrix = win.shifted(camera)
    CHECK.close(extents, win.n * np.array(win.tans), "neutral eye: extents = n tan")
    CHECK.close(far, win.F, "neutral eye: far plane = F")
    CHECK.close(matrix, win.vanilla, "neutral eye: matrix = vanilla Frustum(n tanL0, n tanR0, n tanB0, n tanT0, n, F)")
    check_frustum_matrix(win.vanilla, win.n * np.array(win.tans), win.n, win.F, "vanilla matrix")


def check_shifted(win, camera, extents, far, matrix, plane_rng):
    _, (vr, vu, vn, d) = win.kooima(camera)
    CHECK.close(vr, win.r, "Kooima vr = r")
    CHECK.close(vu, win.u, "Kooima vu = u")
    CHECK.close(vn, -win.f, "Kooima vn = -f (the window normal towards the viewer)")
    CHECK.that(d > 0, f"C' on the viewer's side of the window (d = {d:.6g})")
    CHECK.close(extents, win.extents_alt(camera), "Kooima extents = Delta derivation")
    CHECK.that(extents[0] < extents[1] and extents[2] < extents[3], "l < r and b < t")
    CHECK.that(far + (camera - win.C) @ win.f >= win.F * (1 - RTOL),
               "F' = F + |C' - C|: the shifted far plane is never nearer than the vanilla one")
    check_frustum_matrix(matrix, extents, win.n, far, "shifted matrix")
    full = kooima_full(camera, matrix, vr, vu, vn)
    for _ in range(PLANE_POINTS):
        a, b = plane_rng.uniform(-2.0, 2.0, size=2)
        q = win.W0 + a * win.w * win.r + b * win.h * win.u
        from_vanilla = ndc_of(win.vanilla, win.C, win.basis, q, "window-plane point from C")
        from_shifted = ndc_of(matrix, camera, win.basis, q, "window-plane point from C'")
        CHECK.close(from_vanilla, [a, b], "window-plane point: vanilla NDC = its window coordinates")
        CHECK.close(from_shifted, from_vanilla, "window-plane point: same NDC from C and from C'")
        CHECK.close(full @ np.array([*q, 1.0]), matrix @ view_of(q, camera, win.basis),
                    "Kooima P M^T T(-pe) = Frustum x view space")
    return full


def probe_table(win, camera, matrix, full):
    rows = []
    for label, p in win.probes():
        from_vanilla = ndc_of(win.vanilla, win.C, win.basis, p, f"{label} from C")
        from_shifted = ndc_of(matrix, camera, win.basis, p, f"{label} from C'")
        CHECK.close(from_vanilla, win.window_ndc(win.C, p), f"{label}: vanilla NDC = ray from C through the window")
        CHECK.close(from_shifted, win.window_ndc(camera, p), f"{label}: shifted NDC = ray from C' through the window")
        CHECK.close(full @ np.array([*p, 1.0]), matrix @ view_of(p, camera, win.basis),
                    f"{label}: Kooima P M^T T(-pe) = Frustum x view space")
        if label in WINDOW_NDC:
            CHECK.close(from_shifted, WINDOW_NDC[label], f"{label}: NDC {WINDOW_NDC[label]} from C'")
            CHECK.close(from_vanilla, WINDOW_NDC[label], f"{label}: NDC {WINDOW_NDC[label]} from C")
        rows.append((label, p, from_vanilla, from_shifted))
    return rows


def check_eye(win, eye, camera, rows):
    CHECK.close(win.world_to_eye(camera), eye, "C' read back in screen mm is the eye", scale=max(1.0, win.z0))
    CHECK.that(eye[2] > win.z0 / 2, "the eye is farther from the screen than the half-depth probe")
    vanilla = {label: ndc for label, _, ndc, _ in rows}
    shifted = {label: ndc for label, _, _, ndc in rows}
    points = dict((label, p) for label, p, _, _ in rows)

    # Parallax: step the eye +x (+y); nearer points move -x (-y), farther ones +x (+y).
    for axis, name in ((0, "x"), (1, "y")):
        step = np.zeros(3)
        step[axis] = EYE_STEP_MM
        camera2 = win.eye_to_world(eye + step)
        _, _, matrix2 = win.shifted(camera2)
        for label, p in points.items():
            moved = ndc_of(matrix2, camera2, win.basis, p, f"{label} from the stepped C'") - shifted[label]
            if label in WINDOW_NDC:
                CHECK.close(moved, [0.0, 0.0], f"eye +{name}: {label} stays put")
            elif label in NEARER:
                CHECK.that(moved[axis] < 0, f"eye +{name}: nearer {label} moves to -{name} ({moved[axis]:.3g})")
            else:
                CHECK.that(moved[axis] > 0, f"eye +{name}: farther {label} moves to +{name} ({moved[axis]:.3g})")

    # From the neutral eye along one screen axis only: compare with the vanilla camera.
    offset = eye - win.neutral_eye
    for axis, name in ((0, "x"), (1, "y")):
        other = 1 - axis
        if abs(offset[axis]) > 1.0 and abs(offset[other]) < 1e-9 and abs(offset[2]) < 1e-9:
            sign = math.copysign(1.0, offset[axis])
            for label in NEARER + FARTHER:
                moved = shifted[label] - vanilla[label]
                wanted = -sign if label in NEARER else sign
                CHECK.that(wanted * moved[axis] > 0, f"eye {sign:+g}{name}: {label} moves {wanted:+g}{name}")
                CHECK.close(moved[other], 0.0, f"eye along {name} only: {label} keeps its other NDC")

    # The far probe sits at (e_n.x, e_n.y, -49 z0) in screen mm: from the eye it shows
    # k (e - e_n) away from where the vanilla camera shows it, k = 49 z0 / (e.z + 49 z0).
    k = (FAR_PROBE - 1) * win.z0 / (eye[2] + (FAR_PROBE - 1) * win.z0)
    CHECK.that(0.9 < k < 1.0, f"far probe moves almost 1:1 with the eye (k = {k:.4f})")
    moved_ndc = shifted["axis_far_50D"] - vanilla["axis_far_50D"]
    moved_mm = moved_ndc * np.array([win.w, win.h]) / win.s
    CHECK.close(moved_mm, k * offset[:2], "far probe: screen-mm shift = k (e - e_n)")
    physical_mm = moved_ndc * np.array([win.Ws, win.Hs]) / 2  # NDC +-1 at the screen's edges
    CHECK.that(bool(np.all(np.abs(physical_mm - moved_mm) <= 2e-3 * np.abs(moved_mm) + 1e-9)),
               "far probe: physical-mm shift matches to the screen/frustum aspect mismatch")


# ---------------------------------------------------------------------------
# Cases

NEUTRAL = ("neutral",)


def eye(x, y, z):
    return ("eye", (float(x), float(y), float(z)))


def at_z0(x, y):
    return ("at_z0", (float(x), float(y)))


def neutral_plus(dx, dy, dz):
    return ("neutral_plus", (float(dx), float(dy), float(dz)))


def lifted(placement, metres):
    """C' overridden: the eye's position raised along world +y."""
    return ("lifted", placement, float(metres))


def eye_mm(placement, win):
    kind = placement[0]
    if kind == "neutral":
        return win.neutral_eye.copy()
    if kind == "eye":
        return np.array(placement[1])
    if kind == "at_z0":
        return np.array([placement[1][0], placement[1][1], win.z0])
    if kind == "neutral_plus":
        return win.neutral_eye + np.array(placement[1])
    raise ValueError(f"no eye for {placement!r}")


def describe(placement):
    kind = placement[0]
    if kind == "neutral":
        return "eye=neutral"
    if kind == "eye":
        return "eye({:g},{:g},{:g})".format(*placement[1])
    if kind == "at_z0":
        return "eye({:g},{:g},z0)".format(*placement[1])
    if kind == "neutral_plus":
        return "eye=neutral+({:g},{:g},{:g})".format(*placement[1])
    if kind == "lifted":
        return "override=({})+{:g}m_up".format(describe(placement[1]), placement[2])
    raise ValueError(f"unknown placement {placement!r}")


def case(frustum_name, pitch, yaw, depth, placement, near=NEAR, screen=DEFAULT_SCREEN, origin=False, tag=""):
    return {
        "frustum": frustum_name,
        "pitch": float(pitch),
        "yaw": float(yaw),
        "depth": float(depth),
        "placement": placement,
        "near": float(near),
        "far": FAR,
        "screen": screen,
        "origin": origin,
        "tag": tag,
    }


def case_name(spec):
    parts = [spec["tag"]] if spec["tag"] else []
    parts.append(spec["frustum"])
    if spec["screen"] != DEFAULT_SCREEN:
        parts.append(spec["screen"])
    parts += [f"p{spec['pitch']:g}", f"y{spec['yaw']:g}", f"D{spec['depth']:g}"]
    if spec["near"] != NEAR:
        parts.append(f"n{spec['near']:g}")
    if spec["origin"]:
        parts.append("C=0")
    parts.append(describe(spec["placement"]))
    return "_".join(parts)


def fixed_cases():
    game = "fov37.4296"
    shift_a = "fov37.4296+shift(0.08,-0.05)"
    shift_b = "fov30+shift(-0.22,0.18)"
    ultrawide = "uw-fov40+shift(0.1,0.12)"
    origin = {"origin": True}
    return [
        # The game's camera at the origin, 30 deg down: one eye axis at a time.
        case(game, 30, 0, 100, NEUTRAL, **origin),
        case(game, 30, 0, 100, at_z0(100, 0), **origin),
        case(game, 30, 0, 100, at_z0(-100, 0), **origin),
        case(game, 30, 0, 100, at_z0(300, 0), **origin),
        case(game, 30, 0, 100, at_z0(-300, 0), **origin),
        case(game, 30, 0, 100, at_z0(0, 50), **origin),
        case(game, 30, 0, 100, at_z0(0, -50), **origin),
        case(game, 30, 0, 100, at_z0(0, 150), **origin),
        case(game, 30, 0, 100, at_z0(0, -150), **origin),
        case(game, 30, 0, 100, eye(0, 0, 350), **origin),
        case(game, 30, 0, 100, eye(0, 0, 900), **origin),
        case(game, 30, 0, 100, eye(100, -50, 600), **origin),
        # Symmetric frusta over pitch, yaw and depth.
        case("fov30", 5, 45, 15, NEUTRAL),
        case("fov30", 5, 45, 15, eye(100, 50, 700)),
        case("fov30", 42.6, -30, 250, eye(-300, -150, 650)),
        case("fov30", 60, 137.5, 1200, eye(300, 150, 900)),
        case("fov30", 89, 210, 3000, at_z0(-100, 0)),
        case("fov60", 30, -160, 40, NEUTRAL),
        case("fov60", 30, -160, 40, at_z0(300, 0)),
        case("fov60", 5, 300, 600, eye(-300, 50, 350)),
        case("fov60", 42.6, 12.34, 1200, eye(100, -150, 450)),
        case("fov60", 60, 90, 3000, eye(0, 150, 800)),
        case("fov60", 89, -90, 100, eye(-100, -50, 500)),
        case(game, 5, 180, 15, eye(300, -50, 550)),
        case(game, 42.6, -45, 600, NEUTRAL),
        case(game, 42.6, -45, 600, eye(-100, 150, 400)),
        case(game, 60, 270, 250, at_z0(100, 50)),
        case(game, 89, 33, 1200, eye(-300, -150, 900)),
        case(game, 42.6, 75, 3000, at_z0(300, 0)),
        # Near plane 0.3 m.
        case("fov60", 30, 20, 15, eye(-100, 50, 600), near=0.3),
        case(game, 60, -120, 40, NEUTRAL, near=0.3),
        case(game, 60, -120, 40, eye(300, 150, 500), near=0.3),
        # Lens-shifted frusta: the neutral eye is off the screen centre, and the
        # centred eye (0, 0, z0) is not neutral.
        case(shift_a, 30, 60, 100, NEUTRAL),
        case(shift_a, 30, 60, 100, at_z0(0, 0)),
        case(shift_a, 30, 60, 100, neutral_plus(100, 0, 0)),
        case(shift_a, 42.6, -75, 600, neutral_plus(-300, 0, 0)),
        case(shift_a, 5, 150, 15, eye(100, -50, 600)),
        case(shift_a, 89, 0, 3000, eye(-300, 150, 750)),
        case(shift_b, 60, -15, 250, NEUTRAL),
        case(shift_b, 60, -15, 250, at_z0(0, 0)),
        case(shift_b, 42.6, 200, 1200, neutral_plus(0, -150, 0)),
        case(shift_b, 30, 95, 40, eye(300, -150, 400)),
        case(shift_b, 5, -60, 3000, neutral_plus(100, 50, 250)),
        # Eyes outside the screen rectangle.
        case(game, 42.6, 10, 600, eye(400, 0, 600)),
        case("fov60", 30, -100, 250, eye(-650, 120, 700)),
        case(shift_a, 60, 45, 100, eye(500, -250, 800)),
        case("fov30", 5, 170, 40, eye(-400, -200, 350)),
        # Other screens.
        case(game, 30, 30, 250, eye(100, -50, 600), screen="15.6in"),
        case(game, 42.6, -150, 100, NEUTRAL, screen="15.6in"),
        case(game, 42.6, 80, 600, eye(-300, 150, 900), screen="55in"),
        case(ultrawide, 30, -35, 500, eye(300, -50, 650), screen="34in-uw"),
        case(ultrawide, 60, 110, 1200, NEUTRAL, screen="34in-uw"),
        # The caller overrides C' (lifted above terrain); the window stays put.
        case(game, 42.6, 30, 600, lifted(eye(100, -50, 600), 30)),
        case(game, 42.6, 30, 600, lifted(eye(100, -50, 600), 120)),
        case("fov60", 5, -70, 15, lifted(eye(-300, 0, 450), 30)),
        case("fov60", 5, -70, 15, lifted(NEUTRAL, 120)),
        case(shift_a, 89, 250, 250, lifted(eye(300, 150, 800), 120)),
        case(shift_b, 30, 15, 3000, lifted(NEUTRAL, 30)),
    ]


def mixed_cases(rng, count=8):
    names = ["fov30", "fov37.4296", "fov60", "fov37.4296+shift(0.08,-0.05)", "fov30+shift(-0.22,0.18)"]
    pitches = [5.0, 30.0, 42.6, 60.0, 89.0]
    cases = []
    for i in range(count):
        frustum_name = names[int(rng.integers(len(names)))]
        pitch = pitches[int(rng.integers(len(pitches)))]
        yaw = round(float(rng.uniform(-180.0, 180.0)), 1) + 0.0
        depth = round(math.exp(float(rng.uniform(math.log(15.0), math.log(3000.0)))) * 2) / 2
        x, y, z = (round(float(v) * 2) / 2 + 0.0 for v in rng.uniform([-650, -250, 350], [650, 250, 900]))
        cases.append(case(frustum_name, pitch, yaw, depth, eye(x, y, z), tag=f"mixed{i + 1}"))
    return cases


def random_position(rng):
    x, z = rng.uniform(-7000.0, 7000.0, size=2)
    y = rng.uniform(20.0, 2500.0)
    return np.round(np.array([x, y, z]) * 8) / 8  # exact in float32 as well


def floats(values):
    return [float(v) + 0.0 for v in np.asarray(values, dtype=float).reshape(-1)]


def solve(spec, position, plane_rng):
    CHECK.where = spec["name"]
    win = Window(position, spec["yaw"], spec["pitch"], FRUSTA[spec["frustum"]], spec["near"], spec["far"],
                 SCREENS[spec["screen"]], spec["depth"])
    check_vanilla(win)

    placement = spec["placement"]
    if placement[0] == "lifted":
        the_eye = None
        camera = win.eye_to_world(eye_mm(placement[1], win)) + np.array([0.0, placement[2], 0.0])
    else:
        the_eye = eye_mm(placement, win)
        camera = win.eye_to_world(the_eye)

    extents, far, matrix = win.shifted(camera)
    full = check_shifted(win, camera, extents, far, matrix, plane_rng)
    rows = probe_table(win, camera, matrix, full)
    if the_eye is not None:
        check_eye(win, the_eye, camera, rows)

    tan_l, tan_r, tan_b, tan_t = win.tans
    left, right, bottom, top = (float(x) for x in extents)
    return {
        "name": spec["name"],
        "camera": {
            "position": floats(win.C),
            "right": floats(win.r),
            "up": floats(win.u),
            "forward": floats(win.f),
            "near": win.n,
            "far": win.F,
            "tan": {"left": tan_l, "right": tan_r, "bottom": tan_b, "top": tan_t},
        },
        "screen_mm": [win.Ws, win.Hs],
        "depth": win.D,
        "eye_mm": floats(the_eye) if the_eye is not None else None,
        "camera_override": floats(camera) if the_eye is None else None,
        "expected": {
            "position": floats(camera),
            "left": left,
            "right": right,
            "bottom": bottom,
            "top": top,
            "far": float(far),
            "scale_m_per_mm": float(win.s),
            "z0_mm": float(win.z0),
            "neutral_eye_mm": floats(win.neutral_eye),
            "window_centre": floats(win.W0),
            "matrix": floats(matrix),
            "probes": [
                {"label": label, "world": floats(p), "ndc_vanilla": floats(nv), "ndc_shifted": floats(ns)}
                for label, p, nv, ns in rows
            ],
        },
    }


def check_coverage(specs, records):
    CHECK.where = "coverage"
    CHECK.that(len(records) >= 40, f"at least 40 cases ({len(records)})")
    names = [r["name"] for r in records]
    CHECK.that(len(set(names)) == len(names), "case names are unique")
    CHECK.that({5.0, 30.0, 42.6, 60.0, 89.0} <= {s["pitch"] for s in specs}, "pitches 5, 30, 42.6, 60 and 89")
    depths = [s["depth"] for s in specs]
    CHECK.that(min(depths) == 15.0 and max(depths) == 3000.0, "D from 15 m to 3000 m")
    CHECK.that(any(s["near"] == 0.3 for s in specs), "a near 0.3 m case")
    for name in FRUSTA:
        CHECK.that(any(s["frustum"] == name and s["placement"] == NEUTRAL for s in specs), f"a neutral eye on {name}")
    lifts = {s["placement"][2] for s in specs if s["placement"][0] == "lifted"}
    CHECK.that({30.0, 120.0} <= lifts, "camera_override lifted by 30 m and by 120 m")
    CHECK.that(any(r["eye_mm"] is not None and abs(r["eye_mm"][0]) > r["screen_mm"][0] / 2 for r in records),
               "an eye outside the screen rectangle")
    for r in records:
        CHECK.that((r["eye_mm"] is None) != (r["camera_override"] is None), f"{r['name']}: eye_mm xor camera_override")
        CHECK.that(len(r["expected"]["matrix"]) == 16 and len(r["expected"]["probes"]) == 8, f"{r['name']}: shape")


# ---------------------------------------------------------------------------
# JSON: pretty-printed, number arrays on one line, floats by repr


def scalar(value):
    return json.dumps(value, allow_nan=False)


def dump(value, indent=0):
    pad, inner = "  " * indent, "  " * (indent + 1)
    if isinstance(value, dict):
        items = ",\n".join(f"{inner}{json.dumps(key)}: {dump(item, indent + 1)}" for key, item in value.items())
        return "{\n" + items + "\n" + pad + "}"
    if isinstance(value, list):
        if all(isinstance(item, float) for item in value):
            return "[" + ", ".join(scalar(item) for item in value) + "]"
        items = ",\n".join(inner + dump(item, indent + 1) for item in value)
        return "[\n" + items + "\n" + pad + "]"
    return scalar(value)


def reject_constant(token):
    raise SelfCheckFailed(f"[json] non-finite number {token}")


def main():
    root = Path(__file__).resolve().parents[2]
    rng = np.random.default_rng(SEED)
    plane_rng = np.random.default_rng([SEED, 1])

    specs = fixed_cases() + mixed_cases(rng)
    for spec in specs:
        spec["name"] = case_name(spec)
    records = []
    for spec in specs:
        position = random_position(rng)
        records.append(solve(spec, np.zeros(3) if spec["origin"] else position, plane_rng))
    check_coverage(specs, records)

    data = {"generator": GENERATOR, "units": UNITS, "cases": records}
    text = dump(data) + "\n"
    CHECK.where = "json"
    CHECK.that(json.loads(text, parse_constant=reject_constant) == data, "the JSON text reads back as the data")

    output = root / OUTPUT
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(text, encoding="utf-8")

    neutral = sum(1 for s in specs if s["placement"] == NEUTRAL)
    overrides = sum(1 for r in records if r["camera_override"] is not None)
    shifted = sum(1 for s in specs if "shift" in s["frustum"])
    probes = sum(len(r["expected"]["probes"]) for r in records)
    print(f"{GENERATOR}: {len(records)} cases ({neutral} neutral eye, {overrides} camera_override, "
          f"{shifted} lens-shifted), {probes} probes -> {OUTPUT}; all {CHECK.passed} self-checks passed")


if __name__ == "__main__":
    try:
        main()
    except SelfCheckFailed as failure:
        print(f"{GENERATOR}: SELF-CHECK FAILED, nothing written\n{failure}", file=sys.stderr)
        sys.exit(1)
