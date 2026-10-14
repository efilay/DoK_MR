import bpy,json
from pathlib import Path
out={}
for name in ['coalition_assault','khaaneph_production','khaaneph_siege','khaaneph_baserunner']:
 obj=bpy.data.objects[name]; groups={g.index:[] for g in obj.vertex_groups}
 for v in obj.data.vertices:
  if v.groups: groups[max(v.groups,key=lambda g:g.weight).group].append(v.co)
 out[name]=[]
 for group in obj.vertex_groups:
  verts=groups[group.index]
  if len(verts)>10:
   out[name].append({'bone':group.name,'vertices':len(verts),'bounds':[[round(min(v[i] for v in verts),2),round(max(v[i] for v in verts),2)] for i in range(3)]})
p=Path(__file__).resolve().parents[1]/'evidence/models/bone-parts.json'
p.parent.mkdir(parents=True,exist_ok=True)
p.write_text(json.dumps(out,indent=2));print(json.dumps(out))
