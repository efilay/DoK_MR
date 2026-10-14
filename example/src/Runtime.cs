using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Core.DLC;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.DLC;
using UnityEngine;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyTitle("DoK Khaaneph Assault Cruiser runtime")]

namespace KhaanephCruiser
{
    // Called only by the preloader's exact managed hooks, on the game's main thread.
    public static class Runtime
    {
        public const int AssaultHP = 3650;
        public const string UnitKey = "K_HonorGuard_MP";
        public const string Fabrication = "Tier_3_K_AssaultCruiser";
        public const string AntiAir = "Research_Buff_K_AssaultCruiser_AntiAir";
        public const string AntiAirAbility = "Ability_K_AssaultCruiser_EnableAir_MP";
        public const string PassiveAbility = "Ability_K_AssaultCruiserPassiveRegen";
        public const string SpeechCode = "cas";
        public const string AntiAirSpeechCode = "aca";
        const string SourceUnit = "G_Battlecruiser_MP";
        const string ActiveRepair = "Ability_G_BattleCruiserActiveRegen";
        const string PassiveRepair = "Ability_G_HonorCruiserRegen";
        const string SourceAA = "Ability_G_BattleCruiser_EnableAir_MP";
        // ObjectFormatter only localizes values matching ID_[A-Z_0-9]+.
        const string LongDescription = "ID_DOK_TRANSFER_ASSAULT_DESCRIPTION";
        const string PassiveTitle = "ID_DOK_TRANSFER_PASSIVE_TITLE";
        const string PassiveDescription = "ID_DOK_TRANSFER_PASSIVE_DESCRIPTION";

        static readonly Dictionary<string, ScriptableObject> Assets = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
        static readonly Dictionary<ScriptableObject, Dictionary<string, object>> Originals = new Dictionary<ScriptableObject, Dictionary<string, object>>();
        static readonly Dictionary<UnitAssetContainer, ScriptableObject> FilteredHUDs = new Dictionary<UnitAssetContainer, ScriptableObject>();
        static readonly List<ScriptableObject> Clones = new List<ScriptableObject>();
        static readonly Dictionary<ScriptableObject, object> TechClones = new Dictionary<ScriptableObject, object>();
        static readonly HashSet<string> SpeechAudit = new HashSet<string>();
        static UnitAssetContainer Source, KUnit;
        static Dictionary<string, object> TransferredFields;
        static ScriptableObject KProduction, KAntiAirAbility, KPassiveAbility;
        static UnitFactionDLCPack KPack;
        static object NewFabrication, NewAntiAir;
        static bool MultiplayerSession;
        static int Registration;

        public static void BeforeEntityRegistration(object registry, object additional)
        {
            try
            {
                RestoreOriginals();
                // The preview owns a separate DLC manager and unloads its bundle before a match.
                // Rebuild this index from the actual registry and active match packs each time.
                Assets.Clear();
                FilteredHUDs.Clear();
                TechClones.Clear();
                SpeechAudit.Clear();
                KPack = null;
                AddAssets((IEnumerable)Reflect.Property(registry, "EntityTypeRegistry"));
                if (additional != null) AddAssets((IEnumerable)additional);
                DiscoverFactionPacks();
                MultiplayerSession = IsMultiplayerMode();
                if (MultiplayerSession)
                {
                    PrepareTransfer();
                    if (TransferredFields != null) ApplyTransfer();
                    PatchProducers();
                    PatchTechAssets();
                }
                Log("Registration " + (++Registration) + ": " + (MultiplayerSession ? "skirmish/multiplayer" : "campaign/startup; original definitions restored"));
            }
            catch (Exception ex)
            {
                RestoreOriginals();
                Log("ERROR before registration: " + ex);
                throw new InvalidOperationException("Khaaneph cruiser patch could not safely prepare the game data. See BepInEx/KhaanephCruiser.log.", ex);
            }
        }

        public static void AfterEntityRegistration()
        {
            if (!MultiplayerSession) return;
            try
            {
                EntityTypeCollection collection = (EntityTypeCollection)Reflect.StaticField(typeof(UnitFactionDLCPack).Assembly.GetType("BBI.Unity.Game.ShipbreakersMain", true), "sEntityTypes");
                if (NewFabrication != null && KUnit != null)
                {
                    AddResearch(collection, Fabrication, NewFabrication);
                    AddResearch(collection, AntiAir, NewAntiAir);
                    AddAbility(collection, KProduction);
                    AddAbility(collection, KAntiAirAbility);
                    AddAbility(collection, KPassiveAbility);
                    object data = collection.GetEntityType(UnitKey).GetAll<object>()[0];
                    Require((int)Reflect.Get(data, "m_MaxHealth") == AssaultHP, "Registered assault HP");
                    Log("Khaaneph Assault Cruiser registered: HP=" + AssaultHP + "; fabrication=" + Fabrication + "; passive=48 HP/s after 10s; AA=" + AntiAir);
                }
                AuditRegisteredTech(collection);
                AuditProduction(collection);
            }
            catch (Exception ex)
            {
                Log("ERROR after registration: " + ex);
                throw;
            }
        }

