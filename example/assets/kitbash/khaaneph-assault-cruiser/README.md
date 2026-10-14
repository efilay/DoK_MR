# Khaaneph Assault Cruiser kitbash

Textured authoring model for the transferred Assault Cruiser. The requested visual direction is a rectangular Khaaneph hull, wide forward stabilizers with armored jaw plates, an angular command bridge, exposed machinery and worn faction textures.

Open `preview.html` for the rendered views and original-hull comparison. The editable model is `Khaaneph_Assault_Cruiser.blend`; `Khaaneph_Assault_Cruiser.glb` and `.fbx` include the model, material textures and original named rig. The Blender file also contains a hidden reference collection with the native donor models. Its images are packed.

## Parts

- Original Assault Cruiser: main autocannon turrets, missile hatches/wells, weapon adapters and aft suspension. The obsolete cut-off conning-tower base has been removed.
- Khaaneph Siege Cruiser: rectangular armored hull, stepped plating, cooling housings and its complete angular bridge.
- Khaaneph Production Cruiser: wide forward stabilizers and jaw armor.
- Khaaneph Baserunner: visual reference only; the decorative service pod and its bracket were removed.
- New work: donor-part cuts and caps, two recessed missile wells, a thin marked deck insert, retained-weapon paint treatment and bone bindings.

The original Assault Cruiser is Coalition-owned in the roster but visually shares the Gaalsien design language. The kitbash replaces its smooth outer shell with Khaaneph geometry, rather than merely repainting it.

The circled leading-end assemblies in the supplied fleet screenshot are the forward jaw/stabilizers. An early authoring pass treated the opposite end as the bow; the current model corrects that. `front.png` and `jaw-detail.png` show the leading end.

The decorative service pod and its mounting bracket have been removed. Both cylindrical turret caps use the original pale material. They had no gameplay function. The missile rack keeps its original positions and hatch bindings, while the surrounding Khaaneph deck has been cut back into two recessed wells; see `missile-recess-detail.png`. The large raised marking slab was replaced by a smaller thin insert fitted to the former launcher opening's rim. The rectangular Khaaneph bridge cabin remains beside the rack.

## Rig and game integration

All 57 original transform/bone names are retained. Main weapon parts retain their original skin bindings. Forward stabilizers bind to the existing front suspension bones. The native Khaaneph bridge remains part of the donor hull; the original conning-tower transform is available for attachment remapping during game integration. The authoring file has no copied Unity MonoBehaviour components or animation clips.

The DLL patch installs this model through its own KAC2 runtime geometry payload and PNG textures. It retains the native unit wrapper, animation controller, muzzle/effect transforms and physics. Five skinned parts have three game LODs. Native missile-door, cannon-recoil and steering animations were exercised in an actual match, and the textured model was rendered in the game after DLC unload/reload. FBX and GLB are editable interchange exports; native animation clips remain in the game and are not embedded in those files. The original Assault carcass and destruction effects are retained.

Armor is the visual rationale for greater durability. Model geometry does not change simulation health; the installed gameplay patch already sets the transferred unit to 3650 HP.

## Rebuild and evidence

Root project scripts: `tools/export-model-donors.py`, `tools/build-cruiser-kitbash.py`, `tools/kitbash_mounts.py`, `tools/verify-kitbash.py`. Donor extraction is read-only. Its geometry, UVs and material textures come from the original installed archives.

`kitbash-manifest.json` records each part's donor and binding. `evidence/models/kitbash-verification.json` in the project records source-model and GLB/FBX round-trip checks. Earlier renders and inspection scripts are retained as development evidence; the main model files and named preview images are the current revision.

Original game archives remain unchanged. The DLL patch and its owned runtime assets have been updated to install this kitbash.
