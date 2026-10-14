"""Export native-space, skinned mesh buffers and three LODs for Unity 5.2."""
import bpy,bmesh,json,sys,struct,shutil,math
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'assets/kitbash/khaaneph-assault-cruiser'
OUT=ROOT/'assets/runtime-model'
OUT.mkdir(parents=True,exist_ok=True)
(OUT/'textures').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'Khaaneph_Assault_Cruiser.blend'))
D=json.loads((ROOT/'assets/kitbash/donors/donors.json').read_text())
prefab=D['prefabs']['coalition_assault']
native_root=prefab['transforms'][0]
x,y,z,w=native_root['rotation']
native_matrix=Matrix.LocRotScale(Vector(native_root['position']),Quaternion((w,x,y,z)),Vector(native_root['scale']))
C=Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1)))
conversion=native_matrix.inverted()@C
normal_conversion=conversion.to_3x3().inverted().transposed()
bone_names=[item['name'] for item in prefab['transforms']]
bone_indices={name:i for i,name in enumerate(bone_names)}
bone_world={}
for transform in prefab['transforms']:
    x,y,z,w=transform['rotation']
    local=Matrix.LocRotScale(Vector(transform['position']),Quaternion((w,x,y,z)),Vector(transform['scale']))
    bone_world[transform['key']]=bone_world[transform['parent']]@local if transform['parent'] else local
bind_poses=[bone_world[t['key']].inverted()@native_matrix for t in prefab['transforms']]
parts=[obj for obj in bpy.data.collections['Khaaneph Assault Cruiser'].objects if obj.type=='MESH']
materials=[]
material_ids={}
records=[]

def material_id(mat):
    if mat.name in material_ids:return material_ids[mat.name]
    key=mat.get('native_material_key')
    native=D['materials'].get(key)
    shader=mat.node_tree.nodes.get('Principled BSDF')
    plain=native is None
    textures={}
    if native:
        for name,value in native['textures'].items():
            filename=D['textures'][value['key']]['file']
            shutil.copy2(ROOT/'assets/kitbash/donors'/filename,OUT/'textures'/filename)
            textures[name]={'file':'textures/'+filename,'scale':value['scale'],'offset':value['offset']}
        base=shader.inputs['Base Color']
        if base.is_linked:
            image=base.links[0].from_node.image
            if image.name.startswith('K_AssaultCruiser_HullPaint'):
                filename='K_AssaultCruiser_HullPaint.png'
                shutil.copy2(SOURCE/filename,OUT/'textures'/filename)
                textures['_MainTex']['file']='textures/'+filename
    record={'name':mat.name,'template':native['name'] if native else 'MAT_Unit_G_BattleCruiser',
            'plain':plain,'white_caps':bool(mat.get('kitbash_white_caps')),
            'color':list(shader.inputs['Base Color'].default_value),'textures':textures}
    index=len(materials);material_ids[mat.name]=index;materials.append(record)
    return index

for lod,ratio in enumerate([1.0,.48,.19]):
    for source in parts:
        copy=source.copy();copy.data=source.data.copy();bpy.context.scene.collection.objects.link(copy)
        bm=bmesh.new();bm.from_mesh(copy.data)
        bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(copy.data);bm.free();copy.data.update()
        for modifier in list(copy.modifiers):copy.modifiers.remove(modifier)
        if ratio<1:
            decimate=copy.modifiers.new('Game LOD','DECIMATE');decimate.ratio=ratio
            decimate.use_collapse_triangulate=True
        graph=bpy.context.evaluated_depsgraph_get()
        evaluated=copy.evaluated_get(graph)
        mesh=evaluated.to_mesh(preserve_all_data_layers=True,depsgraph=graph)
        mesh.calc_loop_triangles();mesh.calc_tangents(uvmap=mesh.uv_layers.active.name)
        vertex_records=[];lookup={};triangles={}
        world=source.matrix_world
        for triangle in mesh.loop_triangles:
            material=material_id(source.data.materials[triangle.material_index])
            indices=[]
            for loop_id in triangle.loops:
                loop=mesh.loops[loop_id];vertex=mesh.vertices[loop.vertex_index]
                uv=mesh.uv_layers.active.data[loop_id].uv
                normal=mesh.corner_normals[loop_id].vector
                tangent=loop.tangent
                key=(vertex.index,*uv,*normal,*tangent,loop.bitangent_sign)
                index=lookup.get(key)
                if index is None:
                    index=len(vertex_records);lookup[key]=index
                    position=conversion@(world@vertex.co)
                    normal=(normal_conversion@(world.to_3x3().inverted().transposed()@normal)).normalized()
                    tangent=(conversion.to_3x3()@(world.to_3x3()@tangent)).normalized()
                    weights=sorted([(bone_indices[source.vertex_groups[g.group].name],g.weight)
                                    for g in vertex.groups if g.weight>0],key=lambda pair:pair[1],reverse=True)[:4]
                    total=sum(weight for bone,weight in weights)
                    if total<=0:raise ValueError('Unbound runtime vertex')
                    weights=[(bone,weight/total) for bone,weight in weights]+[(0,0)]*(4-len(weights))
                    vertex_records.append((*position,*normal,*uv,*tangent,-loop.bitangent_sign,
                                           *(weight for bone,weight in weights),*(bone for bone,weight in weights)))
                indices.append(index)
            triangles.setdefault(material,[]).extend(reversed(indices))
        if len(vertex_records)>65535:raise ValueError('Mesh exceeds Unity 5.2 vertex index limit')
        record={'name':source.name,'lod':lod,'vertices':vertex_records,'triangles':triangles}
        records.append(record)
        print('RUNTIME_MESH',lod,source.name,len(vertex_records),sum(len(t) for t in triangles.values())//3,flush=True)
        evaluated.to_mesh_clear();bpy.data.objects.remove(copy,do_unlink=True)

with (OUT/'model.kac').open('wb') as stream:
    def i(value):stream.write(struct.pack('<i',value))
    def f(value):stream.write(struct.pack('<f',value))
    def s(value):
        value=value.encode('utf-8');i(len(value));stream.write(value)
    stream.write(b'KAC2');i(2)
    i(len(bone_names))
    for name in bone_names:s(name)
    for pose in bind_poses:
        for row in pose:
            for value in row:f(value)
    i(len(materials))
    for material in materials:
        s(material['name']);s(material['template'])
        i(int(material['plain']));i(int(material['white_caps']))
        for value in material['color']:f(value)
        i(len(material['textures']))
        for name,texture in material['textures'].items():
            s(name);s(texture['file'])
            for value in texture['scale']+texture['offset']:f(value)
    i(len(records))
    for record in records:
        s(record['name']);i(record['lod']);i(len(record['vertices']))
        for vertex in record['vertices']:
            stream.write(struct.pack('<16f4i',*vertex))
        i(len(record['triangles']))
        for material,triangles in record['triangles'].items():
            i(material);i(len(triangles));stream.write(struct.pack('<'+str(len(triangles))+'i',*triangles))
summary={'format':'KAC2','version':2,'bones':bone_names,'materials':materials,
         'meshes':[{'name':r['name'],'lod':r['lod'],'vertices':len(r['vertices']),
                    'triangles':sum(len(t) for t in r['triangles'].values())//3} for r in records]}
(OUT/'model.json').write_text(json.dumps(summary,indent=2))
(ROOT/'evidence/models/runtime-model-export.json').write_text(json.dumps(summary,indent=2))
print('RUNTIME_MODEL_EXPORT_PASS',len(records),'meshes;',len(bone_names),'bones',flush=True)
