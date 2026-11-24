using System;
using System.IO;
using System.Text;
using ReplayMod.Core;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

public enum CountermeasurePhase : byte { Launch, Sample, End }
public enum CountermeasureKind : byte { Flare, Chaff }

// Format 7. Launch identifies an observed native prefab; samples contain the
// resolved trajectory, never a command which would randomize the ejection again.
[ReplayEvent(EventType.Countermeasure)]
public sealed class CountermeasureEvent : IReplayEvent
{
    public uint countermeasureId, unitId;
    public CountermeasurePhase phase;
    public CountermeasureKind kind;
    public ushort prefabSlot;
    public string unitKey = "", prefabName = "";
    public GlobalPosition position;
    public Quaternion rotation;
    public Vector3 velocity;
    public float age, remaining, endAge = -1, tailSeconds;
    public CountermeasureSnapshot[] history = Array.Empty<CountermeasureSnapshot>();
    public double Time { get; set; }
    public EventType EventType => EventType.Countermeasure;
    public void Execute(object worker = null) { if (worker is UnitController controller) controller.Execute(this); }
    public CountermeasureSnapshot Snapshot => new CountermeasureSnapshot { time=Time,position=position,rotation=rotation,velocity=velocity,age=age,remaining=remaining };
    public void SetSnapshot(CountermeasureSnapshot value)
    { Time=value.time; position=value.position; rotation=value.rotation; velocity=value.velocity; age=value.age; remaining=value.remaining; }
    public void CopyTo(CountermeasureEvent target)
    {
        target.countermeasureId=countermeasureId; target.unitId=unitId; target.phase=phase; target.kind=kind;
        target.prefabSlot=prefabSlot; target.unitKey=unitKey; target.prefabName=prefabName;
        target.SetSnapshot(Snapshot); target.endAge=endAge; target.tailSeconds=tailSeconds;
        target.history=(CountermeasureSnapshot[])history.Clone();
    }
    public void RebaseTime(double time)
    {
        double shift=time-Time;
        for(int i=0;i<history.Length;i++) history[i].time+=shift;
        Time=time;
    }
    public void Write(BinaryWriter writer)
    {
        Validate();
        writer.Write(Time); writer.Write(countermeasureId); writer.Write((byte)phase);
        WriteSnapshot(writer,Snapshot,false); writer.Write(endAge); writer.Write(tailSeconds);
        if(phase!=CountermeasurePhase.Launch)return;
        writer.Write(unitId);writer.Write((byte)kind);writer.Write(prefabSlot);WriteText(writer,unitKey);WriteText(writer,prefabName);
        writer.Write((ushort)history.Length);
        foreach(var sample in history)
        {writer.Write((float)(sample.time-Time));WriteSnapshot(writer,sample,false);}
    }
    public void Read(BinaryReader reader)
    {
        Time=reader.ReadDouble();countermeasureId=reader.ReadUInt32();phase=(CountermeasurePhase)reader.ReadByte();
        SetSnapshot(ReadSnapshot(reader,Time));endAge=reader.ReadSingle();tailSeconds=reader.ReadSingle();
        if(phase==CountermeasurePhase.Launch)
        {
            unitId=reader.ReadUInt32();kind=(CountermeasureKind)reader.ReadByte();prefabSlot=reader.ReadUInt16();unitKey=ReadText(reader);prefabName=ReadText(reader);
            int count=reader.ReadUInt16();if(count>CountermeasureTimeline.MaxHistory)throw new InvalidDataException("Countermeasure history exceeds limit");
            history=new CountermeasureSnapshot[count];
            for(int i=0;i<count;i++)history[i]=ReadSnapshot(reader,Time+reader.ReadSingle());
        }
        else history=Array.Empty<CountermeasureSnapshot>();
        Validate();
    }
    private static void WriteText(BinaryWriter writer,string value)
    {var bytes=Encoding.UTF8.GetBytes(value??"");if(bytes.Length>512)throw new InvalidDataException("Countermeasure identity exceeds limit");writer.Write((ushort)bytes.Length);writer.Write(bytes);}
    private static string ReadText(BinaryReader reader)
    {int count=reader.ReadUInt16();if(count>512)throw new InvalidDataException("Countermeasure identity exceeds limit");var bytes=reader.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(bytes);}
    private static void WriteSnapshot(BinaryWriter writer,CountermeasureSnapshot value,bool includeTime)
    {
        writer.Write(value.position.x);writer.Write(value.position.y);writer.Write(value.position.z);
        writer.Write(value.rotation.x);writer.Write(value.rotation.y);writer.Write(value.rotation.z);writer.Write(value.rotation.w);
        writer.Write(value.velocity.x);writer.Write(value.velocity.y);writer.Write(value.velocity.z);writer.Write(value.age);writer.Write(value.remaining);
    }
    private static CountermeasureSnapshot ReadSnapshot(BinaryReader reader,double time)=>new CountermeasureSnapshot
    {time=time,position=new GlobalPosition(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),
        rotation=new Quaternion {x=reader.ReadSingle(),y=reader.ReadSingle(),z=reader.ReadSingle(),w=reader.ReadSingle()},
        velocity=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),age=reader.ReadSingle(),remaining=reader.ReadSingle()};
    public bool HasValidPayload()
    {
        if(countermeasureId==0||!CountermeasureTimeline.Finite(Time)||Time<0||(byte)phase>2||!CountermeasureTimeline.Valid(Snapshot)||
            !CountermeasureTimeline.Finite(endAge)||endAge < -1||endAge>age+.001f||!CountermeasureTimeline.Finite(tailSeconds)||tailSeconds<0||tailSeconds>CountermeasureTimeline.TrailSeconds)return false;
        if(phase==CountermeasurePhase.End&&(endAge<0||remaining!=0))return false;
        if(phase!=CountermeasurePhase.Launch)return history.Length==0;
        if(unitId==0||(byte)kind>1||prefabSlot>=CountermeasureTimeline.MaxPrefabSlots||string.IsNullOrEmpty(unitKey)||string.IsNullOrEmpty(prefabName)||
            Encoding.UTF8.GetByteCount(unitKey)>512||Encoding.UTF8.GetByteCount(prefabName)>512||history.Length>CountermeasureTimeline.MaxHistory)return false;
        double previous=double.NegativeInfinity;float previousAge=-1;
        foreach(var sample in history)
        {
            if(!CountermeasureTimeline.Valid(sample)||sample.time>Time+.0001||sample.time<Time-CountermeasureTimeline.TrailSeconds-.2||sample.time<previous||sample.age<previousAge||sample.age>age+.001f)return false;
            previous=sample.time;previousAge=sample.age;
        }
        return true;
    }
    private void Validate(){if(!HasValidPayload())throw new InvalidDataException("Invalid replay countermeasure payload");}
    public void Reset()
    {
        Time=0;countermeasureId=unitId=0;phase=CountermeasurePhase.Launch;kind=CountermeasureKind.Flare;prefabSlot=0;unitKey=prefabName="";
        position=default;rotation=default;velocity=default;age=remaining=tailSeconds=0;endAge=-1;history=Array.Empty<CountermeasureSnapshot>();
    }
}
