using ReplayMod.Core;

public static class ReplayCloudClockChecks
{
    private sealed class ParticleClock
    {
        public bool Alive=true,Unscaled;
        public int Count,Writes;
    }
    private static readonly Func<ParticleClock,bool> Alive=s=>s.Alive;
    private static readonly Func<ParticleClock,bool> Capture=s=>s.Unscaled;
    private static readonly Action<ParticleClock,bool> Apply=(s,v)=>{s.Unscaled=v;s.Writes++;};
    public static int Run()
    {
        int passed=0;
        Check("cloud clock override preserves each native flag and ignores duplicate bindings",()=>
        {
            var clocks=new ReplayCloudClockState<ParticleClock>();
            var near=new ParticleClock();var far=new ParticleClock{Unscaled=true};
            clocks.Bind(near,far,near,Alive,Capture,Apply);
            Require(clocks.Count==2&&clocks.OriginalUnscaledCount==1&&near.Unscaled&&far.Unscaled,"unique native clocks captured");
            clocks.Bind(near,far,near,Alive,Capture,Apply);
            Require(near.Writes==1&&far.Writes==1,"same layer applies once");
            Require(clocks.Clear(Alive,Apply)==0&&!near.Unscaled&&far.Unscaled&&clocks.Count==0,"exact original flags restored");
            clocks.Clear(Alive,Apply);Require(near.Writes==2&&far.Writes==2,"repeated cleanup does not change restored flags");
        });
        Check("replacing a cloud layer restores old live systems and safely drops destroyed systems",()=>
        {
            var clocks=new ReplayCloudClockState<ParticleClock>();var old=new ParticleClock();var dead=new ParticleClock();
            clocks.Bind(old,dead,null,Alive,Capture,Apply);dead.Alive=false;
            var next=new ParticleClock();clocks.Bind(next,null,null,Alive,Capture,Apply);
            Require(!old.Unscaled&&next.Unscaled&&dead.Writes==1,"scene replacement owns only the new clocks");
            clocks.Clear(Alive,Apply);Require(!next.Unscaled,"next scene flag restored");
        });
        Check("cloud cleanup continues after one native restore fails",()=>
        {
            var clocks=new ReplayCloudClockState<ParticleClock>();var first=new ParticleClock();var second=new ParticleClock();
            clocks.Bind(first,second,null,Alive,Capture,Apply);
            Require(clocks.Clear(Alive,(s,v)=>{if(ReferenceEquals(s,first))throw new InvalidOperationException();Apply(s,v);})==1,"restore failure counted");
            Require(!second.Unscaled&&clocks.Count==0,"other original restored and old ownership released");
        });
        Check("native editor particle clocks age at 1x, slow motion and pause",()=>
        {
            foreach(float timeScale in new[]{1f,.1f,0f})
            {
                var clocks=new ReplayCloudClockState<ParticleClock>();var system=new ParticleClock();
                clocks.Bind(system,null,null,Alive,Capture,Apply);
                var ages=new List<float>();int largest=0;
                for(int frame=0;frame<600;frame++)
                {
                    // Native CloudLayer manually emits once each render frame.
                    // This models clock-driven lifetime, not Unity rendering/FPS.
                    ages.Add(0);float delta=system.Unscaled?1f/60:timeScale/60;
                    for(int i=ages.Count-1;i>=0;i--){ages[i]+=delta;if(ages[i]>=2)ages.RemoveAt(i);}
                    largest=Math.Max(largest,ages.Count);
                }
                Require(largest<=121,"paused/slow replay cannot accumulate unaging puffs");
                clocks.Clear(Alive,Apply);Require(!system.Unscaled,"native gameplay flag restored");
            }
        });
        Check("steady cloud enforcement and diagnostics allocate no managed query arrays",()=>
        {
            var clocks=new ReplayCloudClockState<ParticleClock>();var a=new ParticleClock{Count=20};var b=new ParticleClock{Count=30};var c=new ParticleClock{Count=40};
            Func<ParticleClock,int> count=s=>s.Count;
            clocks.Bind(a,b,c,Alive,Capture,Apply);
            for(int n=0;n<100;n++){clocks.Bind(a,b,c,Alive,Capture,Apply);clocks.Sum(Alive,count);}
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int n=0;n<10000;n++){clocks.Bind(a,b,c,Alive,Capture,Apply);clocks.Sum(Alive,count);}
            Require(GC.GetAllocatedBytesForCurrentThread()==before&&clocks.Sum(Alive,count)==90,"constant three-system state is allocation-free");
            clocks.Clear(Alive,Apply);
        });
        return passed;
        void Check(string name,Action action){action();passed++;Console.WriteLine("PASS replay cloud "+name);}
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
