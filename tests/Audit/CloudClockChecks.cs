using Mono.Cecil;

public static class CloudClockChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod,string managedDirectory)
    {
        int checks=0,failures=0;
        void Check(string name,bool good)
        {checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"cloud clock "+name);}
        bool Fields(string type,params (string Name,string Type)[] fields)=>types.TryGetValue(type,out var definition)&&
            fields.All(expected=>definition.Fields.Any(f=>f.Name==expected.Name&&!f.IsStatic&&f.FieldType.FullName==expected.Type));
        MethodDefinition? NativeBody(string type,string name)=>types.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)==true;
        bool CallsGeneric(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.GetElementType().FullName==type&&m.Name==name)==true;
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(t=>t.FullName);
        MethodDefinition? Body(string type,string name)=>compiled.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        const string clock="ReplayMod.Core.ReplayCloudParticleClock",patch="ReplayMod.Patches.ReplayCloudParticleClockPatch";
        var clockBodies=compiled.Values.Where(t=>t.FullName==clock||t.FullName.StartsWith(clock+"/",StringComparison.Ordinal))
            .SelectMany(t=>t.Methods.Where(m=>m.HasBody)).ToArray();
        bool ClockCalls(string type,string name)=>clockBodies.Any(body=>Calls(body,type,name));
        bool ClockField(string type,string name)=>clockBodies.Any(body=>body.Body.Instructions.Any(i=>i.Operand is FieldReference field&&field.DeclaringType.FullName==type&&field.Name==name));
        bool ComparesCurrentCloudByReference()
        {
            // Release builds lower ReferenceEquals(level.cloudLayer, cloud) to
            // ldfld / ldarg.0 / ceq rather than retaining a method invocation.
            var instructions=Body(clock,"Ensure")?.Body.Instructions.Where(i=>i.OpCode.Code!=Mono.Cecil.Cil.Code.Nop).ToArray();
            if(instructions==null)return false;
            for(int n=0;n+2<instructions.Length;n++)
                if(instructions[n].Operand is FieldReference field&&field.DeclaringType.FullName=="LevelInfo"&&field.Name=="cloudLayer"&&
                    (instructions[n+1].OpCode.Code==Mono.Cecil.Cil.Code.Ldarg_0||instructions[n+1].Operand is ParameterDefinition argument&&argument.Name=="cloud")&&
                    (instructions[n+2].OpCode.Code==Mono.Cecil.Cil.Code.Ceq||instructions[n+2].Operand is MethodReference method&&method.DeclaringType.FullName=="System.Object"&&method.Name=="ReferenceEquals"))return true;
            return false;
        }

        Check("native near distant and fly-through systems retain module references",Fields("CloudLayer",("cloudSystem","UnityEngine.ParticleSystem"),("distantCloudSystem","UnityEngine.ParticleSystem"),("flyThroughSystem","UnityEngine.ParticleSystem"),
            ("main","UnityEngine.ParticleSystem/MainModule"),("distantMain","UnityEngine.ParticleSystem/MainModule"),("flyThroughMain","UnityEngine.ParticleSystem/MainModule")));
        Check("native cloud startup chooses scaled or editor-unscaled clocks",NativeBody("CloudLayer","Start")?.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="UnityEngine.ParticleSystem/MainModule"&&m.Name=="set_useUnscaledTime")==6&&
            Calls(NativeBody("CloudLayer","Start"),"UnityEngine.ParticleSystem","get_main"));
        Check("native render update emits clouds independently of expiry clock",Calls(NativeBody("CloudLayer","Update"),"UnityEngine.ParticleSystem","Emit")&&Calls(NativeBody("CloudLayer","Update"),"UnityEngine.Time","get_deltaTime"));
        using(var particles=AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory,"UnityEngine.ParticleSystemModule.dll")))
        {
            var particleTypes=AllTypes(particles.MainModule.Types).ToDictionary(t=>t.FullName);
            bool Method(string type,string name,string result,params string[] parameters)=>particleTypes.TryGetValue(type,out var definition)&&definition.Methods.Count(m=>m.Name==name&&!m.IsStatic&&m.ReturnType.FullName==result&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters))==1;
            Check("Unity particle clock capture restore and count APIs",Method("UnityEngine.ParticleSystem/MainModule","get_useUnscaledTime","System.Boolean")&&Method("UnityEngine.ParticleSystem/MainModule","set_useUnscaledTime","System.Void","System.Boolean")&&
                Method("UnityEngine.ParticleSystem","get_main","UnityEngine.ParticleSystem/MainModule")&&Method("UnityEngine.ParticleSystem","get_particleCount","System.Int32"));
        }
        var prefix=Body(patch,"Prefix");
        Check("compiled native cloud update patch invokes lifecycle correction",Calls(prefix,clock,"Ensure")&&compiled.TryGetValue(patch,out var patchType)&&
            patchType.CustomAttributes.Any(a=>a.AttributeType.FullName=="HarmonyLib.HarmonyPatch"&&a.ConstructorArguments.Any(v=>v.Value is TypeReference type&&type.FullName=="CloudLayer")&&a.ConstructorArguments.Any(v=>v.Value is string name&&name=="Update")));
        Check("compiled correction requires offline replay transition and exact level ownership",ClockCalls("ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&
            ClockCalls("ReplayMod.Core.ReplayManager","get_Playback")&&ClockCalls("ReplayMod.Misc.SceneHelper","get_IsTransitionPending")&&
            ClockField("GameManager","IsHeadless")&&ComparesCurrentCloudByReference());
        Check("compiled correction binds exactly the native three cloud references",new[]{"cloudSystem","distantCloudSystem","flyThroughSystem"}.All(name=>ClockField("CloudLayer",name))&&
            ClockCalls("UnityEngine.ParticleSystem","get_main")&&ClockCalls("UnityEngine.ParticleSystem/MainModule","get_useUnscaledTime")&&ClockCalls("UnityEngine.ParticleSystem/MainModule","set_useUnscaledTime"));
        Check("compiled clock exposes protected particle and original-state diagnostics",compiled.TryGetValue(clock,out var clockType)&&new[]{"ProtectedSystemCount","CurrentParticleCount","OriginalUnscaledSystemCount"}.All(name=>
            clockType.Properties.Any(p=>p.Name==name&&p.PropertyType.FullName=="System.Int32"&&p.GetMethod?.IsStatic==true)));
        Check("compiled cloud correction preserves native quality emission and simulation",clockBodies.Length>0&&clockBodies.All(body=>body.Body.Instructions.All(i=>i.Operand is not MethodReference m||
            !(m.DeclaringType.FullName=="GraphicsHelper"&&m.Name.StartsWith("set_",StringComparison.Ordinal))&&
            !(m.DeclaringType.FullName=="UnityEngine.PlayerPrefs"&&m.Name.StartsWith("Set",StringComparison.Ordinal))&&
            !(m.DeclaringType.FullName=="UnityEngine.ParticleSystem/MainModule"&&m.Name.StartsWith("set_",StringComparison.Ordinal)&&m.Name!="set_useUnscaledTime")&&
            !(m.DeclaringType.FullName=="UnityEngine.ParticleSystem/EmissionModule"&&m.Name.StartsWith("set_",StringComparison.Ordinal))&&
            !(m.DeclaringType.FullName=="UnityEngine.ParticleSystem"&&m.Name is "Emit" or "Simulate" or "Play" or "Stop" or "Clear"))));
        Check("compiled clock does not overwrite native cloud or graphics fields",clockBodies.All(body=>body.Body.Instructions.All(i=>
            i.OpCode.Code is not(Mono.Cecil.Cil.Code.Stfld or Mono.Cecil.Cil.Code.Stsfld)||i.Operand is not FieldReference field||field.DeclaringType.FullName is not("CloudLayer" or "GraphicsHelper" or "PlayerSettings"))));
        const string state="ReplayMod.Core.ReplayCloudClockState`1";
        var bind=Body(state,"Bind");var capture=Body(state,"Capture");var restore=Body(state,"Clear");
        Check("compiled state captures three original clocks and restores stored flags",bind?.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="Capture"&&m.DeclaringType.GetElementType().FullName==state)==3&&
            CallsGeneric(capture,"System.Func`2","Invoke")&&
            restore?.Body.Instructions.Any(i=>i.Operand is FieldReference field&&field.Name=="Original"&&field.FieldType.FullName=="System.Boolean")==true&&
            CallsGeneric(restore,"System.Action`2","Invoke")&&restore?.Body.ExceptionHandlers.Any(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Finally)==true);
        Check("compiled repeated updates reuse binding and scene release restores clocks",CallsGeneric(Body(clock,"Ensure"),state,"Matches")&&CallsGeneric(Body(clock,"Ensure"),state,"Bind")&&
            CallsGeneric(Body(clock,"Clear"),state,"Clear")&&Calls(Body("ReplayMod.Core.UnitController","ReleaseOwnedPresentationObjects"),clock,"Clear"));
        Check("compiled correction uses cached references and permits native update after failure",clockBodies.All(body=>body.Body.Instructions.All(i=>i.Operand is not MethodReference m||m.Name is not("FindObjectsByType" or "FindObjectsOfType" or "GetComponentsInChildren")))&&
            Body(clock,"Ensure")?.Body.ExceptionHandlers.Any(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Catch)==true&&prefix?.ReturnType.FullName=="System.Void");
        Console.WriteLine($"Cloud clock API/integration audit: {checks} groups, {failures} failures");
        return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {foreach(var type in types){yield return type;foreach(var child in AllTypes(type.NestedTypes))yield return child;}}
}
