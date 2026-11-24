using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReplayMod.Core;

// Physical, selected-aircraft screens only. Shared render textures, native HUD
// controllers, the local player and tactical sensor state are never changed.
internal sealed class ReplayCockpitFlightScreens
{
    private sealed class Slot
    {
        public Renderer Renderer;public int Index;public Material Material;
        public bool TextureOverride,ColorOverride;public Texture OriginalTexture;public Color OriginalColor;
    }
    private readonly List<Slot> slots=new List<Slot>();
    private readonly uint[] raster=new uint[ReplayFlightInstrumentDisplay.Size*ReplayFlightInstrumentDisplay.Size];
    private readonly Color32[] pixels=new Color32[ReplayFlightInstrumentDisplay.Size*ReplayFlightInstrumentDisplay.Size];
    private readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
    private readonly List<Material> materialScratch=new List<Material>();
    private static readonly int EmissionMap=Shader.PropertyToID("_EmissionMap"),EmissionColor=Shader.PropertyToID("_EmissionColor");
    private Aircraft owner;private Texture2D texture;private double lastTime=double.NaN;
    public int Count=>slots.Count;
    public void Update(Player playback,Aircraft aircraft,double time)
    {
        if(aircraft==null){Clear();return;}
        if(!ReferenceEquals(owner,aircraft)){Clear();owner=aircraft;Bind(aircraft);}
        if(slots.Count==0)return;
        if(!double.IsNaN(lastTime)&&time>=lastTime&&time-lastTime<.1)return;
        lastTime=time;
        var inputs=aircraft.GetInputs();ReplayMod.Data.PositionSnapshot pose=default;bool known=playback!=null&&playback.TryGetRecordedPose(aircraft,out pose);
        var frame=known?new ReplayFlightInstrumentFrame(Math.Sqrt((double)pose.velocity.x*pose.velocity.x+(double)pose.velocity.z*pose.velocity.z),pose.position.y,pose.rotation.eulerAngles.y,pose.velocity.y,inputs!=null?inputs.throttle:0,pose.rotation.eulerAngles.x,pose.rotation.eulerAngles.z,aircraft.gearDeployed):
            new ReplayFlightInstrumentFrame(double.NaN,0,0,0,0,0,0,false);
        ReplayFlightInstrumentDisplay.Render(frame,raster);
        for(int n=0;n<raster.Length;n++){uint value=raster[n];pixels[n]=new Color32((byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value);}
        texture.SetPixels32(pixels);texture.Apply(false,false);
        foreach(var slot in slots)
        {
            if(slot.Renderer==null)continue;
            slot.Renderer.GetSharedMaterials(materialScratch);
            if(slot.Index>=materialScratch.Count||!ReferenceEquals(materialScratch[slot.Index],slot.Material))continue;
            block.Clear();slot.Renderer.GetPropertyBlock(block,slot.Index);
            if(block.isEmpty)slot.Renderer.GetPropertyBlock(block);
            block.SetTexture(EmissionMap,texture);block.SetColor(EmissionColor,Color.white*1.5f);slot.Renderer.SetPropertyBlock(block,slot.Index);
        }
    }
    private void Bind(Aircraft aircraft)
    {
        foreach(var renderer in aircraft.GetComponentsInChildren<Renderer>(true))
        {
            if(renderer==null||slots.Count>=16)continue;
            var part=renderer.GetComponentInParent<UnitPart>(true);
            if(part!=null&&(part.parentUnit!=aircraft||part.IsDetached()||part.fragmented))continue;
            var materials=renderer.sharedMaterials;
            for(int index=0;index<materials.Length&&index<16&&slots.Count<16;index++)
            {
                var material=materials[index];
                // Match the native tactical-screen render texture, not names of
                // arbitrary dark panels or target-video materials.
                if(material==null||!material.HasProperty(EmissionMap)||!material.HasProperty(EmissionColor)||!(material.GetTexture(EmissionMap) is RenderTexture source)||source.name!="TacScreenRenderer")continue;
                block.Clear();renderer.GetPropertyBlock(block,index);
                if(block.isEmpty)renderer.GetPropertyBlock(block);
                slots.Add(new Slot {Renderer=renderer,Index=index,Material=material,TextureOverride=block.HasProperty(EmissionMap),
                    ColorOverride=block.HasProperty(EmissionColor),OriginalTexture=block.GetTexture(EmissionMap),OriginalColor=block.GetColor(EmissionColor)});
            }
        }
        if(slots.Count>0)texture=new Texture2D(ReplayFlightInstrumentDisplay.Size,ReplayFlightInstrumentDisplay.Size,TextureFormat.RGBA32,false)
            {name="Replay selected flight instruments",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
    }
    public void Clear()
    {
        foreach(var slot in slots)
        {
            if(slot.Renderer==null)continue;
            try
            {
                slot.Renderer.GetSharedMaterials(materialScratch);
                if(slot.Index>=materialScratch.Count)continue;
                var currentMaterial=materialScratch[slot.Index];
                block.Clear();slot.Renderer.GetPropertyBlock(block,slot.Index);
                // Preserve any newer screen owner and unrelated property changes.
                if(block.GetTexture(EmissionMap)!=texture)continue;
                block.SetTexture(EmissionMap,slot.TextureOverride?slot.OriginalTexture:currentMaterial!=null&&currentMaterial.HasProperty(EmissionMap)?currentMaterial.GetTexture(EmissionMap):null);
                block.SetColor(EmissionColor,slot.ColorOverride?slot.OriginalColor:currentMaterial!=null&&currentMaterial.HasProperty(EmissionColor)?currentMaterial.GetColor(EmissionColor):Color.white);
                slot.Renderer.SetPropertyBlock(block,slot.Index);
            }
            catch(Exception error){Plugin.SafeLogWarning("Replay physical flight display restore skipped: "+error.Message);}
        }
        slots.Clear();materialScratch.Clear();owner=null;lastTime=double.NaN;
        if(texture!=null)UnityEngine.Object.Destroy(texture);texture=null;
    }
}
