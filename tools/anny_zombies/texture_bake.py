#!/usr/bin/env python3
"""Bake AI-generated art onto the Anny zombies' UV atlas.

Anny ships an atlas (MakeHuman's "tights" layout) but no textures. This script
composes one texture per variant:

  * the whole atlas starts as the AI skin material, so every body island picks
    up the same seamless surface;
  * the head island is then overwritten by projecting the AI face portrait onto
    the head geometry and rasterising it back into UV space, i.e. a
    camera-project bake written in numpy.

The projection is a front-facing cylinder around the head's vertical axis, which
lines the eyes/nose/mouth band up with the head's front without needing a
documented UV layout.

Usage:
    python tools/anny_zombies/texture_bake.py --variant shambler \
        --face assets/textures_src/zombie_face.png \
        --skin assets/textures_src/zombie_skin.png \
        --out Assets/Resources/Zombies/zombie_shambler_albedo.png
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_glb import Glb  # noqa: E402

SIZE = 1024
HALF_ANGLE_H = 0.62     # radians of azimuth the portrait covers either side
EYE_LINE = 0.42         # where the eyes sit in the portrait, 0 = top
VERTICAL_SCALE = 0.34   # portrait height per radian of elevation

VARIANTS = {
    # variant: (brightness, tint rgb, extra darkening at the extremities)
    "shambler": (1.00, (1.00, 1.02, 0.94), 0.00),
    "bloater": (0.88, (0.96, 1.00, 0.88), 0.05),
    "fresh": (1.10, (1.02, 1.00, 1.00), 0.00),
    "small": (0.95, (1.00, 1.04, 0.92), 0.02),
}


HEAD_BONES = ("head", "neck", "eye")
HEAD_CHART = (0.02, 0.52, 0.34, 0.98)   # must match anny_zombie_export.HEAD_CHART
EYE_LINE = 0.50                          # portrait row the head centre maps to
VERTICAL_SPAN = 0.52                     # sin(elevation) per portrait height
SAMPLE_AZIMUTH = 1.05                    # clamp: the sides repeat the cheek, not the card
FACE_HALF_WIDTH = 0.95                   # sin(azimuth) at the portrait edge


def load_mesh(glb_path: Path):
    """Positions, UVs, faces and a per-vertex "is this the head" flag.

    The head is selected from the skin weights rather than from a height
    threshold: a height cut at the shoulders drags the chest island in, while
    "dominated by the head/neck/eye bones" is exactly the skull.
    """
    glb = Glb(glb_path)
    prim = glb.json["meshes"][0]["primitives"][0]
    positions = glb.accessor(prim["attributes"]["POSITION"]).astype(np.float64)
    uvs = glb.accessor(prim["attributes"]["TEXCOORD_0"]).astype(np.float64)
    faces = glb.accessor(prim["indices"]).reshape(-1, 3)

    joint_names = [glb.json["nodes"][j].get("name", "") for j in glb.json["skins"][0]["joints"]]
    head_joints = np.array([any(key in name for key in HEAD_BONES) for name in joint_names])

    joint_ids = glb.accessor(prim["attributes"]["JOINTS_0"])
    weights = glb.accessor(prim["attributes"]["WEIGHTS_0"]).astype(np.float64)
    dominant = joint_ids[np.arange(len(joint_ids)), weights.argmax(axis=1)]
    on_head = head_joints[dominant]
    return positions, uvs, faces, on_head


def face_box(image: np.ndarray, threshold: float = 26.0, coverage: float = 0.06):
    """Bounding box of the subject in a portrait shot on a flat card.

    Uses per-column/per-row coverage rather than a single stray pixel: the
    generated portraits have a soft vignette, so a per-pixel threshold picks up
    the whole frame.
    """
    border = np.concatenate([image[:8, :, :].reshape(-1, 3), image[-8:, :, :].reshape(-1, 3),
                             image[:, :8, :].reshape(-1, 3), image[:, -8:, :].reshape(-1, 3)])
    background = np.median(border, axis=0)
    mask = np.abs(image - background).sum(axis=2) > threshold
    columns = np.where(mask.mean(axis=0) > coverage)[0]
    rows = np.where(mask.mean(axis=1) > coverage)[0]
    if len(rows) == 0 or len(columns) == 0:
        return 0, 0, image.shape[1], image.shape[0]
    return int(columns[0]), int(rows[0]), int(columns[-1]) + 1, int(rows[-1]) + 1


def spherical(points: np.ndarray, centre: np.ndarray):
    """Azimuth and elevation of each point around the head centre."""
    relative = points - centre
    radius = np.maximum(np.linalg.norm(relative, axis=1), 1e-9)
    azimuth = np.arctan2(relative[:, 0], relative[:, 2])
    elevation = np.arcsin(np.clip(relative[:, 1] / radius, -1.0, 1.0))
    return azimuth, elevation


def portrait_uv(size: int):
    """Inverse of the exporter's head chart: atlas texel -> portrait UV."""
    u0, v0, u1, v1 = HEAD_CHART
    xs = ((np.arange(size) + 0.5) / size - u0) / (u1 - u0)
    ys = ((np.arange(size) + 0.5) / size - v0) / (v1 - v0)
    grid_u, grid_v = np.meshgrid(xs, ys)

    # chart u spans the full azimuth circle and v spans crown -> chin
    elevation = (0.5 - grid_v) * np.pi
    azimuth = (grid_u - 0.5) * 2.0 * np.pi

    # A planar front projection rather than a cylinder: sin() compresses the
    # sides the way a camera does, so the cheeks and ears line up instead of
    # repeating the portrait's centre.
    portrait_x = 0.5 + np.sin(azimuth) / (2.0 * FACE_HALF_WIDTH)
    portrait_y = EYE_LINE - np.sin(elevation) * VERTICAL_SPAN

    # only the front half of the skull wears the portrait; the back keeps the
    # skin material so the face is not mirrored onto the occiput
    face_weight = np.clip((1.42 - np.abs(azimuth)) / 0.30, 0.0, 1.0)
    face_weight *= np.clip((0.60 - elevation) / 0.22, 0.0, 1.0)
    face_weight *= np.clip((elevation + 0.95) / 0.18, 0.0, 1.0)

    inside = (grid_u >= 0.0) & (grid_u <= 1.0) & (grid_v >= 0.0) & (grid_v <= 1.0)
    return portrait_x, portrait_y, inside, face_weight


