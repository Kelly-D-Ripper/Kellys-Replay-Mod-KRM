using System;
using System.Threading;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;

internal static class EngineTimelineChecks
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception("Engine timeline: " + message);
    }

    public static int Run()
    {
        var timeline = new ReplayEngineTimeline();
        var first = new AircraftStatusEvent { Time = 1, ignition = true, rpm = new[] { 1000f, 2000f } };
        timeline.Add(first);
        first.rpm[0] = 9999; // The event pool is free to reuse its data.
        timeline.Add(new AircraftStatusEvent { Time = 1.05, ignition = true, rpm = new[] { 1200f, 2200f } });
        Require(!timeline.SampleAt(.9, out _, out _), "future samples must not start an engine early");
        Require(timeline.SampleAt(1.025, out bool ignition, out var rpm) && ignition &&
            Math.Abs(rpm[0] - 1100) < .01 && Math.Abs(rpm[1] - 2100) < .01, "interpolate independent recorded engines");
        timeline.Add(new AircraftStatusEvent { Time = 1.1, ignition = false, rpm = new[] { 800f, 1800f } });
        Require(timeline.SampleAt(1.075, out ignition, out rpm) && ignition && rpm[0] == 1200, "do not blend across ignition changes");
        Require(timeline.SampleAt(1.1, out ignition, out rpm) && !ignition && rpm[0] == 800, "apply ignition at its timestamp");
        timeline.Add(new AircraftStatusEvent { Time = 3, ignition = false, rpm = new[] { 400f, 800f } });
        Require(timeline.SampleAt(2, out _, out rpm) && rpm[0] == 800, "do not invent a slow transition across recording gaps");
        timeline.Add(new AircraftStatusEvent { Time = 3, ignition = false, rpm = new[] { 300f } });
        Require(timeline.SampleAt(3, out _, out rpm) && rpm.Length == 1 && rpm[0] == 300, "replace duplicate timestamps and handle changed engine count");
        timeline.Add(new AircraftStatusEvent { Time = 3.05, ignition = false, rpm = new[] { float.NaN } });
        Require(timeline.SampleAt(3.05, out _, out rpm) && rpm[0] == 300, "invalid data must not poison audio");

        var buffer = new BoundedEventBuffer(8);
        var spawn = new SpawnEvent { Time = 1, unitId = 7 };
        var engine = new AircraftStatusEvent { Time = 1.05, unitId = 7, rpm = new[] { 100f } };
        buffer.Add(spawn, CancellationToken.None);
        buffer.Add(engine, CancellationToken.None);
        Require(!buffer.TryTakeIfReady(.9, out _), "future engine sample cannot cross spawn");
        Require(buffer.TryTakeIfReady(1, out var due) && ReferenceEquals(due, spawn), "spawn comes first");
        buffer.MarkActionExecuted(due);
        Require(buffer.TryTakeIfReady(1, out var future) && ReferenceEquals(future, engine), "engine lookahead becomes available after spawn");

        var cores = new ReplayEngineTimeline();
        var coreFirst = new EngineCoreStateEvent {Time=2,ignition=true,rpm=new[]{1800f,2200f}};
        cores.Add(coreFirst);coreFirst.rpm[0]=42;
        cores.Add(new EngineCoreStateEvent {Time=2.1,ignition=true,rpm=new[]{2200f,2600f}});
        Require(cores.SampleAt(2.05,out ignition,out rpm)&&ignition&&Math.Abs(rpm[0]-2000)<.1&&Math.Abs(rpm[1]-2400)<.1,"recorded core RPM interpolates independently of propellers");
        var coreBuffer=new BoundedEventBuffer(4);var coreSpawn=new SpawnEvent{Time=1,unitId=8};
        var coreSample=new EngineCoreStateEvent{Time=1.05,unitId=8,rpm=new[]{2000f}};
        coreBuffer.Add(coreSpawn,CancellationToken.None);coreBuffer.Add(coreSample,CancellationToken.None);
        Require(!coreBuffer.TryTakeIfReady(.9,out _),"core lookahead does not cross spawn");
        Require(coreBuffer.TryTakeIfReady(1,out due)&&ReferenceEquals(due,coreSpawn),"core spawn comes first");coreBuffer.MarkActionExecuted(due);
        Require(coreBuffer.TryTakeIfReady(1,out future)&&ReferenceEquals(future,coreSample),"core lookahead releases after spawn");
        using var ring=new RollingReplayBuffer(10,1024*1024);
        ring.Add(new SpawnEvent{unitId=9,Time=0,jsonKey="jet",factionName="no faction",isAircraft=true,weapons=new System.Collections.Generic.List<WeaponMount>()});
        ring.Add(new EngineCoreStateEvent{unitId=9,Time=1,ignition=true,rpm=new[]{1200f,1500f}});
        ring.Add(new EngineCoreStateEvent{unitId=9,Time=11,ignition=true,rpm=new[]{1700f,1900f}});
        var clip=ring.Snapshot(12).Select(RollingReplayBuffer.Decode).ToArray();
        var saved=clip.OfType<EngineCoreStateEvent>().OrderBy(e=>e.Time).ToArray();
        Require(saved.Length==2&&saved[0].Time==0&&saved[0].rpm[0]==1200&&saved[1].Time==9&&saved[1].rpm[1]==1900,"rolling clip preserves both baseline and future engine states");
        foreach(var value in clip)ReplayMod.Events.ReplayEventFactory.Return(value);
        return 5;
    }
}
