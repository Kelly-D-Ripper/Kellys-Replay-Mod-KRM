using ReplayMod.Director;

public static class CleanCapturePerformanceChecks
{
    private sealed class Item{public bool Alive=true;public bool Visible;}
    private static readonly Func<Item,bool> Alive=item=>item.Alive;
    private static readonly Func<Item,bool> Visible=item=>item.Visible;
    private static readonly Action<Item,bool> SetVisible=(item,value)=>item.Visible=value;
    public static int Run()
    {
        int passed=0;
        Test("scene discovery is sparse and never repeats for render passes in the same frame",()=>
        {
            var schedule=new CaptureDiscoverySchedule();int scans=0;
            for(int frame=0;frame<1400;frame++)
            {
                double now=frame/140.0;
                if(schedule.ShouldDiscover(now,frame))scans++;
                Require(!schedule.ShouldDiscover(now,frame),"another render pass must not repeat scene discovery");
            }
            Require(scans==5,"ten seconds at 140 FPS uses five fallback queries, rather than thousands");
            schedule.Invalidate();Require(schedule.ShouldDiscover(10,1400),"new scene forces a fresh registration pass");
            schedule.Invalidate();Require(!schedule.ShouldDiscover(10,1400),"invalidation cannot repeat within the same frame");
            Require(schedule.ShouldDiscover(10.01,1401),"deferred invalidation survives until the next frame");
            schedule.Reset();Require(schedule.ShouldDiscover(10.01,1401),"new capture session registers immediately");
        });
        Test("known UI enforcement preserves originals and permits lifecycle registration while hiding",()=>
        {
            var state=new VisibilityState<Item>();var first=new Item{Visible=true};var disabled=new Item();var dynamic=new Item{Visible=true};
            state.HideSingle(first,Alive,Visible,SetVisible);state.HideSingle(disabled,Alive,Visible,SetVisible);
            first.Visible=true;disabled.Visible=true;
            state.HideKnown(Alive,Visible,(item,value)=>
            {
                item.Visible=value;state.HideSingle(dynamic,Alive,Visible,SetVisible);
            });
            Require(!first.Visible&&!disabled.Visible&&!dynamic.Visible,"late UI and existing UI hidden without scene traversal");
            first.Alive=false;state.HideKnown(Alive,Visible,SetVisible);
            state.Restore(Alive,SetVisible);Require(!disabled.Visible&&dynamic.Visible,"destroyed items skipped and first snapshots restored");
        });
        Test("repeated cached hiding allocates no managed scene-query or pruning arrays",()=>
        {
            var state=new VisibilityState<Item>();
            for(int n=0;n<300;n++)state.HideSingle(new Item{Visible=n%2==0},Alive,Visible,SetVisible);
            for(int n=0;n<20;n++)state.HideKnown(Alive,Visible,SetVisible);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int n=0;n<10000;n++)state.HideKnown(Alive,Visible,SetVisible);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Require(bytes==0,"cached hiding must remain allocation free after initial registration");
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS clean capture performance "+name);}
        void Require(bool value,string message){if(!value)throw new Exception(message);}
    }
}
