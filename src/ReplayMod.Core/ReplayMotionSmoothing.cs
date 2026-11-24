using System;
using System.Collections.Generic;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Core;

// Presentation only: evaluate a finite recording-time neighborhood, never the
// previous rendered frame. Pausing, slow motion and repeated seeks therefore
// sample the same curve. Source samples and serialized replay data stay intact.
public static class ReplayMotionSmoothing
{
    public const double MaxRadiusSeconds = 0.35;
    public const double RequiredLookAheadSeconds = MaxRadiusSeconds + .25;
    public const double HistorySeconds = 0.75;
    public const int MaxWindowSamples = 128;
    public static double Radius(int preset) => preset == 1 ? .22 : preset == 2 ? .28 : preset == 3 ? MaxRadiusSeconds : 0;
    public static bool Enabled(int preset) => preset >= 1 && preset <= 3;
    public static bool LookAheadReady(double target,double producedTime,bool finished)=>finished||producedTime>target+RequiredLookAheadSeconds;

    private struct Polynomial
    {
        public double a,b,c;
        public void Add(double value,double weight,double u) {a+=value*weight;b+=value*weight*u;c+=value*weight*u*u;}
        public double Intercept(double k0,double k1,double k2)=>a*k0+b*k1+c*k2;
    }

    public static bool TrySample(List<PositionSnapshot> samples,double time,int preset,double birth,double nextBarrier,out PositionSnapshot result)
    {
        result=default;
        double radius=Radius(preset);
        if(radius<=0||samples==null||!SnapshotHistory.Locate(samples,time,p=>p.time,out int left,out int right,out _))return false;
        result=ReplayPoseInterpolation.At(samples[left],samples[right],time);
        if(time-radius<birth || time+radius>=nextBarrier || left==right)return false;
        // Require complete temporal support. EOF / missing future samples use the
        // exact existing interpolation; no invented prediction or wall-time lag.
        double start=time-radius,end=time+radius;
        if(!SnapshotHistory.Locate(samples,start,p=>p.time,out int first,out _,out _)||
           !SnapshotHistory.Locate(samples,end,p=>p.time,out int endLeft,out int endRight,out _))return false;
        int last=samples[endLeft].time>=end?endLeft:endRight;
        if(samples[first].time>start||samples[last].time<end||last-first+1>MaxWindowSamples)return false;
        double s0=0,s1=0,s2=0,s3=0,s4=0,squaredWeights=0;
        Polynomial px=default,py=default,pz=default,vx=default,vy=default,vz=default,qx=default,qy=default,qz=default,qw=default;
        Quaternion reference=result.rotation;
        double refNorm=Norm(reference);
        if(refNorm<.5||!Finite(refNorm))return false;
        int weighted=0;
        for(int i=first;i<=last;i++)
        {
            var sample=samples[i];
            if(!Valid(sample)||(i>first&&!Continuous(samples[i-1],sample)))return false;
            double u=(sample.time-time)/radius;
            if(Math.Abs(u)>=1)continue;
            double weight=1-u*u;weight*=weight;double u2=u*u;
            s0+=weight;s1+=weight*u;s2+=weight*u2;s3+=weight*u2*u;s4+=weight*u2*u2;squaredWeights+=weight*weight;weighted++;
            // Fit offsets around the raw pose; floating-origin shifts never enter
            // the filter and large map coordinates do not poison the fit sums.
            px.Add(sample.position.x-result.position.x,weight,u);py.Add(sample.position.y-result.position.y,weight,u);pz.Add(sample.position.z-result.position.z,weight,u);
            vx.Add(sample.velocity.x,weight,u);vy.Add(sample.velocity.y,weight,u);vz.Add(sample.velocity.z,weight,u);
            double sign=Dot(sample.rotation,reference)<0?-1:1;
            double norm=Norm(sample.rotation);sign/=norm;
            qx.Add(sample.rotation.x*sign,weight,u);qy.Add(sample.rotation.y*sign,weight,u);qz.Add(sample.rotation.z*sign,weight,u);qw.Add(sample.rotation.w*sign,weight,u);
        }
        // First row of the inverse moment matrix, avoiding per-frame matrices.
        double c0=s2*s4-s3*s3,c1=s2*s3-s1*s4,c2=s1*s3-s2*s2;
        double determinant=s0*c0+s1*c1+s2*c2;
        if(weighted<3||determinant<=1e-10)return false;
        c0/=determinant;c1/=determinant;c2/=determinant;
        double dx=px.Intercept(c0,c1,c2),dy=py.Intercept(c0,c1,c2),dz=pz.Intercept(c0,c1,c2);
        double correction=Math.Sqrt(dx*dx+dy*dy+dz*dz),limit=preset==1?2:preset==2?5:10;
        if(!Finite(correction))return false;
        double strength=preset==1?.35:preset==2?.65:.90;
        // A kernel node enters/exits with zero weight. Fade by effective support
        // instead of an integer sample-count threshold, which otherwise toggles
        // filtering as low-frame-rate recordings alternate between 4 and 5 nodes.
        double effectiveSamples=s0*s0/squaredWeights;
        double support=Math.Min(1,Math.Max(0,(effectiveSamples-2.25)/1.5));
        strength*=support*support*(3-2*support);
        // Ease the correction to zero before a known mutation and after birth;
        // never introduce a visible smoothing switch exactly at the boundary.
        strength*=Math.Min(1,Math.Max(0,(time-birth-radius)/radius));
        if(!double.IsPositiveInfinity(nextBarrier))strength*=Math.Min(1,Math.Max(0,(nextBarrier-time-radius)/radius));
        double positionWeight=strength*(correction>limit?limit/correction:1);
        var rotation=new Quaternion {x=(float)qx.Intercept(c0,c1,c2),y=(float)qy.Intercept(c0,c1,c2),z=(float)qz.Intercept(c0,c1,c2),w=(float)qw.Intercept(c0,c1,c2)};
        double rotationNorm=Norm(rotation);if(rotationNorm<.5||!Finite(rotationNorm))return false;
        rotation.x/=(float)rotationNorm;rotation.y/=(float)rotationNorm;rotation.z/=(float)rotationNorm;rotation.w/=(float)rotationNorm;
        double angle=2*Math.Acos(Math.Min(1,Math.Abs(Dot(rotation,reference)/refNorm)))*180/Math.PI;
        double angleLimit=preset==1?1.5:preset==2?3:6;
        double rotationWeight=strength*(angle>angleLimit?angleLimit/angle:1);
        result.position=new GlobalPosition((float)(result.position.x+dx*positionWeight),(float)(result.position.y+dy*positionWeight),(float)(result.position.z+dz*positionWeight));
        result.rotation=Quaternion.Slerp(reference,rotation,(float)rotationWeight);
        result.velocity=Vector3.Lerp(result.velocity,new Vector3((float)vx.Intercept(c0,c1,c2),(float)vy.Intercept(c0,c1,c2),(float)vz.Intercept(c0,c1,c2)),(float)strength);
        return true;
    }

