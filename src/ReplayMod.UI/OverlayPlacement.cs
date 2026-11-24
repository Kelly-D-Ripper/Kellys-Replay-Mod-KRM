using System;

namespace ReplayMod.UI;

// Canvas-space bounds, measured from its top-left corner. Both descriptions and
// shortcut prompts use this so scaling never sends them outside the viewport.
public readonly struct OverlayBox
{
    public readonly float X,Y,Width,Height;
    public OverlayBox(float x,float y,float width,float height){X=x;Y=y;Width=width;Height=height;}
}
public static class OverlayPlacement
{
    public static OverlayBox NearPointer(float width,float height,float pointerX,float pointerY,float requestedWidth,float requestedHeight)
    {
        width=Math.Max(1,Safe(width,1));height=Math.Max(1,Safe(height,1));
        float margin=Math.Min(8,Math.Min(width,height)*.25f);
        float w=Math.Min(Safe(requestedWidth,400),width-margin*2);
        float h=Math.Min(Safe(requestedHeight,84),height-margin*2);
        float x=Safe(pointerX,0)+18,y=Safe(pointerY,0)-h-18;
        if(x+w>width-margin)x=Safe(pointerX,0)-w-18;
        if(y<margin)y=Safe(pointerY,0)+18;
        return Clamp(width,height,x,y,w,h,margin);
    }
    public static OverlayBox AboveBar(float width,float height,float requestedWidth,float requestedHeight,float barHeight=188)
    {
        width=Math.Max(1,Safe(width,1));height=Math.Max(1,Safe(height,1));
        float margin=Math.Min(8,Math.Min(width,height)*.25f);
        float w=Math.Min(Safe(requestedWidth,600),width-margin*2);
        float h=Math.Min(Safe(requestedHeight,128),height-margin*2);
        return Clamp(width,height,(width-w)*.5f,height-Safe(barHeight,188)-h-20,w,h,margin);
    }
    private static OverlayBox Clamp(float width,float height,float x,float y,float w,float h,float margin)
        =>new OverlayBox(Math.Max(margin,Math.Min(width-w-margin,x)),Math.Max(margin,Math.Min(height-h-margin,y)),w,h);
    private static float Safe(float value,float fallback)=>float.IsNaN(value)||float.IsInfinity(value)||value<0?fallback:value;
}
