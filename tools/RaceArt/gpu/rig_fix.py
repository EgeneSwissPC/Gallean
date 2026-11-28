"""Re-rig a UniRig result properly: cut the weapon off as its own rigid object on the hand bone and
re-skin the body with Blender's automatic (heat) weights.

blender -b -P rig_fix.py -- <rigged.glb> <out.glb> <bone map json>
Bone map keys used: weapon_bones (UniRig bones inside the weapon), weapon_parent (hand bone),
weapon_radius (shaft radius, m), weapon_head_length / weapon_head_radius (the head at the top).
Prints how many faces went to the weapon and whether heat weighting succeeded.
"""
import json
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
src, dst, bone_map = args[0], args[1], json.load(open(args[2]))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.context.scene.objects):
    if o.type == "MESH" and o.name.startswith("Icosphere"):
        bpy.data.objects.remove(o)
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
body = next(o for o in bpy.context.scene.objects if o.type == "MESH")
mw = body.matrix_world

# 1. Weapon line from the vertices the skin gave to the weapon bones.
pts = []
for name in bone_map["weapon_bones"]:
    group = body.vertex_groups.get(name)
    if group is None:
        continue
    for v in body.data.vertices:
        for g in v.groups:
            if g.group == group.index and g.weight > 0.9:
                pts.append(mw @ v.co)
centre = sum(pts, Vector()) / len(pts)
cov = Matrix(((0, 0, 0), (0, 0, 0), (0, 0, 0)))
for p in pts:
    d = p - centre
    for i in range(3):
        for j in range(3):
            cov[i][j] += d[i] * d[j]
axis = Vector((0, 0, 1))
for _ in range(50):
    axis = (cov @ axis).normalized()
ts = [(p - centre).dot(axis) for p in pts]
shaft_r = bone_map.get("weapon_radius", 0.035)
head_from = max(ts) - bone_map.get("weapon_head_length", 0.25)
head_r = bone_map.get("weapon_head_radius", 0.12)


def on_weapon(p):
    t = (p - centre).dot(axis)
    dist = (p - (centre + axis * t)).length
    return dist < (head_r if t > head_from else shaft_r)


# 2. Cut the weapon: faces whose vertices are all on the weapon line.
bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(body.data)
for f in bm.faces:
    f.select = all(on_weapon(mw @ v.co) for v in f.verts)
# Grow from the tight seed over neighbouring faces that stay near the line (the shaft isn't perfectly
# straight, so a tight radius alone leaves pieces of it on the body).
grow_r = bone_map.get("weapon_grow_radius", 0.07)


def near_line(p, r):
    t = (p - centre).dot(axis)
    return (p - (centre + axis * t)).length < (max(r, head_r) if t > head_from else r)


# Generated shafts are often bent below the hand: follow the stick down slice by slice (2 cm), taking
# the thin cluster nearest the previous centre, until it widens (foot, ground) or ends.
verts_w = [(v, mw @ v.co) for v in bm.verts]
low = min(pts, key=lambda p: p.z)          # bottom of what the skin gave the weapon bones
c = Vector((low.x, low.y))
z = low.z
track_r = bone_map.get("weapon_track_radius", 0.05)
taken = set()
steps = 0
while True:
    sl = [(v, p) for v, p in verts_w if z - 0.02 < p.z <= z and (Vector((p.x, p.y)) - c).length < track_r]
    if len(sl) < 3:
        break
    m = sum((Vector((p.x, p.y)) for _, p in sl), Vector((0, 0))) / len(sl)
    spread = max((Vector((p.x, p.y)) - m).length for _, p in sl)
    if spread > track_r * 0.9:
        break
    taken.update(v.index for v, _ in sl)
    c, z, steps = m, z - 0.02, steps + 1
