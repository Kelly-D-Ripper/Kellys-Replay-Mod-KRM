using System;

namespace ReplayMod.Core;

// Matches the native PropFan volume calculation, using the recorded RPM before
// its first physics tick. AudioSource volume is bounded to Unity's 0..1 range.
internal static class ReplayPropAudioPolicy
{
    private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    // Only restart a seekable, fully loaded loop whose current position can be
    // restored. Streaming and one-shot sources retain their native behavior.
    internal static bool CanWakeLoop(bool looping,bool loaded,bool decompressed,int sample,int sampleCount)
        =>looping&&loaded&&decompressed&&sample>=0&&sampleCount>0&&sample<sampleCount;
    internal static bool CanStartJet(float rpm,float maxRpm,float maxPitch,bool outsideCone)
        =>!outsideCone&&Finite(rpm)&&rpm>0&&Finite(maxRpm)&&maxRpm>0&&Finite(maxPitch)&&maxPitch>=0&&Finite(rpm/maxRpm*maxPitch);
    internal static bool TryInitialVolume(float rpm,float nominalRpm,float powerRatio,float throttle,
        float volumeBase,float rpmPortion,float powerPortion,out float volume)
    {
        volume=0;
        if(!Finite(rpm)||rpm<0||!Finite(nominalRpm)||nominalRpm<=0||!Finite(powerRatio)||!Finite(throttle)||
           !Finite(volumeBase)||!Finite(rpmPortion)||!Finite(powerPortion))return false;
        float ratio=rpm/nominalRpm,drive=Math.Min(powerRatio,throttle);
        float value=volumeBase+rpmPortion*ratio+powerPortion*drive*ratio;
        if(!Finite(value)||value<=0)return false;
        volume=Math.Min(1f,value);return true;
    }
}
