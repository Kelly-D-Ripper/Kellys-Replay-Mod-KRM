using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReplayMod.Core;

// Only fields derivable from recorded motion, controls and gear. No radar,
// targeting, fuel, electrical-power or weapon-lock history is invented.
public readonly struct ReplayFlightInstrumentFrame
{
    public readonly double Speed,Altitude,Heading,Climb,Throttle,Pitch,Roll;
    public readonly bool Gear;
    public ReplayFlightInstrumentFrame(double speed,double altitude,double heading,double climb,double throttle,double pitch,double roll,bool gear)
    {Speed=speed;Altitude=altitude;Heading=heading;Climb=climb;Throttle=throttle;Pitch=pitch;Roll=roll;Gear=gear;}
    public bool Valid=>Finite(Speed)&&Speed>=0&&Speed<=10000&&Finite(Altitude)&&Math.Abs(Altitude)<=10000000&&
        Finite(Heading)&&Finite(Climb)&&Math.Abs(Climb)<=10000&&Finite(Throttle)&&Finite(Pitch)&&Finite(Roll);
    private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    public string[] Lines()
    {
        if(!Valid)return new[]{"FLIGHT DATA","NO DATA"};
        double heading=(Heading%360+360)%360;
        return new[]{"FLIGHT DATA","GSPD "+Speed.ToString("0.0",CultureInfo.InvariantCulture)+" M/S",
            "ALT "+Altitude.ToString("0",CultureInfo.InvariantCulture)+" MSL M",
            "HDG "+heading.ToString("000",CultureInfo.InvariantCulture)+" DEG",
            "V/S "+Climb.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture)+" M/S",
            "THR "+(Math.Max(0,Math.Min(1,Throttle))*100).ToString("0",CultureInfo.InvariantCulture)+"%",
            "GEAR "+(Gear?"DOWN":"UP")};
    }
}

