using ReplayMod.Core;

public static class RotorSpeedChecks
{
    public static void Run(Action<string,Action> test)
    {
        test("recorded helicopter RPM restores native angular speed and animation ratio",()=>
        {
            if(!ReplayRotorSpeedPolicy.TryConvert(240,30,out float speed,out float ratio)||Math.Abs(speed*9.55414f-240)>.001f||Math.Abs(ratio-speed/30)>.0001f)
                throw new Exception("Rotor RPM conversion is inconsistent");
            if(!ReplayRotorSpeedPolicy.TryConvert(0,30,out speed,out ratio)||speed!=0||ratio!=0)
                throw new Exception("Stopped rotor was not preserved");
        });
        test("invalid rotor speed and uninitialized limits do not contaminate playback",()=>
        {
            foreach(float rpm in new[]{-1f,float.NaN,float.PositiveInfinity})
                if(ReplayRotorSpeedPolicy.TryConvert(rpm,30,out _,out _))throw new Exception("Invalid RPM accepted");
            foreach(float limit in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.Epsilon})
                if(ReplayRotorSpeedPolicy.TryConvert(float.MaxValue,limit,out _,out _))throw new Exception("Invalid or overflowing rotor limit accepted");
        });
    }
}
