# Khaaneph Assault Cruiser DLL patch

Version **1.1.0**. Runtime mod for the installed Steam Deserts of Kharak build12339551, Unity5.2.2f1/Mono. The loader verifies the supported managed game assembly before inserting hooks in memory. Original game executables, assemblies and asset bundles stay unchanged on disk.

## Rules

- Skirmish, AI skirmish and multiplayer only. Campaign definitions are restored before campaign entity registration.
- Coalition and Soban lose Assault Cruiser production, fabrication and dedicated anti-air research. Their Battlecruisers and other units/upgrades remain.
- Khaaneph's Honor Guard slot becomes the Assault Cruiser, with the custom rectangular Khaaneph hull, armored forward jaws, angular bridge, original Assault weapons and fleet customization preview. The decorative pod is removed and both turret caps are white.
- Research: Sandskimmer fabrication -> Assault Ship fabrication -> Assault Cruiser fabrication. Assault fabrication costs 600 CU / 350 RU and takes 80 seconds. The anti-air upgrade costs 500 CU / 340 RU and takes 65 seconds. It automatically enables the cruiser's AA weapon when research finishes; it is a passive unlock.
- Armor boost and guided missiles stay. Active self-repair becomes Khaaneph passive repair:48HP/s after10s without damage, interrupted by damage.
- HP: Gaalsien artillery2500, Khaaneph artillery3350;34% difference. Assault base2700 yields3618; user requested rounding upward to **3650HP**.
- Selection, movement, attack, attack-move, guard, hold/stop, production, fabrication and AA completion use approved existing Khaaneph recordings. Fabrication says research is complete and production is unlocked; AA uses generic Khaaneph research-complete responses, avoiding Honor Guard announcements. Rare combat-status and ability dialogue retains the original Assault response. No generated voice or cloud audio upload is used.
- Five skinned parts at three LODs retain all 57 native transforms, turret/muzzle bindings and the original animation controller. The jaw stabilizers follow the front suspension bones. Native team-colour and visibility shader properties are retained; the turret caps remain white.
- Custom materials use the persistent base-game unit shader. Cloning a DLC material alone left its shader vulnerable to native DLC unload, causing the magenta hull; this lifecycle regression is now covered by match/preview tests.

The original internal `K_HonorGuard_MP` key is retained for the DLC replacement and production mappings. The visible unit is the Assault Cruiser. Khaaneph DLC ownership is still required.

The patch reapplies these changes to the current faction assets whenever the game registers a new match. Fleet customization uses a separate DLC load, so its current preview pack is prepared separately. Leaving a match and reloading the preview must not restore Honor Guard or lose fabrication research.

## Install and launch

Close the game, then run `install.ps1` from this package using PowerShell. It refuses unsupported game files and an existing loader it does not own. Run the game normally through Steam, or use `launch.ps1`; `-Windowed` opens1280x720 for testing.

All multiplayer participants must use this same patch/version and restart the game. An unpatched participant has different simulation data; live multiplayer synchronization requires a matching peer test.

The patch uses BepInEx's assembly preloader and direct static hooks. Its general MonoBehaviour/plugin chainloader is disabled because that helper crashes on this game's Unity5.2 runtime.

Runtime log: `BepInEx/KhaanephCruiser.log`. Loader log: `BepInEx/KhaanephCruiser.preloader.log`. If startup fails, inspect these and `Data/output_log.txt`.

Wait on the main menu until platform login finishes before opening Fleet Customization or Skirmish. In the observed unpatched test, EOS network retries delayed initialization by about90seconds. The menus exposed during that delay were empty and contained untranslated placeholder text. This also happens with the DLL loader disabled. A patched launch is ready when its runtime log reports `Registration 1: campaign/startup; original definitions restored` and the game log has no initialization exception.

To disable immediately with the game closed, change `enabled=true` to `enabled=false` in `doorstop_config.ini`. To uninstall, close the game and run `uninstall.ps1`. Uninstall checks ownership and hashes before removing only the installed files; it keeps game files and generated logs/configuration.

## Source and validation

Project source: `src/Runtime.cs`, `src/Patcher.cs`, `src/ModelAssets.cs`, `src/NativeVoices.cs`. Build with `tools/build.ps1`, verify with `tools/verify.ps1`, package with `tools/package.ps1`. Static checks use the original DLL and extracted original tech data, verify every registration exit and hook ABI, and exercise the game's own research membership/next-tier rules.

Custom descriptions use the game's required `ID_` localization-key prefix. The previous `DOK_TRANSFER_...` keys were treated as plain text by its tooltip formatter.

`tests/LiveProbe.cs` is a developer-only offline test driver. It uses the native preview, level loading, research and production APIs, then checks actual simulation health, AA weapon activation and repair timing. Its test-only resources and speed changes apply to its temporary matches. The two probe DLLs must be removed after testing and are excluded from the release payload. Results and limits are recorded in `runtime-validation.md` beside this README (`docs/runtime-validation.md` in the source project).

Runtime loader: [BepInEx5.4.23.4](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.4), including Unity Doorstop and its bundled Mono.Cecil/MonoMod/Harmony dependencies. Official downloaded archive SHA256 `f881201b79da03e513bf97cdf39607ffa7f9e0d31a519b1aeeca8eb60f8309e7` was verified against the release asset digest. Follow the upstream licenses for redistribution.

Model replacement and custom-geometry investigation: `model-support.md` beside this README (`docs/model-support.md` in the source project).

The runtime geometry and PNG textures are installed under `BepInEx/assets/KhaanephCruiser`. Blender, GLB and FBX authoring files are supplied separately in the model ZIP. Rebuild these with `tools/fit-kitbash-mounts.py`, verify with `tools/verify-kitbash.py`, and export the game payload with `tools/export-runtime-model.py`. Native destruction effects, carcass, selection and collision definitions are retained from the original Assault Cruiser.