// Small original bitmap alphabet keeps physical screens independent of desktop
// UI, fonts, hidden canvases, native camera stacks and shared render textures.
public static class ReplayFlightInstrumentDisplay
{
    public const int Size=256;
    public const uint Background=0x001006ff,Ink=0x68f0a0ff;
    private static readonly Dictionary<char,ulong> glyphs=BuildGlyphs();
    public static void Render(ReplayFlightInstrumentFrame frame,uint[] pixels)
    {
        if(pixels==null||pixels.Length!=Size*Size)throw new ArgumentException("Flight display requires a 256 square pixel buffer");
        for(int n=0;n<pixels.Length;n++)pixels[n]=Background;
        var lines=frame.Lines();
        for(int n=0;n<lines.Length;n++)Text(lines[n],12,12+n*24,pixels);
        Line(10,34,246,34,pixels);
        if(!frame.Valid)return;
        double roll=frame.Roll*Math.PI/180;
        double pitch=(frame.Pitch%360+540)%360-180;
        int centerY=218+(int)Math.Max(-20,Math.Min(20,pitch));
        int dx=(int)(Math.Cos(roll)*70),dy=(int)(Math.Sin(roll)*70);
        Line(128-dx,centerY-dy,128+dx,centerY+dy,pixels);
        Line(118,218,138,218,pixels);Line(128,213,128,223,pixels);
    }
    private static void Text(string text,int x,int y,uint[] pixels)
    {
        foreach(char c in text)
        {
            if(glyphs.TryGetValue(c,out ulong glyph))
                for(int row=0;row<7;row++)for(int col=0;col<5;col++)if((glyph&(1UL<<(row*5+col)))!=0)
                    for(int py=0;py<2;py++)for(int px=0;px<2;px++)Pixel(x+col*2+px,y+row*2+py,pixels);
            x+=12;if(x>Size-10)break;
        }
    }
    private static void Pixel(int x,int y,uint[] pixels)
    {if(x>=0&&x<Size&&y>=0&&y<Size)pixels[(Size-1-y)*Size+x]=Ink;}
    private static void Line(int x0,int y0,int x1,int y1,uint[] pixels)
    {
        int dx=Math.Abs(x1-x0),sx=x0<x1?1:-1,dy=-Math.Abs(y1-y0),sy=y0<y1?1:-1,error=dx+dy;
        for(int count=0;count<Size*3;count++)
        {Pixel(x0,y0,pixels);if(x0==x1&&y0==y1)break;int twice=error*2;if(twice>=dy){error+=dy;x0+=sx;}if(twice<=dx){error+=dx;y0+=sy;}}
    }
    private static Dictionary<char,ulong> BuildGlyphs()
    {
        var result=new Dictionary<char,ulong>();
        void Add(char c,string rows){ulong bits=0;int at=0;foreach(char v in rows)if(v!=' '&&v!='/'){if(v=='1')bits|=1UL<<at;at++;}result.Add(c,bits);}
        Add('A',"01110/10001/10001/11111/10001/10001/10001");Add('B',"11110/10001/10001/11110/10001/10001/11110");
        Add('C',"01111/10000/10000/10000/10000/10000/01111");Add('D',"11110/10001/10001/10001/10001/10001/11110");
        Add('E',"11111/10000/10000/11110/10000/10000/11111");Add('F',"11111/10000/10000/11110/10000/10000/10000");
        Add('G',"01111/10000/10000/10111/10001/10001/01111");Add('H',"10001/10001/10001/11111/10001/10001/10001");
        Add('I',"11111/00100/00100/00100/00100/00100/11111");Add('J',"00111/00010/00010/00010/00010/10010/01100");
        Add('K',"10001/10010/10100/11000/10100/10010/10001");Add('L',"10000/10000/10000/10000/10000/10000/11111");
        Add('M',"10001/11011/10101/10101/10001/10001/10001");Add('N',"10001/11001/10101/10011/10001/10001/10001");
        Add('O',"01110/10001/10001/10001/10001/10001/01110");Add('P',"11110/10001/10001/11110/10000/10000/10000");
        Add('Q',"01110/10001/10001/10001/10101/10010/01101");Add('R',"11110/10001/10001/11110/10100/10010/10001");
        Add('S',"01111/10000/10000/01110/00001/00001/11110");Add('T',"11111/00100/00100/00100/00100/00100/00100");
        Add('U',"10001/10001/10001/10001/10001/10001/01110");Add('V',"10001/10001/10001/10001/10001/01010/00100");
        Add('W',"10001/10001/10001/10101/10101/11011/10001");Add('X',"10001/10001/01010/00100/01010/10001/10001");
        Add('Y',"10001/10001/01010/00100/00100/00100/00100");Add('Z',"11111/00001/00010/00100/01000/10000/11111");
        Add('0',"01110/10001/10011/10101/11001/10001/01110");Add('1',"00100/01100/00100/00100/00100/00100/01110");
        Add('2',"01110/10001/00001/00010/00100/01000/11111");Add('3',"11110/00001/00001/01110/00001/00001/11110");
        Add('4',"00010/00110/01010/10010/11111/00010/00010");Add('5',"11111/10000/10000/11110/00001/00001/11110");
        Add('6',"01110/10000/10000/11110/10001/10001/01110");Add('7',"11111/00001/00010/00100/01000/01000/01000");
        Add('8',"01110/10001/10001/01110/10001/10001/01110");Add('9',"01110/10001/10001/01111/00001/00001/01110");
        Add('+',"00000/00100/00100/11111/00100/00100/00000");Add('-',"00000/00000/00000/11111/00000/00000/00000");
        Add('.',"00000/00000/00000/00000/00000/00110/00110");Add('/',"00001/00001/00010/00100/01000/10000/10000");
        Add('%',"11001/11010/00010/00100/01000/01011/10011");
        return result;
    }
}
