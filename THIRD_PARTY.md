# Third-party content and license scope

The [MIT license](LICENSE) covers this project's original code, original tools and original documentation. It does not cover the items below.

## Homeworld: Deserts of Kharak content

The game names, native identifiers/data, donor meshes, textures, animation/controller references, screenshots and kitbash-derived model retain their original third-party provenance and rights. The example is an unofficial modification requiring a supported installed game and Khaaneph DLC. It does not grant a license to reuse game content independently.

The runtime reuses installed native voices; it does not ship extracted voice banks or newly generated speech. Research JSON contains selected inspected metadata/data; proprietary decompiled game source and game assemblies are not published.

## Included loader distribution

The example ZIP and `example/tools/bepinex/BepInEx_win_x64_5.4.23.4.zip` contain the unmodified upstream **BepInEx 5.4.23.4** Windows x64 distribution, with Unity Doorstop 4.4.1 and its bundled dependencies. The upstream archive SHA-256 is `f881201b79da03e513bf97cdf39607ffa7f9e0d31a519b1aeeca8eb60f8309e7`.

| Component | Upstream / source | License notice |
|---|---|---|
| BepInEx 5.4.23.4 | [Tagged source](https://github.com/BepInEx/BepInEx/tree/v5.4.23.4), corresponding source archive in `example/third-party/source/` | [MIT at this tag](example/third-party/licenses/BepInEx-LICENSE.txt) |
| Unity Doorstop 4.4.1 | [Tagged source](https://github.com/NeighTools/UnityDoorstop/tree/v4.4.1), corresponding source archive in `example/third-party/source/` | [LGPL-2.1](example/third-party/licenses/UnityDoorstop-LICENSE.txt) |
| Mono.Cecil | [Source](https://github.com/jbevain/cecil), version bundled by BepInEx | [MIT](example/third-party/licenses/Mono.Cecil-LICENSE.txt) |
| MonoMod RuntimeDetour / Utils | [Source](https://github.com/MonoMod/MonoMod), version bundled by BepInEx | [MIT](example/third-party/licenses/MonoMod-LICENSE.txt) |
| Harmony / HarmonyX and interop | [Harmony](https://github.com/pardeike/Harmony), [HarmonyX](https://github.com/BepInEx/HarmonyX), versions bundled by BepInEx | [Harmony MIT](example/third-party/licenses/Harmony-LICENSE.txt), [HarmonyX MIT](example/third-party/licenses/HarmonyX-LICENSE.txt) |

The mod does not modify those upstream binaries. Its own preloader hook removes the generic BepInEx chainloader entry in memory because the helper did not work on this game's Unity runtime. Upstream source archives include their build configuration/instructions; bundled dependency references identify the versions used upstream. The mod source and loader may be rebuilt or replaced separately. Preserve upstream notices when redistributing.

See [example/third-party/README.md](example/third-party/README.md) for exact source/archive provenance and hashes.
