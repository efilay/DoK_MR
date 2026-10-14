# Khaaneph assault cruiser DLL patch

Target: installed Homeworld: Deserts of Kharak, Steam build 12339551, Unity 5.2.2f1 / Mono.
Game: `C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak`.
Project: `<mod-project>`.

## Requested behavior

- Runtime DLL patch; do not modify the original game executable, assemblies, or asset archives on disk.
- Remove assault cruiser production and its dedicated research from Coalition and Soban.
- Replace Khaaneph honor guard cruiser with assault cruiser, including fleet customization preview.
- Research chain: sandskimmer fabrication -> assault ship fabrication -> assault cruiser fabrication.
- Apply all relevant cruiser upgrade research to the transferred cruiser.
- Replace active self repair with existing Khaaneph passive repair behavior.
- Assault cruiser HP multiplier = Khaaneph artillery cruiser HP / Gaalsien artillery cruiser HP, using the installed definitions.
- Skirmish and multiplayer only, confirmed by the user; campaign data restored before campaign entity registration.
- HP: 3350 / 2500 = 1.34; 2700 * 1.34 = 3618. User requested rounding upward to **3650 HP**.

## Investigation and edit ownership

Baseline: newly created project containing initial read-only REA evidence; no Git repository or pre-existing source changes. Original game files have not been modified.

Root owns `src/`, `tools/`, `tests/`, `docs/`, `dist/`, `release/`, and `evidence/code/`.
One resource-analysis worker may create files only under `work/resources/` and `evidence/resources/`; these paths are reserved to that worker until its handoff freezes.
No worker may write to the game installation, GC2 project, REA package, global configuration, or another worker's paths.

## Required evidence before implementation

| Stage | Evidence needed | Status |
|---|---|---|
| Data loading | ShipbreakersMain.InitializeEntityManager / ResetEntityManager, AssetContainer cache | Located |
| Unit transfer | G_Battlecruiser_MP -> K_HonorGuard_MP backend slot; remove C/S production targets | Located |
| Research | Khaaneph branches 2, 3, 14; cloned fabrication and AA; preserve Gaalsien shared originals | Located |
| Passive repair | Actual K honor regen: 48 HP/s after 10s; stop on damage | Located |
| HP | 2500, 3350, 2700; user final value 3650 | Verified static data |
| Fleet preview | SceneContent.SetFactionPack reads UnitFactionDLCPack.NewEntities and UnitView model | Located |
| Runtime injection | Doorstop/BepInEx 5 preloader; in-memory Cecil hook insertion | Built, installed, live startup and skirmish registration verified |
| Verification | Installed build, roster/research/ability invariants, preview, runtime log | Static and native offline match/preview tests passed; pixel, sound and real multiplayer peer checks remain untested |

Initial managed Evidence: `ev_948df56284129c464b940048ae8714006b4413d475a1026ef1c717005da21441` and `ev_592760e0de22faf04f0eb15616415cbe7d4c2ad7ec862da43f1764b3b14c1cf8` for `BBI.Game.Data.dll`.

Runtime observations and static evidence must remain distinguished. Game startup, visual preview, and actual upgrade behavior cannot be claimed from metadata inventories alone.

## Live startup investigation

- Initial BepInEx general chainloader crashed in its `ThreadingHelper.Initialize` / `GameObject.AddComponent<object>` before the mod ran. The preloader now removes only the built-in chainloader entry; UnityEngine is not rewritten.
- The next run reached the mod but logging threw because Unity's child AppDomain has a null ApplicationBase. Log paths now come from the runtime assembly location, and all diagnostics failures are contained.
- An unpatched run with `DOORSTOP_DISABLE=1` reproduced empty menus while EOS authentication was still pending. Its log shows TLS/network retries followed by successful login about90seconds after launch. The user then confirmed that baseline worked. Original managed assembly hashes remained unchanged.
- The subsequent patched run completed login, logged startup restoration, prepared the Khaaneph preview without an exception, and registered a skirmish with HP3650 and the changed research branches. The loaded production audit confirmed zero Coalition/Soban Assault Cruiser producers and exactly one Khaaneph carrier producer gated by the new fabrication research.
- Native screenshots could not be captured (`window capture timed out`). The user confirmed that the preview and skirmish setup worked, but Assault Cruiser fabrication was missing and the preview reverted after leaving a match. A temporary native test driver now exercises research, production, abilities and preview reloads without requiring user participation. Pixel appearance, audible speech and peer synchronization need separate evidence.

