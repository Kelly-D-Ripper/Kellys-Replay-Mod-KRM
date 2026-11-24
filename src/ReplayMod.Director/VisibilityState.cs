using System;
using System.Collections.Generic;

namespace ReplayMod.Director;

// Remember the first observed state once. Repeated hiding must not replace it
// with the forced-hidden state, including when another owner re-enables an item.
public sealed class VisibilityState<T> where T : class
{
    private readonly Dictionary<T,bool> original = new Dictionary<T,bool>();
    private readonly List<T> tracked = new List<T>();
    public void Hide(IEnumerable<T> items,Func<T,bool> alive,Func<T,bool> read,Action<T,bool> write)
    {
        Prune(alive);
        foreach(var item in items)HideSingle(item,alive,read,write);
    }
    public void HideSingle(T item,Func<T,bool> alive,Func<T,bool> read,Action<T,bool> write)
    {
        if(item==null || !alive(item))return;
        bool visible=read(item);
        if(!original.ContainsKey(item)){original.Add(item,visible);tracked.Add(item);}
        if(visible)write(item,false);
    }
    // Enforce only already discovered UI. This does not allocate a scene query,
    // LINQ iterator or a temporary key array on every canvas render callback.
    public void HideKnown(Func<T,bool> alive,Func<T,bool> read,Action<T,bool> write)
    {
        // A write can invoke another component's lifecycle callback, registering
        // more UI. The list remains valid and new entries are hidden on arrival.
        for(int index=tracked.Count-1;index>=0;index--)
        {
            var item=tracked[index];
            if(!alive(item)){original.Remove(item);tracked.RemoveAt(index);continue;}
            if(read(item))write(item,false);
        }
    }
    private void Prune(Func<T,bool> alive)
    {
        for(int index=tracked.Count-1;index>=0;index--)
            if(!alive(tracked[index])){original.Remove(tracked[index]);tracked.RemoveAt(index);}
    }
    public void Restore(Func<T,bool> alive,Action<T,bool> write)
    {
        foreach(var pair in original)if(alive(pair.Key))write(pair.Key,pair.Value);
        original.Clear();tracked.Clear();
    }
}
