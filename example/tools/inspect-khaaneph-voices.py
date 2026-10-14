"""Inventory original Khaaneph speech for locally sourced cruiser callouts."""
import sys,json
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'work/resources/python_deps'))
import UnityPy
GAME=Path(r'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak')
env=UnityPy.load(str(GAME/'Bundles/vo/khaaneph'))
clips=[]
for obj in env.objects:
    if obj.type.name=='AudioClip':
        data=obj.read()
        clips.append({'name':data.m_Name,'path_id':obj.path_id,
                      'length':getattr(data,'m_Length',None)})
clips.sort(key=lambda item:item['name'])
(ROOT/'evidence/audio/khaaneph-voice-inventory.json').write_text(json.dumps(clips,indent=2))
focus=[item for item in clips if any(token in item['name'].lower()
       for token in ['_kbt','_kac','_cas','_aca','_def','_gengen','_genggen'])]
print(json.dumps({'total':len(clips),'focus':focus},indent=2),flush=True)
