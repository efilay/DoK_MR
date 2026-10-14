"""Locate the circled housing in the missile-rack view."""
import bpy,json,bmesh
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets/kitbash/khaaneph-assault-cruiser'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
hull=bpy.data.collections['Khaaneph Assault Cruiser'].objects['Khaaneph rectangular armored hull']
scene=bpy.context.scene
camera=scene.camera
camera.location=(60,-100,65)
camera.rotation_euler=(Vector((0,-37,25))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale=48
bpy.context.view_layer.update()
vertices=[]
for vertex in hull.data.vertices:
    p=world_to_camera_view(scene,camera,hull.matrix_world@vertex.co)
    if .395<p.x<.515 and .56<p.y<.81:
        vertices.append(vertex.co)
report={'screen_region_bounds':[[min(p[i] for p in vertices),max(p[i] for p in vertices)] for i in range(3)]}
print('HOUSING_LOCATION',json.dumps(report),flush=True)
core=bpy.data.collections['Khaaneph Assault Cruiser'].objects['Assault cruiser original weapons and rear suspension']
selection=[]
for vertex in core.data.vertices:
    p=world_to_camera_view(scene,camera,core.matrix_world@vertex.co)
    if .395<p.x<.515 and .56<p.y<.81:
        selection.append(vertex)
print('CORE_HOUSING_LOCATION',json.dumps({'bounds':[[min(v.co[i] for v in selection),max(v.co[i] for v in selection)] for i in range(3)],
      'bindings':list({core.vertex_groups[g.group].name for v in selection for g in v.groups})}),flush=True)
bm_core=bmesh.new();bm_core.from_mesh(core.data)
bmesh.ops.remove_doubles(bm_core,verts=list(bm_core.verts),dist=.0001)
remaining_core=set(bm_core.verts)
while remaining_core:
    first=remaining_core.pop();found={first};queue=[first]
    while queue:
        current=queue.pop()
        for edge in current.link_edges:
            other=edge.other_vert(current)
            if other in remaining_core:
                remaining_core.remove(other);found.add(other);queue.append(other)
    if len(found)>8:
        bounds=[[min(v.co[i] for v in found),max(v.co[i] for v in found)] for i in range(3)]
        if bounds[2][1]>25 and bounds[1][0]<-20 and bounds[1][1]>-32:
            print('CORE_UPPER_COMPONENT',len(found),json.dumps(bounds),flush=True)
bm_core.free()
for x in [-20,-16,-12,-8,-4]:
    samples=[]
    for y in [-31,-29,-27,-25,-23]:
        hit,p,n,f=hull.ray_cast(Vector((x,y,100)),Vector((0,0,-1)))
        samples.append([y,p.z if hit else None])
    print('BRIDGE_GRID',x,json.dumps(samples),flush=True)
bm=bmesh.new();bm.from_mesh(hull.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0001)
remaining=set(bm.verts)
while remaining:
    first=remaining.pop();found={first};queue=[first]
    while queue:
        current=queue.pop()
        for edge in current.link_edges:
            other=edge.other_vert(current)
            if other in remaining:
                remaining.remove(other);found.add(other);queue.append(other)
    if len(found)>8:
        bounds=[[min(v.co[i] for v in found),max(v.co[i] for v in found)] for i in range(3)]
        if bounds[2][0]>20 and bounds[1][0]<-15:
            print('UPPER_REAR_COMPONENT',len(found),json.dumps(bounds),flush=True)
bm.free()
