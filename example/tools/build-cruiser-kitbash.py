"""Blender authoring pipeline for the Khaaneph Assault Cruiser kitbash."""
import bpy
import bmesh
import json
import math
import sys
import numpy as np
from pathlib import Path
from mathutils import Matrix, Vector, Quaternion
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
DONORS = ROOT / "assets/kitbash/donors"
OUT = ROOT / "assets/kitbash/khaaneph-assault-cruiser"
OUT.mkdir(parents=True, exist_ok=True)
D = json.loads((DONORS / "donors.json").read_text())
sys.path.insert(0, str(ROOT / 'tools'))
from kitbash_mounts import recessed_missile_bays, remove_legacy_bridge_housing, flush_marked_panel, white_turret_caps
# Native game space has Y up. Blender uses Z up; keep the native X/front axes.
C = Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1)))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def globals_for(prefab):
    result = {}
    for transform in prefab['transforms']:
        x,y,z,w = transform['rotation']
        local = Matrix.LocRotScale(Vector(transform['position']), Quaternion((w,x,y,z)), Vector(transform['scale']))
        result[transform['key']] = result[transform['parent']] @ local if transform['parent'] else local
    return result

materials = {}
def native_material(key):
    if key in materials:
        return materials[key]
    source = D['materials'][key]
    mat = bpy.data.materials.new(source['name'])
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value = 0.73
    shader.inputs['Metallic'].default_value = 0.22
    if '_MainTex' in source['textures']:
        entry = source['textures']['_MainTex']
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(str(DONORS / D['textures'][entry['key']]['file']), check_existing=True)
        links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    mat['native_material_key'] = key
    materials[key] = mat
    return mat

def import_donor(name, collection):
    print('IMPORT_START',name,flush=True)
    prefab = D['prefabs'][name]
    world = globals_for(prefab)
    renderer = max(prefab['renderers'], key=lambda r: len(D['meshes'][r['mesh']]['vertices']))
    mesh = D['meshes'][renderer['mesh']]
    bone_matrices = [world[key] @ Matrix([pose[i:i+4] for i in range(0,16,4)])
                     for key,pose in zip(renderer['bones'], mesh.get('bindposes', []))]
    vertices = []
    for i, value in enumerate(mesh['vertices']):
        point = Vector((*value,1.0))
        if bone_matrices and mesh['weights']:
            deformed = Vector((0,0,0,0))
            for bone,weight in zip(mesh['bone_indices'][i],mesh['weights'][i]):
                if weight: deformed += (bone_matrices[bone] @ point) * weight
            point = deformed
        else:
            point = world[renderer['transform']] @ point
        vertices.append(tuple((C @ point).to_3d()))
    faces, material_indices = [], []
    for index, submesh in enumerate(mesh['submeshes']):
        for face in submesh:
            faces.append(tuple(reversed(face)))
            material_indices.append(index)
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    uv = data.uv_layers.new(name='NativeUV')
    loop_vertices = np.empty(len(data.loops),dtype=np.int32)
    data.loops.foreach_get('vertex_index',loop_vertices)
    texcoords = np.asarray(mesh['uv'],dtype=np.float32)[:,:2]
    uv.data.foreach_set('uv',texcoords[loop_vertices].ravel())
    for key in renderer['materials']:
        data.materials.append(native_material(key))
    for polygon,index in zip(data.polygons,material_indices):
        polygon.material_index = index
        polygon.use_smooth = True
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    obj['donor'] = name
    obj['native_mesh'] = mesh['name']
    obj['native_mesh_key'] = renderer['mesh']
    names = {t['key']:t['name'] for t in prefab['transforms']}
    for bone in renderer['bones']:
        obj.vertex_groups.new(name=names[bone])
    if mesh['weights']:
        memberships = {}
        for i,(bones,weights) in enumerate(zip(mesh['bone_indices'],mesh['weights'])):
            for bone,weight in zip(bones,weights):
                if weight: memberships.setdefault((bone,round(weight,6)),[]).append(i)
        for (bone,weight),indices in memberships.items():
            obj.vertex_groups[bone].add(indices,weight,'REPLACE')
    bounds = [[min(v[i] for v in vertices),max(v[i] for v in vertices)] for i in range(3)]
    return obj, world, renderer, bounds

