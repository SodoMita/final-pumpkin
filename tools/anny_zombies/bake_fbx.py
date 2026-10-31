#!/usr/bin/env python3
"""Convert the exported zombie GLBs into FBX for Unity's native importer.

Unity handles FBX best: the model importer builds the skinned mesh, the skeleton
and one clip per action as sub-assets, with no runtime glTF package involved.
Blender is used as the converter because assimp's FBX writer silently drops
skinned animation.

Run headless:
    blender --background --python tools/anny_zombies/bake_fbx.py -- --in Assets/StreamingAssets/Zombies --out Assets/Resources/Zombies
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import bpy


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--in", dest="source", default="Assets/StreamingAssets/Zombies")
    parser.add_argument("--out", dest="target", default="Assets/Resources/Zombies")
    parser.add_argument("--variants", default="shambler,bloater,fresh,small")
    return parser.parse_args(argv)


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def convert(glb_path: Path, fbx_path: Path) -> dict:
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(glb_path))

    # Drop anything that is not part of the zombie: the importer (and Blender's
    # factory startup) can leave stray objects behind, and a stray mesh in the
    # FBX would end up as an extra GameObject in Unity.
    for obj in list(bpy.context.scene.objects):
        if obj.name.lower().startswith("icosphere") or (obj.type == "MESH" and not obj.parent):
            bpy.data.objects.remove(obj, do_unlink=True)

    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    actions = list(bpy.data.actions)

    # One action per clip, all named the way the driver expects. The glTF
    # importer stores them as actions already; make sure they survive export.
    for action in actions:
        # the importer suffixes actions with the armature name ("Shamble_ZombieRig");
        # Unity shows the clip name, and the driver looks these up by name
        action.name = action.name.split("_")[0]
        action.use_fake_user = True
        for fcurve in action.fcurves:
            for keyframe in fcurve.keyframe_points:
                keyframe.interpolation = "LINEAR"

    if armatures:
        armature = armatures[0]
        armature.animation_data_create()
        if actions:
            armature.animation_data.action = actions[0]

    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=False,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_NONE",
        axis_forward="-Z",
        axis_up="Y",
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        add_leaf_bones=False,
        use_mesh_modifiers=False,
        mesh_smooth_type="FACE",
        path_mode="STRIP",
        embed_textures=False,
        bake_space_transform=False,
    )

    return {"file": str(fbx_path), "armatures": len(armatures), "meshes": len(meshes),
            "actions": [a.name for a in actions]}


def main() -> int:
    args = parse_args()
    source = Path(args.source)
    target = Path(args.target)
    for variant in args.variants.split(","):
        glb = source / f"zombie_{variant}.glb"
        if not glb.exists():
            print(f"[fbx] missing {glb}")
            continue
        info = convert(glb, target / f"zombie_{variant}.fbx")
        print(f"[fbx] {info}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
