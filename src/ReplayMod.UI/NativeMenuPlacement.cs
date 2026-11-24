using System;

namespace ReplayMod.UI;

// Finds room without shifting native controls. If no unobstructed slot exists,
// leave the menu alone; the configured studio shortcut remains available.
public static class NativeMenuPlacement
{
    public static bool TryFind(float width,float height,OverlayBox source,OverlayBox[] occupied,out OverlayBox result)
    {
        result=default;
        if(!Finite(width)||!Finite(height)||!Finite(source.X)||!Finite(source.Y)||!Finite(source.Width)||!Finite(source.Height)||
            width<=0||height<=0||source.Width<40||source.Height<20)return false;
        occupied=occupied??Array.Empty<OverlayBox>();
        float bottom=source.Y+source.Height;
        foreach(var item in occupied)
            if(item.X<source.X+source.Width&&item.X+item.Width>source.X)bottom=Math.Max(bottom,item.Y+item.Height);
        var candidates=new[]{
            new OverlayBox(source.X,bottom+10,source.Width,source.Height),
            new OverlayBox(source.X+source.Width+14,source.Y,source.Width,source.Height),
            new OverlayBox(source.X-source.Width-14,source.Y,source.Width,source.Height),
            new OverlayBox(width-source.Width-20,height-source.Height-20,source.Width,source.Height),
            new OverlayBox(width-source.Width-20,20,source.Width,source.Height)};
        foreach(var candidate in candidates)
        {
            if(candidate.X<8||candidate.Y<8||candidate.X+candidate.Width>width-8||candidate.Y+candidate.Height>height-8)continue;
            bool clear=true;
            foreach(var item in occupied)
                if(Overlaps(candidate,item,6)){clear=false;break;}
            if(clear){result=candidate;return true;}
        }
        return false;
    }
    private static bool Overlaps(OverlayBox a,OverlayBox b,float pad)=>
        a.X<b.X+b.Width+pad&&a.X+a.Width+pad>b.X&&a.Y<b.Y+b.Height+pad&&a.Y+a.Height+pad>b.Y;
    private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
}
