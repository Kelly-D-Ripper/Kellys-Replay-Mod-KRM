using System.IO;
namespace ReplayMod.Events.ConcreteEvents;
[ReplayEvent(EventType.Landing)]
public class LandingEvent : IReplayEvent
{
    public uint unitId;
    public double Time {get;set;}
    public EventType EventType => EventType.Landing;
    public void Execute(object worker=null) {}
    public void Write(BinaryWriter w) {w.Write(Time);w.Write(unitId);}
    public void Read(BinaryReader r) {Time=r.ReadDouble();unitId=r.ReadUInt32();}
    public void Reset() {Time=0;unitId=0;}
}
