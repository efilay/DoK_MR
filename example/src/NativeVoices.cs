using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KhaanephCruiser
{
    // Fabric still resolves the original Assault event hierarchy. Substitute
    // approved recordings from the already-loaded Khaaneph bank at clip lookup.
    public static class NativeVoices
    {
        static IDictionary LastDictionary;
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        static readonly HashSet<string> Audit = new HashSet<string>();
        static readonly string[] Selection = Names("cruiser_sel_cbtgengengen_t1_", "9503", "9504", "9505");
        static readonly string[] Movement = Names("cruiser_mov_cbtgengengen_t1_", "9474", "9476", "9478", "9480");
        static readonly string[] Attack = Names("cruiser_atk_cbtgengengen_t2_", "9319", "9320", "9322", "9323");
        static readonly string[] Stop = Names("cruiser_stp_cbtgengengen_t1_", "9518", "9520");
        static readonly string[] Fabrication = Names("opsfleet_rsh_opsgengengen_t1_", "13281_alt00", "13281_alt01");
        static readonly string[] Upgrade = Names("opsfleet_rsh_opsgengengen_t1_", "13280_alt00", "13280_alt01", "13301_alt00", "13301_alt01");

        static string[] Names(string family, params string[] numbers)
        {
            string[] result=new string[numbers.Length];
            for(int i=0;i<result.Length;i++)result[i]="vo_khaaneph_"+family+numbers[i];
            return result;
        }
        public static string[] Candidates(string reference)
        {
            if(string.IsNullOrEmpty(reference))return null;
            string path=reference.ToLowerInvariant();
            if(!path.Contains("/coalition/"))return null;
            if(path.Contains("_rsh_opscas"))return Fabrication;
            if(path.Contains("_rsh_opsaca") || path.Contains("_upg_opsaca"))return Upgrade;
            if(path.Contains("_prc_opscas"))return Selection;
            if(!path.Contains("/cruiser_assault/"))return null;
            if(path.Contains("_mov_") || path.Contains("_atm_"))return Movement;
            if(path.Contains("_atk_"))return Attack;
            if(path.Contains("_stp_") || path.Contains("_hld_"))return Stop;
            if(path.Contains("_sel_") || path.Contains("_grd_"))return Selection;
            return null;
        }
        public static object GetClip(object loader,string reference)
        {
            string[] candidates=Candidates(reference);
            if(candidates==null)return null;
            IDictionary dictionary=(IDictionary)Reflect.Get(loader,"mClipDictionary");
            if(!object.ReferenceEquals(dictionary,LastDictionary))
            {
                LastDictionary=dictionary;Clips.Clear();
                foreach(DictionaryEntry entry in dictionary)
                {
                    AudioClip clip=entry.Value as AudioClip;
                    if(clip!=null && clip.name.StartsWith("vo_khaaneph_",StringComparison.Ordinal))Clips[clip.name]=clip;
                }
            }
            uint hash=2166136261;
            foreach(char character in reference)hash=unchecked((hash^character)*16777619);
            AudioClip chosen;
            string name=candidates[(int)(hash%(uint)candidates.Length)];
            if(!Clips.TryGetValue(name,out chosen) || chosen==null)
            {
                // Native unload/reload can reuse the loader's dictionary object
                // while replacing every AudioClip in it.
                Clips.Clear();
                foreach(DictionaryEntry entry in dictionary)
                {
                    AudioClip clip=entry.Value as AudioClip;
                    if(clip!=null && clip.name.StartsWith("vo_khaaneph_",StringComparison.Ordinal))Clips[clip.name]=clip;
                }
                if(Clips.TryGetValue(name,out chosen) && chosen!=null)return chosen;
                if(Audit.Add("missing/"+name))Runtime.Log("ERROR: Khaaneph voice recording unavailable: "+name);
                return null;
            }
            if(Audit.Add(reference))Runtime.Log("Khaaneph recorded voice: "+reference+" -> "+name);
            return chosen;
        }
    }
}
