using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

public static class DamageCheckpointChecks
{
    public static int Run()
    {
        using(var state=new ReplaySeekState())
        {
            state.Add(Spawn());
            var original=Damage(2,20,80);state.Add(original);
            using var clone=state.Clone();var events=new Queue<IReplayEvent>();clone.CopyTo(events);
            var damage=events.OfType<PartDamageEvent>().Single();
            Require(damage.Time==2&&damage.hitPoints==20&&damage.blast==0&&damage.pierce==0&&damage.fire==0&&damage.impact==0,"checkpoint must restore final HP without the historic hit");
            Require(original.blast==80,"copy mutated the original historical event");
            foreach(var value in events)ReplayEventFactory.Return(value);
        }
        Console.WriteLine("PASS damage checkpoint restores HP without repeating the historical impulse");
        using(var ring=new RollingReplayBuffer(10))
        {
            ring.Add(Spawn());ring.Add(Damage(2,20,80));ring.Add(Damage(8,-5,25));ring.Add(new LandingEvent {Time=15,unitId=1});
            var events=ring.Snapshot(15).Select(RollingReplayBuffer.Decode).ToArray();var hits=events.OfType<PartDamageEvent>().OrderBy(e=>e.Time).ToArray();
            Require(hits.Length==2&&hits[0].Time==0&&hits[0].hitPoints==20&&hits[0].blast==0,"recent clip opening must not repeat pre-cut destruction");
            Require(hits[1].Time==3&&hits[1].hitPoints==-5&&hits[1].blast==25,"retained live hit lost its presentation impulse");
            foreach(var value in events)ReplayEventFactory.Return(value);
        }
        Console.WriteLine("PASS damage recent clips distinguish opening health from later recorded hits");
        return 2;
    }
    private static SpawnEvent Spawn()=>new SpawnEvent {unitId=1,ownerId=1,jsonKey="wreck",factionName="blue"};
    private static PartDamageEvent Damage(double time,float hp,float blast)=>new PartDamageEvent {Time=time,unitId=1,partId=3,hitPoints=hp,blast=blast};
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
