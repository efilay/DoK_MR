# Model integration (1.1.0)

The requested Khaaneph Assault Cruiser is installed through the DLL patch. Original game archives are unchanged. Editable Blender, GLB and FBX files are in `assets/kitbash/khaaneph-assault-cruiser`; the game payload is `assets/runtime-model`.

## Runtime contract

`ModelAssets.CreatePrefab` clones the original Assault Cruiser prefab, retaining all 57 transforms, its animator/controller, UnitView, colliders, wheel physics, selection definitions and weapon/effect attachment points. Its three original renderer components are replaced by five custom skinned parts at three LODs. The native view data is cloned independently before registration; the campaign source prefab and view stay original.

The KAC2 file contains native-space mesh buffers, UVs, tangents, submesh/material bindings, normalized four-bone weights and canonical native rest bind poses. Each mesh stays below Unity 5.2's 65,535-vertex limit. Runtime PNG textures and materials are owned by the patch, so their lifetime spans preview and match loads.

The native DLC shader itself does not survive DLC unload. Every custom material therefore uses the persistent base-game copy of the same UNIT shader, while retaining donor material properties and textures. Team-colour and visibility properties remain native; white turret-cap materials clear their team-paint mask.

## Geometry and animation

The Khaaneph Siege hull supplies the rectangular armor, cooling housings and angular bridge. Production Cruiser geometry supplies wide jaw plates and stabilizers. The original Assault Cruiser supplies turret rotations, cannon pitch/recoil, missile hatches, muzzle/effect transforms and aft suspension. The decorative Baserunner pod and bracket are removed. Missile racks retain their original positions within two recessed wells. A smaller deck insert sits on the former Siege launcher opening.

The original controller drives the weapon geometry and new front stabilizer skin bindings. Native clip sampling and a produced unit's controller over live game frames both pass. The native rendered model was visually inspected in a match and after preview reload. The original Assault carcass, destruction effects and gameplay footprint are retained; a separate custom wreck has not been authored.

## Adding more models

The same approach supports additional geometry without rebuilding original archives: author meshes against the required named transforms, export a runtime payload, clone the native view, and swap its model before entity registration and fleet-preview instantiation. A new unit with different hardpoints also needs coordinated turret, muzzle, effect, wheel and attachment definitions. GLB/FBX files are authoring exports; this game does not import them directly at runtime.

## Evidence

- `evidence/models/kitbash-verification.json`: five parts, 57 bones, valid source and GLB/FBX round trips; bounds difference below 0.01.
- `evidence/models/runtime-model-export.json`: three LODs and native mesh buffer/export contracts.
- `evidence/runtime/21-kitbash-liveprobe.log`: actual model, controller, shader lifecycle, gameplay and audio checks.
- `evidence/models/game-match-round-0.png` and `game-preview-round-3.png`: images rendered by native game cameras.
