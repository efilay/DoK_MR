# Frozen resource investigation

Scope: original Deserts of Kharak Steam assets, static inspection only. User scope is skirmish and multiplayer. No game execution, deployment, or original asset writes by this worker.

Authoritative focused evidence: `cruiser-transfer-evidence.json`. Full decoded base evidence: `base-game-data.json`; full DLC evidence: `dlc-resources.json`. Matching original DLC type trees are in `original-dlc-data-typetrees.json`. Early `base-resources.json` and `base-shared0-priority.json` are retained failed/superseded parser experiments and must not supersede the authoritative data.

## Unit and HP identities

Base paths refer to `Data/sharedassets0.assets`. K paths refer to CAB-2e9fc07973a399cde98f3ed5a9cd833d inside `Bundles/dlc/dlc0002`; Soban paths refer to CAB-f71adec1234760ae2c1a1d1630dbade2 inside `Bundles/dlc/dlc0001`.

| Role | Asset / path ID | HP attributes / path ID | MaxHealth |
|---|---|---|---:|
| Coalition Assault Cruiser | G_Battlecruiser_MP / 41980 | G_Battlecruiser_UnitAttributesAsset_MP / 41981 | 2700 |
| Gaalsien Artillery Cruiser | G_ArtilleryCruiser_MP / 41901 | G_ArtilleryCruiser_UnitAttributesAsset_MP / 41902 | 2500 |
| Khaaneph Artillery Cruiser | K_ArtilleryCruiser_MP / 3449570410776292688 | K_ArtilleryCruiser_UnitAttributesAsset_MP / -6674681259473168354 | 3350 |
| Soban Assault Cruiser | C_Sob_AssaultCruiser_MP / 6552851152373777836 | C_Sob_AssaultCruiser_UnitAttributesAsset_MP / 610648012724154825 | 2700 |
| Existing Khaaneph Honor Guard | K_HonorGuard_MP / 4909191118389406259 | G_HonorGuard_UnitAttributesAsset_MP / 3023015576263442463 | See focused JSON |

Exact calculation: 3350 / 2500 = 67 / 50 = 1.34, a 34% increase. 2700 * 1.34 = **3618 HP**. Source Assault m_Class is 2621446 (0x280006); K Honor m_Class is 4718598 (0x480006). Class predicates are not automatically equivalent.

## Production and research

Coalition Assault production `Ability_G_ProduceBattlecruiser_MP` (41393) produces `G_Battlecruiser_MP` and requires `Tier_3_C_AssaultCruiser` (40979). Fabrication costs 600CU/350RU/80s and requires Tier_2_C_ArmouredAssault. Its current exclusive AA research is `Research_Buff_G_BattleCruiser_AntiAir` (41062), 500/340/65s. AA ability `Ability_G_BattleCruiser_EnableAir_MP` (41279) uses that research gate. Ordinary Coalition `C_Battlecruiser_MP` is a separate 6500HP unit and must remain.

Soban Assault production `Ability_C_Sob_ProduceAssaultCruiser_MP` (-8369390879156550897) produces `C_Sob_AssaultCruiser_MP` and requires `Tier_3_S_AssaultCruiser` (679961887590113627). Same fabrication cost/time. Its AA research is `Research_Buff_S_AssaultCruiser_AntiAir` (6718534513792231840), same AA cost/time.

K production `Ability_Produce_K_HonorGuard` (7896340885743911883) produces `K_HonorGuard_MP`, requires `Tier_3_G_HonorCruiser`, and is present on `K_Carrier_MP` (3873342828757755444).

K current tech asset is `Khaaneph` (5686968985193299283), linked by `Faction_DLC0002` (870699723775007089), FactionID **4**, VOName `khaaneph`. It has 17 branches. Zero-based fields:

- branch[0].tier[0]: Sandskimmers -> Tier_1_G_Sandskimmers.
- branch[2].tier[0]: Railguns -> Tier_2_G_Railgun_U1.
- branch[2].tier[1]: Honor Cruiser -> Tier_3_G_HonorCruiser.
- branch[3].tier[0]: Catamaran (Assault Ship) -> Tier_1_G_Catamaran.
- branch[14].upgrade[0]: Honor Cruiser Defenses -> Research_Buff_G_HonorCruiser_Defenses.

