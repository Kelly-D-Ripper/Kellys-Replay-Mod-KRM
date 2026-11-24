using ReplayMod.Director;

public static class TwoAircraftFramingChecks
{
    public static void Run(Action<string, Action> test)
    {
        test("pair framing encloses both aircraft spheres with a fifteen percent margin", () =>
        {
            Require(TwoAircraftFraming.TryDistance(100, 12, 20, 60, 16d/9, 1, 10000, out double d, out double r));
            Near(r, 80.5);
            Near(d, 161);
            Require(r >= 50+12 && r >= 50+20);
            Fits(d, r, 60, 16d/9);
        });
        test("pair framing respects the narrower dimension in portrait and ultrawide viewports", () =>
        {
            double previous = 0;
            foreach (double aspect in new[] { .25, 9d/16, 1, 16d/9, 32d/9 })
            {
                Require(TwoAircraftFraming.TryDistance(250, 8, 35, 45, aspect, 5, 100000, out double d, out double r));
                Fits(d, r, 45, aspect);
                if (previous > 0) Require(d <= previous + 1e-9);
                previous = d;
            }
        });
        test("pair framing handles coincident centres and honours the minimum distance", () =>
        {
            Require(TwoAircraftFraming.TryDistance(0, 10, 30, 60, 1, 100, 1000, out double d, out double r));
            Near(r, 34.5); Near(d, 100); Fits(d, r, 60, 1);
            Require(TwoAircraftFraming.TryDistance(0, 0, 0, 60, 1, 5, 100, out d, out r));
            Near(d, 5); Near(r, 0);
            Require(!TwoAircraftFraming.TryDistance(0, 0, 0, 60, 1, 0, 100, out _, out _));
        });
        test("pair framing rejects a sphere touching or exceeding the far plane", () =>
        {
            Require(TwoAircraftFraming.TryDistance(100, 20, 20, 60, 1, 1, 1000, out double d, out double r));
            Require(!TwoAircraftFraming.TryDistance(100, 20, 20, 60, 1, 1, d+r, out _, out _));
            Require(TwoAircraftFraming.TryDistance(100, 20, 20, 60, 1, 1, d+r+0.001, out _, out _));
            Require(!TwoAircraftFraming.TryDistance(100, 20, 20, 60, 1, 1000, 1000, out _, out _));
        });
        test("pair framing rejects nonfinite and invalid geometric inputs without partial outputs", () =>
        {
            double[] valid = { 100, 20, 25, 60, 1.5, 1, 10000 };
            for (int index=0; index<valid.Length; index++)
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
                {
                    var args=(double[])valid.Clone(); args[index]=invalid;
                    Require(!TwoAircraftFraming.TryDistance(args[0],args[1],args[2],args[3],args[4],args[5],args[6],out double d,out double r));
                    Near(d,0); Near(r,0);
                }
            foreach (double fov in new[] { 0d, 180d, 360d })
                Require(!TwoAircraftFraming.TryDistance(100,20,25,fov,1.5,1,10000,out _,out _));
            Require(!TwoAircraftFraming.TryDistance(100,20,25,60,0,1,10000,out _,out _));
            Require(!TwoAircraftFraming.TryDistance(100,20,25,60,1,1,0,out _,out _));
        });
        test("pair framing stays finite for extreme inputs or fails safely", () =>
        {
            Require(!TwoAircraftFraming.TryDistance(double.MaxValue,double.MaxValue,0,60,1,1,double.MaxValue,out _,out _));
            Require(!TwoAircraftFraming.TryDistance(100,20,25,double.Epsilon,1,1,10000,out _,out _));
            Require(!TwoAircraftFraming.TryDistance(100,20,25,60,double.Epsilon,1,10000,out _,out _));
            Require(TwoAircraftFraming.TryDistance(1e10,1e9,2e9,120,1,1,1e12,out double d,out double r));
            Fits(d,r,120,1);
        });
    }
    private static void Fits(double distance,double radius,double fov,double aspect)
    {
        double angularRadius=Math.Asin(radius/distance);
        double half=fov*Math.PI/360;
        Require(angularRadius<=half+1e-12 && angularRadius<=Math.Atan(Math.Tan(half)*aspect)+1e-12);
    }
    private static void Require(bool value){if(!value)throw new Exception("Two-aircraft framing check failed");}
    private static void Near(double a,double b){if(Math.Abs(a-b)>1e-8)throw new Exception($"{a} differs from {b}");}
}
