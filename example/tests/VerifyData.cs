using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using KhaanephCruiser;

class VerifyData
{
    static string Managed;
    static int Main(string[] args)
    {
        Managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs) {
            string path = Path.Combine(Managed, new AssemblyName(eventArgs.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run(args[1]); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static IDictionary<string, object> Map(object value) { return (IDictionary<string, object>)value; }
    static void Run(string evidence)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
        IDictionary<string, object> doc = serializer.Deserialize<IDictionary<string, object>>(File.ReadAllText(evidence));
        IDictionary<string, object> trees = Map(doc["tech_trees"]);
        Assembly data = Assembly.LoadFrom(Path.Combine(Managed, "BBI.Game.Data.dll"));
        Type techType = data.GetType("BBI.Game.Data.TechTreeAttributesData", true);
        Dictionary<string, object> originals = new Dictionary<string, object>();
        Dictionary<string, object> patched = new Dictionary<string, object>();
        string[] fixtureNames = { "coalition", "soban", "khaaneph" };
        string[] assetNames = { "Coalition", "SobanTechTree", "Khaaneph" };
        for (int i = 0; i < fixtureNames.Length; i++)
        {
            IDictionary<string, object> fixture = Map(trees[fixtureNames[i]]);
            object original = Fill(techType, new Dictionary<string, object> { { "m_TechTrees", fixture["branches"] } });
            originals.Add(assetNames[i], original);
            patched.Add(assetNames[i], DataPlan.PatchTechTree(original, assetNames[i]));
            Check(!object.ReferenceEquals(original, patched[assetNames[i]]), "Root clone isolation");
            Check(DataPlan.ResearchSummary(original).Contains(i == 0 ? "Tier_3_C_AssaultCruiser" : i == 1 ? "Tier_3_S_AssaultCruiser" : "Tier_3_G_HonorCruiser"), "Original research changed");
        }
        string coal = DataPlan.ResearchSummary(patched["Coalition"]), soban = DataPlan.ResearchSummary(patched["SobanTechTree"]), k = DataPlan.ResearchSummary(patched["Khaaneph"]);
        Check(!coal.Contains("AssaultCruiser") && !coal.Contains("BattleCruiser_AntiAir"), "Coalition Assault research remains");
        Check(!soban.Contains("AssaultCruiser"), "Soban Assault research remains");
        Check(coal.Contains("Tier_3_C_Battlecruiser") || coal.Contains("Tier_3_C_BattleCruiser"), "Coalition Battlecruiser removed accidentally");
        Check(coal.Contains("TrackedUnits_Armor") && soban.Contains("TrackedUnits_Armor"), "Shared armor research removed");
        Check(!k.Contains("HonorCruiser") && k.Contains(Runtime.Fabrication) && k.Contains(Runtime.AntiAir), "K research transfer wrong");
        Array branches = (Array)Reflect.Get(patched["Khaaneph"], "m_TechTrees");
        int fabricationBranch = -1;
        for (int i = 0; i < branches.Length; i++)
        {
            Array tiers = (Array)Reflect.Get(branches.GetValue(i), "m_TechTiers");
            for (int j = 0; j < tiers.Length; j++)
            {
                string[] ids = (string[])Reflect.Get(tiers.GetValue(j), "m_ResearchItems");
                if (Array.IndexOf(ids, Runtime.Fabrication) < 0) continue;
                fabricationBranch = i;
                Check(j == 1 && Array.IndexOf((string[])Reflect.Get(tiers.GetValue(0), "m_ResearchItems"), "Tier_1_G_Catamaran") >= 0, "Fabrication not after Assault Ship");
            }
        }
        Check(fabricationBranch >= 0, "New fabrication tier missing");
        // Exercise the game's actual pure research-membership and next-tier methods against the cloned definitions.
        Assembly simulation = Assembly.LoadFrom(Path.Combine(Managed, "BBI.Game.dll"));
        Type helper = simulation.GetType("BBI.Game.Simulation.ResearchHelperShared", true);
        Type researchType = Assembly.LoadFrom(Path.Combine(Managed, "BBI.Unity.Game.Data.dll")).GetType("BBI.Unity.Game.Data.ResearchItemAttributesData", true);
        object fabrication = Activator.CreateInstance(researchType, true);
        researchType.GetMethod("InitializeName").Invoke(fabrication, new object[] { Runtime.Fabrication });
        object aa = Activator.CreateInstance(researchType, true);
        researchType.GetMethod("InitializeName").Invoke(aa, new object[] { Runtime.AntiAir });
        Check((bool)helper.GetMethod("IsResearchItemInTechTree").Invoke(null, new object[] { fabrication, patched["Khaaneph"] }), "Native research membership rejected fabrication");
        Check((bool)helper.GetMethod("IsResearchItemInUpgrades").Invoke(null, new object[] { aa, patched["Khaaneph"] }), "Native research membership rejected AA");
        Type commanderState = simulation.GetType("BBI.Game.Simulation.CommanderState", true);
        object state = FormatterServices.GetUninitializedObject(commanderState);
        int[] current = new int[branches.Length];
        SetState(state, "CurrentTechTreeTiers", current);
        MethodInfo next = helper.GetMethod("IsResearchItemInNextTiers");
        Check(!(bool)next.Invoke(null, new object[] { fabrication, patched["Khaaneph"], state }), "Native next-tier check unlocked fabrication too early");
        current[fabricationBranch] = 1;
        Check((bool)next.Invoke(null, new object[] { fabrication, patched["Khaaneph"], state }), "Native next-tier check rejects after Assault Ship");
        Check(Runtime.AssaultHP == 3650 && 2700 * 3350 / 2500 == 3618, "HP ratio or user rounding wrong");
        Type formatter = Assembly.LoadFrom(Path.Combine(Managed, "BBI.Core.dll")).GetType("BBI.Core.Localization.ObjectFormatter", true);
        foreach (string fieldName in new string[] { "LongDescription", "PassiveTitle", "PassiveDescription" })
        {
            string key = (string)typeof(Runtime).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            Check((bool)formatter.GetMethod("IsTextLocString").Invoke(null, new object[] { key }), "Native tooltip formatter does not recognize mod localization key: " + key);
            object entry = Runtime.GetLocalization(key);
            string text = entry == null ? null : (string)Reflect.Get(entry, "Text");
            Check(!string.IsNullOrEmpty(text) && text != key, "Mod localization resolves to untranslated key: " + key);
        }
        Check(Runtime.GetLocalization("ID_CORE_UNIT_G_BATTLECRUISER_TITLE_5848") == null, "Localization fall-through changed native strings");
        VerifySpeech(simulation);
        Console.WriteLine("PASS: original isolation, Coalition/Soban removal, Battlecruiser/shared upgrades retained, K branch order, native research membership/next tier, HP3650, localization fall-through.");
    }
    static void VerifySpeech(Assembly simulation)
    {
        Type voice = simulation.GetType("BBI.Game.Events.VoiceOverEvent", true);
        object selection = Voice(voice, "sel", "cruiser", "cas", null);
        object routed = DataPlan.RouteAssaultSpeech(selection, "khaaneph");
        Check(!object.ReferenceEquals(selection, routed) && (string)Reflect.Get(routed, "Faction") == "coalition", "Assault selection is not routed to recorded Assault voices");
        Check((string)Reflect.Get(selection, "Faction") == string.Empty && (string)Reflect.Get(routed, "PrimaryDetail") == "cas", "Queued voice event mutated or unit identity lost");
        object production = DataPlan.RouteAssaultSpeech(Voice(voice, "prd", "carrier", "knc", "cas"), "khaaneph");
        Check((string)Reflect.Get(production, "PrimaryDetail") == "car" && (string)Reflect.Get(production, "Faction") == "coalition", "Carrier Assault production voice routing");
        object ability = DataPlan.RouteAssaultSpeech(Voice(voice, "abt", "carrier", "knc", "cas"), "khaaneph");
        Check((string)Reflect.Get(ability, "Faction") == "coalition", "Carrier production ability voice routing");
        object researchEvent = Voice(voice, "rsh", "opsfleet", "ops", null);
        Reflect.Set(researchEvent, "SecondarySummary", "cas");
        object research = DataPlan.RouteAssaultSpeech(researchEvent, "khaaneph");
        Check((string)Reflect.Get(research, "Faction") == "coalition", "Assault fabrication research voice routing");
        object aaEvent = Voice(voice, "rsh", "opsfleet", "ops", null);
        Reflect.Set(aaEvent, "SecondarySummary", "aca");
        object aaResearch = DataPlan.RouteAssaultSpeech(aaEvent, "khaaneph");
        Check(!object.ReferenceEquals(aaEvent, aaResearch) && (string)Reflect.Get(aaResearch, "Faction") == "coalition", "Assault anti-air research voice routing");
        Check((string)Reflect.Get(aaEvent, "Faction") == string.Empty && (string)Reflect.Get(aaResearch, "SecondarySummary") == "aca", "AA research event mutated or original upgrade speech code lost");
        object defense = Voice(voice, "rsh", "opsfleet", "ops", null);
        Reflect.Set(defense, "SecondarySummary", "def");
        Check(object.ReferenceEquals(DataPlan.RouteAssaultSpeech(defense, "khaaneph"), defense), "Other Khaaneph defense research voice changed");
        object unrelated = Voice(voice, "atk", "strike", "skm", "cas");
        Check(object.ReferenceEquals(DataPlan.RouteAssaultSpeech(unrelated, "khaaneph"), unrelated), "Unrelated Khaaneph attack voice changed");
        Check(object.ReferenceEquals(DataPlan.RouteAssaultSpeech(selection, "coalition"), selection), "Original Coalition voice changed");
        Check(object.ReferenceEquals(Runtime.PrepareVoiceEvent(null, selection), selection), "Campaign/startup voice guard failed");
        string root="Audio/VO/English/Coalition/";
        foreach(string action in new string[]{"sel","mov","atk","atm","hld","stp","grd"})
        {
            string[] candidates=NativeVoices.Candidates(root+"Cruiser_Assault/vo_coalition_cruiser_"+action+"_casgengengen_t1_test");
            Check(candidates!=null && candidates.Length>0,"Khaaneph command response missing "+action);
            foreach(string candidate in candidates)Check(candidate.StartsWith("vo_khaaneph_"),"Non-Khaaneph recording in approved command list");
        }
        Check(NativeVoices.Candidates(root+"OpsFleet/vo_coalition_opsfleet_rsh_opscasgengen_t1_test")[0].Contains("13281"),"Fabrication lacks production-unlocked response");
        Check(NativeVoices.Candidates(root+"OpsFleet/vo_coalition_opsfleet_upg_opsacagengen_t1_test")[0].Contains("13280"),"AA lacks generic Khaaneph response");
        Check(NativeVoices.Candidates(root+"OpsFleet/vo_coalition_opsfleet_rsh_opsdefgengen_t1_test")==null,"Other faction upgrade recording overridden");
        Check(NativeVoices.Candidates("Audio/VO/English/Khaaneph/Cruiser/vo_khaaneph_cruiser_sel_cbtgengengen_t1_9503")==null,"Existing Khaaneph unit overridden");
        Check(Runtime.GetAssaultVoiceClip(null,root+"Cruiser_Assault/vo_coalition_cruiser_sel_casgengengen_t1_test")==null,"Campaign clip substitution guard failed");
        Console.WriteLine("PASS: recorded Assault voice routing, carrier/research callouts, original event isolation, unrelated faction/attack voices and campaign guard.");
    }
    static object Voice(Type type, string action, string summary, string detail, string secondary)
    {
        Type group = type.GetField("Group").FieldType;
        return Activator.CreateInstance(type, new object[] { 1, action, Enum.Parse(group, "UnitEvent"), string.Empty, summary, detail, null, secondary });
    }
    static void SetState(object state, string name, object value)
    {
        FieldInfo field = state.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) field = state.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) throw new Exception("Unknown state member " + name);
        field.SetValue(state, value);
    }
    static object Fill(Type type, object json)
    {
        if (json == null) return null;
        if (type.IsArray)
        {
            IEnumerable values = (IEnumerable)json;
            List<object> list = new List<object>();
            foreach (object value in values) list.Add(Fill(type.GetElementType(), value));
            return Reflect.TypedArray(type.GetElementType(), list);
        }
        if (type == typeof(string)) return Convert.ToString(json);
        if (type.IsEnum) return Enum.ToObject(type, Convert.ToInt32(json));
        if (type.IsPrimitive) return Convert.ChangeType(json, type);
        object result = Activator.CreateInstance(type, true);
        foreach (KeyValuePair<string, object> item in Map(json))
        {
            FieldInfo field = null;
            foreach (FieldInfo candidate in Reflect.Fields(type)) if (candidate.Name == item.Key) field = candidate;
            if (field != null) field.SetValue(result, Fill(field.FieldType, item.Value));
        }
        return result;
    }
}
