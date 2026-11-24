using Mono.Cecil;

public static class PropAudioChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> types,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool pass){checks++;if(!pass)failures++;Console.WriteLine((pass?"PASS ":"FAIL ")+"propeller audio "+name);}
        Check("native source and formula fields",types.TryGetValue("PropFan",out var prop)&&new[]{"nominalRPM","powerRatio","volumeBase","rpmVolumePortion","aoaVolumePortion"}.All(name=>prop.Fields.Any(f=>f.Name==name&&f.FieldType.FullName=="System.Single"))&&prop.Fields.Any(f=>f.Name=="source"&&f.FieldType.FullName=="UnityEngine.AudioSource"));
        Check("native fixed update controls source volume",types.TryGetValue("PropFan",out var native)&&native.Methods.Where(m=>m.Name=="FixedUpdate"&&m.HasBody).Any(m=>m.Body.Instructions.Any(i=>i.Operand is MethodReference x&&x.DeclaringType.FullName=="UnityEngine.AudioSource"&&x.Name=="set_volume")));
        var compiled=mod.MainModule.Types.ToDictionary(t=>t.FullName);
        MethodDefinition? Method(string type,string name)=>compiled.TryGetValue(type,out var t)?t.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? method,string type,string name)=>method?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)==true;
        var prime=Method("ReplayMod.Core.UnitController","PrimeRecordedPropAudio");
        Check("only recorded source volume is primed",Calls(prime,"ReplayMod.Core.ReplayPropAudioPolicy","TryInitialVolume")&&Calls(prime,"UnityEngine.AudioSource","get_mute")&&Calls(prime,"UnityEngine.AudioSource","get_isPlaying")&&Calls(prime,"UnityEngine.AudioSource","get_volume")&&Calls(prime,"UnityEngine.AudioSource","set_volume"));
        var clock=Method("ReplayMod.Core.Player","ApplyPlaybackClock");
        Check("clock transition primes recorded props only in replay outside seek and pause",Calls(clock,"ReplayMod.Core.UnitController","PrimeRecordedPropAudio")&&Calls(clock,"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&Calls(clock,"ReplayMod.Core.Player","get_IsSeeking")&&Calls(clock,"ReplayMod.Core.PlaybackControlState","get_IsPaused"));
        var wake=Method("ReplayMod.Core.UnitController","WakePrimedPropAudio");
        Check("only eligible newly primed loops restore their sample position",Calls(wake,"ReplayMod.Core.ReplayPropAudioPolicy","CanWakeLoop")&&Calls(wake,"UnityEngine.AudioSource","get_timeSamples")&&Calls(wake,"UnityEngine.AudioSource","set_timeSamples")&&Calls(wake,"UnityEngine.AudioSource","Stop")&&Calls(wake,"UnityEngine.AudioSource","Play")&&Calls(wake,"UnityEngine.AudioSource","get_mute")&&Calls(wake,"UnityEngine.AudioSource","get_isPlaying"));
        var instructions=clock?.Body.Instructions;
        int scaleIndex=instructions==null?-1:instructions.ToList().FindIndex(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="TimeScaleManager"&&m.Name=="set_Scale");
        int wakeIndex=instructions==null?-1:instructions.ToList().FindIndex(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="ReplayMod.Core.UnitController"&&m.Name=="WakePrimedPropAudio");
        Check("rearm occurs only after the playback clock transition",scaleIndex>=0&&wakeIndex>scaleIndex);
        Check("aircraft exposes distinct propulsion and core registrations",types.TryGetValue("Aircraft",out var aircraft)&&new[]{"engines","engineStates"}.All(name=>aircraft.Fields.Any(f=>f.Name==name)));
        Check("jet core startup native fields",new[]{"Turbojet","Turbofan"}.All(name=>types.TryGetValue(name,out var engine)&&new[]{"turbineAudio","maxRPM","turbineMaxPitch"}.All(field=>engine.Fields.Any(f=>f.Name==field)))&&types["Turbojet"].Fields.Any(f=>f.Name=="outOfSoundCone"));
        var jets=Method("ReplayMod.Core.UnitController","StartRecordedJetAudio");
        Check("recorded core startup checks policy and source guards",Calls(jets,"ReplayMod.Core.ReplayPropAudioPolicy","CanStartJet")&&Calls(jets,"UnityEngine.AudioSource","get_mute")&&Calls(jets,"UnityEngine.AudioSource","get_isPlaying")&&Calls(jets,"UnityEngine.AudioSource","get_loop")&&Calls(jets,"UnityEngine.AudioSource","Play"));
        int jetIndex=instructions==null?-1:instructions.ToList().FindIndex(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="ReplayMod.Core.UnitController"&&m.Name=="StartRecordedJetAudio");
        Check("recorded core startup follows clock transition",scaleIndex>=0&&jetIndex>scaleIndex);
        Check("native rotor speed and animation fields",types.TryGetValue("RotorShaft",out var rotor)&&new[]{"angularSpeed","angularSpeedLimit","angularSpeedRatio"}.All(name=>rotor.Fields.Any(f=>f.Name==name&&f.FieldType.FullName=="System.Single"))&&rotor.Fields.Any(f=>f.Name=="aircraft"&&f.FieldType.FullName=="Aircraft")&&rotor.Methods.Any(m=>m.Name=="GetRPM"&&m.ReturnType.FullName=="System.Single")&&rotor.Methods.Any(m=>m.Name=="AnimateRotor"&&m.HasBody));
        var apply=Method("ReplayMod.Core.UnitController","ApplyEngineRpm");
        Check("recorded rotor RPM is converted before applying",Calls(apply,"ReplayMod.Core.ReplayRotorSpeedPolicy","TryConvert")&&apply?.Body.Instructions.Count(i=>i.Operand is FieldReference f&&f.DeclaringType.FullName=="RotorShaft"&&(f.Name=="angularSpeed"||f.Name=="angularSpeedRatio"))>=2);
        var rotorPatch=Method("ReplayMod.Patches.ReplayRotorAudioStatePatch","Prefix");
        Check("rotor animation restores only recorded playback state",Calls(rotorPatch,"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&Calls(rotorPatch,"ReplayMod.Core.Player","RestoreRecordedEngineRpm"));
        Check("native ducted fan update and aircraft field",types.TryGetValue("DuctedFan",out var fan)&&fan.Fields.Any(f=>f.Name=="aircraft"&&f.FieldType.FullName=="Aircraft")&&fan.Fields.Any(f=>f.Name=="rpm"&&f.FieldType.FullName=="System.Single")&&fan.Methods.Any(m=>m.Name=="Update"&&m.HasBody));
        var fanPatch=Method("ReplayMod.Patches.ReplayDuctedFanStatePatch","Prefix");
        Check("ducted fan presentation restores recorded RPM only in replay",Calls(fanPatch,"ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&Calls(fanPatch,"ReplayMod.Core.Player","RestoreRecordedEngineRpm"));
        Console.WriteLine($"Propeller audio API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
}
