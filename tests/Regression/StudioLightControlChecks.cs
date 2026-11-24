using ReplayMod.UI;

public static class StudioLightControlChecks
{
    public static int Run()
    {
        int checks=0;
        Test("log controls reach exact practical endpoints",()=>
        {
            foreach(var range in new[]{(0f,1000f),(.1f,5000f),(.1f,1000f)})
            {
                Check(StudioLightControlMath.ToSlider(range.Item1,range.Item1,range.Item2)==0,"minimum position");
                Check(StudioLightControlMath.ToSlider(range.Item2,range.Item1,range.Item2)==1,"maximum position");
                Near(StudioLightControlMath.FromSlider(0,range.Item1,range.Item2),range.Item1,.00001f);
                Near(StudioLightControlMath.FromSlider(1,range.Item1,range.Item2),range.Item2,.001f);
            }
        });
        Test("log controls remain monotonic and retain fine low-light adjustment",()=>
        {
            foreach(var range in new[]{(0f,1000f),(.1f,5000f)})
            {
                float previous=range.Item1;
                for(int i=1;i<=1000;i++)
                {float next=StudioLightControlMath.FromSlider(i/1000f,range.Item1,range.Item2);Check(next>previous,"slider reverses or stalls");previous=next;}
            }
            Check(StudioLightControlMath.FromSlider(.1f,0,1000)<1,"small brightness adjustments remain available");
        });
        Test("log controls round trip usable shot values",()=>
        {
            foreach(float brightness in new[]{0,.001f,.1f,1,3,10,50,100,500,1000})
                Near(StudioLightControlMath.FromSlider(StudioLightControlMath.ToSlider(brightness,0,1000),0,1000),brightness,Math.Max(.00001f,brightness*.00001f));
            foreach(float reach in new[]{.1f,.5f,1,10,100,1000,5000})
                Near(StudioLightControlMath.FromSlider(StudioLightControlMath.ToSlider(reach,.1f,5000),.1f,5000),reach,Math.Max(.00001f,reach*.00001f));
        });
        Test("invalid slider input cannot introduce nonfinite light settings",()=>
        {
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {Check(StudioLightControlMath.ToSlider(invalid,0,1000)==0,"bad value fallback");Check(StudioLightControlMath.FromSlider(invalid,0,1000)==0,"bad slider fallback");}
            Check(StudioLightControlMath.FromSlider(-1,0,1000)==0,"lower clamp");
            Near(StudioLightControlMath.FromSlider(2,0,1000),1000,.001f);
            foreach(var range in new[]{(1f,1f),(1f,0f),(float.NaN,1f),(0f,float.PositiveInfinity)})
            {Reject(()=>StudioLightControlMath.ToSlider(0,range.Item1,range.Item2));Reject(()=>StudioLightControlMath.FromSlider(0,range.Item1,range.Item2));}
        });
        Test("beam width represents the outer cone rather than an area emitter",()=>
        {
            Near(StudioLightControlMath.BeamWidth(100,90),200,.001f);
            Near(StudioLightControlMath.BeamWidth(100,60),115.47005f,.001f);
            Near(StudioLightControlMath.BeamWidth(200,60),2*StudioLightControlMath.BeamWidth(100,60),.001f);
            Check(StudioLightControlMath.BeamWidth(0,45)==0,"zero range");
            foreach(float invalid in new[]{-1,180,float.NaN,float.PositiveInfinity})Check(StudioLightControlMath.BeamWidth(100,invalid)==0,"invalid angle");
        });
        Console.WriteLine($"Studio light control audit: {checks} groups, 0 failures");return checks;
        void Test(string title,Action action){action();checks++;Console.WriteLine("PASS studio light control "+title);}
    }
    private static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    private static void Near(float actual,float expected,float tolerance)=>Check(Math.Abs(actual-expected)<=tolerance,$"{actual} != {expected}");
    private static void Reject(Action action){try{action();}catch(ArgumentOutOfRangeException){return;}throw new Exception("invalid range accepted");}
}
