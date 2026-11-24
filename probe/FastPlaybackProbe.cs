using System;
using System.IO;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Director;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class FastPlaybackProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        var baseline=ReplayWorldPlayback.CaptureEnvironment(NetworkSceneSingleton<LevelInfo>.i,0);
        string name="fast-v16-"+Guid.NewGuid().ToString("N")+".replay";string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",name);
        var writer=new RecoverableReplayWriter(file);writer.Start(new ReplayRecordingHeader{MapType=(byte)key.Type,MapPath=key.Path,Resources=new[]{"CAS1"}});await writer.Ready;
        void Add(IReplayEvent e){if(!writer.TryEnqueue(e))throw new Exception("Synthetic writer failed: "+writer.Failure);}
        for(uint id=1;id<=4;id++)Add(new SpawnEvent{Time=0,unitId=id,ownerId=id,jsonKey="CAS1",factionName="no faction",isAircraft=true,weapons=new List<WeaponMount>(),rotation=Quaternion.identity});
        for(int step=0;step<=3000;step++)
        {
            double t=step*.2;var world=new WorldEnvironmentStateEvent();baseline.CopyTo(world);world.Time=t;world.timeOfDay=6+(float)t/50;world.timeFactor=72;world.hasCloudPhase=true;world.cloudDisplacement=new Vector2((float)t*.2f,(float)t*.1f);Add(world);
            for(uint id=1;id<=4;id++)
            {
                Add(new UpdatePositionEvent{Time=t,unitId=id,position=new GlobalPosition((float)t*10,1000,id*100),rotation=Quaternion.identity,velocity=new Vector3(10,0,0)});
                Add(new UpdateInputsEvent{Time=t,id=id,throttle=.7f,pitch=.05f});
            }
        }
        writer.Complete(600);var outcome=await writer.Completion;if(!outcome.Completed)throw new Exception("Synthetic writer failed: "+outcome.Failure);log("FAST format="+ReplayRecordingHeader.CurrentFormat+" "+ReplayIntegrityCheck.Check(file));
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(name);var player=ReplayManager.i.Playback;if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Fast replay did not open");
        player.Pause();await player.TimelineJump(300,0,false);ReplayDirector.i.SetPlaybackSpeed(480);player.Resume();
        float wallStart=Time.realtimeSinceStartup;float deadline=wallStart+60,worstPos=0,worstHour=0,worstCloud=0;int frames=0;double first=player.currentVirtualTime,last=first;
        while(!player.IsPaused)
        {
            if(Time.realtimeSinceStartup>deadline)throw new Exception("480x playback stalled at "+player.currentVirtualTime);
            await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            double t=player.currentVirtualTime;if(t<last)throw new Exception("Fast playback moved backwards");last=t;
            var level=NetworkSceneSingleton<LevelInfo>.i;if(level==null||level.cloudLayer==null)throw new Exception("World missing during fast playback");
            worstHour=Math.Max(worstHour,Math.Abs(level.timeOfDay-(6+(float)t/50)));
            worstCloud=Math.Max(worstCloud,Vector2.Distance(level.cloudLayer.windDisplacement,new Vector2((float)t*.2f,(float)t*.1f)));
            for(uint id=1;id<=4;id++){var actor=player.FindActor(id);if(actor==null)throw new Exception("Actor missing at "+t);var expected=new GlobalPosition((float)t*10,1000,id*100).ToLocalPosition();worstPos=Math.Max(worstPos,Vector3.Distance(actor.transform.position,expected));}
            frames++;
        }
        log("FAST 480x first="+first.ToString("F2")+" end="+last.ToString("F2")+" frames="+frames+" wallSeconds="+(Time.realtimeSinceStartup-wallStart).ToString("F2")+" effectiveRate="+((last-first)/(Time.realtimeSinceStartup-wallStart)).ToString("F1")+" maxPosError="+worstPos.ToString("F3")+" maxHourError="+worstHour.ToString("F3")+" maxCloudError="+worstCloud.ToString("F3")+" failure="+player.PlaybackFailure);
        if(last<599.99||frames<5||worstPos>2||worstHour>.05||worstCloud>.1||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("480x state mismatch");
        ReplayDirector.i.SetPlaybackSpeed(1);log("FAST PASS current-format dense synthetic aircraft and weather stayed aligned throughout 480x playback");
    }
}
