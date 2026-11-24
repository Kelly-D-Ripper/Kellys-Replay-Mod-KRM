using System;
using System.Collections.Generic;

namespace ReplayMod.Core;

// Immutable independently owned prefix states. Compact alternate checkpoints
// when full so long recordings retain checkpoints throughout their duration.
public sealed class SeekCheckpointCache<T> : IDisposable where T : class
{
    public sealed class Entry
    {
        public double Time {get;}
        public long Offset {get;}
        public long Bytes {get;}
        public T State {get;}
        public Entry(double time,long offset,long bytes,T state) {Time=time;Offset=offset;Bytes=bytes;State=state;}
    }
    private readonly List<Entry> entries=new List<Entry>();
    private readonly long byteLimit;
    private readonly int countLimit;
    private readonly Action<T> release;
    private double interval;
    public long Bytes {get;private set;}
    public int Count=>entries.Count;
    public SeekCheckpointCache(long byteLimit,int countLimit,double interval,Action<T> release)
    {
        if(byteLimit<=0||countLimit<2||interval<=0)throw new ArgumentOutOfRangeException();
        this.byteLimit=byteLimit;this.countLimit=countLimit;this.interval=interval;this.release=release;
    }
    public bool ShouldAdd(double time,long estimatedBytes)
    {
        return estimatedBytes<=byteLimit && (entries.Count==0||time-entries[entries.Count-1].Time>=interval);
    }
    public bool Add(double time,long offset,long bytes,T state)
    {
        if(state==null)throw new ArgumentNullException(nameof(state));
        if(bytes<0||bytes>byteLimit||!ShouldAdd(time,bytes)) {release(state);return false;}
        while(entries.Count>0 && (entries.Count>=countLimit||Bytes+bytes>byteLimit))
        {
            for(int i=entries.Count-1;i>=0;i--)if(i%2==1||entries.Count==1)Remove(i);
            interval*=2;
        }
        entries.Add(new Entry(time,offset,bytes,state));Bytes+=bytes;return true;
    }
    public Entry Find(double time)
    {
        int lo=0,hi=entries.Count;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(entries[mid].Time<=time)lo=mid+1;else hi=mid;}
        return lo==0?null:entries[lo-1];
    }
    private void Remove(int i){var entry=entries[i];entries.RemoveAt(i);Bytes-=entry.Bytes;release(entry.State);}
    public void Dispose(){for(int i=entries.Count-1;i>=0;i--)Remove(i);}
}
