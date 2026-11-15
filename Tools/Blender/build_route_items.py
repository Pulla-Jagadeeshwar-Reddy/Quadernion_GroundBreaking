"""Procedurally builds low-poly pickup items for the Unity escape game.

Run:  blender -b --factory-startup -P build_route_items.py
Idempotent: clears the scene, rebuilds everything, overwrites outputs.
"""
import math
import os
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

ROOT = r"C:\Users\Zap\Uni\SEM5\AR-VR\GroundBreaking"
BLEND_PATH = os.path.join(ROOT, "ArtSource", "Routes", "RouteItems.blend")
PREVIEW_PATH = (r"C:\Users\Zap\AppData\Local\Temp\claude\700b252225256bde"
                r"\beb4baae-6014-4370-84e6-9d838abe0498\scratchpad\route_items_preview.png")

SHARED = "Assets/Routes/Shared/Models"
FBX_DIRS = {
    "T_Hammer": SHARED, "T_Screwdriver": SHARED, "T_Crowbar": SHARED,
    "R1_DoorKey": "Assets/Routes/Route01_Locksmith/Models",
    "R2_Fuse": "Assets/Routes/Route02_Blackout/Models",
    "R4_Shovel": "Assets/Routes/Route04_Tunnel/Models",
    "R4_HatchKey": "Assets/Routes/Route04_Tunnel/Models",
    "R5_BellKey": "Assets/Routes/Route05_FrozenKey/Models",
    "R5_IceBlock": "Assets/Routes/Route05_FrozenKey/Models",
}

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


def make_mat(name, color, rough, metal=0.0, alpha=1.0, emission=0.0):
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
        bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission
    m.diffuse_color = (*color, alpha)
    m.roughness = rough
    m.metallic = metal
    if alpha < 1.0 and hasattr(m, "surface_render_method"):
        m.surface_render_method = 'BLENDED'
    MATS[name] = m
    return m


def build_materials():
    make_mat("M_Steel", (0.62, 0.64, 0.67), 0.35, 1.0)
    make_mat("M_Wood", (0.45, 0.27, 0.12), 0.7)
    make_mat("M_DarkRubber", (0.035, 0.035, 0.04), 0.85)
    make_mat("M_YellowPlastic", (0.95, 0.72, 0.04), 0.4)
    make_mat("M_RedPaint", (0.62, 0.03, 0.02), 0.4)
    make_mat("M_Brass", (0.85, 0.58, 0.16), 0.35, 1.0)
    make_mat("M_Iron", (0.11, 0.11, 0.12), 0.6, 1.0)
    make_mat("M_Silver", (0.85, 0.86, 0.9), 0.25, 1.0)
    make_mat("M_Ceramic", (0.9, 0.88, 0.8), 0.3)
    make_mat("M_FuseStripe", (0.8, 0.04, 0.03), 0.5)
    make_mat("M_Glass", (0.92, 0.96, 1.0), 0.05, 0.0, 0.25)
    make_mat("M_Bolt", (1.0, 0.48, 0.0), 0.4, 0.0, 1.0, emission=0.6)
    make_mat("M_Ice", (0.7, 0.88, 1.0), 0.1, 0.0, 0.55)


