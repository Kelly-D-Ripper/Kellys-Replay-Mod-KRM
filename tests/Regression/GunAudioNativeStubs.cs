// Independently written external behavioral fakes, not game implementations.
// They model only the source selection, burst thresholds, pitch and pause
// contracts needed to exercise the linked production audio helper.
namespace UnityEngine
{
    public static class Time
    {
        public static float timeSinceLevelLoad, fixedDeltaTime = .02f, deltaTime = 1f / 60;
    }
    public sealed class AudioClip
    {
        public readonly string name;
        public float length = 1;
        public AudioClip(string name) { this.name = name; }
    }
    public sealed class AudioSource
    {
        private bool playing, paused;
        public bool isPlaying => playing && !paused;
        public bool IsPaused => paused;
        public float pitch = 1, volume = 1, time;
        public bool loop;
        public AudioClip clip;
        public int Plays, Pauses, Resumes, Stops;
        public readonly List<AudioClip> OneShots = new();
        public void Play() { Plays++; playing = true; paused = false; }
        public void PlayOneShot(AudioClip value) { OneShots.Add(value); playing = true; paused = false; }
        public void Pause() { Pauses++; paused = true; }
        public void UnPause() { Resumes++; paused = false; }
        public void Stop() { Stops++; playing = false; paused = false; }
    }
}

public sealed class GunAudioOwner { public float displayDetail = 1; }
public sealed class Gun
{
    public UnityEngine.AudioSource[] sources;
    public UnityEngine.AudioSource recoilSound;
    public GunAudioOwner attachedUnit = new();
    public UnityEngine.AudioClip fireStart = new("start"), fireSustained = new("loop"), fireEnd = new("end");
    public UnityEngine.AudioClip[] fireSounds = { new("shot") };
    public float lastFired = -1000, fireInterval = .08f, pitch = 1, pitchVariation;
    public float startPitch = .5f, pitchClimbRate = 1;
    public bool modifyPitch, heatEnabled;
    public int ShotSoundCalls, LoopSoundCalls;

    public void TrackFiringVisibility() => throw new Exception("Replay audio must not invoke live visibility work");
    public void ShotSound()
    {
        ShotSoundCalls++;
        if (attachedUnit.displayDetail < 1) return;
        if (sources.Length == 1) { EmitDiscreteShot(); return; }
        if (Sustained.isPlaying) return;
        sources[0].PlayOneShot(fireStart);
        // The fake deliberately picks a fixed valid clip offset instead of
        // reproducing native random selection or Unity audio scheduling.
        if (IdleAge < fireInterval * 1.1f + UnityEngine.Time.deltaTime)
        { Sustained.Play(); Sustained.time = Sustained.clip.length * .5f; }
    }

    public void LoopSounds()
    {
        LoopSoundCalls++;
        bool running = Sustained.isPlaying;
        if (modifyPitch) AdvancePitch(running);
        if (!running || IdleAge <= UnityEngine.Time.deltaTime + fireInterval) return;
        Sustained.Stop(); sources[0].PlayOneShot(fireEnd);
    }

    private UnityEngine.AudioSource Sustained => sources[1];
    private float IdleAge => UnityEngine.Time.timeSinceLevelLoad - lastFired;
    private void EmitDiscreteShot()
    {
        sources[0].pitch = pitch + pitchVariation * .5f;
        sources[0].PlayOneShot(fireSounds[0]);
        if (recoilSound == null) return;
        if (!heatEnabled) recoilSound.pitch = 1;
        recoilSound.Play();
    }
    private void AdvancePitch(bool running)
    {
        float current = Sustained.pitch;
        float target = running ? pitch : startPitch;
        float step = UnityEngine.Time.deltaTime * (running ? pitchClimbRate : -1);
        bool moving = running ? current < target : current > target;
        Sustained.pitch = moving ? current + step : target;
    }
}
