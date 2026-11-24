using System;
using System.IO;
using System.Collections.Generic;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

internal static class ReplayIntegrityChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("preflight finds resources spawned after recording began",()=>{
            string path=Path.Combine(Path.GetTempPath(),"replay-preflight-"+Guid.NewGuid().ToString("N")+".replay");
            try{
                using(var output=new BinaryWriter(File.Create(path))){
                    var header=new ReplayRecordingHeader{Format=9,MapPath="map",Resources=new[]{"original"}};header.Write(output);
                    output.Write(ReplayRecordTransport.EncodeEvent(new SpawnEvent{Time=3,unitId=1,ownerId=1,jsonKey="late-unit",factionName="blue",rotation=new Quaternion{w=1}}));
                    header.FinalizeHeader(output,4,1);
                }
                var result=ReplayIntegrityCheck.Inspect(path);
                if(!result.UnitDefinitions.SetEquals(new[]{"original","late-unit"})||!result.Message.Contains("passed"))throw new Exception("Preflight omitted a required unit");
            }finally{if(File.Exists(path))File.Delete(path);}
        });
        test("format 10 ignores stale header resources but still requires recorded spawns",()=>{
            string path=Path.Combine(Path.GetTempPath(),"replay-stale-resource-"+Guid.NewGuid().ToString("N")+".replay");
            try{
                using(var output=new BinaryWriter(File.Create(path))){
                    var header=new ReplayRecordingHeader{Format=10,MapPath="map",Resources=new[]{"expired-mod"}};header.Write(output);
                    output.Write(ReplayRecordTransport.EncodeEvent(new SpawnEvent{Time=3,unitId=1,ownerId=1,jsonKey="recorded-unit",factionName="blue",rotation=new Quaternion{w=1}}));
                    header.FinalizeHeader(output,4,1);
                }
                var result=ReplayIntegrityCheck.Inspect(path);
                if(!result.UnitDefinitions.SetEquals(new[]{"recorded-unit"})||!result.Message.Contains("passed"))throw new Exception("Recorded spawn was omitted or stale header resource retained");
            }finally{if(File.Exists(path))File.Delete(path);}
        });
        test("missing aircraft loadout identifies the unavailable weapon by name",()=>{
            const string key="test-missing-loadout-961c83";
            var spawn=new SpawnEvent{Time=1,unitId=1,ownerId=1,jsonKey="jet",factionName="blue",isAircraft=true,rotation=new Quaternion{w=1},weapons=new List<WeaponMount>{new WeaponMount{jsonKey=key}}};
            byte[] frame=ReplayRecordTransport.EncodeEvent(spawn);
            using var input=new BinaryReader(new MemoryStream(frame));
            try{ReplayRecordTransport.ReadEvent(input,10);throw new Exception("Missing weapon was accepted");}
            catch(InvalidDataException error){if(!error.Message.Contains("missing weapon")||!error.Message.Contains(key))throw;}
        });
        test("aircraft spawn rejects corrupt loadout counts even with a valid frame checksum",()=>{
            var spawn=new SpawnEvent{Time=1,unitId=1,ownerId=1,jsonKey="jet",factionName="blue",isAircraft=true,rotation=new Quaternion{w=1},weapons=new List<WeaponMount>()};
            using var bytes=new MemoryStream();using(var output=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true)){output.Write((byte)spawn.EventType);output.Write(spawn.Time);spawn.Write(output);output.Flush();}
            byte[] body=bytes.ToArray();
            foreach(int invalid in new[]{-1,1025}){
                Array.Copy(BitConverter.GetBytes(invalid),0,body,body.Length-8,4);
                using var input=new BinaryReader(new MemoryStream(ReplayRecordTransport.Frame(body)));
                try{ReplayRecordTransport.ReadEvent(input,10);throw new Exception("Invalid loadout count was accepted");}
                catch(InvalidDataException error){if(!error.Message.Contains("loadout size"))throw;}
            }
        });
        test("integrity checker detects checksum corruption and truncated files without modifying originals",()=>{
            string path=Path.Combine(Path.GetTempPath(),"replay-integrity-"+Guid.NewGuid().ToString("N")+".replay");
            try{
                using(var writer=new BinaryWriter(File.Create(path))){var header=new ReplayRecordingHeader{Format=9,MapPath="map",Metadata="{\"schema\":1}"};header.Write(writer);writer.Write(ReplayRecordTransport.EncodeEvent(new LandingEvent{Time=2,unitId=7}));header.FinalizeHeader(writer,3,1);}
                var original=File.ReadAllBytes(path);if(!ReplayIntegrityCheck.Check(path).Contains("passed"))throw new Exception("Valid recording rejected");
                var corrupt=(byte[])original.Clone();corrupt[corrupt.Length-1]^=1;File.WriteAllBytes(path,corrupt);
                bool rejected=false;try{ReplayIntegrityCheck.Check(path);}catch(InvalidDataException){rejected=true;}
                if(!rejected||!System.Linq.Enumerable.SequenceEqual(corrupt,File.ReadAllBytes(path)))throw new Exception("Corruption accepted or modified");
                File.WriteAllBytes(path,original[..^1]);rejected=false;try{ReplayIntegrityCheck.Check(path);}catch(EndOfStreamException){rejected=true;}
                if(!rejected)throw new Exception("Truncated recording accepted");
            }finally{if(File.Exists(path))File.Delete(path);}
        });
        test("integrity rejects non-finite movement even with a valid checksum",()=>{
            string path=Path.Combine(Path.GetTempPath(),"replay-nonfinite-"+Guid.NewGuid().ToString("N")+".replay");
            try{
                var move=new UpdatePositionEvent{Time=1,unitId=7,position=new GlobalPosition(1,2,3),rotation=new Quaternion{w=1},velocity=new Vector3(4,5,6)};
                var body=ReplayRecordTransport.EncodeEvent(move).Skip(ReplayRecordTransport.FrameHeaderBytes).ToArray();
                foreach(int offset in new[]{21,25,29,33,37,41,45,49,53,57})foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
                    var corrupt=(byte[])body.Clone();Array.Copy(BitConverter.GetBytes(bad),0,corrupt,offset,4);
                    using(var writer=new BinaryWriter(File.Create(path))){var header=new ReplayRecordingHeader{MapPath="map"};header.Write(writer);writer.Write(ReplayRecordTransport.Frame(corrupt));header.FinalizeHeader(writer,2,1);}
                    try{ReplayIntegrityCheck.Check(path);throw new Exception("Non-finite movement accepted at byte "+offset);}
                    catch(InvalidDataException){}
                }
            }finally{if(File.Exists(path))File.Delete(path);}
        });
        test("integrity rejects non-finite spawn poses even with a valid checksum",()=>{
            string path=Path.Combine(Path.GetTempPath(),"replay-invalid-spawn-"+Guid.NewGuid().ToString("N")+".replay");
            try{
                var spawn=new SpawnEvent{Time=1,unitId=7,ownerId=7,jsonKey="vehicle",factionName="blue",pos=new GlobalPosition(1,2,3),rotation=new Quaternion{w=1},startingVelocity=new Vector3(4,5,6)};
                var body=ReplayRecordTransport.EncodeEvent(spawn).Skip(ReplayRecordTransport.FrameHeaderBytes).ToArray();int offset;
                using(var reader=new BinaryReader(new MemoryStream(body))){reader.ReadByte();reader.ReadDouble();reader.ReadDouble();reader.ReadUInt32();ReplayRecordingHeader.ReadString(reader);ReplayRecordingHeader.ReadString(reader);offset=(int)reader.BaseStream.Position;}
                for(int i=0;i<10;i++)foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
                    var corrupt=(byte[])body.Clone();Array.Copy(BitConverter.GetBytes(bad),0,corrupt,offset+i*4,4);
                    using(var writer=new BinaryWriter(File.Create(path))){var header=new ReplayRecordingHeader{MapPath="map"};header.Write(writer);writer.Write(ReplayRecordTransport.Frame(corrupt));header.FinalizeHeader(writer,2,1);}
                    try{ReplayIntegrityCheck.Check(path);throw new Exception("Non-finite spawn accepted at component "+i);}
                    catch(InvalidDataException){}
                }
            }finally{if(File.Exists(path))File.Delete(path);}
        });
        test("integrity rejects non-finite turret and damage data with valid checksums",()=>{
            string path=Path.Combine(Path.GetTempPath(),"replay-invalid-state-"+Guid.NewGuid().ToString("N")+".replay");
            try{
                var turret=new UpdateTurretEvent{Time=1,attachedUnitId=7,elevationAngle=3,traverseAngle=4};
                var damage=new PartDamageEvent{Time=1,unitId=7,partId=2,hitPoints=90,pierce=1,blast=2,fire=3,impact=4};
                var examples=new[]{(ReplayRecordTransport.EncodeEvent(turret).Skip(8).ToArray(),new[]{9,13}),(ReplayRecordTransport.EncodeEvent(damage).Skip(8).ToArray(),new[]{22,26,30,34,38})};
                foreach(var (body,offsets) in examples)foreach(int offset in offsets)foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
                    var corrupt=(byte[])body.Clone();Array.Copy(BitConverter.GetBytes(bad),0,corrupt,offset,4);
                    using(var writer=new BinaryWriter(File.Create(path))){var header=new ReplayRecordingHeader{MapPath="map"};header.Write(writer);writer.Write(ReplayRecordTransport.Frame(corrupt));header.FinalizeHeader(writer,2,1);}
                    try{ReplayIntegrityCheck.Check(path);throw new Exception("Non-finite turret or damage accepted at byte "+offset);}
                    catch(InvalidDataException){}
                }
            }finally{if(File.Exists(path))File.Delete(path);}
        });
        test("writer retains corrupted partial rather than publishing it",()=>{
            string root=Path.Combine(Path.GetTempPath(),"replay-publish-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                string path=Path.Combine(root,"flight.replay");
                var writer=new RecoverableReplayWriter(path,RecoverableReplayWriter.DefaultMaximumQueuedBytes,p=>new CorruptOnClose(p));
                writer.Start(new ReplayRecordingHeader{Format=9,MapPath="map"});writer.Ready.GetAwaiter().GetResult();
                if(!writer.TryEnqueue(new LandingEvent{Time=1,unitId=7}))throw new Exception("Event rejected");writer.Complete(2);
                if(!writer.Completion.Wait(5000)||writer.Completion.Result.Completed||File.Exists(path)||!File.Exists(path+".partial"))throw new Exception("Corrupt recording was published or partial lost");
            }finally{Directory.Delete(root,true);}
        });
        test("quit waits for independent pending file writes without waiting for Unity continuations",()=>{
            var completed=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            ReplayRecordingRecovery.TrackPendingWrite(completed.Task);
            var release=System.Threading.Tasks.Task.Run(async()=>{await System.Threading.Tasks.Task.Delay(150);completed.TrySetResult(true);});
            var clock=System.Diagnostics.Stopwatch.StartNew();
            if(!ReplayRecordingRecovery.WaitForPendingWritesOnQuit(2000)||!completed.Task.IsCompleted||clock.ElapsedMilliseconds<80)
                throw new Exception("Quit did not wait for the pending background write");
            release.GetAwaiter().GetResult();
            var stalled=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            ReplayRecordingRecovery.TrackPendingWrite(stalled.Task);
            if(ReplayRecordingRecovery.WaitForPendingWritesOnQuit(500))throw new Exception("Quit waited indefinitely for a stalled write");
            stalled.TrySetResult(true);
        });
    }
    private sealed class CorruptOnClose:FileStream
    {
        public CorruptOnClose(string path):base(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read){}
        protected override void Dispose(bool disposing)
        {
            if(disposing&&CanSeek&&Length>0){Position=Length-1;int b=ReadByte();Position=Length-1;WriteByte((byte)(b^1));Flush(true);}
            base.Dispose(disposing);
        }
    }
}
