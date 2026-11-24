using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class BuildingSpawnChecks
{
    private sealed class Actor { public uint Id; public bool Alive = true; }

    public static int Run()
    {
        int passed = 0;
        Test("neutral building and scenery spawns do not require faction HQ", () =>
        {
            Require(ReplaySpawnPolicy.CanSpawnWithoutFaction(true, false, false, ReplaySpawnPolicy.NoFaction), "recorded neutral building is supported by native spawner");
            Require(ReplaySpawnPolicy.CanSpawnWithoutFaction(false, true, false, ReplaySpawnPolicy.NoFaction), "native scenery spawner has no HQ argument");
            Require(ReplaySpawnPolicy.CanSpawnWithoutFaction(false, true, false, "previous scenery faction"), "scenery never consumes a faction");
            Require(!ReplaySpawnPolicy.CanSpawnWithoutFaction(true, false, false, "missing mod faction"), "factioned building must not silently change affiliation");
            Require(!ReplaySpawnPolicy.CanSpawnWithoutFaction(false, false, false, ReplaySpawnPolicy.NoFaction), "aircraft and vehicles retain existing faction requirement");
        });
        Test("only explicitly neutral cargo gains the container exemption", () =>
        {
            Require(ReplaySpawnPolicy.CanSpawnWithoutFaction(false,false,true,ReplaySpawnPolicy.NoFaction),"neutral munitions pallet");
            foreach(string faction in new[]{null,"","No faction","no faction ","missing mod faction","FactionA"})
                Require(!ReplaySpawnPolicy.CanSpawnWithoutFaction(false,false,true,faction),"missing or named ownership is not invented");
            Require(!ReplaySpawnPolicy.CanSpawnWithoutFaction(false,false,false,ReplaySpawnPolicy.NoFaction),"pilots and unknown definitions remain guarded");
        });
        Test("explicit neutral vehicle routes preserve neutral ownership", () =>
        {
            Require(ReplaySpawnPolicy.CanSpawnWithoutFaction(false,false,false,ReplaySpawnPolicy.NoFaction,true),"native neutral vehicle route");
            foreach(string faction in new[]{null,"","No faction","no faction ","missing mod faction","FactionA"})
                Require(!ReplaySpawnPolicy.CanSpawnWithoutFaction(false,false,false,faction,true),"unknown named factions remain errors");
            Require(!ReplaySpawnPolicy.CanSpawnWithoutFaction(false,false,false,ReplaySpawnPolicy.NoFaction),"unknown and pilot routes stay protected");
        });
        Test("legacy neutral pallet spawn survives event serialization", () =>
        {
            var original=new SpawnEvent {Time=12.5,unitId=77,ownerId=77,jsonKey="MunitionsPallet1",factionName=ReplaySpawnPolicy.NoFaction,pos=new GlobalPosition(123,45,678),rotation=new Quaternion {w=1}};
            using var stream=new MemoryStream();
            using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))original.Write(writer);
            stream.Position=0;using var reader=new BinaryReader(stream);
            var restored=new SpawnEvent();restored.Read(reader);
            Require(stream.Position==stream.Length&&restored.unitId==77&&restored.jsonKey=="MunitionsPallet1"&&restored.factionName==ReplaySpawnPolicy.NoFaction,"existing event data is sufficient");
            Require(restored.pos.x==123&&restored.Time==12.5&&ReplaySpawnPolicy.CanSpawnWithoutFaction(false,false,true,restored.factionName),"saved pose and neutral classification");
        });
        Test("seek and recent clips preserve neutral cargo ownership", () =>
        {
            var spawn=new SpawnEvent {Time=0,unitId=77,ownerId=77,jsonKey="MunitionsPallet1",factionName=ReplaySpawnPolicy.NoFaction,pos=new GlobalPosition(123,45,678),rotation=new Quaternion {w=1}};
            var state=new ReplaySeekState();state.Add(spawn);
            state.Add(new UpdatePositionEvent {Time=2,unitId=77,position=new GlobalPosition(124,45,678),rotation=new Quaternion {w=1}});
            var events=new Queue<IReplayEvent>();state.Drain(events);
            var restored=events.OfType<SpawnEvent>().Single();
            Require(restored.jsonKey=="MunitionsPallet1"&&restored.factionName==ReplaySpawnPolicy.NoFaction,"seek retains original ownership");
            using var ring=new RollingReplayBuffer(10);ring.Add(restored);
            ring.Add(new UpdatePositionEvent {Time=2,unitId=77,position=new GlobalPosition(124,45,678),rotation=new Quaternion {w=1}});
            ring.Add(new UpdatePositionEvent {Time=12,unitId=77,position=new GlobalPosition(130,45,678),rotation=new Quaternion {w=1}});
            var clip=ring.Snapshot(12).Select(RollingReplayBuffer.Decode).ToArray();var clipped=clip.OfType<SpawnEvent>().Single();
            Require(clipped.jsonKey=="MunitionsPallet1"&&clipped.factionName==ReplaySpawnPolicy.NoFaction,"recent clip retains neutral pallet");
            foreach(var item in clip)ReplayEventFactory.Return(item);
            foreach(var item in events)ReplayEventFactory.Return(item);
        });
        Test("map-hidden actor resolves without native UnitRegistry entry", () =>
        {
            var actors = Registry(); var building = new Actor { Id = 91 };
            actors.Track(7, 91, building);
            Require(actors.TryGet(7, 91, out var found) && ReferenceEquals(found, building), "own actor is available for transform, damage and detach events");
            Require(!actors.TryGet(8, 91, out _), "native PID alone does not grant replay ownership");
        });
        Test("destroyed actor and reassigned native PID cannot resolve old lifetime", () =>
        {
            var actors = Registry(); var destroyed = new Actor { Id = 91 };
            actors.Track(7, 91, destroyed); destroyed.Alive = false;
            Require(!actors.TryGet(7, 91, out _) && actors.Count == 0, "Unity destroyed references are dropped");
            var reassigned = new Actor { Id = 92 }; actors.Track(7, 92, reassigned); reassigned.Id = 93;
            Require(!actors.TryGet(7, 92, out _) && actors.Count == 0, "native ID reuse cannot resurrect the original actor");
        });
        Test("despawn, reset and recorded ID reuse release actor ownership", () =>
        {
            var actors = Registry(); var old = new Actor { Id = 91 }; var replacement = new Actor { Id = 92 };
            actors.Track(7, 91, old); actors.Remove(7);
            Require(!actors.TryGet(7, 91, out _), "despawn no longer resolves actor even while destruction is deferred");
            actors.Track(7, 92, replacement);
            Require(actors.TryGet(7, 92, out var found) && ReferenceEquals(found, replacement), "new lifetime resolves replacement");
            actors.Clear(); Require(actors.Count == 0 && !actors.TryGet(7, 92, out _), "scene reset clears references");
        });
        return passed;

        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS building replay " + name); }
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        ReplayActorRegistry<Actor> Registry() => new ReplayActorRegistry<Actor>(a => a != null && a.Alive, a => a.Id);
    }
}
