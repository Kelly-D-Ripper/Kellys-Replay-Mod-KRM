using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

internal static class CountermeasureAccountingChecks
{
    internal static void Run(Action<string,Action> test)
    {
        test("countermeasure checkpoint accounting stays exact through history trimming clone expiry and clear",()=>
        {
            static void Check(bool value,string why){if(!value)throw new Exception(why);}
            var state=new ReplayCountermeasureState();
            state.Add(new CountermeasureEvent{countermeasureId=1,unitId=1,unitKey="jet",prefabName="flare",tailSeconds=15,rotation=new Quaternion{w=1},remaining=30});
            for(int n=1;n<=500;n++)state.Add(new CountermeasureEvent{countermeasureId=1,phase=CountermeasurePhase.Sample,Time=n*.05,age=n*.05f,remaining=30-n*.05f,tailSeconds=15,rotation=new Quaternion{w=1}});
            var snapshot=new Queue<IReplayEvent>();state.CopyTo(snapshot);
            var launch=(CountermeasureEvent)snapshot.Single();
            long expected=768L+(launch.unitKey.Length+launch.prefabName.Length)*2L+launch.history.Length*96L;
            Check(state.EstimatedBytes==expected&&launch.history.Length<=CountermeasureTimeline.MaxHistory,"trimmed history accounted once");
            var clone=new ReplayCountermeasureState();state.CloneTo(clone);Check(clone.EstimatedBytes==state.EstimatedBytes,"independent clone accounting");
            long before=GC.GetAllocatedBytesForCurrentThread(),total=0;
            for(int n=0;n<10000;n++)total+=state.EstimatedBytes;
            Check(GC.GetAllocatedBytesForCurrentThread()==before&&total==expected*10000,"per-event admission needs no trail scan allocation");
            state.Expire(46);Check(state.Count==0&&state.EstimatedBytes==0&&clone.Count==1,"expiry releases only this owner");
            clone.Clear();Check(clone.Count==0&&clone.EstimatedBytes==0,"clear resets accounting");
            while(snapshot.Count>0)ReplayEventFactory.Return(snapshot.Dequeue());
        });
    }
}
