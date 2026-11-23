"""Generates the built-in abstract wallpapers (assets/wallpapers/abstract/*.jpg).

Original artwork made from code, so it is covered by the project's MIT license.
Re-runnable and deterministic (fixed seeds). Requires numpy and Pillow:

    python build/make-abstract-wallpapers.py
"""

from __future__ import annotations

import pathlib

import numpy as np
from PIL import Image, ImageFilter

W, H = 3840, 2160
OUT = pathlib.Path(__file__).resolve().parent.parent / "assets" / "wallpapers" / "abstract"


def hex_rgb(value: str) -> np.ndarray:
    value = value.lstrip("#")
    return np.array([int(value[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)


def grid(w: int = W, h: int = H) -> tuple[np.ndarray, np.ndarray]:
    """Normalised coordinates: x in [0, w/h], y in [0, 1]."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    return xs / h, ys / h


def finish(rgb: np.ndarray, name: str, seed: int, grain: float = 1.3) -> None:
    """Adds fine film grain (prevents gradient banding) and saves a high-quality JPEG."""
    rng = np.random.default_rng(seed)
    rgb = rgb + rng.normal(0.0, grain, rgb.shape).astype(np.float32)
    img = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB")
    if img.size != (W, H):
        img = img.resize((W, H), Image.LANCZOS)
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / f"{name}.jpg"
    img.save(path, "JPEG", quality=90, optimize=True, progressive=True, subsampling=0)
    print(f"{path.name}: {path.stat().st_size / 1e6:.2f} MB")


def aurora_mesh() -> None:
    """Soft mesh gradient: coloured light pools blended over deep navy, gently domain-warped."""
    w, h = W // 2, H // 2
    x, y = grid(w, h)
    # Organic warp so the pools are not perfect circles.
    xw = x + 0.06 * np.sin(3.1 * y + 0.7) + 0.035 * np.sin(5.3 * x + 1.9 * y)
    yw = y + 0.05 * np.sin(2.7 * x + 1.3) + 0.03 * np.cos(4.1 * y - 2.2 * x)

    base = hex_rgb("#070a18")
    blobs = [  # (x, y, sigma, colour, strength)
        (0.25, 0.30, 0.34, "#14d2c2", 1.00),
        (0.95, 0.20, 0.30, "#6a3df5", 1.00),
        (1.55, 0.55, 0.36, "#e8408c", 0.95),
        (0.75, 0.95, 0.40, "#1d4bff", 0.90),
        (1.70, 0.05, 0.22, "#ffb35c", 0.55),
    ]
    acc = np.zeros((h, w, 3), np.float32)
    weight = np.full((h, w), 0.55, np.float32)  # weight of the dark base
    acc += weight[..., None] * base
    for bx, by, sigma, colour, strength in blobs:
        wgt = strength * np.exp(-((xw - bx) ** 2 + (yw - by) ** 2) / (2 * sigma ** 2))
        acc += wgt[..., None] * hex_rgb(colour)
        weight += wgt
    rgb = acc / weight[..., None]
    # Darken toward the bottom-left corner for depth.
    vignette = 1.0 - 0.35 * np.clip(((x - 0.9) ** 2 + (y - 0.45) ** 2) ** 0.5 - 0.55, 0, 1)
    finish(rgb * vignette[..., None], "aurora-mesh", seed=1)


def paper_waves() -> None:
    """Layered paper-cut waves in sunset colours, each layer casting a soft shadow."""
    x, y = grid()
    aspect = W / H
    palette = [hex_rgb(c) for c in
               ("#1b1036", "#2e1650", "#4d1d68", "#7a2a73", "#a83570", "#d4476a", "#f0694f", "#f8a35a")]
    layers = len(palette) - 1
    layer_f = np.zeros_like(y)
    shadow = np.zeros_like(y)
    for k in range(layers):
        t = (k + 1) / (layers + 1)
        base_y = 0.18 + 0.80 * t
        phase = 1.7 * k + 0.4
        boundary = (base_y
                    + 0.045 * np.sin(2.0 * np.pi * x / aspect * (1.1 + 0.15 * k) + phase)
                    + 0.020 * np.sin(2.0 * np.pi * x / aspect * (2.6 + 0.3 * k) - 1.3 * phase)
                    + 0.008 * np.sin(2.0 * np.pi * x / aspect * 6.0 + k))
        d = (y - boundary) * H  # pixels below this boundary
        layer_f += np.clip(d / 1.6 + 0.5, 0.0, 1.0)  # anti-aliased step
        shadow = np.maximum(shadow, np.where(d > 0, np.exp(-d / 38.0), 0.0) * 0.42)
    lo = np.floor(layer_f).astype(int).clip(0, layers)
    hi = np.minimum(lo + 1, layers)
    frac = (layer_f - lo)[..., None]
    pal = np.stack(palette)
    rgb = pal[lo] * (1 - frac) + pal[hi] * frac
    # Each layer a touch lighter at its top edge; shadow from the layer above.
    rgb = rgb * (1.0 - shadow[..., None])
    rgb *= (0.92 + 0.08 * (1 - y))[..., None]
    finish(rgb, "paper-waves", seed=2, grain=1.1)


def contour_lines() -> None:
    """Glowing topographic contour lines over a dark gradient."""
    w, h = W, H
    x, y = grid(w, h)
    aspect = w / h
    f = (1.2 * np.exp(-((x - 0.55 * aspect) ** 2 + (y - 0.45) ** 2) / 0.16)
         + 0.9 * np.exp(-((x - 0.22 * aspect) ** 2 + (y - 0.80) ** 2) / 0.10)
         - 0.7 * np.exp(-((x - 0.85 * aspect) ** 2 + (y - 0.25) ** 2) / 0.08)
         + 0.18 * np.sin(3.0 * x + 1.2) * np.cos(2.4 * y - 0.5)
         + 0.07 * np.sin(7.0 * x - 3.0 * y))
    levels = f * 26.0
    gy, gx = np.gradient(levels)
    grad = np.sqrt(gx ** 2 + gy ** 2) + 1e-6
    dist = np.abs(levels - np.round(levels)) / grad  # distance to nearest line, in pixels
    line = np.clip(1.6 - dist, 0.0, 1.0)              # ~2.5 px crisp line
    glow = np.exp(-dist / 7.0) * 0.30                 # soft halo
    # Every 5th line brighter, like an index contour.
    major = (np.abs(np.round(levels) % 5) < 0.5).astype(np.float32)
    intensity = np.clip(line * (0.55 + 0.45 * major) + glow * (0.6 + 0.4 * major), 0, 1)

    t = np.clip(x / aspect * 0.8 + y * 0.35, 0, 1)[..., None]
    line_colour = hex_rgb("#3ee6d8") * (1 - t) + hex_rgb("#9b6bff") * t
    bg_top, bg_bottom = hex_rgb("#0a1426"), hex_rgb("#05070f")
    bg = bg_top * (1 - y[..., None]) + bg_bottom * y[..., None]
    halo = np.exp(-((x - 0.55 * aspect) ** 2 + (y - 0.45) ** 2) / 0.5)[..., None] * hex_rgb("#10304a") * 0.6
    rgb = bg + halo + intensity[..., None] * line_colour
    finish(rgb, "contour-lines", seed=3, grain=1.0)


def bokeh_glow() -> None:
    """Warm out-of-focus light circles over a deep plum gradient."""
    w, h = W // 2, H // 2
    rng = np.random.default_rng(42)
    x, y = grid(w, h)
    aspect = w / h
    bg = (hex_rgb("#1a0b24") * (1 - y[..., None]) + hex_rgb("#07040c") * y[..., None])
    light = np.zeros((h, w, 3), np.float32)
    colours = [hex_rgb(c) for c in ("#ffb347", "#ff6f91", "#ffd9a0", "#c56cf0", "#ff9671")]
    for _ in range(70):
        cx = rng.uniform(-0.05, aspect + 0.05)
        cy = rng.uniform(0.05, 1.0) ** 0.8  # a few more toward the bottom
        r = rng.uniform(0.015, 0.11) * (0.6 + 0.8 * cy)
        a = rng.uniform(0.12, 0.42)
        col = colours[rng.integers(len(colours))]
        d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / r
        disc = np.clip((1.0 - d) * 18.0, 0, 1)                 # flat disc, soft edge
        rim = np.exp(-((d - 0.93) ** 2) / 0.004) * 0.35        # slightly brighter rim like real bokeh
        light += ((disc * (0.75 + 0.25 * (1 - d))) + rim * disc.max())[..., None] * col * a
    img = Image.fromarray(np.clip(light, 0, 255).astype(np.uint8), "RGB").filter(ImageFilter.GaussianBlur(3))
    light = np.asarray(img, dtype=np.float32)
    rgb = bg + light * 0.9
    # Big soft warm glow behind everything.
    rgb += np.exp(-((x - 0.65 * aspect) ** 2 + (y - 0.7) ** 2) / 0.35)[..., None] * hex_rgb("#5a1e3c") * 0.55
    finish(rgb, "bokeh-glow", seed=4, grain=1.4)


if __name__ == "__main__":
    aurora_mesh()
    paper_waves()
    contour_lines()
    bokeh_glow()