Evidence: `evidence/runtime/startup-01-crash.log`, `startup-02-broken-menus.log`, `startup-03-broken-menus.log`, `startup-04-loader-disabled.log`, and `startup-05-baseline-login-completed.log`. The early closed test runs did not all wait for EOS completion. The current successful patched run is preserved separately when it closes.

## Match and preview lifetime correction

`PlayerCustomizationPanel` owns a separate preview DLC manager. Unloading its content calls the native bundle unload path with `unloadAllLoadedObjects=true`. The match DLC manager subsequently loads fresh faction assets. A permanent name-to-first-instance cache therefore retained obsolete preview unit and tech assets.

Before every entity registration, the patch restores surviving originals, clears its asset index and per-registration tech/HUD caches, and indexes the actual registry, additional assets and active match DLC headers. The transfer cache is reusable only when both source and Khaaneph unit references match the current instances. Preview preparation similarly indexes the exact pack passed by the native preview controller. Destroyed Unity objects are skipped during restoration. A match without Khaaneph clears the transfer and ability references.

After registration, the patch checks object identity of the collection's real faction tech trees, verifies Khaaneph fabrication and AA membership, and enumerates actual producers. These checks happen after the game's registration code has populated the collection, so a pre-registration clone alone cannot satisfy them.

## Ability definitions and anti-air behavior

The native command panel looks up standalone ability entities for its icons and tooltips. Unit instances also carry embedded ability attributes. Register the Khaaneph production clone and the uniquely named Khaaneph AA/passive clones in both places. Keep the legacy Khaaneph production key for DLC/HUD mappings, while its title and icon come from Assault Cruiser production.

The original Assault Cruiser AA ability is a non-toggleable passive that applies the infinite `Enable_AntiAir_StatusEffect` to enable weapon `AA_RIGHT` once the upgrade is completed. It is not an active toggle. `RequiredResearchIsLocked` denotes a permanently locked research item; it does not indicate that an available upgrade is merely unfinished. The live test therefore checks unfinished/completed research, passive activation and the actual weapon's enabled state before and after research. Earlier test attempts that asserted a toggle or used this lock flag for unfinished research were test-driver errors, preserved in the investigation logs.

The first complete driver run passed simulation and model-instantiation checks but produced `SceneContent.OnSceneLoaded` exceptions after match exits. The driver called `LoadPreviewContent` after `LevelManager.UnloadLevel(ExitGame)` had already started loading it, creating duplicate scene coroutines. The corrected driver waits for that native load, checks its coroutine completion and requires a valid preview camera/render target. This is a test-driver correction; it adds no game hook or release behavior.

## Edit plan and write manifest

Root additions: `src/Patcher.cs`, `src/Runtime.cs`, `tools/build.ps1`, `tools/verify.ps1`, `tools/install.ps1`, `tools/uninstall.ps1`, `tests/VerifyHooks.cs`, `tests/VerifyData.cs`, `docs/README.md`; generated build/package/evidence files under root-owned paths.

| Runtime site | Change and protected invariant | Validation |
|---|---|---|
| BBI.Unity.Game.ShipbreakersMain.InitializeEntityManager | Before: restore original references, apply MP-only unit/producer/tech clones; after: register new K research. All clones are in memory. | Exact IL signatures and hook control flow; actual session log and data audit |
| BBI.Unity.Game.UI.PlayerCustomizationPanel.ContentManager.SceneContent.SetFactionPack | Prepare K assault model in the original K replacement slot before native preview code runs. | Original mapping/model evidence; actual preview check |
| BBI.Unity.Game.Localize.GameLocalizationManager.GetEntry(string) | Resolve only new mod description keys; use original localized Assault names/icons elsewhere. | Null fall-through and new-key return branch IL; live labels |
| UnitAssetContainer / AssetContainer.OnEnable | Clone assault attributes/HUD/AA and K passive; use source Assault voice code; rebuild cached weapon/view data. | HP, class, weapons, repair, HUD and AA gate assertions |
| Coalition/Soban producer containers | Remove only abilities whose ProduceUnit is an Assault key; blank corresponding HUD slots. | Enumerate every producer; campaign restore check |
| Coalition/Soban/K tech assets | Remove dedicated Assault fab/AA from C/S; move K fab after Assault Ship; replace Honor AA with Assault AA. | Clone isolation, tier order, research dependency and native membership rules |

