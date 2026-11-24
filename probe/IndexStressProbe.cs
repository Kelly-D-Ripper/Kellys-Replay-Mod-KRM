using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using ReplayMod.Misc;

// Isolated process only: exercise repeated real index builds while GC and a
// second reader operate concurrently. The original fixture is read-only.
public static class IndexStressProbe
{
    private static string Hash(string path){using(var sha=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(input));}
    public static async UniTask Run(Action<string> log)
    {
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays","test.replay"),before=Hash(path);
        var stop=new CancellationTokenSource();
        var collector=Task.Run(()=>{while(!stop.IsCancellationRequested){GC.Collect();Thread.Sleep(50);}});
        try
        {
            var readers=Enumerable.Range(0,4).Select(worker=>Task.Run(()=>
            {
                for(int i=0;i<500;i++)
                {
                    using(var stream=new MemoryStream())
                    {
                        using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){writer.Write((short)(1000+worker));writer.Write((short)(-2000-worker));}
                        stream.Position=0;using(var reader=new BinaryReader(stream))
                        {
                            var first=EventsHelper.GetInputs(1,1,reader,10000);
                            if(Math.Abs(first.pitch-(1000+worker)/10000f)>.0001f)throw new Exception("Legacy reader pitch crossed streams");
                            var next=EventsHelper.GetInputs(1,8,reader,10000);
                            if(Math.Abs(next.pitch-(1000+worker)/10000f)>.0001f||Math.Abs(next.throttle-(-2000-worker)/10000f)>.0001f)throw new Exception("Legacy reader history crossed streams");
                        }
                    }
                }
            })).ToArray();
            for(int i=0;i<8;i++)
            {
                await ReplayManager.i.SwitchState(ModStates.Idle);
                var verifier=Task.Run(()=>ReplayIntegrityCheck.Check(path));
                await ReplayManager.i.StartPlaying("test.replay");
                string integrity=await verifier;
                var player=ReplayManager.i.Playback;
                if(!ReplayRuntime.IsPlayback||player==null||!integrity.Contains("passed"))throw new Exception("Indexed replay failed to open at iteration "+i);
                player.Pause();await player.TimelineJump(Math.Min(1,player.replayDuration),0,false);
                if(!string.IsNullOrEmpty(player.PlaybackFailure))throw new Exception("Replay seek failed during index stress: "+player.PlaybackFailure);
                log("INDEX STRESS "+(i+1)+"/8 opened, validated and sought");
            }
            await Task.WhenAll(readers);
        }
        finally{stop.Cancel();try{await collector;}catch(OperationCanceledException){}stop.Dispose();}
        if(Hash(path)!=before)throw new Exception("Index stress changed original fixture");
        log("INDEX STRESS PASS 8 repeated replay opens with concurrent independent validation, 2000 legacy readers and forced GC; source hash unchanged");
    }
}
