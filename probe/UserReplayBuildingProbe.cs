using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

// Opens a supplied recording and compares every replayed building with the
// world rotation captured in its spawn event.
public static class UserReplayBuildingProbe
{
    static async UniTask Until(Func<bool> ready,float timeout,string reason){float end=Time.realtimeSinceStartup+timeout;while(!ready()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(reason);await UniTask.NextFrame();}}
    public static async UniTask Run(string fileName,double at,Action<string> log)
    {
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays",fileName);
        var spawns=new Dictionary<uint,(Quaternion rotation,string key)>();int states=0,identityStates=0;
        using(var input=new BinaryReader(File.OpenRead(path)))
        {
            var header=ReplayRecordingHeader.Read(input);log("USER REPLAY format="+header.Format+" events="+header.EventCount);
            for(long i=0;i<header.EventCount;i++)
            {
                var e=ReplayRecordTransport.ReadEvent(input,header.Format);
                if(e is SpawnEvent spawn&&Encyclopedia.Lookup.TryGetValue(spawn.jsonKey??"",out var definition)&&definition is BuildingDefinition)spawns[spawn.unitId]=(spawn.rotation,spawn.jsonKey);
                if(e is BuildingStateEvent state){states++;if(Quaternion.Angle(state.rotation,Quaternion.identity)<.01f)identityStates++;}
                ReplayMod.Events.ReplayEventFactory.Return(e);
            }
        }
        log("USER REPLAY buildings="+spawns.Count+" buildingStates="+states+" statesWithIdentityRotation="+identityStates);
        await ReplayManager.i.SwitchState(ModStates.Idle);
        await ReplayManager.i.StartPlaying(fileName);
        await Until(()=>ReplayRuntime.IsPlayback&&ReplayManager.i.Playback!=null&&!ReplayManager.i.Playback.IsSeeking,180,"user replay");
        var player=ReplayManager.i.Playback;player.Pause();
        await player.TimelineJump(at,0,false);await UniTask.NextFrame();await UniTask.NextFrame();
        int checkedCount=0,wrong=0;float worst=0;var examples=new List<string>();
        foreach(var pair in spawns)
        {
            var actor=player.FindActor(pair.Key);if(actor==null)continue;checkedCount++;
            float angle=Quaternion.Angle(actor.transform.rotation,pair.Value.rotation);worst=Math.Max(worst,angle);
            if(angle>1){wrong++;if(examples.Count<6)examples.Add(pair.Value.key+" #"+pair.Key+" off by "+angle.ToString("F1")+"deg");}
        }
        log("USER REPLAY t="+at+" checked="+checkedCount+" wrong="+wrong+" worst="+worst.ToString("F1")+"deg "+string.Join("; ",examples));
        log(wrong==0?"USER REPLAY PASS buildings match their recorded orientation":"USER REPLAY FAIL");
    }
}