        public static void BeforeFactionPreview(object factionPack)
        {
            UnitFactionDLCPack pack = factionPack as UnitFactionDLCPack;
            if (pack == null || pack.name != "DLC0002_UnitFactionDLCPack") return;
            try
            {
                AddPack(pack);
                PrepareTransfer();
                Require(TransferredFields != null, "Assault source available for Khaaneph preview");
                ApplyTransfer();
                Log("Khaaneph preview: assault model replaces Honor Guard in the original DLC slot.");
            }
            catch (Exception ex)
            {
                Log("ERROR preview: " + ex);
                throw;
            }
        }

        public static object GetLocalization(string key)
        {
            string text;
            if (key == LongDescription)
                text = "Heavy assault cruiser with autocannons, guided missiles, armor boost and an anti-aircraft upgrade. Khaaneph passive self-repair restores 48 HP per second after 10 seconds without taking damage.";
            else if (key == PassiveTitle) text = "Passive Self-Repair";
            else if (key == PassiveDescription) text = "Automatically restores 48 HP per second after 10 seconds without taking damage. Taking damage interrupts repair.";
            else return null;
            Type entry = typeof(UnitFactionDLCPack).Assembly.GetType("BBI.Unity.Game.Localize.LocalizedEntry", true);
            return Activator.CreateInstance(entry, new object[] { (uint)0, key, text });
        }

        public static void BeforeSpeechBankLoad(object loader, object container)
        {
            if (!MultiplayerSession || container == null ||
                !string.Equals((string)Reflect.Property(container, "AssetBundleName"), "vo/khaaneph", StringComparison.OrdinalIgnoreCase)) return;
            Type main = typeof(UnitFactionDLCPack).Assembly.GetType("BBI.Unity.Game.ShipbreakersMain", true);
            object levels = Reflect.Get(Reflect.StaticField(main, "sInstance"), "mLevelManager");
            foreach (CommanderAttributes commander in (CommanderAttributes[])Reflect.Property(levels, "CommanderAttributes"))
            {
                if (commander.Faction.VOName != "coalition") continue;
                object extra = Reflect.Get(commander.Faction, "m_VOCoreSpeechHierarchy");
                loader.GetType().GetMethod("LoadAssetBundle", new Type[] { extra.GetType() }).Invoke(loader, new object[] { extra });
                Log("Assault speech: loading native Coalition recordings alongside Khaaneph speech.");
                return;
            }
            throw new InvalidOperationException("Coalition Assault Cruiser speech hierarchy unavailable.");
        }

        public static object PrepareVoiceEvent(object controller, object ev)
        {
            if (!MultiplayerSession || !DataPlan.IsAssaultSpeech(ev)) return ev;
            string faction = (string)Reflect.Get(ev, "Faction");
            if (string.IsNullOrEmpty(faction))
            {
                object manager = Reflect.Get(controller, "mCommanderManager");
                if (manager == null) return ev;
                object commander = manager.GetType().GetMethod("GetCommanderFromID").Invoke(manager, new object[] { Reflect.Property(manager, "LocalCommanderID") });
                if (commander == null) return ev;
                faction = ((CommanderAttributes)Reflect.Get(commander, "CommanderAttributes")).Faction.VOName;
            }
            object routed = DataPlan.RouteAssaultSpeech(ev, faction);
            if (!object.ReferenceEquals(routed, ev))
            {
                  string key = (string)Reflect.Get(ev, "Action") + "/" + (string)Reflect.Get(ev, "PrimarySummary") + "/" + (string)Reflect.Get(ev, "SecondarySummary");
                if (SpeechAudit.Add(key)) Log("Assault speech route: " + key + "; native Coalition Assault Cruiser recordings.");
            }
            return routed;
        }

        public static object GetAssaultVoiceClip(object loader,string reference)
        {
            return MultiplayerSession ? NativeVoices.GetClip(loader,reference) : null;
        }

        static bool IsMultiplayerMode()
        {
            Type main = typeof(UnitFactionDLCPack).Assembly.GetType("BBI.Unity.Game.ShipbreakersMain", true);
            object instance = Reflect.StaticField(main, "sInstance");
            if (instance == null) return false;
            string mode = Reflect.Get(instance, "mCurrentGameMode").ToString();
            return mode == "Skirmish" || mode == "AISkirmish" || mode == "Multiplayer";
        }

        static void AddAssets(IEnumerable sequence)
        {
            if (sequence == null) return;
            foreach (object item in sequence)
            {
                ScriptableObject asset = item as ScriptableObject;
                if (asset != null) IndexAsset(asset);
            }
        }

