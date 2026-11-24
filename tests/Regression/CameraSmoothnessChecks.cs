using ReplayMod.Director;
using UnityEngine;

internal static class CameraSmoothnessChecks
{
    public static int Run()
    {
        int passed=0;
        Check("classic and straight camera paths retain their existing behavior",()=>
        {
            var keys=Path();
            foreach(bool smooth in new[]{false,true})foreach(double time in new[]{0,1.2,2,4,7,9.5,10})
            {
                if(!CameraPathSampler.TrySample(keys,time,smooth,Resolve,out var legacy))throw new Exception("legacy sample failed");
                var zero=Sample(keys,time,smooth,0);Same(legacy,zero);
                if(!smooth)Same(legacy,Sample(keys,time,false,1));
            }
            Same(Sample(keys,4,true,-2),Sample(keys,4,true,0));Same(Sample(keys,4,true,2),Sample(keys,4,true,1));
        });
        Check("smoothness preserves exact captured times and poses",()=>
        {
            var keys=Path();
            foreach(float strength in new[]{0,.25f,.65f,1})foreach(var key in keys)
            {
                var sample=Sample(keys,key.time,true,strength);Near(sample.position.x,key.x);Near(sample.position.z,key.z);
                Near(sample.fov,key.fov);Near(Angle(sample.rotation),Angle(Rotation(key)),.0001);
            }
            if(keys[1].time!=2||keys[2].time!=7)throw new Exception("smoothing retimed a key");
        });
        Check("full smoothing removes position acceleration jumps on uneven segments",()=>
        {
            var keys=Path();const double h=.005;
            foreach(double time in new[]{2d,7d})
            {
                var center=Sample(keys,time,true,1).position;
                var before=Sample(keys,time-h,true,1).position;var after=Sample(keys,time+h,true,1).position;
                var before2=Sample(keys,time-2*h,true,1).position;var after2=Sample(keys,time+2*h,true,1).position;
                Near((center.x-before.x)/h,(after.x-center.x)/h,.015);Near((center.z-before.z)/h,(after.z-center.z)/h,.015);
                Near((center.x-2*before.x+before2.x)/(h*h),(after2.x-2*after.x+center.x)/(h*h),.4);
                Near((center.z-2*before.z+before2.z)/(h*h),(after2.z-2*after.z+center.z)/(h*h),.4);
                double classic=AccelerationJump(keys,time,0,h),full=AccelerationJump(keys,time,1,h);
                if(full>=classic*.15)throw new Exception($"acceleration jump was not reduced: {classic} -> {full}");
            }
        });
        Check("full smoothing gives the viewing angle a continuous velocity and acceleration",()=>
        {
            var keys=Path();const double h=.005;
            foreach(double time in new[]{2d,7d})
            {
                double a2=Angle(Sample(keys,time-2*h,true,1).rotation),a=Angle(Sample(keys,time-h,true,1).rotation),
                    b=Angle(Sample(keys,time,true,1).rotation),c=Angle(Sample(keys,time+h,true,1).rotation),c2=Angle(Sample(keys,time+2*h,true,1).rotation);
                Near((b-a)/h,(c-b)/h,.05);Near((b-2*a+a2)/(h*h),(c2-2*c+b)/(h*h),.8);
                double classic=AngularVelocityJump(keys,time,0,.005),full=AngularVelocityJump(keys,time,1,.005);
                if(full>=classic*.03)throw new Exception($"angle speed jump was not reduced: {classic} -> {full}");
            }
        });
        Check("smooth zoom stays bounded and continuous through camera points",()=>
        {
            var keys=new[]{Key(0,0,0,0,1),Key(2,10,0,20,50),Key(7,10,20,80,120),Key(10,30,20,100,20)};
            foreach(float strength in new[]{.25f,.65f,1})for(int segment=0;segment<keys.Length-1;segment++)
            {
                var a=keys[segment];var b=keys[segment+1];double previous=a.fov;
                for(int i=0;i<=100;i++)
                {
                    double fov=Sample(keys,a.time+(b.time-a.time)*i/100,true,strength).fov;
                    if(fov<Math.Min(a.fov,b.fov)-.0001||fov>Math.Max(a.fov,b.fov)+.0001)throw new Exception("zoom overshot its saved lens values");
                    if((b.fov>a.fov&&fov<previous-.0001)||(b.fov<a.fov&&fov>previous+.0001))throw new Exception("zoom reversed between monotone keys");previous=fov;
                }
            }
            const double h=.005;
            foreach(double time in new[]{2d,7d})
            {
                double a2=Sample(keys,time-2*h,true,1).fov,a=Sample(keys,time-h,true,1).fov,b=Sample(keys,time,true,1).fov,
                    c=Sample(keys,time+h,true,1).fov,c2=Sample(keys,time+2*h,true,1).fov;
                Near((b-a)/h,(c-b)/h,.08);Near((b-2*a+a2)/(h*h),(c2-2*c+b)/(h*h),1.5);
            }
        });
        Check("two-point moves ease smoothly into and out of held endpoints",()=>
        {
            var keys=new[]{Key(6,0,0,0,30),Key(9,30,0,90,90)};const double h=.01;
            var middle=Sample(keys,7.5,true,1);Near(middle.position.x,15);Near(middle.fov,60);Near(Angle(middle.rotation),45,.001);
            foreach(double endpoint in new[]{6d,9d})
            {
                var center=Sample(keys,endpoint,true,1);var left=Sample(keys,endpoint-h,true,1);var right=Sample(keys,endpoint+h,true,1);
                Near((right.position.x-left.position.x)/(2*h),0,.001);Near((right.fov-left.fov)/(2*h),0,.003);
                Near((right.position.x-2*center.position.x+left.position.x)/(h*h),0,.12);
                Near(Delta(Angle(right.rotation),Angle(left.rotation))/(2*h),0,.003);
            }
            Same(Sample(keys,-100,true,1),Sample(keys,6,true,1));Same(Sample(keys,100,true,1),Sample(keys,9,true,1));
        });
        Check("cinematic paths keep moving through interior turns",()=>
        {
            var keys=Path();const double h=.005;
            foreach(double time in new[]{2d,7d})
            {
                var a=Sample(keys,time-h,true,1).position;var b=Sample(keys,time+h,true,1).position;
                if(Distance(a,b)/(2*h)<1)throw new Exception("camera stopped at an ordinary path point");
            }
            double classic=AccelerationJump(keys,2,0,.01),partial=AccelerationJump(keys,2,.65f,.01),full=AccelerationJump(keys,2,1,.01);
            if(!(classic>partial&&partial>full))throw new Exception("smoothness slider did not progressively soften a corner");
        });
        Check("seeking and replay speed do not change a smooth path sample",()=>
        {
            var keys=Path();var expected=Sample(keys,4.5,true,.65f);
            foreach(double speed in new[]{.1,.25,.5,1,2})
            {
                Sample(keys,9,true,.65f);Sample(keys,.1,true,.65f);double realElapsed=2.5/speed;
                Same(expected,Sample(keys,2+realElapsed*speed,true,.65f));
            }
            foreach(var key in keys)if(key.easing!=0)throw new Exception("sampling mutated saved points");
        });
        Check("attached curves stay in the selected subject's moving reference frame",()=>
        {
            var keys=Path();foreach(var key in keys){key.relative=true;key.target=42;}
            var local=Sample(keys,4.5,true,1);double heading=73,originX=100,originZ=-200;
            bool Attached(CameraKey key,out Vector3 p,out Quaternion q)
            {
                double radians=heading*Math.PI/180,c=Math.Cos(radians),s=Math.Sin(radians);
                p=new Vector3((float)(originX+c*key.x+s*key.z),key.y,(float)(originZ-s*key.x+c*key.z));q=Yaw(heading+Angle(Rotation(key)));return true;
            }
            if(!CameraPathSampler.TrySample(keys,4.5,true,1,Attached,out var world))throw new Exception("attached sample failed");
            double r=heading*Math.PI/180;Near(world.position.x,originX+Math.Cos(r)*local.position.x+Math.Sin(r)*local.position.z,.0001);
            Near(world.position.z,originZ-Math.Sin(r)*local.position.x+Math.Cos(r)*local.position.z,.0001);
            Near(Delta(Angle(world.rotation),heading+Angle(local.rotation)),0,.001);Near(world.fov,local.fov);
        });
        Check("quaternion sign changes cannot cause long spins or invalid orientations",()=>
        {
            var keys=new[]{Key(0,0,0,160),Key(2,10,0,175),Key(7,10,20,195),Key(10,30,20,210)};
            var before=Enumerable.Range(0,201).Select(i=>Sample(keys,i*.05,true,1).rotation).ToArray();
            foreach(int i in new[]{0,2,3}){keys[i].qy=-keys[i].qy;keys[i].qw=-keys[i].qw;}
            for(int i=0;i<before.Length;i++)
            {
                var after=Sample(keys,i*.05,true,1).rotation;Near(Math.Abs(Dot(before[i],after)),1,.00001);
                Near(Dot(after,after),1,.00001);if(i>0&&Math.Abs(Delta(Angle(after),Angle(before[i-1])))>3)throw new Exception("short-arc path spun unexpectedly");
            }
            var close=new[]{Key(0,0,0,179),Key(1,1,0,-179)};Near(Math.Abs(Angle(Sample(close,.5,true,1).rotation)),180,.001);
        });
        Check("stationary and very uneven paths remain finite without large excursions",()=>
        {
            var stationary=new[]{Key(0,10,10,25,60),Key(2,10,10,25,60),Key(9,10,10,25,60)};
            for(int i=0;i<=100;i++){var value=Sample(stationary,i*.09,true,1);Near(value.position.x,10);Near(value.position.z,10);Near(Angle(value.rotation),25,.001);Near(value.fov,60);}
            var uneven=new[]{Key(0,0,0,0),Key(.01,10,0,30),Key(1000,10.001f,.001f,30.01)};
            for(int i=0;i<=1000;i++)
            {
                var value=Sample(uneven,.01+(1000-.01)*i/1000,true,1);
                if(value.position.x<9.999||value.position.x>10.003||Math.Abs(value.position.z)>.003)throw new Exception("a short segment launched the camera far outside the long segment");
            }
            if(CameraPathSampler.TrySample(stationary,1,true,float.NaN,Resolve,out _)||CameraPathSampler.TrySample(stationary,double.PositiveInfinity,true,1,Resolve,out _))throw new Exception("nonfinite controls accepted");
            bool Broken(CameraKey key,out Vector3 p,out Quaternion q){Resolve(key,out p,out q);q=default;return true;}
            if(CameraPathSampler.TrySample(stationary,1,true,1,Broken,out _))throw new Exception("zero quaternion accepted");
        });
        Check("sampling only resolves the active segment and its immediate neighbors",()=>
        {
            var keys=Enumerable.Range(0,2000).Select(i=>Key(i,i,0,i%90)).ToArray();var resolved=new List<double>();
            bool Count(CameraKey key,out Vector3 p,out Quaternion q){resolved.Add(key.time);return Resolve(key,out p,out q);}
            if(!CameraPathSampler.TrySample(keys,1000.5,true,1,Count,out _))throw new Exception("large path failed");
            if(resolved.Count!=4||resolved.Distinct().Count()!=4||resolved.Any(time=>time<999||time>1002))throw new Exception("smoothing scanned or repeatedly resolved the path");
        });
        return passed;

        void Check(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){throw new Exception(name+": "+error.Message,error);}}
    }

    private static CameraKey[] Path()=>new[]{Key(0,0,0,0,30),Key(2,10,0,20,50),Key(7,10,20,80,90),Key(10,30,20,100,60)};
    private static CameraKey Key(double time,float x,float z,double yaw=0,float fov=60)
    {var q=Yaw(yaw);return new CameraKey {time=time,x=x,z=z,qx=q.x,qy=q.y,qz=q.z,qw=q.w,fov=fov,easing=0};}
    private static Quaternion Yaw(double degrees)=>new Quaternion {y=(float)Math.Sin(degrees*Math.PI/360),w=(float)Math.Cos(degrees*Math.PI/360)};
    private static Quaternion Rotation(CameraKey key)=>new Quaternion {x=key.qx,y=key.qy,z=key.qz,w=key.qw};
    private static bool Resolve(CameraKey key,out Vector3 p,out Quaternion q){p=new Vector3(key.x,key.y,key.z);q=Rotation(key);return true;}
    private static CameraPathSample Sample(CameraKey[] keys,double time,bool smooth,float strength)
    {if(!CameraPathSampler.TrySample(keys,time,smooth,strength,Resolve,out var result))throw new Exception("path sample failed");return result;}
    private static double AccelerationJump(CameraKey[] keys,double time,float strength,double h)
    {
        var a2=Sample(keys,time-2*h,true,strength).position;var a=Sample(keys,time-h,true,strength).position;var b=Sample(keys,time,true,strength).position;
        var c=Sample(keys,time+h,true,strength).position;var c2=Sample(keys,time+2*h,true,strength).position;
        double x=(b.x-2*a.x+a2.x-c2.x+2*c.x-b.x)/(h*h),z=(b.z-2*a.z+a2.z-c2.z+2*c.z-b.z)/(h*h);return Math.Sqrt(x*x+z*z);
    }
    private static double AngularVelocityJump(CameraKey[] keys,double time,float strength,double h)
    {double a=Angle(Sample(keys,time-h,true,strength).rotation),b=Angle(Sample(keys,time,true,strength).rotation),c=Angle(Sample(keys,time+h,true,strength).rotation);return Math.Abs(Delta(b,a)/h-Delta(c,b)/h);}
    private static double Angle(Quaternion q)=>2*Math.Atan2(q.y,q.w)*180/Math.PI;
    private static double Delta(double a,double b){double d=(a-b)%360;return d>180?d-360:d< -180?d+360:d;}
    private static double Dot(Quaternion a,Quaternion b)=>(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z+(double)a.w*b.w;
    private static double Distance(Vector3 a,Vector3 b)=>Math.Sqrt(Math.Pow(a.x-b.x,2)+Math.Pow(a.y-b.y,2)+Math.Pow(a.z-b.z,2));
    private static void Same(CameraPathSample a,CameraPathSample b){Near(a.position.x,b.position.x);Near(a.position.y,b.position.y);Near(a.position.z,b.position.z);Near(a.fov,b.fov);Near(Math.Abs(Dot(a.rotation,b.rotation)),1,.00001);}
    private static void Near(double actual,double expected,double tolerance=.0001)
    {if(!double.IsFinite(actual)||!double.IsFinite(expected)||Math.Abs(actual-expected)>tolerance)throw new Exception($"{actual} != {expected} (tolerance {tolerance})");}
}