# --------------------------------------------------------------------- builder
class B:
    """Collects geometry in one bmesh, with per-face material names."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _finish(self, before, mat, M=None):
        mi = self.mi(mat)
        new = [f for f in self.bm.faces if f not in before]
        for f in new:
            f.material_index = mi
            f.smooth = False
        if M is not None:
            for v in {v for f in new for v in f.verts}:
                v.co = M @ v.co
        return new

    # axis-aligned (optionally rotated) box with optional 1-segment bevel
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

    # swept tube along a polyline (parallel-transport frames)
    def tube(self, pts, radii, seg, mat, up=(0, 0, 1), aspect=(1.0, 1.0), closed=False,
             cap=True, seg_scale=None, rot=0.0):
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
                a = 2 * math.pi * k / seg + rot
                s = seg_scale[k % len(seg_scale)] if seg_scale else 1.0
                off = (nrm * math.cos(a) * aspect[0] + bn * math.sin(a) * aspect[1]) * radii[i] * s
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

    def cyl(self, p0, p1, r0, mat, seg=8, r1=None, up=(0, 0, 1), rot=0.0, seg_scale=None):
        self.tube([p0, p1], [r0, r0 if r1 is None else r1], seg, mat, up=up, rot=rot,
                  seg_scale=seg_scale)

    def ring(self, c, R, r, normal_axis, mat, n=16, seg=6):
        c = Vector(c)
        if normal_axis == 'Z':
            pts = [c + Vector((R * math.cos(a), R * math.sin(a), 0))
                   for a in (2 * math.pi * i / n for i in range(n))]
            up = (0, 0, 1)
        elif normal_axis == 'Y':
            pts = [c + Vector((R * math.cos(a), 0, R * math.sin(a)))
                   for a in (2 * math.pi * i / n for i in range(n))]
            up = (0, 1, 0)
        else:
            pts = [c + Vector((0, R * math.cos(a), R * math.sin(a)))
                   for a in (2 * math.pi * i / n for i in range(n))]
            up = (1, 0, 0)
        self.tube(pts, r, seg, mat, up=up, closed=True)

    # surface of revolution around the Y axis; profile = [(radius, y), ...]
    def lathe(self, profile, seg, mat, c=(0, 0, 0)):
        bm = self.bm
        before = set(bm.faces)
        c = Vector(c)
        rings = []
        for r, y in profile:
            if r < 1e-9:
                rings.append([bm.verts.new(c + Vector((0, y, 0)))])
            else:
                rings.append([bm.verts.new(c + Vector((r * math.cos(2 * math.pi * k / seg), y,
                                                       r * math.sin(2 * math.pi * k / seg))))
                              for k in range(seg)])
        for i in range(len(rings) - 1):
            a, b = rings[i], rings[i + 1]
            for k in range(seg):
                k2 = (k + 1) % seg
                if len(a) == 1:
                    bm.faces.new((a[0], b[k2], b[k]))
                elif len(b) == 1:
                    bm.faces.new((a[k], a[k2], b[0]))
                else:
                    bm.faces.new((a[k], a[k2], b[k2], b[k]))
        self._finish(before, mat)

    def merge(self, other, M):
        """Copy another builder's geometry (with materials) into this one."""
        vmap = {}
        for v in other.bm.verts:
            vmap[v] = self.bm.verts.new(M @ v.co)
        for f in other.bm.faces:
            nf = self.bm.faces.new([vmap[v] for v in f.verts])
            nf.material_index = self.mi(other.mats[f.material_index])
            nf.smooth = False

    def finalize(self):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.centre()

    def centre(self):
        co = [v.co for v in self.bm.verts]
        mn = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
        mx = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
        mid = (mn + mx) * 0.5
        for v in self.bm.verts:
            v.co -= mid

    def to_object(self):
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in self.mats:
            mesh.materials.append(MATS[m])
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ---------------------------------------------------------------------- items
def hammer():
    b = B("T_Hammer")
    # handle (wood above the grip) and rubber grip with chamfered ends
    b.tube([(0, 0.098, 0), (0, 0.30, 0)], [0.016, 0.0145], 8, 'M_Wood')
    b.tube([(0, 0.0, 0), (0, 0.005, 0), (0, 0.095, 0), (0, 0.100, 0)],
           [0.014, 0.018, 0.018, 0.0165], 8, 'M_DarkRubber')
    yc = 0.315
    b.box((0, yc, 0), (0.06, 0.035, 0.035), 'M_Steel', bevel=0.003)
    # striking face
    b.tube([(0.03, yc, 0), (0.058, yc, 0), (0.066, yc, 0)], [0.02, 0.02, 0.0165], 8,
           'M_Steel', up=(0, 0, 1))
    # split claw: two thin wedges curving slightly down
    for sz in (-1, 1):
        z = 0.0085 * sz
        b.tube([(-0.028, yc, z), (-0.05, yc - 0.004, z), (-0.072, yc - 0.017, z)],
               [0.0177, 0.0135, 0.005], 4, 'M_Steel', up=(0, 0, 1), aspect=(0.45, 1.0),
               rot=math.pi / 4)
    return b


def screwdriver():
    b = B("T_Screwdriver")
    b.tube([(0, -0.004, 0), (0, 0.008, 0)], [0.0135, 0.0155], 12, 'M_DarkRubber')
    b.tube([(0, 0.008, 0), (0, 0.018, 0), (0, 0.095, 0), (0, 0.11, 0)],
           [0.0135, 0.0185, 0.0185, 0.0125], 12, 'M_YellowPlastic',
           seg_scale=[1.0, 0.82])
    b.cyl((0, 0.108, 0), (0, 0.128, 0), 0.0065, 'M_Steel', seg=8, r1=0.0055)
    b.cyl((0, 0.125, 0), (0, 0.222, 0), 0.004, 'M_Steel', seg=8)
    b.box((0, 0.236, 0), (0.0125, 0.028, 0.0025), 'M_Steel', bevel=0.0006)
    return b


