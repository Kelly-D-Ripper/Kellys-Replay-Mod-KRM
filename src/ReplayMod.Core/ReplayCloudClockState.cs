using System;
using System.Collections.Generic;

namespace ReplayMod.Core;

// Owns only the native cloud layer's three clocks. Duplicate component bindings
// capture once, and replacing a layer restores the previous live components.
public sealed class ReplayCloudClockState<T> where T:class
{
    private readonly struct Entry
    {
        public readonly T System;
        public readonly bool Original;
        public Entry(T system,bool original){System=system;Original=original;}
    }
    private readonly List<Entry> entries=new List<Entry>(3);
    private T first,second,third;
    private bool bound;
    public int Count=>entries.Count;
    public int OriginalUnscaledCount
    {
        get {int count=0;foreach(var entry in entries)if(entry.Original)count++;return count;}
    }
    public bool Matches(T a,T b,T c)=>bound&&ReferenceEquals(first,a)&&ReferenceEquals(second,b)&&ReferenceEquals(third,c);
    public void Bind(T a,T b,T c,Func<T,bool> alive,Func<T,bool> capture,Action<T,bool> apply)
    {
        if(Matches(a,b,c))return;
        Clear(alive,apply);
        try
        {
            Capture(a,alive,capture);Capture(b,alive,capture);Capture(c,alive,capture);
            foreach(var entry in entries)apply(entry.System,true);
            first=a;second=b;third=c;bound=true;
        }
        catch {Clear(alive,apply);throw;}
    }
    private void Capture(T system,Func<T,bool> alive,Func<T,bool> capture)
    {
        if(system==null||!alive(system))return;
        foreach(var entry in entries)if(ReferenceEquals(entry.System,system))return;
        entries.Add(new Entry(system,capture(system)));
    }
    public int Sum(Func<T,bool> alive,Func<T,int> value)
    {
        long total=0;foreach(var entry in entries)if(alive(entry.System))total+=Math.Max(0,value(entry.System));
        return (int)Math.Min(int.MaxValue,total);
    }
    public int Clear(Func<T,bool> alive,Action<T,bool> restore)
    {
        int failures=0;
        try
        {
            foreach(var entry in entries)
                if(alive(entry.System))try{restore(entry.System,entry.Original);}catch(Exception){failures++;}
        }
        finally{entries.Clear();first=second=third=null;bound=false;}
        return failures;
    }
}