def sample_bilinear(image: np.ndarray, px: np.ndarray, py: np.ndarray) -> np.ndarray:
    height, width = image.shape[:2]
    x = np.clip(px, 0.0, width - 1.001)
    y = np.clip(py, 0.0, height - 1.001)
    x0 = x.astype(np.int64)
    y0 = y.astype(np.int64)
    fx = (x - x0)[:, None]
    fy = (y - y0)[:, None]
    top = image[y0, x0] * (1 - fx) + image[y0, x0 + 1] * fx
    bottom = image[y0 + 1, x0] * (1 - fx) + image[y0 + 1, x0 + 1] * fx
    return top * (1 - fy) + bottom * fy


def rasterise_head(uv, faces, head_mask, projected_x, projected_y, frontal, face_image, size):
    """Fill the head's UV islands with the projected portrait.

    Returns the colour canvas and a per-texel weight in [0, 1]: 1 where the
    geometry faces the camera, 0 on the back of the skull (which keeps the skin
    material instead of a stretched copy of the portrait's edge).
    """
    canvas = np.zeros((size, size, 3), dtype=np.float64)
    weight = np.zeros((size, size), dtype=np.float64)
    tri_uv = uv[faces] * size

    for triangle in np.nonzero(head_mask)[0]:
        corner_uv = tri_uv[triangle]
        min_x = max(int(np.floor(corner_uv[:, 0].min())) - 1, 0)
        max_x = min(int(np.ceil(corner_uv[:, 0].max())) + 1, size)
        min_y = max(int(np.floor(corner_uv[:, 1].min())) - 1, 0)
        max_y = min(int(np.ceil(corner_uv[:, 1].max())) + 1, size)
        if min_x >= max_x or min_y >= max_y:
            continue

        gx, gy = np.meshgrid(np.arange(min_x, max_x) + 0.5, np.arange(min_y, max_y) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = corner_uv
        denominator = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(denominator) < 1e-12:
            continue

        wa = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / denominator
        wb = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / denominator
        wc = 1.0 - wa - wb
        inside = (wa >= 0.0) & (wb >= 0.0) & (wc >= 0.0)
        if not inside.any():
            continue

        corner = faces[triangle]
        source_x = (wa * projected_x[corner[0]] + wb * projected_x[corner[1]]
                    + wc * projected_x[corner[2]])
        source_y = (wa * projected_y[corner[0]] + wb * projected_y[corner[1]]
                    + wc * projected_y[corner[2]])
        frontal_texel = (wa * frontal[corner[0]] + wb * frontal[corner[1]]
                         + wc * frontal[corner[2]])

        rows, cols = np.nonzero(inside)
        rows, cols = rows + min_y, cols + min_x
        canvas[rows, cols] = sample_bilinear(face_image, source_x[inside], source_y[inside])
        weight[rows, cols] = frontal_texel[inside]

    return canvas, weight


def fill_gaps(weight: np.ndarray, canvas: np.ndarray, iterations: int = 3):
    """Grow the painted coverage a few texels and bleed colour into the new ones.

    Rasterising triangles at their exact UVs leaves hairline cracks between
    them; this closes those so no unpainted pixels are left inside the island.
    """
    from PIL import ImageFilter

    painted = weight > 0.0
    for _ in range(iterations):
        grown = np.asarray(Image.fromarray((painted * 255).astype(np.uint8))
                           .filter(ImageFilter.MaxFilter(3))) > 127
        new = grown & ~painted
        if not new.any():
            break
        blurred = np.stack([
            np.asarray(Image.fromarray(canvas[:, :, channel].astype(np.uint8))
                       .filter(ImageFilter.BoxBlur(1)), dtype=np.float64)
            for channel in range(3)
        ], axis=-1)
        blurred_weight = np.asarray(Image.fromarray((weight * 255).astype(np.uint8))
                                    .filter(ImageFilter.BoxBlur(1)), dtype=np.float64) / 255.0
        canvas[new] = blurred[new]
        weight[new] = blurred_weight[new]
        painted = grown
    return weight, canvas


def bake(variant: str, glb_path: Path, face_path: Path, skin_path: Path, out_path: Path,
         size: int = SIZE) -> dict:
    face_image = np.asarray(Image.open(face_path).convert("RGB"), dtype=np.float64)
    skin = np.asarray(Image.open(skin_path).convert("RGB").resize((size, size), Image.LANCZOS),
                      dtype=np.float64)

    albedo = skin.copy()

    portrait_x, portrait_y, inside, face_weight = portrait_uv(size)
    # crop to the face itself: the portrait's box also holds hair and shoulders
    x0, y0, x1, y1 = face_box(face_image)
    width_box, height_box = x1 - x0, y1 - y0
    x0, x1 = x0 + 0.20 * width_box, x1 - 0.20 * width_box
    y0, y1 = y0 + 0.02 * height_box, y1 - 0.10 * height_box
    px = x0 + np.clip(portrait_x, 0.0, 1.0) * (x1 - x0)
    py = y0 + np.clip(portrait_y, 0.0, 1.0) * (y1 - y0)

    height, width = face_image.shape[:2]
    sample_x = np.clip(px, 0, width - 1).astype(np.int64)
    sample_y = np.clip(py, 0, height - 1).astype(np.int64)
    portrait = face_image[sample_y, sample_x]

    # blend the chart into the skin at its border so the seam is not a hard line
    u0, v0, u1, v1 = HEAD_CHART
    feather = 0.012
    xs = (np.arange(size) + 0.5) / size
    gx, gy = np.meshgrid(xs, xs)
    edge = np.minimum(np.minimum((gx - u0) / feather, (u1 - gx) / feather),
                      np.minimum((gy - v0) / feather, (v1 - gy) / feather))
    weight = (np.clip(edge, 0.0, 1.0) * face_weight)[:, :, None]
    albedo = np.where(inside[:, :, None], portrait * weight + albedo * (1.0 - weight), albedo)

    brightness, tint, _ = VARIANTS[variant]
    albedo = albedo * brightness * np.array(tint)[None, None, :]

    albedo = np.clip(albedo, 0, 255).astype(np.uint8)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(albedo).save(out_path)

    return {"variant": variant, "file": str(out_path), "size": size,
            "head_texels": int(inside.sum())}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--variant", required=True, choices=sorted(VARIANTS))
    parser.add_argument("--glb", default=None, help="defaults to Assets/StreamingAssets/Zombies/zombie_<variant>.glb")
    parser.add_argument("--face", default="assets/textures_src/zombie_face.png")
    parser.add_argument("--skin", default="assets/textures_src/zombie_skin.png")
    parser.add_argument("--out", default=None, help="defaults to Assets/Resources/Zombies/zombie_<variant>_albedo.png")
    parser.add_argument("--size", type=int, default=SIZE)
    args = parser.parse_args()

    glb = Path(args.glb or f"tools/anny_zombies/build/zombie_{args.variant}.glb")
    out = Path(args.out or f"Assets/Resources/Zombies/zombie_{args.variant}_albedo.png")
    info = bake(args.variant, glb, Path(args.face), Path(args.skin), out, args.size)
    print("[texture] " + str(info))
    return 0


if __name__ == "__main__":
    sys.exit(main())
