using ReplayMod.Core;
public static class SnapshotHistoryChecks
{
    private readonly record struct Sample(double Time,float Value);
    public static int Run()
    {
        int passed=0;
        Test("history inserts restored anchors before lookahead and replaces equal-time samples",()=>
        {
            var samples=new List<Sample>();
            foreach(var sample in new[]{new Sample(12,120),new Sample(8,80),new Sample(10,999),new Sample(10,100)})
                SnapshotHistory.Insert(samples,sample,s=>s.Time);
            Check(samples.Count==3 && samples.Select(s=>s.Time).SequenceEqual(new[]{8d,10d,12d}),"sorted unique timestamps");
            Check(SnapshotHistory.Locate(samples,11,s=>s.Time,out int a,out int b,out float t),"bracket exists");
            Near(samples[a].Value+(samples[b].Value-samples[a].Value)*t,110);
            Check(!SnapshotHistory.Insert(samples,new Sample(double.NaN,1),s=>s.Time) && !SnapshotHistory.Insert(samples,new Sample(double.PositiveInfinity,1),s=>s.Time),"invalid times rejected");
        });
        Test("future pose, control and turret samples do not apply ahead of their time",()=>
        {
            var samples=new List<Sample>{new Sample(5,20),new Sample(6,40)};
            Check(!SnapshotHistory.Locate(samples,4,s=>s.Time,out _,out _,out _),"first future sample is withheld");
            Check(SnapshotHistory.Locate(samples,5,s=>s.Time,out int a,out int b,out float t) && a==0 && b==1 && t==0,"exact boundary");
            Check(SnapshotHistory.Locate(samples,9,s=>s.Time,out a,out b,out t) && a==b && a==1 && t==0,"tail holds final sample");
            Check(!SnapshotHistory.Locate(samples,double.NaN,s=>s.Time,out _,out _,out _),"invalid target rejected");
        });
        Test("turret interpolation crosses the angle wrap through the short arc",()=>
        {
            Near(SnapshotHistory.InterpolateAngle(359,1,.5f),360);
            Near(SnapshotHistory.InterpolateAngle(1,359,.5f),0);
            Near(SnapshotHistory.InterpolateAngle(-170,170,.5f),-180);
            Near(SnapshotHistory.InterpolateAngle(20,40,-1),20);
            Near(SnapshotHistory.InterpolateAngle(20,40,2),40);
        });
        Test("long and tail histories trim without losing the interpolation anchor",()=>
        {
            var samples=Enumerable.Range(0,1000).Select(i=>new Sample(i,i)).ToList();
            Check(SnapshotHistory.Locate(samples,800.5,s=>s.Time,out int a,out _,out _),"long history bracket");
            SnapshotHistory.Trim(samples,a,500);Check(samples[0].Time==799,"keeps one older anchor");
            Check(SnapshotHistory.Locate(samples,800.5,s=>s.Time,out a,out int b,out float t),"bracket after trimming");
            Near(samples[a].Value+(samples[b].Value-samples[a].Value)*t,800.5f);
            Check(SnapshotHistory.Locate(samples,1200,s=>s.Time,out a,out _,out _),"tail index");
            SnapshotHistory.Trim(samples,a,12);Check(samples.Count==2 && samples[1].Time==999,"tail does not retain unbounded stale history");
        });
        return passed;
        void Test(string name,Action action) {action();passed++;Console.WriteLine("PASS "+name);}
    }
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    private static void Near(float value,float expected) {if(Math.Abs(value-expected)>.00011f)throw new Exception($"{value} != {expected}");}
}
