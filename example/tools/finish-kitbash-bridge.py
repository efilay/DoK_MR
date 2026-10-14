"""Remove the donor hull's duplicate cabin after installing the chosen command bridge."""
import bpy,bmesh,json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets/kitbash/khaaneph-assault-cruiser'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
collection=bpy.data.collections['Khaaneph Assault Cruiser']
parts=[obj for obj in collection.objects if obj.type=='MESH']
armature=next(obj for obj in collection.objects if obj.type=='ARMATURE')
hull=collection.objects['Khaaneph rectangular armored hull']
bm=bmesh.new();bm.from_mesh(hull.data)
obsolete=[]
for face in bm.faces:
    p=face.calc_center_median()
    if -24<p.x<-14 and -41<p.y<-25 and p.z>27:obsolete.append(face)
bmesh.ops.delete(bm,geom=obsolete,context='FACES')
bm.normal_update();bm.to_mesh(hull.data);bm.free();hull.data.update()
manifest=json.loads((OUT/'kitbash-manifest.json').read_text())
for part in manifest['parts']:
    if part['part']==hull.name:
        part['vertices']=len(hull.data.vertices);part['triangles']=len(hull.data.polygons)
manifest['bridge']='Production Cruiser cabin; original donor hull cabin removed'
(OUT/'kitbash-manifest.json').write_text(json.dumps(manifest,indent=2))
bpy.ops.object.select_all(action='DESELECT');armature.select_set(True)
for obj in parts:obj.hide_set(False);obj.select_set(True)
bpy.context.view_layer.objects.active=armature
bpy.ops.export_scene.gltf(filepath=str(OUT/'Khaaneph_Assault_Cruiser.glb'),export_format='GLB',use_selection=True,export_animations=False,export_extras=True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Khaaneph_Assault_Cruiser.fbx'),use_selection=True,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=True)
scene=bpy.context.scene;camera=scene.camera
for label,position,target,scale in [('front',(-130,175,110),(0,0,16),166),('rear',(130,-175,135),(0,0,16),166),('top',(0,-10,240),(0,0,16),145),('bridge-detail',(-60,-90,60),(-13,-20,29),42)]:
    for obj in bpy.data.objects:
        if obj.type=='MESH' and obj.name!='StudioFloor':obj.hide_render=obj not in parts
    camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
    scene.render.filepath=str(OUT/(label+'.png'));bpy.ops.render.render(write_still=True)
camera.location=(-130,175,110);camera.rotation_euler=(Vector((0,0,16))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=166
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
print('BRIDGE_FINISH_PASS',len(obsolete),'obsolete faces removed',flush=True)
