using System;
using System.IO;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;

public static class BackwardReplayProbe
{
    static string Hash(string path){using(var hash=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(file));}
    public static async UniTask Run(string original,Action<string> log)
    {
        string source=Path.GetFullPath(original),before=Hash(source),folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays");
        string name="provided-legacy-"+Guid.NewGuid().ToString("N")+".replay",copy=Path.Combine(folder,name);File.Copy(source,copy,false);
        if(Hash(copy)!=before)throw new Exception("Legacy fixture copy differs from source");
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(name);
        var player=ReplayManager.i.Playback;if(!ReplayRuntime.IsPlayback||player==null||player.replayDuration<90)throw new Exception("Provided older replay did not open");
        player.Pause();foreach(double time in new[]{2d,45d,90d,5d})
        {
            await player.TimelineJump(time,0,false);
            if(player.IsSeeking||Math.Abs(player.currentVirtualTime-time)>.001||!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Provided older replay failed seek at "+time);
        }
        if(Hash(source)!=before)throw new Exception("Provided source replay changed");
        log("LEGACY PROVIDED PASS copied format-7 recording reopened and passed four seeks; source hash unchanged");
    }
}
