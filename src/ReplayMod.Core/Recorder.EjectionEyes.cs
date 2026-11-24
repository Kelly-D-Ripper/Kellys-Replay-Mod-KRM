using System;
using System.Collections.Generic;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

public partial class Recorder
{
    private sealed class EyeBinding {public Unit source;public Transform eye;public Quaternion correction;public bool fallback,resolved;public double last=double.NegativeInfinity;public double parentBirth;public uint parent;}
    private readonly Dictionary<uint,EyeBinding> _ejectionEyes=new Dictionary<uint,EyeBinding>();
    private readonly Dictionary<uint,double> _ejectionAircraftBirths=new Dictionary<uint,double>();
    private void NoteEjectionCaptureSpawn(Unit unit)
    {
        if(unit is Aircraft)_ejectionAircraftBirths[unit.persistentID.Id]=_virtualTime;
        // The parent may unregister before this frame's late eye capture.
        // Retain its value-owned lifetime at the child's registration boundary.
        else if(unit is PilotDismounted pilot&&pilot.parentUnit.Id!=0&&_ejectionAircraftBirths.TryGetValue(pilot.parentUnit.Id,out double birth))
            _ejectionEyes[unit.persistentID.Id]=new EyeBinding {source=unit,parent=pilot.parentUnit.Id,parentBirth=birth};
    }
    private void CaptureEjectionEyesLate()
    {
        foreach(var pair in _unitsOnTrack)
        {
            var unit=pair.Value;if(unit==null)continue;
            bool capsule=false;uint parent;byte seat=0;Transform anchor=null;
            if(unit is PilotDismounted pilot){parent=pilot.parentUnit.Id;seat=pilot.pilotNumber;}
            else if(unit is Aircraft aircraft&&aircraft.cockpit!=null&&aircraft.cockpit.IsDetached()&&aircraft.cockpitViewPoint!=null&&aircraft.cockpit.GetComponent<EscapeCapsule>()!=null)
            {capsule=true;parent=unit.persistentID.Id;anchor=aircraft.cockpitViewPoint;}
            else continue;
            if(parent==0||seat>=16)continue;
            if(!_ejectionEyes.TryGetValue(pair.Key.Id,out var binding)||!ReferenceEquals(binding.source,unit))
            {
                if(!_ejectionAircraftBirths.TryGetValue(parent,out double birth))continue;
                binding=new EyeBinding {source=unit,parent=parent,parentBirth=birth};
                _ejectionEyes[pair.Key.Id]=binding;
            }
            if(!binding.resolved)
            {
                if(capsule)binding.eye=anchor;
                else
                {
                    foreach(var skin in unit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var bones=skin.bones;if(bones==null||bones.Length>256)continue;
                        foreach(var bone in bones)if(bone!=null&&(bone.name.Equals("Head",StringComparison.OrdinalIgnoreCase)||bone.name.EndsWith(":Head",StringComparison.OrdinalIgnoreCase)))
                        {binding.eye=bone;break;}
                        if(binding.eye!=null)break;
                    }
                    binding.fallback=binding.eye==null;if(binding.fallback)binding.eye=unit.transform;
                    binding.correction=Quaternion.Inverse(binding.eye.rotation)*unit.transform.rotation;
                }
                binding.resolved=true;
            }
            if(_virtualTime-binding.last<.05||binding.eye==null)continue;
            PilotEjectionStateEvent pending=null;
            try
            {
                Quaternion rotation=capsule?binding.eye.rotation:binding.eye.rotation*binding.correction;
                Vector3 position=capsule?binding.eye.position:binding.fallback?binding.eye.TransformPoint(new Vector3(0,1.5f,.07f)):binding.eye.position+rotation*new Vector3(0,.025f,.075f);
                pending=ReplayEventFactory.GetEvent<PilotEjectionStateEvent>();pending.Time=_virtualTime;pending.unitId=pair.Key.Id;pending.aircraftId=binding.parent;pending.aircraftBirth=binding.parentBirth;
                pending.seat=seat;pending.capsule=capsule;pending.eye=position.ToGlobalPosition();pending.rotation=rotation;
                if(!pending.HasValidPayload())throw new InvalidOperationException("Invalid pilot eye pose");
                var queued=pending;pending=null;QueueEvent(queued);binding.last=_virtualTime;
            }
            catch(Exception error){if(pending!=null)ReplayEventFactory.Return(pending);if(binding.last!=double.PositiveInfinity)Plugin.SafeLogWarning("Replay ejection eye capture unavailable: "+error.Message);binding.last=double.PositiveInfinity;CaptureFailureCount++;}
        }
    }
    private void ForgetEjectionCapture(Unit unit){if(unit!=null){_ejectionEyes.Remove(unit.persistentID.Id);if(unit is Aircraft)_ejectionAircraftBirths.Remove(unit.persistentID.Id);}}
    private void ClearEjectionCapture(){_ejectionEyes.Clear();_ejectionAircraftBirths.Clear();}
}