        static void IndexAsset(ScriptableObject asset)
        {
            ScriptableObject previous;
            string name = asset.name;
            if (Assets.TryGetValue(name, out previous) && !object.ReferenceEquals(previous, asset) &&
                (name == UnitKey || name == "Khaaneph"))
                Log("Rebinding " + name + " to current asset instance " + asset.GetInstanceID());
            Assets[name] = asset;
        }

        static void DiscoverFactionPacks()
        {
            Type main = typeof(UnitFactionDLCPack).Assembly.GetType("BBI.Unity.Game.ShipbreakersMain", true);
            DLCManager manager = (DLCManager)Reflect.StaticField(main, "sDLCManager");
            if (manager == null) return;
            foreach (DLCAssetBundleBase item in manager.GetAllLoadedHeadersOfType(DLCType.Faction))
            {
                UnitFactionDLCPack pack = item as UnitFactionDLCPack;
                if (pack != null) AddPack(pack);
            }
        }

        static void AddPack(UnitFactionDLCPack pack)
        {
            if (pack.name == "DLC0002_UnitFactionDLCPack") KPack = pack;
            foreach (UnitFactionDLCPack.ReplacementMapping mapping in pack.NewEntities)
            {
                ScriptableObject asset = mapping.NewEntityType;
                if (asset != null) IndexAsset(asset);
            }
            object commander = Reflect.Get(pack, "m_FactionAttributes");
            object factionAsset = Reflect.Get(commander, "m_FactionAttributes");
            object faction = Reflect.Property(factionAsset, "Data");
            ScriptableObject tech = (ScriptableObject)Reflect.Get(faction, "m_TechTreeAttributes");
            if (tech != null) IndexAsset(tech);
        }

        static ScriptableObject Asset(string name)
        {
            ScriptableObject value;
            if (Assets.TryGetValue(name, out value)) return value;
            // Production abilities can be contained by a producer without standalone registration.
            foreach (ScriptableObject candidate in new List<ScriptableObject>(Assets.Values))
            {
                UnitAssetContainer unit = candidate as UnitAssetContainer;
                if (unit == null) continue;
                value = FindAbility(unit, name);
                if (value != null) { Assets.Add(name, value); return value; }
            }
            throw new InvalidOperationException("Required asset missing: " + name);
        }

