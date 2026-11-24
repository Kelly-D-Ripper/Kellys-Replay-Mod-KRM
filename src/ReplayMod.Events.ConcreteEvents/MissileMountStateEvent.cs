using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

// Format 12. Presentation state of missiles, rockets and bombs still mounted on
// an aircraft, keyed by native part ID and part-local slot. The local pose carries
// native rail, trapeze and bay-drop motion; the flying projectile is recorded separately.
[ReplayEvent(EventType.MissileMountState)]
public sealed class MissileMountStateEvent:IReplayEvent
{
    public const int MaximumMounts=256;
    public uint unitId;
    public CargoMountPose[] mounts=Array.Empty<CargoMountPose>();
    public double Time{get;set;}
    public EventType EventType=>EventType.MissileMountState;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private static bool Valid(CargoMountPose p)
    {
        if(string.IsNullOrEmpty(p.key)||System.Text.Encoding.UTF8.GetByteCount(p.key)>512||p.ammo<0||p.ammo>10000)return false;
        var v=p.position;var q=p.rotation;
        if(!Finite(v.x)||!Finite(v.y)||!Finite(v.z)||Math.Abs(v.x)>10000||Math.Abs(v.y)>10000||Math.Abs(v.z)>10000)return false;
        if(!Finite(q.x)||!Finite(q.y)||!Finite(q.z)||!Finite(q.w))return false;
        double length=(double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w;
        return length>=.9&&length<=1.1;
    }
    public void Write(BinaryWriter w)
    {
        if(unitId==0||mounts==null||mounts.Length>MaximumMounts)throw new InvalidDataException("Invalid missile mount snapshot");
        w.Write(Time);w.Write(unitId);w.Write((ushort)mounts.Length);var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in mounts)
        {
            if(!Valid(p)||!keys.Add(p.key))throw new InvalidDataException("Invalid missile mount pose");
            w.Write(p.key);w.Write(p.active);w.Write(p.fired);w.Write(p.ammo);
            w.Write(p.position.x);w.Write(p.position.y);w.Write(p.position.z);
            w.Write(p.rotation.x);w.Write(p.rotation.y);w.Write(p.rotation.z);w.Write(p.rotation.w);
        }
    }
    public void Read(BinaryReader r)
    {
        Time=r.ReadDouble();unitId=r.ReadUInt32();int count=r.ReadUInt16();
        if(unitId==0||count>MaximumMounts)throw new InvalidDataException("Invalid missile mount count");
        mounts=new CargoMountPose[count];var keys=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<count;i++)
        {
            var p=new CargoMountPose{key=ReplayRecordingHeader.ReadString(r,512),active=r.ReadBoolean(),fired=r.ReadBoolean(),ammo=r.ReadInt32(),
                position=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()),rotation=new Quaternion{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle(),w=r.ReadSingle()}};
            if(!Valid(p)||!keys.Add(p.key))throw new InvalidDataException("Invalid missile mount pose");mounts[i]=p;
        }
    }
    public void Reset(){Time=0;unitId=0;mounts=Array.Empty<CargoMountPose>();}
}
