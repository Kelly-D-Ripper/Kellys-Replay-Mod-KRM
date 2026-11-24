using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public static class ReplayPartFragmentScope
{
    [ThreadStatic]private static int depth;
    public static bool IsActive=>depth>0;
    public static IDisposable Enter(){depth++;return new ExitScope();}
    private sealed class ExitScope:IDisposable
    {private bool disposed;public void Dispose(){if(!disposed){disposed=true;depth--;}}}
}
public partial class UnitController
{
    private sealed class PlaybackAircraftParts
    {
        public Aircraft aircraft;
        public Dictionary<byte,UnitPart> parts;
        public byte[] applyOrder;
        public readonly int[] sampleIndices=new int[256];
        public AircraftPartPose[] indexedSample;
        public readonly AircraftPartMotionTimeline samples=new AircraftPartMotionTimeline();
        public readonly HashSet<Rigidbody> frozen=new HashSet<Rigidbody>();
        public bool hasSamples;
    }
    private readonly Dictionary<uint,PlaybackAircraftParts> _playbackAircraftParts=new Dictionary<uint,PlaybackAircraftParts>();
    public void RefreshRecordedUnitPoses(double time)
    {
        foreach(var pair in _unitSnapshots)MoveUnit(pair.Key,pair.Value,time);
        RefreshReplayBuildingPresentation(time);
    }
    private PlaybackAircraftParts InitializeAircraftPartMotion(uint id,Aircraft aircraft)
    {
        if(!_playbackAircraftParts.TryGetValue(id,out var state))
            _playbackAircraftParts[id]=state=new PlaybackAircraftParts {aircraft=aircraft};
        if(state.parts==null&&aircraft.partLookup!=null&&aircraft.partLookup.Count>0)
        {
            state.parts=aircraft.partLookup.Where(part=>part!=null).GroupBy(part=>part.id).ToDictionary(group=>group.Key,group=>group.First());
            state.applyOrder=state.parts.Values.OrderBy(part=>PartDepth(part.transform)).Select(part=>part.id).ToArray();
        }
        return state;
    }
    public void Execute(AircraftPartMotionEvent e)
    {
        if(!AcceptSample(e.unitId,e.Time)||!(FindActor(e.unitId)is Aircraft aircraft))return;
        // These world-space samples begin only when the recorder has observed
        // damage, ejection, disabling or a detached/fragmented part.
        MarkMotionPartBarrier(e.unitId,e.Time);
        var state=InitializeAircraftPartMotion(e.unitId,aircraft);state.samples.Add(e);state.hasSamples=true;
    }
    private UnitPart FindRecordedAircraftPart(Unit unit,byte partId)
    {
        if(unit is Aircraft aircraft)
        {
            if(!_reverseUnitMap.TryGetValue(unit.persistentID,out var id))return null;
            var state=InitializeAircraftPartMotion(id,aircraft);
            if(state.parts!=null&&state.parts.TryGetValue(partId,out var part)&&part!=null)return part;
        }
        return unit.partLookup?.FirstOrDefault(part=>part!=null&&part.id==partId);
    }
    private void ApplyRecordedPartDetach(DetachPartEvent e)
    {
        MarkMotionDamaged(e.unitID);
        PositionActorForEffect(e.unitID,e.Time);
        if(!TryGetUnit(e.unitID,out _,out var unit))return;
        var part=FindRecordedAircraftPart(unit,e.partID);
        if(part==null||part.IsDetached()||part.attachInfo==null)return;
        Vector3 velocity=unit.rb!=null?unit.rb.velocity:Vector3.zero;
        // Execute the observed receiver operation directly. Sending another
        // network command can reject/delay it and invent a second breakup.
        DetachRecordedPart(unit,part,velocity);
        if(ReplayRuntime.FormatMajor>=5&&unit is Aircraft aircraft)
        {var state=InitializeAircraftPartMotion(e.unitID,aircraft);FreezeAircraftPart(state,part);}
    }
    public void RefreshRecordedAircraftParts(double time)
    {
        foreach(var pair in _playbackAircraftParts)
        {
            var state=pair.Value;
            if(!state.hasSamples||state.aircraft==null||!ReferenceEquals(FindActor(pair.Key),state.aircraft))continue;
            if(state.parts==null)InitializeAircraftPartMotion(pair.Key,state.aircraft);
            if(state.parts==null||!state.samples.Locate(time,out var previous,out var next,out var fraction))continue;
            if(!ReferenceEquals(state.indexedSample,previous.parts))
            {
                for(int i=0;i<state.sampleIndices.Length;i++)state.sampleIndices[i]=-1;
                for(int i=0;i<previous.parts.Length;i++)state.sampleIndices[previous.parts[i].partId]=i;
                state.indexedSample=previous.parts;
            }
            // Source part IDs are stable even when native RemoveFromUnit drops
            // a capsule/debris part from partLookup during its remaining life.
            foreach(byte id in state.applyOrder)
            {
                int i=state.sampleIndices[id];if(i<0)continue;
                var a=previous.parts[i];var b=next.parts.Length==previous.parts.Length&&next.parts[i].partId==a.partId?next.parts[i]:a;
                if(!state.parts.TryGetValue(a.partId,out var part)||part==null)continue;
                bool present=(a.flags&AircraftPartFlags.Present)!=0;
                if(part.gameObject!=state.aircraft.gameObject&&part.gameObject.activeSelf!=present)part.gameObject.SetActive(present);
                if(!present)continue;
                if((a.flags&AircraftPartFlags.Detached)!=0&&!part.IsDetached()&&part.attachInfo!=null)
                {
                    DetachRecordedPart(state.aircraft,part,Vector3.zero);
                }
                if((a.flags&AircraftPartFlags.Fragmented)!=0&&!part.fragmented)
                    using(ReplayPartFragmentScope.Enter())part.SpawnFragments();
                FreezeAircraftPart(state,part);
                float blend=(b.flags&AircraftPartFlags.Present)!=0?fraction:0;
                var position=new GlobalPosition(a.position.x+(b.position.x-a.position.x)*blend,a.position.y+(b.position.y-a.position.y)*blend,a.position.z+(b.position.z-a.position.z)*blend).ToLocalPosition();
                var rotation=Quaternion.Slerp(a.rotation,b.rotation,blend);
                if((a.flags&(AircraftPartFlags.Detached|AircraftPartFlags.Fragmented))==0)
                    ApplyAttachedPartPresentation(pair.Key,time,ref position,ref rotation);
                part.transform.SetPositionAndRotation(position,rotation);
                if(part.rb!=null&&part.rb!=state.aircraft.rb){part.rb.position=position;part.rb.rotation=rotation;}
            }
        }
    }
    private static void DetachRecordedPart(Unit unit,UnitPart part,Vector3 velocity)
    {
        if(part is AeroPart aero)
        {
            // MergeWithParent schedules the old part body for destruction at
            // frame end. A same-frame seek can detach before that happens.
            // Remove only the abandoned body, never the aircraft's live body.
            var oldBody=part.GetComponent<Rigidbody>();
            if(aero.simplePhysics&&oldBody!=null&&oldBody!=unit.rb&&oldBody!=aero.rb)
            {
                // Some cargo ramps retain a HingeJoint that the game's merge
                // does not remove, which otherwise prevents body removal.
                foreach(var joint in part.GetComponents<Joint>())
                    if(joint!=null)UnityEngine.Object.DestroyImmediate(joint);
                UnityEngine.Object.DestroyImmediate(oldBody);
            }
            if(!aero.simplePhysics&&unit.rb!=null&&aero.rb==unit.rb)
                aero.CreateRB(velocity,part.transform.position);
        }
        part.Detach(velocity,Vector3.zero);
    }
    private static void FreezeAircraftPart(PlaybackAircraftParts state,UnitPart part)
    {
        var body=part.rb;
        if(body==null||body==state.aircraft.rb)return;
        if(state.frozen.Add(body))
        {
            body.detectCollisions=false;body.useGravity=false;body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.isKinematic=true;
            if(part is AeroPart aero)aero.UnregisterFromJob();
        }
    }
    private void ForgetAircraftPartMotion(uint id)
    {
        if(!_playbackAircraftParts.TryGetValue(id,out var state))return;
        _playbackAircraftParts.Remove(id);ReleaseDetachedAircraftParts(state);
    }
    private static void ReleaseDetachedAircraftParts(PlaybackAircraftParts state)
    {
        if(state.parts==null)return;
        foreach(var part in state.parts.Values)
            if(part!=null&&part.gameObject!=state.aircraft?.gameObject&&(state.aircraft==null||!part.transform.IsChildOf(state.aircraft.transform)))
                UnityEngine.Object.Destroy(part.gameObject);
    }
    private void ClearAircraftPartMotion()
    {foreach(var state in _playbackAircraftParts.Values)ReleaseDetachedAircraftParts(state);_playbackAircraftParts.Clear();}
    private static int PartDepth(Transform transform){int depth=0;while(transform!=null&&depth<128){depth++;transform=transform.parent;}return depth;}
}
