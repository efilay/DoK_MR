using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using BepInEx.Preloader;
using BepInEx.Preloader.Patching;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyTitle("DoK Khaaneph Assault Cruiser in-memory hooks")]

namespace KhaanephCruiser.Preloader
{
    // BepInEx 5 preloader contract. No game or runtime references: do not load targets early.
    public static class Patcher
    {
        public const string SupportedGameAssemblySHA256 = "b4c2c7bdbf9737140ac18d6191e50ddd27030a669f050374e8f7e3da8c6ec0e5";
        public static IEnumerable<string> TargetDLLs { get { yield return "BBI.Unity.Game.dll"; } }

        public static void Initialize()
        {
            // Unity5.2 crashes while the general BepInEx chainloader creates its ThreadingHelper
            // MonoBehaviour. This mod uses direct static hooks and needs only the preloader.
            // Remove that built-in entrypoint before target selection; UnityEngine stays unpatched.
            int removed = AssemblyPatcher.PatcherPlugins.RemoveAll(delegate(PatcherPlugin plugin) { return plugin.TypeName == "BepInEx.Chainloader"; });
            File.AppendAllText(Path.Combine(BepInEx.Paths.BepInExRootPath, "KhaanephCruiser.preloader.log"), DateTime.UtcNow.ToString("o") + " Preloader-only mode; general chainloader entries removed=" + removed + Environment.NewLine);
        }

        public static void Finish()
        {
            using (StreamWriter writer = File.AppendText(Path.Combine(BepInEx.Paths.BepInExRootPath, "KhaanephCruiser.preloader.log")))
                foreach (BepInEx.Logging.LogEventArgs entry in PreloaderConsoleListener.LogEvents) writer.WriteLine(entry.ToString());
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            string file = assembly.MainModule.FileName;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) throw new InvalidOperationException("Cannot verify the original DoK assembly path.");
            if (Hash(file) != SupportedGameAssemblySHA256) throw new InvalidOperationException("Unsupported DoK build: BBI.Unity.Game.dll SHA256 mismatch. No cruiser hooks applied.");
            ModuleDefinition module = assembly.MainModule;
            foreach (AssemblyNameReference existing in module.AssemblyReferences)
                if (existing.Name == "KhaanephCruiser.Runtime") throw new InvalidOperationException("DoK assembly already has cruiser hooks.");
            AssemblyNameReference runtime = new AssemblyNameReference("KhaanephCruiser.Runtime", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(runtime);
            TypeReference hooks = new TypeReference("KhaanephCruiser", "Runtime", module, runtime, false);

            MethodDefinition register = Method(module, "BBI.Unity.Game.ShipbreakersMain", "InitializeEntityManager", 2);
            if (!register.IsStatic || register.ReturnType.MetadataType != MetadataType.Void) throw new InvalidOperationException("Unexpected entity registration signature.");
            MethodReference before = Hook(module, hooks, "BeforeEntityRegistration", module.TypeSystem.Void, module.TypeSystem.Object, module.TypeSystem.Object);
            Prefix(register, new Instruction[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, before) });
            Suffix(register, Hook(module, hooks, "AfterEntityRegistration", module.TypeSystem.Void));

            MethodDefinition preview = Method(module, "BBI.Unity.Game.UI.PlayerCustomizationPanel/ContentManager/SceneContent", "SetFactionPack", 1);
            if (preview.IsStatic || preview.ReturnType.MetadataType != MetadataType.Void) throw new InvalidOperationException("Unexpected faction preview signature.");
            Prefix(preview, new Instruction[] { Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, Hook(module, hooks, "BeforeFactionPreview", module.TypeSystem.Void, module.TypeSystem.Object)) });

