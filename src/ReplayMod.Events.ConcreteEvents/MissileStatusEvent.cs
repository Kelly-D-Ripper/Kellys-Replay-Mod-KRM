using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.MissileStatus)]
public class MissileStatusEvent : IReplayEvent
{
    public uint unitId;
    public int stage;
    public float age, throttle, thrust;
    public bool booster;
    public float[] fuel = new float[0], delay = new float[0];
    public bool[] activated = new bool[0];
    public double Time { get; set; }
    public EventType EventType => EventType.MissileStatus;
    public void Execute(object worker = null) { if (worker is UnitController c) c.Execute(this); }
    public void Write(BinaryWriter w)
    {
        w.Write(Time); w.Write(unitId); w.Write(stage); w.Write(age); w.Write(throttle); w.Write(thrust); w.Write(booster); w.Write((ushort)fuel.Length);
        for (int i = 0; i < fuel.Length; i++) { w.Write(fuel[i]); w.Write(delay[i]); w.Write(activated[i]); }
    }
    public void Read(BinaryReader r)
    {
        Time = r.ReadDouble(); unitId = r.ReadUInt32(); stage = r.ReadInt32(); age = r.ReadSingle(); throttle = r.ReadSingle(); thrust = r.ReadSingle(); booster = r.ReadBoolean();
        int n = r.ReadUInt16(); fuel = new float[n]; delay = new float[n]; activated = new bool[n];
        for (int i = 0; i < n; i++) { fuel[i] = r.ReadSingle(); delay[i] = r.ReadSingle(); activated[i] = r.ReadBoolean(); }
    }
    public void Reset() { Time = 0; unitId = 0; stage = 0; age = throttle = thrust = 0; booster = false; fuel = delay = new float[0]; activated = new bool[0]; }
}
