using System;
using System.Collections.Generic;

namespace ReplayMod.UI;

public readonly struct TimelineMark
{
    public readonly double Time;
    public readonly bool Manual;
    public TimelineMark(double time,bool manual){Time=time;Manual=manual;}
}
public readonly struct TimelineTick
{
    public readonly double Time;
    public readonly float Position;
    public readonly bool Manual;
    public readonly int Count;
    public TimelineTick(double time,float position,bool manual,int count){Time=time;Position=position;Manual=manual;Count=count;}
}
public static class TimelineMarkers
{
    public static TimelineTick[] Cluster(IEnumerable<TimelineMark> marks,double duration,int requestedBins=256)
    {
        if(!Finite(duration)||duration<=0||marks==null)return Array.Empty<TimelineTick>();
        int bins=Math.Max(1,Math.Min(256,requestedBins));var values=new TimelineTick[bins];var occupied=new bool[bins];
        foreach(var mark in marks)
        {
            if(!Finite(mark.Time)||mark.Time<0||mark.Time>duration)continue;
            int bin=Math.Min(bins-1,(int)(mark.Time/duration*bins));
            var previous=values[bin];int count=occupied[bin]?previous.Count+1:1;
            bool replace=!occupied[bin]||(mark.Manual&&!previous.Manual)||(mark.Manual==previous.Manual&&mark.Time<previous.Time);
            values[bin]=replace?new TimelineTick(mark.Time,(float)(mark.Time/duration),mark.Manual,count):
                new TimelineTick(previous.Time,previous.Position,previous.Manual,count);
            occupied[bin]=true;
        }
        var result=new List<TimelineTick>(bins);for(int n=0;n<bins;n++)if(occupied[n])result.Add(values[n]);return result.ToArray();
    }
    public static double Snap(TimelineTick[] ticks,double requested,double duration,float widthPixels,float tolerancePixels=6)
    {
        if(ticks==null||ticks.Length==0||!Finite(requested)||!Finite(duration)||duration<=0||!Finite(widthPixels)||widthPixels<=0||!Finite(tolerancePixels)||tolerancePixels<=0)return requested;
        if(requested<=0||requested>=duration)return requested;
        int lo=0,hi=ticks.Length;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(ticks[mid].Time<requested)lo=mid+1;else hi=mid;}
        double closest=lo<ticks.Length?ticks[lo].Time:ticks[ticks.Length-1].Time;
        if(lo>0&&Math.Abs(ticks[lo-1].Time-requested)<=Math.Abs(closest-requested))closest=ticks[lo-1].Time;
        return Math.Abs(closest-requested)<=duration*tolerancePixels/widthPixels?closest:requested;
    }
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
}