def crowbar():
    b = B("T_Crowbar")
    R = 0.08
    cx, cy = R, 0.12
    arc = []
    for k in range(6):
        a = math.radians(180 - 34 * k)
        arc.append((cx + R * math.cos(a), cy + R * math.sin(a), 0))
    # red hexagonal body: straight + first part of the arc
    b.tube([(0, -0.30, 0), (0, cy, 0)] + arc[1:4], 0.012, 6, 'M_RedPaint')
    # steel hook end
    b.tube(arc[3:], [0.0125, 0.0125, 0.0105], 6, 'M_Steel')
    # split claw at the end of the hook
    P = Vector(arc[5])
    t = (Vector(arc[5]) - Vector(arc[4])).normalized()
    for sz in (-1, 1):
        z = Vector((0, 0, 0.0085 * sz))
        b.tube([P + z, P + t * 0.02 + z * 1.15, P + t * 0.045 + z * 1.3],
               [0.017, 0.013, 0.0075], 4, 'M_Steel', up=(0, 0, 1), aspect=(0.45, 1.0),
               rot=math.pi / 4)
    # steel ferrule + 15 degree bent flat chisel
    b.tube([(0, -0.30, 0), (0, -0.34, 0)], 0.0125, 6, 'M_Steel')
    ang = math.radians(15)
    end = Vector((0, -0.34, 0)) + Vector((-math.sin(ang), -math.cos(ang), 0)) * 0.06
    b.tube([(0, -0.34, 0), end], [0.0177, 0.0235], 4, 'M_Steel', up=(0, 0, 1),
           aspect=(1.0, 0.33), rot=math.pi / 4)
    return b


def shovel():
    b = B("R4_Shovel")
    bm = b.bm
    # dished pointed blade: grid sheet then solidify
    before = set(bm.faces)
    ts = [0.0, 0.12, 0.28, 0.45, 0.62, 0.8, 1.0]
    us = [-1.0, -0.5, 0.0, 0.5, 1.0]
    H = 0.28
    rows = []
    for t in ts:
        hw = 0.11 * min(1.0, (t / 0.5) ** 0.7)
        if t == 0.0:
            rows.append([bm.verts.new((0, 0, 0))])
        else:
            rows.append([bm.verts.new((hw * u, t * H, 0.016 * u * u * math.sqrt(t))) for u in us])
    for i in range(len(rows) - 1):
        a, c = rows[i], rows[i + 1]
        for k in range(len(us) - 1):
            if len(a) == 1:
                bm.faces.new((a[0], c[k + 1], c[k]))
            else:
                bm.faces.new((a[k], a[k + 1], c[k + 1], c[k]))
    faces = [f for f in bm.faces if f not in before]
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    for f in faces:  # make sure normals point to +Z so thickness grows toward +Z
        if f.normal.z < 0:
            f.normal_flip()
    bmesh.ops.solidify(bm, geom=faces, thickness=0.006)
    b._finish(before, 'M_Steel', Matrix.Translation((0, 0, -0.003)))
    # socket collar + shaft
    b.tube([(0, 0.215, 0), (0, 0.255, 0), (0, 0.32, 0)], [0.016, 0.0255, 0.0195], 8, 'M_Steel')
    b.cyl((0, 0.28, 0), (0, 1.0, 0), 0.018, 'M_Wood', seg=8)
    # D grip: two side posts + crossbar
    for sx in (-1, 1):
        b.cyl((0.02 * sx, 0.96, 0), (0.10 * sx, 1.02, 0), 0.0135, 'M_DarkRubber', seg=8)
    b.cyl((-0.11, 1.02, 0), (0.11, 1.02, 0), 0.016, 'M_DarkRubber', seg=8, up=(0, 1, 0))
    return b


def door_key_bits(b, mat, bowc, R, tube_r, shaft_r, teeth=True):
    pass


