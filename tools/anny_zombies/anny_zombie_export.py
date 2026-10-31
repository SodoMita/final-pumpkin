#!/usr/bin/env python3
"""Bake naver/anny bodies into animated glTF-binary zombies for Unity.

Anny (https://github.com/naver/anny) is a parametric human body model: it gives
a rigged, skinned mesh plus phenotype controls, but no motion data and no engine
exporter. This script fills both gaps:

  * VARIANTS turns phenotype + local-change values into four zombie bodies.
  * shamble()/idle()/lunge() author a gait as per-bone rotations about
    character-space axes, sampled into keyframes.
  * GltfBuilder writes a self-contained .glb (mesh, skin, inverse bind
    matrices, and the three clips) with no third-party glTF dependency.

The result is Y-up (Anny is Z-up), grounded at y=0, and ready for runtime loading
by Unity's glTFast.

Usage:
    python tools/anny_zombies/anny_zombie_export.py --variant shambler --out tools/anny_zombies/build/zombie_shambler.glb
    python tools/anny_zombies/anny_zombie_export.py --probe
"""

from __future__ import annotations

import argparse
import json
import math
import struct
import sys
from pathlib import Path

import numpy as np

# --------------------------------------------------------------------------- #
# variants
# --------------------------------------------------------------------------- #

# Phenotype axes are 0..1 (gender: 0 = female, 1 = male). Local changes are
# signed multipliers on named regions and are what actually make a body read as
# "wrong": wasted shoulders, a swollen torso, a sunken jaw.
VARIANTS: dict[str, dict] = {
    "shambler": {
        "phenotype": {"gender": 0.74, "age": 0.42, "muscle": 0.28, "weight": 0.34,
                      "height": 0.735, "proportions": 0.58},
        "local_changes": {
            "r-upperarm-muscle-incr": 0.8,
            "l-upperleg-fat-incr": -0.6,
            "torso-scale-depth-incr": -0.35,
            "head-fat-incr": -0.5,
            "chin-jaw-drop-incr": 0.6,
            "head-scale-vert-incr": -0.3,
        },
    },
    "bloater": {
        "phenotype": {"gender": 0.6, "age": 0.5, "muscle": 0.2, "weight": 1.0,
                      "height": 0.56, "proportions": 0.42},
        "local_changes": {
            "torso-scale-horiz-incr": 1.0,
            "torso-scale-depth-incr": 0.9,
            "l-upperleg-fat-incr": 1.0,
            "r-upperleg-fat-incr": 1.0,
            "head-fat-incr": 0.5,
            "r-upperarm-muscle-incr": -0.4,
        },
    },
    "fresh": {
        "phenotype": {"gender": 0.28, "age": 0.19, "muscle": 0.45, "weight": 0.3,
                      "height": 0.5, "proportions": 0.4},
        "local_changes": {
            "head-fat-incr": -0.4,
            "chin-jaw-drop-incr": 0.8,
            "r-upperarm-muscle-incr": -0.5,
            "torso-scale-horiz-incr": -0.3,
        },
    },
    # A child zombie rather than an infant: Anny's age axis is very sensitive,
    # so this stays around 1.1 m.
    "small": {
        "phenotype": {"gender": 0.5, "age": 0.13, "muscle": 0.22, "weight": 0.22,
                      "height": 0.34, "proportions": 0.35},
        "local_changes": {
            "head-fat-incr": 0.4,
            "chin-jaw-drop-incr": 0.5,
            "l-upperarm-muscle-incr": -0.4,
        },
    },
}

BASE_COLOUR = [0.42, 0.47, 0.36, 1.0]  # fallback when no albedo is assigned

# Anny's UV atlas is MakeHuman's "tights" layout: the body islands are unwrapped
# but the head is one blob that ignores the face, so a portrait cannot be
# projected into it. The head therefore gets its own lat-long chart, parked in a
# free corner of the same atlas; tools/anny_zombies/texture_bake.py paints that
# rectangle with the AI face and everything else with the AI skin.
HEAD_BONES = ("head", "neck", "eye")
HEAD_CHART = (0.02, 0.52, 0.34, 0.98)  # u0, v0, u1, v1

