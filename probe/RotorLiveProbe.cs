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

// Isolated native aircraft test; deliberately changes rotor speed before capture.
public static class RotorLiveProbe
{
    private sealed class Sample { public double Time; public bool Ignition; public float[] Rpm; }
    private static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    private static float Expected(List<Sample> samples,double time,int index)
    {
        int n=samples.FindLastIndex(s=>s.Time<=time);if(n<0||index>=samples[n].Rpm.Length)return float.NaN;
        var a=samples[n];if(n+1>=samples.Count)return a.Rpm[index];var b=samples[n+1];
        if(index>=b.Rpm.Length||a.Ignition!=b.Ignition||b.Time-a.Time>.5||b.Time<=a.Time)return a.Rpm[index];
        return Mathf.Lerp(a.Rpm[index],b.Rpm[index],(float)((time-a.Time)/(b.Time-a.Time)));
    }
    public static async UniTask RunLegacy(string name,Action<string> log)
    {
        if(name!=Path.GetFileName(name))throw new Exception("Expected isolated replay filename");
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",name);string hash;
        using(var sha=System.Security.Cryptography.SHA256.Create())using(var input=File.OpenRead(path))hash=BitConverter.ToString(sha.ComputeHash(input));
        var samples=new List<Sample>();uint id=0;
        using(var input=new BinaryReader(File.OpenRead(path)))
        {
            var header=ReplayRecordingHeader.Read(input);if(header.Format!=10)throw new Exception("Expected older format-10 rotor fixture");
            for(long n=0;n<header.EventCount;n++)
            {
                var value=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(value is SpawnEvent spawn&&spawn.jsonKey=="UtilityHelo1")id=spawn.unitId;
                if(value is AircraftStatusEvent status&&status.unitId==id&&id!=0)
                    samples.Add(new Sample{Time=status.Time,Ignition=status.ignition,Rpm=(float[])status.rpm.Clone()});
                ReplayMod.Events.ReplayEventFactory.Return(value);
            }
        }
        if(id==0||samples.Count<30)throw new Exception("Legacy rotor samples absent");
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(name);var player=ReplayManager.i.Playback;
        if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Legacy rotor replay failed to open");
        float maxError=0;int checks=0;
        foreach(double target in new[]{2.4,3.2,4.0,2.6,3.7,2.5})
        {
            player.Pause();await player.TimelineJump(target,0,false);await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            var aircraft=player.FindActor(id) as Aircraft;if(aircraft?.engines==null)throw new Exception("Legacy rotor aircraft missing");
            var indices=aircraft.engines.Select((e,i)=>new{e,i}).Where(x=>x.e is RotorShaft).Select(x=>x.i).ToArray();
            if(indices.Length<1)throw new Exception("Legacy rotors missing");
            foreach(int index in indices){float expected=Expected(samples,target,index);if(float.IsNaN(expected))throw new Exception("Legacy rotor sample absent");maxError=Math.Max(maxError,Math.Abs(aircraft.engines[index].GetRPM()-expected));checks++;}
        }
        if(Environment.GetEnvironmentVariable("REPLAY_FAN_BENCH")=="1")
        {
            player.Pause();await player.TimelineJump(2.4,0,false);
            await FanTimingProbe.Compare(log);
        }
        using(var sha=System.Security.Cryptography.SHA256.Create())using(var input=File.OpenRead(path))if(hash!=BitConverter.ToString(sha.ComputeHash(input)))throw new Exception("Legacy source changed");
        log("ROTOR LEGACY format=10 samples="+samples.Count+" seekChecks="+checks+" maxRpmError="+maxError.ToString("F1")+" source hash unchanged");
        if(checks<6||maxError>20)throw new Exception("Legacy rotor speed diverged after seeking");
        log("ROTOR LEGACY PASS existing captured rotor data restored");
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        float oldVolume=AudioListener.volume;AudioListener.volume=0;
        try
        {
            await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Rotor RPM recording test");
            TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
            var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording)throw new Exception("Rotor recorder unavailable");string partial=recorder.RecordingPath;
            string aircraftKey=Environment.GetEnvironmentVariable("REPLAY_ROTOR_KEY")??"UtilityHelo1";
            var def=(AircraftDefinition)Encyclopedia.Lookup[aircraftKey];
            var faction=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
            var aircraft=NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,def.unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000,3000,1000),Quaternion.identity,new Vector3(0,0,80),null,faction,null,1f,1f);
            if(aircraft==null)throw new Exception("Ibis fixture spawn failed");uint id=aircraft.persistentID.Id;await Wait(2);
            var rotors=aircraft.engines.OfType<RotorShaft>().ToArray();
            var indices=aircraft.engines.Select((e,i)=>new{e,i}).Where(x=>x.e is RotorShaft).Select(x=>x.i).ToArray();
            if(rotors.Length<1)throw new Exception("Helicopter rotor fixture missing");
            log("ROTOR LIVE aircraft="+aircraftKey+" engines="+string.Join(",",aircraft.engines.Select(e=>e.GetType().Name))+" indices="+string.Join(",",indices));
            if(Environment.GetEnvironmentVariable("REPLAY_FAN_PRESENTATION")=="1")
            {
                if(ReplayRuntime.IsPlayback)throw new Exception("Live fan guard test is not in recording mode");
                var fan=aircraft.engines.OfType<DuctedFan>().FirstOrDefault();if(fan==null)throw new Exception("Live ducted fan fixture missing");
                float before=fan.rpm;fan.rpm=before+100;
                typeof(DuctedFan).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(fan,null);
                if(Math.Abs(fan.rpm-(before+100))>.01f)throw new Exception("Replay fan patch changed live recording RPM");
                fan.rpm=before;
                float prior=rotors[0].angularSpeed;rotors[0].angularSpeed=prior+1;
                typeof(RotorShaft).GetMethod("AnimateRotor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(rotors[0],null);
                if(Math.Abs(rotors[0].angularSpeed-(prior+1))>.001f)throw new Exception("Replay rotor patch changed live recording speed");
                rotors[0].angularSpeed=prior;log("ROTOR/FAN live-recording patch guards PASS");
            }
            float start=Time.realtimeSinceStartup,end=start+3;
            while(Time.realtimeSinceStartup<end)
            {
                float fraction=Mathf.Clamp01((Time.realtimeSinceStartup-start)/3f);
                for(int i=0;i<rotors.Length;i++)rotors[i].angularSpeed=(i%2==0?80+170*fraction:250-130*fraction)/9.55414f;
                await UniTask.NextFrame();
            }
            int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);
            string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
            if(faults!=0||!File.Exists(file))throw new Exception("Rotor recording failed");ReplayIntegrityCheck.Check(file);
            var samples=new List<Sample>();
            using(var input=new BinaryReader(File.OpenRead(file)))
            {
                var header=ReplayRecordingHeader.Read(input);
                for(long n=0;n<header.EventCount;n++)
                {
                    var value=ReplayRecordTransport.ReadEvent(input,header.Format);
                    if(value is AircraftStatusEvent status&&status.unitId==id&&status.rpm.Length>indices.Max())
                        samples.Add(new Sample{Time=status.Time,Ignition=status.ignition,Rpm=(float[])status.rpm.Clone()});
                    ReplayMod.Events.ReplayEventFactory.Return(value);
                }
            }
            if(samples.Count<30||samples.Max(s=>s.Rpm[indices[0]])-samples.Min(s=>s.Rpm[indices[0]])<100)throw new Exception("Rotor samples missing or static");
            log("ROTOR LIVE recorded samples="+samples.Count+" range="+samples.Min(s=>s.Rpm[indices[0]]).ToString("F1")+".."+samples.Max(s=>s.Rpm[indices[0]]).ToString("F1"));
            await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
            if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Rotor replay failed to open");
            foreach(float speed in new[]{1f,.1f})
            {
                player.Pause();await player.TimelineJump(2.4,0,false);
                var replay=player.FindActor(id) as Aircraft;if(replay==null||replay.engines.Count<=indices.Max())throw new Exception("Rotor replay aircraft missing");
                var restored=indices.Select(i=>replay.engines[i] as RotorShaft).ToArray();if(restored.Any(x=>x==null))throw new Exception("Rotor index mapping changed");
                ReplayDirector.i.SetPlaybackSpeed(speed);player.Resume();
                if(speed==1&&Environment.GetEnvironmentVariable("REPLAY_FAN_PRESENTATION")=="1")
                {
                    await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                    var fan=replay.engines.OfType<DuctedFan>().FirstOrDefault();if(fan==null)throw new Exception("Ducted fan fixture missing");
                    player.RestoreRecordedEngineRpm(replay);float expectedFan=fan.rpm;fan.rpm=expectedFan+100;
                    var update=typeof(DuctedFan).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    update.Invoke(fan,null);float error=Math.Abs(fan.rpm-expectedFan);
                    log("DUCTED FAN presentation expected="+expectedFan.ToString("F1")+" observed="+fan.rpm.ToString("F1")+" error="+error.ToString("F1"));
                    player.RestoreRecordedEngineRpm(replay);
                    if(error>.1f)throw new Exception("Fan animation did not restore recorded RPM before native update");
                }
                float until=Time.realtimeSinceStartup+(speed==1?1.2f:2f),settled=Time.realtimeSinceStartup+.15f,maxError=0,maxRatioError=0,maxPitchError=0;int checks=0,playing=0;
                while(Time.realtimeSinceStartup<until)
                {
                    await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);if(Time.realtimeSinceStartup<settled)continue;
                    for(int i=0;i<restored.Length;i++)
                    {
                        float expected=Expected(samples,player.currentVirtualTime,indices[i]);if(float.IsNaN(expected))continue;
                        maxError=Math.Max(maxError,Math.Abs(restored[i].GetRPM()-expected));
                        if(restored[i].angularSpeedLimit>0)maxRatioError=Math.Max(maxRatioError,Math.Abs(restored[i].angularSpeedRatio-expected/9.55414f/restored[i].angularSpeedLimit));
                        if(restored[i].rotorSource!=null&&!restored[i].detached)
                        {
                            maxPitchError=Math.Max(maxPitchError,Math.Abs(restored[i].rotorSource.pitch-restored[i].angularSpeedRatio*restored[i].pitch));
                            if(restored[i].rotorSource.isPlaying)playing++;
                        }
                        checks++;
                    }
                }
                log("ROTOR LIVE playback speed="+speed+" checks="+checks+" maxRpmError="+maxError.ToString("F1")+" maxRatioError="+maxRatioError.ToString("F3")+" maxPitchError="+maxPitchError.ToString("F3")+" playing="+playing);
                if(checks<30||maxError>20||maxRatioError>.1f||maxPitchError>.1f)throw new Exception("Rotor did not follow recorded independent RPM and native sound pitch");
            }
            float seekError=0;int seekChecks=0;
            foreach(double target in new[]{4.0,2.6,3.7,2.5,4.2,2.8})
            {
                player.Pause();await player.TimelineJump(target,0,false);await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                var aircraftAtTarget=player.FindActor(id) as Aircraft;
                if(aircraftAtTarget==null||aircraftAtTarget.engines.Count<=indices.Max())throw new Exception("Rotors missing after rewind");
                foreach(int index in indices)
                {
                    float expected=Expected(samples,target,index);if(float.IsNaN(expected))throw new Exception("Rotor sample missing at seek time");
                    seekError=Math.Max(seekError,Math.Abs(aircraftAtTarget.engines[index].GetRPM()-expected));seekChecks++;
                }
            }
            log("ROTOR LIVE seeks="+seekChecks+" maxRpmError="+seekError.ToString("F1"));
            if(seekError>20)throw new Exception("Rotor RPM diverged after rewind");
            log("ROTOR LIVE PASS independent recorded rotor RPM restored at normal and slow speed");
        }
        finally{AudioListener.volume=oldVolume;}
    }
}