    public static bool Continuous(PositionSnapshot a,PositionSnapshot b)
    {
        double dt=b.time-a.time;if(dt<=0||dt>.25)return false;
        double speedA=Length(a.velocity.x,a.velocity.y,a.velocity.z),speedB=Length(b.velocity.x,b.velocity.y,b.velocity.z);
        double distance=Length(b.position.x-a.position.x,b.position.y-a.position.y,b.position.z-a.position.z);
        if(distance>Math.Max(12,(speedA+speedB)*dt+5))return false;
        // High acceleration/turn-rate and impact steps retain recorded motion.
        if(Length(b.velocity.x-a.velocity.x,b.velocity.y-a.velocity.y,b.velocity.z-a.velocity.z)>Math.Max(15,180*dt))return false;
        double angle=2*Math.Acos(Math.Min(1,Math.Abs(Dot(a.rotation,b.rotation)/(Norm(a.rotation)*Norm(b.rotation)))))*180/Math.PI;
        return Finite(angle)&&angle<=Math.Max(12,240*dt);
    }
    public static void TrimHistory(List<PositionSnapshot> samples,double time)
    {
        if(samples==null||samples.Count<3)return;
        int first=0;double cutoff=time-HistorySeconds;
        while(first+1<samples.Count&&samples[first+1].time<cutoff)first++;
        if(first>0)samples.RemoveRange(0,first);
    }
    private static bool Valid(PositionSnapshot s)=>Finite(s.time)&&Finite(s.position.x)&&Finite(s.position.y)&&Finite(s.position.z)&&Finite(s.velocity.x)&&Finite(s.velocity.y)&&Finite(s.velocity.z)&&Finite(s.rotation.x)&&Finite(s.rotation.y)&&Finite(s.rotation.z)&&Finite(s.rotation.w)&&Norm(s.rotation)>.5;
    private static double Length(double x,double y,double z)=>Math.Sqrt(x*x+y*y+z*z);
    private static double Dot(Quaternion a,Quaternion b)=>a.x*(double)b.x+a.y*(double)b.y+a.z*(double)b.z+a.w*(double)b.w;
    private static double Norm(Quaternion q)=>Math.Sqrt(Dot(q,q));
    private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
}
