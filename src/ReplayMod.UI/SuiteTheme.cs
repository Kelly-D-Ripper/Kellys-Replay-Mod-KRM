using System;
using System.Globalization;

namespace ReplayMod.UI;

// Pure palette math: no native graphics, materials or gameplay objects are touched.
internal struct SuiteRgb
{
    internal readonly byte R, G, B;
    internal SuiteRgb(byte r, byte g, byte b) { R=r; G=g; B=b; }
    internal string Hex => "#"+R.ToString("X2")+G.ToString("X2")+B.ToString("X2");
    internal static bool TryParse(string value,out SuiteRgb colour)
    {
        colour=default;
        if(value==null)return false;
        value=value.Trim();if(value.StartsWith("#",StringComparison.Ordinal))value=value.Substring(1);
        if(value.Length!=6 || !uint.TryParse(value,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out uint rgb))return false;
        colour=new SuiteRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);return true;
    }
    internal static SuiteRgb Parse(string value,string fallback)
    {if(TryParse(value,out var colour))return colour;TryParse(fallback,out colour);return colour;}
    internal static SuiteRgb Mix(SuiteRgb a,SuiteRgb b,double amount)
    {
        amount=Math.Max(0,Math.Min(1,amount));
        return new SuiteRgb((byte)Math.Round(a.R+(b.R-a.R)*amount),(byte)Math.Round(a.G+(b.G-a.G)*amount),(byte)Math.Round(a.B+(b.B-a.B)*amount));
    }
    internal SuiteRgb Tint(double multiplier)=>new SuiteRgb((byte)Math.Round(Math.Min(255,R*multiplier)),(byte)Math.Round(Math.Min(255,G*multiplier)),(byte)Math.Round(Math.Min(255,B*multiplier)));
    private static double Linear(byte channel)
    {double value=channel/255.0;return value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4);}
    internal double Luminance => .2126*Linear(R)+.7152*Linear(G)+.0722*Linear(B);
    internal static double Contrast(SuiteRgb a,SuiteRgb b)
    {double x=a.Luminance,y=b.Luminance;return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    internal static SuiteRgb Readable(SuiteRgb requested,SuiteRgb background)
    {
        if(Contrast(requested,background)>=4.5)return requested;
        var black=new SuiteRgb(0,0,0);var white=new SuiteRgb(255,255,255);
        return Contrast(black,background)>=Contrast(white,background)?black:white;
    }
}

internal sealed class SuiteThemeSettings
{
    internal const float NativeOpacity=248f/255;
    internal string Preset="Native",Accent="#58E187",Panel="#050A05",Button="#3E463C",Text="#E0E0E0";
    internal float Opacity=NativeOpacity;
    internal static string NormalPreset(string value)
    {
        foreach(string name in new[]{"Native","Amber","Blue","Neutral","Custom"})
            if(string.Equals(name,value,StringComparison.OrdinalIgnoreCase))return name;
        return "Native";
    }
    internal static float NormalOpacity(float value)
    {return float.IsNaN(value)||float.IsInfinity(value)?NativeOpacity:Math.Max(.65f,Math.Min(1,value));}
    internal static SuiteThemeSettings Create(string preset,string accent,string panel,string button,string text,float opacity)
    {return new SuiteThemeSettings {Preset=NormalPreset(preset),Accent=SuiteRgb.Parse(accent,"#58E187").Hex,Panel=SuiteRgb.Parse(panel,"#050A05").Hex,Button=SuiteRgb.Parse(button,"#3E463C").Hex,Text=SuiteRgb.Parse(text,"#E0E0E0").Hex,Opacity=NormalOpacity(opacity)};}
    internal SuiteThemeSettings Copy()=>Create(Preset,Accent,Panel,Button,Text,Opacity);
    internal string Signature=>Preset+"|"+Accent+"|"+Panel+"|"+Button+"|"+Text+"|"+Opacity.ToString("R",CultureInfo.InvariantCulture);
}

