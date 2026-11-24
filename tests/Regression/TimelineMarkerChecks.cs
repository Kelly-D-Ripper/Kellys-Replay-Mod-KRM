using ReplayMod.UI;

public static class TimelineMarkerChecks
{
    public static int Run()
    {
        int passed=0;
        Test("large marker lists cluster to at most 256 visible ticks",()=>
        {
            var marks=Enumerable.Range(0,100000).Select(n=>new TimelineMark(n,false));
            var ticks=TimelineMarkers.Cluster(marks,100000,10000);
            Check(ticks.Length<=256&&ticks.Sum(t=>t.Count)==100000,"bounded ticks retain cluster counts");
            Check(ticks.Zip(ticks.Skip(1),(a,b)=>a.Time<b.Time).All(v=>v),"tick representatives remain sorted");
        });
        Test("manual bookmarks win a shared cluster and invalid times stay absent",()=>
        {
            var ticks=TimelineMarkers.Cluster(new[]{new TimelineMark(10,false),new TimelineMark(12,true),new TimelineMark(double.NaN,true),new TimelineMark(101,true),new TimelineMark(-1,false)},100,1);
            Check(ticks.Length==1&&ticks[0].Manual&&ticks[0].Time==12&&ticks[0].Count==2,"manual bookmark retained");
            Check(TimelineMarkers.Cluster(new[]{new TimelineMark(0,true)},0).Length==0,"empty duration has no ticks");
        });
        Test("optional snapping uses screen pixel distance and leaves arbitrary times exact",()=>
        {
            var ticks=TimelineMarkers.Cluster(new[]{new TimelineMark(100,false),new TimelineMark(500,true)},1000);
            Check(TimelineMarkers.Snap(ticks,104,1000,1000)==100,"near event snaps");
            Check(TimelineMarkers.Snap(ticks,110,1000,1000)==110,"far position remains exact");
            Check(TimelineMarkers.Snap(ticks,110,1000,500)==100,"same six-pixel tolerance at smaller screen size");
            Check(TimelineMarkers.Snap(ticks,0,1000,1000)==0&&TimelineMarkers.Snap(ticks,1000,1000,1000)==1000,"endpoints stay accessible");
            var nearEnds=TimelineMarkers.Cluster(new[]{new TimelineMark(2,false),new TimelineMark(998,true)},1000);
            Check(TimelineMarkers.Snap(nearEnds,0,1000,1000)==0&&TimelineMarkers.Snap(nearEnds,1000,1000,1000)==1000,"nearby endpoint markers never pull the first or final frame away");
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS timeline markers "+name);}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
    }
}
