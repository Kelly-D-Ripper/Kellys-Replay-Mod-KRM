using Mono.Cecil;
using Mono.Cecil.Cil;

public static class CountermeasureContractChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good)
        {checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"countermeasure "+name);}
        bool Field(string type,string name,string value,bool isStatic=false,bool isPrivate=false)=>
            types.TryGetValue(type,out var definition)&&definition.Fields.Count(field=>field.Name==name&&
                field.FieldType.FullName==value&&field.IsStatic==isStatic&&(!isPrivate||field.IsPrivate))==1;
        bool Fields(string type,bool isPrivate,params (string Name,string Type)[] fields)=>
            fields.All(field=>Field(type,field.Name,field.Type,false,isPrivate));
        MethodDefinition? Native(string type,string name)=>types.TryGetValue(type,out var definition)?
            definition.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(instruction=>
            instruction.Operand is MethodReference method&&method.DeclaringType.GetElementType().FullName==type&&method.Name==name)==true;

        Check("native observed flare launch signature",types.TryGetValue("IRFlare",out var flare)&&
            flare.Methods.Count(method=>method.Name=="LaunchFlare"&&!method.IsStatic&&method.IsPublic&&
                method.ReturnType.FullName=="System.Void"&&method.Parameters.Select(parameter=>parameter.ParameterType.FullName)
                    .SequenceEqual(new[]{"Aircraft","UnityEngine.Transform","UnityEngine.Vector3"}))==1);
        Check("native private observed velocity and burn time",Fields("IRFlare",true,
            ("velocity","UnityEngine.Vector3"),("burnTime","System.Single")));
        Check("native private flare and smoke particle references",Fields("IRFlare",true,
            ("flareParticles","UnityEngine.ParticleSystem"),("smokeParticles","UnityEngine.ParticleSystem")));
        Check("native private emission and minimum speed",Fields("IRFlare",true,
            ("emitFrequency","System.Single"),("minSpeed","System.Single")));
        Check("native private light and source aircraft references",Fields("IRFlare",true,
            ("flareLight","UnityEngine.Light"),("aircraft","Aircraft")));
        Check("native ejector prefab and exact ejection point array",Fields("FlareEjector",true,
            ("flarePrefab","UnityEngine.GameObject"),("ejectionPoints","FlareEjector/EjectionPoint[]")));
        Check("native ejection point transform and owning part",Fields("FlareEjector/EjectionPoint",false,
            ("transform","UnityEngine.Transform"),("part","UnitPart")));
        Check("native countermeasure aircraft identity",Field("Countermeasure","aircraft","Aircraft"));
        Check("native aircraft definition prefab",Field("UnitDefinition","unitPrefab","UnityEngine.GameObject"));
        Check("native static encyclopedia definition lookup",Field("Encyclopedia","Lookup",
            "System.Collections.Generic.Dictionary`2<System.String,UnitDefinition>",true));
        Check("native launch creates gameplay infrared source",Calls(Native("IRFlare","LaunchFlare"),"IRSource",".ctor")&&
            Calls(Native("IRFlare","LaunchFlare"),"Unit","AddIRSource"));
        Check("native common ejector spawn reaches observed launch",types.TryGetValue("FlareEjector",out var ejector)&&
            AllTypes(ejector.NestedTypes).SelectMany(type=>type.Methods).Any(method=>method.HasBody&&
                Calls(method,"IRFlare","LaunchFlare")&&Calls(method,"Spawner","SpawnLocal")));
        Check("native observed chaff launch and private pose lifetime fields",types.TryGetValue("RadarChaff",out var chaff)&&
            chaff.Methods.Count(method=>method.Name=="LaunchChaff"&&!method.IsStatic&&method.IsPublic&&
                method.ReturnType.FullName=="System.Void"&&method.Parameters.Select(parameter=>parameter.ParameterType.FullName)
                    .SequenceEqual(new[]{"Aircraft","UnityEngine.Transform","UnityEngine.Vector3"}))==1&&
            Fields("RadarChaff",true,("velocity","UnityEngine.Vector3"),("chaffTime","System.Single"),("aircraft","Aircraft")));
        Check("native chaff prefab and exact ejection point identity",Fields("ChaffEjector",true,
            ("chaffPrefab","UnityEngine.GameObject"),("ejectionPoints","ChaffEjector/EjectionPoint[]"))&&
            Fields("ChaffEjector/EjectionPoint",false,("transform","UnityEngine.Transform"),("part","UnitPart")));
        Check("native chaff launch registers a gameplay target",Calls(Native("RadarChaff","LaunchChaff"),"Unit","AddRadarChaff"));

        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        MethodDefinition? Body(string type,string name,string? parameter=null)=>compiled.TryGetValue(type,out var definition)?
            definition.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody&&
                (parameter==null||method.Parameters.Count==1&&method.Parameters[0].ParameterType.FullName==parameter)):null;
        IEnumerable<MethodDefinition> Family(string type,string name)=>compiled.Values.Where(definition=>
            definition.FullName==type||definition.FullName.StartsWith(type+"/<"+name+">",StringComparison.Ordinal))
                .SelectMany(definition=>definition.Methods.Where(method=>method.HasBody&&
                    (definition.FullName!=type||method.Name==name)));
        bool FamilyCalls(string owner,string name,string target,string called)=>Family(owner,name).Any(method=>Calls(method,target,called));
        bool ReadField(MethodDefinition? method,string owner,string name)=>method?.Body.Instructions.Any(instruction=>
            instruction.Operand is FieldReference field&&field.DeclaringType.GetElementType().FullName==owner&&field.Name==name)==true;
        bool Constant(string type,string name,double maximum)=>compiled.TryGetValue(type,out var definition)&&
            definition.Fields.Any(field=>field.Name==name&&field.IsStatic&&field.HasConstant&&
                Convert.ToDouble(field.Constant)>0&&Convert.ToDouble(field.Constant)<=maximum);
        const string timeline="ReplayMod.Data.CountermeasureTimeline",state="ReplayMod.Core.ReplayCountermeasureState",
            value="ReplayMod.Events.ConcreteEvents.CountermeasureEvent";
        Check("compiled bounded trajectory and active state capacities",Constant(timeline,"MaxActive",512)&&
            Constant(timeline,"MaxHistory",320)&&Constant(timeline,"TrailSeconds",15)&&
            Calls(Body(timeline,"Insert"),"System.Collections.Generic.List`1","RemoveRange"));
        Check("compiled state validates snapshots and releases pooled producer events",
            Calls(Body(state,"Add"),value,"HasValidPayload")&&Calls(Body(state,"Add"),timeline,".ctor")&&
            Calls(Body(state,"Add"),"ReplayMod.Events.ReplayEventFactory","Return")&&
            Calls(Body(state,"Add"),state,"Expire"));
        Check("compiled independent checkpoint clone and lifetime expiry",
            Calls(Body(state,"CloneTo"),timeline,"Clone")&&Calls(Body(state,"Expire"),timeline,"get_ExpiresAt")&&
            Calls(Body(state,"Expire"),"System.Collections.Generic.Dictionary`2","Remove")&&
            Calls(Body(state,"Clear"),"System.Collections.Generic.Dictionary`2","Clear"));
        Check("compiled event codec validates and owns independent history",
            Calls(Body(value,"Read"),value,"Validate")&&Calls(Body(value,"Write"),value,"Validate")&&
            Calls(Body(value,"CopyTo"),"System.Array","Clone")&&
            Calls(Body(value,"Execute"),"ReplayMod.Core.UnitController","Execute"));

        const string recorder="ReplayMod.Core.Recorder",controller="ReplayMod.Core.UnitController",
            visual="ReplayMod.Core.ReplayCountermeasureVisual",identity="ReplayMod.Core.ReplayCountermeasureIdentity",
            seek="ReplayMod.Core.ReplaySeekState",rolling="ReplayMod.Core.RollingReplayBuffer",buffer="ReplayMod.Core.BoundedEventBuffer";
        Check("compiled common native flare and chaff launch observers",Calls(Body("ReplayMod.Patches.ReplayFlareLaunchPatch","Postfix"),recorder,"RecordFlareLaunch")&&
            Calls(Body("ReplayMod.Patches.ReplayChaffLaunchPatch","Postfix"),recorder,"RecordChaffLaunch")&&
            Calls(Body(recorder,"RecordFlareLaunch"),recorder,"BeginCountermeasure")&&Calls(Body(recorder,"RecordChaffLaunch"),recorder,"BeginCountermeasure"));
        var captureNames=new[]{"RecordFlareLaunch","RecordChaffLaunch","BeginCountermeasure","CaptureCountermeasure","CaptureCountermeasuresLate","IsTracked","IsTrackedOrPending"};
        var captureBodies=captureNames.Select(name=>Body(recorder,name)).ToArray();
        Check("compiled capture retains exact session actor guards without host or random-spread filter",captureBodies.All(method=>method!=null)&&
            Calls(Body(recorder,"BeginCountermeasure"),recorder,"get_SessionValid")&&Calls(Body(recorder,"BeginCountermeasure"),recorder,"IsTrackedOrPending")&&
            Calls(Body(recorder,"IsTrackedOrPending"),recorder,"IsTracked")&&ReadField(Body(recorder,"IsTrackedOrPending"),recorder,"_pendingUnitRegistrations")&&
            Body(recorder,"IsTrackedOrPending")?.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Ceq)==true&&
            captureBodies.All(method=>method!.Body.Instructions.All(instruction=>
                instruction.Operand is not MethodReference call||call.DeclaringType.FullName!="UnityEngine.Random"&&
                    call.Name is not("get_IsServer" or "get_IsHost" or "get_HasAuthority" or "get_IsLocalPlayer" or "IsLocalAircraft")))&&
            captureBodies.All(method=>method!.Body.Instructions.All(instruction=>instruction.Operand is not FieldReference field||
                field.Name is not("isServer" or "isHost" or "hasAuthority"))));
        Check("pending actor launch sample and end use the ordered event buffer",new[]{"BeginCountermeasure","CaptureCountermeasuresLate","RecordCountermeasureEnd"}.All(name=>Calls(Body(recorder,name),recorder,"QueueEventForUnit")));
        Check("compiled observed trajectory samples use resolved native poses at twenty Hz",Calls(Body(recorder,"LateUpdate"),recorder,"CaptureCountermeasuresLate")&&
            Body(recorder,"CaptureCountermeasuresLate")?.Body.Instructions.Any(instruction=>instruction.Operand is double interval&&interval==.05)==true&&
            ReadField(Body(recorder,"CaptureCountermeasure"),"IRFlare","velocity")&&ReadField(Body(recorder,"CaptureCountermeasure"),"RadarChaff","velocity")&&
            Calls(Body(recorder,"CaptureCountermeasure"),"GlobalPositionExtensions","GlobalPosition"));
        Check("compiled recording discovers initial trails and clears every recording lifecycle",FamilyCalls(recorder,"StartRecording",recorder,"CaptureExistingCountermeasures")&&
            Family(recorder,"StartRecording").Sum(method=>method.Body.Instructions.Count(instruction=>instruction.Operand is MethodReference call&&
                call.DeclaringType.FullName==recorder&&call.Name=="ClearRecordingCountermeasures"))>=2&&
            FamilyCalls(recorder,"StopRecordingCore",recorder,"ClearRecordingCountermeasures"));
        Check("compiled native end observers reserve tails and avoid duplicate component launches",
            Calls(Body("ReplayMod.Patches.ReplayFlareEndPatch","Postfix"),recorder,"RecordCountermeasureEnd")&&
            Calls(Body("ReplayMod.Patches.ReplayChaffEndPatch","Postfix"),recorder,"RecordCountermeasureEnd")&&
            Calls(Body(recorder,"BeginCountermeasure"),"System.Collections.Generic.Dictionary`2","ContainsKey")&&
            Calls(Body(recorder,"RecordCountermeasureEnd"),"System.Collections.Generic.List`1","Add")&&
            Calls(Body(recorder,"PruneCountermeasureTails"),"System.Collections.Generic.List`1","RemoveAll"));
        Check("compiled prefab resolution requires recorded unit slot and exact name",ReadField(Body(identity,"Resolve"),"Encyclopedia","Lookup")&&
            ReadField(Body(identity,"Resolve"),"UnitDefinition","unitPrefab")&&ReadField(Body(identity,"Resolve"),value,"prefabSlot")&&
            ReadField(Body(identity,"Resolve"),value,"prefabName")&&Calls(Body(identity,"Resolve"),"System.StringComparer","Equals"));

        var ctor=Body(visual,".ctor");var instructions=ctor?.Body.Instructions.ToArray()??Array.Empty<Instruction>();
        int instantiate=Array.FindIndex(instructions,instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="UnityEngine.Object"&&call.Name=="Instantiate");
        int deactivate=Array.FindIndex(instructions,instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="UnityEngine.GameObject"&&
            call.Name=="SetActive"&&instruction.Previous?.OpCode.Code==Code.Ldc_I4_0);
        int activate=Array.FindIndex(instructions,instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="UnityEngine.GameObject"&&
            call.Name=="SetActive"&&instruction.Previous?.OpCode.Code==Code.Ldc_I4_1);
        int disable=Array.FindIndex(instructions,instruction=>instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="UnityEngine.Behaviour"&&
            call.Name=="set_enabled"&&instruction.Previous?.OpCode.Code==Code.Ldc_I4_0);
        Check("compiled visual clones under inactive ownership before native scripts can run",deactivate>=0&&instantiate>deactivate&&disable>instantiate&&activate>disable&&
            instructions.Any(instruction=>instruction.Operand is GenericInstanceMethod call&&call.Name=="GetComponent"&&call.GenericArguments.Any(argument=>argument.FullName=="IRFlare"))&&
            instructions.Any(instruction=>instruction.Operand is GenericInstanceMethod call&&call.Name=="GetComponent"&&call.GenericArguments.Any(argument=>argument.FullName=="RadarChaff")));
        var presentationBodies=compiled.Values.Where(type=>type.FullName==visual||type.FullName.StartsWith(visual+"/",StringComparison.Ordinal))
            .SelectMany(type=>type.Methods.Where(method=>method.HasBody)).ToArray();
        Check("compiled replay visuals never register guidance targets or launch countermeasures",presentationBodies.Length>0&&
            presentationBodies.All(method=>method.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||
                call.DeclaringType.FullName!="IRSource"&&call.Name is not("LaunchFlare" or "LaunchChaff" or "AddIRSource" or "AddRadarChaff" or "EjectFlare" or "EjectChaff")))&&
            Calls(ctor,"UnityEngine.Rigidbody","set_isKinematic")&&Calls(ctor,"UnityEngine.Rigidbody","set_detectCollisions"));
        Check("compiled particles follow recorded time with pause after simulation",Calls(Body(visual,"Advance"),timeline,"TryAt")&&
            Calls(Body(visual,"Advance"),"UnityEngine.ParticleSystem","Simulate")&&Calls(Body(visual,"Advance"),"UnityEngine.ParticleSystem","Pause")&&
            Calls(Body(visual,"Update"),visual,"Advance")&&presentationBodies.All(method=>method.Body.Instructions.All(instruction=>
                instruction.Operand is not MethodReference call||call.DeclaringType.FullName!="UnityEngine.Time")));
        Check("compiled controller expires and disposes owned visual objects",Calls(Body(controller,"UpdateReplayCountermeasures"),timeline,"get_ExpiresAt")&&
            Calls(Body(controller,"UpdateReplayCountermeasures"),visual,"Dispose")&&Calls(Body(controller,"ClearReplayCountermeasures"),visual,"Dispose")&&
            Calls(Body(visual,"Dispose"),"UnityEngine.GameObject","SetActive")&&Calls(Body(visual,"Dispose"),"UnityEngine.Object","Destroy"));
        Check("compiled update and replay teardown reach countermeasure ownership",Calls(Body(controller,"Update"),controller,"UpdateReplayCountermeasures")&&
            Calls(Body(controller,"ReleaseOwnedPresentationObjects"),controller,"ClearReplayCountermeasures")&&
            Calls(Body(controller,"Execute",value),value,"HasValidPayload")&&Calls(Body(controller,"Execute",value),timeline,".ctor"));
        Check("compiled seek state snapshots clones and clears independent countermeasures",Calls(Body(seek,"Add"),state,"Add")&&
            Calls(Body(seek,"CopyTo"),state,"CopyTo")&&Calls(Body(seek,"Clone"),state,"CloneTo")&&Calls(Body(seek,"Clear"),state,"Clear")&&
            Calls(Body(seek,"ExpireCountermeasures"),state,"Expire"));
        Check("compiled rolling cuts preserve trajectory history and rebase sample clocks",Calls(Body(rolling,"Snapshot"),seek,"ExpireCountermeasures")&&
            Calls(Body(rolling,"Snapshot"),timeline,"SampleAtCut")&&Calls(Body(rolling,"Snapshot"),value,"RebaseTime"));
        Check("compiled future samples retain distinct countermeasure lifetime boundaries",ReadField(Body(buffer,"IsInterpolation"),value,"phase")&&
            Calls(Body(buffer,"BoundaryKey"),buffer,"CountermeasureKey")&&Calls(Body(buffer,"SampleActor"),buffer,"CountermeasureKey")&&
            Body(buffer,"CountermeasureKey")?.Body.Instructions.Any(instruction=>instruction.Operand is long flag&&flag==4294967296L)==true);

        Console.WriteLine($"Countermeasure API/integration audit: {checks} groups, {failures} failures");
        return(checks,failures);
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {foreach(var type in roots){yield return type;foreach(var nested in AllTypes(type.NestedTypes))yield return nested;}}
}
