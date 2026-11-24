using System;
using System.IO;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.BuildingState)]
public sealed class BuildingStateEvent : IReplayEvent
{
    public uint unitId;
    public bool disabled;
    public double collapseStartTime;
    public float duration;
    public GlobalPosition anchor;
    public Quaternion rotation;
    public double Time { get; set; }
    public EventType EventType => EventType.BuildingState;
    public bool HasValidPayload() => BuildingStateSnapshot.Valid(this);
    public void Execute(object worker=null) { if (worker is UnitController controller) controller.Execute(this); }
    public void Write(BinaryWriter writer)
    {
        if(!HasValidPayload())throw new InvalidDataException("Invalid replay building state");
        writer.Write(Time);writer.Write(unitId);writer.Write(disabled);writer.Write(collapseStartTime);writer.Write(duration);
        writer.Write(anchor.x);writer.Write(anchor.y);writer.Write(anchor.z);
        writer.Write(rotation.x);writer.Write(rotation.y);writer.Write(rotation.z);writer.Write(rotation.w);
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();unitId=reader.ReadUInt32();disabled=reader.ReadBoolean();collapseStartTime=reader.ReadDouble();duration=reader.ReadSingle();
        anchor=new GlobalPosition(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
        rotation=new Quaternion {x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle(),w=reader.ReadSingle()};
        if(!HasValidPayload())throw new InvalidDataException("Invalid replay building state");
    }
    public void Rebase(double cut)
    {
        Time=Math.Max(0,Time-cut);
        // Historical collapse state needs only its completed phase, even when
        // a recent clip begins hours after the building was destroyed.
        collapseStartTime=disabled?Math.Max(-duration,collapseStartTime-cut):0;
    }
    public void Reset()
    { Time=0;unitId=0;disabled=false;collapseStartTime=0;duration=0;anchor=default(GlobalPosition);rotation=default(Quaternion); }
}
