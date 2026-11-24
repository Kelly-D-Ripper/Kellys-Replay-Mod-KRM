using System.IO;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.PilotEjectionState)]
public sealed class PilotEjectionStateEvent : IReplayEvent
{
    public uint unitId,aircraftId;
    public double aircraftBirth;
    public byte seat;
    public bool capsule,orphanedAircraft;
    public GlobalPosition eye;
    public Quaternion rotation;
    public double Time {get;set;}
    public EventType EventType=>EventType.PilotEjectionState;
    public bool HasValidPayload()=>unitId!=0&&aircraftId!=0&&(capsule?unitId==aircraftId&&!orphanedAircraft:unitId!=aircraftId)&&seat<16&&
        DirectorProject.Range(Time,0,ReplayRecordingHeader.MaximumDurationSeconds)&&DirectorProject.Range(aircraftBirth,0,Time)&&
        DirectorProject.Range(eye.x,-1e8,1e8)&&DirectorProject.Range(eye.y,-1e8,1e8)&&DirectorProject.Range(eye.z,-1e8,1e8)&&
        DirectorProject.Range((double)rotation.x*rotation.x+(double)rotation.y*rotation.y+(double)rotation.z*rotation.z+(double)rotation.w*rotation.w,.9,1.1);
    public void Execute(object worker){if(worker is UnitController controller)controller.Execute(this);}
    public void Write(BinaryWriter writer)
    {
        if(!HasValidPayload())throw new InvalidDataException("Invalid recorded ejection eye");
        writer.Write(Time);writer.Write(unitId);writer.Write(aircraftId);writer.Write(aircraftBirth);writer.Write(seat);writer.Write(capsule);writer.Write(orphanedAircraft);
        writer.Write(eye.x);writer.Write(eye.y);writer.Write(eye.z);writer.Write(rotation.x);writer.Write(rotation.y);writer.Write(rotation.z);writer.Write(rotation.w);
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();unitId=reader.ReadUInt32();aircraftId=reader.ReadUInt32();aircraftBirth=reader.ReadDouble();seat=reader.ReadByte();capsule=reader.ReadBoolean();orphanedAircraft=reader.ReadBoolean();
        eye=new GlobalPosition(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());rotation=new Quaternion {x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle(),w=reader.ReadSingle()};
        if(!HasValidPayload())throw new InvalidDataException("Invalid recorded ejection eye");
    }
    public void Rebase(double cut)
    {Time=System.Math.Max(0,Time-cut);aircraftBirth=System.Math.Max(0,aircraftBirth-cut);}
    public void Reset(){Time=0;unitId=aircraftId=0;aircraftBirth=0;seat=0;capsule=orphanedAircraft=false;eye=default;rotation=default;}
}
