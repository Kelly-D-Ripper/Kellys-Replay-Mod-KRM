using ReplayMod.Core;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class ReplayMotionSmoothingChecks
{
    public static int Run()
    {
        int passed=0;
        Test("motion smoothing opt-in presets have finite bounded recording-time windows",()=>
        {
            Check(!ReplayMotionSmoothing.Enabled(0)&&!ReplayMotionSmoothing.Enabled(-1)&&!ReplayMotionSmoothing.Enabled(4),"invalid and off presets preserve raw");
            Check(ReplayMotionSmoothing.Radius(1)<ReplayMotionSmoothing.Radius(2)&&ReplayMotionSmoothing.Radius(2)<ReplayMotionSmoothing.Radius(3),"increasing strength");
            Check(ReplayMotionSmoothing.RequiredLookAheadSeconds<PlaybackReadiness.LookAheadSeconds,"existing lookahead covers filter support");
        });
        Test("motion filter preserves constant velocity, vertical motion and smooth acceleration",()=>
        {
            foreach(int preset in new[]{1,2,3})foreach(double time in new[]{.87,1.01,1.31})
            {
                var linear=Samples(t=>Pose(t,200*t,50*t,10*t,200,50,10));
                Check(Filter(linear,time,preset,out var output),"linear sample available");Near(output.position.x,200*time,.0001);Near(output.position.y,50*time,.0001);Near(output.position.z,10*time,.0001);
                var accelerated=Samples(t=>Pose(t,200*t+10*t*t,50*t,0,200+20*t,50,0));
                Check(Filter(accelerated,time,preset,out var curve),"quadratic available");
                Near(curve.position.x,200*time+10*time*time,.002);Near(curve.velocity.x,200+20*time,.0001);
            }
        });
        Test("all motion presets reduce jitter at native20Hz and frame-quantized aircraft recording rates",()=>
        {
            foreach(double interval in new[]{.05,.06})foreach(int preset in new[]{1,2,3})
            {
                var samples=Enumerable.Range(0,81).Select(i=>Pose(i*interval,200*i*interval+(i%2==0?1:-1))).ToList();
                double rawError=0,smoothError=0;
                foreach(int i in Enumerable.Range(20,20))
                {
                    double time=i*interval;Check(Filter(samples,time,preset,out var filtered),"preset supports actual rate");
                    rawError+=1;smoothError+=Math.Pow(filtered.position.x-200*time,2);
                }
                Check(smoothError<rawError*.80,"every preset improves20Hz jitter");
            }
        });
        Test("motion lookahead closes the full recording-time support and EOF remains immediately ready",()=>
        {
            Check(!ReplayMotionSmoothing.LookAheadReady(2,2.5,false),"future window incomplete");
            Check(!ReplayMotionSmoothing.LookAheadReady(2,2+ReplayMotionSmoothing.RequiredLookAheadSeconds,false),"strict watermark closes equal timestamp group");
            Check(ReplayMotionSmoothing.LookAheadReady(2,2.7,false),"complete support");Check(ReplayMotionSmoothing.LookAheadReady(2,2.1,true),"EOF uses raw unavailable tail");
            var full=new BoundedEventBuffer(4);
            for(int i=0;i<4;i++)full.Add(new WeaponManagerFireEvent{unitId=2,Time=.1+i*.01},default);
            Check(full.FreeSpace==0&&!full.TryTakeIfReady(0,out _),"future-actions-only density can block a centered window");
            Check(PlaybackReadiness.CanCommit(0,.13,false,true),"original motion remains playable via established capacity path");full.ClearAndReturnToPool();
        });
        Test("motion smoothing stays continuous across low-frame-rate kernel support boundaries",()=>
        {
            foreach(double interval in new[]{2.0/30,.08,.10,.125})foreach(int preset in new[]{1,2,3})
            {
                var samples=Enumerable.Range(0,81).Select(i=>Pose(i*interval,200*i*interval+(i%2==0?1:-1))).ToList();
                double previous=0;bool initialized=false;double rawEnergy=0,smoothEnergy=0;
                for(int i=0;i<=2000;i++)
                {
                    double time=2+i*.0002;
                    Check(Filter(samples,time,preset,out var output),"bounded low-cadence fit");
                    double residual=output.position.x-200*time;
                    if(initialized)Check(Math.Abs(residual-previous)<.015,"no visible sample-count switch at cadence"+interval+" preset"+preset);
                    previous=residual;initialized=true;
                    SnapshotHistory.Locate(samples,time,p=>p.time,out int a,out int b,out _);
                    double raw=ReplayPoseInterpolation.At(samples[a],samples[b],time).position.x-200*time;
                    rawEnergy+=raw*raw;smoothEnergy+=residual*residual;
                }
                if(interval<=.08)Check(smoothEnergy<rawEnergy*.9,"light and stronger presets improve30fps/25fps captures");
            }
        });
        Test("motion filter attenuates alternating network position and heading jitter without delayed flight",()=>
        {
            var samples=Samples(t=>{int n=(int)Math.Round(t/.02);var p=Pose(t,200*t+(n%2==0?2:-2));p.rotation=Yaw(n%2==0?2:-2);return p;});
            double rawError=0,filteredError=0;
            for(int i=35;i<65;i++)
            {
                double time=i*.02;Check(Filter(samples,time,3,out var filtered),"synthetic flight filtered");
                rawError+=4;filteredError+=Math.Pow(filtered.position.x-200*time,2);
                Check(Math.Abs(filtered.rotation.y)<Math.Sin(2*Math.PI/360)*.4,"heading shake reduced");
            }
            Check(filteredError<rawError*.12,"position jitter energy reduced by at least88 percent");
        });
        Test("motion filter is replay-time deterministic across slow motion, pause and arbitrary evaluation order",()=>
        {
            var samples=Samples(t=>Pose(t,100*t+Math.Sin(t*120)));var expected=new Dictionary<double,PositionSnapshot>();
            foreach(double time in new[]{.77,.80,.93,1.17,1.23}){Check(Filter(samples,time,2,out var pose),"baseline");expected[time]=pose;}
            foreach(double time in new[]{1.23,.77,.93,.93,.80,1.17,.77}){Check(Filter(samples,time,2,out var pose),"repeat");Exact(expected[time],pose);}
        });
        Test("motion filtering never mutates recorded samples or accumulates drift",()=>
        {
            var samples=Samples(t=>Pose(t,200*t+Math.Sin(t*90)));var baseline=samples.ToArray();
            for(int repeat=0;repeat<1000;repeat++)Filter(samples,1.011,3,out _);
            for(int i=0;i<samples.Count;i++)Exact(samples[i],baseline[i]);
            Check(Filter(samples,1,0,out _)==false,"off bypass");
        });
        Test("motion filtering resolves quaternion hemispheres and preserves a real smooth turn",()=>
        {
            var samples=Samples(t=>{var p=Pose(t,200*t);p.rotation=Yaw(t*40);if(((int)Math.Round(t/.02)&1)==1)p.rotation=new Quaternion{x=-p.rotation.x,y=-p.rotation.y,z=-p.rotation.z,w=-p.rotation.w};return p;});
            Check(Filter(samples,1.011,3,out var output),"wrapped quaternion sample");
            Near(Math.Abs(output.rotation.y),Math.Abs(Yaw(40.44).y),.0005);Near(Math.Abs(output.rotation.w),Math.Abs(Yaw(40.44).w),.0005);
        });
        Test("motion filtering is global-coordinate based and preserves map translation",()=>
        {
            var baseline=Samples(t=>Pose(t,200*t+Math.Sin(t*90)));var translated=baseline.Select(p=>{p.position=new GlobalPosition(p.position.x+20000,p.position.y-5000,p.position.z+10000);return p;}).ToList();
            Check(Filter(baseline,1.01,2,out var a),"baseline support");Check(Filter(translated,1.01,2,out var b),"translated support");
            Near(b.position.x-a.position.x,20000,.003);Near(b.position.y-a.position.y,-5000,.003);Near(b.position.z-a.position.z,10000,.003);
        });
        Test("motion filtering leaves spawn edges, EOF tails and missing future samples raw",()=>
        {
            var samples=Samples(t=>Pose(t,200*t));
            Check(!ReplayMotionSmoothing.TrySample(samples,.1,3,0,double.PositiveInfinity,out _),"birth edge");
            Check(!Filter(samples,1.9,3,out _),"EOF support");
            Check(!Filter(samples.Where(p=>p.time<=1.01).ToList(),1,2,out _),"future missing");
            Check(!Filter(samples,2,2,out _),"last exact sample");
        });
        Test("motion filtering cannot bridge teleports, impacts, hard turns or sample gaps",()=>
        {
            foreach(int kind in new[]{0,1,2,3})
            {
                var samples=Samples(t=>{var p=Pose(t,200*t);if(t>=1){if(kind==0)p.position.x+=500;if(kind==1)p.velocity.x=0;if(kind==2)p.rotation=Yaw(90);}return p;});
                if(kind==3)samples.RemoveAll(p=>p.time>.84&&p.time<1.16);
                Check(!Filter(samples,1,3,out _),"discontinuity kind"+kind);
            }
        });
        Test("motion filtering cannot pre-blend damage, ejection, destruction or reused lifetimes",()=>
        {
            var samples=Samples(t=>Pose(t,200*t));
            foreach(double marker in new[]{.9,1.0,1.1,1.3})Check(!ReplayMotionSmoothing.TrySample(samples,1,3,0,marker,out _),"damage barrier");
            Check(!ReplayMotionSmoothing.TrySample(samples,1,2,.95,double.PositiveInfinity,out _),"new lifetime cannot consume old samples");
        });
        Test("motion seek checkpoint windows reproduce continuous sampling and own independent copies",()=>
        {
            var samples=Samples(t=>Pose(t,200*t+Math.Sin(120*t)));using var state=new ReplaySeekState(true);
            state.Add(new SpawnEvent {unitId=1,ownerId=1,isAircraft=true,weapons=new List<WeaponMount>(),jsonKey="jet",factionName="blue",rotation=Yaw(0)});
            foreach(var pose in samples.Where(p=>p.time<=1))state.Add(Move(pose));
            using var checkpoint=state.Clone();var queue=new Queue<IReplayEvent>();checkpoint.CopyTo(queue);
            var history=queue.OfType<UpdatePositionEvent>().Select(PositionSnapshot.Create).ToList();
            history.AddRange(samples.Where(p=>p.time>1));
            Check(Filter(samples,1.01,3,out var continuous),"continuous support");Check(Filter(history,1.01,3,out var restored),"seek has past support");Exact(continuous,restored);
            state.Add(new DespawnEvent{unitId=1,Time=1.1});Check(state.EstimatedBytes==0&&checkpoint.EstimatedBytes>0,"lifetimes and accounting independent");
            while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());
        });
        Test("motion-history retention and evaluation are bounded for dense recordings",()=>
        {
            var dense=new List<PositionSnapshot>();using var state=new ReplaySeekState(true);
            state.Add(new SpawnEvent {unitId=1,ownerId=1,isAircraft=true,weapons=new List<WeaponMount>(),jsonKey="jet",rotation=Yaw(0)});
            for(int i=0;i<5000;i++){var pose=Pose(i*.001,i*.2);dense.Add(pose);state.Add(Move(pose));}
            Check(!Filter(dense,2,3,out _),"unsafe density bypasses bounded fit");
            var queue=new Queue<IReplayEvent>();state.CopyTo(queue);Check(queue.OfType<UpdatePositionEvent>().Count()<=ReplayMotionSmoothing.MaxWindowSamples+1,"bounded checkpoint support");
            ReplayMotionSmoothing.TrimHistory(dense,4.9);Check(dense[0].time<=4.9-ReplayMotionSmoothing.HistorySeconds&&dense[1].time>=4.9-ReplayMotionSmoothing.HistorySeconds,"one edge anchor retained");
            while(queue.Count>0)ReplayEventFactory.Return(queue.Dequeue());
        });
        Test("future damage guards are separate from lifecycle action execution and reset safely",()=>
        {
            var buffer=new BoundedEventBuffer(10);var spawn=new SpawnEvent{unitId=4,Time=0};var damage=new PartDamageEvent{unitId=4,Time=1};var move=new UpdatePositionEvent{unitId=4,Time=1.2};
            buffer.Add(spawn,default);buffer.Add(damage,default);buffer.Add(move,default);
            Check(buffer.TryTakeIfReady(0,out var first)&&ReferenceEquals(first,spawn),"spawn due");buffer.MarkActionExecuted(first);ReplayEventFactory.Return(first);
            Near(buffer.NextMotionBarrier(4),1,0);Check(buffer.TryTakeIfReady(0,out var future)&&ReferenceEquals(future,move),"damage does not block existing interpolation");ReplayEventFactory.Return(future);
            Check(buffer.TryTakeIfReady(1,out var action)&&ReferenceEquals(action,damage),"damage retains action time");buffer.MarkActionExecuted(action);ReplayEventFactory.Return(action);
            Check(double.IsPositiveInfinity(buffer.NextMotionBarrier(4)),"executed marker released before event pool return");buffer.ClearAndReturnToPool();
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static List<PositionSnapshot> Samples(Func<double,PositionSnapshot> make)=>Enumerable.Range(0,101).Select(i=>make(i*.02)).ToList();
    private static PositionSnapshot Pose(double t,double x,double y=0,double z=0,double vx=200,double vy=0,double vz=0)=>new PositionSnapshot{time=t,position=new GlobalPosition((float)x,(float)y,(float)z),velocity=new Vector3((float)vx,(float)vy,(float)vz),rotation=Yaw(0)};
    private static Quaternion Yaw(double degrees)=>new Quaternion{y=(float)Math.Sin(degrees*Math.PI/360),w=(float)Math.Cos(degrees*Math.PI/360)};
    private static UpdatePositionEvent Move(PositionSnapshot p){var e=new UpdatePositionEvent{unitId=1};e.CopyFromSnapshot(p);return e;}
    private static bool Filter(List<PositionSnapshot> samples,double time,int preset,out PositionSnapshot output)=>ReplayMotionSmoothing.TrySample(samples,time,preset,0,double.PositiveInfinity,out output);
    private static void Exact(PositionSnapshot a,PositionSnapshot b){Check(a.time==b.time&&a.position.x==b.position.x&&a.position.y==b.position.y&&a.position.z==b.position.z&&a.rotation.x==b.rotation.x&&a.rotation.y==b.rotation.y&&a.rotation.z==b.rotation.z&&a.rotation.w==b.rotation.w&&a.velocity.x==b.velocity.x&&a.velocity.y==b.velocity.y&&a.velocity.z==b.velocity.z,"exact deterministic pose");}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Near(double value,double expected,double tolerance){if(Math.Abs(value-expected)>tolerance)throw new Exception($"{value:R} != {expected:R} (tolerance {tolerance})");}
}
