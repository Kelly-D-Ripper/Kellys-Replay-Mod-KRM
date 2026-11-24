using System.IO;
using ReplayMod.Core;

namespace ReplayMod.Events.ConcreteEvents;

[ReplayEvent(EventType.PartDamage)]
public class PartDamageEvent : IReplayEvent
{
    public uint unitId;
    public byte partId;
    public float hitPoints, pierce, blast, fire, impact;
    public double Time { get; set; }
    public EventType EventType => EventType.PartDamage;
    public bool HasFiniteValues()=>ReplayDamageStatePolicy.Valid(hitPoints,pierce,blast,fire,impact);
    public void Execute(object worker = null) { if (worker is UnitController c) c.Execute(this); }
    public void Write(BinaryWriter w)
    { if(!HasFiniteValues())throw new InvalidDataException("Replay damage has non-finite values"); w.Write(Time); w.Write(unitId); w.Write(partId); w.Write(hitPoints); w.Write(pierce); w.Write(blast); w.Write(fire); w.Write(impact); }
    public void Read(BinaryReader r)
    { Time = r.ReadDouble(); unitId = r.ReadUInt32(); partId = r.ReadByte(); hitPoints = r.ReadSingle(); pierce = r.ReadSingle(); blast = r.ReadSingle(); fire = r.ReadSingle(); impact = r.ReadSingle(); if(!HasFiniteValues())throw new InvalidDataException("Replay damage has non-finite values"); }
    public void Reset() { Time = 0; unitId = 0; partId = 0; hitPoints = pierce = blast = fire = impact = 0; }
}
