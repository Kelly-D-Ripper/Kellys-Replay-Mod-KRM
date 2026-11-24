using Mono.Cecil;

public static class DamageRestoreContractChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> native,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"damage restore "+name);}
        bool Field(string type,string name,string fieldType)=>native.TryGetValue(type,out var value)&&value.Fields.Any(f=>f.Name==name&&!f.IsStatic&&f.FieldType.FullName==fieldType);
        bool Method(string type,string name,string result,params string[] args)=>native.TryGetValue(type,out var value)&&
            value.Methods.Count(m=>m.Name==name&&!m.IsStatic&&m.ReturnType.FullName==result&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(args))==1;
        Check("native absolute HP and optional renderer binding contract",Field("UnitPart","hitPoints","System.Single")&&Field("UnitPart","damageMaterial","DamageMaterial")&&
            Field("DamageMaterial","renderers","System.Collections.Generic.List`1<UnityEngine.Renderer>"));
        Check("native property-block overloads preserve renderer and material scopes",Method("UnityEngine.Renderer","GetPropertyBlock","System.Void","UnityEngine.MaterialPropertyBlock")&&
            Method("UnityEngine.Renderer","GetPropertyBlock","System.Void","UnityEngine.MaterialPropertyBlock","System.Int32")&&
            Method("UnityEngine.Renderer","SetPropertyBlock","System.Void","UnityEngine.MaterialPropertyBlock")&&
            Method("UnityEngine.Renderer","SetPropertyBlock","System.Void","UnityEngine.MaterialPropertyBlock","System.Int32")&&
            Method("UnityEngine.Renderer","GetSharedMaterials","System.Void","System.Collections.Generic.List`1<UnityEngine.Material>"));
        Check("native reusable property-block storage and health shader setter",Method("UnityEngine.MaterialPropertyBlock","Clear","System.Void")&&
            Method("UnityEngine.MaterialPropertyBlock","SetFloat","System.Void","System.Int32","System.Single")&&
            Method("UnityEngine.MaterialPropertyBlock","get_isEmpty","System.Boolean"));
        var types=AllTypes(mod.MainModule.Types).ToDictionary(t=>t.FullName);
        const string helper="ReplayMod.Core.ReplayPartHealthPresentation",policy="ReplayMod.Core.ReplayDamageStatePolicy";
        MethodDefinition? Body(string type,string name)=>types.TryGetValue(type,out var value)?value.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference target&&target.DeclaringType.FullName==type&&target.Name==name)==true;
        var restore=Body(helper,"TryRestoreSnapshot");var refresh=Body(helper,"RefreshDamageShader");var visual=Body(helper,"ApplyHealth");
        Check("compiled snapshots validate finite data before classifying zero impulses",Calls(restore,policy,"Valid")&&Calls(restore,policy,"IsAbsoluteSnapshot")&&
            restore?.Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&i.Operand is MethodReference target&&target.DeclaringType.FullName=="System.IO.InvalidDataException")==true);
        Check("compiled HP snapshot never invokes live damage or material instancing",types.TryGetValue(helper,out var helperType)&&helperType.Methods.Where(m=>m.HasBody).All(m=>m.Body.Instructions.All(i=>
            i.Operand is not MethodReference target||target.Name is not ("ApplyDamage" or "InvokeDamage" or "SpawnFragments" or "Instantiate" or "get_material" or "get_materials")))&&
            restore?.Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.FullName=="UnitPart"&&f.Name=="hitPoints")==true);
        Check("compiled health shader uses reused blocks and guarded optional visuals",Calls(visual,"UnityEngine.Renderer","GetPropertyBlock")&&Calls(visual,"UnityEngine.Renderer","SetPropertyBlock")&&
            Calls(visual,"UnityEngine.Renderer","GetSharedMaterials")&&Calls(visual,"UnityEngine.MaterialPropertyBlock","SetFloat")&&
            helperType!=null&&helperType.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Count(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Newobj&&i.Operand is MethodReference target&&target.DeclaringType.FullName=="UnityEngine.MaterialPropertyBlock")==1&&
            refresh?.Body.ExceptionHandlers.Any(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Catch&&h.CatchType.FullName=="System.Exception")==true);
        var controller=types.TryGetValue("ReplayMod.Core.UnitController",out var controllerType)?controllerType.Methods.SingleOrDefault(m=>m.Name=="Execute"&&m.HasBody&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="ReplayMod.Events.ConcreteEvents.PartDamageEvent"):null;
        int CallIndex(string type,string method)=>controller==null?-1:controller.Body.Instructions.ToList().FindIndex(i=>i.Operand is MethodReference target&&target.DeclaringType.FullName==type&&target.Name==method);
        int helperIndex=CallIndex(helper,"TryRestoreSnapshot"),nativeIndex=CallIndex("UnitPart","ApplyDamage"),refreshIndex=CallIndex(helper,"RefreshDamageShader");
        Check("compiled controller routes snapshots and refreshes shader after later real impacts",helperIndex>=0&&nativeIndex>helperIndex&&refreshIndex>nativeIndex&&controller!=null&&
            controller.Body.Instructions.Skip(helperIndex+1).Take(nativeIndex-helperIndex-1).Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ret));
        Console.WriteLine($"Damage restore API/integration audit: {checks} groups, {failures} failures");
        return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> values)
    {foreach(var value in values){yield return value;foreach(var child in AllTypes(value.NestedTypes))yield return child;}}
}
