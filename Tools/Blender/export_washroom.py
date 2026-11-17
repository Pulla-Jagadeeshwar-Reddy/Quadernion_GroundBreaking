"""Exports the washroom room (walls, cubicles, toilets, basins, soap dispensers, mirrors, urinals,
air extractor) out of the team's full 1ST_FLOOR.blend into Assets/Routes/Shared/Models/Washroom.fbx.

The source blend (575 MB, team Google Drive "1/1ST_FLOOR.blend") is not kept in the repo.
Usage:
  blender -b --disable-autoexec <path>/1ST_FLOOR.blend --python Tools/Blender/export_washroom.py -- <output.fbx>

Output: centred on the room (x, y), floor at z = 0, walls clipped to the room and 3 m high.
RouteSceneBuilder.BuildWashroom places it flush with the Blackout shutter exit.
"""
import sys

import bpy
from mathutils import Matrix, Vector

# The washroom in the blend's world space (metres, z up).
ROOM_MIN = Vector((4.35, -15.9, 13.3))
ROOM_MAX = Vector((12.21, -9.61, 16.5))
FLOOR_Z = 13.5
CENTRE = Vector(((ROOM_MIN.x + ROOM_MAX.x) / 2, (ROOM_MIN.y + ROOM_MAX.y) / 2, FLOOR_Z))


def world_bounds(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


def main():
    out = sys.argv[sys.argv.index("--") + 1]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    picked = []
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.name == "MAP":
            continue
        lo, hi = world_bounds(obj)
        centre = (lo + hi) / 2
        is_wall = any(c.name == "WALLS" for c in obj.users_collection)
        inside = all(ROOM_MIN[i] - 0.1 <= centre[i] <= ROOM_MAX[i] + 0.4 for i in range(3))
        touches = all(lo[i] < ROOM_MAX[i] and hi[i] > ROOM_MIN[i] for i in range(3))
        if inside or (is_wall and touches):
            picked.append((obj, is_wall))

    copies = []
    for obj, is_wall in picked:
        mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
        mesh.transform(obj.matrix_world)
        if is_wall:
            for v in mesh.vertices:
                v.co.x = min(max(v.co.x, ROOM_MIN.x), ROOM_MAX.x)
                v.co.y = min(max(v.co.y, ROOM_MIN.y), ROOM_MAX.y)
                v.co.z = min(max(v.co.z, ROOM_MIN.z), ROOM_MAX.z)
        mesh.transform(Matrix.Translation(-CENTRE))
        copy = bpy.data.objects.new(obj.name, mesh)
        bpy.context.scene.collection.objects.link(copy)
        copies.append(copy)
        print("WASHROOM part", obj.name, "wall" if is_wall else "")

    bpy.ops.object.select_all(action="DESELECT")
    for copy in copies:
        copy.select_set(True)
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={"MESH"},
                             path_mode="COPY", embed_textures=True, apply_unit_scale=True)
    print("WASHROOM exported", len(copies), "parts to", out)


main()
