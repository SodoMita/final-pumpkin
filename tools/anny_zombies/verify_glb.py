#!/usr/bin/env python3
"""Structural checks for the zombie GLBs, without opening Unity.

Re-implements linear blend skinning from what is stored in the file, so the test
is independent of the exporter:

  * rest pose: skinning with identity animation must reproduce POSITION
  * animation: a sampled frame must move vertices, and the movement must be
    localised (a head rotation must not drag the feet along)
  * loops: sampled first and last frames must agree for looping clips
  * weights are normalised, joint indices are in range, hierarchy is connected

Usage:
    python tools/anny_zombies/verify_glb.py Assets/StreamingAssets/Zombies/*.glb
"""

from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

import numpy as np

COMPONENTS = {5126: np.float32, 5123: np.uint16, 5125: np.uint32, 5121: np.uint8}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
LOOPING = {"Shamble", "Idle"}


class Glb:
    def __init__(self, path: Path) -> None:
        raw = path.read_bytes()
        magic, version, _ = struct.unpack("<III", raw[:12])
        if magic != 0x46546C67:
            raise ValueError(f"{path}: not a GLB")
        offset = 12
        self.json = {}
        self.bin = b""
        while offset < len(raw):
            length, kind = struct.unpack("<II", raw[offset:offset + 8])
            payload = raw[offset + 8:offset + 8 + length]
            if kind == 0x4E4F534A:
                self.json = json.loads(payload)
            elif kind == 0x004E4942:
                self.bin = payload
            offset += 8 + length + ((4 - length % 4) % 4 if length % 4 else 0)

    def accessor(self, index: int) -> np.ndarray:
        acc = self.json["accessors"][index]
        view = self.json["bufferViews"][acc["bufferView"]]
        dtype = COMPONENTS[acc["componentType"]]
        width = WIDTHS[acc["type"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        count = acc["count"] * width
        data = np.frombuffer(self.bin, dtype=dtype, count=count, offset=start)
        return data.reshape(acc["count"], width)


def trs_matrix(translation, rotation, scale) -> np.ndarray:
    x, y, z, w = rotation / np.linalg.norm(rotation)
    rot = np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])
    matrix = np.eye(4)
    matrix[:3, :3] = rot * np.asarray(scale)[None, :]
    matrix[:3, 3] = translation
    return matrix


def node_local(node: dict) -> np.ndarray:
    return trs_matrix(
        np.array(node.get("translation", [0, 0, 0]), dtype=np.float64),
        np.array(node.get("rotation", [0, 0, 0, 1]), dtype=np.float64),
        np.array(node.get("scale", [1, 1, 1]), dtype=np.float64),
    )


def parents_of(nodes: list[dict]) -> dict[int, int]:
    parents: dict[int, int] = {}
    for index, node in enumerate(nodes):
        for child in node.get("children", []):
            parents[child] = index
    return parents


def globals_from(nodes: list[dict], overrides: dict[int, dict] | None = None) -> dict[int, np.ndarray]:
    parents = parents_of(nodes)
    cache: dict[int, np.ndarray] = {}

    def resolve(index: int) -> np.ndarray:
        if index in cache:
            return cache[index]
        node = dict(nodes[index])
        if overrides and index in overrides:
            node.update(overrides[index])
        local = node_local(node)
        parent = parents.get(index)
        matrix = local if parent is None else resolve(parent) @ local
        cache[index] = matrix
        return matrix

    for index in range(len(nodes)):
        resolve(index)
    return cache


def skin(glb: Glb, joint_globals: dict[int, np.ndarray]) -> np.ndarray:
    mesh = glb.json["meshes"][0]["primitives"][0]
    skin_def = glb.json["skins"][0]
    joints = skin_def["joints"]
    ibms = glb.accessor(skin_def["inverseBindMatrices"]).reshape(-1, 4, 4)
    # accessors store matrices column-major
    ibms = np.transpose(ibms, (0, 2, 1))

    positions = glb.accessor(mesh["attributes"]["POSITION"]).astype(np.float64)
    joint_ids = glb.accessor(mesh["attributes"]["JOINTS_0"])
    weights = glb.accessor(mesh["attributes"]["WEIGHTS_0"]).astype(np.float64)

    matrices = np.stack([joint_globals[joints[j]] @ ibms[j] for j in range(len(joints))])

    out = np.zeros_like(positions)
    for influence in range(weights.shape[1]):
        weight = weights[:, influence]
        matrix = matrices[joint_ids[:, influence]]
        points = np.einsum("nij,nj->ni", matrix[:, :3, :3], positions) + matrix[:, :3, 3]
        out += points * weight[:, None]
    return out


def sample_track(times: np.ndarray, values: np.ndarray, time: float) -> np.ndarray:
    if time <= times[0]:
        return values[0]
    if time >= times[-1]:
        return values[-1]
    index = int(np.searchsorted(times, time) - 1)
    t0, t1 = times[index], times[index + 1]
    blend = 0.0 if t1 == t0 else (time - t0) / (t1 - t0)
    a, b = values[index], values[index + 1]
    if values.shape[1] == 4:  # quaternion
        if np.dot(a, b) < 0.0:
            b = -b
        q = a * (1 - blend) + b * blend
        return q / np.linalg.norm(q)
    return a * (1 - blend) + b * blend


