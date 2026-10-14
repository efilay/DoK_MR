"""Prepare local build inputs from the two included, hash-verified archives."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
MOD_SHA256 = '6b49eca0dc1d173b9e82a995fe312a689ee8bdc4e19b25cbeeef3597c2980942'
LOADER_SHA256 = 'f881201b79da03e513bf97cdf39607ffa7f9e0d31a519b1aeeca8eb60f8309e7'


def unpack(archive, expected_hash, destination):
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != expected_hash:
        raise ValueError('Archive SHA-256 mismatch: ' + archive.name)
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as content:
        if content.testzip() is not None:
            raise ValueError('Archive CRC failure: ' + archive.name)
        for entry in content.infolist():
            target = (destination / entry.filename).resolve()
            if not target.is_relative_to(destination.resolve()):
                raise ValueError('Unsafe archive path')
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with content.open(entry) as source, target.open('wb') as output:
                shutil.copyfileobj(source, output)


def main():
    unpack(ROOT / 'tools/bepinex/BepInEx_win_x64_5.4.23.4.zip', LOADER_SHA256,
           ROOT / 'tools/bepinex/5.4.23.4')
    unpack(ROOT / 'release/KhaanephAssaultCruiser-1.1.0.zip', MOD_SHA256,
           ROOT / 'release')
    package = ROOT / 'release/KhaanephAssaultCruiser'
    manifest = json.loads((package / 'manifest.json').read_text(encoding='utf-8-sig'))
    for entry in manifest['files']:
        path = (package / 'payload' / entry['path']).resolve()
        if not path.is_relative_to((package / 'payload').resolve()):
            raise ValueError('Unsafe manifest path')
        if path.stat().st_size != entry['size'] or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
            raise ValueError('Payload mismatch: ' + entry['path'])
    shutil.copytree(package / 'payload/BepInEx/assets/KhaanephCruiser',
                    ROOT / 'assets/runtime-model', dirs_exist_ok=True)
    print('Prepared BepInEx 5.4.23.4 and 53 verified mod payload files.')
    print('Game assemblies remain external inputs from your own installation.')


if __name__ == '__main__':
    main()
