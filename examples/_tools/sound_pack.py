"""Builds examples/sound_pack: soft chiptune sounds for every event name, a seamless rain loop, the bite cue (a 4-frame
strip and a short sound). All synthesised here: 44.1 kHz mono, faded in and out (no click), every event sound brought
to the same loudness (RMS of its audible part) with the peak capped well below full scale, then encoded to Ogg
Vorbis. `python sound_pack.py` builds; `python sound_pack.py measure` prints peak and RMS of the encoded files.
Needs numpy, Pillow (for the cue strip) and ffmpeg with libvorbis (on PATH, or set FFMPEG)."""
import os
import struct
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np

from common import EXAMPLES, from_rows, hexrgb, save

MOD = EXAMPLES / "sound_pack"
RATE = 44100
FFMPEG = os.environ.get("FFMPEG", "ffmpeg")  # with libvorbis
TARGET_RMS_DB = -26.0  # loudness of the audible part of every event sound
PEAK_CAP_DB = -10.0  # Vorbis may add about 0.5 dB to a peak
LOOP_RMS_DB = -30.0
RNG = np.random.default_rng(1999)


def t_of(seconds):
    return np.arange(int(seconds * RATE)) / RATE


def note(n):
    """Frequency of a note name like 'A4', 'C#6'."""
    names = {"C": -9, "C#": -8, "D": -7, "D#": -6, "E": -5, "F": -4, "F#": -3, "G": -2, "G#": -1, "A": 0, "A#": 1, "B": 2}
    return 440.0 * 2 ** ((names[n[:-1]] + 12 * (int(n[-1]) - 4)) / 12)


def env(n, attack, release, decay=None):
    """Attack ramp, then an exponential decay (time constant `decay`) or a hold, and a release ramp to exactly 0."""
    e = np.ones(n)
    a = max(1, int(attack * RATE))
    e[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 2
    if decay:
        e[a:] = np.exp(-np.arange(n - a) / (decay * RATE))
    r = max(1, int(release * RATE))
    e[-r:] *= np.cos(np.linspace(0, np.pi / 2, r)) ** 2
    return e


def lowpass(x, cutoff):
    """Gentle one-pole low-pass (twice), takes the edge off square and triangle waves."""
    a = np.exp(-2 * np.pi * cutoff / RATE)
    for _ in range(2):
        y = np.empty_like(x)
        acc = 0.0
        for i, v in enumerate(x):
            acc = (1 - a) * v + a * acc
            y[i] = acc
        x = y
    return x


def tri(f, t):
    return 2 / np.pi * np.arcsin(np.sin(2 * np.pi * f * t))


def pulse(f, t, duty=0.5):
    return np.where((f * t) % 1.0 < duty, 1.0, -1.0)


def bell(f, seconds, decay):
    t = t_of(seconds)
    x = np.sin(2 * np.pi * f * t) + 0.25 * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t / (decay * 0.4))
    x += 0.08 * np.sin(2 * np.pi * f * 3.01 * t) * np.exp(-t / (decay * 0.25))
    return x * env(len(t), 0.003, 0.02, decay)


def seq(parts, total):
    """Mixes (start seconds, signal) parts into one buffer `total` seconds long."""
    out = np.zeros(int(total * RATE))
    for start, x in parts:
        i = int(start * RATE)
        n = min(len(x), len(out) - i)
        out[i : i + n] += x[:n]
    return out


def echo(x, delay, gain, times=3):
    out = np.concatenate([x, np.zeros(int(delay * RATE * times))])
    for k in range(1, times + 1):
        i = int(delay * RATE * k)
        out[i : i + len(x)] += x * gain**k
    return out


# ---- the sounds ----

def wallet_gain():  # two bright coin chimes, up a fourth
    return seq([(0.0, bell(note("E6"), 0.25, 0.07)), (0.07, bell(note("A6"), 0.33, 0.1))], 0.42)


def wallet_loss():  # the same coin, two duller notes going down
    x = seq([(0.0, bell(note("A5"), 0.22, 0.06)), (0.08, bell(note("E5"), 0.3, 0.08))], 0.4)
    return lowpass(x, 2500)


