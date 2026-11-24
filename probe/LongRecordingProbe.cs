using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using UnityEngine;

public static class LongRecordingProbe
{
    public static async UniTask Verify(string name,Action<string> log)
    {
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",name);
        var seenSpawns=new HashSet<uint>();var sampled=new HashSet<uint>();var missing=new HashSet<uint>();var actionsBeforeSpawn=new HashSet<uint>();int aircraftSpawns=0;
        int expected=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_EXPECT_AIRCRAFT"),out var count)?count:28;
        double minDuration=double.TryParse(Environment.GetEnvironmentVariable("REPLAY_LONG_MIN_SECONDS"),out var duration)?duration:500;
        using(var input=new BinaryReader(File.OpenRead(file)))
        {
            var header=ReplayRecordingHeader.Read(input);
            for(long n=0;n<header.EventCount;n++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is SpawnEvent spawn){seenSpawns.Add(spawn.unitId);if(spawn.isAircraft)aircraftSpawns++;}
                if(e is AircraftStatusEvent status){sampled.Add(status.unitId);if(!seenSpawns.Contains(status.unitId))missing.Add(status.unitId);}
                uint actor=0;
                if(e is SetGearEvent gear)actor=gear.unitId;else if(e is PartDamageEvent damage)actor=damage.unitId;else if(e is EjectionEvent eject)actor=eject.unitId;else if(e is SetActiveStationEvent station)actor=station.unitId;else if(e is WeaponManagerFireEvent fire)actor=fire.unitId;
                if(actor!=0&&!seenSpawns.Contains(actor))actionsBeforeSpawn.Add(actor);
                ReplayEventFactory.Return(e);
            }
        }
        if(missing.Count!=0||actionsBeforeSpawn.Count!=0||aircraftSpawns<expected||sampled.Count<expected)throw new Exception("Sustained recording actor coverage failed: aircraft spawns="+aircraftSpawns+" sampled="+sampled.Count+" missing spawns="+string.Join(",",missing)+" actions before spawn="+string.Join(",",actionsBeforeSpawn));
        log("LONG REPLAY actor coverage PASS aircraft spawns="+aircraftSpawns+" sampled="+sampled.Count+" missing prior spawns=0 early actions=0");
        string integrity=ReplayIntegrityCheck.Check(file);await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayManager.i.StartPlaying(name);var player=ReplayManager.i.Playback;
        if(!ReplayRuntime.IsPlayback||player==null||player.replayDuration<minDuration)throw new Exception("Long recording failed to reopen");
        player.Pause();foreach(double fraction in new[]{.1,.8,.3,.95})
        {
            double at=player.replayDuration*fraction;await player.TimelineJump(at,0,false);
            if(player.IsSeeking||Math.Abs(player.currentVirtualTime-at)>.001||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Long replay seek failed at "+fraction);
        }
        log("LONG REPLAY PASS "+integrity+" duration="+player.replayDuration.ToString("F1")+"s; reopened and sought four times");
    }
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        RollingRecordingController.Instance.SetEnabled(false);await ReplayManager.i.SwitchState(ModStates.Idle);ReplayManager.i.NextRecordingIsRolling=false;await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Sustained recording test");
        TimeScaleManager.Scale=1;await ReplayManager.i.SwitchState(ModStates.Record);
        var recorder=Recorder.Active;if(recorder==null||!recorder.IsRecording||recorder.IsRolling||string.IsNullOrEmpty(recorder.RecordingPath))throw new Exception("Sustained full recorder unavailable");
        string partial=recorder.RecordingPath;
        var definitions=Encyclopedia.Lookup.Where(p=>p.Value is AircraftDefinition&&p.Value.unitPrefab!=null).Select(p=>(AircraftDefinition)p.Value).Take(4).ToArray();
        var hq=UnityEngine.Object.FindObjectsOfType<FactionHQ>().FirstOrDefault();
        if(definitions.Length==0||hq==null)throw new Exception("Sustained test has no aircraft or faction");
        void SpawnFlight(){for(int i=0;i<definitions.Length;i++)NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null,definitions[i].unitPrefab,null,1f,default(LiveryKey),new GlobalPosition(1000+i*200,3000,1000),Quaternion.identity,new Vector3(0,0,100),null,hq,null,1f,1f);}
        SpawnFlight();
        int seconds=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_LONG_SECONDS"),out int requested)?Math.Max(120,Math.Min(3600,requested)):120;
        float start=Time.realtimeSinceStartup,next=start+60,nextFlight=start+90;int frames=0,flights=1;long maximumQueue=0;
        while(Time.realtimeSinceStartup-start<seconds)
        {
            if(!recorder.IsRecording||!string.IsNullOrEmpty(recorder.RecordingFailure))throw new Exception("Sustained recording stopped: "+recorder.RecordingFailure);
            frames++;maximumQueue=Math.Max(maximumQueue,recorder.RecordingQueuedBytes);
            if(Time.realtimeSinceStartup>=nextFlight){SpawnFlight();flights++;nextFlight+=90;}
            if(Time.realtimeSinceStartup>=next){log("LONG RECORD elapsed="+(Time.realtimeSinceStartup-start).ToString("F0")+"s flights="+flights+" queue="+recorder.RecordingQueuedBytes+" maxQueue="+maximumQueue+" captureFaults="+recorder.CaptureFailureCount);next+=60;}
            await UniTask.NextFrame();
        }
        int faults=recorder.CaptureFailureCount;
        await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Long recording missing: "+ReplayManager.i.LastRecordingStopStatus);
        string integrity=ReplayIntegrityCheck.Check(file);log("LONG RECORD "+integrity);
        if(!integrity.Contains("passed"))throw new Exception("Long recording failed integrity");
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
        if(!ReplayRuntime.IsPlayback||player==null||player.replayDuration<seconds*.75)throw new Exception("Long recording could not reopen or duration was unexpectedly short");
        player.Pause();foreach(double fraction in new[]{.1,.8,.3,.95}){double at=player.replayDuration*fraction;await player.TimelineJump(at,0,false);if(player.IsSeeking||Math.Abs(player.currentVirtualTime-at)>.001||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Long replay seek failed at "+fraction);}
        if(faults!=0)throw new Exception("Long recording opened and sought, but had "+faults+" capture faults");
        log("LONG RECORD PASS "+seconds+" seconds, "+flights+" flights, "+frames+" frames, max queued="+maximumQueue+" bytes, file="+new FileInfo(file).Length+" bytes, duration="+player.replayDuration.ToString("F1")+"s, reopened and sought");
        Environment.SetEnvironmentVariable("REPLAY_EXPECT_AIRCRAFT",(flights*definitions.Length).ToString());Environment.SetEnvironmentVariable("REPLAY_LONG_MIN_SECONDS",(seconds*.98).ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Verify(Path.GetFileName(file),log);
    }
}
