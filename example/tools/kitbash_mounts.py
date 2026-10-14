"""Hull-fitted service bracket and recessed missile bays for the cruiser model."""
import bpy
import bmesh
import json
from mathutils import Matrix, Vector
from mathutils.geometry import barycentric_transform


def surface_height(hull, x, y):
    hit, point, normal, face = hull.ray_cast(Vector((x, y, 100)), Vector((0, 0, -1)))
    if not hit:
        raise ValueError(f'No hull surface below fitting at {(x, y)}')
    return point.z


def fitted_service_mount(pod, hull):
    if pod.get('hull_fitted_mount'):
        return json.loads(pod['support_samples_json'])
    # The old fixed-height placement left the cut feet over the sloping side.
    # Seat the fitting inboard, then fit a saddle underneath every cut foot.
    offset = (-3.5, 3.0, -0.8)
    pod.data.transform(Matrix.Translation(offset))
    foot_z = min(vertex.co.z for vertex in pod.data.vertices)
    feet = [list(vertex.co) for vertex in pod.data.vertices if abs(vertex.co.z-foot_z)<0.00001][::8]
    xmin, xmax = 10.25, 18.2
    ymin, ymax = 21.72, 28.57
    corner = 0.45
    outline = [(xmin+corner,ymin),(xmax-corner,ymin),(xmax,ymin+corner),
               (xmax,ymax-corner),(xmax-corner,ymax),(xmin+corner,ymax),
               (xmin,ymax-corner),(xmin,ymin+corner)]
    seat_z = 24.22
    samples = []
    for x,y in outline:
        z = surface_height(hull,x,y)
        if z >= seat_z:
            raise ValueError('Service mount intersects a higher deck feature')
        hit,point,normal,face_index=hull.ray_cast(Vector((x,y,100)),Vector((0,0,-1)))
        polygon=hull.data.polygons[face_index]
        native_uv=hull.data.uv_layers.active.data
        source_points=[hull.data.vertices[i].co for i in polygon.vertices[:3]]
        source_uv=[Vector((*native_uv[i].uv,0)) for i in list(polygon.loop_indices)[:3]]
        mapped_uv=barycentric_transform(point,*source_points,*source_uv)
        samples.append({'x':x,'y':y,'hull_z':z,'base_z':z-0.16,'seat_z':seat_z,
                        'uv':list(mapped_uv[:2])})
    steel = bpy.data.materials['Exposed dark frame']
    if steel.name not in pod.data.materials:
        pod.data.materials.append(steel)
    material_index = list(pod.data.materials).index(steel)
    armor=hull.data.materials[0]
    if armor.name not in pod.data.materials:
        pod.data.materials.append(armor)
    armor_index=list(pod.data.materials).index(armor)
    bm = bmesh.new()
    bm.from_mesh(pod.data)
    uv = bm.loops.layers.uv.active
    deform = bm.verts.layers.deform.verify()
    binding = pod.vertex_groups['RIG_BattleCruiser_Chassis'].index
    bottom = [bm.verts.new((s['x'],s['y'],s['base_z'])) for s in samples]
    top = [bm.verts.new((x,y,seat_z)) for x,y in outline]
    for vertex in bottom+top:
        vertex[deform][binding] = 1.0
    faces = [bm.faces.new(list(reversed(bottom))),bm.faces.new(top)]
    for i in range(8):
        j = (i+1)%8
        faces.append(bm.faces.new((bottom[i],bottom[j],top[j],top[i])))
    support_uv={vertex:(sample['uv'][0],sample['uv'][1]-0.025)
                for vertex,sample in zip(bottom,samples)}
    support_uv.update({vertex:sample['uv'] for vertex,sample in zip(top,samples)})
    for face in faces:
        face.material_index = material_index if face==faces[0] else armor_index
        face.smooth = False
        for loop in face.loops:
            loop[uv].uv = support_uv[loop.vert]
    bmesh.ops.triangulate(bm, faces=faces)
    bm.normal_update()
    bm.to_mesh(pod.data)
    bm.free()
    pod.data.update()
    pod['hull_fitted_mount'] = True
    pod['support_samples_json'] = json.dumps(samples)
    pod['fitting_offset'] = list(offset)
    pod['fitting_seat_z'] = seat_z
    pod['cut_foot_samples_json'] = json.dumps(feet)
    return samples


