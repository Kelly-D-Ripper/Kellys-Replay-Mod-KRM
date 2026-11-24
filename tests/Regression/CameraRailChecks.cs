using ReplayMod.Director;

public static class CameraRailChecks
{
    public static int Run()
    {
        int passed=0;
        Test("constant movement uses real seconds and a normalized direction",()=>
        {
            var rail=new CameraRailState();rail.SetSpeed(5);Require(rail.Start(10,20,30,3,0,4),"rail starts");
            rail.Advance(2);Near(rail.X,16);Near(rail.Y,20);Near(rail.Z,38);
            // No replay-time or pause input enters the state: another second of
            // real time moves exactly five metres even for a frozen replay.
            rail.Advance(1);Near(rail.X,19);Near(rail.Z,42);
        });
        Test("travel direction stays fixed while speed changes without a jump",()=>
        {
            var rail=new CameraRailState();rail.Start(0,0,0,0,0,-1);rail.Advance(1);
            Near(rail.Z,-5);rail.SetSpeed(2);Near(rail.Z,-5);rail.Advance(.5);Near(rail.Z,-6);
            Near(rail.DirectionX,0);Near(rail.DirectionY,0);Near(rail.DirectionZ,-1);
        });
        Test("follow-view steering changes direction without a position or speed jump",()=>
        {
            var rail=new CameraRailState();rail.SetSpeed(2);rail.Start(0,0,0,0,0,1);rail.Advance(1);
            Require(rail.SetDirection(4,0,0),"new view direction accepted");Near(rail.X,0);Near(rail.Z,2);Near(rail.Speed,2);
            rail.Advance(.5);Near(rail.X,1);Near(rail.Z,2);
            Require(!rail.SetDirection(0,0,0)&&!rail.SetDirection(double.NaN,0,1),"invalid steering ignored");
            Require(rail.Active,"invalid steering does not stop current movement");rail.Advance(.5);Near(rail.X,2);Near(rail.Z,2);
        });
        Test("stop and restart use a fresh anchor without residual movement",()=>
        {
            var rail=new CameraRailState();rail.Start(1,2,3,1,0,0);rail.Advance(1);rail.Stop();rail.Advance(10);Near(rail.X,6);
            rail.Start(100,200,300,0,1,0);rail.Advance(2);Near(rail.X,100);Near(rail.Y,210);Near(rail.Z,300);
            Require(!rail.Start(0,0,0,0,0,0) && !rail.Active,"invalid restart stops previous rail");
        });
        Test("invalid speed and time values cannot poison the camera position",()=>
        {
            var rail=new CameraRailState();rail.SetSpeed(-1);Near(rail.Speed,.1f);rail.SetSpeed(1000);Near(rail.Speed,100);
            rail.SetSpeed(7);rail.SetSpeed(float.NaN);rail.SetSpeed(float.PositiveInfinity);Near(rail.Speed,7);
            Require(!rail.Start(double.NaN,0,0,1,0,0),"invalid anchor rejected");
            Require(!rail.Start(0,0,0,double.PositiveInfinity,0,0),"invalid direction rejected");
            rail.Start(0,0,0,1,0,0);rail.Advance(double.NaN);rail.Advance(double.PositiveInfinity);rail.Advance(-1);Near(rail.X,0);
            rail.Advance(double.MaxValue);Require(!rail.Active,"overflow stops movement");Near(rail.X,0);
        });
        Test("gentle movement remains precise across large floating-origin shifts",()=>
        {
            var rail=new CameraRailState();rail.SetSpeed(.1f);rail.Start(1000000.125,100,0,1,0,0);
            rail.Advance(1.0/60);Near(rail.LocalX(-1000000),.12666667,.0000001);
            // Moving Datum changes the local coordinate, never the global rail.
            double global=rail.X;Near(rail.LocalX(-999950),50.12666667,.000003);Near(rail.X,global);
            rail.Advance(1.0/60);Near(rail.LocalX(-1000000),.12833333,.0000001);
        });
        return passed;

        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS camera rail "+name);}
        void Require(bool value,string message){if(!value)throw new Exception(message);}
        void Near(double actual,double expected,double tolerance=.000001){if(Math.Abs(actual-expected)>tolerance)throw new Exception($"{actual} != {expected}");}
    }
}
