using System;
using System.Threading;
using System.Threading.Tasks;
using ReplayMod.Core;

internal static class ReplayOperationBarrierChecks
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    public static void Run(Action<string, Action> test)
    {
        test("scene operation barrier rejects invalid or pre-cancelled requests without starting native work", () => Complete(InvalidRequests()));
        test("scene operation barrier recovers from synchronous, asynchronous and native cancellation failures", () => Complete(FailedOperations()));
        test("cancelled scene caller cannot overlap a continuing native operation", () => Complete(CancelledCaller()));
        test("timed out scene caller and queued timeout retain the original native barrier", () => Complete(TimedOutCaller()));
        test("late failed native scene operation is observed before recovery starts", () => Complete(LateFailure()));
        test("cancelled queued scene request does not release another owner's gate", () => Complete(CancelledWaiter()));
        test("200 repeated scene cancellation timeout and failure cycles preserve native serialization", () => Complete(RepeatedCycles(), 20));
    }

    private static void Complete(Task task, int seconds = 10) => task.WaitAsync(TimeSpan.FromSeconds(seconds)).GetAwaiter().GetResult();
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static async Task InvalidRequests()
    {
        var barrier = new ReplayOperationBarrier();
        int calls = 0;
        Func<Task> action = () => { calls++; return Task.CompletedTask; };
        await Throws<ArgumentNullException>(() => barrier.RunAsync(null, Budget));
        await Throws<ArgumentOutOfRangeException>(() => barrier.RunAsync(action, TimeSpan.Zero));
        await Throws<ArgumentOutOfRangeException>(() => barrier.RunAsync(action, TimeSpan.FromMilliseconds(-1)));
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Throws<OperationCanceledException>(() => barrier.RunAsync(action, Budget, cancellation.Token));
        }
        Check(calls == 0 && !barrier.IsBusy, "invalid/pre-cancelled requests invoked native work or leaked the gate");
        await barrier.RunAsync(action, Budget);
        Check(calls == 1 && !barrier.IsBusy, "barrier unavailable after rejected requests");
    }

    private static async Task FailedOperations()
    {
        var barrier = new ReplayOperationBarrier();
        await Throws<InvalidOperationException>(() => barrier.RunAsync(() => throw new InvalidOperationException("sync native failure"), Budget));
        Check(!barrier.IsBusy, "synchronous failure leaked the gate");
        var failed = Signal(); failed.SetException(new InvalidOperationException("async native failure"));
        await Throws<InvalidOperationException>(() => barrier.RunAsync(() => failed.Task, Budget));
        Check(!barrier.IsBusy, "asynchronous failure leaked the gate");
        using (var nativeCancellation = new CancellationTokenSource())
        {
            nativeCancellation.Cancel();
            await Throws<OperationCanceledException>(() => barrier.RunAsync(() => Task.FromCanceled(nativeCancellation.Token), Budget));
        }
        Check(!barrier.IsBusy, "native cancellation leaked the gate");
        await barrier.RunAsync(() => null, Budget);
        await barrier.RunAsync(() => Task.CompletedTask, Budget);
        Check(!barrier.IsBusy, "recovery failed after completed/null action");
    }

    private static async Task CancelledCaller()
    {
        var barrier = new ReplayOperationBarrier();
        var release = Signal();
        var native = new NativeTracker();
        using var cancellation = new CancellationTokenSource();
        var caller = barrier.RunAsync(() => native.Hold(release.Task), Budget, cancellation.Token);
        Check(native.Active == 1 && barrier.IsBusy, "first native operation was not held");
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => caller);
        Check(native.Active == 1 && barrier.IsBusy, "caller cancellation released continuing native work");

        int recoveryCalls = 0;
        var recovery = barrier.RunAsync(() => { recoveryCalls++; return native.Instant(); }, Budget);
        Check(recoveryCalls == 0 && !recovery.IsCompleted, "recovery overlapped cancelled caller's native work");
        release.SetResult(true);
        await recovery;
        Check(recoveryCalls == 1 && native.Maximum == 1 && native.Active == 0 && !barrier.IsBusy, "cancellation recovery leaked or overlapped native work");
    }

    private static async Task TimedOutCaller()
    {
        var barrier = new ReplayOperationBarrier();
        var release = Signal();
        var native = new NativeTracker();
        await Throws<TimeoutException>(() => barrier.RunAsync(() => native.Hold(release.Task), TimeSpan.FromMilliseconds(5)));
        Check(native.Active == 1 && barrier.IsBusy, "timeout released a continuing native operation");
        int queuedCalls = 0;
        await Throws<TimeoutException>(() => barrier.RunAsync(() => { queuedCalls++; return native.Instant(); }, TimeSpan.FromMilliseconds(5)));
        Check(queuedCalls == 0 && native.Active == 1 && barrier.IsBusy, "queued timeout started overlapping native work");
        release.SetResult(true);
        await barrier.RunAsync(native.Instant, Budget);
        Check(native.Maximum == 1 && native.Active == 0 && !barrier.IsBusy, "timeout recovery leaked or overlapped native work");
    }

    private static async Task LateFailure()
    {
        var barrier = new ReplayOperationBarrier();
        var release = Signal();
        var native = new NativeTracker();
        using var cancellation = new CancellationTokenSource();
        var caller = barrier.RunAsync(() => native.Hold(release.Task), Budget, cancellation.Token);
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => caller);
        int recoveryCalls = 0;
        var recovery = barrier.RunAsync(() => { recoveryCalls++; return native.Instant(); }, Budget);
        Check(recoveryCalls == 0, "recovery began before old native failure settled");
        release.SetException(new InvalidOperationException("native failure after its caller left"));
        await recovery;
        Check(recoveryCalls == 1 && native.Maximum == 1 && native.Active == 0 && !barrier.IsBusy, "late failed operation poisoned recovery");
    }

    private static async Task CancelledWaiter()
    {
        var barrier = new ReplayOperationBarrier();
        var release = Signal();
        var native = new NativeTracker();
        var owner = barrier.RunAsync(() => native.Hold(release.Task), Budget);
        using var cancellation = new CancellationTokenSource();
        int cancelledCalls = 0;
        var waiter = barrier.RunAsync(() => { cancelledCalls++; return native.Instant(); }, Budget, cancellation.Token);
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => waiter);
        Check(cancelledCalls == 0 && barrier.IsBusy && native.Active == 1, "cancelled waiter released its owner's gate");
        int nextCalls = 0;
        var next = barrier.RunAsync(() => { nextCalls++; return native.Instant(); }, Budget);
        Check(nextCalls == 0, "third request entered before owner finished");
        release.SetResult(true);
        await Task.WhenAll(owner, next);
        Check(nextCalls == 1 && native.Maximum == 1 && native.Active == 0 && !barrier.IsBusy, "queued cancellation leaked or over-released the gate");
    }

    private static async Task RepeatedCycles()
    {
        var barrier = new ReplayOperationBarrier();
        var native = new NativeTracker();
        for (int cycle = 0; cycle < 200; cycle++)
        {
            var release = Signal();
            using var cancellation = new CancellationTokenSource();
            int mode = cycle % 5;
            var caller = barrier.RunAsync(() => native.Hold(release.Task), mode == 1 ? TimeSpan.FromMilliseconds(2) : Budget, cancellation.Token);
            Check(native.Active == 1, "cycle failed to acquire native ownership: " + cycle);
            if (mode == 0 || mode == 2)
            {
                cancellation.Cancel();
                await Throws<OperationCanceledException>(() => caller);
            }
            else if (mode == 1) await Throws<TimeoutException>(() => caller);
            else if (mode == 3)
            {
                release.SetException(new InvalidOperationException("native failure " + cycle));
                await Throws<InvalidOperationException>(() => caller);
            }
            else
            {
                using var queuedCancellation = new CancellationTokenSource();
                int rejectedCalls = 0;
                var rejected = barrier.RunAsync(() => { rejectedCalls++; return native.Instant(); }, Budget, queuedCancellation.Token);
                queuedCancellation.Cancel();
                await Throws<OperationCanceledException>(() => rejected);
                Check(rejectedCalls == 0 && native.Active == 1, "queued cancellation ran native work: " + cycle);
            }

            int recoveryCalls = 0;
            var recovery = barrier.RunAsync(() => { recoveryCalls++; return native.Instant(); }, Budget);
            if (mode != 3) Check(recoveryCalls == 0 && !recovery.IsCompleted, "cycle recovery overlapped original native work: " + cycle);
            if (mode == 2) release.TrySetException(new InvalidOperationException("late native failure " + cycle));
            else release.TrySetResult(true);
            if (mode == 4) await caller;
            await recovery;
            Check(recoveryCalls == 1 && native.Maximum == 1 && native.Active == 0 && !barrier.IsBusy, "cycle leaked or overlapped native work: " + cycle);
        }
        Check(native.Started == 400 && native.Completed == 400, "repeated recovery lost native operations");
    }

    private sealed class NativeTracker
    {
        private int active, maximum, started, completed;
        public int Active => Volatile.Read(ref active);
        public int Maximum => Volatile.Read(ref maximum);
        public int Started => Volatile.Read(ref started);
        public int Completed => Volatile.Read(ref completed);
        private void Enter()
        {
            int value = Interlocked.Increment(ref active);
            Interlocked.Increment(ref started);
            int observed;
            do { observed = Volatile.Read(ref maximum); }
            while (value > observed && Interlocked.CompareExchange(ref maximum, value, observed) != observed);
        }
        private void Leave() { Interlocked.Decrement(ref active); Interlocked.Increment(ref completed); }
        public async Task Hold(Task release)
        {
            Enter();
            try { await release; }
            finally { Leave(); }
        }
        public Task Instant()
        {
            Enter();
            try { Check(Active == 1, "more than one native scene operation active"); }
            finally { Leave(); }
            return Task.CompletedTask;
        }
    }
}
