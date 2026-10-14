"""Local ASR to check original voice lines; no recordings are uploaded."""
import sys,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'work/voice-analysis/python_deps'))
from faster_whisper import WhisperModel
model=WhisperModel('tiny.en',device='cpu',compute_type='int8',
                   download_root=str(ROOT/'work/voice-analysis/models'))
report=[]
for path in sorted((ROOT/'assets/voices/candidates').glob('*.wav')):
    segments,info=model.transcribe(str(path),language='en',beam_size=5,vad_filter=False)
    segments=list(segments)
    entry={'file':path.name,'text':' '.join(segment.text.strip() for segment in segments),
           'avg_logprob':[segment.avg_logprob for segment in segments]}
    report.append(entry);print(json.dumps(entry),flush=True)
(ROOT/'evidence/audio/whisper-candidate-transcripts.json').write_text(json.dumps(report,indent=2))