            TypeDefinition audioLoader = Type(module, "BBI.Unity.Game.Audio.AudioAssetBundleLoader");
            MethodDefinition loadSpeech = null;
            foreach (MethodDefinition method in audioLoader.Methods)
                if (method.Name == "LoadAssetBundle" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "BBI.Unity.Core.Utility.StreamableAssetContainer") loadSpeech = method;
            if (loadSpeech == null || loadSpeech.IsStatic) throw new InvalidOperationException("Missing streamed speech hierarchy loader.");
            Prefix(loadSpeech, new Instruction[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, Hook(module, hooks, "BeforeSpeechBankLoad", module.TypeSystem.Void, module.TypeSystem.Object, module.TypeSystem.Object)) });

            MethodDefinition clipLookup=Method(module,"BBI.Unity.Game.Audio.AudioAssetBundleLoader","TryLoadClip",2);
            if(clipLookup.IsStatic || clipLookup.ReturnType.MetadataType!=MetadataType.Boolean || clipLookup.Parameters[0].ParameterType.MetadataType!=MetadataType.String || !(clipLookup.Parameters[1].ParameterType is ByReferenceType))
                throw new InvalidOperationException("Unexpected audio clip lookup signature");
            TypeReference clipType=((ByReferenceType)clipLookup.Parameters[1].ParameterType).ElementType;
            if(clipType.FullName!="UnityEngine.AudioClip")throw new InvalidOperationException("Unexpected audio clip output type");
            VariableDefinition clipOverride=new VariableDefinition(module.TypeSystem.Object);clipLookup.Body.Variables.Add(clipOverride);clipLookup.Body.InitLocals=true;
            Instruction voiceFallback=Instruction.Create(OpCodes.Nop);
            Prefix(clipLookup,new Instruction[]{
                Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Call,Hook(module,hooks,"GetAssaultVoiceClip",module.TypeSystem.Object,module.TypeSystem.Object,module.TypeSystem.String)),
                Instruction.Create(OpCodes.Stloc,clipOverride),Instruction.Create(OpCodes.Ldloc,clipOverride),Instruction.Create(OpCodes.Brfalse,voiceFallback),
                Instruction.Create(OpCodes.Ldarg_2),Instruction.Create(OpCodes.Ldloc,clipOverride),Instruction.Create(OpCodes.Castclass,clipType),Instruction.Create(OpCodes.Stind_Ref),
                Instruction.Create(OpCodes.Ldc_I4_1),Instruction.Create(OpCodes.Ret),voiceFallback
            });

            MethodDefinition voice = Method(module, "BBI.Unity.Game.Audio.VOController", "ExecuteVO", 2);
            if (voice.IsStatic || voice.Parameters[0].ParameterType.FullName != "BBI.Game.Events.VoiceOverEvent" || voice.ReturnType.MetadataType != MetadataType.Boolean) throw new InvalidOperationException("Unexpected voice playback signature.");
            Prefix(voice, new Instruction[] {
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Call, Hook(module, hooks, "PrepareVoiceEvent", module.TypeSystem.Object, module.TypeSystem.Object, module.TypeSystem.Object)),
                Instruction.Create(OpCodes.Castclass, voice.Parameters[0].ParameterType), Instruction.Create(OpCodes.Starg_S, voice.Parameters[0])
            });

            MethodDefinition localize = null;
            TypeDefinition manager = Type(module, "BBI.Unity.Game.Localize.GameLocalizationManager");
            foreach (MethodDefinition method in manager.Methods)
                if (method.Name == "GetEntry" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.MetadataType == MetadataType.String) localize = method;
            if (localize == null || localize.IsStatic) throw new InvalidOperationException("Missing string localization method.");
            Instruction noOverride = Instruction.Create(OpCodes.Pop);
            Prefix(localize, new Instruction[] {
                Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Call, Hook(module, hooks, "GetLocalization", module.TypeSystem.Object, module.TypeSystem.String)),
                Instruction.Create(OpCodes.Dup),
                Instruction.Create(OpCodes.Brfalse, noOverride),
                Instruction.Create(OpCodes.Castclass, localize.ReturnType),
                Instruction.Create(OpCodes.Ret),
                noOverride
            });
        }

        static MethodReference Hook(ModuleDefinition module, TypeReference owner, string name, TypeReference result, params TypeReference[] parameters)
        {
            MethodReference method = new MethodReference(name, result, owner);
            method.HasThis = false;
            foreach (TypeReference type in parameters) method.Parameters.Add(new ParameterDefinition(type));
            return method;
        }
        static void Prefix(MethodDefinition method, Instruction[] instructions)
        {
            if (!method.HasBody || method.Body.Instructions.Count == 0) throw new InvalidOperationException("Missing method body: " + method.FullName);
            ILProcessor il = method.Body.GetILProcessor();
            Instruction first = method.Body.Instructions[0];
            foreach (Instruction instruction in instructions) il.InsertBefore(first, instruction);
            method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 8);
        }
        static void Suffix(MethodDefinition method, MethodReference hook)
        {
            ILProcessor il = method.Body.GetILProcessor();
            List<Instruction> exits = new List<Instruction>();
            foreach (Instruction instruction in method.Body.Instructions) if (instruction.OpCode == OpCodes.Ret) exits.Add(instruction);
            if (exits.Count == 0) throw new InvalidOperationException("No return: " + method.FullName);
            // Preserve branch/exception-handler references to the original return instruction.
            foreach (Instruction instruction in exits)
            {
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = hook;
                il.InsertAfter(instruction, Instruction.Create(OpCodes.Ret));
            }
        }
        static MethodDefinition Method(ModuleDefinition module, string owner, string name, int count)
        {
            MethodDefinition result = null;
            foreach (MethodDefinition method in Type(module, owner).Methods)
            {
                if (method.Name != name || method.Parameters.Count != count) continue;
                if (result != null) throw new InvalidOperationException("Ambiguous hook: " + owner + "." + name);
                result = method;
            }
            if (result == null) throw new InvalidOperationException("Missing hook: " + owner + "." + name);
            return result;
        }
        static TypeDefinition Type(ModuleDefinition module, string name)
        {
            foreach (TypeDefinition type in module.Types)
            {
                TypeDefinition result = Find(type, name);
                if (result != null) return result;
            }
            throw new InvalidOperationException("Missing hook type: " + name);
        }
        static TypeDefinition Find(TypeDefinition type, string name)
        {
            if (type.FullName == name) return type;
            foreach (TypeDefinition nested in type.NestedTypes)
            {
                TypeDefinition result = Find(nested, name);
                if (result != null) return result;
            }
            return null;
        }
        static string Hash(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