        static void PrepareTransfer()
        {
            if (!Assets.ContainsKey(UnitKey))
            {
                KUnit = null;
                TransferredFields = null;
                KProduction = null;
                KAntiAirAbility = KPassiveAbility = null;
                NewFabrication = NewAntiAir = null;
                return;
            }
            UnitAssetContainer currentSource = (UnitAssetContainer)Asset(SourceUnit);
            UnitAssetContainer currentUnit = (UnitAssetContainer)Asset(UnitKey);
            if (TransferredFields != null && object.ReferenceEquals(Source, currentSource) && object.ReferenceEquals(KUnit, currentUnit)) return;
            Source = currentSource;
            KUnit = currentUnit;
            TransferredFields = null;
            KProduction = null;
            KAntiAirAbility = KPassiveAbility = null;
            NewFabrication = NewAntiAir = null;
            Log("Preparing current Khaaneph unit instance " + KUnit.GetInstanceID());
            object sourceUnitData = Reflect.Get(Reflect.Get(Source, "m_Attributes"), "m_Attributes");
            Require((int)Reflect.Get(sourceUnitData, "m_MaxHealth") == 2700, "Supported source HP 2700");
            Require(Convert.ToInt32(Reflect.Get(sourceUnitData, "m_Class")) == 2621446, "Supported assault unit class");
            ScriptableObject originalPassive = FindAbility(KUnit, PassiveRepair);
            Require(originalPassive != null, "Actual Khaaneph passive repair");
            object passiveData = Reflect.Get(originalPassive, "m_Attributes");
            Require((int)Reflect.Get(Reflect.Get(passiveData, "m_ActivationDependencies"), "m_TakeNoDamageForMS") == 10000, "Passive wait 10s");

            TransferredFields = new Dictionary<string, object>();
            foreach (FieldInfo field in Reflect.Fields(typeof(UnitAssetContainer)))
            {
                if (field.Name == "mDataCache") continue;
                TransferredFields.Add(field.Name, field.GetValue(Source));
            }
            // The source assault audio uses "cas". Khaaneph's "kbt" recordings name Honor Guard.
            Require(((UnitAudioAttributes)Reflect.Property(Reflect.Get(Source, "m_Audio"), "Data")).AudioHookups.UnitCode == SpeechCode, "Source assault speech code");

            ScriptableObject attributes = CloneAsset((ScriptableObject)Reflect.Get(Source, "m_Attributes"), "K_AssaultCruiser_UnitAttributes_MP");
            Reflect.Set(Reflect.Get(attributes, "m_Attributes"), "m_MaxHealth", AssaultHP);
            TransferredFields["m_Attributes"] = attributes;

            ScriptableObject hud = CloneAsset((ScriptableObject)Reflect.Get(Source, "m_HUD"), "K_AssaultCruiser_HUD_MP");
            object hudData = Reflect.Get(hud, "m_Attributes");
            Reflect.Set(hudData, "mUnitLocalizedLongDescriptionStringID", LongDescription);
            DataPlan.ReplaceStrings(hudData, new Dictionary<string, string> { { ActiveRepair, PassiveAbility }, { SourceAA, AntiAirAbility } });
            TransferredFields["m_HUD"] = hud;

            ScriptableObject sourceView = (ScriptableObject)Reflect.Get(Source, "m_View");
            ScriptableObject view = CloneAsset(sourceView, "K_AssaultCruiser_Kitbash_View_MP");
            UnitViewAttributes sourceViewData = (UnitViewAttributes)Reflect.Property(sourceView, "Data");
            UnitViewAttributes viewData = (UnitViewAttributes)DataPlan.Clone(sourceViewData);
            int viewFields = 0;
            foreach (FieldInfo field in Reflect.Fields(view.GetType()))
                if (field.FieldType == typeof(UnitViewAttributes)) { field.SetValue(view, viewData); viewFields++; }
            Require(viewFields == 1 && object.ReferenceEquals(Reflect.Property(view, "Data"), viewData), "Independent cloned view attributes");
            viewData.ModelPrefab = ModelAssets.CreatePrefab(sourceViewData.ModelPrefab);
            TransferredFields["m_View"] = view;

            Array oldAbilities = (Array)Reflect.Get(Source, "m_Abilities");
            List<ScriptableObject> abilities = new List<ScriptableObject>();
            bool removed = false, retargeted = false;
            foreach (ScriptableObject ability in oldAbilities)
            {
                if (ability.name == ActiveRepair) { removed = true; continue; }
                if (ability.name == SourceAA)
                {
                    ScriptableObject aa = CloneAsset(ability, AntiAirAbility);
                    DataPlan.ReplaceStrings(Reflect.Get(aa, "m_Attributes"), new Dictionary<string, string> { { SourceAA, AntiAirAbility } });
                    SetAbilityResearch(aa, AntiAir);
                    KAntiAirAbility = aa;
                    abilities.Add(aa);
                    retargeted = true;
                }
                else abilities.Add(ability);
            }
            Require(removed && retargeted, "Source active repair removed and AA retargeted");
            ScriptableObject passive = CloneAsset(originalPassive, PassiveAbility);
            KPassiveAbility = passive;
            object passiveView = Reflect.Get(passive, "m_View");
            Reflect.Set(passiveView, "LocalizedTitleStringID", PassiveTitle);
            Reflect.Set(passiveView, "LocalizedShortDescriptionStringID", PassiveDescription);
            Reflect.Set(passiveView, "LocalizedLongDescriptionStringID", PassiveDescription);
            Reflect.Set(passiveView, "ForcePassiveVisuals", true);
            abilities.Add(passive);
            TransferredFields["m_Abilities"] = Reflect.TypedArray(oldAbilities.GetType().GetElementType(), abilities);

            KProduction = CloneAsset(Asset("Ability_G_ProduceBattlecruiser_MP"), "Ability_Produce_K_HonorGuard");
            SetAbilityResearch(KProduction, Fabrication);
            object prodData = Reflect.Get(KProduction, "m_Attributes");
            Reflect.Set(Reflect.Get(prodData, "m_ProduceUnit"), "m_UnitTypeToProduce", UnitKey);
            Reflect.Set(Reflect.Get(KProduction, "m_View"), "LocalizedLongDescriptionStringID", LongDescription);
            object productionVoice = Reflect.Get(Reflect.Get(KProduction, "m_View"), "VOFeedback");
            Reflect.Set(productionVoice, "DialogueCode", SpeechCode);
            Reflect.Set(Reflect.Get(KProduction, "m_View"), "VOFeedback", productionVoice);
            Require((string)Reflect.Get(Reflect.Get(Reflect.Get(KProduction, "m_View"), "VOFeedback"), "DialogueCode") == SpeechCode, "Assault production speech code");

            NewFabrication = DataPlan.Clone(Reflect.Property(Asset("Tier_3_C_AssaultCruiser"), "Data"));
            ((NamedObjectBase)NewFabrication).InitializeName(Fabrication);
            Reflect.Set(NewFabrication, "m_ResearchDependencies", new string[] { "Tier_1_G_Catamaran" });
            Reflect.Set(NewFabrication, "m_ResearchVOCode", SpeechCode);
            NewAntiAir = DataPlan.Clone(Reflect.Property(Asset("Research_Buff_G_BattleCruiser_AntiAir"), "Data"));
            ((NamedObjectBase)NewAntiAir).InitializeName(AntiAir);
            Reflect.Set(NewAntiAir, "m_ResearchDependencies", new string[] { Fabrication });
            // Keep the source Assault Cruiser AA announcement, rather than Khaaneph Honor Guard "def".
            Require((string)Reflect.Get(NewAntiAir, "m_ResearchVOCode") == AntiAirSpeechCode, "Source assault anti-air speech code");
            Log("Prepared assault definition: original source intact; final HP " + AssaultHP + ".");
        }

