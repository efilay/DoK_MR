# Native API research index

> AI-generated research notes. These are original summaries of inspected behavior, not the game source code.

The private research workspace contained decompilations of installed game assemblies. This publication replaces those file links with the summaries below. The shipped game assemblies and complete decompiled game sources are not distributed here.

To reproduce inspection, use your installed supported build, open the relevant `Data/Managed/*.dll` with ILSpy (or another .NET browser), and search for the type/member named below. Start with `BBI.Unity.Game.dll`, `BBI.Game.Data.dll`, `BBI.Unity.Game.Data.dll` and related BBI assemblies. The [guide](Deserts-of-Kharak-Modding-Guide.md) explains assembly roles and evidence limits; the [member inventory](../example/evidence/code/bbi-unity-game-data-members.json) helps locate a declaration.

A type name or observed method is a research lead, not a stable public API. Verify signatures and control flow against the exact target build before adding hooks.

## AbilityAttributesData

Ability data and conditions connect research gates, targeting, passive activation, status effects and UI definitions. Inspect the unit-embedded data as well as the independently registered ability used by the command panel.

## AssetContainer

Named ScriptableObject-backed containers provide managed Data caches. The mod clones the Unity object and relevant BBI data, then rebuilds cached data with OnEnable when fields change.

## AudioAssetBundleLoader

LoadAssetBundle(StreamableAssetContainer) loads speech hierarchy content. TryLoadClip(string, out AudioClip) is intercepted by the mod for its approved Khaaneph substitutions. Dictionary identity alone does not establish that cached Unity clips are still alive.

## CommanderAttributesData

Commander configuration supplies faction, starting units/resources, population, starting/locked technology, buffs, abilities and AI archetypes. A faction menu entry alone does not create a usable commander loadout.

## CustomizationSettings

Contains the base-family faction selection and UnitSkinPackID array. The inspected initialization allocates two skin-pack slots; these are not the same identity as the gameplay faction ID or DLC ID.

## EntityTypeCollection

Add registers definitions by name and replaces duplicate names case-insensitively, issuing a warning. Registration also interacts with commander-specific lookups; inspect the actual registered object rather than only a prepared clone.

## FactionAttributesData

Faction identity, display/VO identity and technology tree references belong here. Khaaneph uses gameplay faction ID 4, distinct from its base-family selection index.

## GameLobbyView

Faction options are built from LevelManager commander attributes, eligible owned DLC faction descriptors and Random. A fifth independent faction needs coordinated options, settings, preview, spawning and networking work.

## HealthOverTimeProcessor

Processes timed health changes. Use the applied native status effect and simulation timing to establish the actual repair rate; similarly named unused assets are not behavioral evidence.

## ModifyHealthProcessor

Health changes and damage feed the simulation and ability conditions. The cruiser test damages a real produced unit, measures repair, and verifies that new damage restarts the delay.

## NotificationController

Native notifications and production/research responses use event data distinct from ordinary unit selection speech. The AA announcement needed its own voice-event treatment.

## ObjectFormatter

Tooltip formatting recognizes localization tokens matching the ID_[A-Z_0-9]+ convention. The former DOK_TRANSFER_* tokens were plain text; the mod uses ID_DOK_TRANSFER_* keys.

## PlayerCustomizationPreviewController

Preview rendering has a separate scene/content and DLC lifecycle. Prepare the current preview faction pack before instantiation and recheck after returning from a match.

## ResearchHelperShared

Native research availability considers prerequisite completion, membership of the faction technology tree and next-tier rules. All three must agree for the fabrication option to appear.

## ShipbreakersMain

InitializeEntityManager is the registration boundary patched before and after every exit. Before registration the mod restores old definitions and prepares current faction assets; afterward it registers additional research/ability clones.

## Sim

Simulation owns deterministic gameplay state and fixed-point/tick processing. Keep cosmetic variability out of simulation RNG and test multiplayer peers before asserting synchronization.

## SpawnAssigner

GeneratePlayerSelections chooses DLC commander attributes for a registered selected pack; otherwise it uses native commander attributes by faction index. This is a separate dependency from faction menu enumeration.

## TechTreeAttributesData

Technology branches hold ordered fabrication tiers and upgrades. The mod moves the new Khaaneph fabrication onto the Assault Ship branch and removes obsolete Honor Guard technology.

## UnitAssetContainer

Connects unit attributes to abilities, view/model, weapons and related definitions. The mod retains the K_HonorGuard_MP key but substitutes Assault behavior and presentation; this is replacement, not a proven new-unit registry.

## UnitTypeBuff

Unit-type selectors and named attribute modifiers determine buff applicability. Do not assume that similar displayed categories or copied labels target the transferred cruiser.

## UnitView

Unity-side presentation binds the native model hierarchy, animator, weapons, muzzle/effect transforms, wheel physics and events. Missing emission bones can disable effects; a mesh alone is insufficient.

## UnitViewAttributes

View attributes reference the model prefab and presentation configuration. Clone these independently to avoid changing the campaign donor or another faction through shared data.

## VOController

ExecuteVO consumes a VoiceOverEvent. The mod retains the Assault semantic hierarchy while mapping approved callbacks to real existing Khaaneph clips.

## VoiceOverEvent

Primary/secondary summaries, speech code and event fields determine the selected voice event. AA completion uses a separate aca research code; its SecondarySummary formerly retained Honor Guard wording.
