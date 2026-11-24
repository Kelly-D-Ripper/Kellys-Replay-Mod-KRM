using System;
using ReplayMod.Data;
using UnityEngine;
namespace ReplayMod.Core;

// The same position/velocity Lerp and rotation Slerp used by UnitController.
// A rolling clip stores this pose at its actual cut, then rebases it to time zero.
public static class ReplayPoseInterpolation
{
    public static PositionSnapshot At(PositionSnapshot previous, PositionSnapshot next, double time)
    {
        float fraction = next.time > previous.time ? (float)Math.Max(0, Math.Min(1, (time-previous.time)/(next.time-previous.time))) : 0;
        return new PositionSnapshot
        {
            time=time,
            position=new GlobalPosition(
                previous.position.x+(next.position.x-previous.position.x)*fraction,
                previous.position.y+(next.position.y-previous.position.y)*fraction,
                previous.position.z+(next.position.z-previous.position.z)*fraction),
            rotation=Quaternion.Slerp(previous.rotation,next.rotation,fraction),
            velocity=Vector3.Lerp(previous.velocity,next.velocity,fraction)
        };
    }
}
