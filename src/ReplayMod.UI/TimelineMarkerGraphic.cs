using System;
using System.Collections.Generic;
using ReplayMod.Director;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.UI;

internal sealed class TimelineMarkerGraphic : MaskableGraphic
{
    private TimelineTick[] ticks=Array.Empty<TimelineTick>();
    private float[] cameraKeys=Array.Empty<float>();
    private Color manualTint=new Color32(88,225,135,242);
    internal void SetAccent(Color value)
    {value.a=.95f;if(manualTint==value)return;manualTint=value;SetVerticesDirty();}
    internal void SetTicks(TimelineTick[] values){ticks=values??Array.Empty<TimelineTick>();SetVerticesDirty();}
    internal void SetCameraKeys(IReadOnlyList<CameraKey> keys,double duration)
    {
        int count=duration>0&&keys!=null?keys.Count:0;
        bool changed=count!=cameraKeys.Length;
        for(int i=0;!changed&&i<count;i++)
            if((keys[i]==null?0:Mathf.Clamp01((float)(keys[i].time/duration)))!=cameraKeys[i])changed=true;
        if(!changed)return;
        var values=new float[count];
        for(int i=0;i<count;i++)values[i]=keys[i]==null?0:Mathf.Clamp01((float)(keys[i].time/duration));
        cameraKeys=values;SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();var bounds=rectTransform.rect;
        foreach(var tick in ticks)
        {
            float x=bounds.xMin+tick.Position*bounds.width,w=tick.Manual?3:2,h=tick.Manual?9:6;
            x=Mathf.Clamp(x-w*.5f,bounds.xMin,bounds.xMax-w);float y=bounds.yMin+1;
            Color tint=tick.Manual?manualTint:new Color(.95f,.71f,.36f,.8f);
            int start=mesh.currentVertCount;
            mesh.AddVert(new Vector3(x,y),tint,Vector2.zero);mesh.AddVert(new Vector3(x,y+h),tint,Vector2.zero);
            mesh.AddVert(new Vector3(x+w,y+h),tint,Vector2.zero);mesh.AddVert(new Vector3(x+w,y),tint,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
        foreach(var position in cameraKeys)
        {
            float x=Mathf.Clamp(bounds.xMin+position*bounds.width,bounds.xMin+4,bounds.xMax-4),y=bounds.yMax-5;
            Color tint=new Color(.45f,.85f,1f,1f);int start=mesh.currentVertCount;
            mesh.AddVert(new Vector3(x,y+4),tint,Vector2.zero);mesh.AddVert(new Vector3(x+4,y),tint,Vector2.zero);
            mesh.AddVert(new Vector3(x,y-4),tint,Vector2.zero);mesh.AddVert(new Vector3(x-4,y),tint,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
    }
}
