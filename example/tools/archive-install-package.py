"""Verify and archive the portable installer, including all manifest payloads."""
import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / 'release/KhaanephAssaultCruiser'
manifest = json.loads((PACKAGE / 'manifest.json').read_text(encoding='utf-8-sig'))
version = manifest['version']
archive = ROOT / 'release' / ('KhaanephAssaultCruiser-' + version + '.zip')
documents = ['INSTALL.txt', 'README.md', 'runtime-validation.md', 'model-support.md',
             'install.ps1', 'uninstall.ps1', 'launch.ps1', 'manifest.json']
files = [(PACKAGE / name, name) for name in documents]
for entry in manifest['files']:
    path = PACKAGE / 'payload' / entry['path']
    assert path.resolve().is_relative_to((PACKAGE / 'payload').resolve())
    assert path.stat().st_size == entry['size'], 'Payload size mismatch: ' + entry['path']
    assert hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256'], 'Payload hash mismatch: ' + entry['path']
    files.append((path, 'payload/' + entry['path']))

for path, relative in files:
    assert path.is_file() and path.stat().st_size > 0, 'Missing/empty package file: ' + relative
    assert 'liveprobe' not in relative.lower(), 'Test driver must not be distributed'
    assert relative.count(':') == 0 and '..' not in Path(relative).parts

# Explicit manifest-driven inventory includes hidden .doorstop_version and
# excludes generated logs, user settings, test helpers and authoring files.
with ZipFile(archive, 'w', compression=ZIP_DEFLATED, compresslevel=9) as output:
    for path, relative in files:
        output.write(path, PACKAGE.name + '/' + relative)

expected = {PACKAGE.name + '/' + relative: path for path, relative in files}
with ZipFile(archive) as output:
    assert output.testzip() is None, 'ZIP CRC verification failed'
    assert set(output.namelist()) == set(expected), 'ZIP inventory mismatch'
    for name, path in expected.items():
        assert hashlib.sha256(output.read(name)).digest() == hashlib.sha256(path.read_bytes()).digest(), 'Archived hash mismatch: ' + name

report = {'version': version, 'archive': str(archive), 'bytes': archive.stat().st_size,
          'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
          'payload_file_count': len(manifest['files']), 'archive_file_count': len(files),
          'all_entry_hashes_verified': True, 'zip_crc_verified': True,
          'temporary_test_driver_included': False}
(ROOT / 'evidence/runtime/installation-package-integrity.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
archive.with_suffix('.zip.sha256').write_text(report['sha256'] + '  ' + archive.name + '\n', encoding='ascii')
print(json.dumps(report, indent=2))
