using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReplayMod.Director;

public delegate bool ResolveCameraPathKey(CameraKey key,out Vector3 position,out Quaternion rotation);
public struct CameraPathSample { public Vector3 position; public Quaternion rotation; public float fov; }

public static class CameraPathSampler
{
    public static bool TrySample(IReadOnlyList<CameraKey> keys,double time,bool smooth,ResolveCameraPathKey resolve,out CameraPathSample sample)
        =>TrySample(keys,time,smooth,0,resolve,out sample);

    public static bool TrySample(IReadOnlyList<CameraKey> keys,double time,bool smooth,float smoothness,ResolveCameraPathKey resolve,out CameraPathSample sample)
    {
        sample=default;
        if(keys==null||keys.Count==0||resolve==null||!DirectorProject.Finite(time)||!DirectorProject.Finite(smoothness))return false;
        int last=keys.Count-1;
        if(keys[0]==null||keys[last]==null)return false;
        float strength=smooth?(float)Math.Max(0,Math.Min(1,smoothness)):0;
        if(time<=keys[0].time)return Endpoint(keys[0],resolve,strength>0,out sample);
        if(time>=keys[last].time)return Endpoint(keys[last],resolve,strength>0,out sample);
        int low=0,high=last;
        while(high-low>1){int mid=low+(high-low)/2;if(keys[mid]==null)return false;if(keys[mid].time<=time)low=mid;else high=mid;}
        var a=keys[low];var b=keys[high];double duration=b.time-a.time;
        if(duration<=0||!DirectorProject.Finite(duration))return false;
        if(a.segmentMode<0||a.segmentMode>3)return false;
        if(a.segmentMode==3)return Endpoint(a,resolve,false,out sample);
        if(a.segmentMode==1)smooth=false;
        else if(a.segmentMode==2)smooth=true;
        strength=smooth?(float)Math.Max(0,Math.Min(1,smoothness)):0;
        if(!resolve(a,out var p1,out var r1)||!resolve(b,out var p2,out var r2))return false;
        double t=Math.Max(0,Math.Min(1,(time-a.time)/duration));
        float blend=(float)(smooth||a.segmentMode==1?t:DirectorProject.Ease(t,a.easing));
        Vector3 position=Vector3.Lerp(p1,p2,blend);
        var p0=p1;var p3=p2;var r0=r1;var r3=r2;double before=a.time,after=b.time;
        bool previous=false,next=false;
        if(smooth&&keys.Count>2)
        {
            if(low>0&&keys[low-1]!=null&&resolve(keys[low-1],out var previousPosition,out var previousRotation))
            {p0=previousPosition;r0=previousRotation;before=keys[low-1].time;previous=true;}
            if(high<last&&keys[high+1]!=null&&resolve(keys[high+1],out var nextPosition,out var nextRotation))
            {p3=nextPosition;r3=nextRotation;after=keys[high+1].time;next=true;}
            double left=b.time-before,right=after-a.time;
            if(left<=0||right<=0)return false;
            // Time-aware Hermite tangents are shared across neighbouring segments.
            // This keeps a continuous velocity through a turn even when durations differ.
            position=new Vector3(
                Hermite(p1.x,p2.x,(p2.x-p0.x)/left,(p3.x-p1.x)/right,duration,t),
                Hermite(p1.y,p2.y,(p2.y-p0.y)/left,(p3.y-p1.y)/right,duration,t),
                Hermite(p1.z,p2.z,(p2.z-p0.z)/left,(p3.z-p1.z)/right,duration,t));
        }
        sample=new CameraPathSample {position=position,rotation=Quaternion.Slerp(r1,r2,blend),fov=a.fov+(b.fov-a.fov)*blend};
        if(strength>0)
        {
            if(!Finite(p0)||!Finite(p1)||!Finite(p2)||!Finite(p3)||
               !TryRotation(r1,out var q1)||!TryRotation(r2,out var q2))return false;
            q2=Aligned(q2,q1);
            var q0=q1;var q3=q2;
            if(previous&&!TryRotation(r0,out q0))return false;
            if(next&&!TryRotation(r3,out q3))return false;
            q0=Aligned(q0,q1);q3=Aligned(q3,q2);
            double leftDuration=a.time-before,rightDuration=after-b.time;
            if((previous&&(!DirectorProject.Finite(leftDuration)||leftDuration<=0))||
               (next&&(!DirectorProject.Finite(rightDuration)||rightDuration<=0)))return false;

            var v1=previous?Velocity(Value(p0),Value(p1),Value(p2),leftDuration,duration,2):default;
            var v2=next?Velocity(Value(p1),Value(p2),Value(p3),duration,rightDuration,2):default;
            var curved=Quintic(Value(p1),Value(p2),v1,v2,duration,t);
            var w1=previous?RotationVelocity(q0,q1,q2,leftDuration,duration):default;
            var w2=next?RotationVelocity(Aligned(q1,q2),q2,q3,duration,rightDuration):default;
            var orient=Quintic(q1,q2,w1,w2,duration,t);
            if(!TryNormalize(orient,out var normalized))return false;
            double lens1=previous?LensVelocity(keys[low-1].fov,a.fov,b.fov,leftDuration,duration):0;
            double lens2=next?LensVelocity(a.fov,b.fov,keys[high+1].fov,duration,rightDuration):0;
            double lens=Quintic(new Value4(a.fov),new Value4(b.fov),new Value4(lens1),new Value4(lens2),duration,t).x;
            sample.position=Vector3.Lerp(position,new Vector3((float)curved.x,(float)curved.y,(float)curved.z),strength);
            sample.rotation=Quaternion.Slerp(Quaternion.Slerp(Rotation(q1),Rotation(q2),blend),Rotation(normalized),strength);
            sample.fov=(float)(sample.fov+(lens-sample.fov)*strength);
        }
        return Finite(sample.position)&&DirectorProject.Finite(sample.fov);
    }
    private static float Hermite(double a,double b,double va,double vb,double duration,double t)
    {double t2=t*t,t3=t2*t;return(float)((2*t3-3*t2+1)*a+(t3-2*t2+t)*duration*va+(-2*t3+3*t2)*b+(t3-t2)*duration*vb);}
    // The stronger curve shares one velocity and zero acceleration at every key.
    // Thus the full-strength curve is C2 in replay time, including the held ends,
    // without a frame-history filter, timing changes or a stop at each waypoint.
    private static Value4 Quintic(Value4 a,Value4 b,Value4 va,Value4 vb,double duration,double t)
    {
        double t2=t*t,t3=t2*t,t4=t3*t,t5=t4*t;
        return a*(1-10*t3+15*t4-6*t5)+b*(10*t3-15*t4+6*t5)+
            va*(duration*(t-6*t3+8*t4-3*t5))+vb*(duration*(-4*t3+7*t4-3*t5));
    }
    private static Value4 Velocity(Value4 previous,Value4 current,Value4 next,double left,double right,double limit)
    {
        var a=current-previous;var b=next-current;
        var velocity=(next-previous)*(1/(left+right));
        double maximum=limit*Math.Min(a.Length/left,b.Length/right),length=velocity.Length;
        return !DirectorProject.Finite(maximum)||!DirectorProject.Finite(length)||maximum<=0?default:
            length>maximum?velocity*(maximum/length):velocity;
    }
    private static Value4 RotationVelocity(Value4 previous,Value4 current,Value4 next,double left,double right)
    {
        // Hemisphere alignment avoids long spins when equivalent quaternions
        // have opposite signs. Tangents lie on the unit quaternion sphere.
        var velocity=Velocity(Aligned(previous,current),current,Aligned(next,current),left,right,1);
        return velocity-current*Value4.Dot(current,velocity);
    }
    private static double LensVelocity(double previous,double current,double next,double left,double right)
    {
        double a=(current-previous)/left,b=(next-current)/right;
        if(!DirectorProject.Finite(a)||!DirectorProject.Finite(b)||a==0||b==0||Math.Sign(a)!=Math.Sign(b))return 0;
        double w1=2*right+left,w2=right+2*left;
        double harmonic=(w1+w2)/(w1/a+w2/b);
        // These bounds keep the quintic Bezier controls in increasing order;
        // zoom cannot overshoot either saved field of view, even at an extreme.
        return Math.Sign(a)*Math.Min(Math.Abs(harmonic),1.25*Math.Min(Math.Abs(a),Math.Abs(b)));
    }
    private static bool Endpoint(CameraKey key,ResolveCameraPathKey resolve,bool normalize,out CameraPathSample sample)
    {
        sample=default;if(key==null||!resolve(key,out var position,out var rotation)||!Finite(position)||!DirectorProject.Finite(key.fov))return false;
        if(normalize){if(!TryRotation(rotation,out var q))return false;rotation=Rotation(q);}
        sample=new CameraPathSample {position=position,rotation=rotation,fov=key.fov};return true;
    }
    private static bool TryRotation(Quaternion rotation,out Value4 value)=>TryNormalize(new Value4(rotation.x,rotation.y,rotation.z,rotation.w),out value);
    private static bool TryNormalize(Value4 value,out Value4 normalized)
    {
        normalized=default;double length=value.Length;
        if(!DirectorProject.Finite(length)||length<1e-12)return false;
        normalized=value*(1/length);return true;
    }
    private static Value4 Aligned(Value4 value,Value4 reference)=>Value4.Dot(value,reference)<0?value*(-1):value;
    private static Value4 Value(Vector3 value)=>new Value4(value.x,value.y,value.z);
    private static Quaternion Rotation(Value4 value)=>new Quaternion {x=(float)value.x,y=(float)value.y,z=(float)value.z,w=(float)value.w};
    private struct Value4
    {
        public double x,y,z,w;
        public Value4(double x,double y=0,double z=0,double w=0){this.x=x;this.y=y;this.z=z;this.w=w;}
        public double Length=>Math.Sqrt(Dot(this,this));
        public static double Dot(Value4 a,Value4 b)=>a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w;
        public static Value4 operator +(Value4 a,Value4 b)=>new Value4(a.x+b.x,a.y+b.y,a.z+b.z,a.w+b.w);
        public static Value4 operator -(Value4 a,Value4 b)=>new Value4(a.x-b.x,a.y-b.y,a.z-b.z,a.w-b.w);
        public static Value4 operator *(Value4 a,double amount)=>new Value4(a.x*amount,a.y*amount,a.z*amount,a.w*amount);
    }
    private static bool Finite(Vector3 v)=>DirectorProject.Finite(v.x)&&DirectorProject.Finite(v.y)&&DirectorProject.Finite(v.z);
}
