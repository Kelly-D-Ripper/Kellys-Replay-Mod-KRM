using ReplayMod.Core;
using UnityEngine;

public static class GunAudioChecks
{
    public static void Run(Action<string, Action> test)
    {
        test("gun audio first tap and long-gap tap start native sustained sound immediately", () =>
        {
            Clock(0); var gun = LoopGun(); var audio = new ReplayGunAudioPlayback(gun);
            Check(audio.PlayShot(0), "first tap accepted");
            Check(gun.sources[1].isPlaying && gun.sources[1].Plays == 1 && Starts(gun) == 1, "first tap starts both native clips");
            Clock(.3); audio.Update(.3, false); Check(Ends(gun) == 1, "first tap expires");
            Clock(20); Check(audio.PlayShot(20), "long-gap tap accepted");
            Check(gun.sources[1].isPlaying && gun.sources[1].Plays == 2 && Starts(gun) == 2, "long-gap onset does not wait for second shot");
            Near(gun.lastFired, Time.timeSinceLevelLoad);
        });
        test("gun audio quantized continuous groups stay continuous at slow motion and high playback FPS", () =>
        {
            foreach (double interval in new[] {.06, .08, 60d / 900})
            foreach (int fps in new[] {60, 120})
            foreach (double speed in new[] {.1, 1.0})
            {
                var gun = LoopGun((float)interval); var audio = new ReplayGunAudioPlayback(gun);
                double step = speed / fps; int group = 0;
                for (int frame = 0; frame * step <= 1.05; frame++)
                {
                    double time = frame * step; Clock(time, step);
                    while (group <= 10 && group * .1 <= time + 1e-9)
                    {
                        Check(audio.PlayShot(group * .1, false) && audio.PlayShot(group * .1, false), "explicit same-time shots retained");
                        group++;
                    }
                    audio.Update(time, false);
                    Check(gun.sources[1].isPlaying && Starts(gun) == 1 && Ends(gun) == 0, "continuous grouped burst never restarts");
                }
                Check(group == 11 && gun.ShotSoundCalls == 22, "every captured shot consumed");
                Clock(1.3, step); audio.Update(1.3, false);
                Check(Ends(gun) == 1 && !gun.sources[1].isPlaying, "burst has a bounded tail");
            }
        });
        test("gun audio release tail is bounded to native tolerance plus fifty replay milliseconds", () =>
        {
            foreach (int fps in new[] {60, 120})
            foreach (double speed in new[] {.1, 1.0})
            {
                double step = speed / fps; Clock(0, step);
                var gun = LoopGun(); var audio = new ReplayGunAudioPlayback(gun); audio.PlayShot(0);
                double limit = gun.fireInterval + Math.Max(Time.fixedDeltaTime, Time.deltaTime) + ReplayGunAudioPlayback.RecordingFrameGrace;
                double stopped = -1;
                for (int frame = 1; frame * step < .5; frame++)
                {
                    double time = frame * step; Clock(time, step); audio.Update(time, false);
                    if (stopped < 0 && !gun.sources[1].isPlaying) stopped = time;
                }
                Check(stopped >= limit - .0001 && stopped <= limit + step + .0001, "stops within one presentation frame of fixed bound");
                Check(Ends(gun) == 1 && gun.sources[1].Stops == 1, "one native end clip, no repeated tail");
                Near(ReplayGunAudioPlayback.RecordingFrameGrace / speed, speed == .1 ? .5 : .05);
            }
        });
        test("gun audio genuine separate bursts terminate and restart without learned indefinite hold", () =>
        {
            Clock(0); var gun = LoopGun(); var audio = new ReplayGunAudioPlayback(gun); audio.PlayShot(0);
            for (int frame = 1; frame < 24; frame++) { double time = frame / 120d; Clock(time, 1d / 120); audio.Update(time, false); }
            Check(!gun.sources[1].isPlaying && Ends(gun) == 1, "two-tenths gap contains a real release");
            Clock(.2); audio.PlayShot(.2); Check(Starts(gun) == 2 && gun.sources[1].Plays == 2, "new burst starts once");
            Clock(100); audio.Update(100, false); Check(Ends(gun) == 2, "long silence cannot leave loop stuck");
        });
        test("gun audio single-source native shots recoil and legacy duplicate rules remain intact", () =>
        {
            Clock(0); var gun = SingleGun(); gun.recoilSound = new AudioSource(); var audio = new ReplayGunAudioPlayback(gun);
            Check(audio.PlayShot(0, true) && !audio.PlayShot(0, true), "legacy muzzle duplicates coalesced");
            Check(audio.PlayShot(0, false) && audio.PlayShot(0, false), "explicit shots at same time are distinct");
            Check(gun.ShotSoundCalls == 3 && gun.sources[0].OneShots.Count == 3 && gun.recoilSound.Plays == 3, "native per-shot and recoil counts");
            Clock(100); audio.Update(100, false);
            Check(gun.LoopSoundCalls == 0 && gun.sources[0].Stops == 0, "single-source native clips not converted into looping playback");
        });
        test("gun audio native pitch climb and source clips are preserved", () =>
        {
            Clock(0); var gun = LoopGun(); gun.modifyPitch = true; gun.pitch = 1.4f; gun.startPitch = .6f; gun.pitchClimbRate = .3f;
            gun.sources[1].pitch = gun.startPitch; var clip = gun.sources[1].clip; var audio = new ReplayGunAudioPlayback(gun); audio.PlayShot(0);
            float before = gun.sources[1].pitch; Clock(.01, .01); audio.Update(.01, false);
            Near(gun.sources[1].pitch, before + .003f);
            Check(ReferenceEquals(gun.sources[1].clip, clip) && ReferenceEquals(clip, gun.fireSustained), "native loop clip unchanged");
            Clock(.3, .01); audio.Update(.3, false); before = gun.sources[1].pitch;
            Clock(.31, .01); audio.Update(.31, false); Near(gun.sources[1].pitch, before - .01f);
            var single = SingleGun(); single.pitch = .85f; single.heatEnabled = true; single.recoilSound = new AudioSource {pitch = .7f};
            var one = new ReplayGunAudioPlayback(single); one.PlayShot(0); Near(single.sources[0].pitch, .85); Near(single.recoilSound.pitch, .7);
        });
        test("gun audio paused loop resumes before another shot without a second start clip", () =>
        {
            Clock(0); var gun = LoopGun(); var audio = new ReplayGunAudioPlayback(gun); audio.PlayShot(0);
            Clock(.04); audio.Update(.04, true); audio.Update(.04, true);
            Check(!gun.sources[1].isPlaying && gun.sources[1].IsPaused && gun.sources[1].Pauses == 1, "pause flags retained across paused frames");
            Check(audio.PlayShot(.04, false), "stepped shot consumed");
            Check(Starts(gun) == 1 && gun.sources[1].Plays == 1 && gun.sources[1].Resumes == 1, "source resumed before native isPlaying test");
            audio.Update(.04, true); Check(gun.sources[1].IsPaused, "step stays paused after presentation");
            audio.Update(.04, false); Check(gun.sources[1].isPlaying && Starts(gun) == 1, "normal resume does not create an onset");
        });
        test("gun audio pause resumes only cached playing shot and recoil sources", () =>
        {
            Clock(0); var gun = SingleGun(); gun.recoilSound = new AudioSource(); var audio = new ReplayGunAudioPlayback(gun);
            audio.Update(0, true); audio.Update(0, false);
            Check(gun.sources[0].Resumes == 0 && gun.recoilSound.Resumes == 0, "silent sources are never started by resume");
            audio.PlayShot(0); audio.Update(0, true); audio.Update(0, false);
            Check(gun.sources[0].Pauses == 1 && gun.sources[0].Resumes == 1 && gun.recoilSound.Pauses == 1 && gun.recoilSound.Resumes == 1, "both native shot and recoil resume exactly once");
        });
        test("gun audio seek and teardown stop only distinct cached weapon sources", () =>
        {
            Clock(0); var gun = LoopGun(); var engine = new AudioSource(); engine.Play();
            gun.recoilSound = gun.sources[0]; var audio = new ReplayGunAudioPlayback(gun); audio.PlayShot(0); audio.Update(0, true); audio.Stop();
            Check(gun.sources[0].Stops == 1 && gun.sources[1].Stops == 1 && !gun.sources[1].IsPaused, "deduplicated cached sources stopped and unpaused");
            Check(engine.isPlaying && engine.Stops == 0 && engine.Pauses == 0, "unrelated engine sound never touched");
            audio.Update(0, false); Check(!gun.sources[1].isPlaying && gun.sources[1].Resumes == 0, "cleanup cannot resume a stopped source");
            var replacement = new ReplayGunAudioPlayback(gun); replacement.PlayShot(0);
            Check(gun.sources[1].isPlaying && Starts(gun) == 2, "fresh seek actor starts independently at same time");
        });
        test("gun audio invalid and stale clocks cannot change sound or native timestamps", () =>
        {
            Clock(1); var gun = LoopGun(); var audio = new ReplayGunAudioPlayback(gun); audio.PlayShot(1);
            float last = gun.lastFired; int loops = gun.LoopSoundCalls;
            foreach (double bad in new[] {double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0})
            {
                Check(!audio.PlayShot(bad, false), "invalid shot rejected"); audio.Update(bad, false);
                Near(gun.lastFired, last); Check(gun.ShotSoundCalls == 1 && gun.LoopSoundCalls == loops, "invalid time cannot poison native audio");
            }
            Check(!audio.PlayShot(.5, false) && gun.ShotSoundCalls == 1, "stale shot cannot rewind burst");
            Clock(1.04); audio.Update(1.04, false); Check(gun.sources[1].isPlaying, "valid clock continues after rejected input");
        });
        test("gun audio retains native visibility eligibility and dormant extra source profiles", () =>
        {
            Clock(0); var gun = LoopGun(); var extra = new AudioSource {pitch = .77f, volume = .32f};
            gun.sources = new[] {gun.sources[0], gun.sources[1], extra}; gun.attachedUnit.displayDetail = .5f;
            var audio = new ReplayGunAudioPlayback(gun); audio.PlayShot(0);
            Check(Starts(gun) == 0 && gun.sources[1].Plays == 0, "native detail gate remains authoritative");
            gun.attachedUnit.displayDetail = 1; Clock(.01); audio.PlayShot(.01);
            Check(Starts(gun) == 1 && gun.sources[1].isPlaying && extra.Plays == 0, "native source count chooses sustained profile");
            Near(extra.pitch, .77); Near(extra.volume, .32); audio.Stop(); Check(extra.Stops == 1, "cleanup includes every cached native weapon source");
        });
    }

    private static Gun LoopGun(float interval = .08f)
    {
        var gun = new Gun {fireInterval = interval};
        gun.sources = new[] {new AudioSource(), new AudioSource {loop = true, clip = gun.fireSustained}};
        return gun;
    }
    private static Gun SingleGun() => new Gun {sources = new[] {new AudioSource()}};
    private static void Clock(double time, double step = 1d / 60)
    { Time.timeSinceLevelLoad = (float)time; Time.fixedDeltaTime = .02f; Time.deltaTime = (float)step; }
    private static int Starts(Gun gun) => gun.sources[0].OneShots.Count(clip => ReferenceEquals(clip, gun.fireStart));
    private static int Ends(Gun gun) => gun.sources[0].OneShots.Count(clip => ReferenceEquals(clip, gun.fireEnd));
    private static void Near(double actual, double expected)
    { if (Math.Abs(actual - expected) > .00001) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