def door_key():
    b = B("R1_DoorKey")
    b.ring((0, 0.095, 0), 0.0205, 0.004, 'Z', 'M_Brass', n=16, seg=6)
    b.cyl((0, 0.0, 0), (0, 0.076, 0), 0.005, 'M_Brass', seg=8)
    b.cyl((0, 0.042, 0), (0, 0.049, 0), 0.0075, 'M_Brass', seg=8)
    b.box((0.0125, 0.017, 0), (0.017, 0.034, 0.005), 'M_Brass', bevel=0.0008)
    for y0, y1 in ((0.0, 0.008), (0.013, 0.021), (0.026, 0.034)):
        b.box((0.0245, (y0 + y1) / 2, 0), (0.007, y1 - y0, 0.005), 'M_Brass', bevel=0.0008)
    return b


def hatch_key():
    b = B("R4_HatchKey")
    # square bow with a hole (frame of four bars)
    b.box((0, 0.116, 0), (0.05, 0.012, 0.012), 'M_Iron')
    b.box((0, 0.154, 0), (0.05, 0.012, 0.012), 'M_Iron')
    b.box((-0.019, 0.135, 0), (0.012, 0.026, 0.012), 'M_Iron')
    b.box((0.019, 0.135, 0), (0.012, 0.026, 0.012), 'M_Iron')
    b.cyl((0, 0.0, 0), (0, 0.114, 0), 0.008, 'M_Iron', seg=8)
    b.box((0, 0.095, 0), (0.026, 0.012, 0.022), 'M_Iron', bevel=0.0015)
    # big blocky stepped bit
    b.box((0.025, 0.007, 0), (0.034, 0.014, 0.014), 'M_Iron', bevel=0.0015)
    b.box((0.019, 0.022, 0), (0.022, 0.016, 0.014), 'M_Iron', bevel=0.0015)
    b.box((0.025, 0.038, 0), (0.034, 0.016, 0.014), 'M_Iron', bevel=0.0015)
    return b


def bell_key():
    b = B("R5_BellKey")
    # clover (quatrefoil) bow
    n = 24
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        r = 0.0205 * (1 + 0.3 * math.cos(4 * a)) / 1.3
        pts.append((r * math.cos(a), 0.095 + r * math.sin(a), 0))
    b.tube(pts, 0.0045, 6, 'M_Silver', closed=True)
    b.cyl((0, 0.0, 0), (0, 0.078, 0), 0.0045, 'M_Silver', seg=8)
    b.cyl((0, 0.040, 0), (0, 0.046, 0), 0.0068, 'M_Silver', seg=8)
    b.box((0.0120, 0.016, 0), (0.016, 0.032, 0.005), 'M_Silver', bevel=0.0008)
    for y0, y1 in ((0.0, 0.009), (0.015, 0.024)):
        b.box((0.0235, (y0 + y1) / 2, 0), (0.007, y1 - y0, 0.005), 'M_Silver', bevel=0.0008)
    # small brass ring through the left lobe, bell hanging from it
    # short horizontal eyelet pin on the left lobe; the ring is vertical (plane YZ),
    # threaded over the pin and hanging from it, the bell hangs below the ring.
    rx = -0.032
    b.cyl((-0.022, 0.095, 0), (-0.036, 0.095, 0), 0.0025, 'M_Silver', seg=6)
    b.cyl((-0.0355, 0.095, 0), (-0.0375, 0.095, 0), 0.0038, 'M_Silver', seg=6)
    ring_r, ring_t = 0.0075, 0.0017
    ring_cy = 0.095 - (ring_r - ring_t - 0.0025)
    b.ring((rx, ring_cy, 0), ring_r, ring_t, 'X', 'M_Brass', n=12, seg=4)
    bx, top = rx, ring_cy - ring_r - ring_t + 0.0012
    prof = [(0.0, 0.040), (0.004, 0.038), (0.0075, 0.030), (0.011, 0.018), (0.0145, 0.007),
            (0.016, 0.0), (0.0145, 0.0), (0.013, 0.007), (0.0095, 0.018), (0.006, 0.029),
            (0.0, 0.033)]
    b.lathe(prof, 10, 'M_Brass', c=(bx, top - 0.040, 0))
    b.cyl((bx, top - 0.040 + 0.033, 0), (bx, top - 0.040 - 0.002, 0), 0.0012, 'M_Brass', seg=4)
    b.lathe([(0, 0.005), (0.0035, 0.0035), (0.005, 0.0), (0.0035, -0.0035), (0, -0.005)], 6,
            'M_Brass', c=(bx, top - 0.040 - 0.003, 0))
    return b