def look_at(obj, point):
    obj.rotation_euler = (Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()

def studio():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.world.color = (0.12,0.12,0.12)
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    scene.view_settings.exposure = -1.1
    for name,position,power,size in [('Key',(-110,-80,140),1500000,85),('Fill',(100,-20,95),650000,90),('Rim',(10,140,90),1600000,80)]:
        data = bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
        obj = bpy.data.objects.new(name,data); scene.collection.objects.link(obj); obj.location=position; look_at(obj,(0,0,15))
    bpy.ops.mesh.primitive_plane_add(size=2000, location=(0,0,0))
    floor = bpy.context.object; floor.name='StudioFloor'
    mat = bpy.data.materials.new('Studio sandstone'); mat.diffuse_color=(0.22,0.24,0.26,1); floor.data.materials.append(mat)
    camera_data = bpy.data.cameras.new('Camera'); camera = bpy.data.objects.new('Camera',camera_data); scene.collection.objects.link(camera)
    camera_data.type='ORTHO'; camera_data.ortho_scale=158; scene.camera=camera
    return scene,camera

def render(scene,camera,objects,path,position=(130,-175,135),target=(0,0,16),scale=158):
    wanted=set(objects)
    for obj in bpy.data.objects:
        if obj.type=='MESH' and obj.name!='StudioFloor': obj.hide_render=obj not in wanted
    camera.location=position; look_at(camera,target); camera.data.ortho_scale=scale
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)

donor_collection = bpy.data.collections.new('Native donor references')
bpy.context.scene.collection.children.link(donor_collection)
donors = {}
inventory = {}
for name in D['prefabs']:
    obj,world,renderer,bounds = import_donor(name,donor_collection)
    donors[name] = (obj,world,renderer)
    inventory[name] = {'bounds_blender':bounds,'vertices':len(obj.data.vertices),'triangles':len(obj.data.polygons)}
    print('DONOR',name,inventory[name],flush=True)
scene,camera = studio()
if '--references' in sys.argv:
    for name in ['coalition_assault','khaaneph_production','khaaneph_siege','khaaneph_baserunner']:
        scale = 90 if name=='khaaneph_baserunner' else 158
        render(scene,camera,[donors[name][0]],OUT/(name+'_reference.png'),scale=scale)
    (OUT/'reference-inventory.json').write_text(json.dumps(inventory,indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'donor-references.blend'))
    print('REFERENCE_RENDER_PASS',flush=True)
    sys.exit(0)

kit_collection = bpy.data.collections.new('Khaaneph Assault Cruiser')
scene.collection.children.link(kit_collection)
parts = []
part_manifest = []
geometry_cache = {}

def geometry_snapshot(obj):
    key=obj.name
    if key in geometry_cache: return geometry_cache[key]
    data=obj.data
    positions=np.empty(len(data.vertices)*3,dtype=np.float32)
    data.vertices.foreach_get('co',positions); positions=positions.reshape(-1,3)
    loops=np.empty(len(data.loops),dtype=np.int32); data.loops.foreach_get('vertex_index',loops)
    if len(loops)!=len(data.polygons)*3: raise ValueError('Donor is not triangulated')
    faces=loops.reshape(-1,3)
    texcoords=np.empty(len(data.loops)*2,dtype=np.float32)
    data.uv_layers.active.data.foreach_get('uv',texcoords); texcoords=texcoords.reshape(-1,3,2)
    material_indices=np.empty(len(data.polygons),dtype=np.int32)
    data.polygons.foreach_get('material_index',material_indices)
    memberships=[[(g.group,round(g.weight,6)) for g in vertex.groups] for vertex in data.vertices]
    dominant=[obj.vertex_groups[max(groups,key=lambda g:g[1])[0]].name if groups else '' for groups in memberships]
    result={'positions':positions,'faces':faces,'uv':texcoords,'materials':material_indices,
            'memberships':memberships,'dominant':dominant,'centers':positions[faces].mean(axis=1)}
    geometry_cache[key]=result
    return result