The chain predecessor already exists: Tier_1_G_Sandskimmers (41043), 300/0/35s, then Tier_1_G_Catamaran (41023), 600/100/65s. K current Honor fabrication `Tier_3_G_HonorCruiser` (41042), 500/350/85s, currently requires Railgun. Honor defenses `Research_Buff_G_HonorCruiser_Defenses` (41069), 600/370/75s, depends on Honor fabrication. Both are **shared base Gaalsien assets**, so mutation requires a K-specific clone or context guard.

All research currently present in Coalition, Soban, and K tech trees was resolved, including buff targets and weapon modifier names. No nonempty-prefix or unit-class research buff targets Assault or Honor directly. Preserve other factions' research targeting remaining units/producers. K Ship Armor1/2/3 targets Catamaran/Missile/Assault Ship names only; it does not include cruisers. Source `Research_Buff_G_BattleCruiser_Abilities` (41061) and Reload (41063) are not in current Coalition MP tech tree, so their existence is not evidence that players can research them.

K AirCraft Repair has an empty prefix selector and BuffSet_AirCraft_Repair_Rearm (41046): attribute IDs 524290 and 524289, Mode1, Value-50 each, names empty. It appears hangar-specific by asset identity; exact enum applicability remains for managed-code corroboration. All original selectors/values are included in focused JSON.

## Repair and preview

Actual K Honor passive is `Ability_G_HonorCruiserRegen` (5240162549164302344), linked from its container. Requires 10000ms without damage; m_AutoToggleOffConditions.m_TakeDamage=1; no research, zero cooldown/warmup, type100/self-target17/alignment4. Applied `Regen_HonorGuard_StatusEffect` (3114840752867608900) has modifierType8, ID Passive_Regeneration, Amount48, MSTickDuration1000, DamageType1. Lifetime2/Duration20/MaxStacks1 are raw values; parent must corroborate lifetime/ability enum semantics. Do not use merely similarly named ability: actual K Artillery uses Ability_G_ArtilleryCruiserRegen (3323373428028123167), 10s wait and31HP/s; unused Ability_K_ArtilleryCruiserRegen has20s wait.

Source Assault active repair is `Ability_G_BattleCruiserActiveRegen` (41371). Preserve source ArmorBonus (41278), Missile (41280), and AA (41279) when replacing active repair.

K pack `DLC0002_UnitFactionDLCPack` (-3706034352250290525) maps ReplacesEntity G_HonorGuard_MP -> K_HonorGuard_MP. Source assault view `G_Battlecruiser_UnitViewAttributesAsset` (41963) m_Attributes.ModelPrefab is FileID0/PathID9166 in sharedassets0. Parent's separate managed-code finding identifies pack NewEntities as the fleet-preview swap path. This resource worker did not observe preview in the running game.

## Checks and limitations

Final base extraction command exited0:2709 selected game-data records,2390 from sharedassets0 and319 from resources. Exactly2 unrelated decode failures remain: HUDMapControllerAsset and uScriptGlobalSettings. Required unit/view/research/ability/buff/weapon records decoded fully and their name/script headers and complete byte lengths matched. DLC extraction exited0,0 decode errors. Regenerated primitive-array trees required normalization before TypeTreeNode construction; original embedded DLC trees were used for the relevant matching classes.

Focused evidence derivation exited0. Freeze verifier compares all4 original SHA256/size identities, verifies HP/prerequisite placement/passive values/pack replacement/faction4, and records every worker-created file hash. Runtime semantics, networking, campaign regression and live UI remain root verification work.

All worker changes are new files under work/resources and evidence/resources. No original installation file changed. One dependency installation wrote a downloaded wheel to the standard pip cache outside the ownership boundary; this was reported immediately to parent. Subsequent package install used --no-cache-dir. No global config was edited. Source frozen after this report/manifest generation.
