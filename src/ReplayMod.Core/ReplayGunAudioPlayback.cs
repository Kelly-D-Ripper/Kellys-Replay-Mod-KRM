using System;
using System.Linq;
using UnityEngine;

namespace ReplayMod.Core;

internal sealed class ReplayGunAudioPlayback
{
    // Recordings have no trigger-release event, and fixed shots can share one
    // recording frame. Allow only this extra replay-time tail before expiry.
    internal const double RecordingFrameGrace = .05;
    private readonly Gun gun;
    private readonly ReplayGunBurstState burst = new ReplayGunBurstState();
    private readonly AudioSource[] sources;
    private readonly bool[] paused;

    internal ReplayGunAudioPlayback(Gun gun)
    {
        this.gun = gun;
        sources = (gun.sources ?? Array.Empty<AudioSource>())
            .Concat(gun.recoilSound != null ? new[] {gun.recoilSound} : Array.Empty<AudioSource>()).Distinct().ToArray();
        paused = new bool[sources.Length];
    }

    internal bool PlayShot(double time, bool coalesceSameTime = true)
    {
        if (gun == null || !burst.Record(time, coalesceSameTime)) return false;
        Resume();
        // Native firing refreshes this before ShotSound starts the loop. Keep
        // that onset without invoking live visibility or firing logic.
        gun.lastFired = Time.timeSinceLevelLoad;
        gun.ShotSound();
        return true;
    }

    internal void Update(double time, bool isPaused)
    {
        if (gun == null) return;
        if (isPaused) { Pause(); return; }
        if (!burst.HasShot || double.IsNaN(time) || double.IsInfinity(time) || time < 0) return;
        Resume();
        if (gun.sources == null || gun.sources.Length < 2) return;
        double age = Math.Max(0, burst.Age(time) - RecordingFrameGrace);
        gun.lastFired = Time.timeSinceLevelLoad - (float)Math.Min(1000,
            ReplayGunBurstState.NativeAge(age, Time.fixedDeltaTime, Time.deltaTime));
        gun.LoopSounds();
    }

    private void Pause()
    {
        for (int n = 0; n < sources.Length; n++)
            if (sources[n] != null && sources[n].isPlaying) { sources[n].Pause(); paused[n] = true; }
    }

    private void Resume()
    {
        for (int n = 0; n < sources.Length; n++)
            if (paused[n]) { if (sources[n] != null) sources[n].UnPause(); paused[n] = false; }
    }

    internal void Stop()
    {
        for (int n = 0; n < sources.Length; n++)
        { if (sources[n] != null) sources[n].Stop(); paused[n] = false; }
    }
}
