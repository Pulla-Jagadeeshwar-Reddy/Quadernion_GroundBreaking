"""Batch B route props: the models that were still placeholder boxes in Unity.

Run:  blender -b --factory-startup -P Tools/Blender/build_route_props_b.py
Idempotent: clears the scene, rebuilds, overwrites the FBX files and ArtSource/Routes/RoutePropsB.blend.

Props (FBX name -> parts the Unity route builder looks up by name):
  R2_FusePanel   Body, Cover, Fuses, LEDs     floor-standing board; Cover comes off, Fuses appear
  R2_Shutter     Shutter                      1 m wide roller shutter, scaled to the stairwell side
  R3_Ladder      Base, Stage1, Stage2, Stage3 origin at the railing top; the ladder hangs down
  R1_TV          Cart, TV, Screen             AV cart with a CRT showing the passcode
  R3_Plank       R3_Plank                     pickup
  R3_Nails       R3_Nails                     pickup
  S_JanitorSign  Plaque, Text
  S_ExitSign     Post, Sign, Text
Conventions as in route_common: metres, Z up, front faces -Y, origin bottom-centre unless noted.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import route_common as rc  # noqa: E402
from route_common import B, MATS, make_mat  # noqa: E402

BLEND_PATH = os.path.join(rc.ROOT, "ArtSource", "Routes", "RoutePropsB.blend")
ROUTE1 = "Assets/Routes/Route01_Locksmith/Models"
ROUTE2 = "Assets/Routes/Route02_Blackout/Models"
ROUTE3 = "Assets/Routes/Route03_Hacker/Models"
SHARED = "Assets/Routes/Shared/Models"


def extra_materials():
    make_mat("M_Cardboard", (0.58, 0.42, 0.25), 0.9)
    make_mat("M_ShutterSteel", (0.42, 0.44, 0.47), 0.45, 0.85)
    make_mat("M_Porcelain", (0.93, 0.93, 0.9), 0.25)
    make_mat("M_PanelGrey", (0.55, 0.58, 0.6), 0.5, 0.4)
    make_mat("M_ExitGreen", (0.03, 0.45, 0.12), 0.4, emission=2.5, emit_color=(0.1, 1.0, 0.3))
    make_mat("M_SignWhite", (0.95, 0.95, 0.95), 0.4, emission=1.2, emit_color=(1.0, 1.0, 1.0))
    make_mat("M_ScreenGreen", (0.01, 0.02, 0.01), 0.2, emission=2.0, emit_color=(0.25, 1.0, 0.35))
    make_mat("M_ScreenDim", (0.02, 0.06, 0.04), 0.15, emission=0.6, emit_color=(0.1, 0.35, 0.2))
    make_mat("M_Plaque", (0.08, 0.09, 0.1), 0.6)


# ---------------------------------------------------------------- R2 fuse panel
def fuse_panel():
    W, D, Z0, Z1 = 0.72, 0.32, 0.10, 1.75      # cabinet body, sits on a plinth
    T = 0.02
    body = B("Body")
    body.box((0, 0, Z0 / 2), (W - 0.06, D - 0.06, Z0), 'M_Iron')                       # plinth
    body.box((0, D / 2 - T / 2, (Z0 + Z1) / 2), (W, T, Z1 - Z0), 'M_PanelGrey')        # back
    for sx in (-1, 1):
        body.box((sx * (W / 2 - T / 2), 0, (Z0 + Z1) / 2), (T, D, Z1 - Z0), 'M_PanelGrey', bevel=0.004)
    body.box((0, 0, Z1 - T / 2), (W, D, T), 'M_PanelGrey', bevel=0.004)                 # top
    body.box((0, 0, Z0 + T / 2), (W, D, T), 'M_PanelGrey')                               # bottom
    body.box((0, D / 2 - T - 0.005, (Z0 + Z1) / 2), (W - 2 * T, 0.01, Z1 - Z0 - 2 * T), 'M_Enamel')  # mounting plate
    # breaker row (top), fuse holders (middle), terminal strip (bottom)
    for i in range(8):
        x = -0.26 + i * 0.074
        body.box((x, 0.06, 1.48), (0.05, 0.08, 0.12), 'M_DarkRubber', bevel=0.004)
        body.box((x, 0.015, 1.50), (0.016, 0.02, 0.03), 'M_YellowPlastic' if i % 3 else 'M_RedPaint')
    for x in (-0.18, 0.0, 0.18):
        body.box((x, 0.08, 1.12), (0.11, 0.05, 0.26), 'M_Porcelain', bevel=0.006)        # holder block
        for z in (1.02, 1.22):
            body.box((x, 0.045, z), (0.05, 0.02, 0.025), 'M_Brass')                      # clips
        body.box((x, 0.05, 1.12), (0.035, 0.005, 0.12), 'M_DarkRubber')                  # empty slot
    for i in range(10):
        body.box((-0.24 + i * 0.053, 0.08, 0.55), (0.03, 0.06, 0.05), 'M_DarkRubber')
    # main isolator handle on the right side
    body.box((W / 2 + 0.03, -0.02, 1.25), (0.06, 0.12, 0.16), 'M_DarkRubber', bevel=0.01)
    body.box((W / 2 + 0.045, -0.09, 1.30), (0.04, 0.12, 0.04), 'M_RedPaint', bevel=0.008)
    # conduits up to the ceiling
    for x in (-0.2, 0.0, 0.2):
        body.cyl((x, 0.05, Z1), (x, 0.05, 2.6), 0.025, 'M_Steel', seg=8)

    cover = B("Cover")
    cy = -D / 2 - 0.006
    cover.box((0, cy, (Z0 + Z1) / 2 + 0.01), (W - 0.01, 0.012, Z1 - Z0 - 0.04), 'M_PanelGrey', bevel=0.004)
    for sx in (-1, 1):
        for z in (Z0 + 0.08, Z1 - 0.08):
            cover.cyl((sx * (W / 2 - 0.05), cy - 0.006, z), (sx * (W / 2 - 0.05), cy - 0.012, z), 0.012, 'M_Silver', seg=8)
    for i in range(6):                                                                   # louvres
        cover.box((0, cy - 0.006, 0.45 + i * 0.04), (0.36, 0.006, 0.012), 'M_Iron')
    # warning triangle sticker
    tri = [(-0.09, cy - 0.007, 1.30), (0.09, cy - 0.007, 1.30), (0.0, cy - 0.007, 1.46)]
    cover.polyprism(tri, (0, -0.002, 0), 'M_HazardYellow')
    cover.box((0, cy - 0.0095, 1.36), (0.018, 0.002, 0.07), 'M_HazardBlack')
    cover.box((0, cy - 0.0095, 1.315), (0.018, 0.002, 0.018), 'M_HazardBlack')

    fuses = B("Fuses")
    for x in (-0.18, 0.0, 0.18):
        fuses.cyl((x, 0.035, 1.03), (x, 0.035, 1.21), 0.022, 'M_Porcelain', seg=10)
        for z0, z1 in ((1.01, 1.04), (1.20, 1.23)):
            fuses.cyl((x, 0.035, z0), (x, 0.035, z1), 0.025, 'M_Brass', seg=10)

    leds = B("LEDs")
    for i, x in enumerate((-0.12, 0.0, 0.12)):
        leds.box((x, -D / 2 - 0.004, Z1 - 0.045), (0.035, 0.012, 0.02), 'M_StatusLight')
    return [(body, (0, 0, 0)), (cover, (0, 0, 0)), (fuses, (0, 0, 0)), (leds, (0, 0, 0))]


# ------------------------------------------------------------------ R2 shutter
def shutter():
    s = B("Shutter")
    H, W = 2.2, 1.0
    n = 20
    for i in range(n):                                       # slats with a slight ridge
        z = 0.12 + i * (H - 0.4) / n
        s.box((0, 0, z + 0.04), (W, 0.03, 0.08), 'M_ShutterSteel', bevel=0.006)
        s.box((0, -0.018, z + 0.04), (W, 0.006, 0.012), 'M_Iron')
    s.box((0, 0, 0.05), (W, 0.06, 0.1), 'M_Iron', bevel=0.01)                            # bottom bar
    for i in range(5):                                                                   # hazard stripes
        s.box((-0.4 + i * 0.2, -0.032, 0.05), (0.1, 0.004, 0.08), 'M_HazardYellow' if i % 2 == 0 else 'M_HazardBlack')
    s.box((0, 0.02, H - 0.13), (W, 0.28, 0.26), 'M_ShutterSteel', bevel=0.02)          # roller box
    s.box((0, -0.125, H - 0.13), (0.22, 0.004, 0.08), 'M_HazardYellow')                 # label plate
    return [(s, (0, 0, 0))]


# ------------------------------------------------------------------- R3 ladder
def ladder():
    """Origin at the railing top (0,0,0): stages hang down in -Z, stacked so that
    Stage1 covers z 0.6..-0.8, Stage2 -0.8..-2.2, Stage3 -2.2..-3.6 (matches the build stages)."""
    parts = []
    spans = ((0.6, -0.8), (-0.8, -2.2), (-2.2, -3.6))
    for k, (top, bottom) in enumerate(spans):
        st = B("Stage%d" % (k + 1))
        for sx in (-1, 1):
            st.box((sx * 0.24, 0, (top + bottom) / 2), (0.06, 0.045, top - bottom), 'M_Wood', bevel=0.006)
        z = top - 0.15
        while z > bottom + 0.05:
            st.box((0, -0.03, z), (0.56, 0.03, 0.07), 'M_WoodDark', bevel=0.004)
            for sx in (-1, 1):
                st.cyl((sx * 0.24, -0.047, z), (sx * 0.24, -0.052, z), 0.008, 'M_Iron', seg=6)
            z -= 0.3
        if k == 0:                                                # hooks over the railing
            for sx in (-1, 1):
                st.tube([(sx * 0.24, 0, 0.55), (sx * 0.24, 0, 0.75), (sx * 0.24, 0.12, 0.85),
                         (sx * 0.24, 0.24, 0.75), (sx * 0.24, 0.24, 0.6)], 0.018, 8, 'M_Iron')
        parts.append((st, (0, 0, 0)))

    base = B("Base")                                              # build site: a plank stack + rope
    for i, (rz, y) in enumerate(((0.0, 0.0), (0.25, 0.05), (-0.18, -0.04))):
        base.box((0, y - 0.6, 0.0125 + i * 0.025), (1.0, 0.12, 0.025), 'M_Wood', bevel=0.003,
                 rot=(0, 0, rz))
    ring = [(0.45 + 0.12 * math.cos(a), -0.6 + 0.12 * math.sin(a), 0.03)
            for a in [2 * math.pi * k / 16 for k in range(16)]]
    base.tube(ring, 0.018, 6, 'M_Cardboard', closed=True)
    parts.append((base, (0, 0, 0)))
    return parts


# ---------------------------------------------------------------------- R1 TV
def tv():
    cart = B("Cart")
    W, D, H = 0.66, 0.46, 0.95
    for sx in (-1, 1):
        for sy in (-1, 1):
            cart.box((sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), (H + 0.08) / 2), (0.03, 0.03, H - 0.08), 'M_Steel')
            cart.cyl((sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), 0.0), (sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), 0.08),
                     0.035, 'M_DarkRubber', seg=8)
    for z in (0.2, 0.6, H):
        cart.box((0, 0, z), (W, D, 0.025), 'M_Iron', bevel=0.004)
    cart.box((0, -0.05, 0.62), (0.4, 0.3, 0.09), 'M_CasePlastic', bevel=0.01)          # VCR on the shelf
    cart.box((0, -0.201, 0.63), (0.25, 0.004, 0.02), 'M_DarkRubber')

    t = B("TV")
    zc = H + 0.012 + 0.25
    t.box((0, 0, zc), (0.6, 0.42, 0.5), 'M_CasePlastic', bevel=0.02)
    t.loft(0.21, (0, zc), (0.48, 0.4), 0.42, (0, zc), (0.3, 0.26), 'M_CasePlastic')
    for sx in (-1, 1):                                                                   # bezel
        t.box((sx * 0.255, -0.215, zc), (0.06, 0.02, 0.46), 'M_Iron')
    t.box((0, -0.215, zc + 0.205), (0.45, 0.02, 0.05), 'M_Iron')
    t.box((0, -0.215, zc - 0.205), (0.45, 0.02, 0.05), 'M_Iron')
    t.box((0.2, -0.228, zc - 0.205), (0.03, 0.006, 0.012), 'M_StatusLightGreen')
    t.tube([(-0.12, 0.12, zc + 0.25), (-0.22, 0.14, zc + 0.48)], 0.006, 6, 'M_Silver')  # rabbit ears
    t.tube([(0.12, 0.12, zc + 0.25), (0.24, 0.14, zc + 0.46)], 0.006, 6, 'M_Silver')

    scr = B("Screen")
    sy = -0.208
    scr.quad([(-0.22, sy, zc - 0.18), (0.22, sy, zc - 0.18), (0.22, sy, zc + 0.18), (-0.22, sy, zc + 0.18)],
             'M_ScreenDim', uvs=[(0, 0), (1, 0), (1, 1), (0, 1)])
    # "CODE" bar and four 7-segment style digits (8 3 1 5) in bright green
    sy2 = sy - 0.002
    scr.box((0, sy2, zc + 0.12), (0.2, 0.002, 0.03), 'M_ScreenGreen')
    segs = {  # segment boxes per digit: (dx, dz, w, h) in a 0.07 x 0.12 cell
        'a': (0, 0.055, 0.05, 0.012), 'g': (0, 0.0, 0.05, 0.012), 'd': (0, -0.055, 0.05, 0.012),
        'f': (-0.028, 0.028, 0.012, 0.05), 'b': (0.028, 0.028, 0.012, 0.05),
        'e': (-0.028, -0.028, 0.012, 0.05), 'c': (0.028, -0.028, 0.012, 0.05)}
    digits = {'8': 'abcdefg', '3': 'abcdg', '1': 'bc', '5': 'afgcd'}
    for i, ch in enumerate("8315"):
        x0 = -0.135 + i * 0.09
        for sname in digits[ch]:
            dx, dz, w, h = segs[sname]
            scr.box((x0 + dx, sy2, zc - 0.03 + dz), (w, 0.002, h), 'M_ScreenGreen')
    return [(cart, (0, 0, 0)), (t, (0, 0, 0)), (scr, (0, 0, 0))]


# ------------------------------------------------------------- R3 plank, nails
def plank():
    p = B("R3_Plank")
    p.box((0, 0, 0.0125), (1.0, 0.12, 0.025), 'M_Wood', bevel=0.004)
    for x in (-0.3, 0.05, 0.36):                                 # grain lines
        p.box((x, -0.02, 0.0252), (0.22, 0.004, 0.0008), 'M_WoodDark')
    for x in (-0.46, 0.46):                                      # nail holes
        p.cyl((x, 0.0, 0.025), (x, 0.0, 0.0262), 0.006, 'M_Iron', seg=6)
    return [(p, None)]


def nails():
    n = B("R3_Nails")
    W, D, H = 0.12, 0.08, 0.06
    n.box((0, 0, 0.002), (W, D, 0.004), 'M_Cardboard')
    for sx in (-1, 1):
        n.box((sx * (W / 2 - 0.002), 0, H / 2), (0.004, D, H), 'M_Cardboard')
        n.box((0, sx * (D / 2 - 0.002), H / 2), (W, 0.004, H), 'M_Cardboard')
    n.box((0, -D / 2 - 0.001, 0.035), (0.08, 0.002, 0.025), 'M_RedPaint')             # label
    for i in range(9):                                            # nails sticking out
        x = -0.04 + (i % 3) * 0.04
        y = -0.022 + (i // 3) * 0.022
        lean = 0.012 * ((i % 2) * 2 - 1)
        n.cyl((x, y, 0.01), (x + lean, y, 0.085), 0.003, 'M_Steel', seg=5)
        n.cyl((x + lean, y, 0.085), (x + lean, y, 0.089), 0.007, 'M_Steel', seg=6)
    for x, y in ((0.09, 0.02), (0.1, -0.03)):                     # loose ones
        n.cyl((x, y, 0.004), (x + 0.07, y + 0.01, 0.004), 0.003, 'M_Steel', seg=5)
    return [(n, None)]


# ----------------------------------------------------------------------- signs
def text_object(name, text, size, location, mat, extrude=0.003):
    bpy.ops.object.text_add(location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.data.body = text
    obj.data.size = size
    obj.data.align_x = 'CENTER'
    obj.data.align_y = 'CENTER'
    obj.data.extrude = extrude
    obj.rotation_euler = (math.radians(90), 0, 0)               # stand up, readable from -Y
    bpy.ops.object.convert(target='MESH')
    obj = bpy.context.active_object
    obj.data.materials.clear()
    obj.data.materials.append(MATS[mat])
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def janitor_sign():
    pl = B("Plaque")
    pl.box((0, 0, 0.09), (0.5, 0.02, 0.18), 'M_Brass', bevel=0.004)
    pl.box((0, -0.0105, 0.09), (0.46, 0.002, 0.14), 'M_Plaque')
    return [(pl, (0, 0, 0))], [("Text", "JANITOR", 0.075, (0, -0.013, 0.09), 'M_Brass')]


def exit_sign():
    post = B("Post")
    post.cyl((0, 0, 0), (0, 0, 0.02), 0.16, 'M_Iron', seg=12)
    post.cyl((0, 0, 0.02), (0, 0, 1.9), 0.022, 'M_Steel', seg=8)
    sign = B("Sign")
    sign.box((0, 0, 2.0), (0.42, 0.06, 0.18), 'M_ExitGreen', bevel=0.01)
    arrow = [(0.09, -0.032, 1.975), (0.17, -0.032, 2.0), (0.09, -0.032, 2.025)]
    sign.polyprism(arrow, (0, -0.003, 0), 'M_SignWhite')
    return [(post, (0, 0, 0)), (sign, (0, 0, 0))], [("Text", "EXIT", 0.085, (-0.05, -0.034, 2.0), 'M_SignWhite')]


PROPS = {
    "R2_FusePanel": (fuse_panel, ROUTE2),
    "R2_Shutter": (shutter, ROUTE2),
    "R3_Ladder": (ladder, ROUTE3),
    "R1_TV": (tv, ROUTE1),
    "R3_Plank": (plank, ROUTE3),
    "R3_Nails": (nails, ROUTE3),
    "S_JanitorSign": (janitor_sign, SHARED),
    "S_ExitSign": (exit_sign, SHARED),
}


def main():
    rc.clear_scene()
    rc.build_materials()
    extra_materials()
    x = 0.0
    for name, (fn, folder) in PROPS.items():
        result = fn()
        parts, texts = result if isinstance(result, tuple) else (result, [])
        objs = []
        for bld, pivot in parts:
            if pivot is None:
                bld.finalize()
            objs.append(bld.to_object(pivot))
        for tname, text, size, loc, mat in texts:
            objs.append(text_object(tname, text, size, loc, mat))
        bpy.context.view_layer.update()
        path = os.path.join(rc.ROOT, folder.replace("/", os.sep), name + ".fbx")
        rc.export_parts(objs, path)
        mn, mx = rc.world_bounds(objs)
        print("PROP %-14s parts=%s tris=%d size=(%.2f %.2f %.2f)" % (
            name, [o.name for o in objs], sum(rc.tri_count(o) for o in objs), *(mx - mn)))
        for o in objs:                                   # lay out side by side in the .blend
            o.location.x += x
        x += 2.0
    os.makedirs(os.path.dirname(BLEND_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=False)
    print("SAVED", BLEND_PATH)


main()
