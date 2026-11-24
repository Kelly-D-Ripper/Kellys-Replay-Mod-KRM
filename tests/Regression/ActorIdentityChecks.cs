using System.Text;
using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Director;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

public static class ActorIdentityChecks
{
    public static void Run(Action<string,Action> test)
    {
        void Check(bool good,string message){if(!good)throw new Exception(message);}
        ActorIdentityEvent Identity(double time=0,string name="Pilot",string callsign="Alpha 1",bool human=true)=>
            new ActorIdentityEvent {Time=time,unitId=7,playerName=name,callsign=callsign,humanControlled=human};
        byte[] Encode(ActorIdentityEvent value)
        {using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))value.Write(writer);return stream.ToArray();}
        ActorIdentityEvent Decode(byte[] bytes)
        {var result=new ActorIdentityEvent();using var reader=new BinaryReader(new MemoryStream(bytes));result.Read(reader);Check(reader.BaseStream.Position==reader.BaseStream.Length,"Identity frame boundary changed");return result;}
        byte[] Raw(byte[] name,byte owner=1,double time=0,ushort? length=null)
        {using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){writer.Write(time);writer.Write(7u);writer.Write(owner);writer.Write(length??(ushort)name.Length);writer.Write(name);writer.Write((ushort)0);}return stream.ToArray();}
        void Reject(byte[] bytes)
        {try{Decode(bytes);}catch(InvalidDataException){return;}catch(EndOfStreamException){return;}throw new Exception("Malformed identity was accepted");}

        test("recorded names normalize markup controls bidi and Unicode boundaries",()=>
        {
            string clean=ActorIdentityText.Normalize("  <Pilot>\r\nName\t\u202E🚀  ");
            Check(clean=="Pilot Name 🚀"&&ActorIdentityText.Valid(clean),"Unsafe controls or markup survived normalization");
            string limited=ActorIdentityText.Normalize(new string('a',127)+"🚀");
            Check(limited.Length<=128&&ActorIdentityText.Valid(limited),"Truncation split a surrogate pair");
            Check(ActorIdentityText.Valid(ActorIdentityText.Normalize("bad\uD800name")),"Malformed surrogate was retained");
        });
        test("identity codec roundtrips human AI and unresolved cached names",()=>
        {
            foreach(var value in new[]{Identity(name:"Zoë 🚀"),Identity(name:"",human:true),Identity(name:"",human:false),Identity(name:new string('x',128))})
            {var copy=Decode(Encode(value));Check(copy.unitId==7&&copy.playerName==value.playerName&&copy.callsign==value.callsign&&copy.humanControlled==value.humanControlled,"Identity roundtrip changed");}
        });
        test("identity decoder rejects invalid UTF8 oversized fields and truncated data",()=>
        {
            Reject(Raw(new byte[]{0xc0,0xaf}));Reject(Raw(Array.Empty<byte>(),length:513));Reject(Raw(new byte[]{0xf0,0x9f}));
            var valid=Encode(Identity());Reject(valid.Take(valid.Length-1).ToArray());
            Reject(Raw(Encoding.UTF8.GetBytes(new string('x',129))));
        });
        test("identity rejects nonplain labels nonfinite times and inconsistent ownership",()=>
        {
            foreach(var text in new[]{"<b>Pilot</b>","A\nB","A\u202EB"," leading","trailing "})Reject(Raw(Encoding.UTF8.GetBytes(text)));
            Reject(Raw(Array.Empty<byte>(),owner:2));Reject(Raw(Encoding.UTF8.GetBytes("Pilot"),owner:0));
            Reject(Raw(Array.Empty<byte>(),time:double.NaN));Reject(Raw(Array.Empty<byte>(),time:double.PositiveInfinity));
            Check(!Identity(time:-1).HasValidPayload(),"Negative identity timestamp accepted");
        });
        test("identity snapshots deduplicate unchanged display text and clear departed owners",()=>
        {
            var initial=new ActorIdentitySnapshot("Pilot","Alpha 1",true);
            Check(initial.Same(new ActorIdentitySnapshot("Pilot","Alpha 1",true)),"Unchanged identity was not deduplicated");
            Check(!initial.Same(new ActorIdentitySnapshot("Other","Alpha 1",true)),"New owner name was omitted");
            var departed=new ActorIdentitySnapshot("Pilot","Alpha 1",false);
            Check(!initial.Same(departed)&&departed.PlayerName==""&&ActorIdentityText.Caption(departed)=="Alpha 1","Departed human name leaked into AI caption");
        });
        test("identity cache rejects old timestamps and exact reused actor lifetimes",()=>
        {
            var cache=new ActorIdentityCache();cache.Apply(Identity(time:2),0);cache.Apply(Identity(time:1,name:"Stale"),0);
            Check(cache.Get(7,0)?.PlayerName=="Pilot"&&cache.Get(7,10)==null,"Old name crossed a timestamp or spawn boundary");
            cache.Forget(7);cache.Apply(Identity(time:11,name:"New pilot"),10);
            Check(cache.Get(7,10)?.PlayerName=="New pilot"&&cache.Get(7,0)==null,"Reused actor inherited old identity");
            cache.Clear();Check(cache.Get(7,10)==null,"Replay close retained identity");
        });
        test("seek checkpoints retain independent current identities and discard despawned names",()=>
        {
            using var state=new ReplaySeekState();state.Add(new SpawnEvent {Time=0,unitId=7,ownerId=7,isAircraft=true});state.Add(Identity());
            using var clone=state.Clone();state.Add(Identity(time:1,name:"Changed"));
            var old=new Queue<IReplayEvent>();clone.CopyTo(old);Check(old.OfType<ActorIdentityEvent>().Single().playerName=="Pilot","Checkpoint identity was aliased");
            foreach(var value in old)ReplayEventFactory.Return(value);
            state.Add(new DespawnEvent {Time=2,unitId=7});var after=new Queue<IReplayEvent>();state.CopyTo(after);
            Check(!after.OfType<ActorIdentityEvent>().Any()&&state.EstimatedBytes==0,"Despawn retained identity or memory accounting");
            foreach(var value in after)ReplayEventFactory.Return(value);
        });
        test("rolling clip keeps identity baseline at zero without rewriting its source",()=>
        {
            using var buffer=new RollingReplayBuffer(10);buffer.Add(new SpawnEvent {Time=0,unitId=7,ownerId=7,isAircraft=true,
                jsonKey="jet",factionName="blue",rotation=new UnityEngine.Quaternion {w=1},weapons=new List<WeaponMount>()});
            var original=Identity(time:1);buffer.Add(original);buffer.Add(new UpdatePositionEvent {Time=20,unitId=7});
            var decoded=buffer.Snapshot(20).Select(RollingReplayBuffer.Decode).ToArray();
            var identity=decoded.OfType<ActorIdentityEvent>().Single();Check(identity.Time==0&&identity.playerName=="Pilot"&&original.Time==1,"Rolling cut lost or mutated identity");
            foreach(var value in decoded)ReplayEventFactory.Return(value);
        });
        test("captions hide for capture seek buffering reload and subject hold",()=>
        {
            Check(ActorCaptionPolicy.Ready(true,false,false,false,false,true,false),"Ready followed subject was hidden");
            foreach(var state in new[]{(false,false,false,false,false,true,false),(true,true,false,false,false,true,false),
                (true,false,true,false,false,true,false),(true,false,false,true,false,true,false),(true,false,false,false,true,true,false),
                (true,false,false,false,false,false,false),(true,false,false,false,false,true,true)})
                Check(!ActorCaptionPolicy.Ready(state.Item1,state.Item2,state.Item3,state.Item4,state.Item5,state.Item6,state.Item7),"Hidden or unavailable caption leaked");
        });
        test("attached captions follow actual path subjects and reject detached selections",()=>
        {
            var path=new[]{new CameraKey {time=0,target=7,relative=true},new CameraKey {time=10,target=7,relative=true}};
            Check(ActorCaptionPolicy.TryAttachedTarget(path,5,out var actor)&&actor==7,"Attached subject was lost");
            path[1].target=8;Check(!ActorCaptionPolicy.TryAttachedTarget(path,5,out _),"Transition invented one subject");
            Check(ActorCaptionPolicy.TryAttachedTarget(path,10,out actor)&&actor==8,"Completed subject transition selected old target");
            path[0].relative=path[1].relative=false;Check(!ActorCaptionPolicy.TryAttachedTarget(path,5,out _),"Detached path showed merely selected aircraft");
            Check(new DirectorProject().showPlayerNames&&new DirectorProject().version==1,"Default caption or project compatibility changed");
        });
        test("identity event pooling clears all player data",()=>
        {
            var value=Identity();value.Reset();Check(value.unitId==0&&value.playerName==""&&value.callsign==""&&!value.humanControlled&&value.Time==0,"Pooled identity retained a previous player");
        });
    }
}