def dominant_bone(obj, vertex):
    if not vertex.groups: return ''
    return obj.vertex_groups[max(vertex.groups,key=lambda g:g.weight).group].name

def fragment(source, name, predicate, target_bone, matrix=None, keep_weights=False):
    """Copy selected donor faces, preserving UV seams/materials; bind fittings to the native rig."""
    matrix = matrix or Matrix.Identity(4)
    cached=geometry_snapshot(source)
    chosen=np.asarray([i for i,center in enumerate(cached['centers'])
                       if predicate(Vector(center),cached['dominant'][cached['faces'][i,0]],
                                    SimpleNamespace(material_index=int(cached['materials'][i])))],dtype=np.int32)
    if not len(chosen): raise ValueError('Empty donor part: '+name)
    faces=cached['faces'][chosen]
    old_indices=np.unique(faces)
    remap=np.empty(len(cached['positions']),dtype=np.int32); remap[old_indices]=np.arange(len(old_indices))
    positions=cached['positions'][old_indices]
    positions=(np.asarray(matrix,dtype=np.float32)[:3,:3]@positions.T).T+np.asarray(matrix.translation)
    data = bpy.data.meshes.new(name)
    data.from_pydata(positions.tolist(),[],remap[faces].tolist())
    data.update()
    uv = data.uv_layers.new(name='NativeUV')
    uv.data.foreach_set('uv',cached['uv'][chosen].ravel())
    for material in source.data.materials: data.materials.append(material)
    data.polygons.foreach_set('material_index',cached['materials'][chosen])
    data.polygons.foreach_set('use_smooth',np.ones(len(chosen),dtype=bool))
    obj=bpy.data.objects.new(name,data); kit_collection.objects.link(obj)
    if keep_weights:
        for group in source.vertex_groups: obj.vertex_groups.new(name=group.name)
        memberships={}
        for index,old in enumerate(old_indices):
            for group,weight in cached['memberships'][old]:
                memberships.setdefault((group,weight),[]).append(index)
        for (group,weight),indices in memberships.items(): obj.vertex_groups[group].add(indices,weight,'REPLACE')
    else:
        obj.vertex_groups.new(name=target_bone).add(list(range(len(old_indices))),1,'REPLACE')
    obj['donor']=source['donor']; obj['attachment_bone']=target_bone or 'original skin'; obj['purpose']=name
    parts.append(obj)
    part_manifest.append({'part':name,'donor':source['donor'],'vertices':len(data.vertices),
                          'triangles':len(data.polygons),'attachment':obj['attachment_bone'],
                          'donor_transform':[list(row) for row in matrix]})
    print('PART_BUILT',name,len(data.polygons),flush=True)
    return obj

def retain_assault_part(p,b,f):
    if 'ConningTower' in b or any(tag in b for tag in ['Susp_FL','Susp_FR']): return False
    if not b.endswith('Chassis'): return True
    # Retain only the weapons' local adapter plates and missile wells, rather than the smooth outer shell.
    return (abs(p.x)<9 and -7<p.y<12 and p.z>25) or (abs(p.x)<9 and 17<p.y<31 and p.z>23) or (abs(p.x)<14 and -45<p.y<-27 and p.z>22)

core=fragment(donors['coalition_assault'][0],'Assault cruiser original weapons and rear suspension',
              retain_assault_part,None,keep_weights=True)
remove_legacy_bridge_housing(core)
part_manifest[-1].update(vertices=len(core.data.vertices),triangles=len(core.data.polygons),
                        removed='Cut-off original conning-tower base and its small antenna remnants')
chassis='RIG_BattleCruiser_Chassis'
siege=donors['khaaneph_siege'][0]
production=donors['khaaneph_production'][0]
baserunner=donors['khaaneph_baserunner'][0]

