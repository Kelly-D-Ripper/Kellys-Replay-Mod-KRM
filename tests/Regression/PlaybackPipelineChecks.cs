using System.Diagnostics;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class PlaybackPipelineChecks
{
    public static int Run()
    {
        int passed=0;
        Check("timestamp groups close strictly after target, including full buffers",()=>
        {
            Require(!PlaybackReadiness.CanCommit(1,1,false,false),"duplicate group remains open");
            Require(!PlaybackReadiness.CanCommit(1,1,false,true),"capacity cannot close a duplicate group");
            Require(!PlaybackReadiness.CanCommit(1,5,false,false),"lookahead group equality remains open");
            Require(PlaybackReadiness.CanCommit(1,5.001,false,false),"closed complete lookahead");
            Require(PlaybackReadiness.CanCommit(1,2,false,true),"capacity may shorten lookahead only after due group closes");
            Require(PlaybackReadiness.CanCommit(1,-1,true,false),"empty EOF is complete");
        });
        Check("exact seek offset includes all duplicate events and handles endpoints",()=>
        {
            var times=new[]{0d,0d,1d,1d,1d,3d};
            Require(PlaybackReadiness.UpperBound(times,-.1)==0,"before start");
            Require(PlaybackReadiness.UpperBound(times,0)==2,"all events at zero");
            Require(PlaybackReadiness.UpperBound(times,1)==5,"whole equal timestamp group");
            Require(PlaybackReadiness.UpperBound(times,2)==5,"between groups");
            Require(PlaybackReadiness.UpperBound(times,3)==6,"EOF included");
            Require(PlaybackReadiness.UpperBound(Array.Empty<double>(),0)==0,"empty recording");
        });
        Check("corrupt timestamps, duration and event counts fail clearly",()=>
        {
            foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1d})
            {
                Throws(()=>new ReplayTimelineValidation(invalid,1));
                var validator=new ReplayTimelineValidation(5,1);Throws(()=>validator.Add(invalid));
            }
            Throws(()=>new ReplayTimelineValidation(5,-1));
            var ordered=new ReplayTimelineValidation(5,2);ordered.Add(2);Throws(()=>ordered.Add(1));
            var exceeds=new ReplayTimelineValidation(5,1);Throws(()=>exceeds.Add(5.01));
            var shortFile=new ReplayTimelineValidation(5,2);shortFile.Add(1);Throws(shortFile.Complete);
            var extra=new ReplayTimelineValidation(5,1);extra.Add(1);Throws(()=>extra.Add(2));
            var valid=new ReplayTimelineValidation(5,3);valid.Add(0);valid.Add(2);valid.Add(2);valid.Complete();
        });
        Check("legacy omitted turret counter is the only accepted mismatch",()=>
        {
            var legacy=new ReplayTimelineValidation(5,2,true);legacy.Add(0);legacy.Add(1,true);legacy.Add(2);legacy.Complete();
            Require(legacy.UsedLegacyCount,"known upstream counter defect");
            var incomplete=new ReplayTimelineValidation(5,3,true);incomplete.Add(0);incomplete.Add(1,true);Throws(incomplete.Complete);
            var unexpected=new ReplayTimelineValidation(5,1,true);unexpected.Add(0);unexpected.Add(1);Throws(unexpected.Complete);
            var modern=new ReplayTimelineValidation(5,1);modern.Add(0);Throws(()=>modern.Add(1,true));
        });
        Check("equal-time lifecycle actions preserve the original sample order",()=>
        {
            var buffer=new BoundedEventBuffer(8);
            var sample=new UpdatePositionEvent{unitId=7,Time=2};
            var death=new DespawnEvent{unitId=7,Time=2};
            buffer.Add(sample,default);buffer.Add(death,default);
            Require(buffer.TryTakeIfReady(1,out var first)&&ReferenceEquals(first,sample),"last pose before equal-time death is legal lookahead");
            Require(buffer.TryTakeIfReady(2,out var second)&&ReferenceEquals(second,death),"death follows final pose");
            buffer.MarkActionExecuted(second);ReplayEventFactory.Return(first);ReplayEventFactory.Return(second);
            var birth=new SpawnEvent{unitId=7,ownerId=7,Time=3};
            var move=new UpdatePositionEvent{unitId=7,Time=3};
            buffer.Add(birth,default);buffer.Add(move,default);
            Require(!buffer.TryTakeIfReady(2,out _),"new generation waits for birth");
            Require(buffer.TryTakeIfReady(3,out first)&&ReferenceEquals(first,birth),"birth before same-time pose");
            buffer.MarkActionExecuted(first);
            Require(buffer.TryTakeIfReady(3,out second)&&ReferenceEquals(second,move),"new pose released");
            ReplayEventFactory.Return(first);ReplayEventFactory.Return(second);Require(buffer.FreeSpace==8,"all capacity released");
        });
        Check("future lifecycle lookahead cannot block unrelated aircraft",()=>
        {
            var buffer=new BoundedEventBuffer(8);
            var birth=new SpawnEvent{unitId=7,Time=2};
            var waiting=new UpdateInputsEvent{id=7,Time=2.1};
            var unrelated=new UpdateInputsEvent{id=9,Time=2.2};
            buffer.Add(birth,default);buffer.Add(waiting,default);buffer.Add(unrelated,default);
            Require(buffer.TryTakeIfReady(0,out var e)&&ReferenceEquals(e,unrelated),"other actor still interpolates");
            ReplayEventFactory.Return(e);Require(buffer.Count==2,"held samples retain capacity");
            Require(!buffer.TryTakeIfReady(0,out _),"future actor sample remains held");
            Require(buffer.TryTakeIfReady(2,out e)&&ReferenceEquals(e,birth),"due birth");
            // A staged action is not equivalent to an executed birth.
            Require(!buffer.TryTakeIfReady(2,out _),"staging cannot release a later-generation sample");
            buffer.MarkActionExecuted(e);ReplayEventFactory.Return(e);
            Require(buffer.TryTakeIfReady(2,out e)&&ReferenceEquals(e,waiting),"execution releases lookahead");
            ReplayEventFactory.Return(e);buffer.ClearAndReturnToPool();Require(buffer.FreeSpace==8,"no held slot leak");
        });
        Check("cancelling a blocked producer preserves bounded ownership and reuse",()=>
        {
            var buffer=new BoundedEventBuffer(1);buffer.Add(new EjectionEvent{Time=1},default);
            using var cancel=new CancellationTokenSource();using var entered=new ManualResetEventSlim();
            var blocked=new EjectionEvent{Time=2};
            var task=Task.Run(()=>
            {
                entered.Set();
                try{buffer.Add(blocked,cancel.Token);return false;}
                catch(OperationCanceledException){ReplayEventFactory.Return(blocked);return true;}
            });
            Require(entered.Wait(2000),"producer entered");cancel.Cancel();
            Require(task.Wait(2000)&&task.Result,"cancel releases waiter");
            Require(buffer.Count==1&&buffer.FreeSpace==0,"unqueued event took no slot");
            buffer.ClearAndReturnToPool();buffer.ClearAndReturnToPool();Require(buffer.FreeSpace==1,"idempotent clear");
            buffer.Add(new EjectionEvent{Time=0},default);buffer.ClearAndReturnToPool();Require(buffer.FreeSpace==1,"buffer reusable");
        });
        Check("concurrent duplicate group larger than capacity drains without early commit",()=>
        {
            const int amount=4096;var buffer=new BoundedEventBuffer(8);double horizon=-1;int finished=0;
            var producer=Task.Run(()=>
            {
                for(int n=0;n<amount;n++){buffer.Add(new EjectionEvent{Time=1,unitId=(uint)n},default);Volatile.Write(ref horizon,1);}
                buffer.Add(new EjectionEvent{Time=6},default);Volatile.Write(ref horizon,6);Volatile.Write(ref finished,1);
            });
            var pending=new List<IReplayEvent>();var clock=0d;var timer=Stopwatch.StartNew();
            bool committed=false;
            while(!committed&&timer.ElapsedMilliseconds<5000)
            {
                bool ready=PlaybackReadiness.CanCommit(1,Volatile.Read(ref horizon),Volatile.Read(ref finished)!=0,buffer.FreeSpace==0);
                while(buffer.TryTakeIfReady(1,out var e))pending.Add(e);
                if(ready)
                {Require(pending.Count==amount,"all duplicate events staged before clock advances");clock=1;committed=true;}
                else{Require(clock==0,"producer starvation holds visible clock");Thread.Yield();}
            }
            Require(committed&&producer.Wait(2000),"bounded producer made progress");
            Require(pending.Select(e=>((EjectionEvent)e).unitId).SequenceEqual(Enumerable.Range(0,amount).Select(n=>(uint)n)),"recorded duplicate order retained");
            foreach(var e in pending)ReplayEventFactory.Return(e);buffer.ClearAndReturnToPool();Require(buffer.FreeSpace==8,"full group ownership released");
        });
        Check("bounded checkpoint thinning keeps owned states spread and disposes exactly once",()=>
        {
            int disposed=0;
            using(var cache=new SeekCheckpointCache<Box>(1024,4,1,b=>{Require(!b.Disposed,"single ownership release");b.Disposed=true;disposed++;}))
            {
                for(int n=0;n<100;n++)
                {
                    if(cache.ShouldAdd(n,256))cache.Add(n,n*10,256,new Box());
                    Require(cache.Count<=4&&cache.Bytes<=1024,"budget enforced");
                }
                Require(cache.Find(0)!=null,"early recording checkpoint retained");
                Require(cache.Find(99).Time>0,"later recording checkpoint retained");
                var oversized=new Box();Require(!cache.Add(100,1000,2048,oversized)&&oversized.Disposed,"oversized state released");
            }
            Require(disposed>4,"thinned states and retained final states released");
        });
        Check("checkpoint plus suffix matches complete prefix at duplicate boundaries",()=>
        {
            var encoded=new List<byte[]>();var times=new List<double>();
            void Add(IReplayEvent e){times.Add(e.Time);encoded.Add(RollingReplayBuffer.Encode(e));ReplayEventFactory.Return(e);}
            Add(new SpawnEvent{Time=0,unitId=1,ownerId=1,jsonKey="jet",factionName="blue"});
            for(int n=1;n<=4000;n++)
            {
                double time=n*.1;Add(new UpdatePositionEvent{Time=time,unitId=1,position=new GlobalPosition(n,n*.5f,0)});
                if(n%10==0)
                {
                    Add(new PartDamageEvent{Time=time,unitId=1,partId=1,hitPoints=100-n*.01f});
                    Add(new SetGearEvent{Time=time,unitId=1,deployed=n%20==0});
                }
                if(n==1500){Add(new DespawnEvent{Time=time,unitId=1});Add(new SpawnEvent{Time=time,unitId=1,ownerId=1,jsonKey="new-jet",factionName="blue"});}
            }
            using var prefix=new ReplaySeekState();
            using var cache=new SeekCheckpointCache<ReplaySeekState>(1024*1024,64,30,s=>s.Dispose());
            double previous=-1;
            for(int n=0;n<encoded.Count;n++)
            {
                if(times[n]!=previous&&previous>=0&&cache.ShouldAdd(previous,prefix.EstimatedBytes))
                    cache.Add(previous,n,prefix.EstimatedBytes,prefix.Clone());
                prefix.Add(RollingReplayBuffer.Decode(encoded[n]));previous=times[n];
            }
            long uncachedReads=0,cachedReads=0;var timer=Stopwatch.StartNew();
            foreach(double target in new[]{0d,.05,30,30.001,149.999,150,150.001,300,399.9,400})
            {
                using var full=new ReplaySeekState();
                int end=PlaybackReadiness.UpperBound(times.ToArray(),target);
                for(int n=0;n<end;n++){full.Add(RollingReplayBuffer.Decode(encoded[n]));uncachedReads++;}
                var checkpoint=cache.Find(target);
                using var restored=checkpoint?.State.Clone()??new ReplaySeekState();
                for(int n=(int)(checkpoint?.Offset??0);n<end;n++){restored.Add(RollingReplayBuffer.Decode(encoded[n]));cachedReads++;}
                Require(Snapshot(full).SequenceEqual(Snapshot(restored)),"checkpoint restoration equals complete historical scan at "+target);
            }
            Require(cachedReads<uncachedReads/4,"repeat seek scans bounded suffix");
            Console.WriteLine($"Checkpoint CPU/read-count check: {uncachedReads} prefix reads -> {cachedReads} suffix reads, {timer.ElapsedMilliseconds} ms; excludes Unity scene reload.");
        });
        return passed;

        void Check(string name,Action action){action();passed++;Console.WriteLine("PASS pipeline "+name);}
    }
    private sealed class Box{public bool Disposed;}
    private static string[] Snapshot(ReplaySeekState state)
    {
        var queue=new Queue<IReplayEvent>();state.CopyTo(queue);
        try{return queue.Select(e=>Convert.ToBase64String(RollingReplayBuffer.Encode(e))+(e is SetGearEvent g?":"+g.Instant:"")).ToArray();}
        finally{while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());}
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Throws(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected InvalidDataException");}
}
