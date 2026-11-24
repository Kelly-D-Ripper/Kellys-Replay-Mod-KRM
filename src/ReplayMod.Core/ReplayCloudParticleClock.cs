using System;
using ReplayMod.Misc;
using UnityEngine;

namespace ReplayMod.Core;

// Congratulations, slowing down time has somehow invented more clouds.
// CloudLayer manually emits nearby puffs every render frame. Native gameplay
// clocks age those puffs in scaled time, so replay pauses/slow motion otherwise
// accumulate particles. Use the exact three clocks selected by native Editor.
public static class ReplayCloudParticleClock
{
    private static readonly ReplayCloudClockState<ParticleSystem> clocks=new ReplayCloudClockState<ParticleSystem>();
    private static readonly Func<ParticleSystem,bool> Alive=system=>system!=null;
    private static readonly Func<ParticleSystem,bool> ReadClock=system=>system.main.useUnscaledTime;
    private static readonly Action<ParticleSystem,bool> WriteClock=(system,value)=>{var main=system.main;main.useUnscaledTime=value;};
    private static readonly Func<ParticleSystem,int> ReadCount=system=>system.particleCount;
    private static CloudLayer owner,failedOwner;
    public static int ProtectedSystemCount=>clocks.Count;
    public static int OriginalUnscaledSystemCount=>clocks.OriginalUnscaledCount;
    public static int CurrentParticleCount=>clocks.Sum(Alive,ReadCount);
    public static void Ensure(CloudLayer cloud)
    {
        var level=NetworkSceneSingleton<LevelInfo>.i;
        bool scoped=ReplayRuntime.IsPlayback&&ReplayManager.i?.Playback!=null&&!GameManager.IsHeadless&&
            !SceneHelper.IsTransitionPending&&cloud!=null&&level!=null&&ReferenceEquals(level.cloudLayer,cloud);
        if(!scoped)
        {
            // An old layer can keep updating while a new scene initializes. It
            // must never clear a newer layer's independently captured clocks.
            if(ReferenceEquals(owner,cloud)&&!ReferenceEquals(owner,null))Clear();
            return;
        }
        if(ReferenceEquals(failedOwner,cloud))return;
        if(!ReferenceEquals(owner,cloud))Clear();
        if(clocks.Matches(cloud.cloudSystem,cloud.distantCloudSystem,cloud.flyThroughSystem))return;
        try
        {
            clocks.Bind(cloud.cloudSystem,cloud.distantCloudSystem,cloud.flyThroughSystem,Alive,ReadClock,WriteClock);
            owner=cloud;
        }
        catch(Exception error)
        {
            clocks.Clear(Alive,WriteClock);owner=null;failedOwner=cloud;
            Plugin.logger.LogWarning("Replay cloud particle clocks unavailable; native cloud update continues: "+error.Message);
        }
    }
    public static void Clear()
    {
        owner=null;failedOwner=null;
        int failures=clocks.Clear(Alive,WriteClock);
        if(failures>0)Plugin.logger.LogWarning("Replay cloud particle clock restore failures: "+failures);
    }
}
