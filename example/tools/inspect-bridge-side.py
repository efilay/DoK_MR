import bpy
obj=bpy.data.objects['khaaneph_production']
for side in ['low','high']:
 pts=[]
 for v in obj.data.vertices:
  if (v.co.z<26 if side=='low' else v.co.z>=26) and any(obj.vertex_groups[g.group].name.endswith('ControlTower') and g.weight>.5 for g in v.groups):pts.append(v.co)
 print('BRIDGE_PART',side,'vertices',len(pts),'bounds',[[min(v[i] for v in pts),max(v[i] for v in pts)] for i in range(3)])