def item_gained():  # a bubbly pop: a sine sweeping up with a quick decay, and a soft second blip
    t = t_of(0.16)
    f = 380 + 900 * (1 - np.exp(-t / 0.03))
    pop = np.sin(2 * np.pi * np.cumsum(f) / RATE) * env(len(t), 0.002, 0.02, 0.045)
    t2 = t_of(0.12)
    blip = tri(note("E6"), t2) * env(len(t2), 0.002, 0.02, 0.03) * 0.35
    return seq([(0.0, pop), (0.06, blip)], 0.22)


def whisper():  # two soft triangle notes, a little secret
    t = t_of(0.14)
    a = tri(note("A5"), t) * env(len(t), 0.004, 0.03, 0.06)
    b = tri(note("D6"), t) * env(len(t), 0.004, 0.03, 0.07)
    return lowpass(seq([(0.0, a), (0.09, b)], 0.26), 3500)


def chat():  # a tiny wooden tick
    t = t_of(0.05)
    tone = np.sin(2 * np.pi * 1550 * t) + 0.4 * np.sin(2 * np.pi * 2480 * t)
    noise = lowpass(RNG.standard_normal(len(t)), 3000) * 0.6
    return (tone + noise) * env(len(t), 0.003, 0.01, 0.009)


def arpeggio(notes, step, length, wave_fn, cutoff, tail):
    parts = []
    for i, n in enumerate(notes):
        t = t_of(length)
        parts.append((i * step, wave_fn(note(n), t) * env(len(t), 0.006, 0.05, length * 0.45)))
    x = seq(parts, step * (len(notes) - 1) + length)
    return lowpass(echo(x, tail, 0.3, 2), cutoff)


def zone_town():  # a bright little arrival fanfare (C major)
    return arpeggio(["C5", "E5", "G5", "C6"], 0.11, 0.4, lambda f, t: pulse(f, t, 0.25) * 0.5 + tri(f, t) * 0.5, 3200, 0.16)


def zone_playerland():  # home: a slow, warm G major arpeggio
    return arpeggio(["G4", "B4", "D5", "G5", "D5"], 0.15, 0.5, tri, 2400, 0.2)


def zone_holyland():  # the well islet: airy, suspended, with a shimmer
    t = t_of(1.8)
    x = np.zeros(len(t))
    for i, n in enumerate(["A4", "E5", "B5", "F#6"]):
        f = note(n)
        part = np.sin(2 * np.pi * f * t) * (1 + 0.004 * np.sin(2 * np.pi * 5 * t))
        x += part * 0.25 * env(len(t), 0.25 + 0.08 * i, 0.5, 0.7)
    sparkle = arpeggio(["E6", "B6", "E7"], 0.12, 0.25, tri, 5000, 0.15)
    return seq([(0.0, x), (0.35, sparkle * 0.35)], 1.8)


def drops(n, seconds, wrap=False, rate_per_s=6.0, level=0.5):
    """Raindrop plinks at random times (wrapped around the end for a loop)."""
    out = np.zeros(n)
    count = int(rate_per_s * seconds)
    for _ in range(count):
        start = RNG.integers(0, n)
        f = RNG.uniform(1800, 3600)
        t = t_of(0.03)
        d = np.sin(2 * np.pi * (f + 3000 * t) * t) * env(len(t), 0.001, 0.005, 0.006) * RNG.uniform(0.3, 1.0) * level
        idx = (start + np.arange(len(d))) % n if wrap else start + np.arange(len(d))
        ok = idx < n
        np.add.at(out, idx[ok], d[ok])
    return out


def rain_bed(n):
    """Soft rain noise, shaped in the frequency domain, so it is periodic over n samples (a seamless loop)."""
    spec = np.fft.rfft(RNG.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / RATE)
    shape = 1 / np.sqrt(np.maximum(f, 20))  # pink-ish
    shape *= 1 / (1 + (f / 4000) ** 4)  # roll off the hiss
    shape *= (f / 250) ** 2 / (1 + (f / 250) ** 2)  # and the rumble
    return np.fft.irfft(spec * shape, n)


def rain_loop():
    n = int(8.0 * RATE)
    bed = rain_bed(n)
    bed /= np.sqrt(np.mean(bed**2))
    # A slow swell, periodic over the loop, so it breathes instead of sounding like static.
    t = np.arange(n) / RATE
    bed *= 1 + 0.15 * np.sin(2 * np.pi * t / 8.0) + 0.08 * np.sin(2 * np.pi * 3 * t / 8.0)
    return bed + drops(n, 8.0, wrap=True, rate_per_s=5.0, level=1.2)


