using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

internal static class CargoMountChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("cargo mount frames preserve active state, pose and bounded identity",()=>{
            var original=State(7,2,true,3);
            var frame=ReplayRecordTransport.EncodeEvent(original);
            using var reader=new BinaryReader(new MemoryStream(frame));
            var read=(CargoMountStateEvent)ReplayRecordTransport.ReadEvent(reader,10);
            if(read.unitId!=7||read.mounts.Length!=1||!read.mounts[0].active||read.mounts[0].position.z!=3||read.mounts[0].key!="42:0:vehicle")throw new Exception("Cargo mount roundtrip failed");
            ReplayEventFactory.Return(read);
            frame[frame.Length-1]^=1;bool rejected=false;try{using var bad=new BinaryReader(new MemoryStream(frame));ReplayRecordTransport.ReadEvent(bad,10);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Corrupt cargo frame was accepted");
        });
        test("seek checkpoints and rolling clips retain the latest cargo state without crossing actor reuse",()=>{
            using var state=new ReplaySeekState();
            state.Add(new SpawnEvent{Time=0,unitId=7,ownerId=7,jsonKey="jet",factionName="blue"});
            state.Add(State(7,1,true,0));state.Add(State(7,2,false,2));
            var copy=new Queue<IReplayEvent>();using(var checkpoint=state.Clone())checkpoint.CopyTo(copy);
            var cargo=copy.OfType<CargoMountStateEvent>().Single();if(cargo.mounts[0].active||cargo.mounts[0].position.z!=2)throw new Exception("Checkpoint restored stale cargo");
            foreach(var e in copy)ReplayEventFactory.Return(e);
            state.Add(new DespawnEvent{Time=3,unitId=7});state.Add(new SpawnEvent{Time=4,unitId=7,ownerId=7,jsonKey="jet",factionName="blue"});
            copy.Clear();state.CopyTo(copy);if(copy.OfType<CargoMountStateEvent>().Any())throw new Exception("Cargo crossed reused actor lifetime");
            foreach(var e in copy)ReplayEventFactory.Return(e);
            using var ring=new RollingReplayBuffer(10);ring.Add(new SpawnEvent{Time=0,unitId=7,ownerId=7,jsonKey="jet",factionName="blue"});ring.Add(State(7,1,true,0));ring.Add(State(7,2,false,2));ring.Trim(20);
            var output=ring.Snapshot(20).Select(RollingReplayBuffer.Decode).ToArray();if(output.OfType<CargoMountStateEvent>().Single().mounts[0].active)throw new Exception("Recent clip lost deployed cargo state");
            foreach(var e in output)ReplayEventFactory.Return(e);
        });
        test("cargo parachutes keep recorded poses through checksum transport and interpolate without showing a future deployment",()=>{
            var closed=Chutes(7,1,false,1);var open=Chutes(7,2,true,2);var moving=Chutes(7,3,true,8);
            var raw=ReplayRecordTransport.EncodeEvent(open);using var reader=new BinaryReader(new MemoryStream(raw));
            var read=(CargoParachuteStateEvent)ReplayRecordTransport.ReadEvent(reader,10);
            if(!read.present||!read.parachutes[0].canopyActive||read.parachutes[0].canopyPosition.x!=2)throw new Exception("Cargo parachute frame lost its state");
            ReplayEventFactory.Return(read);
            var timeline=new CargoParachuteTimeline();timeline.Add(moving);timeline.Add(closed);timeline.Add(open);
            if(timeline.Sample(.5,out _,out _,out _))throw new Exception("Future parachute leaked before its first sample");
            if(!timeline.Sample(1.5,out var a,out var b,out var fraction)||a.parachutes[0].canopyActive||fraction!=0||!ReferenceEquals(a,b))throw new Exception("Canopy opened early");
            if(!timeline.Sample(2.5,out a,out b,out fraction)||!a.parachutes[0].canopyActive||Math.Abs(fraction-.5)>.001||b.parachutes[0].canopyPosition.x!=8)throw new Exception("Parachute pose interpolation failed");
            using var state=new ReplaySeekState();state.Add(new SpawnEvent{Time=0,unitId=7,ownerId=7,jsonKey="vehicle",factionName="blue"});state.Add(closed);state.Add(open);state.Add(moving);
            var output=new Queue<IReplayEvent>();state.CopyTo(output);var saved=output.OfType<CargoParachuteStateEvent>().Single();
            if(saved.Time!=3||saved.parachutes[0].canopyPosition.x!=8)throw new Exception("Checkpoint lost parachute state");
            foreach(var e in output)ReplayEventFactory.Return(e);
        });
        test("parachute playback holds visibility boundaries and rejects malformed layouts",()=>{
            var open=Chutes(7,1,true,1);var closed=Chutes(7,2,false,8);
            var timeline=new CargoParachuteTimeline();timeline.Add(open);timeline.Add(closed);
            if(!timeline.Sample(1.999,out var a,out var b,out var blend)||!a.parachutes[0].canopyActive||blend!=0||!ReferenceEquals(a,b))throw new Exception("Future cut leaked into current frame");
            if(!timeline.Sample(2,out a,out _,out _)||a.parachutes[0].canopyActive)throw new Exception("Parachute cut did not apply on time");
            var bad=Chutes(7,1,true,0);bad.parachutes[0].canopyRotation=new Quaternion();bool rejected=false;
            try{ReplayRecordTransport.EncodeEvent(bad);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Invalid canopy quaternion accepted");
            bad=Chutes(7,1,true,0);bad.parachutes=new[]{bad.parachutes[0],bad.parachutes[0]};rejected=false;
            try{ReplayRecordTransport.EncodeEvent(bad);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Duplicate parachute identity accepted");
        });
    }
    private static CargoMountStateEvent State(uint id,double time,bool active,float z)=>new CargoMountStateEvent{unitId=id,Time=time,mounts=new[]{new CargoMountPose{key="42:0:vehicle",active=active,fired=!active,ammo=active?1:0,position=new Vector3(1,2,z),rotation=new Quaternion{w=1}}}};
    private static CargoParachuteStateEvent Chutes(uint id,double time,bool open,float x)=>new CargoParachuteStateEvent{unitId=id,Time=time,present=true,parachutes=new[]{new CargoParachutePose{key="0:chute",canopyActive=open,linesActive=open,canopyPosition=new GlobalPosition(x,2,3),linesPosition=new GlobalPosition(x,1,3),canopyRotation=new Quaternion{w=1},linesRotation=new Quaternion{w=1},canopyScale=new Vector3(1,1,1),linesScale=new Vector3(1,1,1)}}};
}
