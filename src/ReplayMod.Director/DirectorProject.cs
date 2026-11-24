using System;
using System.Collections.Generic;
using System.Linq;

namespace ReplayMod.Director;

[Serializable] public class DirectorProject
{
    public int version = 1;
    public string replayHash;
    public List<SplineRecord> path = new List<SplineRecord>();
    public PoseRecord firstPoint;
    public List<CameraKey> cameras = new List<CameraKey>();
    public bool pathAttached, smoothCameraPath=true, stopAtCameraPathEnd=true;
    public float cameraPathSmoothness=.65f;
    public bool showCameraPath=true,animatePathRotation=true,animatePathFov=true;
    public float cameraPathEndHold;
    public List<SpeedKey> speeds = new List<SpeedKey>();
    public List<Bookmark> bookmarks = new List<Bookmark>();
    public List<Shot> shots = new List<Shot>();
    public List<StudioLightSettings> studioLights = new List<StudioLightSettings>();
    public EnvironmentSettings environment = new EnvironmentSettings();
    public int lightMode; // 0 native, 1 on, 2 off
    public int landingLightMode;
    public bool lightsAll = true;
    public uint lightTarget;
    public int rig;
    public uint target;
    public uint secondTarget;
    public int cockpitSeat;
    public float followAzimuth=135,followElevation=12;
    public bool followWithAircraft;
    public float distance = 35, height = 8, orbitRate = 12, smoothing = 4, fov = 60;
    public bool autoFrameSubject;
    public bool showPlayerNames=true;
    public bool reduceCloseClipping,detailedCockpit,seatedPilots;
    public bool allowCameraThroughWorld;
    public bool flightInstruments=true;
    public bool firstPersonEjection;
    public int motionSmoothing; // 0 recorded, 1 light, 2 normal, 3 strong
    public int motionSmoothingScope; // 0 selected aircraft, 1 all eligible aircraft
    public double preRoll = 2;
    public bool Validate(double duration)
    {
        if (version != 1 || path == null || cameras == null || speeds == null || bookmarks == null || shots == null || environment == null || studioLights == null) return false;
        if (path.Count > 2000 || cameras.Count > 2000 || speeds.Count > 2000 || bookmarks.Count > 10000 || shots.Count > 200) return false;
        if (!Finite(duration) || duration < 0 || !Finite(preRoll) || preRoll < 0 || preRoll > 5) return false;
        if(!Range(cameraPathEndHold,0,10)||!Range(cameraPathSmoothness,0,1))return false;
        if(motionSmoothing<0||motionSmoothing>3||motionSmoothingScope<0||motionSmoothingScope>1)return false;
        if(cockpitSeat<0||cockpitSeat>15||!Range(followAzimuth,-180,180)||!Range(followElevation,-89,89))return false;
        if (rig < 0 || rig > 7 || lightMode < 0 || lightMode > 2 || landingLightMode < 0 || landingLightMode > 2 || !Range(fov, 1, 120) || !Range(distance, 1, 1000) || !Range(height, -100, 1000) || !Range(orbitRate, -180, 180) || !Range(smoothing, 0, 20)) return false;
        if (!environment.Valid()) return false;
        if(studioLights.Count>StudioLightSettings.MaximumLights||studioLights.Any(light=>light==null||!light.Valid())||studioLights.Select(light=>light.id).Distinct(StringComparer.Ordinal).Count()!=studioLights.Count)return false;
        if(firstPoint!=null && !firstPoint.Valid(duration))return false;
        double end = -1;
        foreach (var p in path) { if (p == null || p.a==null || p.b==null || p.c==null || p.d==null || !p.a.Valid(duration) || !p.b.Valid(duration) || !p.c.Valid(duration) || !p.d.Valid(duration) || p.d.time <= p.a.time || p.a.time < end) return false; end = p.d.time; }
        if (cameras.Any(k => k == null || !k.Valid(duration) || !Range(k.fov, 1, 120) || k.easing < 0 || k.easing > 1 || k.segmentMode<0 || k.segmentMode>3)) return false;
        if (speeds.Any(k => k == null || !TimeValid(k.time, duration) || !Range(k.scale, .05, 4))) return false;
        cameras.Sort((a,b) => a.time.CompareTo(b.time)); speeds.Sort((a,b) => a.time.CompareTo(b.time));
        if (cameras.Zip(cameras.Skip(1), (a,b) => a.time == b.time).Any(x => x) || speeds.Zip(speeds.Skip(1), (a,b) => a.time == b.time).Any(x => x)) return false;
        return bookmarks.All(b => b != null && TimeValid(b.time, duration) && b.name != null && b.name.Length <= 120) && shots.All(s => s != null && s.Valid(duration));
    }
    public static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    public static bool Range(double n, double lo, double hi) => Finite(n) && n >= lo && n <= hi;
    public static bool TimeValid(double n, double duration) => Range(n, 0, duration);
    // Capture at the displayed replay clock without inventing a segment duration.
    // Rewinding inserts in time order; recapturing a paused timestamp updates it.
    public static bool TryStoreCameraPoint(List<CameraKey> keys,CameraKey point,double duration,out int index,out bool replaced)
    {
        index=-1;replaced=false;
        if(keys==null||keys.Count>2000||point==null||!Finite(duration)||duration<0||!point.Valid(duration)||!Range(point.fov,1,120)||point.easing<0||point.easing>1||point.segmentMode<0||point.segmentMode>3)return false;
        double previous=-1;
        foreach(var existing in keys)
        {if(existing==null||!TimeValid(existing.time,duration)||existing.time<=previous)return false;previous=existing.time;}
        int low=0,high=keys.Count;
        while(low<high){int mid=low+(high-low)/2;if(keys[mid].time<point.time)low=mid+1;else high=mid;}
        bool update=low<keys.Count&&keys[low].time==point.time;
        if(!update&&keys.Count>=2000)return false;
        if(update)keys[low]=point;else keys.Insert(low,point);
        index=low;replaced=update;return true;
    }
    public static double Ease(double value, int easing) { value = Math.Max(0, Math.Min(1, value)); return easing == 1 ? value * value * (3 - 2 * value) : value; }
    public static float SpeedAt(List<SpeedKey> keys, double time, float fallback)
    {
        if (keys.Count == 0) return fallback;
        if (time <= keys[0].time) return keys[0].scale;
        for (int i = 1; i < keys.Count; i++) if (time <= keys[i].time) { var a=keys[i-1]; var b=keys[i]; return (float)(a.scale + (b.scale-a.scale)*Ease((time-a.time)/(b.time-a.time), 1)); }
        return keys[keys.Count-1].scale;
    }
}
[Serializable] public class PoseRecord
{
    public double time;
    public float x,y,z,qx,qy,qz,qw=1;
    public bool Valid(double duration) => DirectorProject.TimeValid(time,duration) && DirectorProject.Range(x,-1e8,1e8) && DirectorProject.Range(y,-1e8,1e8) && DirectorProject.Range(z,-1e8,1e8) && DirectorProject.Finite(qx) && DirectorProject.Finite(qy) && DirectorProject.Finite(qz) && DirectorProject.Finite(qw) && DirectorProject.Range((double)qx*qx+(double)qy*qy+(double)qz*qz+(double)qw*qw,.9,1.1);
}
[Serializable] public class CameraKey : PoseRecord { public float fov=60; public int easing=1; public uint target; public bool relative; public int segmentMode; }
[Serializable] public class SplineRecord { public PoseRecord a,b,c,d; }
[Serializable] public class SpeedKey { public double time; public float scale=1; }
[Serializable] public class Bookmark { public double time; public string name; public uint actor; public bool automatic; public long actorLifetime; }
[Serializable] public class Shot
{
    public string name="Shot";
    public double start,end;
    public int fps=60,width=1920,height=1080;
    public bool Valid(double duration) => name != null && name.Length <= 120 && DirectorProject.TimeValid(start,duration) && DirectorProject.TimeValid(end,duration) && end > start && end-start <= 300 && fps >= 24 && fps <= 120 && width >= 320 && width <= 3840 && height >= 180 && height <= 2160 && width%2==0 && height%2==0;
    public static double FrameTime(double start, int frame, int fps) => start + (double)frame/fps;
}
[Serializable] public class EnvironmentSettings
{
    public bool enabled;
    public float hour=12,conditions,cloudHeight=1800,windSpeed,windHeading,turbulence,moonPhase=14;
    public bool Valid() => DirectorProject.Range(hour,0,24) && DirectorProject.Range(conditions,0,1) && DirectorProject.Range(cloudHeight,500,10000) && DirectorProject.Range(windSpeed,0,100) && DirectorProject.Range(windHeading,0,360) && DirectorProject.Range(turbulence,0,1) && DirectorProject.Range(moonPhase,0,28);
}
