using System;

namespace ReplayMod.Director;

public enum CameraTakePhase { Idle, Preparing, Countdown, Running, EndHold }
public enum CameraTakeAction { None, StartPlayback, HoldEnd, Finish }

// Frame-driven and unscaled; no delayed task can start a cancelled take later.
public sealed class CameraTakeClock
{
    public CameraTakePhase Phase {get;private set;}
    public bool Active=>Phase!=CameraTakePhase.Idle;
    public bool HoldsPlayback=>Phase==CameraTakePhase.Preparing||Phase==CameraTakePhase.Countdown||Phase==CameraTakePhase.EndHold;
    public long Revision {get;private set;}
    private double deadline;
    public long Prepare(){Cancel();Phase=CameraTakePhase.Preparing;return Revision;}
    public bool IsCurrent(long revision)=>Active&&Revision==revision;
    public bool Ready(long revision,double now,double delay=5)
    {
        if(!IsCurrent(revision)||Phase!=CameraTakePhase.Preparing||!DirectorProject.Finite(now)||!DirectorProject.Range(delay,0,60))return false;
        deadline=now+delay;Phase=CameraTakePhase.Countdown;return true;
    }
    public double Remaining(double now)=>Phase==CameraTakePhase.Countdown?Math.Max(0,deadline-now):0;
    public CameraTakeAction Tick(double now,bool atEnd,bool stopAtEnd,double holdSeconds)
    {
        if(!DirectorProject.Finite(now))return CameraTakeAction.None;
        if(Phase==CameraTakePhase.Countdown&&now>=deadline){Phase=CameraTakePhase.Running;return CameraTakeAction.StartPlayback;}
        if(Phase==CameraTakePhase.Running&&atEnd&&stopAtEnd)
        {deadline=now+(DirectorProject.Finite(holdSeconds)?Math.Max(0,Math.Min(10,holdSeconds)):0);Phase=CameraTakePhase.EndHold;return CameraTakeAction.HoldEnd;}
        if(Phase==CameraTakePhase.EndHold&&now>=deadline){Cancel();return CameraTakeAction.Finish;}
        return CameraTakeAction.None;
    }
    public void Cancel(){Revision++;Phase=CameraTakePhase.Idle;deadline=0;}
}
