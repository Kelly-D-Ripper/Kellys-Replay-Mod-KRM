using Mono.Cecil;

public static class StartupControlChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool pass){checks++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+"startup/control "+name);}
        bool Method(string type,string name,string result,params string[] parameters)=>types.TryGetValue(type,out var definition)&&definition.Methods.Count(m=>m.Name==name&&m.ReturnType.FullName==result&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters))==1;
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(t=>t.FullName);
        Check("native cursor field injection matches static visible boolean",types.TryGetValue("CursorManager",out var cursor)&&cursor.Fields.Count(f=>f.Name=="visible"&&f.IsStatic&&f.FieldType.FullName=="System.Boolean")==1);
        MethodDefinition? Body(string type,string method)=>compiled.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(m=>m.Name==method&&m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.GetElementType().FullName==type&&m.Name==name)==true;
        Check("native manager persistence and background properties",Method("UnityEngine.Object","set_hideFlags","System.Void","UnityEngine.HideFlags")&&Method("UnityEngine.Object","DontDestroyOnLoad","System.Void","UnityEngine.Object")&&Method("UnityEngine.Application","set_runInBackground","System.Void","System.Boolean"));
        Check("native first party menu anchor and startup",types.TryGetValue("MainMenu",out var menu)&&menu.Fields.Count(f=>f.Name=="missionsButton"&&f.FieldType.FullName=="UnityEngine.UI.Button")==1&&Method("MainMenu","Start","System.Void"));
        Check("native menu readiness and overlay ownership",menu!=null&&menu.Fields.Any(f=>f.Name=="overlayMenuLayer"&&f.FieldType.FullName=="UnityEngine.Transform")&&
            Method("MainMenu","get_State","MainMenu/LoadingState")&&types.TryGetValue("MainMenu/LoadingState",out var loading)&&loading.Fields.Any(f=>f.Name=="Loaded"&&f.HasConstant));
        var awake=Body("ReplayMod.Plugin","Awake");
        var inputs=Body("ReplayMod.Core.ReplayManager","HandleInputs");
        Check("studio shortcut precedes ordinary action and transition gates",inputs!=null&&
            CallOffset(inputs,"ReplayMod.MainMenu","get_CanToggleStudio")<CallOffset(inputs,"ReplayMod.MainMenu","get_BlockHotkeys")&&
            CallOffset(inputs,"ReplayMod.MainMenu","get_CanToggleStudio")<CallOffset(inputs,"ReplayMod.Core.ReplayManager","get_IsTransitioning")&&
            inputs.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="ReplayMod.MainMenu"&&m.Name=="Toggle")==1);
        var cursorPatch=Body("ReplayMod.Patches.StudioCursorPatch","Postfix");
        Check("studio cursor override is scoped without overwriting native flags or forced hiding",cursorPatch!=null&&
            cursorPatch.Parameters.Any(p=>p.Name=="___visible"&&p.ParameterType.FullName=="System.Boolean&")&&
            Calls(cursorPatch,"ReplayMod.UI.TrailerSuiteUi","get_IsVisible")&&Calls(cursorPatch,"UnityEngine.Cursor","set_visible")&&
            Calls(cursorPatch,"UnityEngine.Cursor","set_lockState")&&
            !cursorPatch.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.DeclaringType.FullName=="CursorManager"&&(f.Name=="flags"||f.Name=="forceHidden")));
        Check("client-only guard occurs before owned runtime allocation",awake!=null&&CallOffset(awake,"UnityEngine.Application","get_isBatchMode")<CallOffset(awake,"UnityEngine.GameObject",".ctor")&&CallOffset(awake,"UnityEngine.SystemInfo","get_graphicsDeviceType")<CallOffset(awake,"UnityEngine.GameObject",".ctor"));
        Check("runtime root has independent lifetime and dispatch",Calls(awake,"UnityEngine.Object","set_hideFlags")&&Calls(awake,"UnityEngine.Object","DontDestroyOnLoad")&&Calls(Body("ReplayMod.ReplayRuntimeHost","Update"),"ReplayMod.Plugin","DrainActions")&&Calls(Body("ReplayMod.ReplayRuntimeHost","Start"),"ReplayMod.Misc.ReflectionCache","Init"));
        Check("diagnostics shutdown follows owned runtime destruction",Calls(Body("ReplayMod.ReplayRuntimeHost","OnDestroy"),"ReplayMod.Diagnostics.ReplayDiagnostics","Shutdown"));
        Check("test control enabled only by explicit default-false configuration",Calls(Body("ReplayMod.Diagnostics.ReplayBackgroundControl","Awake"),"ReplayMod.ConfigManager","get_EnableBackgroundControl")&&Calls(Body("ReplayMod.Diagnostics.ReplayBackgroundControl","OnDestroy"),"UnityEngine.Application","set_runInBackground"));
        Check("test control has no listener or live mission command API",compiled.Values.Where(t=>t.FullName.StartsWith("ReplayMod.Diagnostics.ReplayBackgroundControl",StringComparison.Ordinal)).SelectMany(t=>t.Methods.Where(m=>m.HasBody)).SelectMany(m=>m.Body.Instructions).All(i=>i.Operand is not MethodReference m||(!m.DeclaringType.FullName.StartsWith("System.Net.",StringComparison.Ordinal)&&!(m.DeclaringType.FullName=="UnityEngine.Application"&&m.Name=="Quit"))));
        Check("native delayed join routine exists with expected return",Method("NuclearOption.Networking.Player","WaitShowJoinMenu","Cysharp.Threading.Tasks.UniTask"));
        foreach(var target in new[]{(Type:"TargetDetector",Name:"RepeatSearch",Calls:2),(Type:"LaserDesignator",Name:"LaseTargets",Calls:1),(Type:"Airbase",Name:"WaitRepair",Calls:1)})
        {
            var original=types.TryGetValue(target.Type,out var owner)?owner.Methods.SingleOrDefault(m=>m.Name==target.Name&&m.Parameters.Count==0):null;
            var stateName=original?.CustomAttributes.FirstOrDefault(a=>a.AttributeType.FullName=="System.Runtime.CompilerServices.AsyncStateMachineAttribute")?.ConstructorArguments.FirstOrDefault().Value as TypeReference;
            var state=stateName!=null&&types.TryGetValue(stateName.FullName,out var found)?found:null;
            var move=state?.Methods.SingleOrDefault(m=>m.Name=="MoveNext"&&m.HasBody);
            bool layout=state!=null&&state.Fields.Count(f=>f.Name=="<>4__this"&&f.FieldType.FullName==target.Type)==1&&
                move!=null&&move.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Cysharp.Threading.Tasks.UniTask"&&m.Name=="Delay"&&m.Parameters.Count==5&&m.Parameters[0].ParameterType.FullName=="System.Int32")==target.Calls;
            Check("native replay lifetime delay "+target.Type+"."+target.Name,layout);
        }
        Check("lifetime delay replacement is scoped to replay and native token availability",Calls(Body("ReplayMod.Patches.ReplaySceneDelayCancellationPatch","Delay"),"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&Calls(Body("ReplayMod.Patches.ReplaySceneDelayCancellationPatch","Delay"),"System.Threading.CancellationToken","get_CanBeCanceled"));
        Console.WriteLine($"Startup/control API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static int CallOffset(MethodDefinition body,string type,string name)=>body.Body.Instructions.FirstOrDefault(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)?.Offset??int.MaxValue;
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types){foreach(var type in types){yield return type;foreach(var child in AllTypes(type.NestedTypes))yield return child;}}
}
