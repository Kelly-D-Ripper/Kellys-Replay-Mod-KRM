using System.Collections.Concurrent;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Misc;
using UnityEngine;
using ReplayMod.Director;

int passed = 0;
ReplayEventFactory.RegisterTypes();
ReplayDeleteChecks.Run(Test);
ReplayIntegrityChecks.Run(Test);
CargoMountChecks.Run(Test);
passed += EngineTimelineChecks.Run();
passed += PropAudioPolicyChecks.Run();
RotorSpeedChecks.Run(Test);
NavMaterialChecks.Run(Test);
StudioInputChecks.Run(Test);
passed += CameraPreviewChecks.Run();
CountermeasureAccountingChecks.Run(Test);
passed += CountermeasureChecks.Run();
ActorIdentityChecks.Run(Test);
passed += FlightInstrumentChecks.Run();
passed += LightingFireChecks.Run();
passed += MissileImpactChecks.Run();
passed += StudioLightControlChecks.Run();
passed += StudioLightProjectChecks.Run();
passed += CameraSmoothnessProjectChecks.Run();
passed += CameraSmoothnessChecks.Run();
passed += GunSpinChecks.Run();
passed += MapBuildingChecks.Run();
passed += EditorReplayMapChecks.Run();
passed += EjectionEyeChecks.Run();
passed += DamageCheckpointChecks.Run();
passed += ReplayRenameChecks.Run();
passed += AirframeOpeningStateChecks.Run();
passed += DamageSnapshotChecks.Run();
Test("signed control roundtrip, boundaries and clamping", () =>
{
    foreach (float value in new[] { -2f, -1f, -0.7532f, -0.0001f, 0f, 0.0001f, 0.8765f, 1f, 2f })
    {
        var result = RoundTrip(new UpdateInputsEvent { Time = 3.1, id = 77, pitch = value, roll = -value, yaw = value, throttle = value, brake = value, customAxis1 = -value });
        Near(result.pitch, Math.Clamp(value, -1, 1)); Near(result.roll, Math.Clamp(-value, -1, 1));
        Near(result.yaw, Math.Clamp(value, -1, 1)); Near(result.throttle, Math.Clamp(value, -1, 1));
        Near(result.brake, Math.Clamp(value, -1, 1)); Near(result.customAxis1, Math.Clamp(-value, -1, 1));
        Check(result.mask == 63, "independent samples");
    }
});
Test("legacy delta input masks and independent reader histories", () =>
{
    using var a = Legacy(1234, -3456); using var b = Legacy(-7890, 4567);
    var a1 = EventsHelper.GetInputs(42, 1, a, 10000); var b1 = EventsHelper.GetInputs(42, 1, b, 10000);
    Near(a1.pitch, .1234f); Near(b1.pitch, -.789f);
    var a2 = EventsHelper.GetInputs(42, 8, a, 10000); var b2 = EventsHelper.GetInputs(42, 8, b, 10000);
    Near(a2.pitch, .1234f); Near(b2.pitch, -.789f); Near(a2.throttle, -.3456f); Near(b2.throttle, .4567f);
    for (byte mask = 0; mask < 64; mask++)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        for (int i = 0; i < 6; i++) if ((mask & (1 << i)) != 0) writer.Write((short)(1000 + i));
        stream.Position = 0; using var reader = new BinaryReader(stream);
        EventsHelper.GetInputs(99, mask, reader, 10000); Check(stream.Position == stream.Length, "mask byte consumption");
    }
});
Test("parallel index and playback decoding isolation", () =>
{
    Parallel.For(0, 1000, i =>
    { using var r = Legacy((short)i, (short)-i); var x = EventsHelper.GetInputs(1, 9, r, 10000); Near(x.pitch, i / 10000f); Near(x.throttle, -i / 10000f); });
});
Test("framed input events stay independent without retaining reader histories", () =>
{
    var histories=(System.Collections.ICollection)typeof(EventsHelper).GetField("ReaderHistories",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);
    int before=histories.Count;
    var full=ReplayRecordTransport.EncodeEvent(new UpdateInputsEvent{Time=1,id=42,pitch=.7f,roll=-.3f,throttle=.9f});
    using(var reader=new BinaryReader(new MemoryStream(full)))ReplayEventFactory.Return(ReplayRecordTransport.ReadEvent(reader,7));
    byte[] body;
    using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
    {
        writer.Write((byte)EventType.ControlInputs);writer.Write(2d);writer.Write(2d);writer.Write(42u);writer.Write((byte)2);writer.Write((short)2500);body=stream.ToArray();
    }
    using(var reader=new BinaryReader(new MemoryStream(ReplayRecordTransport.Frame(body))))
    {
        var value=(UpdateInputsEvent)ReplayRecordTransport.ReadEvent(reader,7);
        Near(value.roll,.25f);Near(value.pitch,0);Near(value.throttle,0);ReplayEventFactory.Return(value);
    }
    Parallel.For(0,4000,i=>
    {
        using var reader=new BinaryReader(new MemoryStream(full));var value=(UpdateInputsEvent)ReplayRecordTransport.ReadEvent(reader,11);
        if(Math.Abs(value.pitch-.7f)>.001f||Math.Abs(value.roll+.3f)>.001f)throw new Exception("Parallel framed input state crossed readers");
        ReplayEventFactory.Return(value);
    });
    Check(histories.Count==before,"framed decoding did not create per-reader history entries");
});
Test("legacy delta histories survive collection while their reader is alive",()=>
{
    using var reader=Legacy(1234,-3456);var first=EventsHelper.GetInputs(17,1,reader,10000);Near(first.pitch,.1234f);
    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
    var second=EventsHelper.GetInputs(17,8,reader,10000);Near(second.pitch,.1234f);Near(second.throttle,-.3456f);
});
Test("bullet launch, damage, ejection and engine state roundtrips", () =>
{
    var b = RoundTrip(new BulletEvent { Time = 9, unitId = 345, gunIndex = 3, position = new GlobalPosition(-12, 420, 2048), velocity = new Vector3(123, -24, 555), life = 4.2f, tracerSize = 1.4f, reliability = .71f, color = new Color(1, .3f, .2f, 1), tracer = true, active = true, impacted = true, explosive = true });
    Check(b.unitId == 345 && b.gunIndex == 3 && b.tracer && b.active && b.impacted && b.explosive, "bullet metadata");
    Near(b.position.x, -12); Near(b.velocity.z, 555); Near(b.life, 4.2f); Near(b.reliability, .71f); Near(b.color.g, .3f);
    var d = RoundTrip(new PartDamageEvent { Time = 7, unitId = 1, partId = 2, hitPoints = -5, pierce = 5, blast = 7, fire = 3, impact = 1 });
    Check(d.partId == 2 && d.unitId == 1, "damage identity"); Near(d.hitPoints, -5); Near(d.blast, 7); Near(d.fire, 3); Near(d.impact, 1);
    var e = RoundTrip(new EjectionEvent { Time = 6, unitId = 18, capsule = true }); Check(e.capsule && e.unitId == 18, "ejection");
    var engine = RoundTrip(new AircraftStatusEvent { Time = 2, unitId = 22, ignition = true, rpm = new[] { 3000f, 3210f } }); Check(engine.ignition && engine.rpm.Length == 2, "engine count"); Near(engine.rpm[1], 3210);
    var missile = RoundTrip(new MissileStatusEvent { unitId = 8, stage = 1, age = 8, throttle = .5f, thrust = 2000, booster = true, fuel = new[] { 0f, 2f }, delay = new[] { 0f, 1f }, activated = new[] { true, false } });
    Check(missile.stage == 1 && missile.booster && missile.activated[0] && !missile.activated[1], "missile stage"); Near(missile.fuel[1], 2); Near(missile.delay[1], 1); Near(missile.thrust, 2000);
});
Test("bounded buffer counts both queues and prioritizes due state", () =>
{
    var buffer = new BoundedEventBuffer(4);
    var sample = new UpdateInputsEvent { Time = 2 };
    var state = new EjectionEvent { Time = 0 };
    buffer.Add(sample, default); buffer.Add(state, default);
    Check(buffer.Count == 2 && buffer.FreeSpace == 2, "combined occupancy");
    Check(buffer.TryTakeIfReady(0, out var first) && ReferenceEquals(first, state), "state precedes lookahead");
    Check(buffer.TryTakeIfReady(0, out var second) && ReferenceEquals(second, sample), "lookahead window");
    Check(buffer.FreeSpace == 4, "semaphore restored");
    buffer.Add(new UpdateInputsEvent { Time = 50 }, default); Check(!buffer.TryTakeIfReady(0, out _), "future sample waits");
    buffer.ClearAndReturnToPool(); Check(buffer.FreeSpace == 4, "clear both queues");
});
Test("writer drains all queued events before recording closes", () =>
{
    using var queue = new BlockingCollection<IReplayEvent>(); using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
    var task = Task.Run(() => ReplayFileWriter.Drain(queue, writer));
    for (int i = 0; i < 20000; i++) queue.Add(new EjectionEvent { Time = i, unitId = (uint)i, capsule = i % 2 == 0 });
    queue.CompleteAdding(); Check(task.GetAwaiter().GetResult() == 20000, "actual written count");
    stream.Position = 0; using var reader = new BinaryReader(stream);
    for (int i = 0; i < 20000; i++)
    { var e = (EjectionEvent)ReplayEventFactory.CreateAndRead((EventType)reader.ReadByte(), reader); Check(e.unitId == i && e.Time == i && e.capsule == (i % 2 == 0), "saved order and content"); ReplayEventFactory.Return(e); }
    Check(stream.Position == stream.Length, "no trailing or missing events");
});
Test("new pooled state resets and truncated records fail", () =>
{
    var e = new BulletEvent { Time = 10, unitId = 7, active = true, color = new Color(1, 1, 1, 1), life = 2 };
    e.Reset(); Check(e.unitId == 0 && !e.active && e.life == 0 && e.color.r == 0 && e.Time == 0, "pool reset");
    using var r = new BinaryReader(new MemoryStream(new byte[2]));
    bool failed = false; try { new BulletEvent().Read(r); } catch (EndOfStreamException) { failed = true; } Check(failed, "truncation detection");
});
Test("seeking restores surviving gear, controls, HP and turret state", () =>
{
    var state = new ReplaySeekState();
    state.Add(new SpawnEvent { Time = 0, unitId = 1, ownerId = 1 });
    state.Add(new SpawnEvent { Time = 0, unitId = 2, ownerId = 1 });
    state.Add(new UpdateInputsEvent { Time = 1, id = 1, throttle = .9f });
    state.Add(new SetGearEvent { Time = 2, unitId = 1, deployed = true });
    state.Add(new PartDamageEvent { Time = 3, unitId = 1, partId = 2, hitPoints = 50 });
    state.Add(new PartDamageEvent { Time = 4, unitId = 1, partId = 2, hitPoints = 20 });
    state.Add(new UpdateTurretEvent { Time = 4, attachedUnitId = 1, weaponStationIdx = 3, turretIdx = 2, traverseAngle = 40 });
    state.Add(new DespawnEvent { Time = 5, unitId = 2 });
    var queue = new Queue<IReplayEvent>(); state.Drain(queue);
    Check(queue.Count == 5 && queue.Dequeue() is SpawnEvent spawn && spawn.unitId == 1, "surviving spawn first");
    Check(queue.OfType<SetGearEvent>().Single().Instant, "seek gear must be positioned while paused");
    Near(queue.OfType<UpdateInputsEvent>().Single().throttle, .9f);
    Near(queue.OfType<PartDamageEvent>().Single().hitPoints, 20);
    Check(queue.OfType<UpdateTurretEvent>().Single().weaponStationIdx == 3, "separate station and turret indices");
    var reused = new ReplaySeekState();
    reused.Add(new SpawnEvent { unitId = 8, ownerId = 8 }); reused.Add(new PartDamageEvent { unitId = 8, partId = 1, hitPoints = 10 });
    reused.Add(new DespawnEvent { unitId = 8 }); reused.Add(new SpawnEvent { Time = 10, unitId = 8, ownerId = 8 });
    queue.Clear(); reused.Drain(queue); Check(queue.Count == 1 && queue.Peek() is SpawnEvent, "reused IDs cannot inherit old damage");
});
Test("director rejects malformed projects, duplicate keys and unsafe shot settings", () =>
{
    var p=new DirectorProject();Check(p.Validate(120),"default project");
    p.cameras.Add(new CameraKey {time=2});p.cameras.Add(new CameraKey {time=2});Check(!p.Validate(120),"ambiguous camera times");p.cameras.Clear();
    p.speeds.Add(new SpeedKey {time=1,scale=float.NaN});Check(!p.Validate(120),"NaN speed");p.speeds.Clear();
    p.path.Add(new SplineRecord());Check(!p.Validate(120),"missing spline nodes");p.path.Clear();
    p.environment.hour=float.PositiveInfinity;Check(!p.Validate(120),"infinite environment");p.environment.hour=12;
    p.shots.Add(new Shot {start=0,end=2,width=1919});Check(!p.Validate(120),"odd output dimension");p.shots.Clear();
    p.cameras.Add(new CameraKey {time=121});Check(!p.Validate(120),"outside replay");p.cameras.Clear();
    p.cameras.Add(new CameraKey {time=1,qw=0});Check(!p.Validate(120),"invalid orientation");p.cameras.Clear();
    p.fov=1;p.cameras.Add(new CameraKey {time=1,fov=1});Check(p.Validate(120),"native telephoto lens saves without clamping");
    p.fov=120;p.cameras[0].fov=120;Check(p.Validate(120),"native wide lens boundary");
    p.fov=.99f;Check(!p.Validate(120),"invalid narrow lens");p.fov=60;
    p.cameras[0].fov=120.01f;Check(!p.Validate(120),"invalid key lens");p.cameras.Clear();
    Check(p.Validate(120),"valid project recovered");
});
Test("camera and playback easing remains bounded at cuts and endpoints", () =>
{
    Near((float)DirectorProject.Ease(-2,1),0);Near((float)DirectorProject.Ease(2,1),1);Near((float)DirectorProject.Ease(.5,1),.5f);
    var keys=new List<SpeedKey> {new SpeedKey {time=5,scale=.25f},new SpeedKey {time=15,scale=2}};
    Near(DirectorProject.SpeedAt(keys,0,1),.25f);Near(DirectorProject.SpeedAt(keys,10,1),1.125f);Near(DirectorProject.SpeedAt(keys,100,1),2);
    float prior=.25f;for(double t=5;t<=15;t+=.05){float next=DirectorProject.SpeedAt(keys,t,1);Check(next>=prior && next<=2,"monotonic bounded ramp");prior=next;}
});
Test("rolling clip retains live aircraft baseline, damage and rebased timestamps", () =>
{
    using var ring=new RollingReplayBuffer(10);
    ring.Add(new SpawnEvent {Time=0,unitId=1,ownerId=1,jsonKey="aircraft",factionName="blue"});
    ring.Add(new UpdatePositionEvent {Time=2,unitId=1,position=new GlobalPosition(10,20,30)});
    ring.Add(new PartDamageEvent {Time=3,unitId=1,partId=2,hitPoints=40});
    ring.Add(new UpdateInputsEvent {Time=4,id=1,throttle=.7f});
    ring.Add(new UpdatePositionEvent {Time=12,unitId=1,position=new GlobalPosition(100,200,300)});
    ring.Add(new LandingEvent {Time=16,unitId=1});
    var data=ring.Snapshot(16);Check(ring.CutTime==6,"exact retained window");var events=data.Select(RollingReplayBuffer.Decode).ToArray();
    var spawn=events.OfType<SpawnEvent>().Single();Near(spawn.pos.x,46);Near(spawn.pos.y,92);Near(spawn.pos.z,138);Check(spawn.Time==0,"interpolated anchor spawn starts clip");
    Near(events.OfType<PartDamageEvent>().Single().hitPoints,40);Near(events.OfType<UpdateInputsEvent>().Single().throttle,.7f);
    var moves=events.OfType<UpdatePositionEvent>().OrderBy(e=>e.Time).ToArray();
    Check(moves.Length==2 && moves[0].Time==0 && moves[1].Time==6,"anchor and subsequent move times rebased");
    Near(moves[0].position.x,46);Near(moves[1].position.x,100);Check(events.OfType<LandingEvent>().Single().Time==10,"bookmark rebased");
    foreach(var e in events)ReplayEventFactory.Return(e);
    var again=ring.Snapshot(16);Check(data.Count==again.Count && data.Zip(again,(a,b)=>a.SequenceEqual(b)).All(x=>x),"saving does not consume baseline");
});
Test("rolling byte limit, vanished actors and reused IDs remain bounded", () =>
{
    using var ring=new RollingReplayBuffer(30,1024);
    ring.Add(new SpawnEvent {unitId=8,ownerId=8,jsonKey="old",factionName="blue"});
    ring.Add(new PartDamageEvent {Time=1,unitId=8,partId=0,hitPoints=10});ring.Add(new DespawnEvent {Time=2,unitId=8});
    ring.Add(new SpawnEvent {Time=3,unitId=8,ownerId=8,jsonKey="new",factionName="blue"});
    for(int n=4;n<100;n++)ring.Add(new UpdatePositionEvent {Time=n,unitId=8,position=new GlobalPosition(n,0,0)});
    Check(ring.Bytes<=1024,"hard byte cap");var data=ring.Snapshot(100);var events=data.Select(RollingReplayBuffer.Decode).ToArray();
    Check(events.OfType<SpawnEvent>().Single().jsonKey=="new","reused ID resolves to latest actor");Check(!events.OfType<PartDamageEvent>().Any(),"old damage forgotten");
    Check(events.All(e=>e.Time>=0 && e.Time<=100-ring.CutTime),"rebased range");foreach(var e in events)ReplayEventFactory.Return(e);
});
Test("landing event roundtrip and reducer does not replay historical touchdowns", () =>
{
    var landing=RoundTrip(new LandingEvent {unitId=42,Time=100});Check(landing.unitId==42 && landing.Time==100,"touchdown roundtrip");
    var state=new ReplaySeekState();state.Add(landing);var queue=new Queue<IReplayEvent>();state.Drain(queue);Check(queue.Count==0,"bookmarks do not alter world state");
});
Test("spent missile mounts survive rolling snapshots without replaying launches", () =>
{
    var e=RoundTrip(new MissileStoresEvent {Time=5,unitId=1,ammo=new[]{0,1},fired=new[]{true,false},active=new[]{false,true}});
    Check(e.ammo[0]==0 && e.fired[0] && !e.active[0] && e.active[1],"mounted payload");
    using var ring=new RollingReplayBuffer(10);ring.Add(new SpawnEvent {unitId=1,ownerId=1,jsonKey="jet",factionName="blue"});ring.Add(e);
    ring.Trim(30);var data=ring.Snapshot(30).Select(RollingReplayBuffer.Decode).ToArray();var snapshot=data.OfType<MissileStoresEvent>().Single();
    Check(snapshot.Time==0 && snapshot.ammo[0]==0 && snapshot.fired[0] && !snapshot.active[0],"spent store baseline");foreach(var item in data)ReplayEventFactory.Return(item);
    var second=ring.Snapshot(30).Select(RollingReplayBuffer.Decode).ToArray();Check(second.OfType<MissileStoresEvent>().Single().ammo[1]==1,"repeat snapshot keeps remaining mount");foreach(var item in second)ReplayEventFactory.Return(item);
});
Test("rolling stress keeps snapshots repeatable while recording continues", () =>
{
    using var ring=new RollingReplayBuffer(30,1024*1024);var timer=System.Diagnostics.Stopwatch.StartNew();
    ring.Add(new SpawnEvent {unitId=1,ownerId=1,jsonKey="jet",factionName="blue"});
    for(int n=0;n<100000;n++){var e=new UpdatePositionEvent {Time=n*.002,unitId=1,position=new GlobalPosition(n,0,0)};ring.Add(e);if(n%20000==0)Check(ring.Snapshot(e.Time).Count>0,"mid-recording save");}
    Check(ring.Bytes<=1024*1024,"stress limit");Check(200-ring.CutTime<=30.01,"duration limit");
    Console.WriteLine($"Rolling buffer stress: 100000 events, {timer.ElapsedMilliseconds} ms (CPU-only, not game FPS)");
});
passed += PlaybackChecks.Run();
passed += PlaybackPipelineChecks.Run();
passed += CameraRailChecks.Run();
passed += CameraOrientationChecks.Run();
passed += VisibilityChecks.Run();
passed += CleanCapturePerformanceChecks.Run();
passed += GuiCaptureChecks.Run();
passed += RollingRecordingTests.Run();
passed += SeekStateChecks.Run();
passed += SnapshotHistoryChecks.Run();
passed += ReplayMotionSmoothingChecks.Run();
passed += ReplayBoosterChecks.Run();
passed += RollingCutChecks.Run();
passed += BuildingSpawnChecks.Run();
DiagnosticsChecks.Run(Test);
BackgroundCommandChecks.Run(Test);
passed += ReplayCloseCameraChecks.Run();
passed += ReplayInteriorVisibilityChecks.Run();
RecoveryRecordingChecks.Run(Test);
passed += CameraReliabilityChecks.Run();
passed += NativeMenuPlacementChecks.Run();
passed += TimelineMarkerChecks.Run();
GunChecks.Run(Test);
GunAudioChecks.Run(Test);
WorldStateChecks.Run(Test);
passed += ReplayCloudClockChecks.Run();
passed += AircraftLifecycleRegressionChecks.Run();
passed += AircraftVisualChecks.Run();
VfxStateChecks.Run(Test);
ReplayOperationBarrierChecks.Run(Test);
ReplayOwnershipChecks.Run(Test);
Format4RecordingChecks.Run(Test);
Format5RecordingChecks.Run(Test);
NativeCameraRefreshChecks.Run(Test);
passed += OverlayPlacementChecks.Run();
passed += SuiteLayoutChecks.Run();
passed += SuiteThemeChecks.Run();
VfxVisibilityChecks.Run(Test);
passed += CameraTrackingChecks.Run();
TwoAircraftFramingChecks.Run(Test);
CameraPathChecks.Run(Test);
if (args.Length > 0) RealReplayAudit.Run(args);
Console.WriteLine($"Regression tests: {passed} passed");
return 0;

void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
void Near(float actual, float expected) { if (Math.Abs(actual - expected) > .00011f) throw new Exception($"{actual} != {expected}"); }
BinaryReader Legacy(params short[] values) { var stream = new MemoryStream(); var w = new BinaryWriter(stream); foreach (var v in values) w.Write(v); stream.Position = 0; return new BinaryReader(stream); }
T RoundTrip<T>(T source) where T : IReplayEvent, new()
{ using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, true); source.Write(w); stream.Position = 0; using var r = new BinaryReader(stream); var target = new T(); target.Read(r); Check(stream.Position == stream.Length, "complete event payload"); return target; }