        static void ApplyTransfer()
        {
            foreach (KeyValuePair<string, object> field in TransferredFields) Change(KUnit, field.Key, field.Value);
            Rebuild(KUnit);
            if (KPack != null)
            {
                // Honor Guard textures belong to a different mesh/material set. Keep the assault mesh's
                // own materials and let the native customization controller apply Khaaneph team colours.
                Array textures = (Array)Reflect.Get(KPack, "m_PackTextures");
                List<object> kept = new List<object>();
                foreach (object texture in textures) if ((string)Reflect.Get(texture, "EntityType") != UnitKey) kept.Add(texture);
                Change(KPack, "m_PackTextures", Reflect.TypedArray(textures.GetType().GetElementType(), kept));
                Change(KPack, "mAssignedTextures", null);
            }
        }

        static ScriptableObject FindAbility(UnitAssetContainer unit, string name)
        {
            foreach (ScriptableObject ability in (Array)Reflect.Get(unit, "m_Abilities"))
                if (ability != null && ability.name == name) return ability;
            return null;
        }

        static void SetAbilityResearch(ScriptableObject ability, string research)
        {
            Reflect.Set(Reflect.Get(Reflect.Get(ability, "m_Attributes"), "m_ResearchDeps"), "m_DependentResearch", new string[] { research });
        }

        static void PatchProducers()
        {
            List<ScriptableObject> assets = new List<ScriptableObject>(Assets.Values);
            foreach (ScriptableObject asset in assets)
            {
                UnitAssetContainer unit = asset as UnitAssetContainer;
                if (unit == null) continue;
                Array original = (Array)Reflect.Get(unit, "m_Abilities");
                if (original == null) continue;
                List<ScriptableObject> kept = new List<ScriptableObject>();
                Dictionary<string, string> hidden = new Dictionary<string, string>();
                bool changed = false;
                foreach (ScriptableObject ability in original)
                {
                    if (ability == null) { kept.Add(ability); continue; }
                    object data = Reflect.Get(ability, "m_Attributes");
                    string target = (string)Reflect.Get(Reflect.Get(data, "m_ProduceUnit"), "m_UnitTypeToProduce");
                    if (target == SourceUnit || target == "C_Sob_AssaultCruiser_MP")
                    {
                        hidden[ability.name] = "";
                        changed = true;
                    }
                    else if (unit.name == "K_Carrier_MP" && ability.name == "Ability_Produce_K_HonorGuard" && KProduction != null)
                    {
                        kept.Add(KProduction);
                        changed = true;
                    }
                    else kept.Add(ability);
                }
                if (!changed) continue;
                Change(unit, "m_Abilities", Reflect.TypedArray(original.GetType().GetElementType(), kept));
                if (hidden.Count > 0)
                {
                    ScriptableObject hud;
                    if (!FilteredHUDs.TryGetValue(unit, out hud))
                    {
                        hud = CloneAsset((ScriptableObject)Reflect.Get(unit, "m_HUD"), unit.name + "_CruiserFilteredHUD");
                        DataPlan.ReplaceStrings(Reflect.Get(hud, "m_Attributes"), hidden);
                        FilteredHUDs.Add(unit, hud);
                    }
                    Change(unit, "m_HUD", hud);
                }
                Rebuild(unit);
                Log("Producer " + unit.name + ": removed " + hidden.Count + " Coalition/Soban Assault buttons; K production=" + (unit.name == "K_Carrier_MP"));
            }
        }

        static void PatchTechAssets()
        {
            string[] names = { "Coalition", "SobanTechTree", "Khaaneph" };
            foreach (string name in names)
            {
                ScriptableObject asset;
                if (!Assets.TryGetValue(name, out asset)) continue;
                object data;
                if (!TechClones.TryGetValue(asset, out data))
                {
                    data = DataPlan.PatchTechTree(Reflect.Get(asset, "m_Attributes"), name);
                    TechClones.Add(asset, data);
                }
                Change(asset, "m_Attributes", data);
                Log("Tech tree " + name + ": " + DataPlan.ResearchSummary(data));
            }
        }

        static void AddResearch(EntityTypeCollection collection, string name, object research)
        {
            EntityTypeAttributes entity = new EntityTypeAttributes(name);
            entity.Add(research);
            collection.Add(entity);
        }

        static void AddAbility(EntityTypeCollection collection, ScriptableObject asset)
        {
            // Validation, command icons and tooltips resolve standalone ability entities, while
            // unit instances also carry their own attributes. Both paths must use the same clone.
            EntityTypeAttributes entity = new EntityTypeAttributes(asset.name);
            foreach (object data in (IEnumerable)Reflect.Property(asset, "Data")) entity.Add(data);
            collection.Add(entity);
            Require(object.ReferenceEquals(entity.Get<BBI.Game.Data.AbilityAttributes>(), Reflect.Get(asset, "m_Attributes")), "Registered ability " + asset.name);
        }