# --------------------------------------------------------------------------- #
# coordinate frames
# --------------------------------------------------------------------------- #

# Anny is Z-up; glTF/Unity is Y-up. This maps (x, y, z) -> (x, z, -y), which
# also turns Anny's -Y facing into +Z (Unity's forward).
ZUP_TO_YUP = np.array([[1.0, 0.0, 0.0],
                       [0.0, 0.0, 1.0],
                       [0.0, -1.0, 0.0]])


def to_yup_points(points: np.ndarray) -> np.ndarray:
    return points @ ZUP_TO_YUP.T


def to_yup_transforms(matrices: np.ndarray) -> np.ndarray:
    rotation = np.eye(4)
    rotation[:3, :3] = ZUP_TO_YUP
    return rotation @ matrices @ rotation.T


# --------------------------------------------------------------------------- #
# small math helpers
# --------------------------------------------------------------------------- #


def axis_angle(axis: np.ndarray, angle: float) -> np.ndarray:
    axis = axis / np.linalg.norm(axis)
    s = math.sin(angle * 0.5)
    return np.array([axis[0] * s, axis[1] * s, axis[2] * s, math.cos(angle * 0.5)])


def quat_mul(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return np.array([
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    ])


def quat_to_matrix(q: np.ndarray) -> np.ndarray:
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])


def matrix_to_quat(m: np.ndarray) -> np.ndarray:
    """Rotation part of a 4x4 matrix as an (x, y, z, w) quaternion."""
    r = m[:3, :3]
    trace = r[0, 0] + r[1, 1] + r[2, 2]
    if trace > 0.0:
        s = math.sqrt(trace + 1.0) * 2.0
        q = [ (r[2, 1] - r[1, 2]) / s, (r[0, 2] - r[2, 0]) / s,
              (r[1, 0] - r[0, 1]) / s, 0.25 * s ]
    elif r[0, 0] > r[1, 1] and r[0, 0] > r[2, 2]:
        s = math.sqrt(1.0 + r[0, 0] - r[1, 1] - r[2, 2]) * 2.0
        q = [ 0.25 * s, (r[0, 1] + r[1, 0]) / s,
              (r[0, 2] + r[2, 0]) / s, (r[2, 1] - r[1, 2]) / s ]
    elif r[1, 1] > r[2, 2]:
        s = math.sqrt(1.0 + r[1, 1] - r[0, 0] - r[2, 2]) * 2.0
        q = [ (r[0, 1] + r[1, 0]) / s, 0.25 * s,
              (r[1, 2] + r[2, 1]) / s, (r[0, 2] - r[2, 0]) / s ]
    else:
        s = math.sqrt(1.0 + r[2, 2] - r[0, 0] - r[1, 1]) * 2.0
        q = [ (r[0, 2] + r[2, 0]) / s, (r[1, 2] + r[2, 1]) / s,
              0.25 * s, (r[1, 0] - r[0, 1]) / s ]
    q = np.array(q, dtype=np.float64)
    return q / np.linalg.norm(q)


# --------------------------------------------------------------------------- #
# gaits: bone label -> [(character-space axis, angle in radians), ...]
# Expressing the axes in character space keeps the gait independent of however
# Anny happens to orient a bone's local axes.
# --------------------------------------------------------------------------- #

# Anny is Z-up, so these are the *character* axes expressed in Anny's frame:
# pitch swings a limb forwards/backwards, yaw turns it around the body's vertical
# axis, roll tips it sideways. Negative pitch bends forwards (Anny faces -Y).
PITCH = np.array([1.0, 0.0, 0.0])
YAW = np.array([0.0, 0.0, 1.0])
ROLL = np.array([0.0, 1.0, 0.0])


