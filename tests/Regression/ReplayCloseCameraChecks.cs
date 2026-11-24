using ReplayMod.Core;

public static class ReplayCloseCameraChecks
{
    private sealed class Lens { public float Near = 1; public bool Alive = true; public int Writes; }
    private static readonly Func<Lens, bool> Alive = lens => lens.Alive;
    private static readonly Func<Lens, float> Read = lens => lens.Near;
    private static readonly Action<Lens, float> Write = (lens, value) => { lens.Near = value; lens.Writes++; };
    public static int Run()
    {
        int passed = 0;
        Check("close lens captures and restores external near plane once", () =>
        {
            var state = new ReplayCloseCameraState<Lens>(); var lens = new Lens();
            state.Ensure(lens, .05f, Alive, Read, Write);
            for (int n = 0; n < 100; n++) state.Ensure(lens, .05f, Alive, Read, Write);
            Require(lens.Near == .05f && lens.Writes == 1 && state.HasOverride, "steady replay has one setting write");
            Require(state.Clear(Alive, Read, Write) == 0 && lens.Near == 1 && !state.HasOverride, "exact external value restored");
            state.Clear(Alive, Read, Write); Require(lens.Writes == 2, "repeated cleanup is inert");
        });
        Check("camera mode changes update the restoration baseline", () =>
        {
            var state = new ReplayCloseCameraState<Lens>(); var lens = new Lens();
            state.Ensure(lens, .05f, Alive, Read, Write); lens.Near = .2f;
            state.Ensure(lens, .05f, Alive, Read, Write);
            state.Clear(Alive, Read, Write); Require(lens.Near == .2f, "cockpit near plane restored after native switch");
        });
        Check("close lens never increases a native smaller near plane", () =>
        {
            var state = new ReplayCloseCameraState<Lens>(); var lens = new Lens { Near = .01f };
            state.Ensure(lens, .05f, Alive, Read, Write); state.Clear(Alive, Read, Write);
            Require(lens.Near == .01f && lens.Writes == 0 && !state.HasOverride, "custom close camera retained");
        });
        Check("scene camera replacement restores old live owner and captures new owner", () =>
        {
            var state = new ReplayCloseCameraState<Lens>(); var old = new Lens(); var next = new Lens { Near = .2f };
            state.Ensure(old, .05f, Alive, Read, Write); state.Ensure(next, .05f, Alive, Read, Write);
            Require(old.Near == 1 && next.Near == .05f, "one active camera ownership");
            next.Alive = false; state.Clear(Alive, Read, Write);
            Require(next.Writes == 1 && !state.HasOverride, "destroyed scene camera is released without writing");
        });
        Check("cleanup preserves a camera change made after our last update", () =>
        {
            var state = new ReplayCloseCameraState<Lens>(); var lens = new Lens();
            state.Ensure(lens, .05f, Alive, Read, Write); lens.Near = .3f;
            state.Clear(Alive, Read, Write); Require(lens.Near == .3f && lens.Writes == 1, "later native/mod value wins");
            state.Ensure(lens, .05f, Alive, Read, Write); lens.Near = .01f;
            state.Ensure(lens, .05f, Alive, Read, Write); state.Clear(Alive, Read, Write);
            Require(lens.Near == .01f && !state.HasOverride, "native smaller plane releases override");
        });
        Check("invalid settings and restore failure release ownership safely", () =>
        {
            var state = new ReplayCloseCameraState<Lens>(); var lens = new Lens();
            foreach (float invalid in new[] { 0f, -1, float.NaN, float.PositiveInfinity })
            { state.Ensure(lens, invalid, Alive, Read, Write); Require(lens.Near == 1 && !state.HasOverride, "invalid desired plane ignored"); }
            state.Ensure(lens, .05f, Alive, Read, Write);
            Require(state.Clear(Alive, Read, (target, value) => throw new InvalidOperationException()) == 1 && !state.HasOverride, "failing native restoration is reported and released");
        });
        return passed;
        void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
