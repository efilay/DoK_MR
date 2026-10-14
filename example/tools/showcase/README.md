# Optional offline recording driver

This AI-generated development tool produced the [silent example clip](../../media/README.md). It starts and exits its own temporary offline match, provides resources, advances the research chain, fabricates the cruiser, issues native movement/attack commands, and writes game-camera JPEGs plus a phase CSV. It does not capture the desktop or audio.

Build the normal mod first, then run `build.ps1` in this folder. It references the same local game assemblies and prepared BepInEx dependencies as the example mod. The generated files are ignored by Git:

- `KhaanephCruiser.Showcase.dll` goes temporarily in the game's `BepInEx/core/`.
- `KhaanephCruiser.Showcase.Patcher.dll` goes temporarily in `BepInEx/patchers/`.

Close the game before installing either file. Do not install the other `LiveProbe` driver at the same time. Start Steam first and launch the game through Steam: starting the executable with Steam absent produced a native Steam-ticket crash in the recording environment.

The driver automatically records when initialization finishes and quits after the scenario. Output is under the game's `BepInEx/KhaanephCruiser.Showcase/`. A failed assertion is written to `showcase.log`; do not present an incomplete run as a successful gameplay recording. The match uses a durable target and changed resources/simulation speed for filming, so it is not a balance test.

With the game closed, remove **only these two temporary DLLs**, after confirming they match the files you installed. They must never be added to the player release. Retain the normal cruiser patcher/runtime DLLs.

The showcase was encoded from 187 JPEG frames at a paced 12-frame-per-second input rate, with interpolation to 24 fps, captions and fades. H.264 CRF 27, `yuv420p`, MP4 fast-start, cleared source metadata and no audio produced the lightweight file. This code is specific to the investigated build and has the same native API/version assumptions as the mod.
