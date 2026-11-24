using System;
using System.Collections.Generic;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

// Future pose samples may arrive before the playback clock. Keep visibility
// transitions at their exact time and interpolate only matching live layouts.
public sealed class CargoParachuteTimeline
{
    private readonly List<CargoParachuteStateEvent> samples=new List<CargoParachuteStateEvent>();
    private static readonly Func<CargoParachuteStateEvent,double> At=e=>e.Time;
    public void Add(CargoParachuteStateEvent e)
    {
        if(e==null||e.parachutes==null)return;
        SnapshotHistory.Insert(samples,new CargoParachuteStateEvent{unitId=e.unitId,Time=e.Time,present=e.present,parachutes=(CargoParachutePose[])e.parachutes.Clone()},At);
    }
    public bool Sample(double time,out CargoParachuteStateEvent first,out CargoParachuteStateEvent next,out float fraction)
    {
        first=next=null;fraction=0;
        if(!SnapshotHistory.Locate(samples,time,At,out int left,out int right,out float blend))return false;
        first=samples[left];next=samples[right];
        if(first.present&&next.present&&first.parachutes.Length==next.parachutes.Length)
        {
            bool same=true;
            for(int i=0;i<first.parachutes.Length;i++)
            {
                var a=first.parachutes[i];var b=next.parachutes[i];
                if(!string.Equals(a.key,b.key,StringComparison.Ordinal)||a.canopyActive!=b.canopyActive||a.linesActive!=b.linesActive){same=false;break;}
            }
            if(same)fraction=blend;
        }
        if(fraction==0)next=first;
        SnapshotHistory.Trim(samples,left,12);return true;
    }
}
