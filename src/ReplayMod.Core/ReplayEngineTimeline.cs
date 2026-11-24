using System;
using System.Collections.Generic;
using ReplayMod.Events.ConcreteEvents;

namespace ReplayMod.Core;

// Engine samples share the replay clock. Keep the discrete ignition switch on
// the previous sample, and only blend RPM across a short uninterrupted run.
internal sealed class ReplayEngineTimeline
{
    private sealed class Sample
    {
        public double time;
        public bool ignition;
        public float[] rpm;
    }

    private readonly List<Sample> samples = new List<Sample>();
    private static readonly Func<Sample, double> TimeOf = sample => sample.time;
    private float[] output = Array.Empty<float>();

    public void Add(AircraftStatusEvent e) => Add(e.Time,e.ignition,e.rpm);
    public void Add(EngineCoreStateEvent e) => Add(e.Time,e.ignition,e.rpm);
    private void Add(double time,bool ignition,float[] rpm)
    {
        if (rpm == null || double.IsNaN(time) || double.IsInfinity(time)) return;
        foreach (float value in rpm) if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) return;
        SnapshotHistory.Insert(samples, new Sample { time = time, ignition = ignition, rpm = (float[])rpm.Clone() }, TimeOf);
    }

    public bool SampleAt(double time, out bool ignition, out float[] rpm)
    {
        ignition = false;
        rpm = null;
        if (!SnapshotHistory.Locate(samples, time, TimeOf, out int left, out int right, out float fraction)) return false;
        var previous = samples[left];
        var next = samples[right];
        ignition = previous.ignition;
        if (output.Length != previous.rpm.Length) output = new float[previous.rpm.Length];
        bool blend = right != left && previous.ignition == next.ignition &&
            next.time - previous.time <= 0.5 && previous.rpm.Length == next.rpm.Length;
        for (int i = 0; i < output.Length; i++)
            output[i] = blend ? previous.rpm[i] + (next.rpm[i] - previous.rpm[i]) * fraction : previous.rpm[i];
        rpm = output;
        SnapshotHistory.Trim(samples, left, 32);
        return true;
    }
}
