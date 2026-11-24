using System;
using System.Collections.Generic;

namespace ReplayMod.Director;

public static class ActorCaptionPolicy
{
    public static bool Ready(bool playback,bool clean,bool seeking,bool buffering,bool reloading,bool show,bool holding)
        =>playback&&!clean&&!seeking&&!buffering&&!reloading&&show&&!holding;
    public static bool TryAttachedTarget(IReadOnlyList<CameraKey> keys,double time,out uint target)
    {
        target=0;if(keys==null||keys.Count==0||!DirectorProject.Finite(time))return false;
        int previous=0,next=keys.Count-1;
        if(time<=keys[0].time)next=0;
        else if(time>=keys[next].time)previous=next;
        else
        {while(next-previous>1){int middle=previous+(next-previous)/2;if(keys[middle].time<=time)previous=middle;else next=middle;}}
        var a=keys[previous];var b=keys[next];
        if(a==null||b==null||!a.relative||!b.relative||a.target==0||a.target!=b.target)return false;
        target=a.target;return true;
    }
}
