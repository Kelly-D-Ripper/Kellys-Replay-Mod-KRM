using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Events.ConcreteEvents;

public struct CargoMountPose
{
    public string key;
    public bool active,fired;
    public int ammo;
    public Vector3 position;
    public Quaternion rotation;
}

// This is presentation state for mounts still belonging to a carrier. The
// separately recorded vehicle spawn remains the sole source of deployed units.
[ReplayEvent(EventType.CargoMountState)]
public sealed class CargoMountStateEvent:IReplayEvent
{
    public uint unitId;
    public CargoMountPose[] mounts=Array.Empty<CargoMountPose>();
    public double Time{get;set;}
    public EventType EventType=>EventType.CargoMountState;
    public void Execute(object worker=null){if(worker is UnitController controller)controller.Execute(this);}
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private static bool Valid(CargoMountPose p)=>!string.IsNullOrEmpty(p.key)&&System.Text.Encoding.UTF8.GetByteCount(p.key)<=512&&p.ammo>=0&&p.ammo<=10000&&
        Finite(p.position.x)&&Finite(p.position.y)&&Finite(p.position.z)&&Math.Abs(p.position.x)<=10000&&Math.Abs(p.position.y)<=10000&&Math.Abs(p.position.z)<=10000&&
        Finite(p.rotation.x)&&Finite(p.rotation.y)&&Finite(p.rotation.z)&&Finite(p.rotation.w)&&
        (double)p.rotation.x*p.rotation.x+(double)p.rotation.y*p.rotation.y+(double)p.rotation.z*p.rotation.z+(double)p.rotation.w*p.rotation.w>=.9&&
        (double)p.rotation.x*p.rotation.x+(double)p.rotation.y*p.rotation.y+(double)p.rotation.z*p.rotation.z+(double)p.rotation.w*p.rotation.w<=1.1;
    public void Write(BinaryWriter w)
    {
        if(unitId==0||mounts==null||mounts.Length>128)throw new InvalidDataException("Invalid cargo mount snapshot");
        w.Write(Time);w.Write(unitId);w.Write((ushort)mounts.Length);var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in mounts)
        {
            if(!Valid(p)||!keys.Add(p.key))throw new InvalidDataException("Invalid cargo mount pose");
            w.Write(p.key);w.Write(p.active);w.Write(p.fired);w.Write(p.ammo);
            w.Write(p.position.x);w.Write(p.position.y);w.Write(p.position.z);
            w.Write(p.rotation.x);w.Write(p.rotation.y);w.Write(p.rotation.z);w.Write(p.rotation.w);
        }
    }
    public void Read(BinaryReader r)
    {
        Time=r.ReadDouble();unitId=r.ReadUInt32();int count=r.ReadUInt16();
        if(unitId==0||count>128)throw new InvalidDataException("Invalid cargo mount count");
        mounts=new CargoMountPose[count];var keys=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<count;i++)
        {
            var p=new CargoMountPose{key=ReplayRecordingHeader.ReadString(r,512),active=r.ReadBoolean(),fired=r.ReadBoolean(),ammo=r.ReadInt32(),
                position=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()),rotation=new Quaternion{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle(),w=r.ReadSingle()}};
            if(!Valid(p)||!keys.Add(p.key))throw new InvalidDataException("Invalid cargo mount pose");mounts[i]=p;
        }
    }
    public void Reset(){Time=0;unitId=0;mounts=Array.Empty<CargoMountPose>();}
}