BOLT_POLY = [(0.35, 1.0), (0.95, 1.0), (0.68, 0.58), (0.95, 0.58), (0.22, 0.0),
             (0.40, 0.42), (0.05, 0.42)]


def bolt(b, height, width, thick, rot_y):
    """Flat extruded lightning-bolt solid (classic icon outline) in the XY plane,
    scaled so the polygon's x-extent (0.9 in unit box) becomes `width`."""
    bm = b.bm
    before = set(bm.faces)
    pts = [((x - 0.5) * width / 0.9, (y - 0.5) * height) for x, y in BOLT_POLY]
    top = [bm.verts.new((x, y, thick / 2)) for x, y in pts]
    bot = [bm.verts.new((x, y, -thick / 2)) for x, y in pts]
    n = len(pts)
    from mathutils import geometry
    tris = geometry.tessellate_polygon([[Vector((x, y, 0)) for x, y in pts]])
    for i, j, k in tris:
        bm.faces.new((top[i], top[j], top[k]))
        bm.faces.new((bot[k], bot[j], bot[i]))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((top[i], bot[i], bot[j], top[j]))
    b._finish(before, 'M_Bolt', Matrix.Rotation(rot_y, 4, 'Y'))


def fuse():
    b = B("R2_Fuse")
    b.cyl((0, -0.03, 0), (0, 0.03, 0), 0.02, 'M_Glass', seg=12)
    for s in (-1, 1):
        b.tube([(0, 0.029 * s, 0), (0, 0.0425 * s, 0), (0, 0.044 * s, 0)],
               [0.022, 0.022, 0.019], 12, 'M_Steel')
        b.box((0, 0.0465 * s, 0), (0.016, 0.007, 0.0025), 'M_Steel', bevel=0.0006)
    bolt(b, 0.05, 0.034, 0.003, 0.0)
    return b


def ice_block(key_builder):
    b = B("R5_IceBlock")
    bm = b.bm
    before = set(bm.faces)
    vs = bmesh.ops.create_cube(bm, size=1.0)['verts']
    for v in vs:
        v.co = Vector((v.co.x * 0.30, v.co.y * 0.25, v.co.z * 0.25))
    es = list({e for v in vs for e in v.link_edges})
    bmesh.ops.bevel(bm, geom=es, offset=0.02, offset_type='OFFSET', segments=2,
                    profile=0.5, affect='EDGES')
    rnd = random.Random(7)
    for v in {v for f in bm.faces for v in f.verts}:
        if rnd.random() < 0.35:
            v.co += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * 0.006
    b._finish(before, 'M_Ice')
    # frozen copy of the bell key, lying along X, slightly tilted
    M = (Matrix.Translation((0.0, 0.0, 0.0))
         @ Euler((math.radians(10), 0, math.radians(100))).to_matrix().to_4x4()
         @ Matrix.Scale(1.4, 4))
    b.merge(key_builder, M)
    return b


# -------------------------------------------------------------- export + scene
def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def export_fbx(obj, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    loc, rot = obj.location.copy(), obj.rotation_euler.copy()
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False)
    obj.location, obj.rotation_euler = loc, rot
    obj.select_set(False)


def add_text(text, x, z, size=0.05):
    cu = bpy.data.curves.new("Lbl_" + text, 'FONT')
    cu.body = text
    cu.size = size
    cu.align_x = 'CENTER'
    cu.align_y = 'TOP'
    o = bpy.data.objects.new("Lbl_" + text, cu)
    bpy.context.scene.collection.objects.link(o)
    o.location = (x, -0.05, z)
    o.rotation_euler = (math.radians(90), 0, 0)
    o.data.materials.append(MATS["M_Label"])
    return o


