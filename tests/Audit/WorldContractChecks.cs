using Mono.Cecil;

public static class WorldContractChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod,string managedDirectory)
    {
        int checks=0,failures=0;
        void Check(string name,bool good)
        {checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"world "+name);}
        bool Fields(string type,bool isStatic,params (string Name,string Type)[] fields) => types.TryGetValue(type,out var definition) &&
            fields.All(expected=>definition.Fields.Any(f=>f.Name==expected.Name && f.IsStatic==isStatic && f.FieldType.FullName==expected.Type));
        bool Method(string type,string name,string result,params string[] parameters) => types.TryGetValue(type,out var definition) &&
            definition.Methods.Count(m=>m.Name==name && !m.IsStatic && m.ReturnType.FullName==result && m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters))==1;
        Check("hangar native owner and stable hierarchy",Fields("Hangar",false,("attachedUnit","Unit"),("doors","HangarDoor[]"),("lights","HangarLighting[]")));
        Check("hangar authoritative sampled state",Fields("Hangar",false,("doorState","Hangar/DoorState"),("doorCurrentSpeed","System.Single"),("available","System.Boolean"),("selfDisabled","System.Boolean")) &&
            Fields("Hangar/DoorState",false,("opening","System.Boolean"),("openAmount","System.Single")));
        Check("hangar native state setters",Method("Hangar","set_NetworkdoorState","System.Void","Hangar/DoorState") && Method("Hangar","set_Networkavailable","System.Void","System.Boolean"));
        Check("hangar asynchronous ownership cancellation",Fields("Hangar",false,("cancelMoveDoors","System.Threading.CancellationTokenSource"),("loopSource","UnityEngine.AudioSource")) &&
            Method("Hangar","OpenDoors","Cysharp.Threading.Tasks.UniTask") && Method("Hangar","CloseDoors","Cysharp.Threading.Tasks.UniTask"));
        Check("hangar direct geometry and lighting application",Method("HangarDoor","Move","System.Void","System.Single") && Method("HangarLighting","Enable","System.Void","System.Boolean"));
        Check("level recorded environment field contract",Fields("LevelInfo",false,("timeOfDay","System.Single"),("timeFactor","System.Single"),("conditions","System.Single"),("cloudHeight","System.Single"),
            ("windVelocity","UnityEngine.Vector3"),("windOffset","UnityEngine.Vector3"),("windSpeed","System.Single"),("windTurbulence","System.Single"),("windMainHeading","System.Single"),("windRandomArc","System.Single"),
            ("moonPhase","System.Nullable`1<System.Single>"),("cloudLayer","CloudLayer")));
        Check("level weather native presentation setters",Method("LevelInfo","SetWindSpeed","System.Void","System.Single") && Method("LevelInfo","SetWindHeading","System.Void","System.Single") &&
            Method("LevelInfo","GetWindHeading","System.Single") && Method("LevelInfo","SetWindTurbulence","System.Void","System.Single") && Method("LevelInfo","SetMoonPhase","System.Void","System.Single"));
        Check("level recorded state property setters",new[]{("set_Networkconditions","System.Single"),("set_NetworkcloudHeight","System.Single"),("set_NetworktimeOfDay","System.Single"),
            ("set_NetworkwindSpeed","System.Single"),("set_NetworkwindVelocity","UnityEngine.Vector3"),("set_NetworkmoonPhase","System.Nullable`1<System.Single>")}.All(x=>Method("LevelInfo",x.Item1,"System.Void",x.Item2)));
        Check("level lighting and cloud shadow phase presentation",Method("LevelInfo","UpdateTimeOfDayLighting","System.Void","System.Boolean") && Method("LevelInfo","UpdateSkybox","System.Void","System.Boolean") &&
            Method("LevelInfo","SetCookie","System.Void","UnityEngine.Texture2D","System.Single","UnityEngine.Vector2"));
        Check("cloud actual scroll phase and native material ownership",Fields("CloudLayer",false,("windDisplacement","UnityEngine.Vector2"),("layerMaterial","UnityEngine.Material"),("weatherSets","WeatherSet[]"),
            ("currentWeatherSet","WeatherSet"),("densityMapScale","System.Single")) && Fields("CloudLayer",true,("id_CloudsOriginOffset","System.Int32")) && Fields("WeatherSet",false,("cookies","UnityEngine.Texture2D[]")));
        Check("cloud native weather refresh",Method("CloudLayer","UpdateWeatherSets","System.Void") && types.TryGetValue("Datum",out var datum) &&
            datum.Properties.Any(p=>p.Name=="originPosition" && p.PropertyType.FullName=="UnityEngine.Vector3" && p.GetMethod?.IsStatic==true));
        using(var uniTask=AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory,"UniTask.dll")))
        {
            var task=uniTask.MainModule.Types.SingleOrDefault(t=>t.FullName=="Cysharp.Threading.Tasks.UniTask");
            Check("native async completed value for owned door suppression",task!=null && (task.Fields.Any(f=>f.Name=="CompletedTask" && f.IsStatic && f.FieldType.FullName==task.FullName) ||
                task.Properties.Any(p=>p.Name=="CompletedTask" && p.PropertyType.FullName==task.FullName && p.GetMethod?.IsStatic==true)));
        }
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(t=>t.FullName);
        MethodDefinition? Body(string type,string method)=>compiled.TryGetValue(type,out var definition)?definition.Methods.FirstOrDefault(m=>m.Name==method && m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string method)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.DeclaringType.FullName==type && m.Name==method)==true;
        bool TypeCalls(string caller,string type,string method)=>compiled.TryGetValue(caller,out var definition) && definition.Methods.Any(m=>m.HasBody && Calls(m,type,method));
        const string recorder="ReplayMod.Core.Recorder",controller="ReplayMod.Core.UnitController",world="ReplayMod.Core.ReplayWorldPlayback";
        Check("deferred registration is connected to capture, retry, and stop",Calls(Body(recorder,"HandleUnitRegister"),recorder,"RegisterUnitWhenReady") &&
            Calls(Body(recorder,"HandleUnitRegister"),recorder,"HandleRegistrationFailure") &&
            Calls(Body(recorder,"ProcessPendingUnitRegistrations"),recorder,"RegisterUnitWhenReady") &&
            Calls(Body(recorder,"Update"),recorder,"ProcessPendingUnitRegistrations") &&
            compiled.Values.Where(t=>t.DeclaringType?.FullName==recorder && t.Name.StartsWith("<StopRecordingCore>",StringComparison.Ordinal)).Any(t=>
                t.Methods.Any(m=>m.HasBody && Calls(m,recorder,"ProcessPendingUnitRegistrations"))));
        var registration=Body(recorder,"RegisterUnitWhenReady");
        int CallAt(MethodDefinition? body,string owner,string name)=>body?.Body.Instructions.ToList().FindIndex(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==owner&&m.Name==name)??-1;
        var rollingSave=compiled.Values.Where(t=>t.DeclaringType?.FullName==recorder&&t.Name.StartsWith("<SaveRolling>",StringComparison.Ordinal)).SelectMany(t=>t.Methods).FirstOrDefault(m=>m.Name=="MoveNext"&&m.HasBody);
        Check("recent clip resolves pending actors before snapshot with a bounded unscaled wait",CallAt(rollingSave,recorder,"ProcessPendingUnitRegistrations")>=0&&
            CallAt(rollingSave,recorder,"ProcessPendingUnitRegistrations")<CallAt(rollingSave,recorder,"BeginRollingWrite")&&
            Calls(Body(recorder,"BeginRollingWrite"),"ReplayMod.Core.RollingReplayBuffer","Snapshot")&&
            Calls(rollingSave,"UnityEngine.Time","get_realtimeSinceStartup")&&Calls(rollingSave,"Cysharp.Threading.Tasks.UniTask","NextFrame"));
        var rollingStop=compiled.Values.Where(t=>t.DeclaringType?.FullName==recorder&&t.Name.StartsWith("<StopRecordingCore>",StringComparison.Ordinal)).SelectMany(t=>t.Methods).FirstOrDefault(m=>m.Name=="MoveNext"&&m.HasBody);
        Check("stopping a waiting recent clip starts its write before discarding the buffer",CallAt(rollingStop,recorder,"BeginRollingWrite")>=0&&
            CallAt(rollingStop,recorder,"BeginRollingWrite")<CallAt(rollingStop,"ReplayMod.Core.RollingReplayBuffer","Dispose")&&
            Calls(Body(recorder,"BeginRollingWrite"),"ReplayMod.Core.ReplayRecordingRecovery","TrackPendingWrite"));
        Check("early aircraft events are buffered and emitted after spawn",new[]{"HandleSetGear","HandleDamage","HandleEjection","HandleSetActiveStation","HandleWeaponManagerFire"}.All(name=>Calls(Body(recorder,name),recorder,"QueueEventForUnit")) &&
            Calls(Body(recorder,"QueueEventForUnit"),"ReplayMod.Core.RollingReplayBuffer","Encode") &&
            Calls(Body(recorder,"FlushPendingUnitEvents"),"ReplayMod.Core.RollingReplayBuffer","Decode") &&
            CallAt(registration,recorder,"QueueEvent")>=0 && CallAt(registration,recorder,"FlushPendingUnitEvents")>CallAt(registration,recorder,"QueueEvent"));
        Check("compiled recording registration and removal bridge",Calls(Body(recorder,"RegisterUnitWhenReady"),recorder,"RecordWorldActor") && Calls(Body(recorder,"HandleUnitUnregister"),recorder,"ForgetRecordingWorldActor"));
        Check("compiled cached periodic world capture bridge",Calls(Body(recorder,"Update"),recorder,"RecordWorldState") && Calls(Body(recorder,"RecordWorldState"),world,"CaptureEnvironment") &&
            Body(recorder,"RecordWorldState")?.Body.Instructions.All(i=>i.Operand is not MethodReference m || m.Name is not ("FindObjectsByType" or "FindObjectsOfType" or "GetComponentsInChildren"))==true);
        Check("compiled world recording starts with an initial global sample",compiled.Values.Where(t=>t.DeclaringType?.FullName==recorder && t.Name.StartsWith("<StartRecording>",StringComparison.Ordinal)).Any(t=>
            t.Methods.Any(m=>m.HasBody && Calls(m,recorder,"RecordWorldState") && Calls(m,recorder,"ClearRecordingWorld"))));
        Check("compiled replay world render and lifecycle release bridge",Calls(Body(controller,"Update"),controller,"UpdateWorld") && Calls(Body(controller,"DespawnUnit"),controller,"ForgetWorldActor") &&
            Calls(Body(controller,"ReleaseOwnedPresentationObjects"),controller,"ClearWorldPlayback"));
        var applyDoor=Body(world,"ApplyDoor");
        Check("compiled replay doors use sampled geometry without starting native animation tasks",Calls(applyDoor,"HangarDoor","Move") && !Calls(applyDoor,"HangarDoor","Initialize") && !Calls(applyDoor,"Hangar","OpenDoors") && !Calls(applyDoor,"Hangar","CloseDoors"));
        Check("compiled environment override restores current recorded baseline",Calls(Body("ReplayMod.Director.PresentationOverrides","RestoreEnvironment"),world,"RestoreRecordedEnvironment") &&
            Calls(Body(world,"Render"),world,"get_CustomEnvironment") && Calls(Body(world,"RestoreRecordedEnvironment"),world,"OwnsEnvironment"));
        Check("compiled native world patches require recorded ownership",TypeCalls("ReplayMod.Patches.ReplayWorldSimulationPatch",world,"OwnsEnvironment") &&
            TypeCalls("ReplayMod.Patches.ReplayWorldHangarOpenPatch",world,"OwnsHangar") && TypeCalls("ReplayMod.Patches.ReplayWorldHangarClosePatch",world,"OwnsHangar"));
        Console.WriteLine($"World API/integration audit: {checks} groups, {failures} failures");
        return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach(var type in types){yield return type;foreach(var child in AllTypes(type.NestedTypes))yield return child;}
    }
}
