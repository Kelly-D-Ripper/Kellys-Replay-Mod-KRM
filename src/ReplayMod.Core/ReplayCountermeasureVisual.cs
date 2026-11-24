using System;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

// Owns only the native visual prefab. IRFlare/RadarChaff never run, so replay
// countermeasures cannot register guidance targets, collide or deploy weapons.
internal sealed class ReplayCountermeasureVisual : IDisposable
{
    private readonly GameObject root;
    private readonly ParticleSystem[] systems;
    private readonly bool[] emission;
    private readonly ParticleSystem smoke;
    private readonly Light flareLight;
    private readonly float emitFrequency,minSpeed,startSpeed;
    private double lastTime=double.NaN;
    private double emitCounter;
    private uint random;
    internal ReplayCountermeasureVisual(GameObject prefab,CountermeasureTimeline timeline)
    {
        root=new GameObject("Replay countermeasure "+timeline.metadata.countermeasureId);root.SetActive(false);
        root.transform.SetParent(Datum.origin,false);
        try
        {
            var instance=UnityEngine.Object.Instantiate(prefab,root.transform);
            instance.transform.localPosition=Vector3.zero;instance.transform.localRotation=Quaternion.identity;
            var flare=instance.GetComponent<IRFlare>();var chaff=instance.GetComponent<RadarChaff>();
            // Inactive parenting prevents OnEnable before this native script is
            // disabled; in particular native LaunchFlare is never called.
            if(flare!=null)
            {
                flare.enabled=false;smoke=flare.smokeParticles;flareLight=flare.flareLight;
                emitFrequency=Mathf.Min(60,Mathf.Max(0,flare.emitFrequency));minSpeed=Mathf.Max(.01f,flare.minSpeed);
                if(smoke!=null)
                {var main=smoke.main;main.simulationSpace=ParticleSystemSimulationSpace.Custom;main.customSimulationSpace=Datum.origin;startSpeed=main.startSpeed.constant;}
            }
            if(chaff!=null)chaff.enabled=false;
            foreach(var native in instance.GetComponentsInChildren<IRFlare>(true))native.enabled=false;
            foreach(var native in instance.GetComponentsInChildren<RadarChaff>(true))native.enabled=false;
            foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {body.isKinematic=true;body.detectCollisions=false;body.useGravity=false;}
            systems=instance.GetComponentsInChildren<ParticleSystem>(true);emission=new bool[systems.Length];
            for(int i=0;i<systems.Length;i++)
            {
                var main=systems[i].main;main.useUnscaledTime=false;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                if(main.simulationSpace==ParticleSystemSimulationSpace.Custom)main.customSimulationSpace=Datum.origin;
                systems[i].useAutoRandomSeed=false;systems[i].randomSeed=timeline.metadata.countermeasureId*2654435761u+(uint)i+1;
                emission[i]=systems[i]!=smoke&&systems[i].emission.enabled;
                var emit=systems[i].emission;emit.enabled=false;
                systems[i].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if(flareLight!=null)flareLight.enabled=false;
            ReplayVfxPlayback.Track(root);root.SetActive(true);
            foreach(var system in systems)system.Pause(false);
            random=timeline.metadata.countermeasureId|1u;
        }
        catch{root.SetActive(false);UnityEngine.Object.Destroy(root);throw;}
    }
    internal void Update(CountermeasureTimeline timeline,double time)
    {
        if(root==null||!timeline.TryAt(time,out var pose))return;
        if(double.IsNaN(lastTime)||time<lastTime-.00001||time-lastTime>.5)
        {
            foreach(var system in systems)if(system!=null)system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            emitCounter=0;random=timeline.metadata.countermeasureId|1u;
            lastTime=Math.Max(timeline.FirstTime,time-CountermeasureTimeline.TrailSeconds);
        }
        // A bounded warm-up rebuilds trails from recorded positions, including
        // before a rolling clip's zero point. Normal playback needs one step.
        while(time-lastTime>.000001)
        {
            double next=Math.Min(time,lastTime+.05);
            Advance(timeline,lastTime,next);lastTime=next;
        }
        root.transform.SetPositionAndRotation(pose.position.ToLocalPosition(),pose.rotation);
        if(flareLight!=null)flareLight.enabled=timeline.Burning(time);
    }
    private void Advance(CountermeasureTimeline timeline,double from,double to)
    {
        if(!timeline.TryAt(to,out var pose))return;
        float dt=(float)(to-from);bool burning=timeline.Burning((from+to)*.5);
        root.transform.SetPositionAndRotation(pose.position.ToLocalPosition(),pose.rotation);
        if(smoke!=null&&burning)
        {
            float speed=pose.velocity.magnitude;
            emitCounter+=dt*emitFrequency*Mathf.Min(1,speed/minSpeed);
            int count=Math.Min(8,(int)emitCounter);emitCounter-=count;
            for(int i=0;i<count;i++)
            {
                double emittedAt=from+(to-from)*(i+.5)/count;
                if(!timeline.TryAt(emittedAt,out var emitted)||!timeline.Burning(emittedAt))continue;
                float jitter=startSpeed*emitted.velocity.magnitude*.01f;
                var parameters=new ParticleSystem.EmitParams {position=new Vector3(emitted.position.x,emitted.position.y,emitted.position.z),
                    velocity=emitted.velocity+new Vector3(Noise(),Noise(),Noise())*jitter};
                smoke.Emit(parameters,1);
            }
        }
        for(int i=0;i<systems.Length;i++)
        {
            var system=systems[i];if(system==null)continue;
            var emit=system.emission;bool active=burning&&emission[i];if(emit.enabled!=active)emit.enabled=active;
            // Simulate then Pause prevents Unity from advancing a second time.
            // Recorded time, rather than wall time, owns slow motion and seeks.
            system.Play(false);system.Simulate(dt,false,false,false);system.Pause(false);
        }
    }
    private float Noise(){random^=random<<13;random^=random>>17;random^=random<<5;return (random&0xffff)/32767.5f-1;}
    public void Dispose()
    {
        if(root==null)return;root.SetActive(false);
        foreach(var system in systems)if(system!=null)system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        UnityEngine.Object.Destroy(root);
    }
}