def shamble(t: float, phase: float = 0.0) -> dict[str, list[tuple[np.ndarray, float]]]:
    """Uneven, dragging walk. t is in loop-normalised [0,1)."""
    a = (t + phase) * math.tau
    step = math.sin(a)
    return {
        # hunched forward, shoulders rolling, head hanging and lolling
        "spine01": [(PITCH, math.radians(-13) + math.radians(2.5) * math.sin(a * 2))],
        "spine03": [(PITCH, math.radians(-8)), (ROLL, math.radians(3.5) * math.sin(a))],
        "spine05": [(PITCH, math.radians(-5)), (ROLL, math.radians(4) * math.sin(a + 0.6))],
        "neck01": [(PITCH, math.radians(-10))],
        "head": [(PITCH, math.radians(14)), (YAW, math.radians(6) * math.sin(a * 0.5)),
                 (ROLL, math.radians(8) * math.sin(a * 0.7 + 1.0))],
        # left leg swings forward, right drags
        "upperleg01.L": [(PITCH, math.radians(-22) * step)],
        "upperleg02.L": [(PITCH, math.radians(-6) * step)],
        "lowerleg01.L": [(PITCH, math.radians(16) * max(0.0, -step))],
        "upperleg01.R": [(PITCH, math.radians(20) * step)],
        "upperleg02.R": [(PITCH, math.radians(4) * step)],
        "lowerleg01.R": [(PITCH, math.radians(12) * max(0.0, step))],
        # arms out in front, the left one higher than the right, swaying out of
        # phase with the legs
        # the arms stay in the sagittal plane (pitch only): that keeps them
        # reaching forward with the elbows low, without the shoulder rolling
        "upperarm01.L": [(PITCH, math.radians(-62) + math.radians(7) * step)],
        "lowerarm01.L": [(PITCH, math.radians(-10))],
        "upperarm01.R": [(PITCH, math.radians(-44) - math.radians(7) * step)],
        "lowerarm01.R": [(PITCH, math.radians(-16))],
    }


def idle(t: float, phase: float = 0.0) -> dict[str, list[tuple[np.ndarray, float]]]:
    """Standing sway: breathing, head lolling, arms twitching."""
    a = (t + phase) * math.tau
    return {
        "spine01": [(PITCH, math.radians(-10)), (ROLL, math.radians(2) * math.sin(a))],
        "spine03": [(PITCH, math.radians(-7) + math.radians(1.5) * math.sin(a * 2))],
        "neck01": [(PITCH, math.radians(-11))],
        "head": [(PITCH, math.radians(12)), (YAW, math.radians(9) * math.sin(a)),
                 (ROLL, math.radians(7) * math.sin(a * 0.6 + 0.4))],
        "upperarm01.L": [(PITCH, math.radians(-34))],
        "lowerarm01.L": [(PITCH, math.radians(-14) + math.radians(4) * math.sin(a * 3))],
        "upperarm01.R": [(PITCH, math.radians(-24))],
        "lowerarm01.R": [(PITCH, math.radians(-12) + math.radians(4) * math.sin(a * 3 + 1.2))],
        "upperleg01.L": [(PITCH, math.radians(-3))],
        "upperleg01.R": [(PITCH, math.radians(3))],
    }


def lunge(t: float, phase: float = 0.0) -> dict[str, list[tuple[np.ndarray, float]]]:
    """One shot: both arms shoot forward at head height, then settle back."""
    reach = math.sin(min(1.0, t / 0.45) * math.pi * 0.5) * (1.0 - max(0.0, (t - 0.7) / 0.3))
    return {
        "spine01": [(PITCH, math.radians(-16) * reach)],
        "spine03": [(PITCH, math.radians(-10) * reach)],
        "neck01": [(PITCH, math.radians(-11) + math.radians(6) * reach)],
        "upperarm01.L": [(PITCH, math.radians(-60) - math.radians(22) * reach)],
        "lowerarm01.L": [(PITCH, math.radians(-10) + math.radians(12) * reach)],
        "upperarm01.R": [(PITCH, math.radians(-44) - math.radians(24) * reach)],
        "lowerarm01.R": [(PITCH, math.radians(-16) + math.radians(12) * reach)],
        "upperleg01.L": [(PITCH, math.radians(-10) * reach)],
        "upperleg01.R": [(PITCH, math.radians(7) * reach)],
    }


