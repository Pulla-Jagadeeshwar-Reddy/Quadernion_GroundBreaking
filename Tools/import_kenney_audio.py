"""Copies the clips the game uses out of the Kenney CC0 audio packs into Assets/Resources/Audio,
renamed to the sound groups Sfx.cs plays ("group_N.ogg").

Usage: python Tools/import_kenney_audio.py <folder with the Kenney zips or unzipped folders>
Packs (kenney.nl, CC0): Impact Sounds, Interface Sounds, RPG Audio, Sci-fi Sounds.
Run again after changing GROUPS; it replaces Assets/Resources/Audio/*.ogg (keeps .meta of reused names).
"""
import fnmatch
import os
import shutil
import sys
import tempfile
import zipfile

# group -> filename patterns (first matches win, up to `limit` files per group)
GROUPS = {
    "step_floor":   (["footstep_concrete_*.ogg"], 5),
    "step_heavy":   (["footstep_wood_*.ogg", "footstep0*.ogg"], 5),
    "pickup":       (["handleSmallLeather*.ogg", "cloth*.ogg", "pluck_*.ogg"], 3),
    "place":        (["impactPlank_medium_*.ogg", "drop_00*.ogg"], 3),
    "click":        (["click_00*.ogg", "switch_00*.ogg"], 3),
    "switch":       (["switch_00*.ogg"], 2),
    "door_open":    (["doorOpen_*.ogg"], 2),
    "door_close":   (["doorClose_*.ogg"], 3),
    "door_break":   (["impactWood_heavy_*.ogg"], 3),
    "unlock":       (["metalLatch*.ogg", "metalClick*.ogg"], 2),
    "rummage":      (["cloth*.ogg", "bookFlip*.ogg", "handleSmallLeather*.ogg"], 5),
    "drawer_found": (["metalPot*.ogg", "handleCoins*.ogg"], 2),
    "drawer_empty": (["bookClose*.ogg", "creak*.ogg"], 2),
    "hit":          (["impactPunch_heavy_*.ogg", "impactWood_medium_*.ogg"], 4),
    "glass_break":  (["impactGlass_heavy_*.ogg", "glass_00*.ogg"], 2),
    "pry":          (["creak*.ogg", "impactMetal_light_*.ogg"], 4),
    # "screw", "shatter" and "ice_shatter" are synthesised by Tools/make_synth_sfx.py (WAV).
    "electric":     (["forceField_*.ogg", "computerNoise_*.ogg"], 3),
    "power_on":     (["lowFrequency_explosion_*.ogg", "spaceEngineLow_*.ogg"], 1),
    "hack":         (["computerNoise_*.ogg", "laserSmall_*.ogg"], 5),
    "beep":         (["tick_00*.ogg", "bong_001.ogg"], 2),
    "confirm":      (["confirmation_00*.ogg"], 3),
    "hammer":       (["impactPlank_medium_*.ogg", "impactWood_light_*.ogg"], 5),
    "wood_place":   (["impactWood_heavy_*.ogg", "impactPlank_medium_*.ogg"], 2),
    "metal_drop":   (["impactMetal_heavy_*.ogg"], 2),
    "ice_hit":      (["impactGlass_light_*.ogg", "impactGlass_medium_*.ogg"], 4),
    "ice_break":    (["impactGlass_heavy_*.ogg"], 2),
    "chain":        (["impactMetal_light_*.ogg", "metalClick*.ogg"], 4),
    "throw":        (["drawKnife*.ogg", "cloth*.ogg"], 2),
    "crash":        (["impactGlass_heavy_*.ogg", "impactMetal_heavy_*.ogg"], 3),
    "tick":         (["tick_00*.ogg"], 1),
    "round_start":  (["bong_001.ogg", "confirmation_001.ogg"], 1),
    "sting_win":    (["confirmation_004.ogg", "confirmation_00*.ogg"], 1),
    "sting_lose":   (["lowFrequency_explosion_*.ogg", "error_00*.ogg"], 1),
    "amb_drone":    (["spaceEngineLow_*.ogg", "engineCircular_*.ogg"], 1),
}


def collect(source):
    """All .ogg files under source, unzipping any zips into a temp folder first."""
    roots = [source]
    temp = tempfile.mkdtemp(prefix="kenney_")
    for name in os.listdir(source):
        if name.lower().endswith(".zip"):
            target = os.path.join(temp, name[:-4])
            with zipfile.ZipFile(os.path.join(source, name)) as archive:
                archive.extractall(target)
            roots.append(target)
    files = {}
    for root in roots:
        for folder, _, names in os.walk(root):
            for name in names:
                if name.lower().endswith(".ogg"):
                    files.setdefault(name, os.path.join(folder, name))
    return files


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    files = collect(sys.argv[1])
    print(f"{len(files)} .ogg files found")
    project = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    out = os.path.join(project, "Assets", "Resources", "Audio")
    os.makedirs(out, exist_ok=True)
    wanted = set()
    missing = []
    for group, (patterns, limit) in GROUPS.items():
        picked = []
        for pattern in patterns:
            for name in sorted(files):
                if fnmatch.fnmatch(name, pattern) and name not in picked:
                    picked.append(name)
                if len(picked) >= limit:
                    break
            if len(picked) >= limit:
                break
        if not picked:
            missing.append(group)
        for index, name in enumerate(picked, 1):
            target = f"{group}_{index}.ogg"
            wanted.add(target)
            shutil.copyfile(files[name], os.path.join(out, target))
        print(f"{group:13s} <- {', '.join(picked) if picked else '(none)'}")
    for name in os.listdir(out):
        if name.endswith(".ogg") and name not in wanted:
            os.remove(os.path.join(out, name))
            if os.path.exists(os.path.join(out, name + ".meta")):
                os.remove(os.path.join(out, name + ".meta"))
    print("missing groups:", ", ".join(missing) if missing else "none")
    return 0


if __name__ == "__main__":
    sys.exit(main())