internal sealed class SuiteThemePalette
{
    internal SuiteRgb Panel,Button,Selected,Accent,Text,Muted,ButtonText,SelectedText,Handle,TrackAccent,MarkerAccent;
    internal float Opacity;
    internal float HoverMultiplier=1,PressedMultiplier=1;
    internal bool Native;
    internal static SuiteThemePalette Build(SuiteThemeSettings settings)
    {
        settings=settings??new SuiteThemeSettings();string preset=SuiteThemeSettings.NormalPreset(settings.Preset);
        string accent="#58E187",panel="#050A05",button="#3E463C",text="#E0E0E0";
        switch(preset)
        {
            case "Amber":accent="#FFCE6A";panel="#100D06";button="#4B4230";break;
            case "Blue":accent="#72BEF5";panel="#060C12";button="#344351";break;
            case "Neutral":accent="#D9DEE3";panel="#0A0B0C";button="#42464A";break;
            case "Custom":accent=settings.Accent;panel=settings.Panel;button=settings.Button;text=settings.Text;break;
        }
        var result=new SuiteThemePalette {Panel=SuiteRgb.Parse(panel,"#050A05"),Button=SuiteRgb.Parse(button,"#3E463C"),Accent=SuiteRgb.Parse(accent,"#58E187"),Opacity=SuiteThemeSettings.NormalOpacity(settings.Opacity),Native=preset=="Native"};
        var requested=SuiteRgb.Parse(text,"#E0E0E0");
        result.Selected=result.Native?new SuiteRgb(23,48,33):SuiteRgb.Mix(result.Panel,result.Accent,.16);
        result.Text=SuiteRgb.Readable(requested,result.Panel);result.ButtonText=SuiteRgb.Readable(requested,result.Button);
        result.SelectedText=SuiteRgb.Readable(result.Accent,result.Selected);
        result.TrackAccent=SuiteRgb.Readable(result.Accent,result.Button);result.MarkerAccent=SuiteRgb.Readable(result.Accent,result.Panel);
        result.Muted=SuiteRgb.Readable(result.Native?new SuiteRgb(125,174,125):SuiteRgb.Mix(result.Text,result.Accent,.4),result.Panel);
        result.Handle=SuiteRgb.Readable(requested,result.Button);
        result.Opacity=result.ReadableOpacity(result.Opacity);
        result.HoverMultiplier=result.SafeTint(1.06f);result.PressedMultiplier=result.SafeTint(.9f);return result;
    }
    private bool PanelReadable(float opacity)
    {
        var overBlack=SuiteRgb.Mix(new SuiteRgb(0,0,0),Panel,opacity);
        var overWhite=SuiteRgb.Mix(new SuiteRgb(255,255,255),Panel,opacity);
        return SuiteRgb.Contrast(Text,overBlack)>=4.5&&SuiteRgb.Contrast(Text,overWhite)>=4.5&&
            SuiteRgb.Contrast(Muted,overBlack)>=4.5&&SuiteRgb.Contrast(Muted,overWhite)>=4.5&&
            SuiteRgb.Contrast(MarkerAccent,overBlack)>=4.5&&SuiteRgb.Contrast(MarkerAccent,overWhite)>=4.5;
    }
    private float ReadableOpacity(float requested)
    {
        if(PanelReadable(requested))return requested;
        float low=requested,high=1;
        for(int i=0;i<24;i++)
        {float middle=(low+high)*.5f;if(PanelReadable(middle))high=middle;else low=middle;}
        // Requested opacity stays in the config; only this derived panel colour gains protection.
        return Math.Min(1,high+.000001f);
    }
    private float SafeTint(float requested)
    {
        if(SuiteRgb.Contrast(ButtonText,Button.Tint(requested))>=4.55&&SuiteRgb.Contrast(SelectedText,Selected.Tint(requested))>=4.55&&SuiteRgb.Contrast(Handle.Tint(requested),Button)>=4.55)return requested;
        return 1;
    }
}
