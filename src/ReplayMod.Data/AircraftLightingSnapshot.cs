using System;

namespace ReplayMod.Data;

[Flags]
public enum AircraftLampFlags:byte {None=0,On=1,Usable=2,Gear=4}
public enum AircraftLampControl:byte {Never,Auto,ForceOn,Unknown}

public static class AircraftLightingPolicy
{
    public const int MaxLights=256,MaxKeyBytes=512;
    public static bool Resolve(bool observedOn,bool usable,bool gearAssociated,AircraftLampControl control,bool gearAutoOn,int presentationMode)
    {
        if(!usable||presentationMode==2)return false;
        // Native ForceOn is an explicit pilot/editor choice and can remain lit
        // with retracted gear. Auto lamps follow the recorded gear transition.
        if(presentationMode==1)return true;
        if(gearAssociated&&(control==AircraftLampControl.Auto||control==AircraftLampControl.Never)&&!gearAutoOn)return false;
        return observedOn;
    }
    public static bool GearAutoOn(byte state)=>state==2||state==4;
}
