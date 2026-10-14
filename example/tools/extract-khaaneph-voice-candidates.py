"""Extract original recordings for listening; does not modify speech bundles."""
import sys,json
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'work/resources/python_deps'))
import UnityPy
GAME=Path(r'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak')
OUT=ROOT/'assets/voices/candidates';OUT.mkdir(parents=True,exist_ok=True)
env=UnityPy.load(str(GAME/'Bundles/vo/khaaneph'))
inventory=json.loads((ROOT/'evidence/audio/khaaneph-voice-inventory.json').read_text())
selected={item['name'] for item in inventory if any(token in item['name'] for token in [
    '_cruiser_sel_cbtgengengen_t1_', '_cruiser_mov_cbtgengengen_t1_',
    '_cruiser_atk_cbtgengengen_t2_', '_cruiser_stp_cbtgengengen_t1_',
    '_opsfleet_rsh_opsgengengen_t1_', '_opsfleet_prc_opsgbtgengen_t1_',
    '_opsfleet_rsh_opsgbtgengen_t1_', '_opsfleet_rsh_opsdefgengen_t1_'])}
report=[]
for obj in env.objects:
    if obj.type.name!='AudioClip':continue
    data=obj.read()
    if data.m_Name not in selected:continue
    for name,content in data.samples.items():
        path=OUT/(data.m_Name+Path(name).suffix)
        path.write_bytes(content)
        report.append({'clip':data.m_Name,'file':str(path),'bytes':len(content)})
print(json.dumps(report,indent=2),flush=True)
