using System;
using ReplayMod.Core;

public static class PropAudioPolicyChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(string name,bool ok){if(!ok)throw new Exception("Propeller audio: "+name);checks++;Console.WriteLine("PASS propeller audio "+name);}
        Check("matches native RPM and power formula",ReplayPropAudioPolicy.TryInitialVolume(500,1000,.3f,.7f,.1f,.5f,.2f,out var normal)&&Math.Abs(normal-.38f)<.00001f);
        Check("uses lower throttle when power exceeds it",ReplayPropAudioPolicy.TryInitialVolume(500,1000,.7f,.3f,.1f,.5f,.2f,out var throttle)&&Math.Abs(throttle-.38f)<.00001f);
        Check("bounds high source volume",ReplayPropAudioPolicy.TryInitialVolume(2000,1000,1,1,.5f,1,1,out var high)&&high==1);
        Check("leaves naturally silent source untouched",!ReplayPropAudioPolicy.TryInitialVolume(0,1000,0,0,0,1,1,out _));
        Check("rejects negative or non-finite recorded RPM",!ReplayPropAudioPolicy.TryInitialVolume(-1,1000,1,1,.5f,1,1,out _)&&!ReplayPropAudioPolicy.TryInitialVolume(float.NaN,1000,1,1,.5f,1,1,out _));
        Check("rejects invalid native values and overflow",!ReplayPropAudioPolicy.TryInitialVolume(1,0,1,1,.5f,1,1,out _)&&!ReplayPropAudioPolicy.TryInitialVolume(1,1,float.PositiveInfinity,1,.5f,1,1,out _)&&!ReplayPropAudioPolicy.TryInitialVolume(float.MaxValue,.001f,1,1,.5f,1,1,out _));
        Check("only loaded decompressed loops with an in-range sample can be rearmed",ReplayPropAudioPolicy.CanWakeLoop(true,true,true,42,100)&&!ReplayPropAudioPolicy.CanWakeLoop(false,true,true,42,100)&&!ReplayPropAudioPolicy.CanWakeLoop(true,false,true,42,100)&&!ReplayPropAudioPolicy.CanWakeLoop(true,true,false,42,100)&&!ReplayPropAudioPolicy.CanWakeLoop(true,true,true,-1,100)&&!ReplayPropAudioPolicy.CanWakeLoop(true,true,true,100,100)&&!ReplayPropAudioPolicy.CanWakeLoop(true,true,true,0,0));
        Check("jet startup rejects silent, outside-cone, invalid and overflowing RPM",ReplayPropAudioPolicy.CanStartJet(1500,3000,1.5f,false)&&!ReplayPropAudioPolicy.CanStartJet(0,3000,1.5f,false)&&!ReplayPropAudioPolicy.CanStartJet(1500,3000,1.5f,true)&&!ReplayPropAudioPolicy.CanStartJet(float.NaN,3000,1.5f,false)&&!ReplayPropAudioPolicy.CanStartJet(1,0,1.5f,false)&&!ReplayPropAudioPolicy.CanStartJet(float.MaxValue,.001f,1.5f,false));
        return checks;
    }
}
