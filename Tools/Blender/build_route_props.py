"""Procedurally builds larger low-poly floor props for the Unity escape game.

Run:  blender -b --factory-startup -P build_route_props.py
Idempotent: clears the scene, rebuilds everything, overwrites outputs.

Conventions: metric (1 unit = 1 m), Z up, prop front faces Blender -Y, origin at the
bottom-centre of the footprint, one mesh object per prop named like its FBX file.
To add a prop: write a function returning a finished B(...) and register it in PROPS
(name -> (builder, fbx folder)).
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

ROOT = r"C:\Users\Zap\Uni\SEM5\AR-VR\GroundBreaking"
BLEND_PATH = os.path.join(ROOT, "ArtSource", "Routes", "RouteProps.blend")
SCRATCH = (r"C:\Users\Zap\AppData\Local\Temp\claude\700b252225256bde"
           r"\beb4baae-6014-4370-84e6-9d838abe0498\scratchpad")
PREVIEW_PATH = os.path.join(SCRATCH, "hack_station_preview.png")
CLOSEUP_PATH = os.path.join(SCRATCH, "hack_station_closeup.png")

HACKER = "Assets/Routes/Route03_Hacker/Models"


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


def hazard_band(b, mp, u0, u1, v0, v1, outn, o1, o2, s, shear):
    """Yellow base quad + black diagonal stripes on a flat band.
    mp(u, v, o) -> 3D point (o = offset along the outward normal); outn = outward normal."""
    def emit(poly, mat, o):
        pts = [mp(u, v, o) for u, v in poly]
        n = (Vector(pts[1]) - Vector(pts[0])).cross(Vector(pts[2]) - Vector(pts[1]))
        if n.dot(Vector(outn)) < 0:
            pts = pts[::-1]
        b.quad(pts, mat)
    emit([(u0, v0), (u1, v0), (u1, v1), (u0, v1)], 'M_HazardYellow', o1)
    u = u0
    while u + s + shear <= u1 + 1e-9:
        emit([(u, v0), (u + s, v0), (u + s + shear, v1), (u + shear, v1)], 'M_HazardBlack', o2)
        u += 2 * s


# ----------------------------------------------------------------------- props
def hack_station():
    b = B("R3_HackStation")
    for m in ("M_DeskTop", "M_Metal", "M_CasePlastic", "M_Screen", "M_Keys",
              "M_StatusLight", "M_HazardYellow", "M_HazardBlack"):
        b.mi(m)

    # ---- desk: 1.40 x 0.70, top surface at 0.75, 0.04 thick
    DT, TH = 0.75, 0.04
    b.box((0, 0, DT - TH / 2), (1.40, 0.70, TH), 'M_DeskTop', bevel=0.004)
    lz = DT - TH
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.box((sx * 0.65, sy * 0.30, lz / 2), (0.04, 0.04, lz), 'M_Metal', bevel=0.003)
    # modesty panel at the back, 0.30 tall, just under the top
    b.box((0, 0.30, lz - 0.15), (1.26, 0.02, 0.30), 'M_DeskTop', bevel=0.002)

    # ---- CRT monitor (centred slightly left)
    MCX = -0.28
    zc = DT + 0.07 + 0.20             # vertical centre of the case (bottom at 0.82)
    b.cyl((MCX, 0.05, DT), (MCX, 0.05, DT + 0.02), 0.11, 'M_Metal', seg=8)
    b.cyl((MCX, 0.05, DT + 0.02), (MCX, 0.05, DT + 0.07), 0.05, 'M_Metal', seg=8)
    b.box((MCX, 0.03, zc), (0.46, 0.26, 0.40), 'M_CasePlastic', bevel=0.01)
    # tapered rear housing
    b.loft(0.16, (MCX, zc), (0.40, 0.34), 0.30, (MCX, zc - 0.02), (0.26, 0.22), 'M_CasePlastic')
    # bezel frame around a 0.36 x 0.28 opening
    by = -0.11
    b.box((MCX - 0.205, by, zc), (0.05, 0.02, 0.40), 'M_CasePlastic', bevel=0.003)
    b.box((MCX + 0.205, by, zc), (0.05, 0.02, 0.40), 'M_CasePlastic', bevel=0.003)
    b.box((MCX, by, zc + 0.17), (0.36, 0.02, 0.06), 'M_CasePlastic', bevel=0.003)
    b.box((MCX, by, zc - 0.17), (0.36, 0.02, 0.06), 'M_CasePlastic', bevel=0.003)
    b.box((MCX + 0.15, -0.1215, zc - 0.17), (0.02, 0.003, 0.008), 'M_StatusLight')
    # screen: one flat quad, recessed in the bezel, UV fills 0-1
    sx0, sx1, sz0, sz1 = MCX - 0.18, MCX + 0.18, zc - 0.14, zc + 0.14
    sy = -0.105
    b.quad([(sx0, sy, sz0), (sx1, sy, sz0), (sx1, sy, sz1), (sx0, sy, sz1)], 'M_Screen',
           uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])

    # ---- tower PC
    TCX, ty0, ty1, tz0, tz1 = 0.30, -0.12, 0.33, DT, DT + 0.45
    b.box((TCX, (ty0 + ty1) / 2, (tz0 + tz1) / 2), (0.20, ty1 - ty0, tz1 - tz0),
          'M_CasePlastic', bevel=0.008)
    fy = ty0 - 0.004
    for i in range(5):                                   # raised vent slats
        b.box((TCX, fy, 0.80 + i * 0.026), (0.14, 0.008, 0.012), 'M_Metal')
    for z in (1.14, 1.10):                               # drive bays
        b.box((TCX - 0.025, fy, z), (0.11, 0.008, 0.028), 'M_Keys')
    b.box((TCX - 0.025, fy, 1.04), (0.028, 0.010, 0.028), 'M_Metal')   # power button
    b.box((TCX + 0.07, ty0 - 0.003, 1.04), (0.014, 0.006, 0.20), 'M_StatusLight')
    # hazard sticker on the +X side (faces the crouch pocket)
    sxp = TCX + 0.10
    ya, yb, za, zb = 0.00, 0.20, 0.96, 1.03
    b.quad([(sxp + 0.001, ya, za), (sxp + 0.001, yb, za), (sxp + 0.001, yb, zb),
            (sxp + 0.001, ya, zb)], 'M_HazardYellow')
    s, d = 0.025, zb - za
    for y0 in (0.0, 0.05, 0.10):
        y0 += ya
        b.quad([(sxp + 0.002, y0, za), (sxp + 0.002, y0 + s, za), (sxp + 0.002, y0 + s + d, zb),
                (sxp + 0.002, y0 + d, zb)], 'M_HazardBlack')

    # ---- keyboard wedge (0.45 x 0.16)
    kf, kb = -0.31, -0.15
    hf, hb = 0.015, 0.035
    b.prism([(kf, DT), (kb, DT), (kb, DT + hb), (kf, DT + hf)], MCX - 0.225, MCX + 0.225,
            'M_CasePlastic')
    slope = math.atan2(hb - hf, kb - kf)
    for i, y in enumerate((-0.189, -0.221, -0.253, -0.285)):
        zs = DT + hf + (y - kf) * math.tan(slope) + 0.005
        rot = (slope, 0, 0)
        if i < 3:
            b.box((MCX, y, zs), (0.41, 0.026, 0.012), 'M_Keys', rot=rot)
        else:                                            # space-bar row: three blocks
            x = MCX - 0.205
            for w in (0.105, 0.17, 0.105):
                b.box((x + w / 2, y, zs), (w, 0.026, 0.012), 'M_Keys', rot=rot)
                x += w + 0.015

    # ---- cable monitor -> tower, along the back edge
    pts = [(MCX, 0.30, zc - 0.04), (MCX, 0.33, zc - 0.08), (MCX + 0.02, 0.338, 0.82),
           (MCX + 0.14, 0.338, 0.77), (0.0, 0.338, 0.765), (0.16, 0.338, 0.77),
           (0.28, 0.336, 0.82), (TCX, 0.332, 0.88)]
    b.tube(pts, 0.008, 5, 'M_Metal')
    return b


def crouch_cover():
    b = B("R3_CrouchCover")
    for m in ("M_PartitionFabric", "M_Metal"):
        b.mi(m)
    W, H, T, FT, FOOT = 1.10, 1.25, 0.06, 0.035, 0.05
    zc = (FOOT + H) / 2
    ph = H - FOOT
    for sx in (-1, 1):
        b.box((sx * (W / 2 - FT / 2), 0, zc), (FT, T, ph), 'M_Metal', bevel=0.003)
    b.box((0, 0, H - FT / 2), (W - 2 * FT, T, FT), 'M_Metal', bevel=0.003)
    b.box((0, 0, FOOT + FT / 2), (W - 2 * FT, T, FT), 'M_Metal', bevel=0.003)
    b.box((0, 0, zc), (W - 2 * FT + 0.01, T - 0.01, ph - 2 * FT + 0.01), 'M_PartitionFabric')
    for sx in (-1, 1):
        b.box((sx * 0.42, 0, FOOT / 2), (0.08, 0.40, FOOT), 'M_Metal', bevel=0.004)
    return b


# ------------------------------------------------- batch A (multi-part capable props)
# Builders below work directly in final prop space (prop origin = world origin) and return
# a list of (B, pivot): one entry per separately named mesh object / FBX child.
ROUTE1 = "Assets/Routes/Route01_Locksmith/Models"
ROUTE3 = HACKER
ROUTE5 = "Assets/Routes/Route05_FrozenKey/Models"
SHARED = "Assets/Routes/Shared/Models"


def key_cabinet():
    W, H, D, T = 0.45, 0.60, 0.12, 0.025
    APR = 0.075                         # bottom apron height (holds the red plate)
    fr = B("Frame")
    fr.box((0, -0.006, (APR + H - T) / 2), (W - 2 * T, 0.012, H - T - APR), 'M_DarkRubber')
    for sx in (-1, 1):
        fr.box((sx * (W / 2 - T / 2), -D / 2, H / 2), (T, D, H), 'M_Wood', bevel=0.003)
    fr.box((0, -D / 2, H - T / 2), (W - 2 * T, D, T), 'M_Wood')
    fr.box((0, -(D - 0.004) / 2, APR / 2), (W - 2 * T, D - 0.004, APR), 'M_Wood')
    fr.box((0, -(D - 0.002), APR / 2), (0.26, 0.004, 0.040), 'M_RedPaint')   # plate, no text
    # glass beads (front retaining strips)
    by = -0.11225
    for sx in (-1, 1):
        fr.box((sx * 0.196, by, 0.325), (0.008, 0.0075, 0.50), 'M_Wood')
    fr.box((0, by, 0.571), (0.384, 0.0075, 0.008), 'M_Wood')
    fr.box((0, by, 0.079), (0.384, 0.0075, 0.008), 'M_Wood')
    # hooks (2 rows x 3); the top-centre hook is left empty, the rest carry tags
    tag_mats = ['M_RedPaint', 'M_YellowPlastic', 'M_Silver', 'M_YellowPlastic', 'M_RedPaint']
    ti = 0
    for row, hz in enumerate((0.47, 0.31)):
        for col, hx in enumerate((-0.12, 0.0, 0.12)):
            fr.tube([(hx, -0.011, hz), (hx, -0.036, hz)], 0.0045, 6, 'M_Brass')
            fr.tube([(hx, -0.036, hz), (hx, -0.036, hz + 0.016)], 0.0045, 6, 'M_Brass')
            if row == 0 and col == 1:
                continue
            fr.box((hx, -0.024, hz - 0.036), (0.034, 0.004, 0.052), tag_mats[ti % 5])
            ti += 1

    gl = B("Glass")
    gl.box((0, -0.1065, 0.325), (0.396, 0.004, 0.496), 'M_Glass')

    sh = B("GlassShards")
    y0 = -0.1085
    stuck = [
        [(-0.17, .571), (-0.075, .571), (-0.115, .455)],
        [(-0.045, .571), (0.035, .571), (0.015, .50)],
        [(0.075, .571), (0.175, .571), (0.14, .43)],
        [(-0.196, .50), (-0.196, .385), (-0.125, .41)],
        [(-0.196, .26), (-0.196, .15), (-0.13, .19)],
        [(0.196, .40), (0.196, .30), (0.12, .33)],
        [(0.196, .24), (0.196, .14), (0.135, .20)],
        [(-0.16, .079), (-0.07, .079), (-0.11, .16)],
        [(0.08, .079), (0.18, .079), (0.12, .18)],
    ]
    for tri in stuck:
        sh.polyprism([(x, y0, z) for x, z in tri], (0, 0.004, 0), 'M_Glass')
    flat = [
        [(-0.14, -0.05), (-0.08, -0.07), (-0.11, -0.02)],
        [(-0.03, -0.085), (0.03, -0.06), (0.0, -0.03)],
        [(0.07, -0.09), (0.13, -0.07), (0.10, -0.04)],
        [(0.12, -0.03), (0.17, -0.05), (0.15, -0.015)],
    ]
    for tri in flat:
        sh.polyprism([(x, y, APR) for x, y in tri], (0, 0, 0.003), 'M_Glass')
    return [(fr, (0, 0, 0)), (gl, (0, -0.1065, 0.325)), (sh, (0, -0.1065, 0.325))]


def keypad():
    base = B("KeypadBase")
    base.box((0, -0.01, 0), (0.14, 0.02, 0.22), 'M_Metal', bevel=0.002)
    base.box((0, -0.035, 0), (0.10, 0.03, 0.16), 'M_CasePlastic', bevel=0.002)
    base.box((-0.012, -0.0510, 0.060), (0.050, 0.002, 0.022), 'M_KeypadScreen')   # display
    base.box((0.034, -0.0515, 0.060), (0.010, 0.003, 0.010), 'M_StatusLight')     # card LED
    for r in range(4):
        for c in range(3):
            base.box(((c - 1) * 0.03, -0.053, 0.020 - r * 0.0275), (0.024, 0.006, 0.020),
                     'M_Keys')
    cov = B("KeypadCover")
    cy = -0.0645
    cov.box((0, cy, 0), (0.13, 0.015, 0.20), 'M_Steel', bevel=0.002)
    for i, (sx, sz) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        x, z = sx * 0.057, sz * 0.092
        cov.cyl((x, -0.02, z), (x, -0.057, z), 0.004, 'M_Steel', seg=6)          # standoff
        cov.cyl((x, -0.072, z), (x, -0.076, z), 0.007, 'M_Silver', seg=6)         # screw head
        cov.box((x, -0.0765, z), (0.011, 0.002, 0.003), 'M_Iron',
                rot=(0, math.radians(45 + 90 * (i % 2)), 0))                      # slot
    return [(base, (0, 0, 0)), (cov, (0, cy, 0))]


def freezer():
    y0, y1 = -0.300, 0.315
    ym, dep = (y0 + y1) / 2, y1 - y0
    bd = B("Body")
    bd.box((0, ym, 0.035), (1.04, dep - 0.04, 0.07), 'M_DarkRubber')               # kick strip
    bd.box((0, ym, 0.105), (1.10, dep, 0.07), 'M_Enamel', bevel=0.004)
    for sx in (-1, 1):
        bd.box((sx * 0.525, ym, 0.47), (0.05, dep, 0.66), 'M_Enamel', bevel=0.004)
    bd.box((0, 0.29, 0.47), (1.0, 0.05, 0.66), 'M_Enamel')
    bd.box((0, -0.265, 0.47), (1.0, 0.03, 0.66), 'M_Enamel')                      # front, inner
    for sx in (-1, 1):                                                            # front, outer
        bd.box((sx * 0.31, -0.29, 0.47), (0.38, 0.02, 0.66), 'M_Enamel')
    bd.box((0, -0.29, 0.455), (0.24, 0.02, 0.63), 'M_Enamel')   # notch 0.24 x 0.03 above it
    bd.box((0, ym, 0.145), (1.0, 0.515, 0.01), 'M_FreezerInner')
    # control panel, dial, indicator lights
    bd.box((0.38, -0.3025, 0.45), (0.20, 0.005, 0.12), 'M_Metal')
    bd.cyl((0.34, -0.305, 0.45), (0.34, -0.325, 0.45), 0.025, 'M_DarkRubber', seg=8)
    bd.box((0.34, -0.3245, 0.465), (0.005, 0.002, 0.016), 'M_Silver')
    bd.box((0.43, -0.308, 0.48), (0.016, 0.006, 0.016), 'M_StatusLight')
    bd.box((0.43, -0.308, 0.42), (0.016, 0.006, 0.016), 'M_StatusLightGreen')
    for sx in (-1, 1):                                                            # hinge leaves
        bd.box((sx * 0.38, 0.32, 0.76), (0.09, 0.01, 0.08), 'M_Metal', bevel=0.002)

    lid = B("Lid")
    lid.box((0, ym, 0.825), (1.10, dep, 0.05), 'M_Enamel', bevel=0.005)
    for sx in (-1, 1):
        lid.box((sx * 0.38, 0.32, 0.825), (0.09, 0.01, 0.05), 'M_Metal', bevel=0.002)
        lid.box((sx * 0.12, -0.30625, 0.825), (0.03, 0.0125, 0.03), 'M_Steel')
    lid.tube([(-0.12, -0.3155, 0.825), (0.12, -0.3155, 0.825)], 0.0095, 8, 'M_Steel')
    return [(bd, (0, 0, 0)), (lid, (0, y1, 0.85))]


def smash_bench():
    b = B("R5_SmashBench")
    TOP = 0.90
    b.box((0, 0, TOP - 0.02), (1.20, 0.60, 0.04), 'M_Steel', bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.cyl((sx * 0.55, sy * 0.25, TOP), (sx * 0.55, sy * 0.25, TOP + 0.006), 0.012,
                  'M_Iron', seg=6)
    # scuffed worn patch + raised anvil block with scratches
    b.box((0, 0, TOP + 0.0015), (0.40, 0.30, 0.002), 'M_Iron')
    b.box((0, 0, TOP + 0.04), (0.25, 0.18, 0.08), 'M_Metal', bevel=0.008)
    for i, (dx, dy, a, ln) in enumerate(((-0.04, 0.03, 20, 0.08), (0.03, -0.02, -35, 0.07),
                                         (0.0, 0.05, 80, 0.05), (0.06, 0.04, 5, 0.06))):
        b.box((dx, dy, TOP + 0.0808), (ln, 0.006, 0.0012), 'M_Silver',
              rot=(0, 0, math.radians(a)))
    # hazard band along the front edge of the top
    hazard_band(b, lambda u, v, o: (u, v, TOP + o), -0.60, 0.60, -0.294, -0.244,
                (0, 0, 1), 0.0015, 0.0020, 0.03, 0.05)
    # angled legs, foot pads
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.beam((sx * 0.555, sy * 0.265, 0.015), (sx * 0.50, sy * 0.20, 0.865), 0.05, 0.05,
                   'M_Metal', bevel=0.003)
            b.box((sx * 0.555, sy * 0.265, 0.0075), (0.09, 0.07, 0.015), 'M_Iron', bevel=0.003)
    # apron rails under the top
    for sy in (-1, 1):
        b.box((0, sy * 0.20, 0.83), (1.05, 0.03, 0.06), 'M_Metal')
    for sx in (-1, 1):
        b.box((sx * 0.50, 0, 0.83), (0.03, 0.37, 0.06), 'M_Metal')
    # lower shelf
    b.box((0, 0, 0.22), (1.04, 0.47, 0.03), 'M_Metal', bevel=0.003)
    # vise on the +X end
    vx = 0.42
    b.box((vx, -0.03, TOP + 0.0125), (0.16, 0.28, 0.025), 'M_Metal', bevel=0.004)
    b.box((vx, 0.07, TOP + 0.07), (0.16, 0.05, 0.09), 'M_Steel', bevel=0.004)      # fixed jaw
    b.box((vx, 0.0, TOP + 0.07), (0.16, 0.02, 0.09), 'M_Steel', bevel=0.003)       # moving jaw
    b.cyl((vx, -0.01, TOP + 0.07), (vx, -0.17, TOP + 0.07), 0.009, 'M_Steel', seg=6)
    b.cyl((vx - 0.06, -0.17, TOP + 0.07), (vx + 0.06, -0.17, TOP + 0.07), 0.006, 'M_Iron',
          seg=5)
    for sx in (-1, 1):
        b.box((vx + sx * 0.062, -0.17, TOP + 0.07), (0.014, 0.014, 0.014), 'M_Iron')
    return [(b, (0, 0, 0))]


def chain_lock():
    N, A, RT, BW = 18, 0.03935, 0.0085, 0.019          # links, half-length, tube r, half-width
    HX, HZ, RC = 0.1975, 0.0975, 0.04                  # loop centre-line half sizes, corner r
    ZB = 0.0675                                        # centre-line z of the bottom run
    zc = ZB + HZ
    segs = []                                          # (length, fn(s) -> (point, tangent))

    def line(p0, p1):
        p0, p1 = Vector(p0), Vector(p1)
        t = (p1 - p0).normalized()
        return ((p1 - p0).length, lambda s, p0=p0, t=t: (p0 + t * s, t))

    def arc(c, a0, a1):
        c = Vector(c)
        sgn = 1 if a1 > a0 else -1
        return (abs(a1 - a0) * RC, lambda s, c=c, a0=a0, sgn=sgn: (
            c + Vector((RC * math.cos(a0 + sgn * s / RC), 0, RC * math.sin(a0 + sgn * s / RC))),
            Vector((-sgn * math.sin(a0 + sgn * s / RC), 0, sgn * math.cos(a0 + sgn * s / RC)))))
    hp = math.pi / 2
    segs += [line((0, 0, ZB), (HX - RC, 0, ZB)),
             arc((HX - RC, 0, ZB + RC), -hp, 0),
             line((HX, 0, ZB + RC), (HX, 0, ZB + 2 * HZ - RC)),
             arc((HX - RC, 0, ZB + 2 * HZ - RC), 0, hp),
             line((HX - RC, 0, ZB + 2 * HZ), (-HX + RC, 0, ZB + 2 * HZ)),
             arc((-HX + RC, 0, ZB + 2 * HZ - RC), hp, 2 * hp),
             line((-HX, 0, ZB + 2 * HZ - RC), (-HX, 0, ZB + RC)),
             arc((-HX + RC, 0, ZB + RC), 2 * hp, 3 * hp),
             line((-HX + RC, 0, ZB), (0, 0, ZB))]
    total = sum(s[0] for s in segs)

    def at(s):
        s %= total
        for ln, fn in segs:
            if s <= ln:
                return fn(s)
            s -= ln
        return segs[-1][1](segs[-1][0])

    ch = B("Chain")
    for i in range(N):
        c, t = at(total * i / N)
        flat = i % 2 == 1
        w = Vector((0, 1, 0)).cross(t) if flat else Vector((0, 1, 0))
        up = Vector((0, 1, 0)) if flat else t.cross(Vector((0, 1, 0)))
        pts = [c + t * A * math.cos(2 * math.pi * k / 6) + w * BW * math.sin(2 * math.pi * k / 6)
               for k in range(6)]
        ch.tube(pts, RT, 6, 'M_Iron', up=up, closed=True)

    pl = B("Padlock")
    pl.box((0, 0.0045, 0), (0.07, 0.021, 0.08), 'M_Brass', bevel=0.003)
    pl.box((-0.021, -0.0105, 0), (0.028, 0.009, 0.08), 'M_Brass')
    pl.box((0.021, -0.0105, 0), (0.028, 0.009, 0.08), 'M_Brass')
    pl.box((0, -0.0105, 0.0215), (0.014, 0.009, 0.037), 'M_Brass')
    pl.box((0, -0.0105, -0.0295), (0.014, 0.009, 0.021), 'M_Brass')
    pl.quad([(-0.007, -0.0062, -0.019), (0.007, -0.0062, -0.019), (0.007, -0.0062, 0.003),
             (-0.007, -0.0062, 0.003)], 'M_DarkRubber')                            # keyhole recess
    arcp = [(-0.02, 0, 0.03), (-0.02, 0, 0.06)]
    arcp += [(0.02 * math.cos(math.pi - math.pi * k / 4), 0,
              0.06 + 0.02 * math.sin(math.pi - math.pi * k / 4)) for k in range(1, 4)]
    arcp += [(0.02, 0, 0.06), (0.02, 0, 0.03)]
    pl.tube(arcp, 0.006, 6, 'M_Steel')
    return [(ch, (0, 0, zc)), (pl, (0, 0, 0))]


def crouch_gap():
    b = B("S_CrouchGap")
    H = 2.20
    GX, GH = 0.50, 1.30                                 # clear opening half-width, height
    for sx in (-1, 1):
        b.box((sx * 0.54, 0, (0.05 + H) / 2), (0.08, 0.10, H - 0.05), 'M_Metal', bevel=0.004)
        b.box((sx * 0.76, 0, (0.05 + H) / 2), (0.08, 0.10, H - 0.05), 'M_Metal', bevel=0.004)
        b.box((sx * 0.55, 0, 0.025), (0.10, 0.20, 0.05), 'M_Metal')
        b.box((sx * 0.76, 0, 0.025), (0.08, 0.20, 0.05), 'M_Metal')
    b.box((0, 0, (GH + 0.0015 + 1.42) / 2), (1.16, 0.12, 1.42 - GH - 0.0015), 'M_Metal',
          bevel=0.003)                                   # header over the gap
    b.box((0, 0, 2.16), (1.60, 0.12, 0.08), 'M_Metal', bevel=0.003)                  # top beam
    for z in (1.55, 1.80, 2.05):                                                    # back rails
        b.box((0, 0.075, z), (1.46, 0.03, 0.04), 'M_Metal')
    # hazard stripes on the header: front, back, underside (outside the clear opening)
    hazard_band(b, lambda u, v, o: (u, -0.06 - o, v), -GX, GX, GH + 0.0015, 1.42,
                (0, -1, 0), 0.0005, 0.0010, 0.05, 0.08)
    hazard_band(b, lambda u, v, o: (u, 0.06 + o, v), -GX, GX, GH + 0.0015, 1.42,
                (0, 1, 0), 0.0005, 0.0010, 0.05, 0.08)
    hazard_band(b, lambda u, v, o: (u, v, GH + 0.0015 - o), -GX, GX, -0.06, 0.06,
                (0, 0, -1), 0.0005, 0.0015, 0.05, 0.08)
    nails = []
    # upper planks (front layer, y -0.05 .. -0.08), a bit uneven like hand-nailed boards
    for i, (ln, ox, oy, ang) in enumerate(((1.58, 0.0, 0.0, 0.004), (1.52, 0.03, -0.004, -0.006),
                                           (1.54, -0.02, 0.002, 0.003),
                                           (1.46, 0.05, -0.003, -0.004))):
        z = 1.42 + 0.0825 + i * 0.175
        y = -0.065 + oy
        b.box((ox, y, z), (ln, 0.03, 0.165), 'M_Wood', bevel=0.003, rot=(0, ang, 0))
        for nx in (-0.76, -0.54, 0.54, 0.76):
            nails.append((nx, y - 0.015, z))
    # side wings: planks over the posts, inner edge exactly at the opening (|x| = 0.5)
    for sx in (-1, 1):
        for i in range(5):
            z = 0.05 + 0.131 + i * 0.274
            y = -0.065 + (0.003 if i % 2 else -0.002)
            b.box((sx * 0.65, y, z), (0.30, 0.03, 0.262), 'M_Wood', bevel=0.003)
            for nx in (0.54, 0.76):
                nails.append((sx * nx, y - 0.015, z))
    # cross braces over the upper planks
    for ang in (-0.4, 0.4):
        b.box((0, -0.09, 1.77), (1.5, 0.02, 0.09), 'M_WoodDark', bevel=0.002, rot=(0, ang, 0))
    for (nx, ny, nz) in nails:
        b.tube([(nx, ny, nz), (nx, ny - 0.006, nz)], [0.008, 0.005], 4, 'M_Iron', cap=False)
    return [(b, (0, 0, 0))]


NEW_PROPS = {
    "R1_KeyCabinet": (key_cabinet, ROUTE1),
    "R3_Keypad": (keypad, ROUTE3),
    "R5_Freezer": (freezer, ROUTE5),
    "R5_SmashBench": (smash_bench, ROUTE5),
    "R5_ChainLock": (chain_lock, ROUTE5),
    "S_CrouchGap": (crouch_gap, SHARED),
}

PROPS = {
    "R3_HackStation": (hack_station, HACKER),
    "R3_CrouchCover": (crouch_cover, HACKER),
}


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


def capsule(name, r, h, mat, seg=24, rings=8):
    prof = []
    for i in range(rings + 1):
        a = -math.pi / 2 + (math.pi / 2) * i / rings
        prof.append((r * math.cos(a), r + r * math.sin(a)))
    for i in range(rings + 1):
        a = (math.pi / 2) * i / rings
        prof.append((r * math.cos(a), (h - r) + r * math.sin(a)))
    prof[0] = (0.0, 0.0)
    prof[-1] = (0.0, h)
    bm = bmesh.new()
    rs = []
    for rad, z in prof:
        if rad < 1e-9:
            rs.append([bm.verts.new((0, 0, z))])
        else:
            rs.append([bm.verts.new((rad * math.cos(2 * math.pi * k / seg),
                                     rad * math.sin(2 * math.pi * k / seg), z))
                       for k in range(seg)])
    for i in range(len(rs) - 1):
        a, c = rs[i], rs[i + 1]
        for k in range(seg):
            k2 = (k + 1) % seg
            if len(a) == 1:
                bm.faces.new((a[0], c[k2], c[k]))
            elif len(c) == 1:
                bm.faces.new((a[k], a[k2], c[0]))
            else:
                bm.faces.new((a[k], a[k2], c[k2], c[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(MATS[mat])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def add_text(text, loc, size, cam):
    cu = bpy.data.curves.new("Lbl_" + text, 'FONT')
    cu.body = text
    cu.size = size
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.extrude = 0.004
    o = bpy.data.objects.new("Lbl_" + text, cu)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    cu.materials.append(MATS["M_Label"])
    c = o.constraints.new('COPY_ROTATION')     # billboard: face the camera, readable
    c.target = cam
    return o


def aim(cam, loc, target):
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()


# Layout of the in-game use case (see brief): cover to the desk's right, perpendicular
# to the desk front, GAP metres from the desk edge.
GAP = 1.0
CAP_R = 0.22


def build_preview(objs):
    scene = bpy.context.scene
    make_mat("M_Label", (0.02, 0.02, 0.03), 0.9)
    make_mat("M_Floor", (0.40, 0.41, 0.43), 1.0)
    make_mat("M_PlayerCrouch", (1.0, 0.42, 0.05), 0.6)
    make_mat("M_PlayerStand", (1.0, 0.42, 0.05), 0.6, alpha=0.3)

    desk, cover = objs["R3_HackStation"], objs["R3_CrouchCover"]
    # pocket: x in [0.70, 0.70+GAP], y in [-0.45, -0.45+GAP]; cover closes its front (-Y)
    cx = 0.70 + GAP / 2
    cover.rotation_euler = (0, 0, 0)
    cover.location = (cx, -0.45 - 0.03, 0)
    px = cx
    crouch = capsule("Ref_Crouched", CAP_R, 1.1, "M_PlayerCrouch")
    crouch.location = (px + 0.15, 0.0, 0)
    stand = capsule("Ref_Standing", CAP_R, 2.0, "M_PlayerStand")
    stand.location = (px - 0.3, 0.2, 0)

    # floor
    me = bpy.data.meshes.new("Ref_Floor")
    bmf = bmesh.new()
    bmesh.ops.create_grid(bmf, x_segments=1, y_segments=1, size=9.0)
    bmf.to_mesh(me)
    bmf.free()
    fl = bpy.data.objects.new("Ref_Floor", me)
    scene.collection.objects.link(fl)
    fl.location = (0.8, 0, -0.001)
    me.materials.append(MATS["M_Floor"])

    # camera (front-left, eye height 1.6 m)
    cam_d = bpy.data.cameras.new("Camera")
    cam_d.lens = 32
    cam_d.clip_start = 0.05
    cam_d.clip_end = 50
    cam = bpy.data.objects.new("Camera", cam_d)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_d.lens = 38
    aim(cam, (-2.0, -3.3, 1.6), (0.85, 0.1, 1.0))

    # labels
    add_text("R3_HackStation", (-0.15, 0.0, 1.50), 0.11, cam)
    add_text("R3_CrouchCover", (cx + 0.3, -0.48, 1.75), 0.11, cam)
    add_text("1.1 m crouched", (px + 0.3, 0.0, 1.45), 0.10, cam)
    add_text("2.0 m standing", (px - 0.3, 0.2, 2.12), 0.10, cam)

    # lights: sun + world + a soft red glow in front of the screen
    sun_d = bpy.data.lights.new("Sun", 'SUN')
    sun_d.energy = 2.2
    sun_d.angle = math.radians(20)
    sun = bpy.data.objects.new("Sun", sun_d)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    glow_d = bpy.data.lights.new("ScreenGlow", 'POINT')
    glow_d.color = (1.0, 0.15, 0.08)
    glow_d.energy = 4
    glow_d.shadow_soft_size = 0.15
    glow = bpy.data.objects.new("ScreenGlow", glow_d)
    scene.collection.objects.link(glow)
    glow.location = (-0.28, -0.55, 1.0)
    w = bpy.data.worlds.new("World")
    try:
        w.use_nodes = True
    except Exception:
        pass
    bg = w.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.62, 0.68, 0.76, 1.0)
    bg.inputs["Strength"].default_value = 0.9
    scene.world = w

    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    try:
        scene.view_settings.view_transform = 'Standard'
    except Exception:
        pass
    return cam


ENGINE = []


def render_to(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene = bpy.context.scene
    scene.render.filepath = path
    for eng in (ENGINE or ['BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT', 'BLENDER_WORKBENCH']):
        try:
            scene.render.engine = eng
        except Exception:
            continue
        try:
            if hasattr(scene, "eevee"):
                scene.eevee.taa_render_samples = 32
            bpy.ops.render.render(write_still=True)
            if os.path.exists(path):
                print("RENDER ENGINE:", eng)
                if not ENGINE:
                    ENGINE.append(eng)
                return eng
        except Exception as e:
            print("Render with", eng, "failed:", e)
    return None


def main_legacy():
    clear_scene()
    try:
        bpy.context.preferences.filepaths.save_version = 0
    except Exception:
        pass
    build_materials()
    objs, dirs = {}, {}
    for name, (fn, folder) in PROPS.items():
        bld = fn()
        bld.finalize()
        objs[name] = bld.to_object()
        dirs[name] = folder

    print("TRIANGLES:")
    for nme, o in objs.items():
        d = o.dimensions
        print("  %-16s tris=%4d  dims=(%.3f, %.3f, %.3f)  mats=%s" % (
            nme, tri_count(o), d.x, d.y, d.z, [m.name for m in o.data.materials]))

    for nme, o in objs.items():
        path = os.path.join(ROOT, dirs[nme].replace("/", os.sep), nme + ".fbx")
        export_fbx(o, path)
        print("EXPORTED", path)

    cam = build_preview(objs)
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=False)
    print("SAVED", BLEND_PATH)
    print("PREVIEW", PREVIEW_PATH, render_to(PREVIEW_PATH))
    # close-up of the station for inspection
    aim(cam, (-1.2, -1.9, 1.45), (0.0, 0.0, 0.95))
    cam.data.lens = 40
    print("CLOSEUP", CLOSEUP_PATH, render_to(CLOSEUP_PATH))
    # right-hand view (hazard sticker side)
    aim(cam, (1.9, -1.5, 1.5), (0.35, 0.1, 0.95))
    cam.data.lens = 40
    print("SIDE", render_to(os.path.join(SCRATCH, "hack_station_side.png")))




# ----------------------------------------------------------------- batch A flow
# main_legacy() above is the original R3-only flow (re-exports R3_HackStation and
# R3_CrouchCover). main() below does NOT re-export them, so their FBX stay unchanged.
BATCH_PREVIEW = os.path.join(SCRATCH, "batchA_preview.png")
NEWP = {}                       # prop -> {part name: (object, pivot)}


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


def make_env():
    scene = bpy.context.scene
    make_mat("M_Label", (0.02, 0.02, 0.03), 0.9)
    make_mat("M_Floor", (0.40, 0.41, 0.43), 1.0)
    make_mat("M_Wall", (0.34, 0.32, 0.30), 1.0)
    make_mat("M_PlayerCrouch", (1.0, 0.42, 0.05), 0.6)
    make_mat("M_PlayerStand", (0.2, 0.55, 1.0), 0.6, alpha=0.55)
    env = {}
    for nm, mat, rot, loc in (("Ref_Floor", "M_Floor", (0, 0, 0), (0, 0, -0.001)),
                              ("Ref_Wall", "M_Wall", (math.radians(90), 0, 0), (0, 0.003, 0))):
        me = bpy.data.meshes.new(nm)
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=6.0)
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(nm, me)
        scene.collection.objects.link(o)
        o.rotation_euler = rot
        o.location = loc
        me.materials.append(MATS[mat])
        env[nm] = o
    cam_d = bpy.data.cameras.new("Camera")
    cam_d.lens = 40
    cam_d.clip_start = 0.02
    cam_d.clip_end = 60
    cam = bpy.data.objects.new("Camera", cam_d)
    scene.collection.objects.link(cam)
    scene.camera = cam
    sun_d = bpy.data.lights.new("Sun", 'SUN')
    sun_d.energy = 2.0
    sun_d.angle = math.radians(20)
    sun = bpy.data.objects.new("Sun", sun_d)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(55), math.radians(10), math.radians(-35))
    w = bpy.data.worlds.new("World")
    try:
        w.use_nodes = True
    except Exception:
        pass
    bg = w.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.62, 0.68, 0.76, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    scene.world = w
    scene.render.resolution_x = 600
    scene.render.resolution_y = 400
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    try:
        scene.view_settings.view_transform = 'Standard'
    except Exception:
        pass
    return cam, env


def cam_label(text, cam):
    cu = bpy.data.curves.new("Lbl", 'FONT')
    cu.body = text
    cu.size = 0.017
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.extrude = 0.0008
    o = bpy.data.objects.new("Lbl", cu)
    bpy.context.scene.collection.objects.link(o)
    o.parent = cam
    o.location = (0, -0.128, -0.5)
    cu.materials.append(MATS["M_Label"])
    return o


def build_batch_preview(legacy):
    cam, env = make_env()
    extras = []
    cells = [
        dict(label="R1_KeyCabinet - intact", prop="R1_KeyCabinet",
             hide=("GlassShards",), env="Ref_Wall", off=(0, 0, 0.9), tgt=(0, -0.06, 1.22), d=1.55),
        dict(label="R1_KeyCabinet - broken", prop="R1_KeyCabinet",
             hide=("Glass",), env="Ref_Wall", off=(0, 0, 0.9), tgt=(0, -0.06, 1.22), d=1.55),
        dict(label="R3_Keypad - with cover", prop="R3_Keypad", hide=(), env="Ref_Wall",
             off=(0, 0, 1.2), tgt=(0, -0.04, 1.2), d=0.85),
        dict(label="R3_Keypad - cover removed", prop="R3_Keypad", hide=("KeypadCover",),
             env="Ref_Wall", off=(0, 0, 1.2), tgt=(0, -0.04, 1.2), d=0.85),
        dict(label="R5_Freezer - lid open 70 deg", prop="R5_Freezer", hide=(), env="Ref_Floor",
             off=(0, 0, 0), lid=70, tgt=(0, 0, 0.62), d=3.4),
        dict(label="R5_SmashBench", prop="R5_SmashBench", hide=(), env="Ref_Floor",
             off=(0, 0, 0), tgt=(0, 0, 0.55), d=3.4),
        dict(label="R5_ChainLock", prop="R5_ChainLock", hide=(), env="Ref_Wall",
             off=(0, 0, 1.2), tgt=(0, 0, 1.31), d=1.3, handles=True),
        dict(label="S_CrouchGap - 1.1 m / 2.0 m capsules", prop="S_CrouchGap",
             hide=(), env="Ref_Floor", off=(0, 0, 0), tgt=(0.35, 0, 1.05), d=5.4, caps=True),
        dict(label="R5_Freezer - closed", prop="R5_Freezer", hide=(), env="Ref_Floor",
             off=(0, 0, 0), tgt=(0, 0, 0.5), d=3.0),
    ]
    paths = []
    for i, c in enumerate(cells):
        for o in legacy:
            o.hide_render = True
        for parts in NEWP.values():
            for (o, pv) in parts.values():
                o.hide_render = True
        for nm, o in env.items():
            o.hide_render = nm != c["env"]
        for part, (o, pv) in NEWP[c["prop"]].items():
            o.location = Vector(pv) + Vector(c["off"])
            o.rotation_euler = (-math.radians(c["lid"]) if (part == "Lid" and "lid" in c)
                                else 0, 0, 0)
            o.hide_render = part in c["hide"]
        if c.get("caps"):
            cr = capsule("Ref_Crouched", 0.22, 1.1, "M_PlayerCrouch")
            cr.location = (0, 0, 0)
            st = capsule("Ref_Standing", 0.22, 2.0, "M_PlayerStand")
            st.location = (1.45, -0.3, 0)
            extras += [cr, st]
        if c.get("handles"):
            for sx in (-1, 1):
                bm = bmesh.new()
                bmesh.ops.create_cube(bm, size=1.0)
                me = bpy.data.meshes.new("Ref_Handle")
                bm.to_mesh(me)
                bm.free()
                h = bpy.data.objects.new("Ref_Handle", me)
                bpy.context.scene.collection.objects.link(h)
                h.scale = (0.03, 0.03, 0.16)
                h.location = (sx * 0.178, 0.0, 1.2 + 0.1675)
                me.materials.append(MATS["M_Iron"])
                extras.append(h)
        tgt = Vector(c["tgt"])
        dirv = Vector((-0.62, -0.80, 0.42)).normalized()
        aim(cam, tgt + dirv * c["d"], tgt)
        lbl = cam_label(c["label"], cam)
        p = os.path.join(SCRATCH, "batchA_cell%d.png" % i)
        render_to(p)
        paths.append(p)
        bpy.data.objects.remove(lbl, do_unlink=True)
        for e in extras:
            bpy.data.objects.remove(e, do_unlink=True)
        extras.clear()

    import numpy as np
    cw, ch = 600, 400
    grid = np.zeros((ch * 3, cw * 3, 4), dtype=np.float32)
    grid[..., 3] = 1.0
    for i, p in enumerate(paths):
        img = bpy.data.images.load(p)
        img.colorspace_settings.name = 'Non-Color'
        arr = np.empty(cw * ch * 4, dtype=np.float32)
        img.pixels.foreach_get(arr)
        arr = arr.reshape(ch, cw, 4)
        r, col = divmod(i, 3)
        y0 = (2 - r) * ch
        grid[y0:y0 + ch, col * cw:(col + 1) * cw] = arr
    out = bpy.data.images.new("batchA_grid", cw * 3, ch * 3, alpha=False)
    out.colorspace_settings.name = 'Non-Color'
    out.pixels.foreach_set(grid.reshape(-1))
    out.filepath_raw = BATCH_PREVIEW
    out.file_format = 'PNG'
    out.save()
    print("PREVIEW", BATCH_PREVIEW)


def main():
    clear_scene()
    try:
        bpy.context.preferences.filepaths.save_version = 0
    except Exception:
        pass
    build_materials()
    # Existing R3 props are rebuilt only so they stay in the .blend (no re-export).
    legacy = []
    for name, (fn, folder) in PROPS.items():
        bld = fn()
        bld.finalize()
        legacy.append(bld.to_object())

    for name, (fn, folder) in NEW_PROPS.items():
        NEWP[name] = {}
        for bld, pivot in fn():
            NEWP[name][bld.name] = (bld.to_object(pivot), Vector(pivot))
        objs = [o for (o, pv) in NEWP[name].values()]
        path = os.path.join(ROOT, folder.replace("/", os.sep), name + ".fbx")
        bpy.context.view_layer.update()
        mn, mx = world_bounds(objs)
        export_parts(objs, path)
        print("PROP", name)
        for part, (o, pv) in NEWP[name].items():
            d = o.dimensions
            print("   %-12s tris=%4d dims=(%.3f %.3f %.3f) pivot=(%.4f %.4f %.4f) mats=%s" % (
                part, tri_count(o), d.x, d.y, d.z, pv.x, pv.y, pv.z,
                [m.name for m in o.data.materials]))
        print("   TOTAL tris=%d bounds min=(%.4f %.4f %.4f) max=(%.4f %.4f %.4f) "
              "size=(%.4f %.4f %.4f)" % (sum(tri_count(o) for o in objs), *mn, *mx, *(mx - mn)))
        print("EXPORTED", path, os.path.getsize(path), "bytes")

    # lay the props out in a row for the saved .blend (after export)
    for i, o in enumerate(legacy):
        o.location = (-3.0 - 2.0 * i, 0, 0)
    x = 0.0
    for name in NEW_PROPS:
        zoff = 1.0 if name in ("R1_KeyCabinet", "R3_Keypad", "R5_ChainLock") else 0.0
        for part, (o, pv) in NEWP[name].items():
            o.location = pv + Vector((x, 0, zoff))
        x += 2.2
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=False)
    print("SAVED", BLEND_PATH)
    build_batch_preview(legacy)


main()
