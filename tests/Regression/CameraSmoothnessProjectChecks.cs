using System.Text.Json;
using ReplayMod.Director;

public static class CameraSmoothnessProjectChecks
{
    public static int Run()
    {
        var options=new JsonSerializerOptions {IncludeFields=true};
        var old=JsonSerializer.Deserialize<DirectorProject>("{\"version\":1,\"smoothCameraPath\":true}",options);
        Require(old.Validate(20)&&Math.Abs(old.cameraPathSmoothness-.65f)<.000001,"older project defaults");
        foreach(float amount in new[]{0,.15f,.65f,1})
        {
            var project=new DirectorProject {cameraPathSmoothness=amount};
            var restored=JsonSerializer.Deserialize<DirectorProject>(JsonSerializer.Serialize(project,options),options);
            Require(restored.Validate(20)&&restored.cameraPathSmoothness==amount,"saved strength retained");
        }
        Console.WriteLine("PASS camera smoothness project old sidecar defaults and strength roundtrip");
        foreach(float amount in new[]{-.001f,1.001f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Require(!new DirectorProject {cameraPathSmoothness=amount}.Validate(20),"invalid strength rejected");
        Console.WriteLine("PASS camera smoothness project malformed strengths fail validation");
        return 2;
    }
    private static void Require(bool good,string reason){if(!good)throw new Exception(reason);}
}
