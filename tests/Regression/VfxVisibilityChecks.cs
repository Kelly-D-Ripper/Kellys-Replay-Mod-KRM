using ReplayMod.Core;

public static class VfxVisibilityChecks
{
    private sealed class Target
    {
        public int id, value;
        public bool alive = true;
    }

    public static void Run(Action<string, Action> test)
    {
        test("VFX visibility ownership captures once and restores its original setting", () =>
        {
            var cache = new ReplayOwnedOverrideCache<Target, int>(4);
            var target = new Target { value = 7 }; int captures = 0, writes = 0;
            Require(cache.TryApply(target, item => { captures++; return item.value; }, (item, old) => { item.value = 3; writes++; }), "first ownership applies");
            Require(!cache.TryApply(target, item => { captures++; return item.value; }, (item, old) => { item.value = 2; writes++; }), "repeat registration does not overwrite original snapshot");
            Require(captures == 1 && writes == 1 && target.value == 3 && cache.Count == 1, "no repeated native queries or mutations");
            Require(cache.Restore(item => item.alive, (item, original) => item.value = original) == 0 && target.value == 7 && cache.Count == 0, "reset restores original and releases ownership");
        });
        test("VFX visibility cache is bounded and destroyed references release capacity", () =>
        {
            var cache = new ReplayOwnedOverrideCache<Target, int>(2);
            var first = new Target { value = 5 }; var second = new Target { value = 6 }; var third = new Target { value = 7 };
            cache.TryApply(first, item => item.value, (item, old) => item.value = 3);
            cache.TryApply(second, item => item.value, (item, old) => item.value = 3);
            Require(!cache.TryApply(third, item => throw new Exception("capacity must prevent reads"), (item, old) => throw new Exception("capacity must prevent writes")) && third.value == 7, "budget cannot mutate an unowned target");
            first.alive = false; cache.Prune(item => item.alive);
            Require(cache.Count == 1 && cache.TryApply(third, item => item.value, (item, old) => item.value = 3), "dead object removed and freed slot reusable");
            cache.Restore(item => item.alive, (item, original) => item.value = original);
            Require(second.value == 6 && third.value == 7 && first.value == 3, "destroyed target is never accessed during restore");
        });
        test("VFX visibility object reuse cannot inherit another lifetime's original state", () =>
        {
            var cache = new ReplayOwnedOverrideCache<Target, int>(4);
            var old = new Target { id = 17, value = 0 }; var replacement = new Target { id = 17, value = 2 };
            cache.TryApply(old, item => item.value, (item, original) => item.value = 3); old.alive = false;
            cache.TryApply(replacement, item => item.value, (item, original) => item.value = 3);
            cache.Restore(item => item.alive, (item, original) => item.value = original);
            Require(replacement.value == 2 && old.value == 3 && cache.Count == 0, "reference identity and liveness isolate restored settings");
        });
        test("VFX visibility reset continues after an individual native restore failure", () =>
        {
            var cache = new ReplayOwnedOverrideCache<Target, int>(4);
            var bad = new Target { value = 8 }; var good = new Target { value = 9 };
            cache.TryApply(bad, item => item.value, (item, old) => item.value = 3);
            cache.TryApply(good, item => item.value, (item, old) => item.value = 3);
            int failures = cache.Restore(item => item.alive, (item, original) =>
            { if (ReferenceEquals(item, bad)) throw new InvalidOperationException("simulated native teardown failure"); item.value = original; });
            Require(failures == 1 && good.value == 9 && cache.Count == 0, "one failure cannot strand another live override or retain old ownership");
        });
        test("VFX visibility near fade selection respects native disabled and invalid ranges", () =>
        {
            Require(ReplayVfxPolicy.HasNearCameraFade(10, .05f), "native ribbon 10-to-30 metre range is selected");
            Require(!ReplayVfxPolicy.HasNearCameraFade(0, float.PositiveInfinity), "native disabled fade remains unchanged");
            Require(!ReplayVfxPolicy.HasNearCameraFade(float.NaN, .05f) && !ReplayVfxPolicy.HasNearCameraFade(10, 0) && !ReplayVfxPolicy.HasNearCameraFade(10, -1) && !ReplayVfxPolicy.HasNearCameraFade(float.PositiveInfinity, 1), "invalid or non-fading shader ranges do not get overrides");
            // URP's CameraFade alpha is saturate((eyeDepth-near)*inverseRange).
            Require(Fade(5, 10, .05f) == 0 && Fade(20, 10, .05f) == .5f && Fade(5, 0, 1000) == 1, "confirmed native close-camera disappearance and scoped override preserve visible alpha");
        });
        test("VFX distant fragments only restore a missing owned replay effect", () =>
        {
            Require(ReplayVfxPolicy.ShouldRestoreDistantFragments(true, false), "owned distant detach restores the native distance-skipped effect");
            Require(!ReplayVfxPolicy.ShouldRestoreDistantFragments(true, true), "nearby native effect must not repeat joint-broken callbacks");
            Require(!ReplayVfxPolicy.ShouldRestoreDistantFragments(false, false) && !ReplayVfxPolicy.ShouldRestoreDistantFragments(false, true), "live or unrelated parts retain native distance policy");
        });
    }

    private static float Fade(float depth, float near, float inverseRange) => Math.Clamp((depth - near) * inverseRange, 0, 1);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
