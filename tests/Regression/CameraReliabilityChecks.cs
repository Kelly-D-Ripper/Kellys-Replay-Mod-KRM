using ReplayMod.Core;
using ReplayMod.Director;

public static class CameraReliabilityChecks
{
    public static int Run()
    {
        int passed=0;
        Check("fast subject translation cannot create camera trailing distance",()=>
        {
            var tracking=new CameraRelativeTracking();Require(tracking.Update(0,8,-35,0,4),"initial offset");
            for(int frame=1;frame<=120;frame++)
            {
                Require(tracking.Update(0,8,-35,frame/120d,4),"offset update");
                double subjectX=frame*250d,subjectZ=-frame*100d;
                double cameraX=subjectX+tracking.X,cameraZ=subjectZ+tracking.Z;
                Near(Math.Sqrt(Math.Pow(cameraX-subjectX,2)+Math.Pow(cameraZ-subjectZ,2)),35,1e-8);
                Near(tracking.Y,8);
            }
        });
        Check("a ninety-degree aircraft turn eases its offset while anchoring three-hundred-metres-per-second translation",()=>
        {
            var tracking=new CameraRelativeTracking();tracking.Update(0,8,-35,0,4);
            double step=1d/60,expectedBlend=1-Math.Exp(-4*step);
            tracking.Update(-35,8,0,step,4);
            Near(tracking.X,-35*expectedBlend);Near(tracking.Z,-35*(1-expectedBlend));
            Require(tracking.X<0&&tracking.X>-35&&tracking.Z<0,"gradual ninety-degree turn, not an immediate rigid attachment");
            double subjectX=300*step,cameraX=subjectX+tracking.X;
            Near(cameraX-subjectX,tracking.X);
            double x=tracking.X,z=tracking.Z;
            for(int frame=0;frame<120;frame++)tracking.Update(-35,8,0,step,4);
            Near(tracking.X,x);Near(tracking.Z,z);
            tracking.Update(-35,8,0,step*2,4);
            Near(tracking.X,-35*(1-Math.Exp(-4*step*2)));
            Near((300*step*2+tracking.X)-300*step*2,tracking.X);
        });
        Check("relative smoothing is frame-rate independent and origin independent",()=>
        {
            var thirty=new CameraRelativeTracking();var fast=new CameraRelativeTracking();
            thirty.Update(0,0,-10,0,4);fast.Update(0,0,-10,0,4);
            for(int n=1;n<=30;n++)thirty.Update(20,5,-40,n/30d,4);
            for(int n=1;n<=120;n++)fast.Update(20,5,-40,n/120d,4);
            Near(thirty.X,fast.X);Near(thirty.Y,fast.Y);Near(thirty.Z,fast.Z);
            double globalSubject=1000000,origin=-999980;
            Near((globalSubject+origin+fast.Z)-origin,globalSubject+fast.Z,1e-8);
            fast.Update(20,5,-40,1,4);Near(fast.X,thirty.X);
        });
        Check("seek, settings and invalid values reset without poisoning the tracking pose",()=>
        {
            var tracking=new CameraRelativeTracking();tracking.Update(0,0,-10,10,4);
            tracking.Update(20,5,-40,9,4);Near(tracking.X,20);
            tracking.Update(30,8,-50,9,4,true);Near(tracking.X,30);
            Require(!tracking.Update(double.NaN,0,0,10,4),"invalid desired pose rejected");Near(tracking.X,30);
            tracking.Reset();tracking.Update(7,2,1,9,4);Near(tracking.X,7);
            tracking.Update(8,3,2,9.01,0);Near(tracking.X,8);
        });
        Check("subject loss holds one view, pauses safely and never chooses a replacement",()=>
        {
            var hold=new CameraSubjectHold();Require(hold.Missing(10,2)==CameraSubjectLoss.NoPose,"never observed");
            hold.Observe();Require(hold.Missing(10,2)==CameraSubjectLoss.Hold,"first loss");
            for(int n=0;n<200;n++)Require(hold.Missing(10,2)==CameraSubjectLoss.Hold,"paused hold");
            Require(hold.Missing(11.99,2)==CameraSubjectLoss.Hold,"lead through destruction");
            Require(hold.Missing(12,2)==CameraSubjectLoss.Release,"finite hold");
            hold.Reset();Require(hold.Missing(12,2)==CameraSubjectLoss.NoPose,"manual/seek reset discards old view");
        });
        Check("optional sphere framing fits portrait and rejects impossible or invalid bounds",()=>
        {
            Require(CameraSubjectFraming.TryFov(10,40,16d/9,out float wide),"landscape frame");
            Require(CameraSubjectFraming.TryFov(10,40,9d/16,out float tall)&&tall>wide,"portrait must widen vertical FOV");
            Require(CameraSubjectFraming.TryFov(5,40,16d/9,out float small)&&small<wide,"smaller subject");
            Require(CameraSubjectFraming.TryFov(10,40,16d/9,out float turning,10)&&turning>wide,"damped aim includes the off-centre subject");
            Require(!CameraSubjectFraming.TryFov(10,40,1,out _,90),"subject behind view cannot claim a valid framing lens");
            Require(!CameraSubjectFraming.TryFov(10,5,1,out _),"inside bounds keeps manual lens");
            Require(!CameraSubjectFraming.TryFov(double.NaN,40,1,out _),"invalid bounds");
            Require(!CameraSubjectFraming.TryFov(10,40,0,out _),"invalid aspect");
        });
        Check("bookmark generations distinguish actor reuse within the event lead-in",()=>
        {
            var index=new CameraActorLifetimeIndex();index.Spawn(7,1,10);index.Despawn(7,8);index.Spawn(7,9,30);index.Despawn(7,15);
            Require(index.TryGeneration(7,30,out var intended)&&intended.BirthTime==9,"event actor generation");
            Require(index.VisibleGenerationAt(7,6)==10,"lead-in contains an unrelated earlier generation");
            Require(index.VisibleGenerationAt(7,9)==30&&index.VisibleGenerationAt(7,15)==0,"exact birth/removal frame");
            Require(index.TryAt(7,14,out var at)&&at.Generation==30,"old markers resolve ordinary lifetimes");
            Require(!index.TryGeneration(8,30,out _),"token cannot select another actor");
            index.Clear();Require(!index.TryAt(7,14,out _),"new file reset");
            var bounded=new CameraActorLifetimeIndex(2);bounded.Spawn(1,0,1);bounded.Spawn(2,0,2);
            bool rejected=false;try{bounded.Spawn(3,0,3);}catch(System.IO.InvalidDataException){rejected=true;}
            Require(rejected&&bounded.CurrentGeneration(2)==2,"bounded metadata rejects overflow without corrupting previous lifetimes");
        });
        Check("equal-time hit and spawn bookmarks retain file-order generations",()=>
        {
            var index=new CameraActorLifetimeIndex();index.Spawn(2,0,1);long hit=index.CurrentGeneration(2);
            index.Despawn(2,5);index.Spawn(2,5,6);long launch=index.CurrentGeneration(2);
            Require(hit==1&&launch==6,"file-order metadata");
            Require(index.TryGeneration(2,hit,out var old)&&old.DeathTime==5,"final old hit preserved");
            Require(index.TryGeneration(2,launch,out var next)&&next.BirthTime==5,"new spawn preserved");
            Require(!index.TryAt(2,5,out _),"unknown legacy marker at tied boundary is ambiguous");
            Require(index.VisibleGenerationAt(2,5)==6,"rendered complete timestamp selects latest live generation");
        });
        Check("event preparation ignores stale callbacks and waits for the exact subject generation",()=>
        {
            var lifetime=new ReplayActorLifetime(7,30,9,15);
            Require(CameraEventShotPlan.TryCreate(lifetime,12,5,20,1,out var plan)&&plan.StartTime==7,"five-second lead-in");
            var shot=new CameraEventShot();long first=shot.Begin(plan),latest=shot.Begin(plan);
            Require(!shot.Prepared(first)&&shot.Prepared(latest),"obsolete seek cannot enable camera");
            Require(shot.Evaluate(7,10)==CameraEventSubjectAction.None,"old generation never followed");
            Require(shot.Evaluate(9,30)==CameraEventSubjectAction.Follow,"intended generation appears");
            Require(shot.Evaluate(10,30)==CameraEventSubjectAction.None,"single activation");
            shot.Cancel();Require(!shot.IsCurrent(latest)&&shot.Evaluate(10,30)==CameraEventSubjectAction.None,"manual change wins");
        });
        Check("event-shot plans bound lead-in and reject absent ambiguous or malformed subjects",()=>
        {
            var lifetime=new ReplayActorLifetime(2,1,0,20);
            Require(CameraEventShotPlan.TryCreate(lifetime,3,5,20,2,out var nearStart)&&nearStart.StartTime==0,"start clamp");
            Require(CameraEventShotPlan.TryCreate(lifetime,18,100,20,4,out var bounded)&&bounded.StartTime==8,"lead-in bounded to ten seconds");
            Require(!CameraEventShotPlan.TryCreate(lifetime,21,5,30,1,out _),"event outside actor lifetime");
            Require(!CameraEventShotPlan.TryCreate(lifetime,10,double.NaN,30,1,out _),"invalid lead-in");
            Require(!CameraEventShotPlan.TryCreate(lifetime,10,5,30,0,out _),"only stable tracking rigs");
            CameraEventShotPlan.TryCreate(lifetime,10,5,30,1,out var plan);var shot=new CameraEventShot();shot.Prepared(shot.Begin(plan));
            Require(shot.Evaluate(10.6,0)==CameraEventSubjectAction.Unavailable&&!shot.Active,"missing subject has finite wait");
        });
        return passed;
        void Check(string name,Action action){action();passed++;Console.WriteLine("PASS camera reliability "+name);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Near(double actual,double expected,double epsilon=1e-9){if(Math.Abs(actual-expected)>epsilon)throw new Exception($"{actual:R} != {expected:R}");}
}
