using System;

namespace ReplayMod.Core;

// The requested playback state survives scene reloads and temporary VFX warm-up.
// Requests are issued on Unity's main thread; only the latest queued seek is applied.
public sealed class PlaybackControlState
{
    private long revision;
    public bool IsPaused { get; private set; } = true;
    public bool IsSeeking { get; private set; }
    public float Speed { get; private set; } = 1f;
    // Fast playback advances recorded time separately; never accelerate Unity's
    // physics and effects clock beyond its normal rate.
    public float ClockScale => IsPaused || IsSeeking ? 0f : Math.Min(1f, Speed);
    public const float MaximumSpeed = 480f;
    public const double MaximumFastStep = 1.0;
    public const int MaximumFastStepsPerFrame = 16;
    public static double FrameTarget(double current, double delta, float speed)
    {
        if(double.IsNaN(delta)||double.IsInfinity(delta)||delta<=0||float.IsNaN(speed)||float.IsInfinity(speed)||speed<=0)return current;
        // Do not carry unlimited debt after a stall: achieve the requested rate
        // only when the reader and the main thread can actually keep up.
        return current+Math.Min(delta*speed,MaximumFastStep*MaximumFastStepsPerFrame);
    }
    public static double FastStepTarget(double current,double target) => Math.Min(target,current+MaximumFastStep);
    public static double LimitTime(double requested,double duration,double? end)
    {
        double limit=duration;
        if(end.HasValue&&!double.IsNaN(end.Value)&&!double.IsInfinity(end.Value))limit=Math.Min(limit,Math.Max(0,end.Value));
        return Math.Min(requested,limit);
    }

    public void Pause() => IsPaused = true;
    public void Play() => IsPaused = false;
    public void Toggle() => IsPaused = !IsPaused;
    public void SetSpeed(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        Speed = Math.Max(.1f, Math.Min(MaximumSpeed, value));
    }

    // Native slow-motion requests change the same requested speed as the studio.
    // A speed request must not implicitly resume a paused replay or a seek.
    public bool ApplyNativeTimeFactor(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return false;
        if (value <= 0) Pause();
        else SetSpeed(value);
        return true;
    }

    public long RequestSeek(bool? resumeAfter)
    {
        if (resumeAfter.HasValue) IsPaused = !resumeAfter.Value;
        IsSeeking = true;
        return ++revision;
    }
    public bool IsLatest(long request) => IsSeeking && request == revision;
    public bool CompleteSeek(long request)
    {
        if (!IsLatest(request)) return false;
        IsSeeking = false;
        return true;
    }
    public void CancelSeeks()
    {
        ++revision;
        IsSeeking = false;
        Pause();
    }
}

// Only resume the exact replay that was playing when the native menu opened.
public sealed class ReplayPauseMenuState<T> where T:class
{
    private T player;
    private bool resume;
    public void Open(T current,bool wasPlaying) {player=current;resume=current!=null&&wasPlaying;}
    public bool Close(T current,bool eligible)
    {
        bool result=eligible&&resume&&ReferenceEquals(player,current);
        Clear();return result;
    }
    public void Clear(){player=null;resume=false;}
}
