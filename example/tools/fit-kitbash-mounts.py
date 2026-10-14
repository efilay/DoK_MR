"""Apply the mounting correction and render/export the revised kitbash."""
import bpy
import json
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'assets/kitbash/khaaneph-assault-cruiser'
sys.path.insert(0,str(ROOT/'tools'))
from kitbash_mounts import recessed_missile_bays, remove_legacy_bridge_housing, flush_marked_panel, white_turret_caps

bpy.ops.wm.open_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
collection = bpy.data.collections['Khaaneph Assault Cruiser']
hull = collection.objects['Khaaneph rectangular armored hull']
pod = collection.objects.get('Baserunner armored service pod')
if pod:
    bpy.data.objects.remove(pod,do_unlink=True)
bays = recessed_missile_bays(hull)
core=collection.objects['Assault cruiser original weapons and rear suspension']
panel=collection.objects['Khaaneph marked deck armor']
remove_legacy_bridge_housing(core)
flush_marked_panel(panel,hull)
white_turret_caps(core,bpy.data.objects['coalition_assault'])
parts = [obj for obj in collection.objects if obj.type=='MESH']
armature = next(obj for obj in collection.objects if obj.type=='ARMATURE')
scene = bpy.context.scene
camera = scene.camera

def shot(label,position,target,scale):
    for obj in bpy.data.objects:
        if obj.type=='MESH' and obj.name!='StudioFloor':
            obj.hide_render = obj not in parts
    camera.location = position
    camera.rotation_euler = (Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale = scale
    scene.render.filepath = str(OUT/(label+'.png'))
    bpy.ops.render.render(write_still=True)

if '--preview-only' in sys.argv:
    scene.cycles.samples = 16
    scene.render.resolution_percentage = 75
    shot('missile-recess-candidate',(60,-100,65),(0,-37,25),48)
    print('MOUNT_PREVIEW_PASS',json.dumps({'bays':bays}),flush=True)
    sys.exit(0)

manifest = json.loads((OUT/'kitbash-manifest.json').read_text())
manifest['parts']=[part for part in manifest['parts'] if part['part']!='Baserunner armored service pod']
for part in manifest['parts']:
    obj = collection.objects[part['part']]
    obj.data.calc_loop_triangles()
    part['vertices'] = len(obj.data.vertices)
    part['triangles'] = len(obj.data.loop_triangles)
    if obj==hull:
        part['missile_recesses'] = bays
    if obj==core:
        part['removed']='Cut-off original conning-tower base and its small antenna remnants'
        part['paint']='Both cylindrical turret caps use the original pale material'
    if obj==panel:
        part['finish']='Thin marked insert fitted flush to the hull perimeter'
manifest['revision'] = 'Recessed missile rack, flush insert and cleaned bridge; decorative pod removed'
manifest['removed_decoration']=['Baserunner service pod and its mounting bracket']
(OUT/'kitbash-manifest.json').write_text(json.dumps(manifest,indent=2))
bpy.ops.object.select_all(action='DESELECT')
armature.select_set(True)
for obj in parts:
    obj.hide_set(False)
    obj.select_set(True)
bpy.context.view_layer.objects.active = armature
bpy.ops.export_scene.gltf(filepath=str(OUT/'Khaaneph_Assault_Cruiser.glb'),
                         export_format='GLB',use_selection=True,export_animations=False,export_extras=True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Khaaneph_Assault_Cruiser.fbx'),
                        use_selection=True,add_leaf_bones=False,bake_anim=False,
                        axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
shots = [('front',(-130,175,110),(0,0,16),166),
         ('rear',(130,-175,135),(0,0,16),166),
         ('top',(0,-6,240),(0,-6,16),185),
         ('jaw-detail',(-65,125,55),(0,41,15),78),
         ('bridge-detail',(-60,-90,60),(-19,-33,29),42),
         ('missile-recess-detail',(60,-100,65),(0,-37,25),48)]
if '--no-render' not in sys.argv:
    for args in shots:
        shot(*args)
camera.location = (-130,175,110)
camera.rotation_euler = (Vector((0,0,16))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale = 166
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
print('MOUNT_REVISION_PASS',len(parts),'parts',flush=True)
