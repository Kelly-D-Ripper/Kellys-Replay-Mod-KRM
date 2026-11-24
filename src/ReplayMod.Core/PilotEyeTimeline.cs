using System;
using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

public sealed class PilotEyeTimeline
{
    private readonly List<PositionSnapshot> poses=new List<PositionSnapshot>();
    public uint Aircraft {get;private set;}
    public double AircraftBirth {get;private set;}
    public byte Seat {get;private set;}
    public bool Capsule {get;private set;}
    // A recent clip can retain an ejected pilot after its parent lifetime ended.
    // Its zero-rebased birth must never identify a replacement aircraft at zero.
    public double SourceBirth=>orphanedAircraft?-1:AircraftBirth;
    private bool orphanedAircraft;
    public void Add(PilotEjectionStateEvent value)
    {
        if(!value.HasValidPayload())return;
        if(poses.Count>0&&(Aircraft!=value.aircraftId||AircraftBirth!=value.aircraftBirth||Seat!=value.seat||Capsule!=value.capsule||orphanedAircraft!=value.orphanedAircraft))poses.Clear();
        Aircraft=value.aircraftId;AircraftBirth=value.aircraftBirth;Seat=value.seat;Capsule=value.capsule;orphanedAircraft=value.orphanedAircraft;
        SnapshotHistory.Insert(poses,new PositionSnapshot {time=value.Time,position=value.eye,rotation=value.rotation},pose=>pose.time);
        if(poses.Count>128)poses.RemoveRange(0,poses.Count-128);
    }
    public bool TryAt(double time,out PositionSnapshot pose)
    {
        pose=default;if(!SnapshotHistory.Locate(poses,time,item=>item.time,out int left,out int right,out _))return false;
        pose=ReplayPoseInterpolation.At(poses[left],poses[right],time);
        SnapshotHistory.Trim(poses,Math.Max(0,left-1),128);return true;
    }
}
