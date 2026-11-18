"""Generates the sample textures in Textures/ (and the contact sheet in Docs~/).

    python Tools~/generate_sample_textures.py

Everything is computed here from fixed seeds, so the output is reproducible and carries no third-party content.
Needs numpy and Pillow. Noise and streak textures tile; they are built in frequency space or on a torus.
"""

import math
import os

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(ROOT, "Textures")
SHEET = os.path.join(ROOT, "Docs~", "sample_textures.png")
NOISE_SIZE = 512
MASK_SIZE = 256


def normalize(a, low=0.5, high=99.5):
    """Stretches to 0..1 between two percentiles, so a few extreme values do not flatten the contrast."""
    lo, hi = np.percentile(a, [low, high])
    return np.clip((a - lo) / max(hi - lo, 1e-9), 0.0, 1.0)


def spectral_noise(size, seed, beta, stretch_u=1.0, stretch_v=1.0, low_cut=0.0):
    """Tileable noise: random phases shaped by 1/f^beta. An inverse FFT is periodic, so it wraps without a seam.

    stretch_u / stretch_v > 1 makes features longer along that axis.
    """
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size) * size
    fx = np.fft.fftfreq(size) * size
    gx, gy = np.meshgrid(fx * stretch_u, fy * stretch_v)
    radius = np.sqrt(gx * gx + gy * gy)
    radius[0, 0] = 1.0
    amplitude = radius ** (-beta / 2.0)
    amplitude[0, 0] = 0.0
    if low_cut > 0.0:
        amplitude[radius < low_cut] = 0.0
    phase = rng.uniform(0.0, math.tau, (size, size))
    field = np.fft.ifft2(amplitude * np.exp(1j * phase)).real
    return normalize(field)


def voronoi(size, seed, grid):
    """Tileable cells: returns the distance to the nearest feature point and the distance to the nearest cell border.

    Points are jittered on a grid so cells stay about the same size. Everything is measured on a torus. The border
    distance is exact (d2^2 - d1^2) / (2 |p2 - p1|), which keeps lines even where two feature points sit close together.
    """
    rng = np.random.default_rng(seed)
    cells = (np.mgrid[0:grid, 0:grid].reshape(2, -1).T + rng.uniform(0.1, 0.9, (grid * grid, 2))) / grid
    coords = (np.arange(size) + 0.5) / size
    gx, gy = np.meshgrid(coords, coords)
    # wrapped vector from each pixel to each point: (points, size, size)
    vx = cells[:, 0, None, None] - gx
    vy = cells[:, 1, None, None] - gy
    vx = vx - np.round(vx)
    vy = vy - np.round(vy)
    dist = np.sqrt(vx * vx + vy * vy)
    order = np.argsort(dist, axis=0)[:2]
    take = lambda a, k: np.take_along_axis(a, order[k:k + 1], axis=0)[0]
    d1, d2 = take(dist, 0), take(dist, 1)
    span = np.sqrt((take(vx, 1) - take(vx, 0)) ** 2 + (take(vy, 1) - take(vy, 0)) ** 2)
    return d1, (d2 * d2 - d1 * d1) / (2.0 * np.maximum(span, 1e-6))


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def radial(size):
    coords = (np.arange(size) + 0.5) / size * 2.0 - 1.0
    gx, gy = np.meshgrid(coords, coords)
    return gx, gy, np.sqrt(gx * gx + gy * gy)


def save_opaque(name, value):
    """Greyscale value, fully opaque. For noise that multiplies or distorts."""
    Image.fromarray(np.uint8(np.clip(value, 0.0, 1.0) * 255.0 + 0.5)).save(os.path.join(OUT, name + ".png"), optimize=True)


def save_mask(name, value):
    """Greyscale value also stored in alpha, so it works in both alpha-blended and additive materials."""
    v = np.uint8(np.clip(value, 0.0, 1.0) * 255.0 + 0.5)
    Image.fromarray(np.dstack([v, v]), "LA").save(os.path.join(OUT, name + ".png"), optimize=True)


def main():
    os.makedirs(OUT, exist_ok=True)

    # ---- tileable noise ----
    save_opaque("T_Noise_Cloud", spectral_noise(NOISE_SIZE, 11, 2.6))
    save_opaque("T_Noise_Fine", spectral_noise(NOISE_SIZE, 12, 1.5, low_cut=6.0))
    save_opaque("T_Noise_Wispy", spectral_noise(NOISE_SIZE, 13, 2.4, stretch_u=1.0, stretch_v=5.0))

    nearest, border = voronoi(NOISE_SIZE, 21, 6)
    save_opaque("T_Noise_Cell", normalize(nearest))
    save_opaque("T_Noise_Crack", 1.0 - smoothstep(0.002, 0.012, border))

    # ---- tileable streaks, for scrolling along V ----
    save_opaque("T_Streak_Soft", spectral_noise(NOISE_SIZE, 31, 2.2, stretch_u=1.0, stretch_v=24.0))
    streak = spectral_noise(NOISE_SIZE, 32, 1.2, stretch_u=1.0, stretch_v=40.0, low_cut=4.0)
    save_opaque("T_Streak_Sharp", smoothstep(0.55, 0.8, streak))

    # ---- masks ----
    gx, gy, r = radial(MASK_SIZE)
    save_mask("T_Mask_Circle", 1.0 - smoothstep(0.0, 1.0, r))
    save_mask("T_Mask_Ring", np.exp(-(((r - 0.68) / 0.13) ** 2)) * (1.0 - smoothstep(0.93, 1.0, r)))
    glow = np.exp(-r * 3.2) * (1.0 - smoothstep(0.7, 1.0, r))
    arms = np.exp(-np.abs(gx) * 36.0) * (1.0 - smoothstep(0.1, 1.0, np.abs(gy))) + \
        np.exp(-np.abs(gy) * 36.0) * (1.0 - smoothstep(0.1, 1.0, np.abs(gx)))
    save_mask("T_Mask_Spark", np.clip(glow * 1.4 + arms, 0.0, 1.0))

    v = (np.arange(MASK_SIZE) + 0.5) / MASK_SIZE
    _, gv = np.meshgrid(v, v)
    # PNG rows run top to bottom and Unity's V runs bottom to top: V = 0 (bottom of the image) is bright
    save_mask("T_Mask_FadeV", gv)
    save_mask("T_Mask_EdgeFade", smoothstep(0.0, 0.25, gv) * smoothstep(1.0, 0.75, gv))

    write_sheet()


def write_sheet():
    names = sorted(f for f in os.listdir(OUT) if f.endswith(".png"))
    columns = 4
    cell = 170
    label = 22
    rows = int(math.ceil(len(names) / columns))
    sheet = Image.new("RGB", (columns * cell, rows * (cell + label)), (30, 30, 32))
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(names):
        x = (i % columns) * cell
        y = (i // columns) * (cell + label)
        image = Image.open(os.path.join(OUT, name))
        grey = image.convert("L").resize((cell - 10, cell - 10), Image.LANCZOS)
        sheet.paste(grey, (x + 5, y + 5))
        draw.text((x + 6, y + cell - 2), name[:-4], fill=(220, 220, 220))
    sheet.save(SHEET, optimize=True)
    print("wrote", len(names), "textures to", OUT)


if __name__ == "__main__":
    main()