print("lower shaft tracked", steps, "slices down to z", round(z, 2))
# The butt of the shaft touches the ground next to a foot, so the slice widens there and tracking stops
# a few cm early: take everything below that point within the shaft radius of the last centre.
butt = [v.index for v, p in verts_w if p.z <= z + 0.03 and (Vector((p.x, p.y)) - c).length < bone_map.get("weapon_butt_radius", 0.08)]
taken.update(butt)
print("shaft butt vertices", len(butt))
for f in bm.faces:
    if not f.select and all(v.index in taken for v in f.verts):
        f.select = True

frontier = [f for f in bm.faces if f.select]
while frontier:
    nxt = []
    for f in frontier:
        for e in f.edges:
            for g in e.link_faces:
                if not g.select and all(near_line(mw @ v.co, grow_r) for v in g.verts):
                    g.select = True
                    nxt.append(g)
    frontier = nxt
n_faces = sum(f.select for f in bm.faces)
bmesh.update_edit_mesh(body.data)
bpy.ops.mesh.separate(type="SELECTED")
bpy.ops.object.mode_set(mode="OBJECT")
weapon = next(o for o in bpy.context.scene.objects if o.type == "MESH" and o is not body)
weapon.name = "weapon"
print("weapon faces", n_faces, "body faces", len(body.data.polygons))

# 3. Weapon: no skin, rigidly parented to the hand bone.
for o in (weapon,):
    for mod in list(o.modifiers):
        o.modifiers.remove(mod)
    o.vertex_groups.clear()
    world = o.matrix_world.copy()
    o.parent = arm
    o.parent_type = "BONE"
    o.parent_bone = bone_map["weapon_parent"]
    o.matrix_world = world

# 4. Body: drop UniRig's weights, heat weights from the deforming bones only.
for mod in list(body.modifiers):
    body.modifiers.remove(mod)
body.vertex_groups.clear()
world = body.matrix_world.copy()
body.parent = None
body.matrix_world = world
for b in arm.data.bones:
    b.use_deform = b.name not in bone_map["weapon_bones"]
# Hunyuan meshes are split at every UV seam (3 vertices per face), which makes heat weighting fail.
# Weight a welded, cleaned proxy instead and transfer its weights to the textured body.
proxy = body.copy()
proxy.data = body.data.copy()
proxy.name = "proxy"
bpy.context.scene.collection.objects.link(proxy)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = proxy
proxy.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.002)
bpy.ops.mesh.delete_loose()
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode="OBJECT")
print("proxy vertices", len(proxy.data.vertices))
bpy.ops.object.select_all(action="DESELECT")
proxy.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
proxy_unweighted = sum(1 for v in proxy.data.vertices if not v.groups)
print("proxy unweighted", proxy_unweighted, "of", len(proxy.data.vertices))

for g in proxy.vertex_groups:
    body.vertex_groups.new(name=g.name)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = body
body.select_set(True)
dt = body.modifiers.new("dt", "DATA_TRANSFER")
dt.object = proxy
dt.use_vert_data = True
dt.data_types_verts = {"VGROUP_WEIGHTS"}
dt.vert_mapping = "POLYINTERP_NEAREST"
dt.layers_vgroup_select_src = "ALL"
dt.layers_vgroup_select_dst = "NAME"
bpy.ops.object.modifier_apply(modifier=dt.name)
bpy.data.objects.remove(proxy)
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm
world = body.matrix_world.copy()
body.parent = arm
body.matrix_world = world
# Vertices heat weighting missed would stay behind in the rest pose (floating bits): copy the
# weights of the nearest weighted vertex.
from mathutils.kdtree import KDTree  # noqa: E402

weighted = [v for v in body.data.vertices if v.groups]
tree = KDTree(len(weighted))
for i, v in enumerate(weighted):
    tree.insert(v.co, i)
tree.balance()
for v in body.data.vertices:
    if not v.groups:
        _, i, _ = tree.find(v.co)
        for g in weighted[i].groups:
            body.vertex_groups[g.group].add([v.index], g.weight, "REPLACE")
unweighted = sum(1 for v in body.data.vertices if not v.groups)
print("heat weights: groups", len(body.vertex_groups), "unweighted vertices", unweighted, "of", len(body.data.vertices))

bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_skins=True, export_animations=False)
print("wrote", dst)
