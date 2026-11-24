using System.Collections.Generic;
using ReplayMod.Data;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

namespace ReplayMod.Core;

// A building remains the same replay actor throughout collapse/repair. Native
// async Collapse destroys roots and runs network/physics work that cannot be
// rewound, so presentation is evaluated directly from the recorded clock.
public sealed class ReplayBuildingPresentation
{
    private sealed class BuildingVisual
    {
        internal Building actor;
        // Recorded spawn orientation. Map-placed buildings never set native
        // startRotation, so recordings before this fix stored identity there.
        internal Quaternion rotation;
        internal Renderer[] meshes;
        internal bool[] meshEnabled;
        internal Collider[] colliders;
        internal bool[] colliderEnabled;
        internal BuildingStateSnapshot state;
        internal bool legacyHidden, changed, queued, lastHidden, lastDisabled;
        internal double lastStatus=-1;
    }
    private readonly UnitController controller;
    private readonly Dictionary<uint,BuildingVisual> buildings=new Dictionary<uint,BuildingVisual>();
    private readonly Queue<BuildingVisual> dirty=new Queue<BuildingVisual>();
    public ReplayBuildingPresentation(UnitController controller){this.controller=controller;}
    public void Track(uint id,Unit unit)
    {
        Forget(id);
        if(!(unit is Building building))return;
        var all=building.GetComponentsInChildren<Renderer>(true);var meshes=new List<Renderer>();
        foreach(var renderer in all)if(renderer is MeshRenderer||renderer is SkinnedMeshRenderer)meshes.Add(renderer);
        var binding=new BuildingVisual {actor=building,rotation=building.transform.rotation,meshes=meshes.ToArray(),colliders=building.GetComponentsInChildren<Collider>(true)};
        binding.meshEnabled=new bool[binding.meshes.Length];binding.colliderEnabled=new bool[binding.colliders.Length];
        for(int n=0;n<binding.meshes.Length;n++)binding.meshEnabled[n]=binding.meshes[n].enabled;
        for(int n=0;n<binding.colliders.Length;n++)binding.colliderEnabled[n]=binding.colliders[n].enabled;
        buildings[id]=binding;
    }
    public void Apply(BuildingStateEvent sample)
    {
        if(!sample.HasValidPayload()||!buildings.TryGetValue(sample.unitId,out var binding)||!Exact(sample.unitId,binding))return;
        if(binding.state!=null&&sample.Time<binding.state.time)return;
        binding.state=BuildingStateSnapshot.From(sample);binding.legacyHidden=false;
        binding.actor.disabled=sample.disabled;
        MarkDirty(binding);
    }
    public void ApplyLegacy(UnitStatusEvent sample)
    {
        if(!buildings.TryGetValue(sample.unitId,out var binding)||!Exact(sample.unitId,binding)||binding.state!=null||sample.Time<binding.lastStatus)return;
        binding.lastStatus=sample.Time;binding.legacyHidden=sample.disabled;
        MarkDirty(binding);
    }
    private void MarkDirty(BuildingVisual binding){if(binding.queued)return;binding.queued=true;dirty.Enqueue(binding);}
    private bool Exact(uint id,BuildingVisual binding)=>binding.actor!=null&&ReferenceEquals(controller.FindActor(id),binding.actor);
    public bool TryPose(uint id,Unit actor,double time)
    {
        if(!BuildingStateSnapshot.Finite(time)||!buildings.TryGetValue(id,out var binding)||!ReferenceEquals(binding.actor,actor)||binding.state==null)return false;
        var sample=binding.state;
        var point=sample.anchor.ToLocalPosition()-Vector3.up*sample.DropAt(time);
        actor.transform.SetPositionAndRotation(point,binding.rotation);
        if(actor.rb!=null){actor.rb.position=point;actor.rb.rotation=binding.rotation;}
        ApplyVisibility(binding,sample.HiddenAt(time),sample.disabled);
        return true;
    }
    public void Render(double time)
    {
        if(!BuildingStateSnapshot.Finite(time))return;
        // Only changed states need a visibility pass. Continuous collapse pose
        // is already substituted in UnitController.MoveUnit's existing loop.
        while(dirty.Count>0)
        {
            var binding=dirty.Dequeue();binding.queued=false;
            if(binding.actor==null||!controller.OwnsReplayUnit(binding.actor))continue;
            ApplyVisibility(binding,binding.state?.HiddenAt(time)??binding.legacyHidden,binding.state?.disabled??binding.legacyHidden);
        }
    }
    private static void ApplyVisibility(BuildingVisual binding,bool hidden,bool disableCollision)
    {
        if(binding.lastHidden==hidden&&binding.lastDisabled==disableCollision)return;
        binding.lastHidden=hidden;binding.lastDisabled=disableCollision;
        if(hidden||disableCollision)binding.changed=true;
        if(!binding.changed)return;
        for(int n=0;n<binding.meshes.Length;n++)if(binding.meshes[n]!=null)binding.meshes[n].enabled=!hidden&&binding.meshEnabled[n];
        for(int n=0;n<binding.colliders.Length;n++)if(binding.colliders[n]!=null)binding.colliders[n].enabled=!disableCollision&&binding.colliderEnabled[n];
        if(!hidden&&!disableCollision)binding.changed=false;
    }
    public void Forget(uint id)
    {
        if(!buildings.TryGetValue(id,out var binding))return;
        if(Exact(id,binding))ApplyVisibility(binding,false,false);
        buildings.Remove(id);
    }
    public void Clear()
    {
        foreach(var pair in buildings)if(Exact(pair.Key,pair.Value))ApplyVisibility(pair.Value,false,false);
        buildings.Clear();dirty.Clear();
    }
}
