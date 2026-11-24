using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class WorldLiveProbe
{
    static async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayMod.Misc.SceneHelper.LoadSceneAsync(key,"Recorded cloud and time test");
        var level=NetworkSceneSingleton<LevelInfo>.i;if(level==null)throw new Exception("Native level unavailable");
        level.Networkconditions=.25f;level.NetworkcloudHeight=1800;level.NetworktimeOfDay=23.95f;level.SetWindSpeed(12);TimeScaleManager.Scale=1;
        await Wait(2);await ReplayManager.i.SwitchState(ModStates.Record);var recorder=Recorder.Active;
        if(recorder==null||!recorder.IsRecording)throw new Exception("Environment recorder unavailable");string partial=recorder.RecordingPath;
        await Wait(2);level.Networkconditions=.8f;level.NetworkcloudHeight=2400;level.NetworktimeOfDay=.05f;level.SetWindSpeed(20);await Wait(3);int faults=recorder.CaptureFailureCount;await ReplayManager.i.SwitchState(ModStates.Idle);
        string file=partial.EndsWith(".partial",StringComparison.OrdinalIgnoreCase)?partial.Substring(0,partial.Length-8):partial;
        if(!File.Exists(file))throw new Exception("Environment recording missing");ReplayIntegrityCheck.Check(file);
        var samples=new List<WorldStateSnapshot>();using(var input=new BinaryReader(File.OpenRead(file)))
        {var header=ReplayRecordingHeader.Read(input);for(long i=0;i<header.EventCount;i++){var e=ReplayRecordTransport.ReadEvent(input,header.Format);if(e is WorldEnvironmentStateEvent world&&world.hasCloudPhase)samples.Add(WorldStateSnapshot.From(world));ReplayEventFactory.Return(e);}}
        if(samples.Count<10||faults!=0||!samples.Any(s=>s.conditions<.3f && s.timeOfDay>23)||!samples.Any(s=>s.conditions>.7f && s.timeOfDay<1))throw new Exception("Recorded environment transition unavailable: "+samples.Count+" faults="+faults);
        await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null)throw new Exception("Environment replay failed to open");player.Pause();
        foreach(int index in new[]{samples.Count/4,samples.Count*3/4,samples.Count/2})
        {
            var expected=samples[index];await player.TimelineJump(expected.time,0,false);await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            level=NetworkSceneSingleton<LevelInfo>.i;var cloud=level?.cloudLayer;if(cloud==null||cloud.layerMaterial==null)throw new Exception("Native cloud unavailable in replay");
            float phase=Vector2.Distance(cloud.windDisplacement,expected.cloudDisplacement),hour=Math.Abs(level.timeOfDay-expected.timeOfDay);
            var shader=cloud.layerMaterial.GetVector(CloudLayer.id_CloudsOriginOffset);float shaderError=Vector2.Distance(new Vector2(shader.x,shader.y),new Vector2(Datum.originPosition.x+expected.cloudDisplacement.x,Datum.originPosition.z+expected.cloudDisplacement.y));
            log("WORLD LIVE seek="+expected.time.ToString("F2")+" hourError="+hour.ToString("F4")+" cloudPhaseError="+phase.ToString("F4")+" shaderError="+shaderError.ToString("F4")+" conditions="+level.conditions.ToString("F2"));
            if(hour>.01||phase>.05||shaderError>.1||Math.Abs(level.conditions-expected.conditions)>.001)throw new Exception("Recorded environment differs after seek");
        }
        foreach(float speed in new[]{1f,.1f,4f})
        {
            double start=samples[2].time,end=samples[samples.Count-3].time;
            await player.TimelineJump(start,0,false);ReplayMod.Director.ReplayDirector.i.SetPlaybackSpeed(speed);player.Resume();
            float timeout=Time.realtimeSinceStartup+55,worstPhase=0,worstShader=0,worstHour=0,worstConditions=0;int frames=0;
            while(player.currentVirtualTime<end)
            {
                if(Time.realtimeSinceStartup>timeout)throw new Exception("Cloud playback stalled at "+speed+"x");
                await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
                double time=player.currentVirtualTime;int next=samples.FindIndex(s=>s.time>=time);
                if(next<=0||next>=samples.Count)continue;
                var expected=WorldStateSnapshot.At(samples[next-1],samples[next],time);
                level=NetworkSceneSingleton<LevelInfo>.i;var cloud=level?.cloudLayer;
                if(cloud==null||cloud.layerMaterial==null)throw new Exception("Cloud missing during playback");
                worstPhase=Math.Max(worstPhase,Vector2.Distance(cloud.windDisplacement,expected.cloudDisplacement));
                worstHour=Math.Max(worstHour,Math.Abs(Mathf.DeltaAngle(level.timeOfDay*15,expected.timeOfDay*15))/15);worstConditions=Math.Max(worstConditions,Math.Abs(level.conditions-expected.conditions));
                var shader=cloud.layerMaterial.GetVector(CloudLayer.id_CloudsOriginOffset);
                worstShader=Math.Max(worstShader,Vector2.Distance(new Vector2(shader.x,shader.y),new Vector2(Datum.originPosition.x+expected.cloudDisplacement.x,Datum.originPosition.z+expected.cloudDisplacement.y)));
                frames++;
            }
            player.Pause();log("WORLD LIVE continuous speed="+speed+" frames="+frames+" maxPhaseError="+worstPhase.ToString("F4")+" maxShaderError="+worstShader.ToString("F4")+" maxHourError="+worstHour.ToString("F4")+" maxConditionError="+worstConditions.ToString("F4"));
            if(frames<5||worstPhase>.3||worstShader>.3||worstHour>.01||worstConditions>.001)throw new Exception("Cloud phase drift during playback at "+speed+"x");
        }
        log("WORLD LIVE PASS native weather, time and cloud displacement recorded and restored across three seeks; samples="+samples.Count);
    }
}