def build_preview(objs):
    scene = bpy.context.scene
    make_mat("M_Label", (0.02, 0.02, 0.03), 0.9)
    make_mat("M_Backdrop", (0.45, 0.48, 0.54), 1.0)
    make_mat("M_Ref", (0.9, 0.35, 0.1), 0.6)
    names = list(objs.keys())
    x0, dx = -1.6, 0.4
    for i, nme in enumerate(names):
        o = objs[nme]
        h = max(v.co.y for v in o.data.vertices)
        o.rotation_euler = (math.radians(90), 0, 0)
        o.location = (x0 + dx * i, 0, h)
        add_text(nme, x0 + dx * i, -0.015, 0.045)

    def ref_box(name, c, s):
        bmm = bmesh.new()
        bmesh.ops.create_cube(bmm, size=1.0)
        for v in bmm.verts:
            v.co = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2]))
        me = bpy.data.meshes.new(name)
        bmm.to_mesh(me)
        bmm.free()
        o = bpy.data.objects.new(name, me)
        scene.collection.objects.link(o)
        o.location = c
        return o

    # 2 m reference cylinder
    me = bpy.data.meshes.new("Ref_Cylinder")
    bmm = bmesh.new()
    bmesh.ops.create_cone(bmm, cap_ends=True, segments=16, radius1=0.03, radius2=0.03, depth=2.0)
    bmm.to_mesh(me)
    bmm.free()
    cyl = bpy.data.objects.new("Ref_Cylinder", me)
    scene.collection.objects.link(cyl)
    cyl.location = (-2.4, 0, 1.0)
    cyl.data.materials.append(MATS["M_Ref"])
    add_text("2.0 m", -2.4, -0.015, 0.06)
    # 1 m scale bar
    bar = ref_box("Ref_ScaleBar", (2.3, 0, 0.012), (1.0, 0.03, 0.024))
    bar.data.materials.append(MATS["M_Ref"])
    add_text("1.0 m", 2.3, -0.015, 0.06)
    # ground line + backdrop
    gl = ref_box("Ref_Ground", (0, 0, -0.004), (6.0, 0.04, 0.008))
    gl.data.materials.append(MATS["M_Label"])
    bd = ref_box("Ref_Backdrop", (0, 0.6, 1.0), (8.0, 0.02, 4.0))
    bd.data.materials.append(MATS["M_Backdrop"])

    # light, world, camera
    sun_d = bpy.data.lights.new("Sun", 'SUN')
    sun_d.energy = 1.4
    sun_d.angle = math.radians(15)
    sun = bpy.data.objects.new("Sun", sun_d)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(55), 0, math.radians(-30))
    w = bpy.data.worlds.new("World")
    try:
        w.use_nodes = True
    except Exception:
        pass
    bg = w.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.62, 0.68, 0.75, 1.0)
    bg.inputs["Strength"].default_value = 0.7
    scene.world = w
    cam_d = bpy.data.cameras.new("Camera")
    cam_d.type = 'ORTHO'
    cam_d.ortho_scale = 6.0
    cam = bpy.data.objects.new("Camera", cam_d)
    scene.collection.objects.link(cam)
    cam.location = (0, -8, 0.9)
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam
    cam_d.clip_end = 50

    scene.render.resolution_x = 1600
    scene.render.resolution_y = 700
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = PREVIEW_PATH
    try:
        scene.view_settings.view_transform = 'Standard'
    except Exception:
        pass


def render_preview():
    os.makedirs(os.path.dirname(PREVIEW_PATH), exist_ok=True)
    scene = bpy.context.scene
    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT', 'BLENDER_WORKBENCH'):
        try:
            scene.render.engine = eng
        except Exception:
            continue
        try:
            if hasattr(scene, "eevee"):
                scene.eevee.taa_render_samples = 24
            bpy.ops.render.render(write_still=True)
            if os.path.exists(PREVIEW_PATH):
                print("RENDER ENGINE:", eng)
                return eng
        except Exception as e:
            print("Render with", eng, "failed:", e)
    return None


CLOSEUP_PATH = os.path.join(os.path.dirname(PREVIEW_PATH), "route_items_closeup.png")


