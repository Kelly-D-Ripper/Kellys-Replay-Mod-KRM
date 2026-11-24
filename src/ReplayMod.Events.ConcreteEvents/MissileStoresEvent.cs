using System;
using System.IO;
using ReplayMod.Core;
namespace ReplayMod.Events.ConcreteEvents;
[ReplayEvent(EventType.MissileStores)]
public class MissileStoresEvent : IReplayEvent
{
    public uint unitId;
    public int[] ammo=Array.Empty<int>();
    public bool[] fired=Array.Empty<bool>(),active=Array.Empty<bool>();
    public double Time {get;set;}
    public EventType EventType => EventType.MissileStores;
    public void Execute(object worker=null){if(worker is UnitController c)c.Execute(this);}
    public void Write(BinaryWriter w)
    {
        if(ammo.Length>256 || fired.Length!=ammo.Length || active.Length!=ammo.Length)throw new InvalidDataException("Invalid mounted missile snapshot");
        w.Write(Time);w.Write(unitId);w.Write((ushort)ammo.Length);
        for(int n=0;n<ammo.Length;n++){w.Write(ammo[n]);w.Write(fired[n]);w.Write(active[n]);}
    }
    public void Read(BinaryReader r)
    {
        Time=r.ReadDouble();unitId=r.ReadUInt32();int count=r.ReadUInt16();if(count>256)throw new InvalidDataException("Too many mounted missiles");
        ammo=new int[count];fired=new bool[count];active=new bool[count];
        for(int n=0;n<count;n++){ammo[n]=r.ReadInt32();fired[n]=r.ReadBoolean();active[n]=r.ReadBoolean();}
    }
    public void Reset(){Time=0;unitId=0;ammo=Array.Empty<int>();fired=Array.Empty<bool>();active=Array.Empty<bool>();}
}
