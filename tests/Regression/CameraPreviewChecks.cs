using ReplayMod.Camera;

public static class CameraPreviewChecks
{
    public static int Run()
    {
        int passed=0;
        void Check(string name,Action test){test();passed++;Console.WriteLine("PASS camera preview "+name);}
        void Require(bool value,string reason){if(!value)throw new Exception(reason);}
        Check("capture hides current and newly created owned previews",()=>
        {
            var state=new CameraPreviewVisibility<object>();var first=new object();var createdHidden=new object();
            state.Register(first,true);Require(state.IsVisible(first),"editor marker visible");
            state.SetCaptureHidden(true);state.Register(createdHidden,true);
            Require(!state.IsVisible(first)&&!state.IsVisible(createdHidden),"no legacy marker exposed during capture or seek restore");
            state.SetCaptureHidden(false);Require(state.IsVisible(first)&&state.IsVisible(createdHidden),"capture restores requested preview visibility");
        });
        Check("editor changes during capture survive restoration",()=>
        {
            var state=new CameraPreviewVisibility<object>();var hidden=new object();var shown=new object();
            state.Register(hidden,true);state.Register(shown,false);state.SetCaptureHidden(true);
            state.SetRequested(hidden,false);state.SetRequested(shown,true);
            Require(!state.IsVisible(hidden)&&!state.IsVisible(shown),"capture still hides both");
            state.SetCaptureHidden(false);Require(!state.IsVisible(hidden)&&state.IsVisible(shown),"latest editor request wins");
        });
        Check("repeated frame enforcement preserves requests and allocates no state",()=>
        {
            var state=new CameraPreviewVisibility<object>();var marker=new object();state.Register(marker,true);state.SetCaptureHidden(true);
            for(int i=0;i<100;i++)state.SetCaptureHidden(true);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10000;i++)Require(!state.SetCaptureHidden(true)&&!state.IsVisible(marker),"same capture setting is inert");
            Require(GC.GetAllocatedBytesForCurrentThread()==before,"no steady-state allocations");
            state.SetCaptureHidden(false);Require(state.IsVisible(marker),"initial request retained");
        });
        Check("forgotten object and unknown objects fail closed",()=>
        {
            var state=new CameraPreviewVisibility<object>();var marker=new object();var other=new object();
            state.Register(marker,true);state.Forget(marker);state.SetCaptureHidden(false);
            Require(!state.IsVisible(marker)&&!state.IsVisible(other)&&!state.SetRequested(other,true)&&state.Count==0,"no unrelated scene-object override");
            state.Register(marker,false);Require(!state.IsVisible(marker),"recreated marker receives a fresh request");
        });
        Check("preview registry capacity rejects additions without exposing them",()=>
        {
            var state=new CameraPreviewVisibility<object>(1);var first=new object();var extra=new object();
            Require(state.Register(first,false)&&!state.Register(extra,true),"only owned capacity admitted");
            Require(!state.IsVisible(extra)&&state.Count==1,"overflow stays hidden");
            state.Forget(first);Require(state.Register(extra,true)&&state.IsVisible(extra),"released capacity can be reused");
        });
        return passed;
    }
}
