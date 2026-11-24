using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

public struct CargoParachutePose
{
    public string key;
    public bool canopyActive,linesActive;
    public GlobalPosition canopyPosition,linesPosition;
    public Quaternion canopyRotation,linesRotation;
    public Vector3 canopyScale,linesScale;
}

// A detached native canopy is not driven by a kinematic replay vehicle. Save
// its observed presentation rather than resimulating forces on playback.
[ReplayEvent(EventType.CargoParachuteState)]
public sealed class CargoParachuteStateEvent:IReplayEvent
{
    public uint unitId;
    public bool present;
    public CargoParachutePose[] parachutes=Array.Empty<CargoParachutePose>();
    public double Time{get;set;}
    public EventType EventType=>EventType.CargoParachuteState;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private static bool Vec(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z)&&Math.Abs(v.x)<=10000&&Math.Abs(v.y)<=10000&&Math.Abs(v.z)<=10000;
    private static bool Pos(GlobalPosition p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&Math.Abs(p.x)<=1e8&&Math.Abs(p.y)<=1e8&&Math.Abs(p.z)<=1e8;
    private static bool Rot(Quaternion q)
    {double n=(double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w;return Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w)&&n>=.9&&n<=1.1;}
    private static bool Valid(CargoParachutePose p)=>!string.IsNullOrEmpty(p.key)&&System.Text.Encoding.UTF8.GetByteCount(p.key)<=512&&
        Pos(p.canopyPosition)&&Pos(p.linesPosition)&&Rot(p.canopyRotation)&&Rot(p.linesRotation)&&Vec(p.canopyScale)&&Vec(p.linesScale);
    public void Write(BinaryWriter w)
    {
        if(unitId==0||parachutes==null||parachutes.Length>64||(!present&&parachutes.Length!=0))throw new InvalidDataException("Invalid cargo parachute snapshot");
        w.Write(Time);w.Write(unitId);w.Write(present);w.Write((byte)parachutes.Length);var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in parachutes)
        {
            if(!Valid(p)||!keys.Add(p.key))throw new InvalidDataException("Invalid cargo parachute pose");
            w.Write(p.key);w.Write(p.canopyActive);w.Write(p.linesActive);
            WritePos(w,p.canopyPosition);WriteRot(w,p.canopyRotation);WriteVec(w,p.canopyScale);
            WritePos(w,p.linesPosition);WriteRot(w,p.linesRotation);WriteVec(w,p.linesScale);
        }
    }
    public void Read(BinaryReader r)
    {
        Time=r.ReadDouble();unitId=r.ReadUInt32();present=r.ReadBoolean();int count=r.ReadByte();
        if(unitId==0||count>64||(!present&&count!=0))throw new InvalidDataException("Invalid cargo parachute count");
        parachutes=new CargoParachutePose[count];var keys=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<count;i++)
        {
            var p=new CargoParachutePose{key=ReplayRecordingHeader.ReadString(r,512),canopyActive=r.ReadBoolean(),linesActive=r.ReadBoolean(),
                canopyPosition=ReadPos(r),canopyRotation=ReadRot(r),canopyScale=ReadVec(r),linesPosition=ReadPos(r),linesRotation=ReadRot(r),linesScale=ReadVec(r)};
            if(!Valid(p)||!keys.Add(p.key))throw new InvalidDataException("Invalid cargo parachute pose");parachutes[i]=p;
        }
    }
    private static void WritePos(BinaryWriter w,GlobalPosition p){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
    private static void WriteRot(BinaryWriter w,Quaternion q){w.Write(q.x);w.Write(q.y);w.Write(q.z);w.Write(q.w);}
    private static void WriteVec(BinaryWriter w,Vector3 p){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
    private static GlobalPosition ReadPos(BinaryReader r)=>new GlobalPosition(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    private static Quaternion ReadRot(BinaryReader r)=>new Quaternion{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle(),w=r.ReadSingle()};
    private static Vector3 ReadVec(BinaryReader r)=>new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    public void Reset(){Time=0;unitId=0;present=false;parachutes=Array.Empty<CargoParachutePose>();}
}
