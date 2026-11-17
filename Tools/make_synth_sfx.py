"""Synthesises the sounds the Kenney packs do not have, into Assets/Resources/Audio as WAV:
- shatter_N:     glass cabinet smashed (impact thump, noise crash, a shower of glass tinkles)
- ice_shatter_N: ice block breaking (duller crunch, lower and shorter tinkles)
- screw_N:       soft metal clicks for unscrewing a panel cover (replaces the Kenney screw mix)

Usage: python -I Tools/make_synth_sfx.py   (numpy + scipy). Seeded, so reruns give the same files.
"""
import os
import sys

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

RATE = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Audio")


def band(signal, low, high, order=4):
    sos = butter(order, [low, high], btype="band", fs=RATE, output="sos")
    return sosfilt(sos, signal)


def highpass(signal, cutoff, order=4):
    return sosfilt(butter(order, cutoff, btype="high", fs=RATE, output="sos"), signal)


def lowpass(signal, cutoff, order=4):
    return sosfilt(butter(order, cutoff, btype="low", fs=RATE, output="sos"), signal)


def env(n, attack, decay):
    t = np.arange(n) / RATE
    return np.minimum(t / max(attack, 1e-4), 1.0) * np.exp(-t / decay)


def tinkle(rng, freq_low, freq_high, decay):
    """One small shard: a few inharmonic damped partials."""
    length = int(RATE * decay * 6)
    t = np.arange(length) / RATE
    base = rng.uniform(freq_low, freq_high)
    out = np.zeros(length)
    for ratio, amp in ((1.0, 1.0), (2.32, 0.5), (3.71, 0.3), (5.13, 0.15)):
        out += amp * np.sin(2 * np.pi * base * ratio * t + rng.uniform(0, 6.28)) * np.exp(-t / (decay / ratio ** 0.5))
    return out


def shatter(seed, length=1.6, thump_hz=90, crash_band=(1800, 9000), shard_range=(2500, 7500),
            shard_decay=0.035, shards=70, shower=0.9):
    rng = np.random.default_rng(seed)
    n = int(RATE * length)
    out = np.zeros(n)
    # Impact thump.
    t = np.arange(int(RATE * 0.25)) / RATE
    out[:len(t)] += 0.6 * np.sin(2 * np.pi * thump_hz * t * (1 - 0.4 * t)) * np.exp(-t / 0.05)
    # The crash: bright noise burst with a quick decay and a long tail.
    noise = rng.standard_normal(n)
    crash = band(noise, *crash_band) * (env(n, 0.002, 0.08) + 0.15 * env(n, 0.01, 0.35))
    out += 0.9 * crash
    # The shower of falling shards: dense at first, thinning out.
    for _ in range(shards):
        start = int(RATE * shower * rng.random() ** 2.2)
        piece = tinkle(rng, *shard_range, shard_decay * rng.uniform(0.6, 1.6)) * rng.uniform(0.15, 0.6)
        end = min(n, start + len(piece))
        out[start:end] += piece[:end - start]
    # A few bigger pieces landing later.
    for _ in range(6):
        start = int(RATE * rng.uniform(0.25, shower + 0.2))
        hit = band(rng.standard_normal(int(RATE * 0.06)), 2000, 8000) * env(int(RATE * 0.06), 0.001, 0.012)
        end = min(n, start + len(hit))
        out[start:end] += 0.5 * hit[:end - start]
    out = highpass(out, 60)
    fade = np.ones(n)
    fade[-int(RATE * 0.2):] = np.linspace(1, 0, int(RATE * 0.2))
    return out * fade


def ice(seed):
    rng = np.random.default_rng(seed)
    out = shatter(seed + 100, length=1.2, thump_hz=70, crash_band=(600, 5000), shard_range=(1200, 3800),
                  shard_decay=0.022, shards=45, shower=0.6)
    # Crunch: grainy low-mid crackle at the start.
    n = len(out)
    crackle = (rng.random(n) < 0.02) * rng.standard_normal(n)
    crackle = band(crackle, 300, 3000) * env(n, 0.002, 0.18)
    return out * 0.8 + 2.5 * crackle


def soft_click(seed):
    """A soft metal click: a short damped ring with a muted transient, like a screw turning."""
    rng = np.random.default_rng(seed)
    n = int(RATE * 0.18)
    t = np.arange(n) / RATE
    ring = np.zeros(n)
    base = rng.uniform(2200, 3400)
    for ratio, amp in ((1.0, 1.0), (1.53, 0.4), (2.71, 0.2)):
        ring += amp * np.sin(2 * np.pi * base * ratio * t) * np.exp(-t / rng.uniform(0.012, 0.025))
    transient = lowpass(rng.standard_normal(n), 4000) * np.exp(-t / 0.003)
    out = 0.5 * ring + 0.35 * transient
    # Some clicks are a double tick (thread catching).
    if rng.random() < 0.5:
        shift = int(RATE * rng.uniform(0.025, 0.045))
        out[shift:] += 0.45 * out[:n - shift]
    return lowpass(out, 6000)


def write(name, signal, peak):
    signal = signal / (np.max(np.abs(signal)) + 1e-9) * peak
    wavfile.write(os.path.join(OUT, name + ".wav"), RATE, (signal * 32767).astype(np.int16))
    print("wrote", name)


def main():
    os.makedirs(OUT, exist_ok=True)
    for i in range(3):
        write(f"shatter_{i + 1}", shatter(11 + i), 0.9)
        write(f"ice_shatter_{i + 1}", ice(31 + i), 0.85)
    for i in range(4):
        write(f"screw_{i + 1}", soft_click(51 + i), 0.5)
    return 0


if __name__ == "__main__":
    sys.exit(main())
