using ReplayMod.Director;

public static class CameraOrientationChecks
{
    public static int Run()
    {
        int passed=0;
        Test("level view retains compass heading for pitched or rolled views",()=>
        {
            Near(CameraOrientation.FlatYaw(0,.2,45),0);
            Near(CameraOrientation.FlatYaw(.001,0,45),90);
            Near(CameraOrientation.FlatYaw(0,-.8,45),180);
            Near(CameraOrientation.FlatYaw(-.1,0,45),270);
            Near(CameraOrientation.FlatYaw(.2,.2,300),45);
        });
        Test("vertical view retains its previous yaw without creating NaN",()=>
        {
            Near(CameraOrientation.FlatYaw(0,0,123),123);
            Near(CameraOrientation.FlatYaw(1e-9,-1e-9,-45),315);
            Near(CameraOrientation.FlatYaw(double.NaN,1,361),1);
            Near(CameraOrientation.FlatYaw(double.PositiveInfinity,0,double.NaN),0);
        });
        Test("native lens bounds preserve telephoto zoom and reject invalid values",()=>
        {
            Near(CameraOrientation.Lens(1),1);Near(CameraOrientation.Lens(35),35);Near(CameraOrientation.Lens(120),120);
            Near(CameraOrientation.Lens(-1),1);Near(CameraOrientation.Lens(200),120);
            Near(CameraOrientation.Lens(float.NaN,43),43);Near(CameraOrientation.Lens(float.PositiveInfinity,float.NaN),60);
        });
        return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS camera orientation "+name);}
        void Near(double actual,double expected){if(Math.Abs(actual-expected)>.00001)throw new Exception($"{actual} != {expected}");}
    }
}
