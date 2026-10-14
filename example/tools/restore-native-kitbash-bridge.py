"""Use the complete donor bridge instead of mixing partially skinned cabin pieces."""
import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets/kitbash/khaaneph-assault-cruiser'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
collection=bpy.data.collections['Khaaneph Assault Cruiser']
hull=collection.objects['Khaaneph rectangular armored hull']
with bpy.data.libraries.load(str(OUT/'Khaaneph_Assault_Cruiser.blend1'),link=False) as (source,target):
    target.meshes=[name for name in source.meshes if name=='Khaaneph rectangular armored hull']
if len(target.meshes)!=1:raise ValueError('Uncut native hull backup missing')
hull.data=target.meshes[0]
extra=collection.objects.get('Production cruiser command tower')
if extra:bpy.data.objects.remove(extra,do_unlink=True)
parts=[obj for obj in collection.objects if obj.type=='MESH']
armature=next(obj for obj in collection.objects if obj.type=='ARMATURE')
manifest=json.loads((OUT/'kitbash-manifest.json').read_text())
manifest['parts']=[part for part in manifest['parts'] if part['part']!='Production cruiser command tower']
for part in manifest['parts']:
    if part['part']==hull.name:part['vertices']=len(hull.data.vertices);part['triangles']=len(hull.data.polygons)
manifest['bridge']='Complete native Khaaneph bridge retained with the rectangular hull'
(OUT/'kitbash-manifest.json').write_text(json.dumps(manifest,indent=2))
bpy.ops.object.select_all(action='DESELECT');armature.select_set(True)
for obj in parts:obj.hide_set(False);obj.select_set(True)
bpy.context.view_layer.objects.active=armature
bpy.ops.export_scene.gltf(filepath=str(OUT/'Khaaneph_Assault_Cruiser.glb'),export_format='GLB',use_selection=True,export_animations=False,export_extras=True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Khaaneph_Assault_Cruiser.fbx'),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
scene=bpy.context.scene;camera=scene.camera
for label,position,target,scale in [('front',(-130,175,110),(0,0,16),166),('rear',(130,-175,135),(0,0,16),166),('top',(0,-6,240),(0,-6,16),185),('jaw-detail',(-65,125,55),(0,41,15),78),('bridge-detail',(-60,-90,60),(-19,-33,29),42)]:
    for obj in bpy.data.objects:
        if obj.type=='MESH' and obj.name!='StudioFloor':obj.hide_render=obj not in parts
    camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
    scene.render.filepath=str(OUT/(label+'.png'));bpy.ops.render.render(write_still=True)
camera.location=(-130,175,110);camera.rotation_euler=(Vector((0,0,16))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=166
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
print('NATIVE_BRIDGE_PASS',len(parts),'parts',flush=True)
