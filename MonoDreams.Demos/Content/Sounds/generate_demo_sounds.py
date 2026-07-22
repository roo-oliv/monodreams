#!/usr/bin/env python3
"""Procedurally generates the audio demo's .wav assets — no third-party or
copyrighted sounds, Python 3 stdlib only, fully deterministic (fixed RNG seed),
so re-running writes byte-identical files.

Run from this directory:

    python3 generate_demo_sounds.py

Outputs (22050 Hz, 16-bit PCM, mono — small files, ample quality for a demo):

- click.wav    — short two-partial sine ping (the one-shot `PlaySoundRequest` case).
- wind.wav     — low-pass-filtered noise with a gust envelope, seamlessly loopable
                 (the `AudioSourceComponent Loop=true` ambience case).
- jukebox.wav  — a ~10s arpeggiated chord riff (the interruptible source case; long
                 enough that the demo's scripted cut at ~5s of interactive play — and
                 any headless-speed cut — always lands mid-play).

These are consumed by ../Content.mgcb via WavImporter + SoundEffectProcessor
(built-in importers in both the MonoGame and KNI MGCB toolchains).
"""

import math
import random
import struct
import wave
from pathlib import Path

SAMPLE_RATE = 22050
OUT_DIR = Path(__file__).resolve().parent


def write_wav(name: str, samples: list[float]) -> None:
    """Writes mono 16-bit PCM, clamping to [-1, 1]."""
    path = OUT_DIR / name
    frames = bytearray()
    for s in samples:
        s = max(-1.0, min(1.0, s))
        frames += struct.pack("<h", int(s * 32767))
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(bytes(frames))
    print(f"wrote {path.name}: {len(samples) / SAMPLE_RATE:.2f}s, {path.stat().st_size} bytes")


def note_freq(semitones_from_a4: int) -> float:
    return 440.0 * (2.0 ** (semitones_from_a4 / 12.0))


def make_click() -> list[float]:
    """0.15s ping: 880 Hz + a quieter 1760 Hz partial, fast exponential decay.
    A 2 ms linear attack avoids a start pop."""
    duration = 0.15
    n = int(SAMPLE_RATE * duration)
    attack = int(SAMPLE_RATE * 0.002)
    out = []
    for i in range(n):
        t = i / SAMPLE_RATE
        env = math.exp(-t * 40.0) * (min(i, attack) / attack if attack else 1.0)
        s = math.sin(2 * math.pi * 880.0 * t) + 0.4 * math.sin(2 * math.pi * 1760.0 * t)
        out.append(0.6 * env * s)
    return out


def make_wind() -> list[float]:
    """4s loopable wind: white noise -> one-pole low-pass, gust-modulated by an LFO
    with whole cycles over the loop (envelope is loop-continuous), then the noise
    itself is made seamless by crossfading an extra generated tail onto the head."""
    duration = 4.0
    fade = 0.25  # crossfade seconds (loop seam)
    n = int(SAMPLE_RATE * duration)
    fade_n = int(SAMPLE_RATE * fade)
    rng = random.Random(42)

    # Generate n + fade_n filtered samples; the extra tail is folded onto the head.
    raw = []
    lp = 0.0
    alpha = 0.08  # one-pole low-pass coefficient (~300 Hz): rumble, not hiss
    for _ in range(n + fade_n):
        lp += alpha * (rng.uniform(-1.0, 1.0) - lp)
        raw.append(lp)

    out = []
    gust_hz = 0.5  # 2 whole cycles over the 4s loop -> envelope continuous at the seam
    for i in range(n):
        s = raw[i]
        if i < fade_n:  # crossfade: the tail flows into the head, so end -> start is seamless
            w = i / fade_n
            s = s * w + raw[n + i] * (1.0 - w)
        t = i / SAMPLE_RATE
        env = 0.6 + 0.4 * math.sin(2 * math.pi * gust_hz * t)
        out.append(2.2 * env * s)  # filtered noise is quiet; scale toward full range
    peak = max(abs(s) for s in out)
    return [0.8 * s / peak for s in out]


def make_jukebox() -> list[float]:
    """~10s chiptune-ish riff: Am F C G Am, arpeggiated in eighth notes at 120 BPM
    (0.25s per note) over a half-note bass root. Soft-square voices (odd harmonics),
    per-note decay envelopes, normalized with headroom, 10 ms final fade-out."""
    # Chords as semitone offsets from A4: (bass root, arpeggio triad)
    chords = [
        (-24, (-12, -9, -5)),   # Am: A3 C4 E4, bass A2
        (-28, (-16, -12, -9)),  # F:  F3 A3 C4, bass F2
        (-21, (-9, -5, -2)),    # C:  C4 E4 G4, bass C3
        (-26, (-14, -10, -7)),  # G:  G3 B3 D4, bass G2
        (-24, (-12, -9, -5)),   # Am again — resolves the loop of the riff
    ]
    note_len = 0.25            # eighth note at 120 BPM
    notes_per_chord = 8        # 2s per chord -> 10s total
    pattern = (0, 1, 2, 1, 0, 1, 2, 1)
    n = int(SAMPLE_RATE * note_len * notes_per_chord * len(chords))
    out = [0.0] * n

    def voice(freq: float, t: float) -> float:
        # Soft square: fundamental + odd harmonics.
        return (math.sin(2 * math.pi * freq * t)
                + 0.33 * math.sin(2 * math.pi * 3 * freq * t)
                + 0.2 * math.sin(2 * math.pi * 5 * freq * t))

    attack_n = int(SAMPLE_RATE * 0.005)
    for c, (bass, triad) in enumerate(chords):
        chord_start = int(c * notes_per_chord * note_len * SAMPLE_RATE)
        # Bass: two half notes per chord.
        bass_freq = note_freq(bass)
        bass_len = int(SAMPLE_RATE * note_len * notes_per_chord / 2)
        for h in range(2):
            start = chord_start + h * bass_len
            for i in range(bass_len):
                t = i / SAMPLE_RATE
                env = math.exp(-t * 1.2) * (min(i, attack_n) / attack_n if attack_n else 1.0)
                out[start + i] += 0.5 * env * voice(bass_freq, t)
        # Arpeggio: eighth notes following the pattern.
        for k in range(notes_per_chord):
            freq = note_freq(triad[pattern[k]])
            start = chord_start + int(k * note_len * SAMPLE_RATE)
            count = int(note_len * SAMPLE_RATE)
            for i in range(min(count, n - start)):
                t = i / SAMPLE_RATE
                env = math.exp(-t * 4.0) * (min(i, attack_n) / attack_n if attack_n else 1.0)
                out[start + i] += 0.35 * env * voice(freq, t)

    fade_n = int(SAMPLE_RATE * 0.01)  # end fade avoids a cutoff pop
    for i in range(fade_n):
        out[n - 1 - i] *= i / fade_n
    peak = max(abs(s) for s in out)
    return [0.8 * s / peak for s in out]


if __name__ == "__main__":
    write_wav("click.wav", make_click())
    write_wav("wind.wav", make_wind())
    write_wav("jukebox.wav", make_jukebox())
