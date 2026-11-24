using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class WorldStateChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("world environment codec preserves measured multiplayer state and optional phases",()=>
        {
            var source=Environment(12.5);source.timeOfDay=23.99f;source.timeFactor=0;source.moonPhase=27.95f;
            source.windHeading=315;source.windMainHeading=350;source.windRandomArc=45;source.windTurbulence=.7f;
            source.windVelocity=new Vector3(7,2,-15);source.windOffset=new Vector3(123,456,789);source.cloudDisplacement=new Vector2(12345,-34567);
            var copy=RoundTrip(source);
            Require(copy.Time==source.Time && copy.timeFactor==0 && copy.hasCloudPhase && copy.moonPhase==source.moonPhase,"client factor and moon availability");
            Near(copy.cloudDisplacement.x,12345);Near(copy.windVelocity.z,-15);Near(copy.windOffset.y,456);Near(copy.windMainHeading,350);Near(copy.windRandomArc,45);
            source.hasCloudPhase=false;source.moonPhase=null;copy=RoundTrip(source);
            Require(!copy.hasCloudPhase && !copy.moonPhase.HasValue,"unavailable native state stays unavailable");
        });
        test("world codecs reject nonfinite phases and oversized or truncated door paths",()=>
        {
            var bad=Environment(0);bad.windOffset=new Vector3(float.NaN,0,0);
            Throws<InvalidDataException>(()=>RollingReplayBuffer.Encode(bad));
            var door=Door(1,0,0);door.hangarPath=new string('1',WorldStateSnapshot.MaxPathBytes+1);
            Throws<InvalidDataException>(()=>RollingReplayBuffer.Encode(door));
            door=Door(1,0,0);door.openAmount=float.PositiveInfinity;
            Throws<InvalidDataException>(()=>RollingReplayBuffer.Encode(door));
            using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            {writer.Write(0d);writer.Write(1u);writer.Write((ushort)8);writer.Write(new byte[]{1,2});}
            stream.Position=0;using var reader=new BinaryReader(stream);Throws<EndOfStreamException>(()=>new HangarDoorStateEvent().Read(reader));
        });
        test("world pooled events release prior owner paths and nullable weather state",()=>
        {
            var door=Door(100,9,.7f);door.Reset();
            Require(door.unitId==0 && door.Time==0 && door.hangarPath==string.Empty && !door.available && door.openAmount==0,"door pool cannot inherit another actor");
            var environment=Environment(4);environment.moonPhase=14;environment.Reset();
            Require(environment.Time==0 && !environment.hasCloudPhase && !environment.moonPhase.HasValue && environment.cloudDisplacement.x==0 && environment.windOffset.z==0,"environment pool cannot inherit old phase");
        });
        test("world interpolation wraps midnight and lunar phase using measured client samples",()=>
        {
            var first=Environment(0);first.timeOfDay=23.9f;first.moonPhase=27.9f;first.windHeading=350;first.timeFactor=0;
            first.cloudDisplacement=new Vector2(100,200);first.windOffset=new Vector3(10,10,10);
            var next=Environment(2);next.timeOfDay=.1f;next.moonPhase=.1f;next.windHeading=10;next.cloudDisplacement=new Vector2(120,240);next.windOffset=new Vector3(30,30,30);
            var middle=WorldStateSnapshot.At(WorldStateSnapshot.From(first),WorldStateSnapshot.From(next),1);
            Require(middle.timeOfDay<.001 || middle.timeOfDay>23.999,"midnight takes short cycle");
            Require(middle.moonPhase<.001 || middle.moonPhase>27.999,"lunar month takes short cycle");
            Near(middle.cloudDisplacement.x,110);Near(middle.cloudDisplacement.y,220);Near(middle.windOffset.z,20);Near(middle.windHeading,360);
            Near(middle.timeFactor,0); // Client timeFactor need not be authoritative.
        });
        test("world lookahead cannot apply a future weather setting or invent missing cloud phase",()=>
        {
            var first=Environment(0);first.conditions=.1f;first.cloudHeight=1000;first.hasCloudPhase=false;first.moonPhase=null;
            var next=Environment(10);next.conditions=.9f;next.cloudHeight=8000;next.hasCloudPhase=true;next.moonPhase=14;
            var before=WorldStateSnapshot.At(WorldStateSnapshot.From(first),WorldStateSnapshot.From(next),9.99);
            Require(before.conditions==.1f && before.cloudHeight==1000 && !before.hasCloudPhase && !before.moonPhase.HasValue,"future discrete state stays hidden until due");
            var due=WorldStateSnapshot.At(WorldStateSnapshot.From(first),WorldStateSnapshot.From(next),10);
            Require(due.conditions==.9f && due.cloudHeight==8000 && due.hasCloudPhase && due.moonPhase==14,"due setting applies exactly at timestamp");
        });
        test("world rolling cut samples actual preexisting cloud and turbulence phase",()=>
        {
            var first=Environment(100);first.cloudDisplacement=new Vector2(10000,-30000);first.windOffset=new Vector3(55000,55000,55000);first.timeOfDay=10;first.timeFactor=60;
            var next=Environment(100.25);next.cloudDisplacement=new Vector2(10002,-29996);next.windOffset=new Vector3(55002.5f,55002.5f,55002.5f);next.timeOfDay=10.0041667f;
            var cut=new WorldEnvironmentStateEvent();WorldStateSnapshot.SampleAtCut(first,next,100.125,cut);
            Require(cut.Time==100.125 && cut.hasCloudPhase,"cut owns sampled source time and phase availability");
            Near(cut.cloudDisplacement.x,10001);Near(cut.cloudDisplacement.y,-29998);Near(cut.windOffset.x,55001.25f);Near(cut.timeOfDay,10.0020833f);
            var end=new WorldEnvironmentStateEvent();WorldStateSnapshot.SampleAtCut(first,null,100.25,end);
            Near(end.timeOfDay,10.0041667f);Near(end.cloudDisplacement.x,10002.5f);Near(end.windOffset.x,55002.5f);
        });
        test("hangar observed motion windows preserve stationary doors across long event gaps",()=>
        {
            var closed=Door(1,0,0);var opening=Door(1,20,.5f);opening.blendSeconds=.1f;opening.opening=true;
            var held=HangarDoorSnapshot.At(HangarDoorSnapshot.From(closed),HangarDoorSnapshot.From(opening),19);
            var moving=HangarDoorSnapshot.At(HangarDoorSnapshot.From(closed),HangarDoorSnapshot.From(opening),19.95);
            Near(held.openAmount,0);Near(moving.openAmount,.25f);
            Require(!moving.opening,"future direction/status does not leak before its timestamp");
        });
        test("hangar rolling cut retains exact amount and independent ownership without stretching motion",()=>
        {
            var first=Door(7,1,.2f);first.currentSpeed=.4f;var next=Door(7,1.1,.6f);next.currentSpeed=.8f;next.blendSeconds=.1f;
            var cut=new HangarDoorStateEvent();HangarDoorSnapshot.SampleAtCut(first,next,1.05,cut);
            Require(cut.unitId==7 && cut.hangarPath==first.hangarPath && cut.Time==1.05 && cut.blendSeconds==0,"cut baseline keeps channel and resets interpolation instruction");
            Near(cut.openAmount,.4f);Near(cut.currentSpeed,.6f);
            next.unitId=8;Throws<ArgumentException>(()=>HangarDoorSnapshot.SampleAtCut(first,next,1.05,cut));
            var copy=RoundTrip(cut);first.Reset();Require(copy.unitId==7 && copy.hangarPath=="0/3@1","source reset cannot mutate retained channel");
        });
        test("world seek baseline survives actor removal while removed hangar state is discarded",()=>
        {
            using var state=new ReplaySeekState();state.Add(Environment(0));state.Add(new SpawnEvent {unitId=7,ownerId=7,Time=0,jsonKey="hangar",factionName="test"});state.Add(Door(7,1,.5f));
            state.Add(new DespawnEvent {unitId=7,Time=2});var restored=new Queue<IReplayEvent>();state.CopyTo(restored);
            Require(restored.OfType<WorldEnvironmentStateEvent>().Count()==1 && !restored.OfType<HangarDoorStateEvent>().Any(),"global sky and actor lifecycle have separate ownership");
            while(restored.Count>0)ReplayEventFactory.Return(restored.Dequeue());
        });
    }
    private static WorldEnvironmentStateEvent Environment(double time)=>new WorldEnvironmentStateEvent {Time=time,timeOfDay=10,timeFactor=1,cloudHeight=1800,
        conditions=.4f,windSpeed=10,windVelocity=new Vector3(10,0,20),hasCloudPhase=true,moonPhase=14};
    private static HangarDoorStateEvent Door(uint id,double time,float amount)=>new HangarDoorStateEvent {unitId=id,Time=time,hangarPath="0/3@1",openAmount=amount,available=true};
    private static T RoundTrip<T>(T value) where T:class,IReplayEvent=>(T)RollingReplayBuffer.Decode(RollingReplayBuffer.Encode(value));
    private static void Near(float a,float b){if(Math.Abs(a-b)>.001f)throw new Exception($"Expected {b}, got {a}");}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Throws<T>(Action action)where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
}
