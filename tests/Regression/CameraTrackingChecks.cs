using ReplayMod.Director;

public static class CameraTrackingChecks
{
    public static int Run()
    {
        int passed=0;
        Check("camera target remains attached to the selected actor lifetime",()=>
        {
            var targets=new CameraTargetBinding<object>();var building=new object();var aircraft=new object();
            targets.Select(10,building);Require(targets.TryResolve(10,building,out var found)&&ReferenceEquals(found,building),"selected building");
            targets.Select(20,aircraft);Require(targets.TryResolve(20,aircraft,out found)&&ReferenceEquals(found,aircraft),"map aircraft replaces subject by recorded ID");
            Require(!targets.TryResolve(20,new object(),out _),"a reused recorded ID cannot silently replace the aircraft");
            Require(targets.TryResolve(10,building,out found)&&ReferenceEquals(found,building),"other recorded lifetime stays independent");
        });
        Check("missing targets bind only when they first appear and never after their lifetime ends",()=>
        {
            var targets=new CameraTargetBinding<object>();var first=new object();
            Require(!targets.TryResolve(5,null,out _),"before actor birth");
            Require(targets.TryResolve(5,first,out _),"first actor birth");
            Require(!targets.TryResolve(5,null,out _),"after actor destruction");
            Require(!targets.TryResolve(5,new object(),out _),"later reused ID still absent from this shot");
        });
        Check("explicit reselection and seek reconstruction intentionally rebind lifetimes",()=>
        {
            var targets=new CameraTargetBinding<object>();var first=new object();var replacement=new object();
            targets.Select(2,first);targets.Select(2,replacement);
            Require(targets.TryResolve(2,replacement,out _),"explicit user selection wins");
            Require(!targets.TryResolve(2,first,out _),"old lifetime rejected");
            targets.Reset();Require(targets.TryResolve(2,first,out _),"seek reconstructs the selected recorded subject");
        });
        Check("camera bank wraps continuously through full turns",()=>
        {
            Near(CameraRollMath.Add(179,2),-179);Near(CameraRollMath.Add(-179,-2),179);
            Near(CameraRollMath.Normalize(1080),0);Near(CameraRollMath.Normalize(-1080),0);
            Near(CameraRollMath.Normalize(180),180);Near(CameraRollMath.Normalize(-180),-180);
        });
        Check("held roll uses real time consistently across frame rates",()=>
        {
            float thirty=0,oneTwenty=0;
            for(int n=0;n<30;n++)thirty=CameraRollMath.Advance(thirty,1,1f/30);
            for(int n=0;n<120;n++)oneTwenty=CameraRollMath.Advance(oneTwenty,1,1f/120);
            Near(thirty,45);Near(oneTwenty,45);
            Near(CameraRollMath.Advance(0,-1,1),-45);
            Near(CameraRollMath.Advance(25,0,1),25);
        });
        Check("invalid roll inputs cannot poison camera rotation",()=>
        {
            Near(CameraRollMath.Normalize(float.NaN),0);
            Near(CameraRollMath.Add(30,float.PositiveInfinity),30);
            Near(CameraRollMath.Advance(30,float.NaN,1),30);
            Near(CameraRollMath.Advance(30,1,float.PositiveInfinity),30);
            Near(CameraRollMath.Advance(30,1,-1),30);
            Near(CameraRollMath.Advance(0,100,1),45);
        });
        return passed;
        void Check(string name,Action action){action();passed++;Console.WriteLine("PASS camera tracking "+name);}
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Near(float actual,float expected){if(Math.Abs(actual-expected)>.001f)throw new Exception($"{actual} != {expected}");}
}