The original K backend unit key is retained to preserve DLC replacement and production mappings. Its visible name, icons, model, weapons and mechanics become the Assault Cruiser. No faction entitlement or ownership code is changed.

Native results, authoritative completed driver logs and limits are recorded in `docs/runtime-validation.md`. Temporary driver DLLs are removed before delivering the release.

## Tooltip and speech follow-up

The production tooltip formats `{LocalizedLongDescriptionStringID}` through `BBI.Core.Localization.ObjectFormatter`. Its regex recognizes `ID_[A-Z_0-9]+`; the original mod keys did not match, so no localization lookup occurred. The three mod keys now begin with `ID_DOK_TRANSFER_`. A regression check first failed on the previous key and now verifies native recognition and translated text. The live driver opens the real production tooltip through `UISystem.ShowTooltip` and checks its UILabel.

Honor Guard barks came from the retained `kbt` unit audio identity, the historical production dialogue code and cruiser fabrication research speech. The user explicitly selected Coalition Assault Cruiser recordings. The transferred unit now uses `cas`, and its production/fabrication codes are explicitly remapped to `cas`. A streamed-bank-load prefix loads the original Coalition hierarchy alongside Khaaneph's bank. A playback prefix clones only relevant Khaaneph voice events and routes them to the Coalition bank; queued originals, unrelated commands and campaign playback are preserved. The native commander's `CommanderAttributes` is a field, not a property; this distinction is covered by the live playback check.

The AA upgrade had a separate leftover: the patch set its research speech code to Khaaneph's `def`, which announces Honor Guard defenses. It now retains the original Assault Cruiser upgrade's `aca`. `NotificationController.OnResearchEvent` puts this code in `VoiceOverEvent.SecondarySummary`, so routing checks that field for the fleet-operations research event. The Coalition hierarchy serves the original `vo_coalition_opsfleet_upg_opsacagengen_t1_12202` recording. A failing-before/passing-after regression covers AA routing and leaves unrelated Khaaneph `def` events intact. The live driver checks the actual clip callback after native AA research completion.

Historical 1.0.1 hook sites: `AudioAssetBundleLoader.LoadAssetBundle(StreamableAssetContainer)` and `VOController.ExecuteVO(VoiceOverEvent,bool)` in the original `BBI.Unity.Game.dll`. Static verification includes these hook signatures and voice-event isolation. Model feasibility and attachment requirements are recorded in `docs/model-support.md`; version 1.1.0 now includes the requested custom model.

## Kitbash and existing Khaaneph speech (1.1.0)

- `Runtime.PrepareTransfer` clones the source view asset and its UnitViewAttributes; `ModelAssets.CreatePrefab` retains the complete native hierarchy/controller/physics and replaces the three native skinned renderer components with five parts at three LODs. `tools/export-runtime-model.py` exports the native rest bind poses and skin buffers.
- All custom materials use the base-game UNIT shader object. The DLC shader copies were destroyed on preview unload even though their cloned materials remained alive; native renders showed `Hidden/InternalErrorShader` on the Khaaneph parts. The persistent shader is verified through initial preview, two matches and three preview reloads.
- `AudioAssetBundleLoader.TryLoadClip` has an exact-signature prefix returning approved Khaaneph AudioClips for selected Assault event aliases. The original event routing hierarchy remains intact. `NativeVoices.Candidates` isolates command, production, fabrication and AA paths and leaves unrelated units/research alone. Cached clips refresh when native bundle unload/reload replaces objects inside the same dictionary.
- The latest user preference is existing Khaaneph recordings. No OpenAI API key was configured; no voice audio was uploaded to cloud processing. Local ASR and the installed bank inventory support the approved recording list. Rare combat-status and ability dialogue retains native Assault responses.
- Live test helpers drive the native animator over multiple real frames; immediate Play/Update calls on a freshly instantiated preview clone did not update transforms. Direct clip sampling independently verifies the mesh skin weights. Temporary probe patching runs after the release hook so actual substituted clip returns are observed.