        static void AuditProduction(EntityTypeCollection collection)
        {
            int khaaneph = 0;
            foreach (string key in collection.GetAllEntityTypeNames())
            {
                EntityTypeAttributes entity = collection.GetEntityType(key);
                foreach (object data in entity.GetAll<object>())
                {
                    if (data == null || data.GetType().Name != "AbilityAttributesData") continue;
                    string target = (string)Reflect.Get(Reflect.Get(data, "m_ProduceUnit"), "m_UnitTypeToProduce");
                    // Standalone ability definitions remain available to legacy data; unit producers must not contain them.
                    if (entity.GetAll<object>().Length <= 2) continue;
                    Require(target != SourceUnit && target != "C_Sob_AssaultCruiser_MP", "No C/S producer for " + key);
                    if (key == "K_Carrier_MP" && target == UnitKey)
                    {
                        string[] deps = (string[])Reflect.Get(Reflect.Get(data, "m_ResearchDeps"), "m_DependentResearch");
                        Require(deps.Length == 1 && deps[0] == Fabrication, "K production research gate");
                        khaaneph++;
                    }
                }
            }
            if (KUnit != null) Require(khaaneph == 1, "Exactly one Khaaneph carrier Assault production ability");
            Log("Production audit passed: no C/S Assault producers; K carrier Assault buttons=" + khaaneph + ".");
        }

        static void AuditRegisteredTech(EntityTypeCollection collection)
        {
            foreach (KeyValuePair<ScriptableObject, object> item in TechClones)
            {
                EntityTypeAttributes entity = collection.GetEntityType(item.Key.name);
                object registered = entity == null ? null : entity.Get<BBI.Game.Data.TechTreeAttributes>();
                Require(object.ReferenceEquals(registered, item.Value), "Actual registered " + item.Key.name + " tech tree uses current patched data");
                string ids = DataPlan.ResearchSummary(registered);
                if (item.Key.name == "Khaaneph")
                    Require(ids.Contains(Fabrication) && ids.Contains(AntiAir) && !ids.Contains("HonorCruiser"), "Actual Khaaneph registered research roster");
                else Require(!ids.Contains("AssaultCruiser"), "Actual " + item.Key.name + " has no Assault research");
                Log("Registered tech audit passed: " + item.Key.name + "; asset instance=" + item.Key.GetInstanceID());
            }
            if (KUnit != null) Require(TechClones.ContainsKey(Asset("Khaaneph")), "Current Khaaneph tech tree patched");
        }

        static ScriptableObject CloneAsset(ScriptableObject original, string name)
        {
            ScriptableObject clone = (ScriptableObject)UnityEngine.Object.Instantiate(original);
            // Unity's serialized clone does not retain prepared nonserialized fields; rebuild from a separate managed copy.
            foreach (FieldInfo field in Reflect.Fields(original.GetType())) field.SetValue(clone, DataPlan.Clone(field.GetValue(original)));
            clone.name = name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            Clones.Add(clone);
            Reflect.InvokeOnEnable(clone);
            return clone;
        }

        static void Change(ScriptableObject asset, string field, object value)
        {
            Dictionary<string, object> fields;
            if (!Originals.TryGetValue(asset, out fields))
            {
                fields = new Dictionary<string, object>();
                Originals.Add(asset, fields);
            }
            if (!fields.ContainsKey(field)) fields.Add(field, Reflect.Get(asset, field));
            Reflect.Set(asset, field, value);
        }

        static void Rebuild(UnitAssetContainer unit)
        {
            Change(unit, "mDataCache", null);
            Reflect.InvokeOnEnable(unit);
        }

        static void RestoreOriginals()
        {
            List<ScriptableObject> unloaded = new List<ScriptableObject>();
            foreach (KeyValuePair<ScriptableObject, Dictionary<string, object>> item in Originals)
            {
                if (item.Key == null) { unloaded.Add(item.Key); continue; }
                foreach (KeyValuePair<string, object> field in item.Value) Reflect.Set(item.Key, field.Key, field.Value);
            }
            foreach (ScriptableObject asset in unloaded) Originals.Remove(asset);
        }

        internal static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Invariant failed: " + message);
        }

