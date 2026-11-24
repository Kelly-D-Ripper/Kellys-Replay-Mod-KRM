using System.Collections.Generic;
using UnityEngine;

namespace ReplayMod.Core;

// Legacy/disintegration effects retain their native fade and materials, but a
// frozen replay Rigidbody must not be mistaken for a culled physical parent.
public static class ReplayDamageParticleMotion
{
    private sealed class Effect
    {internal UnitPart part;internal Vector3 position;internal double time;internal float speed;}
    private const int MaxEffects=2048;
    private static readonly Dictionary<DamageParticles,Effect> effects=new Dictionary<DamageParticles,Effect>();
    private static readonly List<DamageParticles> stale=new List<DamageParticles>();
    public static bool CanOwn(DamageParticles effect)
    {
        if(!ReplayRuntime.IsPlayback||effect==null||ReplayFuelFlameVisual.IsControlled(effect))return false;
        var part=effect.GetComponentInParent<UnitPart>();
        return part!=null&&ReplayVfxPlayback.IsOwnedActor(part.parentUnit)&&(effects.ContainsKey(effect)||effects.Count<MaxEffects);
    }
    public static void Register(DamageParticles effect,UnitPart part)
    {
        if(effect==null||part==null||effects.Count>=MaxEffects||ReplayFuelFlameVisual.IsControlled(effect))return;
        double time=ReplayManager.i?.Playback?.currentVirtualTime??0;
        var state=new Effect {part=part,position=part.transform.position,time=time,speed=Mathf.Max(0,part.parentUnit?.speed??0)};
        effects[effect]=state;Apply(effect,state);
        foreach(var behavior in effect.systemBehaviours)behavior.Update(effect.time,effect.speed);
    }
    private static void Apply(DamageParticles effect,Effect state)
    {effect.unitPart=state.part;effect.rb=null;effect.detached=true;effect.speed=state.speed;effect.fireDamage=0;}
    public static bool Keep(DamageParticles effect)
    {
        if(!effects.TryGetValue(effect,out var state)||state.part==null)return false;
        var part=state.part;return ReplayRuntime.IsPlayback&&ReplayVfxPlayback.IsOwnedActor(part.parentUnit)&&!part.fragmented&&part.gameObject.activeInHierarchy&&part.transform.position.y>Datum.LocalSeaY;
    }
    public static void Refresh(double time)
    {
        stale.Clear();
        foreach(var pair in effects)
        {
            var effect=pair.Key;var state=pair.Value;
            if(effect==null||state.part==null){stale.Add(effect);continue;}
            var position=state.part.transform.position;double dt=time-state.time;
            if(dt>.00001&&dt<.5)
            {
                var velocity=(position-state.position)/(float)dt;
                var level=NetworkSceneSingleton<LevelInfo>.i;
                if(level!=null)velocity-=level.GetWind(position.ToGlobalPosition());
                float speed=velocity.magnitude;if(ReplayVfxPolicy.IsFinite(speed))state.speed=Mathf.Min(speed,100000);
            }
            if(dt>.00001){state.time=time;state.position=position;}
            Apply(effect,state);
        }
        foreach(var effect in stale)effects.Remove(effect);
    }
    public static void Prepare(DamageParticles effect){if(effects.TryGetValue(effect,out var state))Apply(effect,state);}
    public static void Clear(){effects.Clear();stale.Clear();}
}
