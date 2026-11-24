using System.Collections.Concurrent;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;

internal static class ReplayOwnershipChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("concurrent event pool retention stays bounded after repeated peak loads",()=>
        {
            for(int cycle=0;cycle<4;cycle++)
            {
                var events=new LandingEvent[ReplayEventPool<LandingEvent>.MaximumRetained*2];
                Parallel.For(0,events.Length,i=>events[i]=ReplayEventFactory.GetEvent<LandingEvent>());
                if(events.Distinct(ReferenceEqualityComparer.Instance).Count()!=events.Length)throw new Exception("pool issued one object to concurrent owners");
                Parallel.ForEach(events,e=>ReplayEventFactory.Return(e));
                if(ReplayEventPool<LandingEvent>.RetainedCount>ReplayEventPool<LandingEvent>.MaximumRetained)throw new Exception("pool retained a peak without a bound");
            }
        });
        test("failed file write returns its current pooled event while leaving queued ownership intact",()=>
        {
            using var queue=new BlockingCollection<IReplayEvent>();
            var current=new LandingEvent {Time=12,unitId=42};var pending=new LandingEvent {Time=13,unitId=43};
            queue.Add(current);queue.Add(pending);queue.CompleteAdding();
            using var writer=new BinaryWriter(new FailedStream());
            try { ReplayFileWriter.Drain(queue,writer);throw new Exception("expected disk failure"); }
            catch(IOException) { }
            if(current.Time!=0||current.unitId!=0||queue.Count!=1)throw new Exception("failed write leaked or consumed an unowned event");
            if(!queue.TryTake(out var remaining)||!ReferenceEquals(remaining,pending)||pending.Time!=13)throw new Exception("queued event ownership changed");
            ReplayEventFactory.Return(remaining);
        });
    }
    private sealed class FailedStream:Stream
    {
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>0;public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override void Flush() { }
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();
        public override void SetLength(long v)=>throw new NotSupportedException();
        public override void Write(byte[] b,int o,int c)=>throw new IOException("simulated full disk");
        public override void WriteByte(byte b)=>throw new IOException("simulated full disk");
    }
}
