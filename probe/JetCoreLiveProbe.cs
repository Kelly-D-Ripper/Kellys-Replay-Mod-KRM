using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class JetCoreLiveProbe
{
    private sealed class Sample { public double Time; public bool Ignition; public float[] Rpm; }
    private static IEngine FirstJet(Aircraft aircraft)=>aircraft?.engineStates?.FirstOrDefault(e=>e is Turbojet||e is Turbofan);
    private static void SetRpm(IEngine engine,float rpm)
    {if(engine is Turbojet jet)jet.rpm=rpm;else if(engine is Turbofan fan)fan.currentRPM=rpm;else throw new Exception("Unsupported engine fixture");}
    private static AudioSource Source(IEngine engine)=>engine is Turbojet jet?jet.turbineAudio:engine is Turbofan fan?fan.turbineAudio:null;
    private static async UniTask Wait(float seconds) { float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame(); }
    private static float Expected(List<Sample> samples,double time,int index)
    {
        int n=samples.FindLastIndex(s=>s.Time<=time);if(n<0)return float.NaN;
        var a=samples[n];if(index>=a.Rpm.Length)return float.NaN;
        if(n+1>=samples.Count)return a.Rpm[index];var b=samples[n+1];
        if(index>=b.Rpm.Length||a.Ignition!=b.Ignition||b.Time-a.Time>.5||b.Time<=a.Time)return a.Rpm[index];
        return Mathf.Lerp(a.Rpm[index],b.Rpm[index],(float)((time-a.Time)/(b.Time-a.Time)));
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            await ReplayManager.i.SwitchState(ModStates.Idle);
            await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Native jet recording test");
            TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
            var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("No recorder");string partial=recorder.RecordingPath;
            string aircraftKey=Environment.GetEnvironmentVariable("REPLAY_JET_CORE_KEY")??"Fighter1";
            var def=(AircraftDefinition)Encyclopedia.Lookup[aircraftKey];
            var faction=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            var plane=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,faction,null,1f,1f);
            if(plane==null)throw new Exception("Jet spawn failed");uint id=plane.persistentID.Id;await Wait(2);
            var jet=FirstJet(plane);if(jet==null)throw new Exception("Jet engineStates registration absent");
            var liveCores=plane.engineStates.Where(e=>e is Turbojet||e is Turbofan).ToArray();
            if(liveCores.Length!=plane.engineStates.Count)throw new Exception("Unexpected mixed engine fixture");
            log("JET CORE live key="+aircraftKey+" type="+jet.GetType().Name+" propulsion="+(plane.engines?.Count??-1)+" cores="+plane.engineStates.Count);
            float start=Time.realtimeSinceStartup,end=start+3f;
            while(Time.realtimeSinceStartup<end)
            {
                float fraction=Mathf.Clamp01((Time.realtimeSinceStartup-start)/3f);
                for(int i=0;i<liveCores.Length;i++)SetRpm(liveCores[i],i%2==0?800+1800*fraction:2400-1200*fraction);
                await UniTask.NextFrame();
            }
            int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(faults!=0||!File.Exists(file))throw new Exception("Jet recording failed");ReplayIntegrityCheck.Check(file);
            var samples=new List<Sample>();byte format;
            using(var input=new BinaryReader(File.OpenRead(file)))
            {
                var header=ReplayRecordingHeader.Read(input);format=header.Format;
                for(long n=0;n<header.EventCount;n++)
                {
                    var e=ReplayRecordTransport.ReadEvent(input,format);
                    if(e is EngineCoreStateEvent core&&core.unitId==id&&core.rpm.Length>0)
                        samples.Add(new Sample{Time=core.Time,Ignition=core.ignition,Rpm=(float[])core.rpm.Clone()});
                }
            }
            if(format!=11||samples.Count<30||samples.Any(s=>s.Rpm.Length!=liveCores.Length)||samples.Max(s=>s.Rpm[0])-samples.Min(s=>s.Rpm[0])<500)throw new Exception("Recorded core ramp missing or static");
            if(liveCores.Length>1&&!samples.Any(s=>Math.Abs(s.Rpm[0]-s.Rpm[1])>500))throw new Exception("Independent core values were not recorded");
            log("JET CORE record format="+format+" cores="+liveCores.Length+" samples="+samples.Count+" min="+samples.Min(s=>s.Rpm[0]).ToString("F1")+" max="+samples.Max(s=>s.Rpm[0]).ToString("F1"));
            await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
            if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Jet replay did not open");
            foreach(float speed in new[]{1f,.1f})
            {
                player.Pause();await player.TimelineJump(2.4,0,false);
                var replay=player.FindActor(id) as Aircraft;var restored=FirstJet(replay);
                if(restored==null)throw new Exception("Restored jet core missing");
                var restoredCores=replay.engineStates.Where(e=>e is Turbojet||e is Turbofan).ToArray();
                if(restoredCores.Length!=liveCores.Length)throw new Exception("Restored engine count mismatch");
                var source=Source(restored);if(source==null||source.isPlaying)throw new Exception("Expected freshly restored stopped jet loop");
                ReplayDirector.i.SetPlaybackSpeed(speed);
                if(speed==1)
                {
                    source.mute=true;player.Resume();if(source.isPlaying)throw new Exception("Muted jet loop was started by replay");player.Pause();source.mute=false;
                    if(restored is Turbojet replayJet)
                    {
                        bool oldCone=replayJet.outOfSoundCone;replayJet.outOfSoundCone=true;player.Resume();
                        if(source.isPlaying)throw new Exception("Jet loop started outside native sound cone");player.Pause();replayJet.outOfSoundCone=oldCone;
                    }
                    log("JET CORE startup scope muted source untouched; cone checked="+(restored is Turbojet));
                }
                player.Resume();if(!source.isPlaying)throw new Exception("Recorded jet loop did not start on playback transition");
                float until=Time.realtimeSinceStartup+(speed==1?1.2f:2f),settled=Time.realtimeSinceStartup+.15f,maxError=0;int checkedFrames=0;
                while(Time.realtimeSinceStartup<until)
                {
                    await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                    if(Time.realtimeSinceStartup<settled)continue;
                    for(int i=0;i<restoredCores.Length;i++)
                    {
                        float expected=Expected(samples,player.currentVirtualTime,i);if(float.IsNaN(expected))continue;
                        maxError=Math.Max(maxError,Math.Abs(restoredCores[i].GetRPM()-expected));checkedFrames++;
                    }
                }
                log("JET CORE playback speed="+speed+" checks="+checkedFrames+" maxRpmError="+maxError.ToString("F1"));
                if(checkedFrames<30||maxError>150)throw new Exception("Jet core RPM did not follow recorded ramp");
            }
            float seekError=0;int seekChecks=0;
            foreach(double target in new[]{4.0,2.6,3.7,2.5,4.2,2.8})
            {
                player.Pause();await player.TimelineJump(target,0,false);await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                var restored=player.FindActor(id) as Aircraft;
                if(restored==null||restored.engineStates.Count!=liveCores.Length)throw new Exception("Core actors missing after rewind");
                for(int i=0;i<liveCores.Length;i++)
                {
                    float expected=Expected(samples,target,i);if(float.IsNaN(expected))throw new Exception("No core sample at seek target");
                    seekError=Math.Max(seekError,Math.Abs(restored.engineStates[i].GetRPM()-expected));seekChecks++;
                }
            }
            log("JET CORE repeated seeks checks="+seekChecks+" maxRpmError="+seekError.ToString("F1"));
            if(seekError>150)throw new Exception("Core RPM diverged after repeated seeking");
            log("JET CORE PASS recorded separate jet state and restored/interpolated RPM at normal and slow speeds");
        }
        finally{AudioListener.volume=oldVolume;}
    }
}
