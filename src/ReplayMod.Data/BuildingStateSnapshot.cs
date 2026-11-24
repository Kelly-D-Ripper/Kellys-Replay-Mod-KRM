using System;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Data;

// Original foundation pose and collapse clock survive seek pose folding and
// rolling cuts. Falling is evaluated from replay time, never native async work.
public sealed class BuildingStateSnapshot
{
    public double time, collapseStartTime;
    public bool disabled;
    public float duration;
    public GlobalPosition anchor;
    public Quaternion rotation;
    public static readonly Func<BuildingStateSnapshot, double> Time = sample => sample.time;
    public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    public static bool Valid(BuildingStateEvent value) => value != null && Finite(value.Time) && value.Time >= 0 &&
        Finite(value.collapseStartTime) && value.collapseStartTime >= -120 && Finite(value.duration) && value.duration >= 0 && value.duration <= 120 &&
        (!value.disabled || value.collapseStartTime <= value.Time) &&
        Finite(value.anchor.x) && Finite(value.anchor.y) && Finite(value.anchor.z) &&
        Math.Abs(value.anchor.x) <= 100000000 && Math.Abs(value.anchor.y) <= 100000000 && Math.Abs(value.anchor.z) <= 100000000 &&
        Finite(value.rotation.x) && Finite(value.rotation.y) && Finite(value.rotation.z) && Finite(value.rotation.w) &&
        RotationLength(value.rotation) >= .9 && RotationLength(value.rotation) <= 1.1;
    private static double RotationLength(Quaternion q) => (double)q.x*q.x + (double)q.y*q.y + (double)q.z*q.z + (double)q.w*q.w;
    public static BuildingStateSnapshot From(BuildingStateEvent value) => new BuildingStateSnapshot
    { time=value.Time, collapseStartTime=value.collapseStartTime, disabled=value.disabled, duration=value.duration, anchor=value.anchor, rotation=value.rotation };
    public void CopyTo(BuildingStateEvent value)
    { value.Time=time; value.collapseStartTime=collapseStartTime; value.disabled=disabled; value.duration=duration; value.anchor=anchor; value.rotation=rotation; }
    public bool Changed(BuildingStateSnapshot previous) => previous == null || disabled != previous.disabled ||
        duration != previous.duration || anchor.x != previous.anchor.x || anchor.y != previous.anchor.y || anchor.z != previous.anchor.z ||
        rotation.x != previous.rotation.x || rotation.y != previous.rotation.y || rotation.z != previous.rotation.z || rotation.w != previous.rotation.w;
    public bool Matches(bool nextDisabled,float nextDuration,GlobalPosition nextAnchor,Quaternion nextRotation) =>
        disabled==nextDisabled&&duration==nextDuration&&anchor.x==nextAnchor.x&&anchor.y==nextAnchor.y&&anchor.z==nextAnchor.z&&
        rotation.x==nextRotation.x&&rotation.y==nextRotation.y&&rotation.z==nextRotation.z&&rotation.w==nextRotation.w;
    public float DropAt(double replayTime)
    {
        if (!disabled || !Finite(replayTime)) return 0;
        double elapsed=Math.Max(0,Math.Min(duration,replayTime-collapseStartTime));
        return (float)(.5*9.81*elapsed*elapsed);
    }
    public bool HiddenAt(double replayTime) => disabled && Finite(replayTime) && replayTime >= collapseStartTime + duration;
}
