"""Bakes MAIN-ROOF.fbx into Assets/Routes/Shared/Models/roof_mesh.json (Blender world-space
triangles) for RouteSceneBuilder. Unity imports that FBX as a tiny stub, so the builder makes the
roof mesh itself from this file.
Run: blender -b --python Tools/Blender/export_roof.py
"""
import bpy, bmesh, json, os
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "Assets", "Scenes", "MAIN-ROOF.fbx"))
verts, tris = [], []
for obj in bpy.data.objects:
    if obj.type != 'MESH':
        continue
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    base = len(verts)
    verts += [[round(v.co.x, 4), round(v.co.y, 4), round(v.co.z, 4)] for v in bm.verts]
    bm.verts.index_update()
    tris += [[base + v.index for v in f.verts] for f in bm.faces]
    bm.free()
out = os.path.join(ROOT, "Assets", "Routes", "Shared", "Models", "roof_mesh.json")
with open(out, "w") as f:
    json.dump({"source": "Assets/Scenes/MAIN-ROOF.fbx", "verts": [c for v in verts for c in v], "tris": [i for t in tris for i in t]}, f, separators=(",", ":"))
print("roof triangles", len(tris), "->", out)
