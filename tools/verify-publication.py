"""Check the publication snapshot without launching or modifying the game."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import tarfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def slug(text):
    return re.sub(r'[^\w -]', '', text.lower()).replace(' ', '-')


def main():
    tracked = subprocess.run(['git', 'ls-files', '-z'], cwd=ROOT,
                             capture_output=True, check=True).stdout
    files = [ROOT / name.decode('utf-8') for name in tracked.split(b'\0') if name]
    assert files, 'Stage the intended publication files before verification.'
    link_count = 0
    for path in files:
        assert path.stat().st_size < 100 * 1024 * 1024, 'GitHub file size limit: ' + path.name
        if path.suffix == '.json':
            json.loads(path.read_text(encoding='utf-8-sig'))
        if path.suffix != '.md':
            continue
        text = path.read_text(encoding='utf-8-sig')
        assert '\ufffd' not in text
        assert len(re.findall(r'^```', text, re.M)) % 2 == 0, 'Unbalanced fences: ' + path.name
        anchors = {slug(name) for name in re.findall(r'^#{1,6} (.+)$', text, re.M)}
        for href in re.findall(r'!?\[[^\]]*\]\(([^)]+)\)', text):
            href = href.strip('<>')
            if re.match(r'[a-z]+://|mailto:', href):
                continue
            file, _, anchor = href.partition('#')
            target = (path.parent / file).resolve() if file else path
            assert target.exists(), 'Broken file link in ' + path.name + ': ' + href
            if anchor:
                target_anchors = anchors if target == path else {
                    slug(name) for name in re.findall(r'^#{1,6} (.+)$', target.read_text(encoding='utf-8-sig'), re.M)}
                assert anchor in target_anchors, 'Broken anchor in ' + path.name + ': ' + href
            link_count += 1
    guide = (ROOT / 'docs/Deserts-of-Kharak-Modding-Guide.md').read_text(encoding='utf-8')
    for marker in ['## General overview', '## Part I — Proven concepts',
                   '## Part II — Hypotheses', '## Part III — Research']:
        assert marker in guide
    readme = (ROOT / 'README.md').read_text(encoding='utf-8')
    assert 'This project is AI-generated.' in readme
    mod = ROOT / 'example/release/KhaanephAssaultCruiser-1.1.0.zip'
    expected = (mod.with_suffix('.zip.sha256')).read_text().split()[0]
    assert digest(mod) == expected
    with zipfile.ZipFile(mod) as archive:
        assert archive.testzip() is None
        manifest = json.loads(archive.read('KhaanephAssaultCruiser/manifest.json').decode('utf-8-sig'))
        for entry in manifest['files']:
            content = archive.read('KhaanephAssaultCruiser/payload/' + entry['path'])
            assert len(content) == entry['size']
            assert hashlib.sha256(content).hexdigest() == entry['sha256']
        assert all('liveprobe' not in name.lower() for name in archive.namelist())
    upstream = ROOT / 'example/third-party'
    for record in json.loads((upstream / 'upstream-files.json').read_text(encoding='utf-8-sig')):
        assert digest(upstream / record['path']) == record['sha256'], 'Changed upstream file: ' + record['path']
    for path in (upstream / 'source').glob('*.tar.gz'):
        with tarfile.open(path) as archive:
            assert archive.getmembers()
    print(json.dumps({'files':len(files), 'local_markdown_links':link_count,
                      'guide_structure':True, 'ai_disclosure':True,
                      'json_and_fences':True, 'installer_sha256':expected,
                      'payload_hashes_verified':len(manifest['files']),
                      'upstream_files_verified':True}, indent=2))


if __name__ == '__main__':
    main()