def simple_material(name,color,metal=0.3,rough=0.7):
    material=bpy.data.materials.new(name); material.use_nodes=True
    shader=material.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value=(*color,1)
    shader.inputs['Metallic'].default_value=metal; shader.inputs['Roughness'].default_value=rough
    return material

steel=simple_material('Exposed dark frame',(0.055,0.063,0.064),0.6)

def clipped_part(source,name,group,bounds,target,matrix):
    obj=fragment(source,name,lambda p,b,f: b.endswith(group),target)
    # Exact plane cuts interpolate the native UVs and cap the exposed edges.
    bm=bmesh.new(); bm.from_mesh(obj.data)
    cap_material=len(obj.data.materials); obj.data.materials.append(steel)
    for axis,(minimum,maximum) in enumerate(bounds):
        for limit,sign in [(minimum,-1),(maximum,1)]:
            if limit is None: continue
            point=Vector((0,0,0)); point[axis]=limit
            normal=Vector((0,0,0)); normal[axis]=sign
            result=bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                                         plane_co=point,plane_no=normal,dist=0.00001,
                                         clear_outer=True,clear_inner=False)
            edges=[item for item in result['geom_cut'] if isinstance(item,bmesh.types.BMEdge) and item.is_boundary]
            if edges:
                for face in bmesh.ops.holes_fill(bm,edges=edges,sides=0)['faces']:
                    face.material_index=cap_material; face.smooth=False
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.00001)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=0.00001)
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.normal_update(); bm.to_mesh(obj.data); bm.free()
    obj.data.transform(matrix); obj.data.update()
    while len(obj.vertex_groups): obj.vertex_groups.remove(obj.vertex_groups[0])
    obj.vertex_groups.new(name=target).add(list(range(len(obj.data.vertices))),1,'REPLACE')
    part_manifest[-1].update(vertices=len(obj.data.vertices),triangles=len(obj.data.polygons),
                              donor_transform=[list(row) for row in matrix],cut_bounds=bounds)
    print('PART_CLIPPED',name,len(obj.data.polygons),flush=True)
    return obj

hull=fragment(siege,'Khaaneph rectangular armored hull',
              lambda p,b,f:b.endswith('Chassis'),chassis,Matrix.Translation((0,0,1)))
for sign,label in [(-1,'Port'),(1,'Starboard')]:
    donor_group='WheelBase_FLTop' if sign<0 else 'WheelBase_FRTop'
    target='RIG_BattleCruiser_Susp_FLT' if sign<0 else 'RIG_BattleCruiser_Susp_FRT'
    fragment(production,label+' wide armored jaw stabilizer',
             lambda p,b,f,g=donor_group:b.endswith(g),target,Matrix.Translation((0,0,5)))
bays=recessed_missile_bays(hull)
for entry in part_manifest:
    if entry['part']==hull.name:
        entry.update(vertices=len(hull.data.vertices),triangles=len(hull.data.polygons),missile_recesses=bays)

def beam(name,start,end,width,depth,material=steel):
    delta=Vector(end)-Vector(start)
    bpy.ops.mesh.primitive_cube_add(size=1,location=(Vector(start)+Vector(end))*0.5)
    obj=bpy.context.object; obj.name=name; obj.dimensions=(width,depth,delta.length)
    obj.rotation_euler=delta.to_track_quat('Z','Y').to_euler()
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    obj.data.materials.append(material)
    modifier=obj.modifiers.new('Rounded welded edges','BEVEL'); modifier.width=0.12; modifier.segments=2
    bpy.context.view_layer.objects.active=obj; bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.transform(obj.matrix_world); obj.matrix_world=Matrix.Identity(4)
    for collection in list(obj.users_collection): collection.objects.unlink(obj)
    kit_collection.objects.link(obj)
    obj.vertex_groups.new(name=chassis).add(list(range(len(obj.data.vertices))),1,'REPLACE')
    parts.append(obj); part_manifest.append({'part':name,'donor':'new framing','attachment':chassis,
                                             'vertices':len(obj.data.vertices),'triangles':len(obj.data.polygons)})
    return obj

