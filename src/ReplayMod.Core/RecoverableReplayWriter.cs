using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReplayMod.Events;

namespace ReplayMod.Core;

public sealed class ReplayWriterResult
{
    public string Path,Failure;
    public long Events;
    public double Duration;
    public bool Completed;
}

// The worker exclusively owns its stream and completion header. A bounded stop
// wait never closes that stream underneath an outstanding disk write.
public sealed class RecoverableReplayWriter
{
    public const long DefaultMaximumQueuedBytes=64L*1024*1024;
    private sealed class Entry{internal byte[] Bytes;internal double Time;}
    private readonly ConcurrentQueue<Entry> queue=new ConcurrentQueue<Entry>();
    private readonly object gate=new object();
    private readonly AutoResetEvent signal=new AutoResetEvent(false);
    private readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<ReplayWriterResult> finished=new TaskCompletionSource<ReplayWriterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long maximumBytes;
    private readonly Func<string,Stream> open;
    private readonly bool publish;
    private long queuedBytes;
    private string failure;
    private bool stopping,started;
    private bool ended;
    private double duration,lastAccepted=-1;
    public string TargetPath{get;}
    public string PartialPath=>TargetPath+".partial";
    public long QueuedBytes=>Interlocked.Read(ref queuedBytes);
    public string Failure=>Volatile.Read(ref failure);
    public Task Ready=>ready.Task;
    public Task<ReplayWriterResult> Completion=>finished.Task;
    public RecoverableReplayWriter(string targetPath,long maximumQueuedBytes=DefaultMaximumQueuedBytes,Func<string,Stream> streamFactory=null,bool publishCompletedFile=true)
    {
        if(maximumQueuedBytes<1)throw new ArgumentOutOfRangeException(nameof(maximumQueuedBytes));
        TargetPath=System.IO.Path.GetFullPath(targetPath);maximumBytes=maximumQueuedBytes;publish=publishCompletedFile;
        open=streamFactory??(path=>new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536));
    }
    public bool TryEnqueue(IReplayEvent value)
    {
        lock(gate)
        {
            if(stopping||ended||Failure!=null)return false;
            try
            {
                byte[] frame=ReplayRecordTransport.EncodeEvent(value);
                if(value.Time<lastAccepted)throw new InvalidDataException("Recording event timestamps moved backwards");
                if(frame.Length>maximumBytes-queuedBytes){failure="Recording disk queue reached its byte limit; capture stopped. Recover the complete prefix from Library.";stopping=true;signal.Set();return false;}
                queue.Enqueue(new Entry{Bytes=frame,Time=value.Time});Interlocked.Add(ref queuedBytes,frame.Length);lastAccepted=value.Time;signal.Set();return true;
            }
            catch(Exception error){failure="Recording event could not be encoded: "+error.Message;stopping=true;signal.Set();return false;}
        }
    }
    public void Start(ReplayRecordingHeader header)
    {
        lock(gate){if(started||ended)throw new InvalidOperationException("Recording writer already started or stopped");started=true;}
        new Thread(()=>Run(header)){IsBackground=true,Name="ReplayMod recording writer"}.Start();
    }
    public void Complete(double stopTime)
    {
        lock(gate)
        {
            if(ended)return;duration=double.IsNaN(stopTime)||double.IsInfinity(stopTime)?0:Math.Min(ReplayRecordingHeader.MaximumDurationSeconds,Math.Max(0,stopTime));stopping=true;
            if(!started)
            {
                ended=true;while(queue.TryDequeue(out var abandoned))Interlocked.Add(ref queuedBytes,-abandoned.Bytes.Length);
                ready.TrySetCanceled();finished.TrySetResult(new ReplayWriterResult{Path=PartialPath,Failure="Recording startup stopped before the file opened"});signal.Dispose();return;
            }
            signal.Set();
        }
    }
    public async Task<bool> WaitForCompletionAsync(int milliseconds=3000)
    {return await Task.WhenAny(Completion,Task.Delay(Math.Max(0,milliseconds)))==Completion;}
    private void Run(ReplayRecordingHeader header)
    {
        long count=0;double lastTime=0;bool complete=false;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(TargetPath));
            if(publish&&File.Exists(TargetPath))throw new IOException("Recording destination already exists");
            using(var stream=open(PartialPath))using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            {
                header.Write(writer);writer.Flush();ready.TrySetResult(true);var flushClock=Stopwatch.StartNew();
                while(true)
                {
                    signal.WaitOne(100);
                    while(queue.TryDequeue(out var item))
                    {
                        try{writer.Write(item.Bytes);count++;lastTime=item.Time;}
                        finally{Interlocked.Add(ref queuedBytes,-item.Bytes.Length);}
                        if(flushClock.ElapsedMilliseconds>=1000){writer.Flush();flushClock.Restart();}
                    }
                    if(flushClock.ElapsedMilliseconds>=1000){writer.Flush();flushClock.Restart();}
                    lock(gate){if(stopping&&queue.IsEmpty)break;}
                }
                header.FinalizeHeader(writer,Math.Max(lastTime,duration),count,Failure==null);writer.Flush();
                if(stream is FileStream file)file.Flush(true);else stream.Flush();
            }
            // Verify the closed file before publishing it into the normal
            // library. On failure leave the original partial for recovery.
            if(Failure==null){if(publish){ReplayIntegrityCheck.Check(PartialPath);File.Move(PartialPath,TargetPath);}complete=true;}
        }
        catch(Exception error)
        {Interlocked.CompareExchange(ref failure,"Recording write failed: "+error.Message,null);ready.TrySetException(error);}
        finally
        {
            lock(gate){ended=true;stopping=true;while(queue.TryDequeue(out var abandoned))Interlocked.Add(ref queuedBytes,-abandoned.Bytes.Length);signal.Dispose();}
            finished.TrySetResult(new ReplayWriterResult{Path=complete?TargetPath:PartialPath,Failure=Failure,Events=count,Duration=Math.Max(lastTime,duration),Completed=complete});
        }
    }
}
