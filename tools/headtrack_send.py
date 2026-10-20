#!/usr/bin/env python3
"""Test sender for the HeadTrack mod: head poses as tobii-opentrack sends them,
and commands for the mod's debug channel.

Poses are OpenTrack "UDP over network" packets (six little-endian doubles:
X, Y, Z in cm, yaw, pitch, roll in degrees) sent at 33 Hz, built from an eye
position in mm in the display frame (+x right, +y up, +z towards the user):
TX = -x/10, TY = y/10, TZ = z/10, as tobii-opentrack does.

  headtrack_send.py static --eye 0 0 600 --seconds 5
  headtrack_send.py sweep --axis x --amp 80 --period 4 --seconds 20
  headtrack_send.py circle --radius 60 --period 5 --seconds 20
  headtrack_send.py cmd dump
  headtrack_send.py scenario basic        # poses + screenshots, see SCENARIOS

Only the standard library is used.
"""

import argparse
import json
import math
import socket
import struct
import sys
import threading
import time

RATE_HZ = 33.0


def packet(eye_mm):
    """The 48-byte packet for an eye position in mm."""
    x, y, z = eye_mm
    return struct.pack("<6d", -x / 10.0, y / 10.0, z / 10.0, 0.0, 0.0, 0.0)


class PoseStream:
    """Sends the current target eye at RATE_HZ from a background thread; None sends nothing (face lost)."""

    def __init__(self, host, port, rate=RATE_HZ):
        self.address = (host, port)
        self.period = 1.0 / rate
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.lock = threading.Lock()
        self.target = None
        self.glide_from = None
        self.glide_start = 0.0
        self.glide_seconds = 0.0
        self.running = True
        self.sent = 0
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def set(self, eye, glide_seconds=0.0):
        with self.lock:
            current = self.current()
            self.glide_from = current if (glide_seconds > 0 and current is not None and eye is not None) else None
            self.glide_start = time.monotonic()
            self.glide_seconds = glide_seconds
            self.target = None if eye is None else tuple(float(v) for v in eye)

    def current(self):
        if self.target is None or self.glide_from is None:
            return self.target
        progress = (time.monotonic() - self.glide_start) / self.glide_seconds
        if progress >= 1.0:
            return self.target
        s = progress * progress * (3 - 2 * progress)
        return tuple(a + (b - a) * s for a, b in zip(self.glide_from, self.target))

    def run(self):
        next_send = time.monotonic()
        while self.running:
            with self.lock:
                eye = self.current()
            if eye is not None:
                self.socket.sendto(packet(eye), self.address)
                self.sent += 1
            next_send += self.period
            time.sleep(max(0.0, next_send - time.monotonic()))

    def stop(self):
        self.running = False
        self.thread.join(1.0)


class DebugClient:
    """Sends one command to the mod's debug channel and waits for its reply."""

    def __init__(self, port, host="127.0.0.1"):
        self.address = (host, port)
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.socket.bind(("127.0.0.1", 0))

    def send(self, text, timeout=30.0):
        self.socket.settimeout(timeout)
        self.socket.sendto(text.encode("utf-8"), self.address)
        try:
            reply, _ = self.socket.recvfrom(65535)
        except socket.timeout:
            return f"timeout waiting for a reply to '{text}'"
        return reply.decode("utf-8", errors="replace")


def linux_path(wine_path):
    """Z:\\home\\... -> /home/... for paths the mod reports."""
    if len(wine_path) > 2 and wine_path[1] == ":" and wine_path[0].upper() == "Z":
        return wine_path[2:].replace("\\", "/")
    return wine_path


REST = (0.0, 0.0, 600.0)

