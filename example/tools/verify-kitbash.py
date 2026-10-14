"""Blender: verify source skin bindings and both exported model round trips."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets/kitbash/khaaneph-assault-cruiser'
EVIDENCE=ROOT/'evidence/models'
EVIDENCE.mkdir(parents=True,exist_ok=True)

def check(condition,message):
    if not condition: raise AssertionError(message)

def bounds(objects):
    graph=bpy.context.evaluated_depsgraph_get()
    points=[]
    for obj in objects:
        evaluated=obj.evaluated_get(graph)
        mesh=evaluated.to_mesh()
        points.extend(evaluated.matrix_world@vertex.co for vertex in mesh.vertices)
        evaluated.to_mesh_clear()
    check(bool(points),'No exported geometry')
    check(all(math.isfinite(value) for point in points for value in point),'Non-finite vertex coordinates')
    return [[min(point[i] for point in points),max(point[i] for point in points)] for i in range(3)]

def clear():
    # Selection-based deletion skips the intentionally hidden donor references.
    for obj in list(bpy.data.objects): bpy.data.objects.remove(obj,do_unlink=True)

def ray_world(obj, origin, direction):
    inverse=obj.matrix_world.inverted()
    hit,point,normal,face=obj.ray_cast(inverse@Vector(origin),
                                      (inverse.to_3x3()@Vector(direction)).normalized())
    return obj.matrix_world@point if hit else None

def verify_mount_geometry(meshes,bays):
    hull=next(obj for obj in meshes if obj.name.startswith('Khaaneph rectangular armored hull'))
    check(not any(obj.name.startswith('Baserunner armored service pod') for obj in meshes),
          'Decorative service pod remains in the model')
    depths=[]
    for bay in bays:
        xmin,xmax,ymin,ymax=bay['bounds']
        center=((xmin+xmax)/2,(ymin+ymax)/2)
        floor=ray_world(hull,(*center,100),(0,0,-1))
        edge_x=xmin-0.3 if xmin<0 else xmax+0.3
        rim=ray_world(hull,(edge_x,center[1],100),(0,0,-1))
        check(floor is not None and rim is not None,'Missile well has no floor/rim')
        depth=rim.z-floor.z
        check(depth>1.2,'Missile rack remains covered by the deck')
        depths.append(depth)
    return {'missile_well_depths':depths}

bpy.ops.wm.open_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
collection=bpy.data.collections['Khaaneph Assault Cruiser']
parts=[obj for obj in collection.objects if obj.type=='MESH']
armature=next(obj for obj in collection.objects if obj.type=='ARMATURE')
source=json.loads((ROOT/'assets/kitbash/donors/donors.json').read_text())
native_names={entry['name'] for entry in source['prefabs']['coalition_assault']['transforms']}
check(native_names==set(armature.data.bones.keys()),'Original transform names changed')
check(len(native_names)==57,'Native source rig changed')
check(len(parts)==5,'Expected five authored parts')
for obj in parts:
    check(bool(obj.data.uv_layers),'Part has no texture UVs: '+obj.name)
    check(not obj.data.validate(verbose=True,clean_customdata=False),'Invalid mesh topology: '+obj.name)
    for vertex in obj.data.vertices:
        weights=[group.weight for group in vertex.groups]
        check(weights and abs(sum(weights)-1)<0.0001,'Unbound/non-normalized skin vertex in '+obj.name)
        check(all(obj.vertex_groups[group.group].name in native_names for group in vertex.groups),'Unknown rig binding')
for name in ['Khaaneph rectangular armored hull','Port wide armored jaw stabilizer','Starboard wide armored jaw stabilizer','Khaaneph marked deck armor']:
    check(name in collection.objects,'Required visual feature missing: '+name)
source_bounds=bounds(parts)
hull=collection.objects['Khaaneph rectangular armored hull']
check(hull.get('recessed_missile_bays'),'Missile recess correction missing')
check(collection.objects['Assault cruiser original weapons and rear suspension'].get('legacy_bridge_housing_removed'),
      'Cut-off original bridge housing remains')
panel=collection.objects['Khaaneph marked deck armor']
check(panel.get('flush_deck_insert'),'Raised deck slab remains')
for vertex in panel.data.vertices:
    ground=ray_world(hull,(vertex.co.x,vertex.co.y,100),(0,0,-1))
    check(ground is not None and -0.2<=vertex.co.z-ground.z<=0.06,'Marked insert floats above the hull')
cover=ray_world(panel,(0,-12,100),(0,0,-1))
check(cover is not None and 26<cover.z<28,'Former launcher opening remains uncovered')
bays=json.loads(hull['missile_bays_json'])
mount_geometry=verify_mount_geometry(parts,bays)
source_triangles=sum(len(obj.data.loop_triangles) or len(obj.data.polygons) for obj in parts)
image_count=sum(image.packed_file is not None for image in bpy.data.images if image.type=='IMAGE')
check(image_count>=6,'Textures not packed in the editable file')
report={'source':{'parts':len(parts),'bones':len(native_names),'packed_images':image_count,
                  'bounds':source_bounds,'mount_geometry':mount_geometry},'roundtrips':{}}

for format in ['glb','fbx']:
    clear()
    path=OUT/('Khaaneph_Assault_Cruiser.'+format)
    if format=='glb': bpy.ops.import_scene.gltf(filepath=str(path))
    else: bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
    rigs=[obj for obj in bpy.context.scene.objects if obj.type=='ARMATURE']
    control_shapes={bone.custom_shape for rig in rigs for bone in rig.pose.bones if bone.custom_shape}
    meshes=[obj for obj in bpy.context.scene.objects if obj.type=='MESH' and obj not in control_shapes]
    check(len(meshes)==5,format+' roundtrip mesh count changed')
    check(len(rigs)==1,format+' roundtrip lost the rig')
    check(native_names==set(rigs[0].data.bones.keys()),format+' roundtrip changed joint names')
    imported_bounds=bounds(meshes)
    delta=max(abs(before-after) for axis_a,axis_b in zip(source_bounds,imported_bounds) for before,after in zip(axis_a,axis_b))
    check(delta<0.01,format+' roundtrip changed model placement: '+str(delta))
    images={node.image for obj in meshes for material in obj.data.materials if material and material.node_tree
            for node in material.node_tree.nodes if node.type=='TEX_IMAGE' and node.image and node.image.size[0]>1}
    check(len(images)>=6,format+' roundtrip lacks material textures')
    report['roundtrips'][format]={'meshes':len(meshes),'bones':len(rigs[0].data.bones),
                                 'bounds':imported_bounds,'max_bounds_delta':delta,
                                 'texture_images_available':len(images),
                                 'mount_geometry':verify_mount_geometry(meshes,bays)}

(EVIDENCE/'kitbash-verification.json').write_text(json.dumps(report,indent=2))
print('KITBASH_VERIFICATION_PASS',json.dumps(report),flush=True)
