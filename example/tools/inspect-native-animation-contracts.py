"""Read installed donor animator, suspension and material contracts."""
import sys,json
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'work/resources/python_deps'))
import UnityPy
GAME=Path(r'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak')
env=UnityPy.Environment(path=str(GAME/'Data'))
env.load_file(str(GAME/'Data/sharedassets0.assets'))
env.load_file(str(GAME/'Bundles/dlc/dlc0002'))
objects={(obj.assets_file.name,obj.path_id):obj for obj in env.objects}
sources=[('assault','sharedassets0.assets',9166),
         ('siege','CAB-2e9fc07973a399cde98f3ed5a9cd833d',-3419007820283949715),
         ('production','CAB-2e9fc07973a399cde98f3ed5a9cd833d',6462739249178112410),
         ('baserunner','CAB-2e9fc07973a399cde98f3ed5a9cd833d',-7324966947487926791)]

def ptr_key(ptr):
    if not ptr:return None
    obj=ptr.deref();return [obj.assets_file.name,obj.path_id]

def simple(value):
    if value is None or isinstance(value,(str,int,float,bool)):return value
    if isinstance(value,(bytes,bytearray)):return {'bytes':len(value)}
    if isinstance(value,(tuple,list)):return [simple(v) for v in value]
    if isinstance(value,dict):return {str(k):simple(v) for k,v in value.items()}
    if hasattr(value,'m_FileID'):return ptr_key(value)
    if hasattr(value,'__dict__'):return simple(vars(value))
    return str(type(value))

report={}
for label,file,pid in sources:
    data={'nodes':[],'animators':[],'animation_clips':[]}
    def visit(go):
        node={'name':go.m_Name,'components':[]}
        transform=None
        for pair in go.m_Component:
            ptr=pair.component if hasattr(pair,'component') else pair[1]
            reader=ptr.deref();kind=reader.type.name
            obj=reader.read(check_read=False);entry={'type':kind,'key':ptr_key(ptr)}
            if kind=='Transform':transform=obj
            elif kind=='Animator':
                tree=reader.read_typetree();entry['data']=simple(tree)
                controller=obj.m_Controller.read()
                controller_tree=obj.m_Controller.deref().read_typetree()
                data['animators'].append({'node':go.m_Name,'data':simple(tree),
                                         'controller_name':controller.m_Name,
                                         'controller':simple(controller_tree)})
                clips=list(getattr(controller,'m_AnimationClips',[]))
                for override in getattr(controller,'m_Clips',[]):
                    clips.append(override.m_OriginalClip)
                    if override.m_OverrideClip:clips.append(override.m_OverrideClip)
                for clip in clips:
                    clip_reader=clip.deref();clip_data=clip_reader.read()
                    data['animation_clips'].append({'name':clip_data.m_Name,'key':ptr_key(clip),
                                                   'data':simple(clip_reader.read_typetree())})
            elif kind=='MonoBehaviour':
                try:
                    script=obj.m_Script.read();entry['class']=script.m_ClassName
                    if 'Suspension' in script.m_ClassName:entry['data']=simple(reader.read_typetree(check_read=False))
                except Exception as exc:entry['error']=str(exc)
            elif kind in ['WheelCollider','LODGroup']:
                entry['data']=simple(reader.read_typetree())
            node['components'].append(entry)
        data['nodes'].append(node)
        for child in transform.m_Children:
            visit(child.read().m_GameObject.read())
    visit(objects[(file,pid)].read())
    report[label]=data
output=ROOT/'evidence/models/native-animation-contracts.json'
output.write_text(json.dumps(report,indent=2))
donors=json.loads((ROOT/'assets/kitbash/donors/donors.json').read_text())
materials={key:value for key,value in donors['materials'].items()
           if value['name'] in ['MAT_Unit_G_BattleCruiser','MAT_DLC0002_Unit_G_ArtilleryCruiser',
                                'MAT_DLC0002_Unit_G_SupportCruiser','MAT_Spongs','MAT_Gack']}
(ROOT/'evidence/models/native-material-contracts.json').write_text(json.dumps(materials,indent=2))
print(json.dumps({label:{'nodes':len(data['nodes']),
                        'controllers':[item['controller_name'] for item in data['animators']],
                        'clips':sorted({item['name'] for item in data['animation_clips']})}
                  for label,data in report.items()},indent=2),flush=True)