def rain_start():  # the rain fading in, first drops
    n = int(1.6 * RATE)
    bed = rain_bed(n)
    bed /= np.sqrt(np.mean(bed**2))
    swell = np.linspace(0, 1, n) ** 1.5
    return (bed * swell + drops(n, 1.6, rate_per_s=7, level=1.2)) * env(n, 0.05, 0.3)


def rain_stop():  # the rain dying away, last drops
    n = int(1.6 * RATE)
    bed = rain_bed(n)
    bed /= np.sqrt(np.mean(bed**2))
    fade = np.linspace(1, 0, n) ** 1.5
    return (bed * fade + drops(n, 1.0, rate_per_s=5, level=1.0)) * env(n, 0.05, 0.3)


def two_step(a, b):
    t = t_of(0.07)
    w = lambda f: (pulse(f, t, 0.5) * 0.4 + tri(f, t) * 0.6) * env(len(t), 0.003, 0.02, 0.05)
    return lowpass(seq([(0.0, w(note(a))), (0.045, w(note(b)))], 0.13), 2800)


def ui_open():
    return two_step("G5", "D6")


def ui_close():
    return two_step("D6", "G5")


def fishing_bite():  # a quick bright "plink-plink!" over the water
    parts = [(0.0, bell(note("G6"), 0.12, 0.04)), (0.06, bell(note("C7"), 0.12, 0.04)), (0.12, bell(note("E7"), 0.2, 0.07))]
    return seq(parts, 0.34)


EVENTS = {
    "wallet_gain": wallet_gain, "wallet_loss": wallet_loss, "item_gained": item_gained, "whisper": whisper,
    "chat": chat, "zone_town": zone_town, "zone_playerland": zone_playerland, "zone_holyland": zone_holyland,
    "rain_start": rain_start, "rain_stop": rain_stop, "ui_open": ui_open, "ui_close": ui_close,
}

SOUNDS_XML = """<Sounds>
  <!-- volume 0..1 (1 = as loud as the file), cooldown: seconds before the same sound plays again -->
  <Sound name="wallet_gain" volume="0.7" cooldown="0.3" />
  <Sound name="wallet_loss" volume="0.6" cooldown="0.3" />
  <Sound name="item_gained" volume="0.7" cooldown="0.15" />
  <Sound name="whisper" volume="0.8" cooldown="0.5" />
  <Sound name="chat" volume="0.45" cooldown="1" />
  <Sound name="zone_town" volume="0.7" cooldown="3" />
  <Sound name="zone_playerland" volume="0.7" cooldown="3" />
  <Sound name="zone_holyland" volume="0.7" cooldown="3" />
  <Sound name="rain_start" volume="0.6" cooldown="5" />
  <Sound name="rain_stop" volume="0.6" cooldown="5" />
  <Sound name="rain_loop" volume="0.5" />
  <Sound name="ui_open" volume="0.5" />
  <Sound name="ui_close" volume="0.5" />
</Sounds>
"""

CUES_XML = """<Cues>
  <!-- hide_native="1": draw this instead of the game's "!"; frames/fps: the PNG is a strip of 4 frames -->
  <Cue slot="fishing_bite" hide_native="1" anchor="mark" offset_y="0" scale="1" frames="4" fps="8" volume="0.8" />
</Cues>
"""


def active_rms(x):
    """RMS over the part louder than -40 dB of the peak (what the ear hears, not the silent tail)."""
    a = np.abs(x)
    win = max(1, int(0.01 * RATE))
    level = np.sqrt(np.convolve(x**2, np.ones(win) / win, mode="same"))
    loud = level > a.max() * 10 ** (-40 / 20)
    return np.sqrt(np.mean(x[loud] ** 2))


def finish(x, loop=False):
    x = x - np.mean(x)
    if loop:
        x *= 10 ** (LOOP_RMS_DB / 20) / np.sqrt(np.mean(x**2))
    else:
        x *= 10 ** (TARGET_RMS_DB / 20) / active_rms(x)
        x[:32] *= np.linspace(0, 1, 32)  # first and last sample exactly 0
        x[-32:] *= np.linspace(1, 0, 32)
    cap = 10 ** (PEAK_CAP_DB / 20)
    if np.abs(x).max() > cap:
        x *= cap / np.abs(x).max()
    if not loop:  # 15 ms of silence each side: room for the codec's ringing, so the file starts and ends at 0
        pad = np.zeros(int(0.015 * RATE))
        x = np.concatenate([pad, x, pad])
    return x


