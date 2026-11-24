using System;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ReplayMod.Core;
using UnityEngine;

public sealed class DelayScopeOwner:MonoBehaviour {}

public static class DelayScopeProbe
{
    public static async UniTask Run(bool playback,Action<string> log)
    {
        if(ReplayRuntime.IsPlayback!=playback)throw new Exception("Wrong delay test scope");
        var type=typeof(ReplayManager).Assembly.GetType("ReplayMod.Patches.ReplaySceneDelayCancellationPatch");
        var method=type?.GetMethod("Delay",BindingFlags.Static|BindingFlags.Public);
        if(method==null)throw new Exception("Replay delay wrapper unavailable");
        var go=new GameObject("isolated delay lifetime test");var owner=go.AddComponent<DelayScopeOwner>();
        var task=(UniTask)method.Invoke(null,new object[]{10000,false,PlayerLoopTiming.Update,CancellationToken.None,false,owner});
        if(task.Status!=UniTaskStatus.Pending)throw new Exception("Delay was not pending");
        UnityEngine.Object.Destroy(go);await UniTask.NextFrame();await UniTask.NextFrame();
        var expected=playback?UniTaskStatus.Canceled:UniTaskStatus.Pending;
        if(task.Status!=expected)throw new Exception("Delay destruction scope mismatch; expected "+expected+", got "+task.Status);
        if(playback)
        {
            var other=new GameObject("isolated explicit cancellation test");var otherOwner=other.AddComponent<DelayScopeOwner>();
            using(var source=new CancellationTokenSource())
            {
                var original=(UniTask)method.Invoke(null,new object[]{10000,false,PlayerLoopTiming.Update,source.Token,true,otherOwner});
                UnityEngine.Object.Destroy(other);await UniTask.NextFrame();await UniTask.NextFrame();
                if(original.Status!=UniTaskStatus.Pending)throw new Exception("Explicit native token was replaced");
                source.Cancel();if(original.Status!=UniTaskStatus.Canceled)throw new Exception("Native token did not cancel delay");
            }
        }
        log("DELAY SCOPE PASS playback="+playback+"; owner destruction and existing token semantics");
    }
}
