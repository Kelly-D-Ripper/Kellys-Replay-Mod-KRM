using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReplayMod.Core;

// Unity cannot cancel an already started native scene operation. A cancelled or
// timed-out caller releases its UI, but the next transition must still join the
// original operation before it may touch the native world.
public sealed class ReplayOperationBarrier
{
    private readonly SemaphoreSlim gate = new SemaphoreSlim(1,1);
    private Task inFlight;
    public bool IsBusy => gate.CurrentCount==0 || (inFlight!=null&&!inFlight.IsCompleted);

    public async Task RunAsync(Func<Task> action, TimeSpan budget, CancellationToken cancellation=default)
    {
        if(action==null)throw new ArgumentNullException(nameof(action));
        if(budget<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(budget));
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            deadline.CancelAfter(budget);
            bool entered=false;
            try
            {
                await gate.WaitAsync(deadline.Token);entered=true;
                if(inFlight!=null)
                {
                    try { await WaitAsync(inFlight,deadline.Token); }
                    catch when(inFlight.IsCompleted) { /* Observe an earlier failed operation, then allow recovery. */ }
                    deadline.Token.ThrowIfCancellationRequested();
                    inFlight=null;
                }
                deadline.Token.ThrowIfCancellationRequested();
                inFlight=action()??Task.CompletedTask;
                // A caller can leave before native IO finishes; observe its error
                // even when no later transition comes along to join it.
                _=inFlight.ContinueWith(t=> {var observed=t.Exception;},CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                await WaitAsync(inFlight,deadline.Token);
            }
            catch(OperationCanceledException) when(deadline.IsCancellationRequested&&!cancellation.IsCancellationRequested)
            { throw new TimeoutException("The replay scene transition timed out. A previous native load must finish before another replay can open."); }
            finally
            {
                if(entered)
                {
                    if(inFlight?.IsCompleted==true)inFlight=null;
                    gate.Release();
                }
            }
        }
    }

    private static async Task WaitAsync(Task task,CancellationToken cancellation)
    {
        if(task.IsCompleted){await task;return;}
        var interrupted=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(cancellation.Register(()=>interrupted.TrySetCanceled()))
        {
            if(await Task.WhenAny(task,interrupted.Task)!=task)cancellation.ThrowIfCancellationRequested();
            await task;
        }
    }
}
