using ReplayMod.Core;

public static class NavMaterialChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("navigation cleanup claims only new replay-owned renderer material",()=>
        {
            if(!ReplayNavMaterialPolicy.ShouldTrack(true,true,true)||ReplayNavMaterialPolicy.ShouldTrack(false,true,true)||
               ReplayNavMaterialPolicy.ShouldTrack(true,false,true)||ReplayNavMaterialPolicy.ShouldTrack(true,true,false))
                throw new Exception("Shared, live-game or missing renderer material was incorrectly claimed");
        });
        test("inactive renderer cleanup preserves parts detached from the old ancestor",()=>
        {
            if(!ReplayNavMaterialPolicy.CanRelease(false,true,false)||!ReplayNavMaterialPolicy.CanRelease(true,false,false)||
               !ReplayNavMaterialPolicy.CanRelease(true,true,true)||ReplayNavMaterialPolicy.CanRelease(true,true,false))
                throw new Exception("Detached renderer material lifetime was not preserved");
        });
    }
}
