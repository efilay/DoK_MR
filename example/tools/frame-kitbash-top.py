"""Render the full top silhouette without modifying the exported model."""
import bpy
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets/kitbash/khaaneph-assault-cruiser'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'Khaaneph_Assault_Cruiser.blend'))
scene = bpy.context.scene
camera = scene.camera
camera.location = (0, -6, 240)
camera.rotation_euler = (Vector((0, -6, 16)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.ortho_scale = 185
scene.render.filepath = str(OUT / 'top.png')
bpy.ops.render.render(write_still=True)
print('TOP_PREVIEW_PASS', flush=True)
