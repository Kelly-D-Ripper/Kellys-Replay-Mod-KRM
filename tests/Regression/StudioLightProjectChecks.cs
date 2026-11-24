using System.Text.Json;
using ReplayMod.Director;

// Sidecar and procedural-mask checks run the production data/model code.
// This does not stand in for Unity JsonUtility or native rendering acceptance.
public static class StudioLightProjectChecks
{
    public static int Run()
    {
        int checks=0;
        Test("old projects default to an empty light collection",()=>
        {
            var options=new JsonSerializerOptions {IncludeFields=true};
            var old=JsonSerializer.Deserialize<DirectorProject>("{\"version\":1,\"replayHash\":\"old\"}",options);
            Check(old.Validate(100)&&old.studioLights.Count==0,"old sidecar defaults");
            var fresh=new StudioLightSettings();Check(fresh.Valid(),"new light");
            Check(new DirectorProject().Validate(0),"empty replay");
        });
        Test("project limits and identities prevent ambiguous ownership",()=>
        {
            var project=new DirectorProject();
            for(int i=0;i<16;i++)project.studioLights.Add(new StudioLightSettings());
            Check(project.Validate(100),"sixteen lights");
            project.studioLights.Add(new StudioLightSettings());Check(!project.Validate(100),"excess rejected");
            project.studioLights.RemoveAt(16);
            project.studioLights[1].id=project.studioLights[0].id;Check(!project.Validate(100),"duplicate identity rejected");
            project.studioLights[1]=null;Check(!project.Validate(100),"null entry rejected");
            project.studioLights=null;Check(!project.Validate(100),"null list rejected");
        });
        Test("duplicate retains a shot setup with independent identity",()=>
        {
            var source=new StudioLightSettings {name=new string('A',80),type=1,x=123456.125,y=-15.25,z=98765.5,beamProfile=3,beamWidth=.7f,beamHeight=.4f,r=.2f,g=.8f,b=.6f,useTemperature=true,temperature=3200,shadowMode=2};
            var copy=source.Duplicate();Check(copy.Valid()&&source.Valid(),"bounded names");
            Check(copy.id!=source.id&&copy.name.EndsWith(" copy")&&copy.name.Length<=80,"unique bounded clone");
            Check(copy.x==source.x&&copy.beamProfile==3&&copy.useTemperature&&copy.temperature==3200&&copy.shadowMode==2,"settings retained");
            copy.x++;copy.name="New shot";Check(source.x==123456.125&&source.name.Length==80,"independent edits");
        });
        Test("malformed hand-edited light records are rejected",()=>
        {
            Action<StudioLightSettings>[] corrupt={l=>l.id=null,l=>l.id=new string('g',32),l=>l.id=new string('A',32),l=>l.name="bad\nname",l=>l.name=new string('x',81),l=>l.x=double.NaN,l=>l.z=double.PositiveInfinity,l=>l.x=1e8+1,l=>l.qw=0,l=>l.qx=float.NaN,l=>l.r=-.1f,l=>l.g=float.PositiveInfinity,l=>l.b=1.1f,l=>l.intensity=1001,l=>l.range=0,l=>l.type=2,l=>l.spotOuter=180,l=>l.spotInner=46,l=>l.beamProfile=5,l=>l.beamWidth=0,l=>l.beamHeight=float.NaN,l=>l.beamSoftness=1.1f,l=>l.temperature=999,l=>l.shadowMode=3,l=>l.shadowStrength=-1,l=>l.shadowNearPlane=11};
            foreach(var mutation in corrupt){var project=new DirectorProject();var light=new StudioLightSettings();mutation(light);project.studioLights.Add(light);Check(!light.Valid()&&!project.Validate(100),"invalid settings escaped project validation");}
        });
        Test("sidecar fields retain precise poses and independent light settings",()=>
        {
            var options=new JsonSerializerOptions {IncludeFields=true};
            var project=new DirectorProject();
            project.studioLights.Add(new StudioLightSettings {name="Warm window",type=1,x=99999999.123456,y=-12345678.125,z=.000000125,qz=.6f,qw=.8f,r=.75f,g=.5f,b=.25f,intensity=7.5f,range=345,spotOuter=75,spotInner=31,beamProfile=3,beamWidth=.45f,beamHeight=.8f,beamSoftness=.55f,useTemperature=true,temperature=4200,shadowMode=1,shadowStrength=.8f,shadowNearPlane=.15f});
            var copy=JsonSerializer.Deserialize<DirectorProject>(JsonSerializer.Serialize(project,options),options);
            var a=project.studioLights[0];var b=copy.studioLights[0];
            Check(copy.Validate(100)&&a.x==b.x&&a.y==b.y&&a.z==b.z,"double global position retained");
            Check(JsonSerializer.Serialize(a,options)==JsonSerializer.Serialize(b,options),"all light fields retained");
            b.enabled=false;Check(a.enabled,"independent deserialized collection");
        });
        Test("beam width height and soft edges produce distinct shapes",()=>
        {
            float S(int profile,float u,float v,float soft=0)=>StudioLightBeamProfile.Sample(profile,1,1,soft,u,v);
            Check(S(1,.9f,.9f)==0&&S(2,.9f,.9f)==1,"ellipse and rectangle corners");
            Check(StudioLightBeamProfile.Sample(2,.4f,.8f,0,.75f,.5f)==0&&StudioLightBeamProfile.Sample(2,.4f,.8f,0,.5f,.75f)==1,"independent projected dimensions");
            float feather=S(2,.95f,.5f,.5f);Check(feather>0&&feather<1&&S(2,.95f,.5f)==1,"soft boundary");
            foreach(int profile in new[]{1,2})for(int i=1;i<20;i++)for(int j=1;j<20;j++)
            {float u=i/20f,v=j/20f;Near(S(profile,u,v,.3f),S(profile,1-u,v,.3f),.00001f);Near(S(profile,u,v,.3f),S(profile,v,u,.3f),.00001f);}
        });
        Test("window and slat masks contain dark dividers and open panes",()=>
        {
            Check(StudioLightBeamProfile.Sample(3,1,1,.2f,.5f,.75f)==0,"vertical window divider");
            Check(StudioLightBeamProfile.Sample(3,1,1,.2f,.75f,.5f)==0,"horizontal window divider");
            Check(StudioLightBeamProfile.Sample(3,1,1,.2f,.75f,.75f)==1,"window pane");
            Check(StudioLightBeamProfile.Sample(4,1,1,0,.5f,.5f)==0,"slat divider");
            Check(StudioLightBeamProfile.Sample(4,1,1,0,.5f,1f/12f)> .99f,"open slat");
        });
        Test("raster masks are deterministic bounded grayscale with opaque borders",()=>
        {
            const int size=128;var rgba=new byte[size*size*4];var duplicate=new byte[rgba.Length];
            for(int profile=0;profile<=4;profile++)
            {
                Check(StudioLightBeamProfile.Rasterize(profile,.65f,.9f,.3f,size,rgba),"raster succeeds");
                Check(StudioLightBeamProfile.Rasterize(profile,.65f,.9f,.3f,size,duplicate)&&rgba.SequenceEqual(duplicate),"deterministic");
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {int o=(y*size+x)*4;Check(rgba[o]==rgba[o+1]&&rgba[o]==rgba[o+2]&&rgba[o+3]==255,"opaque grayscale");if(x==0||y==0||x==size-1||y==size-1)Check(rgba[o]==(profile==0?255:0),"projection border");}
            }
            foreach(int boundary in new[]{4,512})Check(StudioLightBeamProfile.Rasterize(2,1,1,0,boundary,new byte[boundary*boundary*4]),"supported size limit");
        });
        Test("invalid mask requests preserve the upload buffer",()=>
        {
            var rgba=Enumerable.Repeat((byte)57,64).ToArray();
            foreach(var request in new[]{(-1,1f,1f,0f,4),(1,float.NaN,1f,0f,4),(1,1f,1f,2f,4),(1,1f,1f,0f,3),(1,1f,1f,0f,513),(1,1f,1f,0f,5)})
                Check(!StudioLightBeamProfile.Rasterize(request.Item1,request.Item2,request.Item3,request.Item4,request.Item5,rgba)&&rgba.All(b=>b==57),"rejected before mutation");
            Check(!StudioLightBeamProfile.Rasterize(1,1,1,0,4,null),"null buffer");
            foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-.1f,1.1f})Check(StudioLightBeamProfile.Sample(1,1,1,0,invalid,.5f)==0,"invalid UV");
        });
        Test("native cone mask leaves distance falloff and angular cone to Unity",()=>
        {
            for(int i=0;i<=10;i++)for(int j=0;j<=10;j++)Check(StudioLightBeamProfile.Sample(0,.1f,.1f,1,i/10f,j/10f)==1,"native profile white");
        });
        Console.WriteLine($"Studio light project/mask audit: {checks} groups, 0 failures");return checks;
        void Test(string title,Action action){action();checks++;Console.WriteLine("PASS studio light project "+title);}
    }
    private static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    private static void Near(float actual,float expected,float tolerance)=>Check(Math.Abs(actual-expected)<=tolerance,$"{actual} != {expected}");
}
