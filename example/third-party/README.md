# Upstream loader provenance

The binary loader archive is the unmodified BepInEx 5.4.23.4 Windows x64 release. The player ZIP contains those same upstream loader files plus this project's own mod DLLs/assets. `tools/prepare.py` verifies the upstream archive before extracting it.

- Binary release: <https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.4>
- BepInEx corresponding source: `source/BepInEx-v5.4.23.4.tar.gz`, from the matching upstream tag.
- Doorstop corresponding source: `source/UnityDoorstop-v4.4.1.tar.gz`, from the version recorded in `.doorstop_version`.
- License copies: `licenses/`.
- Fetch URLs and SHA-256 values: `upstream-files.json`.

The source archives are unchanged upstream sources, not this project's AI-generated work. Follow their included build instructions to rebuild the libraries. These dependencies are independent of the original mod's MIT license. Homeworld game assemblies remain external build inputs from the user's own installation.
