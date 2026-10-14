import bpy,json
from mathutils import Vector
obj=bpy.data.objects['Assault cruiser chassis and original weapons']
for x,y in [(0,-17),(-8,-17),(8,-17),(0,-28),(0,-6)]:
 ok,point,normal,face=obj.ray_cast(Vector((x,y,100)),Vector((0,0,-1)))
 print('DECK',x,y,ok,list(point))
