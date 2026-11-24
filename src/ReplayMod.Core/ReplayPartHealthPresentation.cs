using System;
using System.Collections.Generic;
using System.IO;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

// Main-thread, per-controller presentation cache. Restoring recorded HP must
// not invoke live damage callbacks, instantiate old effects, fragment parts,
// or clone each damaged renderer's material during a seek/recording startup.
public sealed class ReplayPartHealthPresentation
{
    private static readonly int HitPointsProperty=Shader.PropertyToID("_HitPoints");
    private readonly List<Material> materials=new List<Material>(8);
    private MaterialPropertyBlock properties;
    private int warnings;
    public bool TryRestoreSnapshot(UnitPart part,PartDamageEvent sample)
    {
        if(sample==null)throw new ArgumentNullException(nameof(sample));
        if(!ReplayDamageStatePolicy.Valid(sample.hitPoints,sample.pierce,sample.blast,sample.fire,sample.impact))
            throw new InvalidDataException("Replay part health/damage is non-finite (actor "+sample.unitId+", part "+sample.partId+")");
        if(!ReplayDamageStatePolicy.IsAbsoluteSnapshot(sample.pierce,sample.blast,sample.fire,sample.impact))return false;
        if(part==null)return true;
        part.hitPoints=sample.hitPoints;
        RefreshDamageShader(part,sample);
        return true;
    }
    public void RefreshDamageShader(UnitPart part,PartDamageEvent sample)
    {
        if(part==null)return;
        if(!ReplayDamageStatePolicy.Finite(part.hitPoints))
            throw new InvalidDataException("Replay part damage produced non-finite health (actor "+(sample?.unitId??0)+", part "+(sample?.partId??0)+")");
        var renderers=part.damageMaterial?.renderers;
        if(renderers==null)return;
        for(int n=0;n<renderers.Count;n++)
        {
            var renderer=renderers[n];if(renderer==null)continue;
            try{ApplyHealth(renderer,part.hitPoints);}
            catch(Exception error)
            {
                if(warnings>=8)continue;warnings++;
                string message=error.Message??error.GetType().Name;if(message.Length>180)message=message.Substring(0,180);
                try{Plugin.logger?.LogWarning("Replay HP restored; optional damage shader unavailable for actor "+(sample?.unitId??0)+", part "+(sample?.partId??0)+": "+message);}catch(Exception){}
            }
            finally{properties?.Clear();materials.Clear();}
        }
    }
    private void ApplyHealth(Renderer renderer,float hitPoints)
    {
        if(properties==null)properties=new MaterialPropertyBlock();
        properties.Clear();renderer.GetPropertyBlock(properties);properties.SetFloat(HitPointsProperty,hitPoints);renderer.SetPropertyBlock(properties);
        // A pre-existing per-material block takes precedence over the renderer
        // block. Preserve all its values and set HP there too; do not create a
        // new per-material override that would mask later renderer-wide inputs.
        renderer.GetSharedMaterials(materials);
        for(int index=0;index<materials.Count;index++)
        {
            properties.Clear();renderer.GetPropertyBlock(properties,index);
            if(properties.isEmpty)continue;
            properties.SetFloat(HitPointsProperty,hitPoints);renderer.SetPropertyBlock(properties,index);
        }
    }
}
