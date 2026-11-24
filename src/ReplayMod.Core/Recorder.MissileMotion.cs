using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Data;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class Recorder
{
    // Retain exact object ownership: a reused persistent ID must never inherit
    // an earlier missile's impact. This table changes capture only, not native
    // missile state, registration, audio or destruction timing.
    private readonly Dictionary<uint,Missile> _terminalRecordingMissiles=new Dictionary<uint,Missile>();
    private static bool IsNativelyTerminalMissile(Missile missile)=>missile.disabled||(missile.rb!=null&&missile.rb.isKinematic);
    private bool IsRecordingMissileTerminal(Missile missile)=>missile!=null&&
        _terminalRecordingMissiles.TryGetValue(missile.persistentID.Id,out var terminal)&&ReferenceEquals(terminal,missile);
    private void MarkRecordingMissileTerminal(Missile missile)
    {
        if(IsTrackedLifecycleUnit(missile))_terminalRecordingMissiles[missile.persistentID.Id]=missile;
    }
    private void ForgetRecordingMissileMotion(uint id)=>_terminalRecordingMissiles.Remove(id);
    private void ForgetRecordingMissileMotion(Unit unit)
    {
        if(unit!=null&&_terminalRecordingMissiles.TryGetValue(unit.persistentID.Id,out var terminal)&&ReferenceEquals(terminal,unit))
            _terminalRecordingMissiles.Remove(unit.persistentID.Id);
    }
    private void ClearRecordingMissileMotion()=>_terminalRecordingMissiles.Clear();

    private void RecordObservedMissileMotion(Missile missile)
    {
        if(!IsTrackedLifecycleUnit(missile)||IsRecordingMissileTerminal(missile))return;
        if(_lastPositionUpdateTime.TryGetValue(missile.persistentID,out var previous)&&_virtualTime-previous<ReplayManager.AircraftUpdateInterval)return;
        UpdatePositionEvent pose=null;
        try
        {
            // SendTransformBatcher presents connected-client missiles in its
            // LateUpdate. Recorder order 10000 observes that completed pose on
            // the same recording clock and phase as aircraft, rather than the
            // previous frame's transform in Update.
            pose=ReplayEventFactory.GetEvent<UpdatePositionEvent>();pose.unitId=missile.persistentID.Id;pose.Time=_virtualTime;
            pose.position=missile.transform.position.ToGlobalPosition();pose.rotation=missile.transform.rotation;
            pose.velocity=missile.rb!=null?missile.rb.velocity:Vector3.zero;
            if(!AircraftPartMotionSnapshot.Valid(new AircraftPartPose {position=pose.position,rotation=pose.rotation})||
                !AircraftVisualSnapshot.Finite(pose.velocity.x)||!AircraftVisualSnapshot.Finite(pose.velocity.y)||!AircraftVisualSnapshot.Finite(pose.velocity.z))
                throw new InvalidDataException("Invalid observed missile world pose");
            var queued=pose;pose=null;QueueEvent(queued);_lastPositionUpdateTime[missile.persistentID]=_virtualTime;
            RecordMissileStatus(missile);
        }
        catch(Exception error)
        {
            CaptureFailureCount++;uint id=missile.persistentID.Id;
            if(!_captureFaults.ContainsKey(id))Plugin.logger.LogWarning("Replay missile capture failed for "+id+"; other actors continue: "+error);
            _captureFaults[id]=_captureFaults.TryGetValue(id,out var failures)?failures+1:1;
        }
        finally {if(pose!=null)ReplayEventFactory.Return(pose);}
    }
}
