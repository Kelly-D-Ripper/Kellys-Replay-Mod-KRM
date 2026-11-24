using ReplayMod.UI;

public static class SuiteLayoutChecks
{
    public static int Run()
    {
        int passed=0;
        var screens=new[]{(1280f,720f),(1920f,1080f),(2560f,1440f),(3440f,1440f),(1280f,1024f),(1080f,1920f)};
        Test("scaled landscape, ultrawide and portrait controls remain inside the canvas",()=>
        {
            foreach(var screen in screens)
            {
                var canvas=SuiteLayout.CanvasForScreen(screen.Item1,screen.Item2);
                Check(canvas.Width>=1919.99f&&canvas.Height>=1079.99f,"Expand preserves reference dimensions");
                Verify(SuiteLayout.Calculate(canvas.Width,canvas.Height));
            }
        });
        Test("compact 1280 canvas keeps every transport and menu control reachable",()=>
        {
            var layout=SuiteLayout.Calculate(1280,720);Verify(layout);
            Check(layout.Navigation.Length==7,"all seven workspaces remain reachable");
            Check(layout.Play.Width>=84&&layout.Pause.Width>=84,"play and pause stay prominent");
            Check(layout.Speed.Width>=102&&layout.HideUi.Width>=160&&layout.Settings.Width>=96,"speed and capture controls retain usable widths");
            Check(layout.Navigation.All(control=>control.Width>=96),"workspace names retain readable text width");
            Check(layout.DockHeight<=178,"unexpected compact roots use at most two control rows");
        });
        Test("normal dock keeps more picture visible without reducing its control targets",()=>
        {
            foreach(var width in new[]{1536f,1920f,2580f})
            {
                var layout=SuiteLayout.Calculate(width,1080);Verify(layout);
                Check(layout.DockHeight<=132,"dock is smaller than the previous 188-unit design");
                Check(Controls(layout).All(c=>c.Height>=40),"transport and navigation targets remain full height");
                Check(layout.Timeline.Height>=24&&layout.Timeline.Width>=width-24,"timeline remains a separate full-width target");
            }
        });
        Test("wide screens gain spacing instead of oversized buttons and panels",()=>
        {
            var layout=SuiteLayout.Calculate(3840,1080);Verify(layout);
            Check(layout.Play.Width<=96&&layout.Navigation.All(c=>c.Width<=96),"button labels retain a compact shape");
            Check(layout.Popout.Width<=560&&layout.Popout.Height<=560,"tools do not expand across the scene");
            Check(layout.Navigation[0].X>layout.SpeedText.X+layout.SpeedText.Width,"workspaces are separated from transport");
            Check(layout.HideUi.X>layout.Navigation[6].X+layout.Navigation[6].Width,"capture stays separate from workspaces");
        });
        Test("panels clear the dock and leave every edge on screen",()=>
        {
            foreach(var size in new[]{(1280f,720f),(1920f,1080f),(1920f,3413f),(1920f,480f)})
            {
                var layout=SuiteLayout.Calculate(size.Item1,size.Item2);Verify(layout);
                Check(layout.Popout.Y>=20-.01f,"panel retains a top margin");
                Check(layout.Popout.Y+layout.Popout.Height<=layout.Dock.Y-10+.01f,"panel clears the dock");
            }
        });
        Test("eighth experimental workspace fits the existing dock at every normal width",()=>
        {
            for(int width=1536;width<=3840;width+=8)
            {
                var layout=SuiteLayout.Calculate(width,1080);Verify(layout);
                Check(layout.Navigation.Length==7,"workspace count incorrect");
                Check(layout.DockHeight==132,"lights do not enlarge the normal dock");
                Check(layout.Navigation.All(box=>box.Width>=88&&box.Height==40),"readable navigation target");
            }
            var reference=SuiteLayout.CanvasForScreen(1280,720);
            Check(SuiteLayout.Calculate(reference.Width,reference.Height).DockHeight==132,"720p scaled dock retains its height");
        });
        Test("unexpected narrow roots wrap controls without obscuring another control",()=>
        {
            foreach(var size in new[]{(1080f,1920f),(720f,1280f),(480f,960f)})
            {var layout=SuiteLayout.Calculate(size.Item1,size.Item2);Verify(layout);Check(layout.DockHeight>132,"narrow roots wrap instead of shrinking text");}
        });
        Test("invalid viewport values fail before generating unusable UI geometry",()=>
        {
            foreach(float invalid in new[]{0,-1,float.NaN,float.PositiveInfinity})
            {Reject(()=>SuiteLayout.Calculate(invalid,1080));Reject(()=>SuiteLayout.Calculate(1920,invalid));Reject(()=>SuiteLayout.CanvasForScreen(invalid,1080));}
            Reject(()=>SuiteLayout.Calculate(100,1080));Reject(()=>SuiteLayout.Calculate(480,240));
        });
        Console.WriteLine($"Suite layout audit: {passed} groups, 0 failures");
        return passed;

        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS suite layout "+name);}
    }

    private static IEnumerable<OverlayBox> Controls(SuiteLayoutPlan p)=>new[]{p.Play,p.Pause,p.FrameBack,p.FrameNext,p.SpeedLess,p.Speed,p.SpeedMore,p.SpeedText,p.HideUi,p.Settings}.Concat(p.Navigation);
    private static void Verify(SuiteLayoutPlan layout)
    {
        Inside(layout.Dock,layout.Width,layout.Height);Inside(layout.Popout,layout.Width,layout.Height);
        var children=Controls(layout).Concat(new[]{layout.Title,layout.Time,layout.Status,layout.Timeline}).ToArray();
        for(int i=0;i<children.Length;i++)
        {
            Inside(children[i],layout.Width,layout.DockHeight);
            for(int j=i+1;j<children.Length;j++)Check(!Overlap(children[i],children[j]),"dock controls overlap");
        }
    }
    private static void Inside(OverlayBox box,float width,float height)=>Check(box.Width>0&&box.Height>0&&box.X>=0&&box.Y>=0&&box.X+box.Width<=width+.01f&&box.Y+box.Height<=height+.01f,"UI rectangle is outside its owner");
    private static bool Overlap(OverlayBox a,OverlayBox b)=>a.X<b.X+b.Width-.001f&&a.X+a.Width>b.X+.001f&&a.Y<b.Y+b.Height-.001f&&a.Y+a.Height>b.Y+.001f;
    private static void Check(bool pass,string reason){if(!pass)throw new Exception(reason);}
    private static void Reject(Action action){try{action();}catch(ArgumentOutOfRangeException){return;}throw new Exception("invalid viewport accepted");}
}

