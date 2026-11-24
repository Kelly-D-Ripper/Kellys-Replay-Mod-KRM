using UnityEngine;

namespace ReplayMod.Core;

[DefaultExecutionOrder(10000)]
public sealed class ReplayAircraftVisualLatePass : MonoBehaviour
{
    private void LateUpdate()
    {
        if(!ReplayRuntime.IsPlayback)return;
        var playback=ReplayManager.i.Playback;
        if(playback!=null&&!playback.IsSeeking)playback.RefreshAircraftVisualsLate();
    }
}
