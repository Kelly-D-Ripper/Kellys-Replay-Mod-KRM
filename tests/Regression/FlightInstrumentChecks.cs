using ReplayMod.Core;
using System.Globalization;

public static class FlightInstrumentChecks
{
    public static int Run()
    {
        int passed=0;
        void Check(string name,Action test){test();passed++;Console.WriteLine("PASS flight instruments "+name);}
        void Require(bool value,string reason){if(!value)throw new Exception(reason);}
        Check("recorded motion is labelled ground speed and MSL altitude",()=>
        {
            var frame=new ReplayFlightInstrumentFrame(250.25,1850,-10,-15.5,.72,0,0,true);
            var lines=frame.Lines();Require(frame.Valid&&lines.Contains("GSPD 250.3 M/S")&&lines.Contains("ALT 1850 MSL M")&&lines.Contains("HDG 350 DEG")&&lines.Contains("V/S -15.5 M/S")&&lines.Contains("THR 72%")&&lines.Contains("GEAR DOWN"),"known data and units retained");
            Require(!lines.Any(line=>line.Contains("RADAR")||line.Contains("FUEL")||line.Contains("LOCK")||line.Contains("TARGET")),"unrecorded tactical/electrical state omitted");
        });
        Check("pause gives an identical physical screen and rewind reconstructs earlier data",()=>
        {
            var before=new ReplayFlightInstrumentFrame(100,400,45,20,.4,0,0,false);var after=new ReplayFlightInstrumentFrame(300,1000,90,40,.9,20,45,true);
            var a=new uint[256*256];var b=new uint[a.Length];var c=new uint[a.Length];
            ReplayFlightInstrumentDisplay.Render(before,a);ReplayFlightInstrumentDisplay.Render(before,b);ReplayFlightInstrumentDisplay.Render(after,c);
            Require(a.SequenceEqual(b)&&!a.SequenceEqual(c),"paused deterministic screen and changed telemetry");
            ReplayFlightInstrumentDisplay.Render(before,c);Require(a.SequenceEqual(c),"backward seek has no accumulated display history");
        });
        Check("invalid flight values display NO DATA instead of fabricated instrument readings",()=>
        {
            foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1,10001})
            {
                var frame=new ReplayFlightInstrumentFrame(invalid,0,0,0,0,0,0,false);
                Require(!frame.Valid&&frame.Lines().SequenceEqual(new[]{"FLIGHT DATA","NO DATA"}),"invalid speed rejected");
                ReplayFlightInstrumentDisplay.Render(frame,new uint[256*256]);
            }
        });
        Check("physical screen raster is bounded, nonempty and contains only its own palette",()=>
        {
            var pixels=new uint[256*256];
            for(int i=0;i<720;i++)ReplayFlightInstrumentDisplay.Render(new ReplayFlightInstrumentFrame(120,1000,i,10,.8,i,i,true),pixels);
            Require(pixels.Any(pixel=>pixel==ReplayFlightInstrumentDisplay.Ink)&&pixels.Any(pixel=>pixel==ReplayFlightInstrumentDisplay.Background)&&pixels.All(pixel=>pixel is ReplayFlightInstrumentDisplay.Ink or ReplayFlightInstrumentDisplay.Background),"all attitudes keep bounded original artwork");
            bool rejected=false;try{ReplayFlightInstrumentDisplay.Render(default,new uint[4]);}catch(ArgumentException){rejected=true;}Require(rejected,"wrong target buffer rejected");
        });
        Check("locale and control overshoot cannot change instrument units or overflow text",()=>
        {
            var original=CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
                var frame=new ReplayFlightInstrumentFrame(120.5,-20,1080,5,2,0,0,false);
                Require(frame.Lines().Contains("GSPD 120.5 M/S")&&frame.Lines().Contains("THR 100%")&&frame.Lines().Contains("HDG 000 DEG"),"fixed units, dot decimal, wrapped heading and bounded throttle");
            }
            finally{CultureInfo.CurrentCulture=original;}
        });
        return passed;
    }
}
