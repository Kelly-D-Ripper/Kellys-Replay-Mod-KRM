using System;
using System.Collections.Generic;
using ReplayMod.Data;
using UnityEngine;

namespace ReplayMod.Core;

internal sealed class ReplayFuelFlameVisual:IDisposable
{
    private static readonly HashSet<DamageParticles> controlled=new HashSet<DamageParticles>();
    private readonly GameObject root;
    private readonly DamageParticles damage;
    private readonly ParticleSystem[] allSystems;
    private readonly bool[] active;
    private bool warmed;
    internal static bool IsControlled(DamageParticles target)=>target!=null&&controlled.Contains(target);
    internal ReplayFuelFlameVisual(GameObject prefab,FuelFlameSnapshot initial,uint id)
    {
        root=new GameObject("Replay fuel fire "+id);root.SetActive(false);root.transform.SetParent(Datum.origin,false);
        try
        {
            var instance=UnityEngine.Object.Instantiate(prefab,root.transform);instance.transform.localPosition=Vector3.zero;instance.transform.localRotation=Quaternion.identity;
            damage=instance.GetComponent<DamageParticles>();if(damage==null||damage.systemBehaviours==null||damage.systemBehaviours.Length!=initial.opacity.Length)throw new InvalidOperationException("Recorded fuel fire prefab no longer matches particle layout");
            if(instance.GetComponentsInChildren<DamageParticles>(true).Length!=1)throw new InvalidOperationException("Recorded fuel fire prefab has an unsupported nested damage component");
            controlled.Add(damage);damage.enabled=false;damage.fireDamage=0;damage.rb=null;damage.unitPart=null;damage.detached=true;
            allSystems=instance.GetComponentsInChildren<ParticleSystem>(true);active=new bool[damage.systemBehaviours.Length];
            for(int i=0;i<allSystems.Length;i++)
            {
                var main=allSystems[i].main;main.playOnAwake=false;main.useUnscaledTime=false;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                if(main.simulationSpace==ParticleSystemSimulationSpace.Custom)main.customSimulationSpace=Datum.origin;
                allSystems[i].useAutoRandomSeed=false;allSystems[i].randomSeed=id*2654435761u+(uint)i+1;
                allSystems[i].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach(var system in damage.systemBehaviours)system.Initialize(initial.speed);
            foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var body in instance.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.detectCollisions=false;body.useGravity=false;}
            ReplayVfxPlayback.Track(root);root.SetActive(true);
        }
        catch{if(damage!=null)controlled.Remove(damage);root.SetActive(false);UnityEngine.Object.Destroy(root);throw;}
    }
    internal void Apply(FuelFlameSnapshot state,Transform anchor)
    {
        if(root==null)return;
        var position=state.attached&&anchor!=null?anchor.TransformPoint(state.localPosition):state.position.ToLocalPosition();
        var rotation=state.attached&&anchor!=null?anchor.rotation*state.localRotation:state.rotation;
        root.transform.SetPositionAndRotation(position,rotation);
        for(int i=0;i<damage.systemBehaviours.Length;i++)
        {
            var behavior=damage.systemBehaviours[i];var system=behavior.system;if(system==null)continue;
            bool emitting=(state.emittingMask&(1u<<i))!=0;
            var emission=system.emission;emission.rateOverTime=behavior.baseRate+state.speed*behavior.increaseRateWithSpeed;
            float life=1f/Mathf.Max(state.speed*behavior.reduceLifeWithSpeed,1);var main=system.main;
            main.startLifetime=new ParticleSystem.MinMaxCurve(behavior.lowerLifetime*life,behavior.upperLifetime*life);
            float opacity=Mathf.Clamp01(state.opacity[i])/(1+behavior.reduceOpacityWithSpeed*state.speed*.01f);
            main.startColor=new ParticleSystem.MinMaxGradient(behavior.lowerColor*new Color(1,1,1,opacity),behavior.upperColor*new Color(1,1,1,opacity));
            if(emitting!=active[i]){if(emitting)system.Play(false);else system.Stop(false,ParticleSystemStopBehavior.StopEmitting);active[i]=emitting;}
            else if(!emitting&&system.isEmitting)system.Stop(false,ParticleSystemStopBehavior.StopEmitting);
        }
        if(!warmed)
        {
            // Apply observed colours/lifetimes first. A stopped effect has no
            // captured particle inventory, so a direct seek must not invent a
            // fresh opaque fire; playback/pre-roll preserves its existing tail.
            for(int i=0;i<damage.systemBehaviours.Length;i++)
            {
                var system=damage.systemBehaviours[i].system;if(system==null)continue;
                float warm=FuelFlameWarmPolicy.Duration(state,i);
                if(warm>0){system.Simulate(warm,false,true,false);system.Play(false);}
            }
            warmed=true;
        }
        if(damage.fireLight!=null){damage.fireLight.enabled=state.lightIntensity>0;damage.fireLight.intensity=state.lightIntensity;}
    }
    public void Dispose()
    {if(damage!=null)controlled.Remove(damage);if(root==null)return;root.SetActive(false);foreach(var system in allSystems)if(system!=null)system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);UnityEngine.Object.Destroy(root);}
}
