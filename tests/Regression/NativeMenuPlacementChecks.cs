using ReplayMod.UI;

public static class NativeMenuPlacementChecks
{
    public static int Run()
    {
        int passed=0;
        Test("replay entry fits below the existing menu without moving its buttons",()=>
        {
            var source=new OverlayBox(30,100,240,44);var buttons=new[]{source,new OverlayBox(30,154,240,44)};
            Check(NativeMenuPlacement.TryFind(1280,720,source,buttons,out var slot),"room found");
            Check(slot.X==30&&slot.Y==208&&slot.Width==240&&slot.Height==44,"native button shape retained");
        });
        Test("occupied menu bottom uses a clear adjacent slot",()=>
        {
            var source=new OverlayBox(30,100,240,44);var occupied=new[]{source,new OverlayBox(30,150,240,560)};
            Check(NativeMenuPlacement.TryFind(1280,720,source,occupied,out var slot),"adjacent room found");
            Check(slot.X>=284&&slot.Y>=8&&slot.Y+slot.Height<=712,"adjacent button stays on screen");
        });
        Test("full menus and invalid geometry receive no injected button",()=>
        {
            var source=new OverlayBox(30,100,240,44);
            Check(!NativeMenuPlacement.TryFind(640,480,source,new[]{new OverlayBox(0,0,640,480)},out _),"crowded native menu preserved");
            Check(!NativeMenuPlacement.TryFind(float.NaN,480,source,null,out _),"invalid viewport rejected");
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS native replay menu "+name);}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
    }
}
