using System;
using System.IO;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.AircraftVisualState)]
public sealed class AircraftVisualStateEvent : IReplayEvent
{
    public uint unitId,layoutId;
    public bool gearDeployed;
    public byte gearState;
    public float blendSeconds;
    public AircraftVisualPose[] poses=Array.Empty<AircraftVisualPose>();
    public double Time {get;set;}
    public EventType EventType=>EventType.AircraftVisualState;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    public void Write(BinaryWriter writer)
    {
        if(poses.Length>AircraftVisualSnapshot.MaxChannels||gearState>4||!AircraftVisualSnapshot.Finite(blendSeconds)||blendSeconds<0||blendSeconds>3600)throw new InvalidDataException("Invalid aircraft visual state");
        writer.Write(Time);writer.Write(unitId);writer.Write(layoutId);writer.Write(gearDeployed);writer.Write(gearState);writer.Write(blendSeconds);writer.Write((ushort)poses.Length);
        foreach(var pose in poses)
        {
            if(!AircraftVisualSnapshot.Valid(pose))throw new InvalidDataException("Non-finite aircraft visual pose");
            writer.Write(pose.position.x);writer.Write(pose.position.y);writer.Write(pose.position.z);
            writer.Write(pose.rotation.x);writer.Write(pose.rotation.y);writer.Write(pose.rotation.z);writer.Write(pose.rotation.w);
        }
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();unitId=reader.ReadUInt32();layoutId=reader.ReadUInt32();gearDeployed=reader.ReadBoolean();gearState=reader.ReadByte();blendSeconds=reader.ReadSingle();int count=reader.ReadUInt16();
        if(count>AircraftVisualSnapshot.MaxChannels||gearState>4||!AircraftVisualSnapshot.Finite(blendSeconds)||blendSeconds<0||blendSeconds>3600)throw new InvalidDataException("Invalid aircraft visual state");
        poses=new AircraftVisualPose[count];
        for(int i=0;i<count;i++)
        {
            poses[i]=new AircraftVisualPose {position=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),
                rotation=new Quaternion {x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle(),w=reader.ReadSingle()}};
            if(!AircraftVisualSnapshot.Valid(poses[i]))throw new InvalidDataException("Non-finite aircraft visual pose");
        }
    }
    public void Reset(){Time=0;unitId=layoutId=0;gearDeployed=false;gearState=0;blendSeconds=0;poses=Array.Empty<AircraftVisualPose>();}
}
