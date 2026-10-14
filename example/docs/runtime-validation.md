# Runtime validation - 1.1.0

Validated against installed Steam build 12339551, Unity 5.2.2f1 / Mono. The final native run was 2026-10-13T18:17:44.0882441Z to 2026-10-13T18:23:08.8801977Z on 13 October 2026. All probe assertions passed. Both temporary test DLLs were removed afterward.

| DLL | SHA256 |
|---|---|
| KhaanephCruiser.Runtime.dll | `cc13b1887bb4b3f30e552a1714f454655d9dd4e589a34e7628bb9baa78e6722b` |
| KhaanephCruiser.Patcher.dll | `c9b49760b1d9744f295068b0d694d277a41acd8912e3e36491908a1f00f72d6b` |

## Observed results

| Check | Evidence/result |
|---|---|
| Custom model | Five parts at three LODs, 57 bone transforms and rest bind poses; native Animator/controller and physics retained. |
| Native clip deformation | Missile doors moved vertices by 4.728 units; cannon recoil by 4.139 units; front stabilizer steering changed by 12.338 degrees. |
| Live controller | A produced cruiser's initialized controller opened doors, recoiled a cannon and steered the new jaw stabilizer over actual game frames. Immediate Play/Update on a new preview clone was an invalid test setup; multi-frame match testing resolved it. |
| Shader lifetime | All 60 logged material checks across previews and a match retained supported UNIT shaders. Previously the Khaaneph parts switched to Hidden/InternalErrorShader after DLC unload. The persistent base-game shader fixes that magenta regression. |
| Pixel inspection | Native match and reloaded preview images show textured hull/jaws, bridge, machinery and white caps. |
| Preview persistence | Initial preview and three menu returns instantiate the custom Assault Cruiser; no Honor Guard reversion. |
| Description | Native tooltip UILabel resolves the description, 48 HP/s and ten-second delay. |
| Khaaneph speech | Actual playback callbacks return Khaaneph recordings for selection, natural production/fabrication and AA completion. Both AA callbacks pass, including bank reload. |
| Research/production | Sandskimmer -> Assault Ship -> Assault Cruiser research completes through native APIs. Carrier produces a Khaaneph-owned cruiser with 3650 HP. |
| AA | AA_RIGHT is disabled before research; the same unit's passive and air weapon activate afterward. |
| Passive repair | No healing after nine seconds; 2000 to 2144 after thirteen seconds, representing three 48 HP ticks. New damage interrupts repair and restarts the delay. |
| Four factions | Actual Khaaneph, Coalition, Gaalsien and Soban trees register; Coalition/Soban Assault production/research is absent. |
| No-Khaaneph match | Subsequent Coalition/Gaalsien match has no stale DLC entity/cache; Coalition removal persists. |
| Campaign registration | SinglePlayer registration restores Coalition Assault production/research and original 2700 HP. No campaign mission played. |
| Static/export checks | Exact hook ABI, .NET 2 metadata, every registration return, duplicate/unsupported guards, clone isolation, native research methods and voice boundaries pass. GLB/FBX retain five parts, 57 bones and matching bounds/textures. |

## Method and limits

The temporary driver uses native offline preview, skirmish, research, production and audio APIs. Test-only resources, allied commanders and 4x simulation speed isolate the repair measurements. Both driver DLLs are excluded from release. Captures use native Unity cameras.

Audio evidence checks actual non-null AudioClips served to playback, not human listening. Nineteen original Khaaneph recordings were inventoried and checked by local ASR. Selection, movement/attack, attack-move, guard, hold/stop, production and research aliases use approved generic lines to avoid Honor Guard callouts. Rare combat-status and ability dialogue retains original Assault responses. No generated speech or cloud audio upload was used.

Real multiplayer peer synchronization, campaign missions and long-session stress are untested. Peers require identical patch files. Original Assault carcass/destruction and gameplay footprint definitions are retained.

## Evidence

- `evidence/runtime/kitbash-native-verification.json`
- `evidence/runtime/21-kitbash-liveprobe.log`, `21-kitbash-runtime.log`, `21-kitbash-game.log`, `21-kitbash-preloader.log`
- `evidence/models/game-match-round-0.png`, `game-preview-round-3.png`
- `evidence/models/kitbash-verification.json`, `runtime-model-export.json`
- `evidence/audio/approved-khaaneph-voices.json`, `whisper-candidate-transcripts.json`
- `evidence/runtime/original-assets-integrity.json`, `installation-integrity.json`

Earlier 1.0.1 logs and failed investigations remain separate. Their Coalition speech treatment and four-vertex API prototype are superseded by this custom model and Khaaneph speech treatment.
