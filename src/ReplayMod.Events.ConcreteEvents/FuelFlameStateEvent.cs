using System;
using System.IO;
using System.Text;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

public enum FuelFlamePhase:byte {Begin,Sample,End}

// Format8: an observed effect instance, including detached fading effects, rather
// than the FuelTank's persistent onFire boolean. IDs survive aircraft despawn.
[ReplayEvent(EventType.FuelFlameState)]
public sealed class FuelFlameStateEvent:IReplayEvent
{
    public uint flameId,unitId;
    public byte partId;
    public ushort prefabSlot;
    public double ownerBirth;
    public string unitKey="",prefabName="";
    public FuelFlamePhase phase;
    public FuelFlameSnapshot snapshot=new FuelFlameSnapshot {rotation=new Quaternion {w=1},localRotation=new Quaternion {w=1}};
    public double Time{get=>snapshot.time;set=>snapshot.time=value;}
    public EventType EventType=>EventType.FuelFlameState;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    public void CopyTo(FuelFlameStateEvent value)
    {value.flameId=flameId;value.unitId=unitId;value.partId=partId;value.prefabSlot=prefabSlot;value.ownerBirth=ownerBirth;value.unitKey=unitKey;value.prefabName=prefabName;value.phase=phase;value.snapshot=snapshot.Copy();}
    public void RebaseTime(double time){ownerBirth+=time-Time;Time=time;}
    public void Write(BinaryWriter writer)
    {
        Validate();writer.Write(Time);writer.Write(flameId);writer.Write((byte)phase);if(phase==FuelFlamePhase.End)return;
        var s=snapshot;writer.Write(s.attached);WriteVector(writer,s.position.x,s.position.y,s.position.z);WriteQuaternion(writer,s.rotation);
        WriteVector(writer,s.localPosition.x,s.localPosition.y,s.localPosition.z);WriteQuaternion(writer,s.localRotation);
        writer.Write(s.age);writer.Write(s.speed);writer.Write(s.lightIntensity);writer.Write(s.blendSeconds);writer.Write(s.emittingMask);writer.Write((byte)s.opacity.Length);
        foreach(float value in s.opacity)writer.Write(value);
        if(phase!=FuelFlamePhase.Begin)return;
        writer.Write(unitId);writer.Write(partId);writer.Write(prefabSlot);writer.Write(ownerBirth);WriteText(writer,unitKey);WriteText(writer,prefabName);
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();flameId=reader.ReadUInt32();phase=(FuelFlamePhase)reader.ReadByte();
        if(phase!=FuelFlamePhase.End)
        {
            snapshot.attached=reader.ReadBoolean();snapshot.position=new GlobalPosition(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());snapshot.rotation=ReadQuaternion(reader);
            snapshot.localPosition=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());snapshot.localRotation=ReadQuaternion(reader);
            snapshot.age=reader.ReadSingle();snapshot.speed=reader.ReadSingle();snapshot.lightIntensity=reader.ReadSingle();snapshot.blendSeconds=reader.ReadSingle();snapshot.emittingMask=reader.ReadUInt32();
            int count=reader.ReadByte();if(count>FuelFlameTimeline.MaxSystems)throw new InvalidDataException("Fuel fire particle count exceeds limit");snapshot.opacity=new float[count];for(int i=0;i<count;i++)snapshot.opacity[i]=reader.ReadSingle();
            if(phase==FuelFlamePhase.Begin){unitId=reader.ReadUInt32();partId=reader.ReadByte();prefabSlot=reader.ReadUInt16();ownerBirth=reader.ReadDouble();unitKey=ReadText(reader);prefabName=ReadText(reader);}
        }
        Validate();
    }
    private static void WriteVector(BinaryWriter writer,float x,float y,float z){writer.Write(x);writer.Write(y);writer.Write(z);}
    private static void WriteQuaternion(BinaryWriter writer,Quaternion q){writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);}
    private static Quaternion ReadQuaternion(BinaryReader reader)=>new Quaternion {x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle(),w=reader.ReadSingle()};
    private static void WriteText(BinaryWriter writer,string text){var bytes=Encoding.UTF8.GetBytes(text);writer.Write((ushort)bytes.Length);writer.Write(bytes);}
    private static string ReadText(BinaryReader reader){int count=reader.ReadUInt16();if(count>512)throw new InvalidDataException("Fuel fire identity exceeds limit");var bytes=reader.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(bytes);}
    public bool HasValidPayload()
    {
        if(flameId==0||!CountermeasureTimeline.Finite(Time)||Time<0||(byte)phase>2)return false;
        if(phase==FuelFlamePhase.End)return true;
        if(!FuelFlameTimeline.Valid(snapshot))return false;
        return phase!=FuelFlamePhase.Begin||(unitId!=0&&prefabSlot<1024&&CountermeasureTimeline.Finite(ownerBirth)&&ownerBirth<=Time+.00001&&
            !string.IsNullOrEmpty(unitKey)&&!string.IsNullOrEmpty(prefabName)&&Encoding.UTF8.GetByteCount(unitKey)<=512&&Encoding.UTF8.GetByteCount(prefabName)<=512);
    }
    private void Validate(){if(!HasValidPayload())throw new InvalidDataException("Invalid recorded fuel fire");}
    public void Reset(){flameId=unitId=0;partId=0;prefabSlot=0;ownerBirth=0;unitKey=prefabName="";phase=FuelFlamePhase.Begin;snapshot=new FuelFlameSnapshot {rotation=new Quaternion {w=1},localRotation=new Quaternion {w=1}};}
}
