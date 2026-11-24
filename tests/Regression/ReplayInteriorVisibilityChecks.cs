using ReplayMod.Core;

public static class ReplayInteriorVisibilityChecks
{
    private sealed class Renderer
    {public bool visible,alive=true,eligible=true;}
    public static int Run()
    {
        int passed=0;
        void Check(string name,Action test){test();passed++;Console.WriteLine("PASS replay interior "+name);}
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        bool Read(Renderer renderer)=>renderer.visible;
        void Write(Renderer renderer,bool value)=>renderer.visible=value;
        int Restore(ReplayInteriorVisibilityState<Renderer> state)=>state.Restore(renderer=>renderer.alive,renderer=>renderer.eligible,Write);
        Check("enabling existing interior restores its initially hidden state",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>();var renderer=new Renderer();
            Require(state.Apply(renderer,true,Read,Write)&&renderer.visible,"interior visible");
            state.Apply(renderer,true,Read,Write);Require(state.Count==1,"captured once");
            Require(Restore(state)==0&&!renderer.visible&&state.Count==0,"initial visibility restored");
        });
        Check("native camera mode requests become the latest restore baseline",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>();var renderer=new Renderer();
            state.Apply(renderer,true,Read,Write);state.ObserveNative(renderer,true);state.Apply(renderer,true,Read,Write);
            Restore(state);Require(renderer.visible,"native cockpit enabled survives release");
            state.Apply(renderer,true,Read,Write);state.ObserveNative(renderer,false);state.Apply(renderer,true,Read,Write);
            Restore(state);Require(!renderer.visible,"native exterior mode survives release");
        });
        Check("eye-view pilot hiding restores exterior visibility on camera release",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>();var pilot=new Renderer{visible=true};
            state.Apply(pilot,false,Read,Write);Require(!pilot.visible,"pilot does not obstruct eye camera");
            Restore(state);Require(pilot.visible,"existing seated model returns in exterior view");
        });
        Check("damage and ejection prevent restoring removed renderers",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>();var renderer=new Renderer{visible=true};
            state.Apply(renderer,true,Read,Write);renderer.eligible=false;renderer.visible=false;
            Restore(state);Require(!renderer.visible,"removed part/pilot stays hidden");
        });
        Check("destroyed old actor renderers are not written on reconstruction",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>();var old=new Renderer();
            state.Apply(old,true,Read,Write);old.alive=false;
            int writes=0;state.Restore(renderer=>renderer.alive,renderer=>renderer.eligible,(renderer,value)=>writes++);
            Require(writes==0&&state.Count==0,"old lifetime released without touching dead objects");
            var current=new Renderer{visible=true};state.Apply(current,true,Read,Write);Restore(state);
            Require(current.visible,"new lifetime captures its own baseline");
        });
        Check("bounded overrides reject extra targets without mutating them",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>(1);var first=new Renderer();var extra=new Renderer();
            Require(state.Apply(first,true,Read,Write),"first fits");Require(!state.Apply(extra,true,Read,Write)&&!extra.visible,"overflow not touched");
            state.ObserveNative(extra,true);Restore(state);Require(!first.visible&&!extra.visible,"only bound targets restored");
        });
        Check("one failed restore still restores remaining targets and clears ownership",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>();var first=new Renderer();var second=new Renderer();
            state.Apply(first,true,Read,Write);state.Apply(second,true,Read,Write);
            int failures=state.Restore(renderer=>true,renderer=>true,(renderer,value)=>{if(ReferenceEquals(renderer,first))throw new InvalidOperationException();renderer.visible=value;});
            Require(failures==1&&!second.visible&&state.Count==0,"restoration fault isolated");
        });
        Check("external cockpit layer mask retains original world layers on release",()=>
        {
            var mask=new ReplayInteriorCameraMask();int original=(1<<0)|(1<<14);
            int applied=mask.Apply(original,(1<<3)|(1<<14));
            Require(applied==(original|(1<<3)),"only missing interior layer added");
            Require(mask.Restore(applied)==original,"original world layers restored");
        });
        Check("unrelated native mask changes survive applying and releasing cockpit rendering",()=>
        {
            var mask=new ReplayInteriorCameraMask();int applied=mask.Apply(1,1<<3);
            int changed=(applied|(1<<9))&~1;
            applied=mask.Apply(changed,1<<3);
            Require(applied==((1<<3)|(1<<9)),"native added and removed layers retained");
            Require(mask.Restore(applied)==1<<9,"only mod-added interior bit removed");
        });
        Check("returning to eye view and camera replacement release added layer ownership",()=>
        {
            var mask=new ReplayInteriorCameraMask();int external=mask.Apply(1,1<<3);
            Require(mask.Apply(external,0)==1,"eye overlay does not duplicate main-camera cockpit layer");
            Require(mask.Restore(1)==1,"eye release leaves baseline");
            int replacement=mask.Apply(1<<14,1<<3);
            Require(mask.Restore(replacement)==1<<14,"replacement camera baseline is independent");
        });
        Check("releasing one complementary renderer preserves independent ownership",()=>
        {
            var state=new ReplayInteriorVisibilityState<Renderer>();var glass=new Renderer{visible=true};var insert=new Renderer{visible=true};
            state.Apply(glass,false,Read,Write);state.Apply(insert,false,Read,Write);
            state.Release(glass,true,Write);Require(glass.visible&&!insert.visible&&state.Count==1,"exterior glass restores independently of simplified insert");
            state.ObserveNative(insert,false);state.Release(insert,true,Write);Require(!insert.visible&&state.Count==0,"latest native stand-in request survives release");
        });
        Check("isolation identifies only newly exposed cockpit layers",()=>
        {
            var mask=new ReplayInteriorCameraMask();int original=(1<<0)|(1<<14);
            int applied=mask.Apply(original,(1<<3)|(1<<14));
            Require(mask.AddedLayers==1<<3&&(mask.AddedLayers&(1<<0))==0&&(mask.AddedLayers&(1<<14))==0,"already visible body and glass layers never hidden by isolation");
            mask.Apply(applied,0);Require(mask.AddedLayers==0,"removing detailed rendering releases isolation bits");
        });
        return passed;
    }
}
