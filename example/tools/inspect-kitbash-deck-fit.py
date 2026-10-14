"""Measure the deck insert perimeter and the fitting's actual hull contacts."""
import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets/kitbash/khaaneph-assault-cruiser'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Khaaneph_Assault_Cruiser.blend'))
collection=bpy.data.collections['Khaaneph Assault Cruiser']
hull=collection.objects['Khaaneph rectangular armored hull']
pod=collection.objects['Baserunner armored service pod']
plaque=collection.objects['Khaaneph marked deck armor']

def height(obj,x,y,start=100,direction=-1):
    hit,point,normal,face=obj.ray_cast(Vector((x,y,start)),Vector((0,0,direction)))
    return point.z if hit else None

report={'insert':[], 'foot_contacts':[]}
for vertex in plaque.data.vertices:
    p=vertex.co
    report['insert'].append({'vertex':vertex.index,'co':list(p),'hull_z':height(hull,p.x,p.y),
                            'near_rim_z':height(hull,p.x*1.03,p.y)})
for x,y,z in json.loads(pod['cut_foot_samples_json']):
    base=height(pod,x,y,z-.05)
    surface=height(hull,x,y)
    report['foot_contacts'].append({'co':[x,y,z],'mount_base_z':base,'hull_z':surface,
                                    'clearance':base-surface if base is not None else None})
print('DECK_FIT_INSPECTION',json.dumps(report),flush=True)
for y in range(-28,-4,2):
    print('DECK_ROW',y,json.dumps([[x,round(height(hull,x,y),3) if height(hull,x,y) is not None else None]
                                  for x in [-14,-12,-10,-9,-8,-6,-4,-2,0,2,4,6,8,9,10,12,14]]),flush=True)
(ROOT/'evidence/models/deck-fit-inspection.json').write_text(json.dumps(report,indent=2))