def write_ogg(x, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(".tmp.wav")
    with wave.open(str(tmp), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(np.round(x * 32767).astype("<i2").tobytes())
    subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", os.path.relpath(tmp), "-c:a", "libvorbis",
                    "-q:a", "5", os.path.relpath(path)], check=True)
    tmp.unlink()


def decode(path):
    raw = subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-i", os.path.relpath(path), "-f", "s16le", "-ac", "1",
                          "-ar", str(RATE), "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, "<i2").astype(float) / 32768


def db(v):
    return 20 * np.log10(max(v, 1e-9))


def measure():
    rows = []
    for path in sorted((MOD / "sounds").glob("*.ogg")) + sorted((MOD / "cues").glob("*.ogg")):
        x = decode(path)
        spec = np.abs(np.fft.rfft(x * np.hanning(len(x))))
        f = np.fft.rfftfreq(len(x), 1 / RATE)
        centroid = (spec * f).sum() / spec.sum()
        edge = max(abs(x[0]), abs(x[-1]))
        loop = path.stem == "rain_loop"
        # The step across the loop point, relative to the median step between neighbouring samples.
        seam = abs(x[-1] - x[0]) / np.median(np.abs(np.diff(x))) if loop else None
        rows.append((path.relative_to(MOD), len(x) / RATE, db(np.abs(x).max()), db(np.sqrt(np.mean(x**2))),
                     db(active_rms(x)), centroid, edge, seam))
    print(f"{'file':28} {'s':>5} {'peak dB':>8} {'RMS dB':>7} {'audible RMS':>11} {'centroid Hz':>11} {'edge':>7}")
    for r in rows:
        print(f"{str(r[0]):28} {r[1]:5.2f} {r[2]:8.1f} {r[3]:7.1f} {r[4]:11.1f} {r[5]:11.0f} {r[6]:7.4f}"
              + (f"  loop seam step {r[7]:.2f}x the median step" if r[7] is not None else ""))


BITE_PALETTE = {"K": (20, 22, 32, 255), "W": (255, 255, 255, 255), "B": hexrgb("cfefff") + (255,),
                "R": hexrgb("ff5a3c") + (255,), "O": hexrgb("ffb43c") + (255,)}
BUBBLE = [  # 12x14: a round white speech bubble with a "!" in it, the tail pointing down
    "...KKKKKK...",
    "..KWWWWWWK..",
    ".KWWW!!WWBK.",
    "KWWWW!!WWWBK",
    "KWWWW!!WWWBK",
    "KWWWW!!WWWBK",
    "KWWWW!!WWWBK",
    "KWWWWWWWWWBK",
    "KWWWW!!WWWBK",
    ".KBWWWWWWBK.",
    "..KBBBBBBK..",
    "...KKBBKK...",
    ".....KK.....",
    "............",
]


def bite_strip():
    """4 frames of 12x16: the bubble hops up 2 px and settles, the "!" flashes red and orange."""
    frames = []
    for i, (lift, mark) in enumerate([(0, "R"), (2, "R"), (1, "O"), (1, "R")]):
        rows = ["............"] * (2 - lift) + [r.replace("!", mark) for r in BUBBLE] + ["............"] * lift
        frames.append(from_rows(rows, BITE_PALETTE))
    from PIL import Image

    strip = Image.new("RGBA", (12 * 4, 16), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        strip.paste(f, (12 * i, 0))
    return strip


def build():
    for name, fn in EVENTS.items():
        write_ogg(finish(fn()), MOD / "sounds" / f"{name}.ogg")
    write_ogg(finish(rain_loop(), loop=True), MOD / "sounds" / "rain_loop.ogg")
    (MOD / "sounds" / "sounds.xml").write_text(SOUNDS_XML)
    write_ogg(finish(fishing_bite()), MOD / "cues" / "fishing_bite.ogg")
    save(bite_strip(), MOD / "cues" / "fishing_bite.png")
    (MOD / "cues" / "cues.xml").write_text(CUES_XML)


if __name__ == "__main__":
    if sys.argv[1:] == ["measure"]:
        measure()
    else:
        build()
        measure()
