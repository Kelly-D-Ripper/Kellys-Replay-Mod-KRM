using Mono.Cecil;
using Mono.Cecil.Cil;

public static class MissileImpactContractChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"missile impact "+name);}
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(type=>type.FullName);
        MethodDefinition? Body(string owner,string name)=>compiled.TryGetValue(owner,out var type)?type.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        MethodDefinition? Native(string owner,string name)=>types.TryGetValue(owner,out var type)?type.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody):null;
        MethodDefinition? AsyncBody(string owner,string name)
        {
            var method=Body(owner,name);
            var state=method?.CustomAttributes.FirstOrDefault(attribute=>attribute.AttributeType.FullName=="System.Runtime.CompilerServices.AsyncStateMachineAttribute")?.ConstructorArguments.FirstOrDefault().Value as TypeReference;
            return state!=null?Body(state.FullName,"MoveNext"):method;
        }
        bool Calls(MethodDefinition? method,string owner,string name)=>method?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call&&
            call.DeclaringType.GetElementType().FullName==owner&&call.Name==name)==true;
        bool Fields(MethodDefinition? method,string owner,string name)=>method?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&
            field.DeclaringType.GetElementType().FullName==owner&&field.Name==name)==true;
        int CallIndex(MethodDefinition? method,string owner,string name)=>method?.Body.Instructions.ToList().FindIndex(instruction=>instruction.Operand is MethodReference call&&
            call.DeclaringType.GetElementType().FullName==owner&&call.Name==name)??-1;
        bool UsesType(MethodDefinition? method,string name)=>method?.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Isinst&&instruction.Operand is TypeReference type&&type.FullName==name)==true;
        bool ExactReference(MethodDefinition? method)=>Calls(method,"System.Object","ReferenceEquals")||method?.Body.Instructions.Any(instruction=>instruction.OpCode.Code==Code.Ceq)==true;
        const string recorder="ReplayMod.Core.Recorder",controller="ReplayMod.Core.UnitController",vfx="ReplayMod.Core.ReplayVfxPlayback";
        const string batcher="NuclearOption.NetworkTransforms.SendTransformBatcher",networkMissile="NuclearOption.NetworkTransforms.MissileNetworkTransform";
        const string impact="ReplayMod.Events.ConcreteEvents.MissileDetonationEvent",move="ReplayMod.Events.ConcreteEvents.UpdatePositionEvent",status="ReplayMod.Events.ConcreteEvents.MissileStatusEvent";

        Check("native remote missile presentation occurs in batcher LateUpdate",Calls(Native(batcher,"LateUpdate"),batcher,"ClientUpdate")&&
            Calls(Native(batcher,"ClientUpdate"),batcher,"VisualUpdate")&&Calls(Native(batcher,"VisualUpdate"),"NuclearOption.NetworkTransforms.NetworkTransformBase","VisualUpdate"));
        Check("native remote presentation writes real rigidbody velocity",Calls(Native(networkMissile,"VisualUpdate"),networkMissile,"ApplySnapshot")&&
            Calls(Native(networkMissile,"ApplySnapshot"),"UnityEngine.Rigidbody","set_velocity"));
        var resolved=types.TryGetValue("Missile",out var missile)?missile.Methods.SingleOrDefault(method=>method.Name.StartsWith("UserCode_RpcDetonate_",StringComparison.Ordinal)&&method.HasBody):null;
        Check("native resolved impact hides and pins the body before deferred destruction",Calls(resolved,"Missile","Disappear")&&
            Calls(resolved,"UnityEngine.Rigidbody","set_isKinematic")&&Calls(resolved,"UnityEngine.Transform","set_position")&&
            Calls(Native("Missile","Detonate"),"Missile","DelayedDestroy")&&Native("Missile","Detonate")?.Body.Instructions.Any(instruction=>instruction.Operand is float seconds&&seconds==2)==true);
        var late=Body(recorder,"LateUpdate");
        Check("compiled late capture observes aircraft and missiles together after native presentation",Calls(late,recorder,"RecordObservedMissileMotion")&&
            Calls(late,recorder,"RecordObservedAircraftMotion")&&compiled.TryGetValue(recorder,out var recorderType)&&recorderType.CustomAttributes.Any(attribute=>
                attribute.AttributeType.FullName=="UnityEngine.DefaultExecutionOrder"&&attribute.ConstructorArguments.Count==1&&(int)attribute.ConstructorArguments[0].Value==10000)&&
            !Calls(late,"UnityEngine.Object","FindObjectsOfType"));
        Check("compiled Update no longer publishes pre-presentation missile motion/status",UsesType(Body(recorder,"Update"),"Missile")&&
            !Calls(Body(recorder,"Update"),recorder,"RecordMissileStatus")&&!Calls(Body(recorder,"Update"),recorder,"RecordObservedMissileMotion"));
        var capture=Body(recorder,"RecordObservedMissileMotion");var terminal=Body(recorder,"IsRecordingMissileTerminal");
        Check("compiled capture rejects spent exact actors before publishing pose or motor state",Calls(capture,recorder,"IsTrackedLifecycleUnit")&&
            Calls(capture,recorder,"IsRecordingMissileTerminal")&&CallIndex(capture,recorder,"IsRecordingMissileTerminal")<CallIndex(capture,recorder,"QueueEvent")&&
            Calls(capture,recorder,"RecordMissileStatus")&&Calls(Body(recorder,"RecordMissileStatus"),recorder,"IsRecordingMissileTerminal")&&
            Fields(terminal,recorder,"_terminalRecordingMissiles")&&ExactReference(terminal)&&
            capture?.Body.Instructions.Any(instruction=>instruction.Operand is double interval&&interval==.05)==true);
        var callback=Body(recorder,"HandleRecordedDetonation");var mark=Body(recorder,"MarkRecordingMissileTerminal");
        Check("compiled resolved RPC closes flight capture once without ending native actor lifetime",Calls(callback,recorder,"IsRecordingMissileTerminal")&&
            Calls(callback,recorder,"MarkRecordingMissileTerminal")&&CallIndex(callback,recorder,"MarkRecordingMissileTerminal")<CallIndex(callback,recorder,"QueueEvent")&&
            Calls(mark,recorder,"IsTrackedLifecycleUnit")&&Fields(mark,recorder,"_terminalRecordingMissiles")&&
            !Fields(mark,recorder,"_unitsOnTrack")&&!Fields(callback,recorder,"_unitsOnTrack")&&
            !Calls(callback,"UnityEngine.Object","Destroy")&&!Calls(callback,controller,"DespawnUnit"));
        Check("compiled recording startup rejects native spent bodies and exact untracked cleanup",Calls(Body(recorder,"RegisterUnitWhenReady"),recorder,"IsNativelyTerminalMissile")&&
            Calls(Body(recorder,"IsNativelyTerminalMissile"),"UnityEngine.Rigidbody","get_isKinematic")&&
            (Fields(Body(recorder,"IsNativelyTerminalMissile"),"Unit","disabled")||Calls(Body(recorder,"IsNativelyTerminalMissile"),"Unit","get_disabled"))&&
            Calls(Body(recorder,"HandleUnitUnregister"),recorder,"IsTracked")&&Calls(Body(recorder,"HandleUnitUnregister"),recorder,"ForgetRecordingMissileMotion"));
        Check("compiled recording terminals clear on restart failure stop and ID reuse",Calls(AsyncBody(recorder,"StartRecording"),recorder,"ClearRecordingMissileMotion")&&
            Calls(AsyncBody(recorder,"StopRecordingCore"),recorder,"ClearRecordingMissileMotion")&&
            Calls(Body(recorder,"RegisterUnitWhenReady"),recorder,"ForgetRecordingMissileMotion")&&
            compiled[recorder].Methods.Count(method=>method.Name=="ForgetRecordingMissileMotion"&&method.HasBody)==2);
        Check("compiled capture never edits native projectile physics or effects",new[]{capture,mark,Body(recorder,"IsNativelyTerminalMissile")}.All(method=>method!=null&&
            method.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference call||
                call.Name is not("set_velocity" or "set_isKinematic" or "MovePosition" or "SetPositionAndRotation" or "Disappear" or "Detonate" or "Destruct" or "Destroy"))));

        var ownTerminal=Body(controller,"IsTerminalMissile");var canApply=Body(controller,"CanApplyMissileImpact");
        Check("compiled playback impact belongs to the current exact missile lifetime",ExactReference(ownTerminal)&&ExactReference(canApply)&&
            Fields(ownTerminal,controller,"_missileImpacts")&&Calls(canApply,controller,"TryGetUnit")&&Fields(canApply,controller,"_spawnTimes")&&
            Calls(canApply,"System.Double","IsNaN")&&Calls(canApply,"System.Double","IsInfinity")&&
            Calls(Body(controller,"RecordMissileImpact"),controller,"CanApplyMissileImpact")&&Calls(Body(controller,"RecordMissileImpact"),controller,"PinMissileImpact"));
        var moveBody=compiled.TryGetValue(controller,out var controllerType)?controllerType.Methods.FirstOrDefault(method=>method.Name=="MoveUnit"&&method.HasBody&&Calls(method,controller,"RestoreTerminalMissilePose")):null;
        Check("compiled terminal pin wins before ordinary per-frame motion and physics configuration",moveBody!=null&&
            CallIndex(moveBody,controller,"RestoreTerminalMissilePose")<CallIndex(moveBody,controller,"ConfigureReplayPhysics")&&
            Calls(Body(controller,"RestoreTerminalMissilePose"),controller,"IsTerminalMissile")&&Calls(Body(controller,"RestoreTerminalMissilePose"),controller,"PinMissileImpact"));
        MethodDefinition? Execute(string parameter)=>controllerType?.Methods.FirstOrDefault(method=>method.Name=="Execute"&&method.Parameters.Count==1&&method.Parameters[0].ParameterType.FullName==parameter&&method.HasBody);
        var unitStatus=compiled.TryGetValue(vfx,out var presentation)?presentation.Methods.FirstOrDefault(method=>method.Name=="Apply"&&method.Parameters.Count==2&&
            method.Parameters[1].ParameterType.FullName=="ReplayMod.Events.ConcreteEvents.UnitStatusEvent"&&method.HasBody):null;
        Check("compiled late legacy positions motor snapshots and status cannot revive an impacted missile",Calls(Execute(move),controller,"IsTerminalMissile")&&
            Calls(Execute(status),controller,"IsTerminalMissile")&&Calls(unitStatus,controller,"IsTerminalMissile"));
        var presentImpact=presentation?.Methods.FirstOrDefault(method=>method.Name=="Apply"&&method.Parameters.Count==2&&method.Parameters[1].ParameterType.FullName==impact&&method.HasBody);
        Check("compiled impact detaches existing world trail before the authoritative correction",Calls(presentImpact,controller,"CanApplyMissileImpact")&&
            Calls(presentImpact,"UnityEngine.Transform","SetParent")&&Calls(presentImpact,controller,"RecordMissileImpact")&&
            CallIndex(presentImpact,"UnityEngine.Transform","SetParent")<CallIndex(presentImpact,controller,"RecordMissileImpact")&&
            Calls(presentImpact,"Missile/Motor","Burnout")&&CallIndex(presentImpact,"Missile/Motor","Burnout")<CallIndex(presentImpact,controller,"RecordMissileImpact"));
        Check("compiled terminal physics is stationary and lifetime state releases on despawn and close",Calls(Body(controller,"PinMissileImpact"),"UnityEngine.Rigidbody","set_isKinematic")&&
            Calls(Body(controller,"PinMissileImpact"),"UnityEngine.Rigidbody","set_position")&&Calls(Body(controller,"PinMissileImpact"),"UnityEngine.Transform","set_position")&&
            Calls(Body(controller,"DespawnUnit"),controller,"ForgetMissileImpact")&&Calls(Body(controller,"ReleaseOwnedPresentationObjects"),controller,"ClearMissileImpacts"));
        const string buffer="ReplayMod.Core.BoundedEventBuffer",seek="ReplayMod.Core.ReplaySeekState",rolling="ReplayMod.Core.RollingReplayBuffer";
        Check("compiled producer impact is an actor boundary fencing future interpolation",UsesType(Body(buffer,"BoundaryActor"),impact)&&Fields(Body(buffer,"BoundaryActor"),impact,"unitId")&&
            Calls(Body(buffer,"BoundaryKey"),buffer,"BoundaryActor")&&Calls(Body(buffer,"IsBlocked"),buffer,"SampleActor")&&
            Calls(Body(buffer,"TryTakeIfReady"),buffer,"IsBlocked")&&Calls(Body(buffer,"MarkActionExecuted"),buffer,"BoundaryKey"));
        Check("compiled seek and rolling cuts discard spent flight anchors without replaying old explosions",UsesType(Body(seek,"Add"),impact)&&
            Calls(Body(seek,"Add"),seek,"IsAlive")&&Calls(Body(seek,"Add"),seek,"Forget")&&
            UsesType(Body(rolling,"Snapshot"),impact)&&Fields(Body(rolling,"Snapshot"),impact,"unitId")&&
            Calls(Body(rolling,"Snapshot"),"System.Collections.Generic.Dictionary`2","Remove"));
        // Native seekers run SlowChecks from their own slow-update timer, which
        // ServerFixedUpdate suppression does not cover, and can call Detonate.
        bool IsSeeker(TypeDefinition type)
        {
            for(var parent=type.BaseType;parent!=null;parent=types.TryGetValue(parent.FullName,out var resolved)?resolved.BaseType:null)
                if(parent.FullName=="MissileSeeker")return true;
            return false;
        }
        var seekerTypes=types.Values.Where(IsSeeker).ToArray();
        Check("native seekers initialise only from missile LocalStart",Calls(Native("Missile","LocalStart"),"MissileSeeker","Initialize")&&
            !types.Values.SelectMany(type=>type.Methods).Where(method=>method.HasBody&&!(method.DeclaringType.FullName=="Missile"&&method.Name=="LocalStart")&&
                !seekerTypes.Contains(method.DeclaringType)&&method.DeclaringType.FullName!="MissileSeeker").Any(method=>method.Body.Instructions.Any(instruction=>
                instruction.Operand is MethodReference call&&call.Name=="Initialize"&&(call.DeclaringType.FullName=="MissileSeeker"||seekerTypes.Any(type=>type.FullName==call.DeclaringType.FullName)))));
        var selfDestructingSeekers=seekerTypes.Where(type=>Calls(type.Methods.FirstOrDefault(method=>method.Name=="SlowChecks"&&method.HasBody),"Missile","Detonate")).ToArray();
        Check("native seeker slow checks can self-destruct a missile outside ServerFixedUpdate",selfDestructingSeekers.Length>0&&
            selfDestructingSeekers.All(type=>Calls(type.Methods.FirstOrDefault(method=>method.Name=="Initialize"&&method.HasBody),"SlowUpdateExtensions","StartSlowUpdateDelayed")||
                Calls(type.Methods.FirstOrDefault(method=>method.Name=="Initialize"&&method.HasBody),"SlowUpdateExtensions","StartSlowUpdate")));
        Check("native missile damage can detonate",Calls(Native("Missile","TakeDamage"),"Missile","Detonate"));
        bool PlaybackGuard(string patch,string target)
        {
            if(!compiled.TryGetValue(patch,out var type))return false;
            var harmony=type.CustomAttributes.FirstOrDefault(attribute=>attribute.AttributeType.Name=="HarmonyPatch");
            var prefix=type.Methods.FirstOrDefault(method=>method.Name=="Prefix"&&method.HasBody&&method.ReturnType.FullName=="System.Boolean"&&
                method.CustomAttributes.Any(attribute=>attribute.AttributeType.Name=="HarmonyPrefix"));
            return harmony!=null&&harmony.ConstructorArguments.Any(argument=>argument.Value is TypeReference owner&&owner.FullName=="Missile")&&
                harmony.ConstructorArguments.Any(argument=>argument.Value is string name&&name==target)&&Calls(prefix,"ReplayMod.Core.ReplayRuntime","get_IsPlayback");
        }
        Check("compiled playback suppresses native seeker start, detonation and damage",PlaybackGuard("ReplayMod.Patches.ReplayMissileSeekerStartPatch","LocalStart")&&
            PlaybackGuard("ReplayMod.Patches.ReplayMissileNativeDetonationPatch","Detonate")&&PlaybackGuard("ReplayMod.Patches.ReplayMissileDamagePatch","TakeDamage"));
        const string detonationGuard="ReplayMod.Patches.ReplayMissileNativeDetonationPatch";
        var nativeDetonators=compiled.Values.SelectMany(type=>type.Methods).Where(method=>method.HasBody&&Calls(method,"Missile","Detonate")).ToArray();
        Check("compiled native detonation only for a legacy recorded despawn",presentImpact!=null&&!Calls(presentImpact,"Missile","Detonate")&&
            nativeDetonators.Length==1&&nativeDetonators[0].DeclaringType.FullName==detonationGuard&&nativeDetonators[0].Name=="DetonateRecordedDespawn"&&
            Calls(Body(controller,"DespawnUnit"),detonationGuard,"DetonateRecordedDespawn")&&
            compiled.Values.SelectMany(type=>type.Methods).Count(method=>method.HasBody&&Calls(method,detonationGuard,"DetonateRecordedDespawn"))==1);
        Console.WriteLine($"Missile impact API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {foreach(var type in roots){yield return type;foreach(var nested in AllTypes(type.NestedTypes))yield return nested;}}
}