CLIPS = [
    {"name": "Shamble", "fn": shamble, "seconds": 1.6, "loop": True, "frames": 21},
    {"name": "Idle", "fn": idle, "seconds": 3.0, "loop": True, "frames": 25},
    {"name": "Lunge", "fn": lunge, "seconds": 1.2, "loop": False, "frames": 25},
]


def gait_delta(entries: list[tuple[np.ndarray, float]]) -> np.ndarray:
    """Compose a gait entry into a 4x4 delta.

    Anny's default pose parameterisation is 'local-ref', where each delta is
    already expressed in world-aligned axes, so character-space axes can be used
    as-is and "pitch" means the same thing on every bone.
    """
    delta = np.array([0.0, 0.0, 0.0, 1.0])
    for axis, angle in entries:
        delta = quat_mul(delta, axis_angle(axis, angle))
    matrix = np.eye(4)
    matrix[:3, :3] = quat_to_matrix(delta)
    return matrix


# --------------------------------------------------------------------------- #
# glTF writer
# --------------------------------------------------------------------------- #

COMPONENT_FLOAT = 5126
COMPONENT_USHORT = 5123
COMPONENT_UINT = 5125


class GltfBuilder:
    """Minimal glTF 2.0 binary writer: one skinned mesh, one skin, N clips."""

    extras: dict = {}

    def __init__(self) -> None:
        self.bin = bytearray()
        self.accessors: list[dict] = []
        self.buffer_views: list[dict] = []
        self.nodes: list[dict] = []
        self.meshes: list[dict] = []
        self.animations: list[dict] = []
        self.skins: list[dict] = []
        self.materials: list[dict] = []

    # -- buffers ---------------------------------------------------------- #

    def _view(self, data: bytes, target: int | None = None) -> int:
        while len(self.bin) % 4:
            self.bin.append(0)
        offset = len(self.bin)
        self.bin.extend(data)
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(data)}
        if target is not None:
            view["target"] = target
        self.buffer_views.append(view)
        return len(self.buffer_views) - 1

    def accessor(self, array: np.ndarray, component_type: int, type_name: str,
                 target: int | None = None, normalized: bool = False) -> int:
        data = np.ascontiguousarray(array).tobytes()
        view = self._view(data, target)
        accessor = {
            "bufferView": view,
            "componentType": component_type,
            "count": int(array.shape[0]),
            "type": type_name,
        }
        if normalized:
            accessor["normalized"] = True
        # min/max are required for POSITION and harmless elsewhere; np.atleast_1d
        # keeps SCALAR accessors from producing bare floats.
        flat = array.reshape(array.shape[0], -1)
        accessor["min"] = [float(v) for v in np.atleast_1d(flat.min(axis=0))]
        accessor["max"] = [float(v) for v in np.atleast_1d(flat.max(axis=0))]
        self.accessors.append(accessor)
        return len(self.accessors) - 1

    # -- assembly --------------------------------------------------------- #

    def build(self, path: Path) -> None:
        while len(self.bin) % 4:
            self.bin.append(0)

        document = {
            "asset": {"version": "2.0", "generator": "anny_zombie_export.py"},
            "scene": 0,
            "scenes": [{"name": "Zombie", "nodes": [0, self.mesh_node]}],
            "nodes": self.nodes,
            "meshes": self.meshes,
            "skins": self.skins,
            "materials": self.materials,
            "accessors": self.accessors,
            "bufferViews": self.buffer_views,
            "buffers": [{"byteLength": len(self.bin)}],
        }
        if self.animations:
            document["animations"] = self.animations
        if self.extras:
            document["extras"] = self.extras

        json_bytes = json.dumps(document, separators=(",", ":")).encode("utf-8")
        while len(json_bytes) % 4:
            json_bytes += b" "

        total = 12 + 8 + len(json_bytes) + 8 + len(self.bin)
        with path.open("wb") as handle:
            handle.write(struct.pack("<III", 0x46546C67, 2, total))
            handle.write(struct.pack("<II", len(json_bytes), 0x4E4F534A))
            handle.write(json_bytes)
            handle.write(struct.pack("<II", len(self.bin), 0x004E4942))
            handle.write(bytes(self.bin))


