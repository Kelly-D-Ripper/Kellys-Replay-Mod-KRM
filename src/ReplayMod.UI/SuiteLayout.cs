using System;

namespace ReplayMod.UI;

// UI geometry stays independent of Unity so the rendered dock and offline
// viewport checks use the same dimensions. Dock children are top-left local;
// Dock and Popout are top-left positions in the complete canvas.
public sealed class SuiteLayoutPlan
{
    public float Width,Height,DockHeight;
    public OverlayBox Dock,Title,Time,Status,Timeline;
    public OverlayBox Play,Pause,FrameBack,FrameNext,SpeedLess,Speed,SpeedMore,SpeedText;
    public OverlayBox HideUi,Settings,Popout;
    public OverlayBox[] Navigation;
}

public static class SuiteLayout
{
    public const float ReferenceWidth=1920,ReferenceHeight=1080;
    public const float DefaultDockHeight=132,PanelGap=10;
    public const int WorkspaceCount=7;
    private const float Margin=12,Gap=6,ControlY=78,ControlHeight=40;

    // Equivalent to this suite's CanvasScaler Expand policy. The portrait and
    // lower-resolution cases retain readable reference-space controls.
    public static OverlayBox CanvasForScreen(float screenWidth,float screenHeight)
    {
        RequirePositive(screenWidth,nameof(screenWidth));RequirePositive(screenHeight,nameof(screenHeight));
        float scale=Math.Min(screenWidth/ReferenceWidth,screenHeight/ReferenceHeight);
        return new OverlayBox(0,0,screenWidth/scale,screenHeight/scale);
    }

    public static SuiteLayoutPlan Calculate(float width,float height)
    {
        RequirePositive(width,nameof(width));RequirePositive(height,nameof(height));
        if(width<320||height<240)throw new ArgumentOutOfRangeException("viewport","The replay tools require at least 320 by 240 canvas units.");
        var result=new SuiteLayoutPlan {Width=width,Height=height,Navigation=new OverlayBox[WorkspaceCount]};
        bool compact=width<1700;
        float[] widths={compact?84:96,compact?84:96,compact?36:40,compact?36:40,32,compact?102:140,32,compact?52:58};
        var transport=new OverlayBox[widths.Length];
        float navigationWidth=width>=1536&&width<1700?88:96;
        float dockHeight=DefaultDockHeight;
        if(width>=1536)
        {
            float x=Margin;
            for(int i=0;i<widths.Length;i++){transport[i]=Control(x,ControlY,widths[i]);x+=widths[i]+(compact?5:Gap);}
            result.Settings=Control(width-Margin-96,ControlY,96);
            result.HideUi=Control(result.Settings.X-Gap-160,ControlY,160);
            navigationWidth=Math.Min(navigationWidth,(result.HideUi.X-Gap-x-(WorkspaceCount-1)*Gap)/WorkspaceCount);
            float navigationTotal=WorkspaceCount*navigationWidth+(WorkspaceCount-1)*Gap;
            float navigationX=(x+result.HideUi.X-Gap-navigationTotal)*.5f;
            for(int i=0;i<WorkspaceCount;i++)result.Navigation[i]=Control(navigationX+i*(navigationWidth+Gap),ControlY,navigationWidth);
        }
        else
        {
            // Normally Expand keeps the root at least 1920 units wide. Keep a
            // reachable wrapped layout if a different scaler or host supplies
            // a genuinely narrower canvas instead of shrinking its controls.
            float x=Margin,y=ControlY;
            for(int i=0;i<widths.Length;i++)transport[i]=Next(widths[i]);
            for(int i=0;i<WorkspaceCount;i++)result.Navigation[i]=Next(navigationWidth);
            result.HideUi=Next(160);result.Settings=Next(96);
            dockHeight=y+ControlHeight+14;
            OverlayBox Next(float controlWidth)
            {
                if(x+controlWidth>width-Margin){x=Margin;y+=ControlHeight+Gap;}
                var box=Control(x,y,controlWidth);x+=controlWidth+Gap;return box;
            }
        }
        if(dockHeight+PanelGap+20>height)throw new ArgumentOutOfRangeException("viewport","The canvas is too short for the replay tools.");
        result.Play=transport[0];result.Pause=transport[1];result.FrameBack=transport[2];result.FrameNext=transport[3];
        result.SpeedLess=transport[4];result.Speed=transport[5];result.SpeedMore=transport[6];result.SpeedText=transport[7];
        result.DockHeight=dockHeight;result.Dock=new OverlayBox(0,height-dockHeight,width,dockHeight);
        float titleWidth=Math.Min(154,width*.3f),timeX=Margin+titleWidth+10;
        float timeWidth=Math.Min(400,width*.32f),statusX=timeX+timeWidth+16;
        result.Title=new OverlayBox(Margin,8,titleWidth,22);
        result.Time=new OverlayBox(timeX,8,timeWidth,22);
        result.Status=new OverlayBox(statusX,8,width-statusX-Margin,22);
        result.Timeline=new OverlayBox(Margin,38,width-2*Margin,24);
        float panelWidth=Math.Min(560,width-2*Margin),panelHeight=Math.Min(560,height-dockHeight-PanelGap-20);
        result.Popout=new OverlayBox(Margin,height-dockHeight-PanelGap-panelHeight,panelWidth,panelHeight);
        return result;
    }

    private static OverlayBox Control(float x,float y,float width)=>new OverlayBox(x,y,width,ControlHeight);
    private static void RequirePositive(float value,string name)
    {if(float.IsNaN(value)||float.IsInfinity(value)||value<=0)throw new ArgumentOutOfRangeException(name);}
}