# A reinforced plaque uses the Production Cruiser's actual worn Khaaneph marking.
outline=[(-8,-11), (8,-11), (9,-10), (9,10), (8,11), (-8,11), (-9,10), (-9,-10)]
deck_heights=[]
for x,y in outline:
    hit,point,normal,face=hull.ray_cast(Vector((x,y-17,100)),Vector((0,0,-1)))
    if not hit: raise ValueError('Deck plaque has no hull support')
    deck_heights.append(max(26.0,point.z))
vertices=[(x,y-17,z+0.18) for (x,y),z in zip(outline,deck_heights)]+[(x,y-17,z+1.1) for (x,y),z in zip(outline,deck_heights)]
faces=[tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
data=bpy.data.meshes.new('Khaaneph deck plaque'); data.from_pydata(vertices,[],faces); data.update()
data.materials.append(production.data.materials[0]); data.materials.append(steel)
uv=data.uv_layers.new(name='Khaaneph marking UV')
for polygon in data.polygons:
    polygon.material_index=0 if polygon.index==1 else 1
    for loop in polygon.loop_indices:
        v=data.vertices[data.loops[loop].vertex_index].co
        uv.data[loop].uv=(0.135+(v.x+9)/18*0.212,0.16+(v.y+28)/22*0.19)
obj=bpy.data.objects.new('Khaaneph marked deck armor',data); kit_collection.objects.link(obj)
obj.vertex_groups.new(name=chassis).add(list(range(16)),1,'REPLACE'); parts.append(obj)
part_manifest.append({'part':obj.name,'donor':'Production cruiser texture / new plate geometry',
                      'attachment':chassis,'vertices':16,'triangles':28,
                      'finish':'Thin marked insert fitted flush to the hull perimeter'})
flush_marked_panel(obj,hull)

# Bake a worn burnt-orange finish onto the retained hull UVs. Donor textures remain native.
original=core.data.materials[0]
paint=original.copy(); paint.name='Khaaneph burnt orange retained hull'
core.data.materials[0]=paint
nodes,links=paint.node_tree.nodes,paint.node_tree.links
shader=nodes.get('Principled BSDF'); output=nodes.get('Material Output')
texture=next(node for node in nodes if node.type=='TEX_IMAGE')
multiply=nodes.new('ShaderNodeMixRGB'); multiply.blend_type='MULTIPLY'
multiply.inputs[0].default_value=1; multiply.inputs[2].default_value=(0.67,0.22,0.077,1)
links.new(texture.outputs['Color'],multiply.inputs[1]); links.new(multiply.outputs[0],shader.inputs['Base Color'])
emission=nodes.new('ShaderNodeEmission'); links.new(multiply.outputs[0],emission.inputs['Color'])
links.new(emission.outputs[0],output.inputs['Surface'])
bake_image=bpy.data.images.new('K_AssaultCruiser_HullPaint',width=2048,height=2048,alpha=False)
bake_node=nodes.new('ShaderNodeTexImage'); bake_node.image=bake_image; nodes.active=bake_node
bake_obj=fragment(core,'Temporary hull material bake',lambda p,b,f:f.material_index==0,chassis)
parts.remove(bake_obj); part_manifest.pop()
bake_obj.data.materials.clear(); bake_obj.data.materials.append(paint)
bpy.ops.object.select_all(action='DESELECT'); bake_obj.select_set(True); bpy.context.view_layer.objects.active=bake_obj
scene.render.bake.margin=12
bpy.ops.object.bake(type='EMIT')
bake_image.filepath_raw=str(OUT/'K_AssaultCruiser_HullPaint.png'); bake_image.file_format='PNG'; bake_image.save()
bpy.data.objects.remove(bake_obj,do_unlink=True)
links.new(bake_node.outputs['Color'],shader.inputs['Base Color']); links.new(shader.outputs[0],output.inputs['Surface'])
nodes.remove(emission); nodes.remove(multiply); nodes.remove(texture)
shader.inputs['Roughness'].default_value=0.76
white_turret_caps(core,donors['coalition_assault'][0])

# Preserve the complete source transform hierarchy and all original skin groups.
prefab=D['prefabs']['coalition_assault']; world=donors['coalition_assault'][1]
armature_data=bpy.data.armatures.new('Native Assault Cruiser rig')
armature=bpy.data.objects.new('Assault Cruiser original rig',armature_data); kit_collection.objects.link(armature)
bpy.context.view_layer.objects.active=armature; armature.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bones={}
for transform in prefab['transforms']:
    bone=armature_data.edit_bones.new(transform['name']); matrix=C@world[transform['key']]@C.inverted()
    bone.matrix=matrix; bone.length=2.0
    if transform['parent']: bone.parent=bones[transform['parent']]
    bones[transform['key']]=bone
bpy.ops.object.mode_set(mode='OBJECT'); armature.show_in_front=True
for obj in parts:
    modifier=obj.modifiers.new('Native cruiser bone binding','ARMATURE'); modifier.object=armature
    obj.parent=armature
for donor in donor_collection.objects: donor.hide_render=True; donor.hide_set(True)

manifest={'name':'Khaaneph Assault Cruiser','stage':'textured kitbash prototype',
          'native_unit_key':'K_HonorGuard_MP','original_rig_transforms':len(prefab['transforms']),
          'coordinate_system':'Blender Z-up; native transform data retained in donor export',
          'parts':part_manifest,'bridge':'Complete native Khaaneph bridge retained with the rectangular hull',
          'revision':'Recessed missile rack, flush insert and cleaned bridge; decorative pod removed',
          'removed_decoration':['Baserunner service pod and its mounting bracket'],
          'note':'No original game archives or installed DLLs were changed.'}
(OUT/'kitbash-manifest.json').write_text(json.dumps(manifest,indent=2))
for image in bpy.data.images:
    if image.source=='FILE' or image==bake_image:
        image.pack()

# Save only the model and reference collection; studio objects are separate from the export selection.
bpy.ops.object.select_all(action='DESELECT')
armature.select_set(True)
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active=armature
bpy.ops.export_scene.gltf(filepath=str(OUT/'Khaaneph_Assault_Cruiser.glb'),export_format='GLB',
                          use_selection=True,export_animations=False,export_extras=True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'Khaaneph_Assault_Cruiser.fbx'),use_selection=True,
                         add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',
                         path_mode='COPY',embed_textures=True)