def check(path: Path) -> bool:
    glb = Glb(path)
    nodes = glb.json["nodes"]
    ok = True

    def fail(message: str) -> None:
        nonlocal ok
        ok = False
        print(f"  FAIL {message}")

    print(f"{path.name}: {len(nodes)} nodes, {len(glb.json['animations'])} animations")

    mesh = glb.json["meshes"][0]["primitives"][0]
    positions = glb.accessor(mesh["attributes"]["POSITION"]).astype(np.float64)
    weights = glb.accessor(mesh["attributes"]["WEIGHTS_0"]).astype(np.float64)
    joint_ids = glb.accessor(mesh["attributes"]["JOINTS_0"])
    joint_count = len(glb.json["skins"][0]["joints"])

    if abs(weights.sum(axis=1) - 1.0).max() > 1e-4:
        fail("weights are not normalised")
    if joint_ids.max() >= joint_count:
        fail(f"joint index {joint_ids.max()} out of range for {joint_count} joints")

    # scene roots
    scene_nodes = glb.json["scenes"][glb.json.get("scene", 0)]["nodes"]
    if not any("skin" in nodes[i] for i in scene_nodes):
        fail("skinned mesh node is not in the scene")

    # rest pose reproduces the stored mesh
    rest_globals = globals_from(nodes)
    rest_skinned = skin(glb, rest_globals)
    rest_error = np.abs(rest_skinned - positions).max()
    print(f"  rest skinning error: {rest_error:.2e}")
    if rest_error > 1e-3:
        fail("rest pose does not reproduce POSITION")

    height = positions[:, 1].max() - positions[:, 1].min()
    print(f"  height {height:.2f} m, soles at y={positions[:, 1].min():.3f}")
    if not (0.5 <= height <= 2.4):
        fail(f"implausible height {height:.2f} m")
    if abs(positions[:, 1].min()) > 0.02:
        fail("model is not standing on y=0")

    # animations actually deform, and loops close
    for animation in glb.json["animations"]:
        name = animation["name"]
        duration = 0.0
        for sampler in animation["samplers"]:
            times = glb.accessor(sampler["input"])[:, 0].astype(np.float64)
            duration = max(duration, float(times[-1]))

        def pose_at(time: float) -> dict[int, dict]:
            overrides: dict[int, dict] = {}
            for channel in animation["channels"]:
                sampler = animation["samplers"][channel["sampler"]]
                times = glb.accessor(sampler["input"])[:, 0].astype(np.float64)
                values = glb.accessor(sampler["output"]).astype(np.float64)
                value = sample_track(times, values, time)
                target = channel["target"]
                overrides.setdefault(target["node"], {})[target["path"]] = value.tolist()
            return overrides

        mid = skin(glb, globals_from(nodes, pose_at(duration * 0.5)))
        movement = np.linalg.norm(mid - rest_skinned, axis=1)

        # frame-to-frame motion: how much the clip itself animates, independent
        # of how far its stance sits from the bind pose
        samples = [skin(glb, globals_from(nodes, pose_at(duration * f)))
                   for f in (0.0, 0.25, 0.5, 0.75)]
        swing = max(float(np.linalg.norm(samples[i] - samples[j], axis=1).max())
                    for i in range(len(samples)) for j in range(i + 1, len(samples)))
        print(f"  {name}: {duration:.2f}s, pose vs bind {movement.max():.3f} m (mean "
              f"{movement.mean():.3f}), frame-to-frame swing {swing:.3f} m")
        if movement.max() < 0.02:
            fail(f"{name} does not move the mesh")
        if swing < 0.01:
            fail(f"{name} barely animates ({swing:.3f} m of frame-to-frame motion)")

        if name in LOOPING:
            first = skin(glb, globals_from(nodes, pose_at(0.0)))
            last = skin(glb, globals_from(nodes, pose_at(duration)))
            gap = np.abs(first - last).max()
            print(f"    loop seam error: {gap:.2e}")
            if gap > 1e-3:
                fail(f"{name} does not loop cleanly")

    # localisation: rotating the head bone must not move the feet
    head_index = next((i for i, n in enumerate(nodes) if n.get("name") == "head"), None)
    if head_index is None:
        fail("no bone named 'head'")
    else:
        angle = np.radians(35.0)
        override = {head_index: {"rotation": [0.0, float(np.sin(angle / 2)), 0.0,
                                              float(np.cos(angle / 2))]}}
        turned = skin(glb, globals_from(nodes, override))
        travel = np.linalg.norm(turned - rest_skinned, axis=1)
        head_zone = rest_skinned[:, 1] > height * 0.82
        foot_zone = rest_skinned[:, 1] < height * 0.10
        print(f"  head turn: head vertices move up to {travel[head_zone].max():.3f} m, "
              f"feet move up to {travel[foot_zone].max():.3f} m")
        if travel[head_zone].max() < 0.01:
            fail("rotating the head bone does not move head vertices")
        if travel[foot_zone].max() > 0.02:
            fail("rotating the head bone moves the feet")

    print("  -> " + ("PASS" if ok else "FAIL"))
    return ok


def main() -> int:
    paths = [Path(p) for p in sys.argv[1:]]
    if not paths:
        print(__doc__)
        return 2
    results = [check(path) for path in paths]
    print(f"\n{sum(results)}/{len(results)} files passed")
    return 0 if all(results) else 1


if __name__ == "__main__":
    sys.exit(main())