        internal static void Log(string message)
        {
            // Unity's child domain has no ApplicationBase. The loaded runtime lives in BepInEx/core.
            // Diagnostics must never abort the game's initialization, including path lookup failures.
            try
            {
                string root = Path.GetDirectoryName(Path.GetDirectoryName(typeof(Runtime).Assembly.Location));
                string path = Path.Combine(root, "KhaanephCruiser.log");
                using (StreamWriter writer = File.AppendText(path)) writer.WriteLine(DateTime.UtcNow.ToString("o") + " " + message);
            }
            catch { try { Debug.Log("[Khaaneph Cruiser] " + message); } catch { } }
        }
    }

    public static class DataPlan
    {
        sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return object.ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
        static readonly MethodInfo Memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsAssaultSpeech(object ev)
        {
            if (ev == null) return false;
            if ((string)Reflect.Get(ev, "PrimaryDetail") == Runtime.SpeechCode) return true;
            string summary = (string)Reflect.Get(ev, "PrimarySummary");
            string action = (string)Reflect.Get(ev, "Action");
            if (summary == "opsfleet" && (action == "rsh" || action == "upg") &&
                (string)Reflect.Get(ev, "SecondarySummary") == Runtime.AntiAirSpeechCode) return true;
            if ((string)Reflect.Get(ev, "SecondaryDetail") != Runtime.SpeechCode &&
                (string)Reflect.Get(ev, "SecondarySummary") != Runtime.SpeechCode) return false;
            return summary == "opsfleet" || summary == "opscombat" ||
                (summary == "carrier" && ((string)Reflect.Get(ev, "Action") == "prd" || (string)Reflect.Get(ev, "Action") == "abt"));
        }

        public static object RouteAssaultSpeech(object ev, string faction)
        {
            if (!string.Equals(faction, "khaaneph", StringComparison.OrdinalIgnoreCase) || !IsAssaultSpeech(ev)) return ev;
            // Keep the queued event and entity references intact; only its playback routing changes.
            object routed = Memberwise.Invoke(ev, null);
            Reflect.Set(routed, "Faction", "coalition");
            if ((string)Reflect.Get(routed, "PrimarySummary") == "carrier") Reflect.Set(routed, "PrimaryDetail", "car");
            return routed;
        }

        public static object Clone(object value) { return Clone(value, new Dictionary<object, object>(new IdentityComparer())); }
        static object Clone(object value, Dictionary<object, object> seen)
        {
            if (value == null) return null;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type)) return value;
            object previous;
            if (seen.TryGetValue(value, out previous)) return previous;
            Array array = value as Array;
            if (array != null)
            {
                Array copy = (Array)array.Clone();
                seen.Add(value, copy);
                for (int i = 0; i < copy.Length; i++) copy.SetValue(Clone(array.GetValue(i), seen), i);
                return copy;
            }
            // Preserve external framework caches, delegates and immutable fixed-point values.
            if (type.Namespace == null || !type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) return value;
            object result = Memberwise.Invoke(value, null);
            seen.Add(value, result);
            foreach (FieldInfo field in Reflect.Fields(type)) field.SetValue(result, Clone(field.GetValue(value), seen));
            return result;
        }

        public static object PatchTechTree(object original, string faction)
        {
            object result = Clone(original);
            Array branches = (Array)Reflect.Get(result, "m_TechTrees");
            if (faction == "Khaaneph")
            {
                object honorTier = null, armored = null, honorUpgradeBranch = null;
                foreach (object branch in branches)
                {
                    foreach (object tier in (Array)Reflect.Get(branch, "m_TechTiers"))
                    {
                        string[] ids = (string[])Reflect.Get(tier, "m_ResearchItems");
                        if (Array.IndexOf(ids, "Tier_3_G_HonorCruiser") >= 0) honorTier = tier;
                        if (Array.IndexOf(ids, "Tier_1_G_Catamaran") >= 0) armored = branch;
                    }
                    foreach (object upgrade in (Array)Reflect.Get(branch, "m_Upgrades"))
                        if ((string)Reflect.Get(upgrade, "m_ResearchItem") == "Research_Buff_G_HonorCruiser_Defenses") honorUpgradeBranch = branch;
                }
                Runtime.Require(honorTier != null && armored != null && honorUpgradeBranch != null, "Khaaneph original tech layout");
                RemoveResearch(branches, new string[] { "Tier_3_G_HonorCruiser" });
                object newTier = Clone(honorTier);
                Reflect.Set(newTier, "m_TierName", "Assault Cruiser");
                Reflect.Set(newTier, "m_ResearchItems", new string[] { Runtime.Fabrication });
                Array prior = (Array)Reflect.Get(armored, "m_TechTiers");
                Runtime.Require(prior.Length == 1, "Assault Ship predecessor tier");
                Reflect.Set(armored, "m_TechTiers", Reflect.Append(prior, newTier));
                ReplaceStrings(honorUpgradeBranch, new Dictionary<string, string> {
                    { "Research_Buff_G_HonorCruiser_Defenses", Runtime.AntiAir },
                    { "Honor Cruiser", "Assault Cruiser" },
                    { "Honor Cruiser Defenses", "Assault Cruiser Anti-Air" },
                    { "Icon-Command-Production-G_HonorCruiser", "Icon-Command-Production-G_BattleCruiser" }
                });
            }
            else
            {
                string[] removed = faction == "Coalition"
                    ? new string[] { "Tier_3_C_AssaultCruiser", "Research_Buff_G_BattleCruiser_AntiAir" }
                    : new string[] { "Tier_3_S_AssaultCruiser", "Research_Buff_S_AssaultCruiser_AntiAir" };
                RemoveResearch(branches, removed);
                List<object> kept = new List<object>();
                foreach (object branch in branches)
                    if (((Array)Reflect.Get(branch, "m_TechTiers")).Length > 0 || ((Array)Reflect.Get(branch, "m_Upgrades")).Length > 0) kept.Add(branch);
                Reflect.Set(result, "m_TechTrees", Reflect.TypedArray(branches.GetType().GetElementType(), kept));
            }
            return result;
        }

        static void RemoveResearch(Array branches, string[] removed)
        {
            foreach (object branch in branches)
            {
                Array tiers = (Array)Reflect.Get(branch, "m_TechTiers");
                List<object> keptTiers = new List<object>();
                foreach (object tier in tiers)
                {
                    List<string> keptIDs = new List<string>();
                    foreach (string id in (string[])Reflect.Get(tier, "m_ResearchItems")) if (Array.IndexOf(removed, id) < 0) keptIDs.Add(id);
                    Reflect.Set(tier, "m_ResearchItems", keptIDs.ToArray());
                    if (keptIDs.Count > 0) keptTiers.Add(tier);
                }
                Reflect.Set(branch, "m_TechTiers", Reflect.TypedArray(tiers.GetType().GetElementType(), keptTiers));
                Array upgrades = (Array)Reflect.Get(branch, "m_Upgrades");
                List<object> keptUpgrades = new List<object>();
                foreach (object upgrade in upgrades) if (Array.IndexOf(removed, (string)Reflect.Get(upgrade, "m_ResearchItem")) < 0) keptUpgrades.Add(upgrade);
                Reflect.Set(branch, "m_Upgrades", Reflect.TypedArray(upgrades.GetType().GetElementType(), keptUpgrades));
            }
        }

        public static string ResearchSummary(object data)
        {
            List<string> ids = new List<string>();
            foreach (object branch in (Array)Reflect.Get(data, "m_TechTrees"))
            {
                foreach (object tier in (Array)Reflect.Get(branch, "m_TechTiers")) ids.AddRange((string[])Reflect.Get(tier, "m_ResearchItems"));
                foreach (object upgrade in (Array)Reflect.Get(branch, "m_Upgrades")) ids.Add((string)Reflect.Get(upgrade, "m_ResearchItem"));
            }
            return string.Join(",", ids.ToArray());
        }

        public static void ReplaceStrings(object value, IDictionary<string, string> replacements)
        { ReplaceStrings(value, replacements, new HashSet<object>(new IdentityComparer())); }
        static void ReplaceStrings(object value, IDictionary<string, string> replacements, HashSet<object> seen)
        {
            if (value == null || !seen.Add(value)) return;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || typeof(UnityEngine.Object).IsAssignableFrom(type)) return;
            Array array = value as Array;
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    string text = array.GetValue(i) as string, replacement;
                    if (text != null && replacements.TryGetValue(text, out replacement)) array.SetValue(replacement, i);
                    else ReplaceStrings(array.GetValue(i), replacements, seen);
                }
            }
            else if (type.Namespace != null && type.Namespace.StartsWith("BBI.", StringComparison.Ordinal))
            {
                foreach (FieldInfo field in Reflect.Fields(type))
                {
                    string text = field.GetValue(value) as string, replacement;
                    if (text != null && replacements.TryGetValue(text, out replacement)) field.SetValue(value, replacement);
                    else ReplaceStrings(field.GetValue(value), replacements, seen);
                }
            }
        }
    }

    public static class Reflect
    {
        const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        public static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (Type current = type; current != null && current != typeof(ScriptableObject) && current != typeof(UnityEngine.Object) && current != typeof(object); current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(InstanceFlags)) if (!field.IsStatic) yield return field;
        }
        public static FieldInfo Field(Type type, string name)
        {
            foreach (FieldInfo field in Fields(type)) if (field.Name == name) return field;
            throw new MissingFieldException(type.FullName, name);
        }
        public static object Get(object instance, string name) { return Field(instance.GetType(), name).GetValue(instance); }
        public static void Set(object instance, string name, object value) { Field(instance.GetType(), name).SetValue(instance, value); }
        public static object Property(object instance, string name) { return instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance, null); }
        public static object StaticField(Type type, string name) { return type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null); }
        public static Array TypedArray(Type type, IEnumerable items)
        {
            List<object> values = new List<object>();
            foreach (object item in items) values.Add(item);
            Array array = Array.CreateInstance(type, values.Count);
            for (int i = 0; i < values.Count; i++) array.SetValue(values[i], i);
            return array;
        }
        public static Array Append(Array array, object item)
        {
            Array result = Array.CreateInstance(array.GetType().GetElementType(), array.Length + 1);
            Array.Copy(array, result, array.Length);
            result.SetValue(item, array.Length);
            return result;
        }
        public static void InvokeOnEnable(ScriptableObject asset)
        {
            MethodInfo method = asset.GetType().GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null) method.Invoke(asset, null);
        }
    }
}
