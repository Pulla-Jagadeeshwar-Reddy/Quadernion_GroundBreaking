"""Shared helpers for the route prop scripts: materials, the B mesh builder, FBX export.

Conventions: metric (1 unit = 1 m), Z up, prop front faces Blender -Y, origin at the
bottom-centre of the footprint unless a pivot is given.
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

# Project root = two folders above this file (Tools/Blender/..).
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PREVIEW_DIR = os.path.join(ROOT, "Logs", "blender_previews")


# ----------------------------------------------------------------- scene reset
def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.cameras,
                 bpy.data.lights, bpy.data.worlds, bpy.data.images):
        for d in list(coll):
            coll.remove(d)


# ------------------------------------------------------------------- materials
MATS = {}


def make_mat(name, color, rough, metal=0.0, alpha=1.0, emission=0.0, emit_color=None):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Alpha"].default_value = alpha
    if emission > 0:
        bsdf.inputs["Emission Color"].default_value = (*(emit_color or color), 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    vc = (emit_color or color) if emission > 0 else color
    m.diffuse_color = (*vc, alpha)
    m.roughness = rough
    m.metallic = metal
    if alpha < 1.0 and hasattr(m, "surface_render_method"):
        m.surface_render_method = 'BLENDED'
    MATS[name] = m
    return m


def build_materials():
    MATS.clear()
    make_mat("M_DeskTop", (0.62, 0.57, 0.50), 0.55)
    make_mat("M_Metal", (0.09, 0.10, 0.115), 0.4, 0.8)
    make_mat("M_CasePlastic", (0.17, 0.19, 0.22), 0.5)
    make_mat("M_Screen", (0.015, 0.015, 0.018), 0.2, emission=1.5, emit_color=(1.0, 0.12, 0.08))
    make_mat("M_Keys", (0.45, 0.46, 0.48), 0.5)
    make_mat("M_StatusLight", (0.02, 0.02, 0.02), 0.3, emission=3.0, emit_color=(1.0, 0.1, 0.06))
    make_mat("M_HazardYellow", (0.95, 0.72, 0.04), 0.5)
    make_mat("M_HazardBlack", (0.02, 0.02, 0.025), 0.6)
    make_mat("M_PartitionFabric", (0.34, 0.40, 0.48), 0.9)
    # --- batch A additions (palette names shared with build_route_items.py)
    make_mat("M_Steel", (0.62, 0.64, 0.67), 0.35, 1.0)
    make_mat("M_Wood", (0.45, 0.27, 0.12), 0.7)
    make_mat("M_WoodDark", (0.30, 0.18, 0.08), 0.75)
    make_mat("M_DarkRubber", (0.035, 0.035, 0.04), 0.85)
    make_mat("M_YellowPlastic", (0.95, 0.72, 0.04), 0.4)
    make_mat("M_RedPaint", (0.62, 0.03, 0.02), 0.4)
    make_mat("M_Brass", (0.85, 0.58, 0.16), 0.35, 1.0)
    make_mat("M_Iron", (0.11, 0.11, 0.12), 0.6, 1.0)
    make_mat("M_Silver", (0.85, 0.86, 0.9), 0.25, 1.0)
    make_mat("M_Glass", (0.92, 0.96, 1.0), 0.05, 0.0, 0.25)
    make_mat("M_Enamel", (0.92, 0.93, 0.94), 0.35)
    make_mat("M_FreezerInner", (0.72, 0.85, 0.95), 0.4)
    make_mat("M_KeypadScreen", (0.015, 0.015, 0.018), 0.2, emission=1.5,
             emit_color=(1.0, 0.12, 0.08))
    make_mat("M_StatusLightGreen", (0.02, 0.02, 0.02), 0.3, emission=3.0,
             emit_color=(0.1, 1.0, 0.2))


# --------------------------------------------------------------------- builder
class B:
    """Collects geometry in one bmesh, with per-face material names (Z-up)."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.mats = []

    def mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _box_uv(self, f):
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for lp in f.loops:
            c = lp.vert.co
            u, v = ((c.y, c.z), (c.x, c.z), (c.x, c.y))[ax]
            lp[self.uv].uv = (u, v)

    def _finish(self, before, mat, M=None, recalc=True):
        mi = self.mi(mat)
        new = [f for f in self.bm.faces if f not in before]
        if M is not None:
            for v in {v for f in new for v in f.verts}:
                v.co = M @ v.co
        if recalc:
            bmesh.ops.recalc_face_normals(self.bm, faces=new)
        for f in new:
            f.material_index = mi
            f.smooth = False
            f.normal_update()
            self._box_uv(f)
        return new

    def box(self, c, sz, mat, bevel=0.0, rot=None):
        before = set(self.bm.faces)
        vs = bmesh.ops.create_cube(self.bm, size=1.0)['verts']
        for v in vs:
            v.co = Vector((v.co.x * sz[0], v.co.y * sz[1], v.co.z * sz[2]))
        if bevel > 0:
            o = min(bevel, 0.4 * min(sz))
            es = list({e for v in vs for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=es, offset=o, offset_type='OFFSET',
                            segments=1, profile=0.5, affect='EDGES')
        M = Matrix.Translation(Vector(c))
        if rot:
            M = M @ Euler(rot).to_matrix().to_4x4()
        self._finish(before, mat, M)

    def tube(self, pts, radii, seg, mat, up=(0, 0, 1), closed=False, cap=True):
        bm = self.bm
        before = set(bm.faces)
        pts = [Vector(p) for p in pts]
        n = len(pts)
        if not isinstance(radii, (list, tuple)):
            radii = [radii] * n
        tans = []
        for i in range(n):
            if closed:
                t = pts[(i + 1) % n] - pts[(i - 1) % n]
            else:
                t = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
            tans.append(t.normalized())
        up = Vector(up)
        nrm = up - tans[0] * up.dot(tans[0])
        if nrm.length < 1e-6:
            nrm = Vector((1, 0, 0)) - tans[0] * tans[0].x
        nrm.normalize()
        rings = []
        for i in range(n):
            t = tans[i]
            if i > 0:
                nrm = nrm - t * nrm.dot(t)
                nrm.normalize()
            bn = t.cross(nrm)
            ring = []
            for k in range(seg):
                a = 2 * math.pi * k / seg
                off = (nrm * math.cos(a) + bn * math.sin(a)) * radii[i]
                ring.append(bm.verts.new(pts[i] + off))
            rings.append(ring)
        last = n if closed else n - 1
        for i in range(last):
            a, b = rings[i], rings[(i + 1) % n]
            for k in range(seg):
                bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
        if cap and not closed:
            bm.faces.new(rings[0][::-1])
            bm.faces.new(rings[-1])
        self._finish(before, mat)

    def cyl(self, p0, p1, r0, mat, seg=8, r1=None):
        self.tube([p0, p1], [r0, r0 if r1 is None else r1], seg, mat)

    def loft(self, y0, c0, s0, y1, c1, s1, mat):
        """Frustum between two rectangles in XZ planes; c=(x, z), s=(w, h)."""
        bm = self.bm
        before = set(bm.faces)

        def rect(y, c, s):
            return [bm.verts.new((c[0] + sx * s[0] / 2, y, c[1] + sz * s[1] / 2))
                    for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        a, b = rect(y0, c0, s0), rect(y1, c1, s1)
        bm.faces.new(a)
        bm.faces.new(b)
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
        self._finish(before, mat)

    def prism(self, profile, x0, x1, mat):
        """Extrude a convex (y, z) profile along X."""
        bm = self.bm
        before = set(bm.faces)
        a = [bm.verts.new((x0, y, z)) for y, z in profile]
        b = [bm.verts.new((x1, y, z)) for y, z in profile]
        n = len(profile)
        bm.faces.new(a)
        bm.faces.new(b)
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
        self._finish(before, mat)

    def quad(self, pts, mat, uvs=None):
        """Single flat quad; vertex order defines the normal (no recalculation)."""
        bm = self.bm
        before = set(bm.faces)
        f = bm.faces.new([bm.verts.new(p) for p in pts])
        new = self._finish(before, mat, recalc=False)
        if uvs:
            for lp, uv in zip(f.loops, uvs):
                lp[self.uv].uv = uv
        return new

    def beam(self, p0, p1, w, d, mat, bevel=0.0):
        """Box of cross-section w x d running from p0 to p1 (local Z aligned to the span)."""
        p0, p1 = Vector(p0), Vector(p1)
        dv = p1 - p0
        q = Vector((0, 0, 1)).rotation_difference(dv.normalized())
        before = set(self.bm.faces)
        vs = bmesh.ops.create_cube(self.bm, size=1.0)['verts']
        for v in vs:
            v.co = Vector((v.co.x * w, v.co.y * d, v.co.z * dv.length))
        if bevel > 0:
            o = min(bevel, 0.4 * min(w, d))
            es = list({e for v in vs for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=es, offset=o, offset_type='OFFSET',
                            segments=1, profile=0.5, affect='EDGES')
        self._finish(before, mat, Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4())

    def polyprism(self, pts, off, mat):
        """Convex polygon (3D points) extruded by the vector `off`."""
        bm = self.bm
        before = set(bm.faces)
        off = Vector(off)
        a = [bm.verts.new(p) for p in pts]
        b = [bm.verts.new(Vector(p) + off) for p in pts]
        n = len(pts)
        bm.faces.new(a)
        bm.faces.new(b[::-1])
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
        self._finish(before, mat)

    def finalize(self):
        """Origin to bottom-centre of the footprint."""
        co = [v.co for v in self.bm.verts]
        mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
        mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
        shift = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
        for v in self.bm.verts:
            v.co -= shift

    def to_object(self, pivot=None):
        """pivot (prop-space) becomes the object origin; the object stays in place."""
        if pivot is not None:
            pv = Vector(pivot)
            for v in self.bm.verts:
                v.co -= pv
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in self.mats:
            mesh.materials.append(MATS[m])
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        if pivot is not None:
            obj.location = pivot
        return obj


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def export_parts(objs, path):
    """Export placed objects (assembled positions, prop origin at world 0) as one FBX."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False)
    for o in objs:
        o.select_set(False)


def world_bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx
