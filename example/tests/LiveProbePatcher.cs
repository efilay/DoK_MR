using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using BepInEx.Preloader.Patching;

namespace KhaanephCruiser.Testing
{
    // Development-only preloader. Never included in the release package.
    public static class LiveProbePatcher
    {
        public static void Initialize()
        {
            // Instrument all native and production-hook returns after the real
            // patcher has installed its AudioClip substitution.
            List<PatcherPlugin> plugins=AssemblyPatcher.PatcherPlugins;
            PatcherPlugin self=plugins.Find(delegate(PatcherPlugin plugin){return plugin.TypeName==typeof(LiveProbePatcher).FullName;});
            if(self!=null){plugins.Remove(self);plugins.Add(self);}
        }
        public static IEnumerable<string> TargetDLLs { get { yield return "BBI.Unity.Game.dll"; yield return "BBI.Game.dll"; } }
        public static void Patch(AssemblyDefinition assembly)
        {
            string expected = assembly.Name.Name == "BBI.Game"
                ? "1216502eff7958a99fe305b439f81709bf340610c782ce449929d13b36ae45cc"
                : "b4c2c7bdbf9737140ac18d6191e50ddd27030a669f050374e8f7e3da8c6ec0e5";
            using (SHA256 sha = SHA256.Create())
            using (FileStream file = File.OpenRead(assembly.MainModule.FileName))
                if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != expected) throw new InvalidOperationException("Unsupported live probe target");
            ModuleDefinition module = assembly.MainModule;
            AssemblyNameReference reference = new AssemblyNameReference("KhaanephCruiser.LiveProbe", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(reference);
            TypeReference owner = new TypeReference("KhaanephCruiser.Testing", "LiveProbe", module, reference);
            bool game = assembly.Name.Name == "BBI.Game";
            TypeDefinition type = module.GetType(game ? "BBI.Game.Simulation.Sim" : "BBI.Unity.Game.ShipbreakersMain");
            MethodDefinition method = null;
            foreach (MethodDefinition candidate in type.Methods) if (candidate.Name == (game ? "Tick" : "Update")) method = candidate;
            if (method == null) throw new MissingMethodException(type.FullName, "probe target");
            MethodReference hook = new MethodReference(game ? "SimTick" : "Tick", module.TypeSystem.Void, owner);
            hook.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            ILProcessor il = method.Body.GetILProcessor();
            if (!game)
            {
                Instruction first = method.Body.Instructions[0];
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                il.InsertBefore(first, Instruction.Create(OpCodes.Call, hook));
            }
            else
            {
                List<Instruction> exits = new List<Instruction>();
                foreach (Instruction instruction in method.Body.Instructions) if (instruction.OpCode == OpCodes.Ret) exits.Add(instruction);
                foreach (Instruction instruction in exits)
                {
                    // The checksum remains below the probe's argument on the evaluation stack.
                    instruction.OpCode = OpCodes.Ldarg_0;
                    il.InsertAfter(instruction, Instruction.Create(OpCodes.Call, hook));
                    il.InsertAfter(instruction.Next, Instruction.Create(OpCodes.Ret));
                }
            }
            method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 8);
            if (!game)
            {
                TypeDefinition loader = module.GetType("BBI.Unity.Game.Audio.AudioAssetBundleLoader");
                foreach (MethodDefinition candidate in loader.Methods)
                {
                    if (candidate.Name != "TryLoadClip") continue;
                    MethodReference clip = new MethodReference("ClipLoaded", module.TypeSystem.Void, owner);
                    clip.Parameters.Add(new ParameterDefinition(module.TypeSystem.String)); clip.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                    List<Instruction> exits = new List<Instruction>();
                    foreach (Instruction instruction in candidate.Body.Instructions) if (instruction.OpCode == OpCodes.Ret) exits.Add(instruction);
                    ILProcessor clipIL = candidate.Body.GetILProcessor();
                    foreach (Instruction instruction in exits)
                    {
                        instruction.OpCode = OpCodes.Ldarg_1;
                        Instruction arg = Instruction.Create(OpCodes.Ldarg_2); clipIL.InsertAfter(instruction, arg);
                        Instruction dereference = Instruction.Create(OpCodes.Ldind_Ref); clipIL.InsertAfter(arg, dereference);
                        Instruction call = Instruction.Create(OpCodes.Call, clip); clipIL.InsertAfter(dereference, call); clipIL.InsertAfter(call, Instruction.Create(OpCodes.Ret));
                    }
                    candidate.Body.MaxStackSize = Math.Max(candidate.Body.MaxStackSize, 8);
                }
            }
        }
    }
}
