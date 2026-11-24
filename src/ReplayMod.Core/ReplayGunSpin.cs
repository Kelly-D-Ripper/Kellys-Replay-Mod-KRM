using System;

namespace ReplayMod.Core;

// A shot refreshes native Gun.currentSpinRate to one, then SpinBarrels reduces
// it by one per second. Integrate that envelope in replay seconds, rather than
// starting the native async task or accumulating frame-sized rotations.
public sealed class ReplayGunSpin
{
    public bool HasState { get; private set; }
    public double Time { get; private set; }
    public double Phase { get; private set; }
    public double Speed { get; private set; }

    public bool RecordShot(double time)
    {
        // A rolling cut can put a decaying anchor and a fresh retained shot at
        // the same new timestamp. Refresh that partial speed; pellet duplicates
        // already at full speed remain idempotent.
        if (!Finite(time) || time < 0 || (HasState && (time < Time || (time == Time && Speed == 1)))) return false;
        double phase = PhaseAt(time);
        Time = time; Phase = phase; Speed = 1; HasState = true; return true;
    }

    public bool Restore(double time, double phase, double speed)
    {
        if (!Valid(time, phase, speed)) return false;
        Time = time; Phase = phase; Speed = speed; HasState = true; return true;
    }

    public double PhaseAt(double time)
    {
        if (!HasState || !Finite(time)) return HasState ? Phase : 0;
        double elapsed = Math.Max(0, Math.Min(Speed, time - Time));
        return Phase + Speed * elapsed - .5 * elapsed * elapsed;
    }

    public double SpeedAt(double time) => !HasState || !Finite(time) ? 0 : Math.Max(0, Speed - Math.Max(0, time - Time));

    public static bool Valid(double time, double phase, double speed) =>
        Finite(time) && time >= 0 && Finite(phase) && phase >= 0 && phase <= 1e12 && Finite(speed) && speed >= 0 && speed <= 1;

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
