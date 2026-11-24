using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.AircraftPartMotion)]
public sealed class AircraftPartMotionEvent : IReplayEvent
{
    public uint unitId;
    public float blendSeconds;
    public AircraftPartPose[] parts=Array.Empty<AircraftPartPose>();
    public double Time{get;set;}
    public EventType EventType=>EventType.AircraftPartMotion;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    public void Write(BinaryWriter writer)
    {
        if(parts.Length>AircraftPartMotionSnapshot.MaxParts||!AircraftVisualSnapshot.Finite(blendSeconds)||blendSeconds<0||blendSeconds>3600)throw new InvalidDataException("Invalid aircraft part motion");
        writer.Write(Time);writer.Write(unitId);writer.Write(blendSeconds);writer.Write((ushort)parts.Length);
        var ids=new HashSet<byte>();
        foreach(var part in parts)
        {
            if(!ids.Add(part.partId)||!AircraftPartMotionSnapshot.Valid(part))throw new InvalidDataException("Invalid aircraft part pose");
            writer.Write(part.partId);writer.Write((byte)part.flags);writer.Write(part.position.x);writer.Write(part.position.y);writer.Write(part.position.z);
            writer.Write(part.rotation.x);writer.Write(part.rotation.y);writer.Write(part.rotation.z);writer.Write(part.rotation.w);
        }
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();unitId=reader.ReadUInt32();blendSeconds=reader.ReadSingle();int count=reader.ReadUInt16();
        if(count>AircraftPartMotionSnapshot.MaxParts||!AircraftVisualSnapshot.Finite(blendSeconds)||blendSeconds<0||blendSeconds>3600)throw new InvalidDataException("Invalid aircraft part motion");
        parts=new AircraftPartPose[count];var ids=new HashSet<byte>();
        for(int i=0;i<count;i++)
        {
            parts[i]=new AircraftPartPose {partId=reader.ReadByte(),flags=(AircraftPartFlags)reader.ReadByte(),position=new GlobalPosition(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),
                rotation=new Quaternion {x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle(),w=reader.ReadSingle()}};
            if(!ids.Add(parts[i].partId)||!AircraftPartMotionSnapshot.Valid(parts[i]))throw new InvalidDataException("Invalid aircraft part pose");
        }
    }
    public void Reset(){Time=0;unitId=0;blendSeconds=0;parts=Array.Empty<AircraftPartPose>();}
}
