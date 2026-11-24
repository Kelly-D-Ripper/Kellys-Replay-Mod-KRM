using System;
using System.Collections.Generic;
using System.Linq;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Core;

// The file stores this small alias table once. Aliases use native part IDs and
// component slots, rather than hierarchy paths: complex-physics AeroParts are
// detached from the aircraft transform while recording and merged on playback.
internal sealed class ReplayAircraftVisuals
{
    internal sealed class Channel
    {
        public string key;
        public Transform target,parent;
        public UnitPart owner;
        public AircraftVisualPose last;
        public bool Valid=>target!=null&&parent!=null&&(owner==null||!owner.IsDetached());
        public AircraftVisualPose Capture()
        {
            if(Valid)
            {
                var pose=new AircraftVisualPose {position=parent.InverseTransformPoint(target.position),rotation=Quaternion.Inverse(parent.rotation)*target.rotation};
                if(AircraftVisualSnapshot.Valid(pose))last=pose;
            }
            return last;
        }
        public void Apply(AircraftVisualPose a,AircraftVisualPose b,float fraction)
        {
            if(!Valid)return;
            target.SetPositionAndRotation(parent.TransformPoint(Vector3.Lerp(a.position,b.position,fraction)),parent.rotation*Quaternion.Slerp(a.rotation,b.rotation,fraction));
        }
    }
    public readonly Aircraft aircraft;
    public readonly Channel[] channels;
    public readonly LandingGear[] gears;
    public readonly SwingWingController[] swings;
    public readonly TiltWingController[] tilts;
    public readonly TailHook[] hooks;
    public readonly uint layoutId;
    private readonly Dictionary<string,Channel> lookup;
    public ReplayAircraftVisuals(Aircraft aircraft)
    {
        this.aircraft=aircraft;
        var selected=new List<Channel>();var seen=new HashSet<Transform>();
        void Add(string key,Transform transform,UnitPart owner=null,Transform parent=null)
        {
            // The aircraft root belongs exclusively to the world pose stream.
            // A sweep's connectedParts can include that root body.
            if(transform==null||transform==aircraft.transform||!seen.Add(transform)||selected.Count>=AircraftVisualSnapshot.MaxChannels)return;
            parent=parent!=null?parent:transform.parent;
            if(parent==null)return;
            selected.Add(new Channel {key=key,target=transform,parent=parent,owner=owner,
                last=new AircraftVisualPose {rotation=Quaternion.identity}});
        }
        gears=Components<LandingGear>(aircraft);
        for(int n=0;n<gears.Length;n++)
        {
            var gear=gears[n];var owner=gear.attachedPart;string key="gear:"+(owner!=null?owner.id:255)+":"+n+":";
            Add(key+"hinge",gear.gearHinge,owner);Add(key+"strut",gear.strutRotationTransform,owner);
            Add(key+"suspension",gear.unsprung!=null?gear.unsprung.transform:null,owner);
            if(gear.wheels!=null)for(int i=0;i<gear.wheels.Length;i++)Add(key+"wheel:"+i,gear.wheels[i],owner);
            if(gear.gearDoors!=null)for(int i=0;i<gear.gearDoors.Count;i++)Add(key+"door:"+i,gear.gearDoors[i].transform,owner);
            if(gear.movingParts!=null)for(int i=0;i<gear.movingParts.Length;i++)Add(key+"link:"+i,gear.movingParts[i].transform,owner);
            if(gear.joints!=null)for(int i=0;i<gear.joints.Length;i++)
            {Add(key+"ik1:"+i,gear.joints[i].hinge1,owner);Add(key+"ik2:"+i,gear.joints[i].hinge2,owner);}
        }
        swings=Components<SwingWingController>(aircraft);
        for(int n=0;n<swings.Length;n++)
        {
            var swing=swings[n];if(swing.rotators==null)continue;
            for(int i=0;i<swing.rotators.Length;i++)
            {
                var rotator=swing.rotators[i];string key="sweep:"+n+":"+i+":";var part=rotator.part;
                Add(key+"anchor",rotator.anchor,part);
                if(part!=null&&part.attachInfo?.parentPart!=null)Add(key+"part",part.transform,part,part.attachInfo.parentPart.transform);
                if(rotator.counterRotators!=null)for(int j=0;j<rotator.counterRotators.Length;j++)Add(key+"counter:"+j,rotator.counterRotators[j],part);
                if(rotator.connectedParts!=null)for(int j=0;j<rotator.connectedParts.Length;j++)
                {
                    var linked=rotator.connectedParts[j];if(linked!=null&&linked.attachInfo?.parentPart!=null)Add(key+"linked:"+j,linked.transform,linked,linked.attachInfo.parentPart.transform);
                }
            }
        }
        tilts=Components<TiltWingController>(aircraft);
        for(int n=0;n<tilts.Length;n++)if(tilts[n].rotatingJoints!=null)
            for(int i=0;i<tilts[n].rotatingJoints.Length;i++)
            {
                var rotor=tilts[n].rotatingJoints[i];var part=rotor.part;string key="tilt:"+n+":"+i+":";
                if(part!=null&&part.attachInfo?.parentPart!=null)Add(key+"part",part.transform,part,part.attachInfo.parentPart.transform);
                if(rotor.links!=null)for(int j=0;j<rotor.links.Length;j++)Add(key+"link:"+j,rotor.links[j].linkage,part);
            }
        var ducts=Components<DuctedThrustSystem>(aircraft);
        for(int n=0;n<ducts.Length;n++)
        {
            if(ducts[n].swivelTransforms!=null)for(int i=0;i<ducts[n].swivelTransforms.Length;i++)
                Add("duct:"+n+":swivel:"+i,ducts[n].swivelTransforms[i].transform,ducts[n].swivelTransforms[i].part);
            if(ducts[n].intakeDoors!=null)for(int i=0;i<ducts[n].intakeDoors.Length;i++)Add("duct:"+n+":intake:"+i,ducts[n].intakeDoors[i].hinge);
        }
        var nozzles=Components<JetNozzle>(aircraft);
        for(int n=0;n<nozzles.Length;n++)if(nozzles[n].vectorTransforms!=null)
            for(int i=0;i<nozzles[n].vectorTransforms.Length;i++)Add("nozzle:"+n+":"+i,nozzles[n].vectorTransforms[i],nozzles[n].part);
        var doors=Components<BayDoor>(aircraft);
        for(int n=0;n<doors.Length;n++)Add("bay:"+n,doors[n].transform,doors[n].GetComponentInParent<UnitPart>());
        hooks=Components<TailHook>(aircraft);
        for(int n=0;n<hooks.Length;n++)Add("tailhook:"+n,hooks[n].hook,hooks[n].unitPart!=null?hooks[n].unitPart:hooks[n].GetComponentInParent<UnitPart>());
        var canopies=Components<Canopy>(aircraft);
        for(int n=0;n<canopies.Length;n++)if(canopies[n].canopyHinges!=null)
            for(int i=0;i<canopies[n].canopyHinges.Length;i++)Add("canopy:"+n+":"+i,canopies[n].canopyHinges[i].transform,canopies[n].attachedPart);
        var brakes=Components<Airbrake>(aircraft);
        for(int n=0;n<brakes.Length;n++)if(brakes[n].transforms!=null)
            for(int i=0;i<brakes[n].transforms.Length;i++)Add("airbrake:"+n+":"+i,brakes[n].transforms[i],brakes[n].part);
        var surfaces=Components<ControlSurface>(aircraft);
        for(int n=0;n<surfaces.Length;n++)
        {
            var surface=surfaces[n];string key="surface:"+(surface.attachedSurface!=null?surface.attachedSurface.id:255)+":"+n+":";
            Add(key+"mesh",surface.visibleMesh!=null?surface.visibleMesh.transform:null,surface.attachedSurface);
            Add(key+"upper",surface.splitUpper,surface.attachedSurface);Add(key+"lower",surface.splitLower,surface.attachedSurface);
        }
        // Parents precede their child channels so restored parent movement does
        // not invalidate a child's final pose in the same render frame.
        channels=selected.OrderBy(c=>Depth(c.target)).ThenBy(c=>c.key,StringComparer.Ordinal).ToArray();
        lookup=channels.ToDictionary(c=>c.key,StringComparer.Ordinal);
        uint hash=2166136261;foreach(var channel in channels)foreach(char value in channel.key+"\n")hash=unchecked((hash^value)*16777619);
        layoutId=hash==0?1:hash;
    }
    private static int Depth(Transform transform)
    {int depth=0;while(transform!=null&&depth<128){depth++;transform=transform.parent;}return depth;}
    private static T[] Components<T>(Aircraft aircraft)where T:Component
    {
        var found=new HashSet<T>();var result=new List<T>();
        void Include(GameObject root){foreach(var component in root.GetComponentsInChildren<T>(true))if(component!=null&&found.Add(component))result.Add(component);}
        // Native part order is stable even when physical parts have no transform parent.
        if(aircraft.partLookup!=null)foreach(var part in aircraft.partLookup.Where(p=>p!=null).OrderBy(p=>p.id))Include(part.gameObject);
        Include(aircraft.gameObject);return result.OrderBy(c=>ComponentKey(c,aircraft),StringComparer.Ordinal).ToArray();
    }
    private static string ComponentKey(Component component,Aircraft aircraft)
    {
        UnitPart owner=component is LandingGear gear?gear.attachedPart:component is ControlSurface surface?surface.attachedSurface:
            component is Airbrake brake?brake.part:component.GetComponentInParent<UnitPart>();
        Transform root=owner!=null?owner.transform:aircraft.transform;
        var path=new List<string>();Transform current=component.transform;
        while(current!=null&&current!=root&&path.Count<128)
        {
            int duplicate=0;
            if(current.parent!=null)for(int i=0;i<current.GetSiblingIndex();i++)if(current.parent.GetChild(i).name==current.name)duplicate++;
            path.Add(current.name+"#"+duplicate);current=current.parent;
        }
        path.Reverse();return(owner!=null?owner.id.ToString("D3"):"root")+"/"+string.Join("/",path);
    }
    public Channel[] Resolve(string[] names)
    {return names.Select(name=>lookup.TryGetValue(name,out var channel)?channel:null).ToArray();}
    public AircraftVisualSnapshot Capture(double time)
    {
        return new AircraftVisualSnapshot {time=time,layoutId=layoutId,gearDeployed=aircraft.gearDeployed,gearState=(byte)aircraft.gearState,
            poses=channels.Select(channel=>channel.Capture()).ToArray()};
    }
    public void ApplyLegacyGear(double delta)
    {
        foreach(var gear in gears)
        {
            if(gear==null||gear.attachedPart==null||gear.attachedPart.IsDetached()||gear.unsprung==null||gear.bumpStop==null)continue;
            float compression=0;
            if(gear.foldAmount<0.2f&&gear.castPoint!=null&&Physics.Linecast(gear.castPoint.position,gear.castPoint.position-gear.castPoint.up*gear.suspensionTravel,out var hit,
                ~((int)PhysicsLayers.ExclusionZonesMask|(int)PhysicsLayers.IgnoreCollisionsMask)))
                compression=Mathf.Clamp(gear.suspensionTravel-hit.distance,0,gear.suspensionTravel);
            gear.compressionDistance=compression;
            gear.unsprung.transform.position=gear.bumpStop.transform.position-gear.bumpStop.transform.up*(gear.suspensionTravel-compression-gear.wheelRadius);
            if(gear.steering&&aircraft.controlInputs!=null)
            {
                gear.steeringAngle=Mathf.Clamp(aircraft.controlInputs.yaw*gear.steeringLock,-Mathf.Abs(gear.steeringLock),Mathf.Abs(gear.steeringLock));
                gear.unsprung.transform.localEulerAngles=new Vector3(0,gear.steeringAngle,0);
            }
            if(delta>0&&compression>0&&gear.wheels!=null&&gear.wheelRadius>0)
                foreach(var wheel in gear.wheels)if(wheel!=null)wheel.Rotate(360f*(float)delta*aircraft.speed/(6.28318f*gear.wheelRadius),0,0,Space.Self);
            if(gear.joints!=null)foreach(var joint in gear.joints)joint.Update();
        }
    }
    public void ApplyLegacySweep()
    {
        if(aircraft.controlInputs==null)return;
        foreach(var swing in swings)
        {
            if(swing==null||swing.rotators==null)continue;
            float input=Mathf.InverseLerp(swing.axisRemapLower,swing.axisRemapUpper,aircraft.controlInputs.customAxis1);
            swing.positionPrev=input;
            if(swing.lockingControlSurfaces!=null)foreach(var surface in swing.lockingControlSurfaces)if(surface!=null)surface.SetLocked(input>0);
            foreach(var rotator in swing.rotators)
            {
                if(rotator.part==null||rotator.part.IsDetached()||rotator.condition<=0)continue;
                float angle=Mathf.Lerp(rotator.minAngle,rotator.maxAngle,input),change=angle-rotator.currentAngle;
                rotator.currentAngle=angle;
                if(rotator.counterRotators!=null)foreach(var counter in rotator.counterRotators)if(counter!=null)counter.Rotate(0,0,-change,Space.Self);
                // The native simple-physics branch directly sets anchor geometry.
                rotator.Animate(input,out _);
            }
        }
    }
    public void ApplyLegacyTilt()
    {
        if(aircraft.controlInputs==null)return;
        foreach(var tilt in tilts)
        {
            if(tilt==null||tilt.rotatingJoints==null)continue;
            tilt.tiltCorrectionPosition=aircraft.controlInputs.customAxis1;
            foreach(var rotor in tilt.rotatingJoints)
            {
                if(rotor.part==null||rotor.part.IsDetached())continue;
                float yaw=Mathf.Clamp01(1f-rotor.currentAngle*.03f),amount=rotor.maxAngle!=0?rotor.currentAngle/rotor.maxAngle:0;
                float input=rotor.yawFactor*aircraft.controlInputs.yaw*yaw+rotor.pitchFactor*(amount>.2f&&amount<.7f?1:0)*aircraft.controlInputs.pitch+tilt.tiltCorrectionPosition;
                rotor.currentAngle=Mathf.Lerp(rotor.minAngle,rotor.maxAngle,input);rotor.Animate(aircraft.controlInputs,aircraft.speed,tilt.tiltCorrectionPosition);
            }
        }
    }
    public void UpdateTiltLinkages()
    {
        foreach(var tilt in tilts)if(tilt!=null&&tilt.rotatingJoints!=null)foreach(var rotor in tilt.rotatingJoints)
            if(rotor.part!=null&&!rotor.part.IsDetached()&&rotor.links!=null)foreach(var link in rotor.links)if(link.linkage!=null&&link.stretchTo!=null)link.Animate();
    }
    public void ApplyLegacyTailHooks()
    {
        foreach(var hook in hooks)if(hook!=null&&hook.unitPart!=null&&!hook.unitPart.IsDetached()&&hook.hook!=null)
        {
            hook.CheckDeployConditions();hook.deployedAmount=hook.deployed?1:0;
            hook.hook.localEulerAngles=new Vector3(Mathf.Lerp(hook.stowedAngle,hook.deployedAngle,hook.deployedAmount),0,0);
        }
    }
}
