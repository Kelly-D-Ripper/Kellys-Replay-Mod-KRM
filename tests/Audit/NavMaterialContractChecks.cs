using Mono.Cecil;

public static class NavMaterialContractChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool pass){checks++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+"navigation material "+name);}
        bool Calls(MethodDefinition? method,string type,string name)=>method?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)==true;
        var compiled=mod.MainModule.Types.ToDictionary(t=>t.FullName);
        MethodDefinition? Method(string type,string name)=>compiled.TryGetValue(type,out var t)?t.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        Check("native initializer and serialized fields",types.TryGetValue("NavLights/NavLight",out var native)&&new[]{"renderer","baseMaterial"}.All(n=>native.Fields.Any(f=>f.Name==n))&&Calls(native.Methods.FirstOrDefault(m=>m.Name=="Initialize"&&m.HasBody),"UnityEngine.Renderer","get_material"));
        var prefix=Method("ReplayMod.Patches.ReplayNavMaterialCleanupPatch","Prefix");
        Check("prefix observes existing material only in replay",Calls(prefix,"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&Calls(prefix,"UnityEngine.Renderer","get_sharedMaterial"));
        var postfix=Method("ReplayMod.Patches.ReplayNavMaterialCleanupPatch","Postfix");
        Check("postfix claims only guarded material",Calls(postfix,"ReplayMod.Core.ReplayNavMaterialPolicy","ShouldTrack")&&Calls(postfix,"ReplayMod.Patches.ReplayNavMaterialOwner","Add"));
        var release=Method("ReplayMod.Patches.ReplayNavMaterialLease","Release");
        Check("lease guards detached children before destroy",Calls(release,"ReplayMod.Core.ReplayNavMaterialPolicy","CanRelease")&&Calls(release,"UnityEngine.Transform","IsChildOf")&&Calls(release,"UnityEngine.Object","Destroy"));
        Console.WriteLine($"Navigation material API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
}
