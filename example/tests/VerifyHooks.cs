using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using KhaanephCruiser.Preloader;

class VerifyHooks
{
    static string[] Paths;
    static int Main(string[] args)
    {
        Paths = new string[] { args[1], args[2], Path.GetDirectoryName(args[0]) };
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs) {
            foreach (string path in Paths) { string file = Path.Combine(path, new AssemblyName(eventArgs.Name).Name + ".dll"); if (File.Exists(file)) return Assembly.LoadFrom(file); }
            return null;
        };
        try { Run(args[0], args[3]); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static string Hash(string file) { using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static void Run(string source, string output)
    {
        string initial = Hash(source);
        DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
        foreach (string path in Paths) resolver.AddSearchDirectory(path);
        VerifyPreloaderOnly(Path.GetDirectoryName(output));
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { AssemblyResolver = resolver }))
        {
            Patcher.Patch(assembly);
            MethodDefinition register = assembly.MainModule.GetType("BBI.Unity.Game.ShipbreakersMain").Methods.Find(m => m.Name == "InitializeEntityManager");
            Check(register.Body.Instructions[2].Operand.ToString().Contains("BeforeEntityRegistration"), "Registration prefix missing");
            int returns = 0;
            foreach (Instruction instruction in register.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Ret) continue;
                returns++;
                Check(instruction.Previous.OpCode == OpCodes.Call && instruction.Previous.Operand.ToString().Contains("AfterEntityRegistration"), "A registration exit bypasses the suffix");
            }
            Check(returns > 0, "No verified registration returns");
            int calls = 0;
            foreach (TypeDefinition type in assembly.MainModule.Types) calls += Count(type);
            Check(calls == returns + 6, "Unexpected hook call count: " + calls);
            AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(Path.Combine(Paths[1], "KhaanephCruiser.Runtime.dll"));
            Check(runtime.MainModule.Runtime == TargetRuntime.Net_2_0, "Runtime must target old Mono/.NET2 metadata");
            foreach (AssemblyNameReference reference in runtime.MainModule.AssemblyReferences)
                if (reference.Name == "mscorlib") Check(reference.Version.Major == 2, "Wrong mscorlib version");
            VerifyABI(assembly, runtime);
            bool duplicateRejected = false;
            try { Patcher.Patch(assembly); } catch (InvalidOperationException) { duplicateRejected = true; }
            Check(duplicateRejected, "Duplicate patch was accepted");
            assembly.Write(output);
            using (AssemblyDefinition reread = AssemblyDefinition.ReadAssembly(output, new ReaderParameters { AssemblyResolver = resolver }))
                Check(reread.MainModule.AssemblyReferences.Find(r => r.Name == "KhaanephCruiser.Runtime") != null, "Serialized patch lost runtime reference");
        }
        string unsupported = Path.Combine(Path.GetDirectoryName(output), "Unsupported-guard.dll");
        File.Copy(source, unsupported, true);
        using (FileStream stream = new FileStream(unsupported, FileMode.Append)) stream.WriteByte(0);
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(unsupported))
        {
            bool rejected = false;
            try { Patcher.Patch(assembly); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Changed build accepted");
            Check(assembly.MainModule.AssemblyReferences.Find(r => r.Name == "KhaanephCruiser.Runtime") == null, "Unsupported build mutated before rejection");
        }
        Check(Hash(source) == initial && initial == Patcher.SupportedGameAssemblySHA256, "Original game file changed");
        Console.WriteLine("PASS: preloader-only initialization, hook ABI, prefixes, every registration return, serialized DLL, .NET2 target, duplicate/unsupported guards, unchanged original.");
    }
    static void VerifyPreloaderOnly(string evidencePath)
    {
        string root = Path.Combine(evidencePath, "preloader-only-test");
        Directory.CreateDirectory(root);
        Type paths = Assembly.LoadFrom(Path.Combine(Paths[0], "BepInEx.dll")).GetType("BepInEx.Paths", true);
        paths.GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { Path.Combine(evidencePath, "VerifyHooks.exe"), root, Paths[2], new string[] { Paths[2] } });
        Type patcher = Assembly.LoadFrom(Path.Combine(Paths[0], "BepInEx.Preloader.dll")).GetType("BepInEx.Preloader.Patching.AssemblyPatcher", true);
        IList plugins = (IList)patcher.GetProperty("PatcherPlugins").GetValue(null, null);
        Type pluginType = plugins.GetType().GetGenericArguments()[0];
        object builtIn = Activator.CreateInstance(pluginType), other = Activator.CreateInstance(pluginType);
        SetPluginName(pluginType, builtIn, "BepInEx.Chainloader");
        SetPluginName(pluginType, other, "OtherPatcher");
        plugins.Add(builtIn); plugins.Add(other);
        Patcher.Initialize();
        Check(plugins.Count == 1 && object.ReferenceEquals(plugins[0], other), "General chainloader was not isolated before target selection");
        plugins.Clear();
    }
    static void SetPluginName(Type type, object instance, string name)
    {
        PropertyInfo property = type.GetProperty("TypeName");
        if (property != null) property.SetValue(instance, name, null);
        else type.GetField("TypeName").SetValue(instance, name);
    }
    static int Count(TypeDefinition type)
    {
        int count = 0;
        foreach (MethodDefinition method in type.Methods)
        {
            if (!method.HasBody) continue;
            foreach (Instruction instruction in method.Body.Instructions)
                if (instruction.OpCode == OpCodes.Call && instruction.Operand.ToString().Contains("KhaanephCruiser.Runtime::")) count++;
        }
        foreach (TypeDefinition child in type.NestedTypes) count += Count(child);
        return count;
    }
    static void VerifyABI(AssemblyDefinition game, AssemblyDefinition runtime)
    {
        foreach (TypeDefinition type in game.MainModule.Types) VerifyABIType(type, runtime.MainModule.GetType("KhaanephCruiser.Runtime"));
    }
    static void VerifyABIType(TypeDefinition type, TypeDefinition runtime)
    {
        foreach (MethodDefinition method in type.Methods)
        {
            if (!method.HasBody) continue;
            foreach (Instruction instruction in method.Body.Instructions)
            {
                MethodReference call = instruction.Operand as MethodReference;
                if (call == null || call.DeclaringType.FullName != runtime.FullName) continue;
                MethodDefinition target = runtime.Methods.Find(m => m.Name == call.Name);
                Check(target != null && target.IsPublic && target.IsStatic && target.Parameters.Count == call.Parameters.Count && target.ReturnType.FullName == call.ReturnType.FullName, "Hook ABI mismatch " + call.Name);
                for (int i = 0; i < call.Parameters.Count; i++) Check(target.Parameters[i].ParameterType.FullName == call.Parameters[i].ParameterType.FullName, "Hook argument mismatch");
            }
        }
        foreach (TypeDefinition child in type.NestedTypes) VerifyABIType(child, runtime);
    }
}

static class CollectionSearch
{
    public static T Find<T>(this System.Collections.Generic.IEnumerable<T> values, Predicate<T> predicate) where T : class
    { foreach (T value in values) if (predicate(value)) return value; return null; }
}
