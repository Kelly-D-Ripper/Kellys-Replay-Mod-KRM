using ReplayMod.UI;

public static class OverlayPlacementChecks
{
    public static int Run()
    {
        int passed=0;
        Test("tooltip stays on screen at every corner and chooses the available side",()=>
        {
            foreach(float x in new[]{0,960,1920})foreach(float y in new[]{0,540,1080})
                Inside(OverlayPlacement.NearPointer(1920,1080,x,y,400,260),1920,1080);
            Check(OverlayPlacement.NearPointer(1920,1080,100,5,400,100).Y>5,"top edge places description below pointer");
            Check(OverlayPlacement.NearPointer(1920,1080,100,1075,400,100).Y<1075-100,"bottom edge places description above pointer");
        });
        Test("oversized descriptions and invalid pointer values stay bounded",()=>
        {
            Inside(OverlayPlacement.NearPointer(320,180,319,179,600,300),320,180);
            Inside(OverlayPlacement.NearPointer(320,180,float.NaN,float.PositiveInfinity,600,300),320,180);
        });
        Test("shortcut prompt remains above the toolbar and reachable at small sizes",()=>
        {
            var prompt=OverlayPlacement.AboveBar(1920,1080,600,160);
            Inside(prompt,1920,1080);Check(prompt.Y+prompt.Height<=1080-188,"prompt clears toolbar");
            Inside(OverlayPlacement.AboveBar(320,180,600,260),320,180);
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS overlay placement "+name);}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        void Inside(OverlayBox b,float width,float height){Check(b.X>=0&&b.Y>=0&&b.X+b.Width<=width+.001f&&b.Y+b.Height<=height+.001f,"overlay outside screen");}
    }
}
