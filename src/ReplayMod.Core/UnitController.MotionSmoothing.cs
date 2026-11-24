using System;
using System.Collections.Generic;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Core;

public partial class UnitController
{
    private int _motionSmoothing;
    private uint _motionSmoothingActor;
    private bool _motionSmoothingSelectedOnly;
    private readonly HashSet<uint> _motionSmoothingDamaged=new HashSet<uint>();
    private readonly Dictionary<uint,double> _motionPartBarriers=new Dictionary<uint,double>();
    private Func<uint,double> _nextMotionBarrier;
    private readonly Dictionary<uint,MotionPresentationPose> _motionPresentationPoses=new Dictionary<uint,MotionPresentationPose>();
    private struct MotionPresentationPose
    {
        public double time;
        public Vector3 rawPosition,renderPosition;
        public Quaternion rawRotation,renderRotation;
    }
    public int SmoothedAircraftCount {get;private set;}
    public void SetMotionSmoothing(int preset,uint selectedActorId,bool selectedOnly,Func<uint,double> nextBarrier=null)
    {
        _motionSmoothing=ReplayMotionSmoothing.Enabled(preset)?preset:0;
        _motionSmoothingActor=selectedActorId;_motionSmoothingSelectedOnly=selectedOnly;
        _nextMotionBarrier=nextBarrier;
    }
    private bool SmoothMotion(uint id,Unit unit,List<PositionSnapshot> samples,double time,out PositionSnapshot filtered)
    {
        filtered=default;
        if(_motionSmoothing==0||!(unit is Aircraft)||unit.disabled||_motionSmoothingDamaged.Contains(id)||
            (_motionSmoothingSelectedOnly&&id!=_motionSmoothingActor)||!_spawnTimes.TryGetValue(id,out double birth))return false;
        double barrier=_nextMotionBarrier?.Invoke(id)??double.PositiveInfinity;
        if(_motionPartBarriers.TryGetValue(id,out double partBarrier))barrier=Math.Min(barrier,partBarrier);
        return ReplayMotionSmoothing.TrySample(samples,time,_motionSmoothing,birth,barrier,out filtered);
    }
    private void MarkMotionPartBarrier(uint id,double time)
    {if(!_motionPartBarriers.TryGetValue(id,out double previous)||time<previous)_motionPartBarriers[id]=time;}
    private void MarkMotionDamaged(uint id) { _motionSmoothingDamaged.Add(id);_motionPresentationPoses.Remove(id); }
    private void ForgetMotionSmoothing(uint id) { _motionSmoothingDamaged.Remove(id);_motionPartBarriers.Remove(id);_motionPresentationPoses.Remove(id); }
    private void ClearMotionSmoothing() { _motionSmoothingDamaged.Clear();_motionPartBarriers.Clear();_motionPresentationPoses.Clear();SmoothedAircraftCount=0; }
    private void ApplyAttachedPartPresentation(uint id,double time,ref Vector3 position,ref Quaternion rotation)
    {
        if(!_motionPresentationPoses.TryGetValue(id,out var pose)||pose.time!=time)return;
        Quaternion delta=pose.renderRotation*Quaternion.Inverse(pose.rawRotation);
        position=pose.renderPosition+delta*(position-pose.rawPosition);
        rotation=delta*rotation;
    }
}
