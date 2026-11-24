using System;
using System.IO;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using UnityEngine;

public static class LongTimelineProbe
{
    public static async UniTask Run(string name,Action<string> log)
    {
        if(string.IsNullOrEmpty(name)||name!=Path.GetFileName(name))throw new Exception("Expected isolated replay filename");
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(name);
        var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null||Math.Abs(player.replayDuration-7200)>.01)throw new Exception("Synthetic two-hour replay did not open");
        player.Pause();foreach(double at in new[]{1d,1234.5,7199.8,3600.1,12.2,6000d})
        {
            await player.TimelineJump(at,0,false);await UniTask.NextFrame();
            if(player.IsSeeking||Math.Abs(player.currentVirtualTime-at)>.001||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Two-hour seek failed at "+at);
            for(uint id=1;id<=4;id++)
            {
                var actor=player.FindActor(id);if(actor==null)throw new Exception("Actor missing at "+at+" id="+id);
                var expected=new GlobalPosition((float)(at*10),1000,id*100).ToLocalPosition();float error=Vector3.Distance(actor.transform.position,expected);
                if(error>1)throw new Exception("Actor position differs at "+at+" id="+id+" by "+error);
            }
            log("TWO HOUR SEEK PASS time="+at+" actors=4");
        }
        log("TWO HOUR UNITY PASS synthetic replay opened and six forward/backward seeks restored all four expected positions");
    }
}