def render_closeups(objs, tile=600):
    """Render each item alone (3/4 perspective, auto-framed) and tile them 3x3."""
    import numpy as np
    scene = bpy.context.scene
    cam = scene.camera
    cam.data.type = 'PERSP'
    cam.data.lens = 50
    cam.data.sensor_width = 36
    cam.data.clip_start = 0.01
    cam.data.clip_end = 20
    scene.render.resolution_x = tile
    scene.render.resolution_y = tile
    half_fov = math.atan(18.0 / 50.0)
    keep = {cam, *[o for o in bpy.data.objects if o.type == 'LIGHT']}
    items = set(objs.values())
    old_labels = [o for o in bpy.data.objects if o.name.startswith("Lbl_")]
    hide_always = [o for o in bpy.data.objects
                   if o not in keep and o not in items]
    for o in hide_always:
        o.hide_render = True
    bg = scene.world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.55, 0.60, 0.67, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    tmpdir = os.path.dirname(PREVIEW_PATH)
    tile_paths = []
    pix = np.zeros((tile * 3, tile * 3, 4), dtype=np.float32)
    pix[..., 3] = 1.0
    for idx, (name, o) in enumerate(objs.items()):
        for other in items:
            other.hide_render = other is not o
        bpy.context.view_layer.update()
        corners = [o.matrix_world @ Vector(c) for c in o.bound_box]
        centre = sum(corners, Vector()) / 8.0
        r = max((c - centre).length for c in corners)
        r_eff = r * 1.3
        dist = r_eff / math.sin(half_fov)
        az, el = ((math.radians(20), math.radians(8)) if name == "R2_Fuse"
                  else (math.radians(35), math.radians(22)))
        direction = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el),
                            math.sin(el)))
        cam.location = centre + direction * dist
        quat = (centre - cam.location).to_track_quat('-Z', 'Y')
        cam.rotation_euler = quat.to_euler()
        cam_up = quat @ Vector((0, 1, 0))
        # label that faces the camera, below the item
        cu = bpy.data.curves.new("CL_" + name, 'FONT')
        cu.body = name
        cu.size = r * 0.15
        cu.align_x = 'CENTER'
        cu.align_y = 'CENTER'
        lbl = bpy.data.objects.new("CL_" + name, cu)
        scene.collection.objects.link(lbl)
        lbl.rotation_euler = cam.rotation_euler
        lbl.location = centre - cam_up * (r * 1.12)
        cu.materials.append(MATS["M_Label"])
        # nudge the item up a little so the label has room
        tp = os.path.join(tmpdir, "closeup_tile_%d.png" % idx)
        scene.render.filepath = tp
        scene.render.image_settings.file_format = 'PNG'
        bpy.ops.render.render(write_still=True)
        tile_paths.append(tp)
        bpy.data.objects.remove(lbl, do_unlink=True)
        bpy.data.curves.remove(cu)
        img = bpy.data.images.load(tp)
        a = np.empty(tile * tile * 4, dtype=np.float32)
        img.pixels.foreach_get(a)
        a = a.reshape(tile, tile, 4)
        row, col = divmod(idx, 3)
        y0 = (2 - row) * tile
        pix[y0:y0 + tile, col * tile:(col + 1) * tile, :] = a
        bpy.data.images.remove(img)
    out = bpy.data.images.new("closeup_grid", tile * 3, tile * 3, alpha=False)
    out.colorspace_settings.name = 'sRGB'
    out.pixels.foreach_set(pix.ravel())
    out.filepath_raw = CLOSEUP_PATH
    out.file_format = 'PNG'
    out.save()
    for tp in tile_paths:
        try:
            os.remove(tp)
        except OSError:
            pass
    for o in hide_always + list(items):
        o.hide_render = False


def main():
    clear_scene()
    try:
        bpy.context.preferences.filepaths.save_version = 0
    except Exception:
        pass
    build_materials()
    bell_for_ice = bell_key()
    bell_for_ice.finalize()  # centred copy for the ice block
    builders = [hammer(), screwdriver(), crowbar(), shovel(), door_key(), hatch_key(),
                bell_key(), fuse(), ice_block(bell_for_ice)]
    objs = {}
    for bld in builders:
        if bld.name != "R5_IceBlock":
            bld.finalize()
        else:
            bld.finalize()
        objs[bld.name] = bld.to_object()
    # leftover free of the ice-block source builder
    try:
        bell_for_ice.bm.free()
    except Exception:
        pass

    print("TRIANGLES:")
    for nme, o in objs.items():
        d = o.dimensions
        print("  %-14s tris=%4d  dims=(%.3f, %.3f, %.3f)  mats=%s" % (
            nme, tri_count(o), d.x, d.y, d.z, [m.name for m in o.data.materials]))

    for nme, o in objs.items():
        path = os.path.join(ROOT, FBX_DIRS[nme].replace("/", os.sep), nme + ".fbx")
        export_fbx(o, path)
        print("EXPORTED", path)

    build_preview(objs)
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=False)
    print("SAVED", BLEND_PATH)
    eng = render_preview()
    print("PREVIEW", PREVIEW_PATH, eng)
    render_closeups(objs)
    print("CLOSEUP", CLOSEUP_PATH)


main()