# --------------------------------------------------------------------------- #
# baking
# --------------------------------------------------------------------------- #


def local_from_global(globals_: np.ndarray, parents: np.ndarray) -> np.ndarray:
    locals_ = np.zeros_like(globals_)
    for index in range(len(globals_)):
        parent = parents[index]
        locals_[index] = globals_[index] if parent < 0 else np.linalg.inv(globals_[parent]) @ globals_[index]
    return locals_


def top_four(weights: np.ndarray, indices: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    order = np.argsort(-weights, axis=1)[:, :4]
    top_w = np.take_along_axis(weights, order, axis=1)
    top_i = np.take_along_axis(indices, order, axis=1)
    total = top_w.sum(axis=1, keepdims=True)
    total[total == 0.0] = 1.0
    return top_w / total, top_i


def head_chart_coordinates(vertices: np.ndarray, faces: np.ndarray,
                           corner_head: np.ndarray, strong: np.ndarray | None = None,
                           eyes: np.ndarray | None = None) -> tuple[np.ndarray, dict]:
    """Lat-long UVs for the head, laid out inside HEAD_CHART.

    The model faces +Z, so azimuth 0 is the face; u wraps the full circle and v
    runs from the crown (0) to the chin (1). Only corners flagged as head are
    meaningful, but the mapping is computed for every vertex so seam triangles
    can use it too.
    """
    # the chart has to be centred on the skull, not on the shoulder vertices that
    # also touch the skull's triangles
    if strong is not None and len(strong) > 0:
        centre = strong.mean(axis=0)
    else:
        centre = vertices.mean(axis=0)
    centre[1] = (vertices[corner_head.any() and np.unique(faces.reshape(-1)[corner_head])][:, 1].min()
                 + vertices[np.unique(faces.reshape(-1)[corner_head])][:, 1].max()) / 2.0 \
        if corner_head.any() else centre[1]
    relative = vertices - centre
    radius = np.maximum(np.linalg.norm(relative, axis=1), 1e-9)
    azimuth = np.arctan2(relative[:, 0], relative[:, 2])
    elevation = np.arcsin(np.clip(relative[:, 1] / radius, -1.0, 1.0))

    u0, v0, u1, v1 = HEAD_CHART
    u = u0 + ((azimuth + math.pi) / (2.0 * math.pi)) * (u1 - u0)
    v = v0 + ((math.pi * 0.5 - elevation) / math.pi) * (v1 - v0)

    # Publish the frame, so the texture baker lines the portrait up with this
    # exact chart instead of guessing where the eyes ended up.
    info = {"u0": u0, "v0": v0, "u1": u1, "v1": v1,
            "centre": [float(c) for c in centre],
            "eye_elevation": 0.0}
    if eyes is not None and len(eyes):
        eye_centroid = eyes.mean(axis=0)
        relative = eye_centroid - centre
        info["eye_elevation"] = float(np.arcsin(np.clip(relative[1] / max(np.linalg.norm(relative), 1e-9), -1, 1)))
    return np.stack([u, v], axis=1), info


def skin_frame(joint_globals: np.ndarray, bind_inverse: np.ndarray,
               positions: np.ndarray, weights: np.ndarray, joint_ids: np.ndarray) -> np.ndarray:
    """Linear blend skinning for one frame, used only to ground the animation."""
    matrices = np.stack([joint_globals[index] @ bind_inverse[index]
                         for index in range(len(joint_globals))])
    out = np.zeros_like(positions)
    for influence in range(weights.shape[1]):
        matrix = matrices[joint_ids[:, influence]]
        points = np.einsum("nij,nj->ni", matrix[:, :3, :3], positions) + matrix[:, :3, 3]
        out += points * weights[:, influence][:, None]
    return out


def bake_variant(model, variant: str, with_animations: bool = True) -> tuple[GltfBuilder, dict]:
    import torch

    spec = VARIANTS[variant]
    labels = list(model.bone_labels)
    parents = np.asarray(list(model.bone_parents), dtype=np.int64)
    bone_count = len(labels)

    with torch.no_grad():
        rest = model(phenotype_kwargs=spec["phenotype"],
                     local_changes_kwargs=spec["local_changes"])

    rest_vertices = rest["rest_vertices"][0].cpu().numpy().astype(np.float64)
    rest_globals_zup = rest["rest_bone_poses"][0].cpu().numpy().astype(np.float64)

    vertices = to_yup_points(rest_vertices)
    globals_ = to_yup_transforms(rest_globals_zup)

    # Anny's soles are not exactly at 0 after posing the rest skeleton.
    ground_offset = float(vertices[:, 1].min())
    vertices[:, 1] -= ground_offset
    globals_[:, 1, 3] -= ground_offset

    # character-space axes -> bone rest frames (Y-up world)
    rest_root_translation = np.eye(4)
    rest_root_translation[:3, 3] = globals_[0, :3, 3]

    locals_ = local_from_global(globals_, parents)

    builder = GltfBuilder()
    builder.materials.append({
        "name": "ZombieSkin",
        "doubleSided": False,
        "pbrMetallicRoughness": {
            "baseColorFactor": BASE_COLOUR,
            "metallicFactor": 0.0,
            "roughnessFactor": 0.85,
        },
    })

    faces = np.asarray(model.faces, dtype=np.int64).reshape(-1, 3)

    weights, joint_indices = top_four(
        np.asarray(model.vertex_bone_weights.detach().cpu().numpy(), dtype=np.float64),
        np.asarray(model.vertex_bone_indices.detach().cpu().numpy(), dtype=np.int64),
    )

    # Anny keeps its UV atlas separate from the positions (21334 UVs for 13718
    # vertices), so the mesh has to be split along the UV seams the way a normal
    # exporter would: one vertex per unique (position, uv) pair.
    uv_all = np.asarray(model.texture_coordinates.detach().cpu().numpy(), dtype=np.float64)
    face_uv_all = np.asarray(model.face_texture_coordinate_indices.detach().cpu().numpy(), dtype=np.int64).reshape(-1)

    # per-corner UVs: Anny's atlas, except that every corner of a head triangle
    # is moved onto the head chart (a face may not straddle the two, or it would
    # stretch across the atlas)
    corner_uv = uv_all[face_uv_all].copy()
    original_faces = faces.copy()
    head_joints = np.array([any(key in label for key in HEAD_BONES) for label in labels])
    head_weight = np.zeros(len(labels))
    head_weight[head_joints] = 1.0
    head_influence = (weights * head_weight[joint_indices]).sum(axis=1)
    vertex_head = head_influence > 0.15
    corner_head = vertex_head[original_faces.reshape(-1)].reshape(-1, 3).any(axis=1)
    corner_head = corner_head[:, None].repeat(3, axis=1).reshape(-1)

    eye_labels = [i for i, label in enumerate(labels) if label.startswith("eye")]
    eyes = globals_[eye_labels, :3, 3] if eye_labels else None
    chart_uv, chart_info = head_chart_coordinates(vertices, original_faces, corner_head,
                                                  strong=vertices[head_influence > 0.55],
                                                  eyes=eyes)
    corner_uv[corner_head] = chart_uv[original_faces.reshape(-1)][corner_head]

    keys = original_faces.reshape(-1).astype(np.int64) * (1 << 24) + \
        (corner_uv[:, 0] * 65535).astype(np.int64) * 256 + (corner_uv[:, 1] * 65535).astype(np.int64)
    _, first_corner, inverse = np.unique(keys, return_index=True, return_inverse=True)
    source_vertex = original_faces.reshape(-1)[first_corner]
    faces = inverse.reshape(-1, 3).astype(np.int64)

    vertices = vertices[source_vertex]
    weights = weights[source_vertex]
    joint_indices = joint_indices[source_vertex]
    uvs = corner_uv[first_corner]

    # the animation grounding below skins the bind pose, so it has to use the
    # expanded vertex set too
    rest_positions = vertices.astype(np.float64)

    position_accessor = builder.accessor(vertices.astype(np.float32), COMPONENT_FLOAT, "VEC3", target=34962)
    normals = np.zeros_like(vertices)
    tri = vertices[faces]
    face_normals = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    for corner in range(3):
        np.add.at(normals, faces[:, corner], face_normals)
    lengths = np.linalg.norm(normals, axis=1, keepdims=True)
    lengths[lengths == 0.0] = 1.0
    normals /= lengths

    uv_accessor = builder.accessor(uvs.astype(np.float32), COMPONENT_FLOAT, "VEC2", target=34962)
    normal_accessor = builder.accessor(normals.astype(np.float32), COMPONENT_FLOAT, "VEC3", target=34962)
    joints_accessor = builder.accessor(joint_indices.astype(np.uint16), COMPONENT_USHORT, "VEC4", target=34962)
    weights_accessor = builder.accessor(weights.astype(np.float32), COMPONENT_FLOAT, "VEC4", target=34962)
    index_accessor = builder.accessor(faces.reshape(-1).astype(np.uint32), COMPONENT_UINT, "SCALAR", target=34963)

    builder.meshes.append({
        "name": "ZombieMesh",
        "primitives": [{
            "attributes": {
                "POSITION": position_accessor,
                "TEXCOORD_0": uv_accessor,
                "NORMAL": normal_accessor,
                "JOINTS_0": joints_accessor,
                "WEIGHTS_0": weights_accessor,
            },
            "indices": index_accessor,
            "material": 0,
            "mode": 4,
        }],
    })

    # node 0 is the armature, bones follow at 1..N, and the skinned mesh sits at
    # the scene root (glTF requires a skinned mesh node to be a root node).
    builder.nodes.append({"name": "ZombieRig", "children": [1] if parents[0] < 0 else []})
    for index in range(bone_count):
        local = locals_[index]
        node = {
            "name": labels[index],
            "translation": [float(v) for v in local[:3, 3]],
            "rotation": [float(v) for v in matrix_to_quat(local)],
        }
        children = [int(child) + 1 for child in np.nonzero(parents == index)[0]]
        if children:
            node["children"] = children
        builder.nodes.append(node)

    ibm = np.stack([np.linalg.inv(globals_[index]) for index in range(bone_count)])
    # accessors store matrices column-major
    ibm_accessor = builder.accessor(np.transpose(ibm, (0, 2, 1)).reshape(bone_count, 16).astype(np.float32),
                                    COMPONENT_FLOAT, "MAT4")

    builder.skins.append({
        "name": "ZombieSkin",
        "skeleton": 1,
        "joints": list(range(1, bone_count + 1)),
        "inverseBindMatrices": ibm_accessor,
    })

    builder.mesh_node = len(builder.nodes)
    builder.nodes.append({"name": "ZombieBody", "mesh": 0, "skin": 0})

    builder.extras["head_chart"] = chart_info
    info = {"variant": variant, "vertices": int(len(vertices)),
            "triangles": int(len(faces)), "bones": bone_count}

    if not with_animations:
        return builder, info

    for clip in CLIPS:
        frames = clip["frames"]
        times = np.linspace(0.0, clip["seconds"], frames)
        # normalised [0,1] for the gait functions; the last frame repeats the
        # first for looping clips so the seam is exact
        deltas: dict[str, list] = {label: [] for label in labels}
        for frame in range(frames):
            t = frame / (frames - 1)
            if clip["loop"]:
                t = t % 1.0 if frame < frames - 1 else 0.0
            entries = clip["fn"](t)
            for label in labels:
                deltas[label].append(gait_delta(entries.get(label, [])))

        # One batched forward pass: (B, J, 4, 4) deltas in, (B, J, 4, 4) globals out
        pose_batch = {
            label: torch.tensor(np.stack(deltas[label]), dtype=torch.float32)
            for label in labels
        }
        with torch.no_grad():
            posed = model(pose_parameters=pose_batch,
                          phenotype_kwargs=spec["phenotype"],
                          local_changes_kwargs=spec["local_changes"])
        posed_globals = to_yup_transforms(posed["bone_poses"].cpu().numpy().astype(np.float64))

        # A posed forward pass drops the root's rest translation (Anny returns
        # bone_poses in a frame where the root sits at the origin), so put it
        # back before measuring anything.
        posed_globals = np.einsum("ij,bkjl->bkil", rest_root_translation, posed_globals)

        # Anny does not plant the feet for us either: ground every frame from the
        # skinned result so the soles stay on y=0 throughout the cycle.
        bind_inverse = np.stack([np.linalg.inv(globals_[index]) for index in range(bone_count)])
        for b in range(frames):
            vertices_b = skin_frame(posed_globals[b], bind_inverse, rest_positions,
                                    weights, joint_indices)
            posed_globals[b, :, 1, 3] -= float(vertices_b[:, 1].min())

        time_accessor = builder.accessor(times.astype(np.float32), COMPONENT_FLOAT, "SCALAR")
        samplers = []
        channels = []
        posed_locals = np.stack([local_from_global(posed_globals[b], parents) for b in range(frames)])

        for index in range(bone_count):
            translations = posed_locals[:, index, :3, 3].astype(np.float32)
            rotations = np.stack([matrix_to_quat(posed_locals[b, index]) for b in range(frames)]).astype(np.float32)
            # keep quaternions on one hemisphere so linear interpolation is short
            for b in range(1, frames):
                if np.dot(rotations[b], rotations[b - 1]) < 0.0:
                    rotations[b] *= -1.0

            t_accessor = builder.accessor(translations, COMPONENT_FLOAT, "VEC3")
            r_accessor = builder.accessor(rotations, COMPONENT_FLOAT, "VEC4")
            samplers.append({"input": time_accessor, "output": t_accessor, "interpolation": "LINEAR"})
            channels.append({"sampler": len(samplers) - 1,
                             "target": {"node": index + 1, "path": "translation"}})
            samplers.append({"input": time_accessor, "output": r_accessor, "interpolation": "LINEAR"})
            channels.append({"sampler": len(samplers) - 1,
                             "target": {"node": index + 1, "path": "rotation"}})

        builder.animations.append({"name": clip["name"], "samplers": samplers, "channels": channels})
        info.setdefault("animations", []).append(clip["name"])

    return builder, info


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--variant", choices=sorted(VARIANTS), help="which zombie to bake")
    parser.add_argument("--out", type=Path, help="output .glb path")
    parser.add_argument("--all", action="store_true", help="bake every variant into --out's folder")
    parser.add_argument("--no-animations", action="store_true")
    parser.add_argument("--probe", action="store_true", help="print the baked height of each variant")
    args = parser.parse_args()

    import torch
    import anny

    torch.set_num_threads(1)
    # phenotype_blendshapes=1 keeps the load around 1 GB; the full set is
    # several GB and gets the process killed on small machines.
    model = anny.Anny(local_changes="all", phenotype_blendshapes=1)

    variants = sorted(VARIANTS) if (args.all or args.probe) else [args.variant]
    if args.variant is None and not args.probe and not args.all:
        parser.error("pass --variant, --all or --probe")

    for variant in variants:
        builder, info = bake_variant(model, variant, with_animations=not args.no_animations)
        if args.probe:
            heights = [None]
            # cheap: measure the baked height from the mesh accessor bounds
            position_accessor = builder.accessors[0]
            info["height_m"] = round(position_accessor["max"][1] - position_accessor["min"][1], 3)
        if args.out is not None and not args.probe:
            if args.all:
                args.out.mkdir(parents=True, exist_ok=True)
                path = args.out / f"zombie_{variant}.glb"
            else:
                path = args.out
                path.parent.mkdir(parents=True, exist_ok=True)
            builder.build(path)
            info["file"] = str(path)
        if "height_m" not in info:
            position_accessor = builder.accessors[0]
            info["height_m"] = round(position_accessor["max"][1] - position_accessor["min"][1], 3)
        print("[anny] wrote " + json.dumps(info), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
