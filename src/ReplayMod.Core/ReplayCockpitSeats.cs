using System;
using UnityEngine;

namespace ReplayMod.Core;

// Extra seats have Pilot models but no native CameraCockpitState entry. Calibrate
// their actual head anchors against the aircraft's own primary-seat viewpoint.
// No aircraft-type offsets or guessed seats are used when those anchors are absent.
public static class ReplayCockpitSeats
{
    private static Aircraft boundAircraft;
    private static Transform[] heads=Array.Empty<Transform>();
    public static int SeatCount(Aircraft aircraft)=>aircraft?.pilots==null?0:Math.Min(16,aircraft.pilots.Length);
    public static bool TryEye(Aircraft aircraft,int seat,out Vector3 position,out Quaternion rotation)
    {
        position=Vector3.zero;rotation=Quaternion.identity;
        if(aircraft==null||aircraft.cockpit==null||aircraft.cockpitViewPoint==null||seat<0||seat>=16)return false;
        rotation=aircraft.cockpit.transform.rotation;
        if(seat==0){position=aircraft.cockpitViewPoint.position;return IsFinite(position)&&IsFinite(rotation);}
        if(seat>=SeatCount(aircraft))return false;
        var pilot=aircraft.pilots[seat];
        if(pilot==null||pilot.ejected||pilot.dead||!pilot.gameObject.activeInHierarchy||pilot.aircraft!=aircraft)return false;
        var part=pilot.GetUnitPart();
        if(part!=null&&(!part.gameObject.activeInHierarchy||part.IsDetached()||part.fragmented))return false;
        if(!ReferenceEquals(boundAircraft,aircraft))
        {
            boundAircraft=aircraft;heads=new Transform[SeatCount(aircraft)];
            for(int i=0;i<heads.Length;i++)heads[i]=FindHead(aircraft.pilots[i]);
        }
        if(heads.Length<=seat||heads[0]==null||heads[seat]==null||!heads[seat].gameObject.activeInHierarchy)return false;
        Vector3 eyeOffset=aircraft.cockpitViewPoint.position-heads[0].position;
        // Reject mismatched rigs instead of placing the camera metres from a head.
        if(!IsFinite(eyeOffset)||eyeOffset.sqrMagnitude>1)return false;
        var primary=aircraft.pilots[0];if(primary==null)return false;
        var seatRotation=pilot.transform.rotation*Quaternion.Inverse(primary.transform.rotation);
        position=heads[seat].position+seatRotation*eyeOffset;rotation=seatRotation*rotation;
        return IsFinite(position)&&IsFinite(rotation);
    }
    private static Transform FindHead(Pilot pilot)
    {
        if(pilot==null||pilot.skinnedMeshRenderer==null)return null;
        var bones=pilot.skinnedMeshRenderer.bones;
        if(bones==null||bones.Length>256)return null;
        foreach(var bone in bones)
            if(bone!=null&&(bone.name.Equals("Head",StringComparison.OrdinalIgnoreCase)||bone.name.EndsWith(":Head",StringComparison.OrdinalIgnoreCase)))return bone;
        return null;
    }
    private static bool IsFinite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
    private static bool IsFinite(Quaternion q)=>Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w);
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    public static void Clear(){boundAircraft=null;heads=Array.Empty<Transform>();}
}

