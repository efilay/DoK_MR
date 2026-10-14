import bpy
from pathlib import Path
from mathutils import Vector

out=Path(__file__).resolve().parents[1]/'assets/kitbash/khaaneph-assault-cruiser'
scene=bpy.context.scene
scene.cycles.samples=16
scene.view_settings.exposure=-1.1
camera=scene.camera
shots=[('construction-jaw','khaaneph_production',(65,-135,45),(0,-55,12),68),
       ('construction-bridge','khaaneph_production',(-60,-90,60),(-13,-24,29),38),
       ('baserunner-jaw','khaaneph_baserunner',(36,-60,27),(0,-16,8),32)]
for name,model,position,target,scale in shots:
    for obj in bpy.data.objects:
        if obj.type=='MESH' and obj.name!='StudioFloor': obj.hide_render=obj.name!=model
    camera.location=position
    camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale
    scene.render.filepath=str(out/(name+'.png'))
    bpy.ops.render.render(write_still=True)
print('DETAIL_RENDER_PASS',flush=True)
