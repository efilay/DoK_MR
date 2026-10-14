"""Inspect the circled Baserunner fitting and its support against the hull."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets/kitbash/khaaneph-assault-cruiser'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'Khaaneph_Assault_Cruiser.blend'))
collection = bpy.data.collections['Khaaneph Assault Cruiser']
pod = collection.objects['Baserunner armored service pod']
hull = collection.objects['Khaaneph rectangular armored hull']
points = [pod.matrix_world @ vertex.co for vertex in pod.data.vertices]
extent = [[min(p[i] for p in points), max(p[i] for p in points)] for i in range(3)]

def hull_height(x, y):
    local = hull.matrix_world.inverted() @ Vector((x, y, 100))
    hit, point, normal, face = hull.ray_cast(local, Vector((0, 0, -1)))
    return (hull.matrix_world @ point).z if hit else None

lowest = sorted(points, key=lambda p: p.z)[:40]
samples = [{'position': list(p), 'hull_z': hull_height(p.x, p.y)} for p in lowest[::4]]
for sample in samples:
    if sample['hull_z'] is not None:
        sample['vertical_clearance'] = sample['position'][2] - sample['hull_z']
grid = []
for x in [extent[0][0], sum(extent[0])/2, extent[0][1]]:
    for y in [extent[1][0], sum(extent[1])/2, extent[1][1]]:
        grid.append({'x':x, 'y':y, 'hull_z':hull_height(x,y)})
report = {'object':pod.name, 'bounds':extent, 'lowest_vertex_samples':samples,
          'hull_under_fitting':grid, 'bone_bindings':list(pod.vertex_groups.keys())}
report['inboard_mount_samples'] = [
    {'x': x, 'y':y, 'hull_z':hull_height(x,y)}
    for x in [10.2, 12.0, 15.0, 18.2]
    for y in [21.7, 24.6, 28.5]
]
report['rack_deck_samples'] = [
    {'x': x, 'y': y, 'new_hull_z': hull_height(x, y)}
    for x in [-12, -8, -4, 0, 4, 8, 12]
    for y in [-43, -40, -37, -34, -31, -28]
]
native = bpy.data.objects['coalition_assault']
for sample in report['rack_deck_samples']:
    hit, point, normal, face = native.ray_cast(Vector((sample['x'], sample['y'], 100)), Vector((0, 0, -1)))
    sample['original_surface_z'] = point.z if hit else None
(ROOT/'evidence/models/service-pod-before.json').write_text(json.dumps(report, indent=2))
print('POD_INSPECTION', json.dumps(report), flush=True)
