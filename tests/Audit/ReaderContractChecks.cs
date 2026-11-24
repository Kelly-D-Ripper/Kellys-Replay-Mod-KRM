using Mono.Cecil;
using Mono.Cecil.Cil;

public static class ReaderContractChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool ok){checks++;if(!ok)failures++;Console.WriteLine((ok?"PASS ":"FAIL ")+"replay input reader "+name);}
        var types=mod.MainModule.Types.ToDictionary(t=>t.FullName);
        MethodDefinition? M(string type,string name)=>types.TryGetValue(type,out var t)?t.Methods.FirstOrDefault(x=>x.Name==name&&x.HasBody):null;
        bool NewReader(MethodDefinition? m)=>m?.Body.Instructions.Any(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference r&&r.DeclaringType.FullName=="ReplayMod.Core.ReplayFrameReader")==true;
        Check("framed and rolling events use independent readers",NewReader(M("ReplayMod.Core.ReplayRecordTransport","DecodeBody"))&&NewReader(M("ReplayMod.Core.RollingReplayBuffer","Decode")));
        var read=M("ReplayMod.Events.ConcreteEvents.UpdateInputsEvent","Read");
        Check("framed input has an explicit independent path",read?.Body.Instructions.Any(i=>i.OpCode==OpCodes.Isinst&&i.Operand is TypeReference t&&t.FullName=="ReplayMod.Core.ReplayFrameReader")==true);
        var helpers=types["ReplayMod.Misc.EventsHelper"];
        Check("legacy histories do not use conditional weak tables",!helpers.Fields.Any(f=>f.FieldType.FullName.Contains("ConditionalWeakTable"))&&helpers.NestedTypes.Any(t=>t.Name=="ReaderHistory"&&t.Fields.Any(f=>f.Name=="reader"&&f.FieldType.FullName=="System.WeakReference")));
        Console.WriteLine($"Replay input reader audit: {checks} groups, {failures} failures");return(checks,failures);
    }
}
