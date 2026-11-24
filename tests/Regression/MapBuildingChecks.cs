using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class MapBuildingChecks
{
    public static int Run()
    {
        int passed=0;
        Test("building foundation and collapse clock survive binary roundtrip",()=>
        {
            var source=State();var restored=Read(Write(source));
            Require(restored.unitId==source.unitId&&restored.disabled&&restored.duration==3&&restored.Time==10&&restored.collapseStartTime==8,"state metadata");
            Require(restored.anchor.x==12345.125f&&restored.anchor.y==24.5&&restored.anchor.z==-7654.25f&&restored.rotation.w==1,"native global foundation precision");
        });
        Test("building malformed values and truncated payloads fail before playback",()=>
        {
            foreach(var value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1d})
            {var sample=State();sample.Time=value;RejectWrite(sample);}
            foreach(var value in new[]{float.NaN,float.PositiveInfinity,-.1f,120.01f})
            {var sample=State();sample.duration=value;RejectWrite(sample);}
            var bad=State();bad.collapseStartTime=11;RejectWrite(bad);
            bad=State();bad.collapseStartTime=-121;RejectWrite(bad);
            bad=State();bad.anchor=new GlobalPosition(float.NaN,0,0);RejectWrite(bad);
            bad=State();bad.rotation=default;RejectWrite(bad);
            var bytes=Write(State());
            foreach(var count in new[]{0,1,12,20,bytes.Length-1})
            {bool failed=false;try{Read(bytes.Take(count).ToArray());}catch(EndOfStreamException){failed=true;}Require(failed,"truncated building payload");}
            var poisoned=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(double.NaN),0,poisoned,0,8);
            bool rejected=false;try{Read(poisoned);}catch(InvalidDataException){rejected=true;}Require(rejected,"read validates raw payload");
        });
        Test("collapse phase is replay-clock deterministic across pauses and render rates",()=>
        {
            var state=BuildingStateSnapshot.From(State());
            foreach(var time in new[]{8d,8.25,9,10,10.999,11,12,1000})
            {
                double elapsed=Math.Clamp(time-8,0,3);float expected=(float)(.5*9.81*elapsed*elapsed);
                Require(Math.Abs(state.DropAt(time)-expected)<.0001,"closed-form collapse position");
                for(int repeat=0;repeat<8;repeat++)Require(state.DropAt(time)==expected,"pause is stationary");
                Require(state.HiddenAt(time)==(time>=11),"hide at final phase only");
            }
            Require(state.DropAt(double.NaN)==0&&!state.HiddenAt(double.PositiveInfinity),"invalid render times cannot mutate visible state");
            var active=State();active.disabled=false;state=BuildingStateSnapshot.From(active);
            Require(state.DropAt(1000)==0&&!state.HiddenAt(1000),"repair restores foundation rather than continuing historical fall");
        });
        Test("recording during collapse and historical rolling cuts retain phase",()=>
        {
            var initial=State();initial.Time=0;initial.collapseStartTime=-2;
            Require(initial.HasValidPayload()&&Math.Abs(BuildingStateSnapshot.From(initial).DropAt(0)-19.62)<.0001,"ongoing collapse at recording start");
            var historical=State();historical.Time=500;historical.collapseStartTime=8;historical.Rebase(400);
            Require(historical.Time==100&&historical.collapseStartTime==-3&&historical.HasValidPayload()&&BuildingStateSnapshot.From(historical).HiddenAt(0),"old collapse clamps to completed phase");
            var current=State();current.Rebase(9);
            Require(current.Time==1&&current.collapseStartTime==-1&&Math.Abs(BuildingStateSnapshot.From(current).DropAt(1)-19.62)<.0001,"recent cut preserves animation phase");
        });
        Test("checkpoint building anchor is independent of folded spawn position and event pools",()=>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(0,7));state.Add(State());
            state.Add(new UpdatePositionEvent {Time=20,unitId=7,position=new GlobalPosition(1,-900,2),rotation=new Quaternion{w=1}});
            using var copy=state.Clone();var restored=new Queue<IReplayEvent>();copy.CopyTo(restored);
            var collapse=restored.OfType<BuildingStateEvent>().Single();
            Require(restored.OfType<SpawnEvent>().Single().pos.y==-900&&collapse.anchor.y==24.5&&collapse.collapseStartTime==8,"foundation survives pose folding");
            collapse.anchor=new GlobalPosition(0,0,0);foreach(var e in restored)ReplayEventFactory.Return(e);
            restored.Clear();state.CopyTo(restored);Require(restored.OfType<BuildingStateEvent>().Single().anchor.y==24.5,"checkpoint owns independent building state");
            foreach(var e in restored)ReplayEventFactory.Return(e);
        });
        Test("building state respects despawn and recorded ID reuse",()=>
        {
            using var state=new ReplaySeekState();state.Add(Spawn(0,7));state.Add(State());state.Add(new DespawnEvent {Time=12,unitId=7});
            state.Add(Spawn(20,7));var stale=State();state.Add(stale);
            var restored=new Queue<IReplayEvent>();state.CopyTo(restored);
            Require(!restored.OfType<BuildingStateEvent>().Any(),"old collapse must not attach to a reused actor");
            foreach(var e in restored)ReplayEventFactory.Return(e);
        });
        Test("recent recording preserves building foundation and rebases collapse clock",()=>
        {
            using var ring=new RollingReplayBuffer(10);ring.Add(Spawn(0,7));ring.Add(State());
            ring.Add(new UpdatePositionEvent {Time=20,unitId=7,position=new GlobalPosition(1,-900,2),rotation=new Quaternion{w=1}});
            var saved=ring.Snapshot(20).Select(RollingReplayBuffer.Decode).ToArray();var building=saved.OfType<BuildingStateEvent>().Single();
            Require(building.Time==0&&building.collapseStartTime==-2&&building.anchor.y==24.5&&building.HasValidPayload(),"recent clip's first frame retains ongoing/completed collapse");
            foreach(var e in saved)ReplayEventFactory.Return(e);
        });
        Test("replay map shows only eligible exact actor lifetimes",()=>
        {
            Require(ReplayMapPolicy.Visible(true,false,false,false,1),"faction-independent recorded actor");
            Require(!ReplayMapPolicy.Visible(false,false,false,false,1),"old/native PID alone cannot grant ownership");
            Require(!ReplayMapPolicy.Visible(true,true,false,false,1),"disabled actor");
            Require(!ReplayMapPolicy.Visible(true,false,true,false,1),"native hidden building remains hidden");
            Require(!ReplayMapPolicy.Visible(true,false,false,true,1),"spent missile is no longer flying on map");
            foreach(var size in new[]{0f,-1f,float.NaN,float.PositiveInfinity})Require(!ReplayMapPolicy.Visible(true,false,false,false,size),"native no-icon and malformed definitions remain excluded");
            Require(ReplayMapPolicy.RefreshBudget==64&&ReplayMapPolicy.RefreshInterval>=.05,"bounded map upkeep");
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS map/building "+name);}
        void Require(bool result,string message){if(!result)throw new Exception(message);}
        BuildingStateEvent State()=>new BuildingStateEvent {unitId=7,Time=10,disabled=true,collapseStartTime=8,duration=3,anchor=new GlobalPosition(12345.125f,24.5f,-7654.25f),rotation=new Quaternion{w=1}};
        SpawnEvent Spawn(double time,uint id)=>new SpawnEvent {Time=time,unitId=id,ownerId=id,jsonKey="Building",factionName="no faction",rotation=new Quaternion{w=1}};
        byte[] Write(BuildingStateEvent sample){using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))sample.Write(writer);return stream.ToArray();}
        BuildingStateEvent Read(byte[] bytes){using var reader=new BinaryReader(new MemoryStream(bytes));var sample=new BuildingStateEvent();sample.Read(reader);return sample;}
        void RejectWrite(BuildingStateEvent sample){bool failed=false;try{Write(sample);}catch(InvalidDataException){failed=true;}Require(failed,"malformed building state");}
    }
}