for label,position,target,scale in [('front',(-130,175,110),(0,0,16),166),('rear',(130,-175,135),(0,0,16),166),('top',(0,-6,240),(0,-6,16),185)]:
    render(scene,camera,parts,OUT/(label+'.png'),position=position,target=target,scale=scale)
render(scene,camera,parts,OUT/'jaw-detail.png',position=(-65,125,55),target=(0,41,15),scale=78)
render(scene,camera,parts,OUT/'bridge-detail.png',position=(-60,-90,60),target=(-19,-33,29),scale=42)
render(scene,camera,parts,OUT/'missile-recess-detail.png',position=(60,-100,65),target=(0,-37,25),scale=48)
render(scene,camera,[donors['coalition_assault'][0]],OUT/'original-comparison.png',position=(-130,175,110),scale=166)
# Leave the finished model visible when the editable file is opened.
for obj in parts: obj.hide_render=False; obj.hide_set(False)
for donor in donor_collection.objects: donor.hide_render=True; donor.hide_set(True)
scene.camera.location=(-130,175,110); look_at(scene.camera,(0,0,16))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
print('KITBASH_EXPORT_PASS',json.dumps({'parts':len(parts),'rig_bones':len(armature_data.bones),
                                      'triangles':sum(len(obj.data.polygons) for obj in parts)}),flush=True)
