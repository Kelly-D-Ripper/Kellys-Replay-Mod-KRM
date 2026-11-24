using System;
using System.Collections.Generic;

namespace ReplayMod.Core;

// A rendering override tracks later native visibility requests as its baseline,
// so releasing a shot does not undo a cockpit-state change made during it.
public sealed class ReplayInteriorVisibilityState<T> where T:class
{
    private readonly Dictionary<T,bool> originals=new Dictionary<T,bool>();
    private readonly int capacity;
    public int Count=>originals.Count;
    public ReplayInteriorVisibilityState(int capacity=256)
    {if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;}
    public bool Apply(T target,bool visible,Func<T,bool> read,Action<T,bool> write)
    {
        if(target==null)return false;
        if(!originals.ContainsKey(target))
        {
            if(originals.Count>=capacity)return false;
            originals.Add(target,read(target));
        }
        write(target,visible);return true;
    }
    public void ObserveNative(T target,bool visible)
    {if(target!=null&&originals.ContainsKey(target))originals[target]=visible;}
    public void Release(T target,bool eligible,Action<T,bool> write)
    {if(target!=null&&originals.TryGetValue(target,out bool original)){originals.Remove(target);write(target,original&&eligible);}}
    public int Restore(Func<T,bool> alive,Func<T,bool> eligible,Action<T,bool> write)
    {
        int failures=0;
        try
        {
            foreach(var entry in originals)
                if(alive(entry.Key))
                    try{write(entry.Key,entry.Value&&eligible(entry.Key));}
                    catch(Exception){failures++;}
        }
        finally{originals.Clear();}
        return failures;
    }
}

// A camera mask override owns only bits it added. Native or other-mod changes
// to unrelated layers survive repeated application and eventual release.
public sealed class ReplayInteriorCameraMask
{
    private bool bound;
    private int baseline,added,lastApplied;
    public int AddedLayers=>added;
    public int Apply(int current,int requestedLayers)
    {
        if(!bound){bound=true;baseline=current;}
        else if(current!=lastApplied)baseline=current&~added;
        added=requestedLayers&~baseline;
        return lastApplied=baseline|requestedLayers;
    }
    public int Restore(int current)
    {int result=bound?current&~added:current;bound=false;baseline=added=lastApplied=0;return result;}
}
