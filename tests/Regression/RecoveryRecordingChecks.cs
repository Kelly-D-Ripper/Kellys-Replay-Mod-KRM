using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class RecoveryRecordingChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("format 12 defaults preserve checksummed framing and reject unsupported headers before playback",()=>
        {
            Check(new ReplayRecordingHeader().Format==12,"new recordings advertise the missile-mount reader requirement");
            foreach(byte version in new byte[]{6,7,8,9,10,11,12})
            {
                using var file=new MemoryStream();using var writer=new BinaryWriter(file,Encoding.UTF8,true);
                var header=new ReplayRecordingHeader{Format=version,MapPath="compat",Metadata="{\"schema\":1}"};header.Write(writer);
                writer.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=2,unitId=7}));header.FinalizeHeader(writer,3,1);
                file.Position=0;using var reader=new BinaryReader(file,Encoding.UTF8,true);var read=ReplayRecordingHeader.Read(reader);
                Check(read.Format==version&&read.Complete,"explicit version and commit survive");
                var value=ReplayRecordTransport.ReadEvent(reader,read.Format);Check(value is LandingEvent&&value.Time==2&&file.Position==file.Length,"CRC event boundary unchanged");ReplayEventFactory.Return(value);
                byte[] future=file.ToArray();future[4]=13;using var unknown=new BinaryReader(new MemoryStream(future));Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(unknown));
                byte[] invalid=file.ToArray();Array.Copy(BitConverter.GetBytes(123),0,invalid,header.MarkerOffset,4);using var bad=new BinaryReader(new MemoryStream(invalid));Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(bad,false));
            }
        });
        test("format 11 engine states round-trip and are rejected under older headers",()=>
        {
            var input=new EngineCoreStateEvent{Time=2,unitId=7,ignition=true,rpm=new[]{1500f,2400f}};
            byte[] frame=ReplayRecordTransport.EncodeEvent(input);
            using(var reader=new BinaryReader(new MemoryStream(frame)))
            {
                var value=ReplayRecordTransport.ReadEvent(reader,11) as EngineCoreStateEvent;
                Check(value!=null&&value.unitId==7&&value.ignition&&value.rpm.SequenceEqual(input.rpm),"new core values survive framing");ReplayEventFactory.Return(value);
            }
            using(var reader=new BinaryReader(new MemoryStream(frame)))Throws<InvalidDataException>(()=>ReplayRecordTransport.ReadEvent(reader,10));
            using var legacy=new MemoryStream();using(var writer=new BinaryWriter(legacy,Encoding.UTF8,true)){writer.Write((byte)EventType.EngineCoreState);input.Write(writer);}
            legacy.Position=0;using(var reader=new BinaryReader(legacy))Throws<InvalidDataException>(()=>ReplayRecordTransport.ReadEvent(reader,5));
        });
        test("format 12 missile mount states round-trip and are rejected under older headers",()=>
        {
            var input=new MissileMountStateEvent{Time=2,unitId=7,mounts=new[]{
                new CargoMountPose{key="20:0:aam1",active=false,fired=true,ammo=0,position=new Vector3(0,-1.15f,.06f),rotation=new Quaternion{w=1}},
                new CargoMountPose{key="20:1:aam1",active=true,fired=false,ammo=1,position=new Vector3(0,-.14f,.06f),rotation=new Quaternion{w=1}}}};
            byte[] frame=ReplayRecordTransport.EncodeEvent(input);
            using(var reader=new BinaryReader(new MemoryStream(frame)))
            {
                var value=ReplayRecordTransport.ReadEvent(reader,12) as MissileMountStateEvent;
                Check(value!=null&&value.unitId==7&&value.mounts.Length==2&&!value.mounts[0].active&&value.mounts[0].fired&&value.mounts[0].position.y==-1.15f&&
                    value.mounts[1].key=="20:1:aam1"&&value.mounts[1].active,"mount visibility, rail pose and identity survive framing");ReplayEventFactory.Return(value);
            }
            using(var reader=new BinaryReader(new MemoryStream(frame)))Throws<InvalidDataException>(()=>ReplayRecordTransport.ReadEvent(reader,11));
            using var legacy=new MemoryStream();using(var writer=new BinaryWriter(legacy,Encoding.UTF8,true)){writer.Write((byte)EventType.MissileMountState);input.Write(writer);}
            legacy.Position=0;using(var reader=new BinaryReader(legacy))Throws<InvalidDataException>(()=>ReplayRecordTransport.ReadEvent(reader,5));
            var duplicate=new MissileMountStateEvent{Time=2,unitId=7,mounts=new[]{input.mounts[0],input.mounts[0]}};
            Throws<InvalidDataException>(()=>ReplayRecordTransport.EncodeEvent(duplicate));
            var tooMany=new MissileMountStateEvent{Time=2,unitId=7,mounts=Enumerable.Range(0,MissileMountStateEvent.MaximumMounts+1).Select(i=>
                new CargoMountPose{key="1:"+i+":m",active=true,rotation=new Quaternion{w=1}}).ToArray()};
            Throws<InvalidDataException>(()=>ReplayRecordTransport.EncodeEvent(tooMany));
        });
        test("seek checkpoints keep the latest missile mount state without crossing actor reuse or replacing cargo",()=>
        {
            MissileMountStateEvent Mounts(double time,bool active,float y)=>new MissileMountStateEvent{Time=time,unitId=7,mounts=new[]{
                new CargoMountPose{key="20:0:aam1",active=active,fired=!active,ammo=active?1:0,position=new Vector3(0,y,0),rotation=new Quaternion{w=1}}}};
            using var state=new ReplaySeekState();
            state.Add(new SpawnEvent{Time=0,unitId=7,ownerId=7,jsonKey="jet",factionName="blue"});
            state.Add(Mounts(1,true,-.14f));state.Add(new CargoMountStateEvent{Time=1.5,unitId=7,mounts=new[]{new CargoMountPose{key="42:0:vehicle",active=true,rotation=new Quaternion{w=1}}}});
            state.Add(Mounts(2,false,-1.15f));
            var copy=new Queue<IReplayEvent>();using(var checkpoint=state.Clone())checkpoint.CopyTo(copy);
            var saved=copy.OfType<MissileMountStateEvent>().Single();
            Check(!saved.mounts[0].active&&saved.mounts[0].fired&&saved.mounts[0].position.y==-1.15f,"rewind restores the fired, dropped store");
            Check(copy.OfType<CargoMountStateEvent>().Count()==1,"cargo checkpoint is independent of missile mounts");
            foreach(var e in copy)ReplayEventFactory.Return(e);
            state.Add(new DespawnEvent{Time=3,unitId=7});state.Add(new SpawnEvent{Time=4,unitId=7,ownerId=7,jsonKey="jet",factionName="blue"});
            copy.Clear();state.CopyTo(copy);Check(!copy.OfType<MissileMountStateEvent>().Any(),"missile mounts do not cross a reused actor ID");
            foreach(var e in copy)ReplayEventFactory.Return(e);
            using var ring=new RollingReplayBuffer(10);ring.Add(new SpawnEvent{Time=0,unitId=7,ownerId=7,jsonKey="jet",factionName="blue"});
            ring.Add(Mounts(1,true,-.14f));ring.Add(Mounts(2,false,-1.15f));ring.Trim(20);
            var clip=ring.Snapshot(20).Select(RollingReplayBuffer.Decode).ToArray();
            Check(!clip.OfType<MissileMountStateEvent>().Single().mounts[0].active,"recent clip starts with the store already fired");
            foreach(var e in clip)ReplayEventFactory.Return(e);
        });
        test("older interrupted recording stops recovery before a format 11 event",()=>WithDirectory(dir=>
        {
            string path=Path.Combine(dir,"old-format-with-new-frame.replay.partial");
            using(var file=File.Create(path))using(var writer=new BinaryWriter(file))
            {
                new ReplayRecordingHeader{Format=10,MapPath="compat"}.Write(writer);
                writer.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=1,unitId=1}));
                writer.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=2,unitId=1}));
                writer.Write(ReplayRecordTransport.EncodeEvent(new EngineCoreStateEvent{Time=3,unitId=1,rpm=new[]{1000f}}));
            }
            byte[] original=File.ReadAllBytes(path);var result=ReplayRecordingRecovery.RecoverAsync(path).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(path).SequenceEqual(original)&&result.EventCount==2,"recovery preserves original and stops before unsupported event");
            using var reader=new BinaryReader(File.OpenRead(result.Path));var header=ReplayRecordingHeader.Read(reader);
            Check(header.Format==10&&header.EventCount==2,"recovered copy retains old format and safe prefix");
            for(int i=0;i<2;i++){var value=ReplayRecordTransport.ReadEvent(reader,header.Format);Check(value is LandingEvent,"only old events survive");ReplayEventFactory.Return(value);}
        }));
        test("format 7 interrupted recovery preserves original bytes and publishes only closed timestamp groups",()=>WithDirectory(dir=>
        {
            string source=Path.Combine(dir,"countermeasure-era.replay.partial");
            using(var file=File.Create(source))using(var writer=new BinaryWriter(file))
            {
                new ReplayRecordingHeader{Format=7,MapPath="compat"}.Write(writer);
                foreach(double time in new[]{0d,1d,2d,2d})writer.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=time,unitId=1}));
                writer.Write(new byte[]{1,2,3});
            }
            byte[] before=File.ReadAllBytes(source);var recovered=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();
            Check(File.ReadAllBytes(source).SequenceEqual(before)&&recovered.EventCount==2&&recovered.Duration==1,"read-only source and safe prefix");
            using var output=File.OpenRead(recovered.Path);using var input=new BinaryReader(output);var header=ReplayRecordingHeader.Read(input);
            Check(header.Format==7&&header.Complete,"recovered reader requirement preserved");
            for(int i=0;i<header.EventCount;i++)ReplayEventFactory.Return(ReplayRecordTransport.ReadEvent(input,header.Format));
            Check(output.Position==output.Length,"recovered prefix frame boundaries exact");
        }));
        test("format 6 framed events preserve payloads offsets and legacy decoding",()=>
        {
            Check(ReplayRecordTransport.Crc32(Encoding.ASCII.GetBytes("123456789"),0,9)==0xcbf43926,"standard CRC32 vector");
            using var file=new MemoryStream();using var writer=new BinaryWriter(file,Encoding.UTF8,true);
            var header=Header();header.Write(writer);long first=file.Position;
            writer.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=0,unitId=7}));long second=file.Position;
            writer.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=2,unitId=8}));header.FinalizeHeader(writer,3,2);
            file.Position=0;using var reader=new BinaryReader(file,Encoding.UTF8,true);var read=ReplayRecordingHeader.Read(reader);
            Check(read.Format==6&&read.Complete&&read.DataOffset==first&&read.EventCount==2&&read.Duration==3,"completed header and data offset");
            var one=ReplayRecordTransport.ReadEvent(reader,6);Check(one.Time==0&&file.Position==second,"first frame boundary");ReplayEventFactory.Return(one);
            file.Position=second;var two=ReplayRecordTransport.ReadEvent(reader,6);Check(two.Time==2&&file.Position==file.Length,"seek directly to frame boundary");ReplayEventFactory.Return(two);
            byte[] legacy=RollingReplayBuffer.Encode(new LandingEvent{Time=4,unitId=9});using var raw=new MemoryStream(legacy);using var old=new BinaryReader(raw);
            var value=ReplayRecordTransport.ReadEvent(old,5);Check(value.Time==4&&raw.Position==raw.Length,"legacy event byte stream unchanged");ReplayEventFactory.Return(value);
        });
        test("format 6 rejects corrupt metadata frame sizes CRC timestamps and trailing payloads",()=>
        {
            byte[] good=ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=1,unitId=1});
            var corrupt=(byte[])good.Clone();corrupt[corrupt.Length-1]^=1;RejectFrame(corrupt);
            foreach(int length in new[]{-1,0,8,ReplayRecordTransport.MaximumEventBytes+1})
            {using var s=new MemoryStream();using(var w=new BinaryWriter(s,Encoding.UTF8,true)){w.Write(length);w.Write(0u);}RejectFrame(s.ToArray());}
            RejectFrame(good.Take(good.Length-1).ToArray());
            byte[] body=good.Skip(8).ToArray();body[0]=255;RejectFrame(ReplayRecordTransport.Frame(body));
            body=RollingReplayBuffer.Encode(new LandingEvent{Time=double.NaN,unitId=1});RejectFrame(ReplayRecordTransport.Frame(body));
            body=good.Skip(8).Concat(new byte[]{1}).ToArray();RejectFrame(ReplayRecordTransport.Frame(body));
            body=good.Skip(8).ToArray();Array.Copy(BitConverter.GetBytes(3d),0,body,1,8);RejectFrame(ReplayRecordTransport.Frame(body));
            foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1d,1e100})
            {
                body=good.Skip(8).ToArray();Array.Copy(BitConverter.GetBytes(invalid),0,body,1,8);RejectFrame(ReplayRecordTransport.Frame(body));
                using var output=new MemoryStream();using var write=new BinaryWriter(output,Encoding.UTF8,true);var invalidHeader=Header();invalidHeader.Duration=invalid;
                Throws<InvalidDataException>(()=>invalidHeader.Write(write));
                var validHeader=Header();validHeader.Write(write);Throws<InvalidDataException>(()=>validHeader.FinalizeHeader(write,invalid,1));
                byte[] bytes=output.ToArray();Array.Copy(BitConverter.GetBytes(invalid),0,bytes,validHeader.DurationOffset,8);
                foreach(byte version in new byte[]{5,6,7,8,9,10,11})
                {bytes[4]=version;using var input=new MemoryStream(bytes);using var read=new BinaryReader(input);Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(read));}
            }
            using var file=new MemoryStream();using(var w=new BinaryWriter(file,Encoding.UTF8,true))Header().Write(w);
            byte[] header=file.ToArray();header[9]^=1;using var h=new MemoryStream(header);using var r=new BinaryReader(h);Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(r,false));
            using var pending=new MemoryStream(file.ToArray());using var p=new BinaryReader(pending);Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(p));
        });
        test("recording disk backlog is byte bounded and enqueue never waits for stalled IO",()=>
        {
            var stream=new StalledStream();var writer=new RecoverableReplayWriter("test.replay",100,_=>stream,false);writer.Start(Header());writer.Ready.GetAwaiter().GetResult();stream.Arm=true;
            Check(writer.TryEnqueue(new LandingEvent{Time=0,unitId=1}),"first event accepted");Check(stream.Entered.Wait(3000),"worker entered simulated disk stall");
            var watch=Stopwatch.StartNew();bool refused=false;
            for(int n=1;n<20;n++)if(!writer.TryEnqueue(new LandingEvent{Time=n,unitId=1})){refused=true;break;}
            Check(refused&&writer.Failure!=null&&writer.QueuedBytes<=100&&watch.ElapsedMilliseconds<500,"bounded failure instead of blocking or growing");
            writer.Complete(20);Check(!writer.WaitForCompletionAsync(5).GetAwaiter().GetResult()&&!stream.Disposed,"bounded async stop keeps worker-owned stream open");
            stream.Release.Set();Check(writer.Completion.Wait(3000),"writer drains after disk resumes");
            var result=writer.Completion.Result;Check(!result.Completed&&result.Events>0&&writer.QueuedBytes==0&&stream.Disposed,"complete prefix retained after capacity fault");
            Check(!writer.TryEnqueue(new LandingEvent{Time=30,unitId=1}),"completed worker never accepts more events");writer.Complete(30);
        });
        test("recording worker completes and publishes without touching the original destination",()=>WithDirectory(dir=>
        {
            string path=Path.Combine(dir,"session.replay");var writer=new RecoverableReplayWriter(path);writer.Start(Header());writer.Ready.GetAwaiter().GetResult();
            for(int n=0;n<5;n++)Check(writer.TryEnqueue(new LandingEvent{Time=n,unitId=7}),"event accepted");writer.Complete(5);
            Check(writer.Completion.Wait(3000)&&writer.Completion.Result.Completed&&File.Exists(path)&&!File.Exists(path+".partial"),"atomic completed file publish");
            RealReplayAudit.AuditGenerated(path);
            byte[] before=File.ReadAllBytes(path);var duplicate=new RecoverableReplayWriter(path);duplicate.Start(Header());Throws<IOException>(()=>duplicate.Ready.GetAwaiter().GetResult());
            Check(duplicate.Completion.Wait(3000)&&File.ReadAllBytes(path).SequenceEqual(before),"existing replay never overwritten");
            var aborted=new RecoverableReplayWriter(Path.Combine(dir,"aborted.replay"));aborted.Complete(0);Check(aborted.Completion.IsCompleted&&!aborted.Completion.Result.Completed,"failed startup has no dangling completion or reservation");
        }));
        test("recording mid-frame disk failure leaves a closed recoverable prefix",()=>WithDirectory(dir=>
        {
            string path=Path.Combine(dir,"disk-failure.replay");PartialFailureStream fixture=null;
            var writer=new RecoverableReplayWriter(path,RecoverableReplayWriter.DefaultMaximumQueuedBytes,p=>fixture=new PartialFailureStream(p));
            writer.Start(Header());writer.Ready.GetAwaiter().GetResult();fixture.Armed=true;
            for(int n=0;n<4;n++)Check(writer.TryEnqueue(new LandingEvent{Time=n,unitId=1}),"queued before simulated partial write");
            Check(writer.Completion.Wait(3000),"failed disk write returns promptly");var stopped=writer.Completion.Result;
            Check(!stopped.Completed&&stopped.Events==3&&stopped.Failure.Contains("simulated")&&fixture.Disposed&&writer.QueuedBytes==0&&!File.Exists(path),"failed worker closes its own stream and leaves partial file");
            byte[] original=File.ReadAllBytes(path+".partial");var recovered=ReplayRecordingRecovery.RecoverAsync(path+".partial").GetAwaiter().GetResult();
            Check(recovered.EventCount==2&&recovered.Duration==1&&File.ReadAllBytes(path+".partial").SequenceEqual(original),"closed groups survive partial frame; failed original retained");
            RealReplayAudit.AuditGenerated(recovered.Path);
        }));
        test("recording periodically flushes while a long queue is still draining",()=>
        {
            var fixture=new SlowDrainStream();var writer=new RecoverableReplayWriter("periodic-test.replay",RecoverableReplayWriter.DefaultMaximumQueuedBytes,_=>fixture,false);
            writer.Start(Header());writer.Ready.GetAwaiter().GetResult();fixture.Armed=true;
            for(int n=0;n<125;n++)Check(writer.TryEnqueue(new LandingEvent{Time=n,unitId=1}),"continuous backlog accepted");writer.Complete(125);
            Check(writer.Completion.Wait(5000)&&writer.Completion.Result.Completed&&fixture.FlushedDuringDrain,"one-second flush is checked inside the dequeue loop");
        });
        test("interrupted header finalization recovers closed event groups from invalid mutable fields",()=>WithDirectory(dir=>
        {
            foreach(var fault in new[]{"duration","count","both"})
            {
                string source=Path.Combine(dir,"torn-"+fault+".replay.partial");
                WritePartial(source,new[]{0d,1d,2d,3d},Array.Empty<byte>());
                ReplayRecordingHeader offsets;
                using(var input=new BinaryReader(File.OpenRead(source))) offsets=ReplayRecordingHeader.Read(input,false);
                using(var file=new FileStream(source,FileMode.Open,FileAccess.Write,FileShare.None))using(var writer=new BinaryWriter(file))
                {
                    if(fault!="count"){file.Position=offsets.DurationOffset;writer.Write(double.NaN);}
                    if(fault!="duration"){file.Position=offsets.CountOffset;writer.Write(-1L);}
                }
                byte[] before=File.ReadAllBytes(source);
                var recovered=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();
                Check(recovered.EventCount==3&&recovered.Duration==2&&File.ReadAllBytes(source).SequenceEqual(before),"closed frames recovered and source preserved after "+fault);
                RealReplayAudit.AuditGenerated(recovered.Path);ReplayIntegrityCheck.Check(recovered.Path);
                using(var input=new BinaryReader(File.OpenRead(source)))Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(input));
                byte[] committed=(byte[])before.Clone();Array.Copy(BitConverter.GetBytes(ReplayRecordingHeader.CompletedMarker),0,committed,offsets.MarkerOffset,4);
                using(var input=new BinaryReader(new MemoryStream(committed)))Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(input,false));
            }
        }));
        test("a torn completion marker remains recoverable without weakening normal replay validation",()=>WithDirectory(dir=>
        {
            foreach(int prefix in new[]{1,2,3})
            {
                string source=Path.Combine(dir,"torn-marker-"+prefix+".replay.partial");
                WritePartial(source,new[]{0d,1d,2d,3d},Array.Empty<byte>());
                ReplayRecordingHeader offsets;
                using(var input=new BinaryReader(File.OpenRead(source)))offsets=ReplayRecordingHeader.Read(input,false);
                byte[] marker=BitConverter.GetBytes(ReplayRecordingHeader.CompletedMarker);
                using(var file=new FileStream(source,FileMode.Open,FileAccess.Write,FileShare.None))
                {file.Position=offsets.MarkerOffset;file.Write(marker,0,prefix);}
                byte[] original=File.ReadAllBytes(source);
                using(var input=new BinaryReader(File.OpenRead(source)))Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(input));
                using(var input=new BinaryReader(File.OpenRead(source)))Throws<InvalidDataException>(()=>ReplayRecordingHeader.Read(input,false));
                var recovered=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();
                Check(recovered.EventCount==3&&recovered.Duration==2&&recovered.Reason.Contains("marker")&&File.ReadAllBytes(source).SequenceEqual(original),"validated closed groups recovered and source preserved after partial marker "+prefix);
                ReplayIntegrityCheck.Check(recovered.Path);RealReplayAudit.AuditGenerated(recovered.Path);
            }
        }));
        test("torn-marker recovery still rejects damaged immutable headers",()=>WithDirectory(dir=>
        {
            string source=Path.Combine(dir,"bad-header.replay.partial");
            WritePartial(source,new[]{0d,1d,2d},Array.Empty<byte>());
            ReplayRecordingHeader offsets;
            using(var reader=new BinaryReader(File.OpenRead(source)))offsets=ReplayRecordingHeader.Read(reader,false);
            byte[] bytes=File.ReadAllBytes(source);bytes[9]^=1;bytes[offsets.MarkerOffset]=0x43;File.WriteAllBytes(source,bytes);
            Throws<InvalidDataException>(()=>ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult());
            Check(File.ReadAllBytes(source).SequenceEqual(bytes)&&Directory.GetFiles(dir).Length==1,"invalid header remains untouched and no output is published");
        }));
        test("torn-marker recovery also stops at an independently corrupt event",()=>WithDirectory(dir=>
        {
            string source=Path.Combine(dir,"bad-marker-and-frame.replay.partial");
            WritePartial(source,new[]{0d,1d},Array.Empty<byte>());
            byte[] badFrame=ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=2,unitId=1});badFrame[badFrame.Length-1]^=1;
            using(var file=new FileStream(source,FileMode.Append,FileAccess.Write,FileShare.None))file.Write(badFrame,0,badFrame.Length);
            ReplayRecordingHeader offsets;using(var reader=new BinaryReader(File.OpenRead(source)))offsets=ReplayRecordingHeader.Read(reader,false);
            using(var file=new FileStream(source,FileMode.Open,FileAccess.Write,FileShare.None)){file.Position=offsets.MarkerOffset;file.WriteByte(0x43);}
            byte[] original=File.ReadAllBytes(source);var recovered=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();
            Check(recovered.EventCount==1&&recovered.Duration==0&&recovered.DiscardedBytes>badFrame.Length&&File.ReadAllBytes(source).SequenceEqual(original),"only the previously closed timestamp group was published");
            ReplayIntegrityCheck.Check(recovered.Path);
        }));
        test("format 6 actual movement turret controls and rolling snapshots survive 204 seeks",()=>WithDirectory(dir=>
        {
            string path=Path.Combine(dir,"native-layouts.replay");var writer=new RecoverableReplayWriter(path);writer.Start(Header());writer.Ready.GetAwaiter().GetResult();
            void Record(IReplayEvent value){try{Check(writer.TryEnqueue(value),"actual semantic event accepted: "+value.EventType);}finally{ReplayEventFactory.Return(value);}}
            foreach(uint actor in new[]{1u,2u})Record(new SpawnEvent{unitId=actor,ownerId=actor,jsonKey="jet",factionName="Blue",isAircraft=true,weapons=new List<WeaponMount>(),rotation=new Quaternion{w=1}});
            for(int n=0;n<=1200;n++)
            {
                double time=n*.05;
                foreach(uint actor in new[]{1u,2u})
                {
                    Record(new UpdatePositionEvent{Time=time,unitId=actor,position=new GlobalPosition((float)time*20,1000,actor*50),rotation=new Quaternion{w=1},velocity=new Vector3(20,0,0)});
                    Record(new UpdateInputsEvent{Time=time,id=actor,throttle=.6f,pitch=.1f});
                }
                if(n%3==0)Record(new UpdateTurretEvent{Time=time,attachedUnitId=1,weaponStationIdx=0,turretIdx=0,traverseAngle=n%360,elevationAngle=5});
                if(n%20==0)Record(new GunBulletEvent{Time=time,unitId=1,station=0,partId=4,weaponName="cannon",position=new GlobalPosition((float)time*20,1000,1),velocity=new Vector3(900,0,0),life=3,tracerSize=1,reliability=.5f,color=new Color(1,1,1,1),active=true,tracer=true,sound=true});
                if(n%60==0)Record(new WorldEnvironmentStateEvent{Time=time,timeOfDay=12,conditions=.5f,cloudHeight=2000,windVelocity=new Vector3(5,0,0),hasCloudPhase=true});
            }
            writer.Complete(60);Check(writer.Completion.Wait(5000)&&writer.Completion.Result.Completed,"movement/turret file finalized");RealReplayAudit.AuditGenerated(path);
            foreach(IReplayEvent value in new IReplayEvent[]{new UpdatePositionEvent{Time=7,unitId=123,rotation=new Quaternion{w=1}},new UpdateTurretEvent{Time=8,attachedUnitId=123,weaponStationIdx=2,turretIdx=3,traverseAngle=40,elevationAngle=5}})
            {
                byte[] raw=RollingReplayBuffer.Encode(value),frame=ReplayRecordTransport.FrameLegacyEvent(raw);using var stream=new MemoryStream(frame);using var reader=new BinaryReader(stream);
                var restored=ReplayRecordTransport.ReadEvent(reader,6);try{Check(restored.Time==value.Time&&restored.EventType==value.EventType,"rolling snapshot wraps actual codec timestamp");}finally{ReplayEventFactory.Return(restored);}
            }
        }));
        test("interrupted recovery preserves the original and excludes an unfinished timestamp group",()=>WithDirectory(dir=>
        {
            string source=Path.Combine(dir,"interrupted.replay.partial");
            WritePartial(source,new[]{0d,1d,2d,2d},new byte[]{3,4,5});byte[] before=SHA256.HashData(File.ReadAllBytes(source));
            Check(ReplayRecordingRecovery.FindCandidates(dir).Contains(source),"interrupted file discoverable without decoding it");
            var result=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();
            Check(result.EventCount==2&&result.Duration==1&&result.DiscardedBytes>0&&result.Path!=source&&File.Exists(result.Path),"last unconfirmed tied-time group trimmed");
            Check(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(before),"original interrupted file unchanged");
            RealReplayAudit.AuditGenerated(result.Path);
            var again=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();Check(again.Path!=result.Path&&File.Exists(result.Path),"repeat recovery creates another copy without replacement");
        }));
        test("recovery validates semantic event bodies and preserves exact closed groups",()=>WithDirectory(dir=>
        {
            string source=Path.Combine(dir,"malformed.replay.partial");
            // A frame can have a valid CRC and timestamp while lacking a known
            // event's mandatory unit ID. It must never become playable output.
            byte[] body=new byte[9];body[0]=(byte)EventType.Landing;Array.Copy(BitConverter.GetBytes(2d),0,body,1,8);
            WritePartial(source,new[]{0d,1d},ReplayRecordTransport.Frame(body));
            var result=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();Check(result.EventCount==2&&result.Duration==1&&result.Reason.Contains("byte"),"semantic corruption stops before copy and closes prior time group");
            using(var f=File.OpenRead(result.Path))using(var r=new BinaryReader(f)){var header=ReplayRecordingHeader.Read(r);for(int n=0;n<header.EventCount;n++)ReplayEventFactory.Return(ReplayRecordTransport.ReadEvent(r,6));Check(f.Position==f.Length,"new copy has no malformed frame");}
        }));
        test("recovery preserves closed groups before a checksummed non-finite pose",()=>WithDirectory(dir=>
        {
            string source=Path.Combine(dir,"invalid-pose.replay.partial");
            var move=new UpdatePositionEvent{unitId=7,Time=2,position=new GlobalPosition(1,2,3),rotation=new Quaternion{w=1},velocity=new Vector3(4,5,6)};
            byte[] body=ReplayRecordTransport.EncodeEvent(move).Skip(ReplayRecordTransport.FrameHeaderBytes).ToArray();
            Array.Copy(BitConverter.GetBytes(float.NaN),0,body,21,4);
            WritePartial(source,new[]{0d,1d},ReplayRecordTransport.Frame(body));byte[] original=File.ReadAllBytes(source);
            var recovered=ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult();
            Check(recovered.EventCount==2&&recovered.Duration==1&&recovered.Reason.Contains("non-finite")&&File.ReadAllBytes(source).SequenceEqual(original),"valid prior groups and original survive invalid pose");
            ReplayIntegrityCheck.Check(recovered.Path);RealReplayAudit.AuditGenerated(recovered.Path);
        }));
        test("recovery and recording admission are atomic and cancellation preserves originals",()=>WithDirectory(dir=>
        {
            string source=Path.Combine(dir,"cancelled.replay.partial");WritePartial(source,new[]{0d,1d,2d},Array.Empty<byte>());byte[] before=File.ReadAllBytes(source);
            Check(ReplayRecordingRecovery.TryBeginRecording(),"recording reservation acquired");
            try{Throws<InvalidOperationException>(()=>ReplayRecordingRecovery.RecoverAsync(source));Check(!ReplayRecordingRecovery.TryBeginRecording(),"pending writer prevents another recorder");}
            finally{ReplayRecordingRecovery.EndRecording();}
            Check(ReplayRecordingRecovery.TryBeginRecording(),"rolling recorder reserves capture");var pending=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReplayRecordingRecovery.EndRecordingWhenComplete(pending.Task);
            Check(ReplayRecordingRecovery.HasRecordingReservation&&!ReplayRecordingRecovery.TryBeginRecording(),"stop rolling retains pending save exclusion");
            pending.SetException(new IOException("simulated clip failure"));_=pending.Task.Exception;
            Check(SpinWait.SpinUntil(()=>!ReplayRecordingRecovery.HasRecordingReservation,3000),"failed background clip releases reservation after closing");
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();Throws<OperationCanceledException>(()=>ReplayRecordingRecovery.RecoverAsync(source,cancellation.Token).GetAwaiter().GetResult());
            Check(!ReplayRecordingRecovery.IsBusy&&!ReplayRecordingRecovery.HasRecordingReservation&&File.ReadAllBytes(source).SequenceEqual(before)&&Directory.GetFiles(dir).Length==1,"cancelled output removed; original and admission retained");
            using(var held=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None))Throws<IOException>(()=>ReplayRecordingRecovery.RecoverAsync(source).GetAwaiter().GetResult());
            Check(!ReplayRecordingRecovery.IsBusy,"exclusive-source refusal releases busy gate");
        }));
        test("recording provenance is bounded safe JSON with background file fingerprints",()=>WithDirectory(dir=>
        {
            string metadata=ReplayRecordingProvenance.Build("game","unity","mod","client","1.0",Enumerable.Range(0,200).Select(n=>"mod"+n+"\"\n"+new string('é',600)));
            using(var parsed=JsonDocument.Parse(metadata)){Check(parsed.RootElement.GetProperty("plugins").GetArrayLength()<=96&&parsed.RootElement.GetProperty("plugins_omitted").GetInt32()>0,"bounded plugin inventory and omission count");}
            Check(Encoding.UTF8.GetByteCount(metadata)<ReplayRecordingHeader.MaximumMetadataBytes&&!metadata.Contains("C:\\Users"),"bounded metadata has no implicit personal paths");
            string game=Path.Combine(dir,"game.dll"),mod=Path.Combine(dir,"mod.dll");File.WriteAllText(game,"game bytes");File.WriteAllText(mod,"recorder bytes");
            string withHashes=ReplayRecordingProvenance.AddFingerprints(metadata,game,mod);
            using var json=JsonDocument.Parse(withHashes);Check(json.RootElement.GetProperty("game_assembly_file_sha256").GetString()==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(game))),"actual file fingerprint");
            Check(json.RootElement.GetProperty("recorder_file_sha256").GetString().Length==64,"recorder fingerprint");
        }));
    }
    private static ReplayRecordingHeader Header()=>new ReplayRecordingHeader{Format=6,MapPath="generated-map",Metadata="{\"schema\":1}"};
    private static void WritePartial(string path,double[] times,byte[] tail)
    {using var f=File.Create(path);using var w=new BinaryWriter(f);Header().Write(w);foreach(double time in times)w.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=time,unitId=1}));w.Write(tail);}
    private static void RejectFrame(byte[] bytes)
    {using var f=new MemoryStream(bytes);using var r=new BinaryReader(f);try{var value=ReplayRecordTransport.ReadEvent(r,6);ReplayEventFactory.Return(value);}catch(Exception e)when(e is InvalidDataException||e is EndOfStreamException||e is ArgumentException){return;}throw new Exception("Corrupt frame accepted");}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static void WithDirectory(Action<string> action)
    {string dir=Path.Combine(Path.GetTempPath(),"replay-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);try{action(dir);}finally{Directory.Delete(dir,true);}}
    private sealed class StalledStream:MemoryStream
    {
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(),Release=new ManualResetEventSlim();
        public volatile bool Arm;public bool Disposed;
        public override void Write(byte[] buffer,int offset,int count){if(Arm){Entered.Set();if(!Release.Wait(5000))throw new IOException("Test disk remained stalled");}base.Write(buffer,offset,count);}
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
    }
    private sealed class SlowDrainStream:MemoryStream
    {
        public volatile bool Armed;public bool FlushedDuringDrain;private int frames;
        public override void Write(byte[] buffer,int offset,int count){if(Armed&&count>20){Thread.Sleep(10);frames++;}base.Write(buffer,offset,count);}
        public override void Flush(){if(Armed&&frames>0&&frames<125)FlushedDuringDrain=true;base.Flush();}
    }
    private sealed class PartialFailureStream:Stream
    {
        private readonly FileStream file;private int frames;public volatile bool Armed;public bool Disposed;
        public PartialFailureStream(string path){file=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);}
        public override void Write(byte[] buffer,int offset,int count)
        {if(Armed&&++frames==4){file.Write(buffer,offset,Math.Min(5,count));file.Flush();throw new IOException("simulated mid-frame disk failure");}file.Write(buffer,offset,count);}
        protected override void Dispose(bool disposing){Disposed=true;if(disposing)file.Dispose();base.Dispose(disposing);}
        public override bool CanRead=>file.CanRead;public override bool CanSeek=>file.CanSeek;public override bool CanWrite=>file.CanWrite;public override long Length=>file.Length;
        public override long Position{get=>file.Position;set=>file.Position=value;}public override int Read(byte[] buffer,int offset,int count)=>file.Read(buffer,offset,count);
        public override void Flush()=>file.Flush();public override long Seek(long offset,SeekOrigin origin)=>file.Seek(offset,origin);public override void SetLength(long value)=>file.SetLength(value);
    }
}
