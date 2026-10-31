#!/usr/bin/env python3
"""A small textured software rasteriser, so the zombies can be previewed
with their albedo maps without Unity or a GPU.

Pipeline per triangle: transform -> backface cull -> screen-space bounding box ->
barycentric coverage test -> z-buffer test -> bilinear albedo lookup -> Lambert
shading. Slow but honest, and it reuses the same skinning code as verify_glb.py.

Usage:
    python tools/anny_zombies/soft_render.py --variant shambler --out preview/zombie_shambler_textured.png
    python tools/anny_zombies/soft_render.py --gif preview/zombies_textured.gif
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from verify_glb import Glb, globals_from, skin, sample_track  # noqa: E402

LIGHT = np.array([-0.35, 0.72, 0.60])
LIGHT /= np.linalg.norm(LIGHT)


class Model:
    def __init__(self, glb_path: Path, texture_path: Path | None):
        self.glb = Glb(glb_path)
        prim = self.glb.json["meshes"][0]["primitives"][0]
        self.positions = self.glb.accessor(prim["attributes"]["POSITION"]).astype(np.float64)
        self.normals = self.glb.accessor(prim["attributes"]["NORMAL"]).astype(np.float64)
        self.uvs = (self.glb.accessor(prim["attributes"]["TEXCOORD_0"]).astype(np.float64)
                    if "TEXCOORD_0" in prim["attributes"] else None)
        self.faces = self.glb.accessor(prim["indices"]).reshape(-1, 3)
        self.nodes = self.glb.json["nodes"]
        self.animations = {a["name"]: a for a in self.glb.json.get("animations", [])}
        self.texture = (np.asarray(Image.open(texture_path).convert("RGB"), dtype=np.float64) / 255.0
                        if texture_path and Path(texture_path).exists() else None)
        material = self.glb.json.get("materials", [{}])[0]
        self.base_colour = np.array(
            material.get("pbrMetallicRoughness", {}).get("baseColorFactor", [0.6, 0.6, 0.6, 1.0])[:3])
        self.rest = skin(self.glb, globals_from(self.nodes))
        self._poses: dict[str, np.ndarray] = {}

    def pose(self, clip: str | None, time: float) -> np.ndarray:
        if clip is None:
            return self.rest
        animation = self.animations[clip]
        overrides: dict[int, dict] = {}
        for channel in animation["channels"]:
            sampler = animation["samplers"][channel["sampler"]]
            times = self.glb.accessor(sampler["input"])[:, 0].astype(np.float64)
            values = self.glb.accessor(sampler["output"]).astype(np.float64)
            target = channel["target"]
            overrides.setdefault(target["node"], {})[target["path"]] = sample_track(times, values, time).tolist()
        return skin(self.glb, globals_from(self.nodes, overrides))

    def clip_length(self, clip: str) -> float:
        end = 0.0
        for sampler in self.animations[clip]["samplers"]:
            times = self.glb.accessor(sampler["input"])[:, 0].astype(np.float64)
            end = max(end, float(times[-1]))
        return end


def render(model: Model, vertices: np.ndarray, width: int, height: int, yaw: float,
           pitch: float, scale: float, centre: tuple[float, float]) -> np.ndarray:
    cy, sy = np.cos(yaw), np.sin(yaw)
    cp, sp = np.cos(pitch), np.sin(pitch)
    rot_yaw = np.array([[cy, 0.0, -sy], [0.0, 1.0, 0.0], [sy, 0.0, cy]])
    rot_pitch = np.array([[1.0, 0.0, 0.0], [0.0, cp, -sp], [0.0, sp, cp]])
    rotation = rot_pitch @ rot_yaw
    points = vertices @ rotation.T
    normals = model.normals @ rotation.T

    fx = width * scale
    screen = np.empty((len(points), 2))
    screen[:, 0] = width * 0.5 + (points[:, 0] - centre[0]) * fx
    screen[:, 1] = height * 0.5 - (points[:, 1] - centre[1]) * fx
    depth = points[:, 2]

    colour = np.zeros((height, width, 3), dtype=np.float64)
    zbuffer = np.full((height, width), -1e9)
    has_texture = model.texture is not None
    if has_texture:
        texture = model.texture
        tex_h, tex_w = texture.shape[:2]

    order = np.argsort(depth[model.faces].mean(axis=1))
    for index in order:
        f = model.faces[index]
        z = depth[f]
        if z.mean() < -0.35:  # behind the camera plane
            continue
        a, b, c = screen[f[0]], screen[f[1]], screen[f[2]]
        area = (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])
        if abs(area) < 1e-9:
            continue

        min_x = max(int(np.floor(min(a[0], b[0], c[0]))), 0)
        max_x = min(int(np.ceil(max(a[0], b[0], c[0]))) + 1, width)
        min_y = max(int(np.floor(min(a[1], b[1], c[1]))), 0)
        max_y = min(int(np.ceil(max(a[1], b[1], c[1]))) + 1, height)
        if min_x >= max_x or min_y >= max_y:
            continue

        gx, gy = np.meshgrid(np.arange(min_x, max_x) + 0.5, np.arange(min_y, max_y) + 0.5)
        w0 = ((b[0] - gx) * (c[1] - gy) - (c[0] - gx) * (b[1] - gy)) / area
        w1 = ((c[0] - gx) * (a[1] - gy) - (a[0] - gx) * (c[1] - gy)) / area
        w2 = 1.0 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any():
            continue

        triangle_depth = w0 * z[0] + w1 * z[1] + w2 * z[2]
        rows, cols = np.nonzero(inside)
        target = (rows + min_y, cols + min_x)
        closer = triangle_depth[inside] > zbuffer[target]
        if not closer.any():
            continue
        rows, cols = rows[closer], cols[closer]
        target = (rows + min_y, cols + min_x)
        zbuffer[target] = triangle_depth[inside][closer]

        w0f, w1f, w2f = w0[inside][closer], w1[inside][closer], w2[inside][closer]
        normal = (w0f[:, None] * normals[f[0]] + w1f[:, None] * normals[f[1]]
                  + w2f[:, None] * normals[f[2]])
        normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-9)
        shade = np.clip(normal @ LIGHT, 0.0, 1.0) * 0.70 + 0.42

        if has_texture and model.uvs is not None:
            uv = (w0f[:, None] * model.uvs[f[0]] + w1f[:, None] * model.uvs[f[1]]
                  + w2f[:, None] * model.uvs[f[2]])
            px = np.clip((uv[:, 0] % 1.0) * (tex_w - 1), 0, tex_w - 1).astype(np.int64)
            py = np.clip((uv[:, 1] % 1.0) * (tex_h - 1), 0, tex_h - 1).astype(np.int64)
            albedo = texture[py, px]
        else:
            albedo = np.broadcast_to(model.base_colour, (len(rows), 3))

        colour[target] = np.clip(albedo * shade[:, None], 0.0, 1.0)

    return colour


def contact_sheet(models: dict[str, tuple[Model, str]], out: Path, phase: float = 0.3,
                   width: int = 380, height: int = 620) -> None:
    columns = len(models)
    sheet = Image.new("RGB", (width * columns, height + 34), (10, 12, 16))
    draw = ImageDraw.Draw(sheet)

    for column, (label, (model, clip)) in enumerate(models.items()):
        vertices = model.pose(clip, model.clip_length(clip) * phase if clip else 0.0)
        centre = ((vertices[:, 0].min() + vertices[:, 0].max()) / 2.0,
                  (vertices[:, 1].min() + vertices[:, 1].max()) / 2.0)
        image = render(model, vertices, width, height, yaw=np.radians(20), pitch=np.radians(-4),
                       scale=1.55 / max(vertices[:, 1].max() - vertices[:, 1].min(), 0.5), centre=centre)
        sheet.paste(Image.fromarray((image * 255).astype(np.uint8)), (width * column, 0))
        draw.text((width * column + 10, height + 10), label, fill=(205, 214, 226))

    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out)
    print(f"wrote {out}")


def walk_gif(models: dict[str, tuple[Model, str]], out: Path, frames: int = 14, fps: int = 12,
             width: int = 300, height: int = 520) -> None:
    columns = len(models)
    images = []
    for frame in range(frames):
        sheet = Image.new("RGB", (width * columns, height), (10, 12, 16))
        for column, (label, (model, clip)) in enumerate(models.items()):
            length = model.clip_length(clip) if clip else 1.0
            time = ((frame / frames) + column * 0.17) % 1.0 * length
            vertices = model.pose(clip, time)
            centre = ((vertices[:, 0].min() + vertices[:, 0].max()) / 2.0,
                      (vertices[:, 1].min() + vertices[:, 1].max()) / 2.0)
            image = render(model, vertices, width, height, yaw=np.radians(20), pitch=np.radians(-4),
                           scale=1.55 / max(vertices[:, 1].max() - vertices[:, 1].min(), 0.5),
                           centre=centre)
            sheet.paste(Image.fromarray((image * 255).astype(np.uint8)), (width * column, 0))
        images.append(sheet)

    out.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(out, save_all=True, append_images=images[1:], duration=int(1000 / fps), loop=0, optimize=True)
    print(f"wrote {out} ({len(images)} frames)")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--glb-dir", default="tools/anny_zombies/build")
    parser.add_argument("--texture-dir", default="Assets/Resources/Zombies")
    parser.add_argument("--variants", default="shambler,bloater,fresh,small")
    parser.add_argument("--out", default="preview/zombies_textured.png")
    parser.add_argument("--gif", default=None)
    parser.add_argument("--clip", default="Shamble")
    args = parser.parse_args()

    models: dict[str, tuple[Model, str]] = {}
    for variant in args.variants.split(","):
        glb = Path(args.glb_dir) / f"zombie_{variant}.glb"
        texture = Path(args.texture_dir) / f"zombie_{variant}_albedo.png"
        model = Model(glb, texture if texture.exists() else None)
        height = model.rest[:, 1].max() - model.rest[:, 1].min()
        models[f"{variant}  {height:.2f} m"] = (model, args.clip)

    contact_sheet(models, Path(args.out))
    if args.gif:
        walk_gif(models, Path(args.gif))
    return 0


if __name__ == "__main__":
    sys.exit(main())
