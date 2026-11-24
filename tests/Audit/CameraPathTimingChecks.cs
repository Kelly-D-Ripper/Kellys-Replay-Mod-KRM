using Mono.Cecil;

public static class CameraPathTimingChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"camera path timing "+name);}
        var director=mod.MainModule.Types.Single(type=>type.FullName=="ReplayMod.Director.ReplayDirector");
        var model=mod.MainModule.Types.Single(type=>type.FullName=="ReplayMod.Director.DirectorProject");
        var capture=director.Methods.Single(method=>method.Name=="CapturePathPoint");
        var store=model.Methods.Single(method=>method.Name=="TryStoreCameraPoint");
        bool Calls(MethodDefinition body,string type,string method)=>body.HasBody&&body.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName==type&&call.Name==method);
        Check("capture reads replay clock and uses tested point storage without stretching time",Calls(capture,"ReplayMod.Core.ReplayManager","GetCurrentVirtualTime")&&
            Calls(capture,model.FullName,"TryStoreCameraPoint")&&Calls(capture,director.FullName,"ResetCameraSmoothing")&&
            !Calls(capture,"System.Math","Max")&&!Calls(capture,"UnityEngine.Mathf","Max"));
        Check("storage orders new keys updates existing keys and preserves captured timestamps",store.HasBody&&
            store.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1",StringComparison.Ordinal)&&call.Name=="Insert")&&
            store.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1",StringComparison.Ordinal)&&call.Name=="set_Item")&&
            !store.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&instruction.Operand is FieldReference field&&field.Name=="time"));
        Console.WriteLine($"Camera path timing API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
}
