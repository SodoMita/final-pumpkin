#!/usr/bin/env python3
"""Render the exported zombie GLBs to a PNG contact sheet and a walk GIF.

No Unity and no GPU involved: the skinned mesh is re-evaluated in numpy (same
code path as verify_glb.py) and drawn with matplotlib's painter's algorithm,
so the preview shows exactly the geometry and animation that shipped inside
the .glb files.

Usage:
    python tools/anny_zombies/render_preview.py \
        --sheet preview/zombies_pose.png --gif preview/zombies_walk.gif
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_glb import Glb, globals_from, skin, sample_track  # noqa: E402

VARIANTS = ["shambler", "bloater", "fresh", "small"]
TITLES = {"shambler": "Shambler  1.71 m", "bloater": "Bloater  1.66 m",
          "fresh": "Fresh  1.05 m", "small": "Small  0.78 m"}
LIGHT = np.array([0.35, 0.75, 0.55])
LIGHT = LIGHT / np.linalg.norm(LIGHT)
YAW = np.radians(24.0)


def pose_overrides(glb: Glb, animation: dict, time: float) -> dict:
    overrides: dict[int, dict] = {}
    for channel in animation["channels"]:
        sampler = animation["samplers"][channel["sampler"]]
        times = glb.accessor(sampler["input"])[:, 0].astype(np.float64)
        values = glb.accessor(sampler["output"]).astype(np.float64)
        value = sample_track(times, values, time)
        target = channel["target"]
        overrides.setdefault(target["node"], {})[target["path"]] = value.tolist()
    return overrides


def duration_of(glb: Glb, animation: dict) -> float:
    end = 0.0
    for sampler in animation["samplers"]:
        times = glb.accessor(sampler["input"])[:, 0].astype(np.float64)
        end = max(end, float(times[-1]))
    return end


def posed_mesh(glb: Glb, animation: dict | None, time: float):
    nodes = glb.json["nodes"]
    if animation is None:
        points = skin(glb, globals_from(nodes))
    else:
        points = skin(glb, globals_from(nodes, pose_overrides(glb, animation, time)))
    faces = glb.json["meshes"][0]["primitives"][0]["indices"]
    if faces is not None:
        tri = glb.accessor(faces).reshape(-1, 3)
    else:
        tri = np.arange(len(points)).reshape(-1, 3)
    material = glb.json.get("materials", [{}])[0]
    base = material.get("pbrMetallicRoughness", {}).get("baseColorFactor", [0.55, 0.6, 0.5, 1.0])
    return points, tri, tuple(base[:3])


def draw(ax, glb: Glb, animation: dict | None, time: float, title: str) -> None:
    points, tri, base = posed_mesh(glb, animation, time)

    c, s = np.cos(YAW), np.sin(YAW)
    rotated = points @ np.array([[c, 0.0, -s], [0.0, 1.0, 0.0], [s, 0.0, c]]).T

    screen = rotated[:, [0, 1]]
    depth = rotated[:, 2]
    tris = screen[tri]
    tri_depth = depth[tri].mean(axis=1)

    # face normals in view space for flat shading
    a, b, cc = rotated[tri[:, 0]], rotated[tri[:, 1]], rotated[tri[:, 2]]
    normals = np.cross(b - a, cc - a)
    lengths = np.linalg.norm(normals, axis=1)
    lengths[lengths == 0] = 1.0
    normals /= lengths[:, None]

    # back-face cull against the view direction (+Z towards the camera)
    facing = normals[:, 2] > 0.0
    shade = np.clip(normals @ LIGHT, 0.0, 1.0) * 0.75 + 0.25
    colours = np.clip(np.array(base)[None, :] * shade[:, None], 0.0, 1.0)

    order = np.argsort(-tri_depth)  # far first
    keep = order[facing[order]]
    ax.add_collection(PolyCollection(tris[keep], facecolors=colours[keep],
                                     edgecolors="none", linewidths=0.0, antialiased=False))

    span_x = screen[:, 0].max() - screen[:, 0].min()
    span_y = screen[:, 1].max() - screen[:, 1].min()
    margin = 0.08 * max(span_x, span_y)
    ax.set_xlim(screen[:, 0].min() - margin, screen[:, 0].max() + margin)
    ax.set_ylim(-0.05, screen[:, 1].max() + margin)
    ax.set_aspect("equal")
    ax.set_facecolor("#0a0c10")
    ax.set_xticks([])
    ax.set_yticks([])
    for spine in ax.spines.values():
        spine.set_visible(False)
    ax.set_title(title, color="#cfd6e2", fontsize=11, pad=6)


def load(folder: Path, variant: str):
    glb = Glb(folder / f"zombie_{variant}.glb")
    animation = next((a for a in glb.json["animations"] if a["name"] == "Shamble"), None)
    return glb, animation


def render_sheet(folder: Path, out: Path, phase: float = 0.35) -> None:
    figure, axes = plt.subplots(1, len(VARIANTS), figsize=(4 * len(VARIANTS), 5.6))
    for ax, variant in zip(np.atleast_1d(axes), VARIANTS):
        glb, animation = load(folder, variant)
        time = duration_of(glb, animation) * phase if animation else 0.0
        draw(ax, glb, animation, time, TITLES[variant])
    figure.patch.set_facecolor("#0a0c10")
    figure.suptitle("Anny zombies - mid Shamble pose, untextured base colour",
                    color="#e8eef7", fontsize=13)
    figure.tight_layout()
    out.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(out, dpi=90, facecolor=figure.get_facecolor())
    plt.close(figure)
    print(f"wrote {out}")


def render_gif(folder: Path, out: Path, frames: int = 16, fps: int = 12) -> None:
    fig, axes = plt.subplots(1, len(VARIANTS), figsize=(3.2 * len(VARIANTS), 4.6))
    axes = np.atleast_1d(axes)
    fig.patch.set_facecolor("#0a0c10")

    loaded = [load(folder, variant) for variant in VARIANTS]
    durations = [duration_of(glb, animation) for glb, animation in loaded]
    images = []

    for frame in range(frames):
        for ax in axes:
            ax.clear()
        for ax, variant, (glb, animation), duration in zip(axes, VARIANTS, loaded, durations):
            # stagger phases so the horde does not march in lockstep
            offset = VARIANTS.index(variant) * 0.13
            time = ((frame / frames) + offset) % 1.0 * duration
            draw(ax, glb, animation, time, TITLES[variant] if frame == 0 else "")
        fig.tight_layout()
        fig.canvas.draw()
        buffer = np.asarray(fig.canvas.buffer_rgba())[:, :, :3]
        images.append(buffer.copy())

    plt.close(fig)
    from PIL import Image

    out.parent.mkdir(parents=True, exist_ok=True)
    frames_out = [Image.fromarray(image) for image in images]
    frames_out[0].save(out, save_all=True, append_images=frames_out[1:],
                       duration=int(1000 / fps), loop=0, optimize=True)
    print(f"wrote {out} ({len(frames_out)} frames)")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--folder", default="Assets/StreamingAssets/Zombies")
    parser.add_argument("--sheet", default="preview/zombies_pose.png")
    parser.add_argument("--gif", default="preview/zombies_walk.gif")
    parser.add_argument("--skip-gif", action="store_true")
    args = parser.parse_args()

    folder = Path(args.folder)
    render_sheet(folder, Path(args.sheet))
    if not args.skip_gif:
        render_gif(folder, Path(args.gif))
    return 0


if __name__ == "__main__":
    sys.exit(main())