def recessed_missile_bays(hull):
    if hull.get('recessed_missile_bays'):
        return json.loads(hull['missile_bays_json'])
    # Keep the original rack/hatch vertices and bone pivots. Cut only the new
    # Khaaneph deck which otherwise covers the native well and rack base.
    wells = [(-11.7,-0.55,-44.5,-30.2),(0.55,11.7,-44.5,-30.2)]
    floor_z = 23.15
    profiles = []
    for xmin,xmax,ymin,ymax in wells:
        corners = [(xmin,ymin),(xmax,ymin),(xmax,ymax),(xmin,ymax)]
        heights = [surface_height(hull,x,y) for x,y in corners]
        profiles.append({'bounds':[xmin,xmax,ymin,ymax], 'floor_z':floor_z,
                         'rim_z':heights,'depth':min(heights)-floor_z})
    bm = bmesh.new()
    bm.from_mesh(hull.data)
    for axis, values in [(0,[-11.7,-0.55,0.55,11.7]),(1,[-44.5,-30.2]),(2,[floor_z])]:
        for value in values:
            origin = Vector((0,0,0)); origin[axis] = value
            normal = Vector((0,0,0)); normal[axis] = 1
            bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                                  plane_co=origin,plane_no=normal,dist=0.000001,
                                  clear_inner=False,clear_outer=False)
    removed = []
    for face in bm.faces:
        center = face.calc_center_median()
        if center.z > floor_z+0.0001 and any(
            xmin+0.00001<center.x<xmax-0.00001 and ymin+0.00001<center.y<ymax-0.00001
            for xmin,xmax,ymin,ymax in wells
        ):
            removed.append(face)
    if not removed:
        raise ValueError('Missile well cut did not remove any deck')
    bmesh.ops.delete(bm,geom=removed,context='FACES')
    steel = bpy.data.materials['Exposed dark frame']
    if steel.name not in hull.data.materials:
        hull.data.materials.append(steel)
    material_index = list(hull.data.materials).index(steel)
    uv = bm.loops.layers.uv.active
    deform = bm.verts.layers.deform.verify()
    binding = hull.vertex_groups['RIG_BattleCruiser_Chassis'].index
    new_faces = []
    for profile in profiles:
        xmin,xmax,ymin,ymax = profile['bounds']
        corners = [(xmin,ymin),(xmax,ymin),(xmax,ymax),(xmin,ymax)]
        bottom = [bm.verts.new((x,y,floor_z)) for x,y in corners]
        top = [bm.verts.new((x,y,z)) for (x,y),z in zip(corners,profile['rim_z'])]
        for vertex in bottom+top:
            vertex[deform][binding] = 1.0
        new_faces.append(bm.faces.new(bottom))
        for i in range(4):
            j = (i+1)%4
            # Face into the recess, towards the machinery inside it.
            new_faces.append(bm.faces.new((bottom[i],top[i],top[j],bottom[j])))
    for face in new_faces:
        face.material_index = material_index
        face.smooth = False
        for loop in face.loops:
            loop[uv].uv = (loop.vert.co.x*0.05,loop.vert.co.y*0.05)
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.normal_update()
    bm.to_mesh(hull.data)
    bm.free()
    hull.data.update()
    hull['recessed_missile_bays'] = True
    hull['missile_bays_json'] = json.dumps(profiles)
    return profiles


def remove_legacy_bridge_housing(core):
    """Remove the cut-off original tower base accidentally copied with the rack."""
    if core.get('legacy_bridge_housing_removed'):
        return
    bm=bmesh.new();bm.from_mesh(core.data)
    deform=bm.verts.layers.deform.active
    groups={group.index:group.name for group in core.vertex_groups}
    removed=[]
    for face in bm.faces:
        center=face.calc_center_median()
        weights=dict(face.verts[0][deform].items())
        binding=groups[max(weights,key=weights.get)] if weights else ''
        if binding=='RIG_BattleCruiser_Chassis' and \
           -16.0<center.x<-8.0 and -33.1<center.y<-26.0 and center.z>21.8:
            removed.append(face)
    if not removed:
        raise ValueError('Legacy bridge housing not found in the original weapon mesh')
    bmesh.ops.delete(bm,geom=removed,context='FACES')
    loose=[vertex for vertex in bm.verts if not vertex.link_faces]
    if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bm.normal_update();bm.to_mesh(core.data);bm.free();core.data.update()
    core['legacy_bridge_housing_removed']=True
    core['legacy_bridge_faces_removed']=len(removed)


def flush_marked_panel(panel,hull):
    """Fit a thin marked insert directly to the native hull's perimeter."""
    if panel.get('flush_deck_insert_version')==3:
        return
    if len(panel.data.vertices)!=16:
        raise ValueError('Unexpected marked panel topology')
    # The donor cavity is narrower than the old decorative plate. Seat the
    # cover on its raised rim, rather than following the floor inside it.
    outline=[(-6.2,-19.5),(6.2,-19.5),(6.25,-19.45),(6.25,-5.55),
             (6.2,-5.5),(-6.2,-5.5),(-6.25,-5.55),(-6.25,-19.45)]
    for vertex in panel.data.vertices:
        x,y=outline[vertex.index%8]
        vertex.co.x=x;vertex.co.y=y
        z=surface_height(hull,x,y)
        if z<26:
            raise ValueError('Deck insert corner lies inside the opening instead of on its rim')
        vertex.co.z=z+0.045 if vertex.index>=8 else z-0.18
    panel.data.update()
    panel['flush_deck_insert']=True
    panel['flush_deck_insert_version']=3


def white_turret_caps(core,original):
    """Restore the native pale finish on both cylindrical turret caps."""
    material=bpy.data.materials.get('White Assault Cruiser turret caps')
    if material is None:
        material=original.data.materials[0].copy()
        material.name='White Assault Cruiser turret caps'
        material['kitbash_white_caps']=True
    if material.name not in core.data.materials:
        core.data.materials.append(material)
    index=list(core.data.materials).index(material)
    painted={'front':0,'rear':0}
    for face in core.data.polygons:
        vertices=[core.data.vertices[i] for i in face.vertices]
        center=sum((v.co for v in vertices),Vector())/len(vertices)
        first=vertices[0]
        group=core.vertex_groups[max(first.groups,key=lambda g:g.weight).group].name
        front=group=='RIG_BattleCruiser_FrontTurret_Rot' and center.z>28.8 and abs(center.x)<2.4
        rear=group=='RIG_BattleCruiser_RearTurret_Rot' and center.z>30.7 and abs(center.x)<2.4
        if front or rear:
            face.material_index=index
            painted['front' if front else 'rear']+=1
    if not all(painted.values()):
        raise ValueError('Both turret caps must receive the pale native material')
    core['white_turret_caps']=True
    core['white_turret_cap_faces_json']=json.dumps(painted)
    return painted
