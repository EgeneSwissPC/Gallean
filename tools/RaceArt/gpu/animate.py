"""Key battle animations on a UniRig-rigged unit and render them like the game's battle frames.

blender -b -P animate.py -- <rigged.glb> <out dir> <bone map json>
Bone map: {"hips","spine","chest","head","weapon_arm":[shoulder,upper,fore],"shield_arm":[...],
           "leg_l":[...],"leg_r":[...],"weapon_bones":[...], "weapon_parent": bone}
  weapon_bones are merged into weapon_parent's vertex group, so the weapon stays rigid.
Writes <out>/<side>/<anim>/<frame>.png (side attacker = seen from the front, defender = from behind,
as in the original frames) and <out>/frames.json (canvas, foot point, trigger frame).
Motion is written in WORLD axes (forward = -Y, up = +Z) and converted per bone, so it does not
depend on UniRig's bone roll.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
glb, out, bone_map = args[0], args[1], json.load(open(args[2]))

CANVAS_W, CANVAS_H, FOOT_FROM_BOTTOM = 300, 260, 20   # logical px
PIXEL_SCALE = 2
UNIT_HEIGHT_PX = 140                                     # logical px for the whole figure
SIDES = {"attacker": 35, "defender": 215}                # camera azimuth from the unit's front
ELEVATION = 28

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.context.scene.objects):
    if o.type == "MESH" and o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
mesh = max((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
mw = mesh.matrix_world
arm.animation_data_clear()

PREPARED = bool(bone_map.get("prepared"))   # rig_fix.py output: weapon is its own object, body heat-weighted
if not PREPARED:
    # Rigid weapon: move the weapon bones' weights onto the hand/forearm bone.
    parent_group = mesh.vertex_groups[bone_map["weapon_parent"]]
    pts = []
    for name in bone_map["weapon_bones"]:
        group = mesh.vertex_groups.get(name)
        for v in mesh.data.vertices:
            for g in v.groups:
                if group is not None and g.group == group.index and g.weight > 0.9:
                    pts.append(mesh.matrix_world @ v.co)
    for name in bone_map["weapon_bones"]:
        group = mesh.vertex_groups.get(name)
        if group is None:
            continue
        for v in mesh.data.vertices:
            for g in v.groups:
                if g.group == group.index and g.weight > 0:
                    parent_group.add([v.index], g.weight, "ADD")
        mesh.vertex_groups.remove(group)

    # The automatic skin also gives parts of a long weapon to nearby leg/hip bones. Capture every vertex
    # close to the weapon's line (through the weapon bones, extended both ways) fully onto the parent bone.
    # The line is fitted to the vertices the skin gave to the weapon bones (the bones themselves don't run
    # through the shaft), then extended over the whole mesh height.
    mw = mesh.matrix_world
    centre = sum(pts, Vector()) / len(pts)
    cov = Matrix(((0, 0, 0), (0, 0, 0), (0, 0, 0)))
    for p in pts:
        d = p - centre
        for i in range(3):
            for j in range(3):
                cov[i][j] += d[i] * d[j]
    axis = Vector((0, 0, 1))
    for _ in range(50):                      # power iteration -> main direction
        axis = (cov @ axis).normalized()
    grip = centre
    dists = sorted((p - (centre + axis * (p - centre).dot(axis))).length for p in pts)
    shaft_r = bone_map.get("weapon_radius") or dists[len(dists) // 2] * 1.6
    ts = [(p - centre).dot(axis) for p in pts]
    head_from = max(ts) - bone_map.get("weapon_head_length", 0.25)
    head_r = bone_map.get("weapon_head_radius", 0.12)
    print("weapon line", tuple(round(c, 3) for c in axis), "shaft radius", round(shaft_r, 3), "points", len(pts))
    captured = 0
    for v in mesh.data.vertices:
        p = mw @ v.co
        t = (p - grip).dot(axis)
        dist = (p - (grip + axis * t)).length
        radius = head_r if t > head_from else shaft_r
        if dist < radius:
            for g in list(v.groups):
                mesh.vertex_groups[g.group].remove([v.index])
            parent_group.add([v.index], 1.0, "REPLACE")
            captured += 1
    print("weapon vertices captured", captured)
    weapon_verts = set()
    for v in mesh.data.vertices:
        for g in v.groups:
            if g.group == parent_group.index and g.weight >= 0.999 and len(v.groups) == 1:
                weapon_verts.add(v.index)

    # Capsule cleanup: the automatic skin lets bones own vertices far away from them (the hanging arm owns
    # part of the kelp skirt, a leg owns the heel of the other foot). Drop every weight whose vertex is
    # farther from that bone's segment than the bone's reach, renormalise, and give orphans to the
    # nearest bone.
    segs = {}
    for b in arm.data.bones:
        h, t = arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local
        segs[b.name] = (h, t, max(0.10, (t - h).length * 0.6))


    def seg_dist(p, h, t):
        d = t - h
        k = 0.0 if d.length_squared == 0 else max(0.0, min(1.0, (p - h).dot(d) / d.length_squared))
        return (p - (h + d * k)).length


    reach = bone_map.get("reach_factor", 1.0)
    names = {g.index: g.name for g in mesh.vertex_groups}
    dropped = orphans = 0
    for v in mesh.data.vertices:
        if v.index in weapon_verts:
            continue
        p = mw @ v.co
        keep = []
        for g in list(v.groups):
            name = names.get(g.group)
            if name not in segs or g.weight <= 0:
                continue
            h, t, r = segs[name]
            if seg_dist(p, h, t) <= r * reach:
                keep.append((g.group, g.weight))
            else:
                dropped += 1
        for g in list(v.groups):
            mesh.vertex_groups[g.group].remove([v.index])
        if not keep:
            orphans += 1
            best = min((n for n in segs if n not in bone_map["weapon_bones"] and n in mesh.vertex_groups),
                       key=lambda n: seg_dist(p, segs[n][0], segs[n][1]))
            keep = [(mesh.vertex_groups[best].index, 1.0)]
        total = sum(w for _, w in keep)
        for gi, w in keep:
            mesh.vertex_groups[gi].add([v.index], w / total, "REPLACE")
    print("capsule cleanup: dropped", dropped, "weights, orphans", orphans)
    if os.environ.get("DEBUG_WEAPON"):   # paint the captured weapon red to check the capture
        red = bpy.data.materials.new("debug_red")
        red.diffuse_color = (1, 0, 0, 1)
        red.use_nodes = True
        red.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0, 0, 1)
        mesh.data.materials.append(red)
        slot = len(mesh.data.materials) - 1
        for poly in mesh.data.polygons:
            if all(i in weapon_verts for i in poly.vertices):
                poly.material_index = slot

if PREPARED and os.environ.get("DEBUG_WEAPON"):
    red = bpy.data.materials.new("debug_red")
    red.use_nodes = True
    red.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0, 0, 1)
    for o in bpy.context.scene.objects:
        if o.type == "MESH" and o is not mesh:
            o.data.materials.clear()
            o.data.materials.append(red)

pb = arm.pose.bones
for b in pb:
    b.rotation_mode = "QUATERNION"
W = arm.matrix_world.to_3x3()


def world_rot(bone, axis, degrees):
    """Local quaternion that rotates `bone` by `degrees` about a world axis (rest frame)."""
    rest = bone.bone.matrix_local.to_3x3()
    axis_local = (rest.inverted() @ W.inverted() @ Vector(axis)).normalized()
    return Matrix.Rotation(math.radians(degrees), 3, axis_local).to_quaternion()


def world_loc(bone, offset):
    rest = bone.bone.matrix_local.to_3x3()
    return rest.inverted() @ W.inverted() @ Vector(offset)


def get(key):
    name, _, idx = key.partition(".")
    value = bone_map[name]
    return pb[value[int(idx)] if idx else value]


X, Z = (1, 0, 0), (0, 0, 1)   # +X rotation leans forward (up -> -Y); -X raises a hanging arm forward


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def idle(t):
    s = math.sin(2 * math.pi * t)
    return [("spine", X, 1.5 * s), ("chest", X, 1.5 * s), ("head", Z, 3 * s),
            ("weapon_arm.1", X, -2 * s), ("shield_arm.1", X, 2 * s)], (0, 0, 0.006 * s)


def attack(t):
    # anticipation (0-0.3), lunge + thrust (0.3-0.5, impact at 0.5), hold (0.5-0.65), recover (0.65-1)
    wind = ease(t / 0.3) * (1 - ease((t - 0.3) / 0.2))
    lunge = ease((t - 0.3) / 0.2) * (1 - ease((t - 0.65) / 0.35))
    lean = -10 * wind + 22 * lunge
    # The weapon is held upright: tilt the forearm forward (+X) so the trident points at the enemy.
    return [("spine", X, lean * 0.5), ("chest", X, lean * 0.5), ("chest", Z, 15 * wind - 20 * lunge),
            ("weapon_arm.1", X, 20 * wind - 35 * lunge), ("weapon_arm.2", X, -20 * wind + 75 * lunge),
            ("shield_arm.1", X, -15 * lunge), ("head", X, -lean * 0.4),
            ("leg_l.0", X, -30 * lunge), ("leg_l.1", X, 25 * lunge), ("leg_r.0", X, 15 * lunge)], \
        (0, 0.06 * wind - 0.22 * lunge, -0.04 * lunge)


def hit(t):
    k = math.sin(math.pi * min(1.0, t * 1.25))
    return [("spine", X, -10 * k), ("chest", X, -10 * k), ("head", X, -5 * k),
            ("weapon_arm.1", X, 15 * k), ("shield_arm.1", X, -20 * k)], (0, 0.08 * k, 0)


ANIMS = {"idle": (16, idle), "attack": (24, attack), "hit": (8, hit)}
TRIGGER = {"attack": 12}


def pose(fn, t):
    for b in pb:
        b.rotation_quaternion = (1, 0, 0, 0)
        b.location = (0, 0, 0)
    rots, offset = fn(t)
    for key, axis, deg in rots:
        b = get(key)
        b.rotation_quaternion = world_rot(b, axis, deg) @ b.rotation_quaternion
    hips = get("hips")
    hips.location = world_loc(hips, offset)


scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "GPU"
prefs = bpy.context.preferences.addons["cycles"].preferences
prefs.compute_device_type = "OPTIX"
prefs.get_devices()
for d in prefs.devices:
    d.use = d.type != "CPU"
print("devices", [(d.name, d.type, d.use) for d in prefs.devices])
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = CANVAS_W * PIXEL_SCALE, CANVAS_H * PIXEL_SCALE
scene.render.film_transparent = True
scene.view_settings.view_transform = "Standard"
scene.world = bpy.data.worlds.new("w")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.5

pts = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
zmin = min(p.z for p in pts)
height = max(p.z for p in pts) - zmin
foot = Vector((0, 0, zmin))
ortho = CANVAS_W * height / UNIT_HEIGHT_PX

cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = ortho
cam.data.shift_y = (0.5 - FOOT_FROM_BOTTOM / CANVAS_H) * CANVAS_H / CANVAS_W
scene.collection.objects.link(cam)
scene.camera = cam
key = bpy.data.objects.new("key", bpy.data.lights.new("key", "SUN"))
key.data.energy = 4.0
fill = bpy.data.objects.new("fill", bpy.data.lights.new("fill", "SUN"))
fill.data.energy = 1.2
scene.collection.objects.link(key)
scene.collection.objects.link(fill)

for side, azimuth in SIDES.items():
    a, e = math.radians(azimuth), math.radians(ELEVATION)
    direction = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = foot + direction * 20
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    key.rotation_euler = (math.radians(40), 0, a - math.radians(50))
    fill.rotation_euler = (math.radians(60), 0, a + math.radians(70))
    for name, (count, fn) in ANIMS.items():
        if os.environ.get("ONLY") and name != os.environ["ONLY"]:
            continue
        os.makedirs(f"{out}/{side}/{name}", exist_ok=True)
        quick = os.environ.get("QUICK")   # e.g. QUICK=attack:6,12 renders only those attacker frames
        for i in range(count):
            if quick:
                qa, _, qf = quick.partition(":")
                if side != "attacker" or name != qa or str(i) not in qf.split(","):
                    continue
            pose(fn, i / count)
            bpy.context.view_layer.update()
            scene.render.filepath = f"{out}/{side}/{name}/{i:03d}.png"
            bpy.ops.render.render(write_still=True)
        print("rendered", side, name, count, flush=True)

json.dump({"canvas": [CANVAS_W, CANVAS_H], "foot_from_bottom": FOOT_FROM_BOTTOM, "pixel_scale": PIXEL_SCALE,
           "anims": {n: c for n, (c, _) in ANIMS.items()}, "trigger": TRIGGER},
          open(f"{out}/frames.json", "w"), indent=1)