# Each step: {"eye": [x, y, z] | null, "glide": s, "hold": s} moves the head and waits;
# {"cmd": "..."} sends a debug command and prints the reply.
SCENARIOS = {
    "basic": [
        {"eye": REST, "hold": 1.5},
        {"cmd": "centre"},
        {"eye": REST, "hold": 1.5},
        {"cmd": "pause 1"},
        {"cmd": "dump"},
        {"cmd": "shot a_rest"},
        {"eye": [100, 0, 600], "glide": 0.5, "hold": 1.5},
        {"cmd": "shot b_right100"},
        {"eye": [-100, 0, 600], "glide": 0.8, "hold": 1.5},
        {"cmd": "shot c_left100"},
        {"eye": [0, 50, 600], "glide": 0.5, "hold": 1.5},
        {"cmd": "shot d_up50"},
        {"eye": [0, -50, 600], "glide": 0.5, "hold": 1.5},
        {"cmd": "shot e_down50"},
        {"eye": [0, 0, 500], "glide": 0.5, "hold": 1.5},
        {"cmd": "shot f_near100"},
        {"eye": [0, 0, 700], "glide": 0.5, "hold": 1.5},
        {"cmd": "shot g_far100"},
        {"eye": REST, "glide": 0.5, "hold": 1.0},
        {"cmd": "set gain 4"},
        {"eye": [50, 0, 600], "glide": 0.5, "hold": 1.5},
        {"cmd": "shot h_gain4_right50"},
        {"cmd": "set path physical"},
        {"eye": [50, 0, 600], "hold": 1.0},
        {"cmd": "shot i_gain4_right50_physical"},
        {"cmd": "dump"},
        {"cmd": "set path explicit"},
        {"cmd": "set gain 1"},
        {"eye": None, "hold": 2.0},
        {"cmd": "shot j_absent"},
        {"eye": REST, "hold": 1.5},
        {"cmd": "enable 0"},
        {"eye": REST, "hold": 1.0},
        {"cmd": "shot k_disabled"},
        {"cmd": "enable 1"},
        {"cmd": "pause 0"},
        {"cmd": "dump"},
    ],
}


def run_scenario(stream, client, steps):
    for step in steps:
        if "cmd" in step:
            reply = client.send(step["cmd"])
            print(f"> {step['cmd']}\n< {reply}", flush=True)
            continue
        stream.set(step.get("eye"), step.get("glide", 0.0))
        time.sleep(step.get("glide", 0.0) + step.get("hold", 0.0))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=4242, help="the mod's pose port")
    parser.add_argument("--debug-port", type=int, default=4250, help="the mod's debug port")
    sub = parser.add_subparsers(dest="command", required=True)

    static = sub.add_parser("static", help="hold one eye position")
    static.add_argument("--eye", type=float, nargs=3, default=list(REST))
    static.add_argument("--seconds", type=float, default=10.0)

    sweep = sub.add_parser("sweep", help="sinusoidal motion along one axis")
    sweep.add_argument("--axis", choices="xyz", default="x")
    sweep.add_argument("--amp", type=float, default=80.0, help="amplitude, mm")
    sweep.add_argument("--period", type=float, default=4.0, help="seconds")
    sweep.add_argument("--seconds", type=float, default=20.0)
    sweep.add_argument("--centre", type=float, nargs=3, default=list(REST))

    circle = sub.add_parser("circle", help="circular motion in the screen plane")
    circle.add_argument("--radius", type=float, default=60.0)
    circle.add_argument("--period", type=float, default=5.0)
    circle.add_argument("--seconds", type=float, default=20.0)
    circle.add_argument("--centre", type=float, nargs=3, default=list(REST))

    absent = sub.add_parser("absent", help="send nothing, as when the face is lost")
    absent.add_argument("--seconds", type=float, default=2.0)

    cmd = sub.add_parser("cmd", help="send one debug command")
    cmd.add_argument("text", nargs="+")

    scenario = sub.add_parser("scenario", help="run a scenario: a name in SCENARIOS or a JSON file of steps")
    scenario.add_argument("name")

    args = parser.parse_args()

    if args.command == "cmd":
        print(DebugClient(args.debug_port, args.host).send(" ".join(args.text)))
        return 0

    stream = PoseStream(args.host, args.port)
    try:
        if args.command == "static":
            stream.set(args.eye)
            time.sleep(args.seconds)
        elif args.command == "absent":
            stream.set(None)
            time.sleep(args.seconds)
        elif args.command in ("sweep", "circle"):
            start = time.monotonic()
            while time.monotonic() - start < args.seconds:
                phase = 2 * math.pi * (time.monotonic() - start) / args.period
                eye = list(args.centre)
                if args.command == "sweep":
                    eye["xyz".index(args.axis)] += args.amp * math.sin(phase)
                else:
                    eye[0] += args.radius * math.cos(phase)
                    eye[1] += args.radius * math.sin(phase)
                stream.set(eye)
                time.sleep(0.005)
        elif args.command == "scenario":
            if args.name in SCENARIOS:
                steps = SCENARIOS[args.name]
            else:
                with open(args.name, encoding="utf-8") as f:
                    steps = json.load(f)
            run_scenario(stream, DebugClient(args.debug_port, args.host), steps)
    finally:
        stream.stop()
    print(f"sent {stream.sent} poses", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
