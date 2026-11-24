using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReplayMod.Events;

namespace ReplayMod.Core;

public sealed class ReplayRecoveryResult
{
    public string Path,Reason;
    public long EventCount,DiscardedBytes;
    public double Duration;
}
public static class ReplayRecordingRecovery
{
    private static readonly object gate=new object();
    private static bool busy;
    private static int recordings;
    private static readonly HashSet<Task> pendingWrites=new HashSet<Task>();
    private static Stopwatch shutdownClock;
    public static bool IsBusy{get{lock(gate)return busy;}}
    public static bool HasRecordingReservation{get{lock(gate)return recordings>0;}}
    public static bool TryBeginRecording(){lock(gate){if(busy||recordings>0)return false;recordings++;return true;}}
    public static void EndRecording(){lock(gate){if(recordings>0)recordings--;}}
    public static void EndRecordingWhenComplete(Task pending)
    {
        if(pending==null||pending.IsCompleted){EndRecording();return;}
        _=pending.ContinueWith(_=>EndRecording(),TaskScheduler.Default);
    }
    public static void TrackPendingWrite(Task pending)
    {
        if(pending==null||pending.IsCompleted)return;
        lock(gate)pendingWrites.Add(pending);
        _=pending.ContinueWith(done=>{lock(gate)pendingWrites.Remove(done);},TaskScheduler.Default);
    }
    // Unity does not await async quit callbacks. Only wait for background file
    // tasks, never a continuation that needs Unity's main thread. A shared
    // deadline keeps shutdown bounded even when multiple callbacks run.
    public static bool WaitForPendingWritesOnQuit(int maximumMilliseconds=10000)
    {
        Stopwatch clock;
        lock(gate)clock=shutdownClock??(shutdownClock=Stopwatch.StartNew());
        while(true)
        {
            Task pending;
            lock(gate)pending=pendingWrites.FirstOrDefault(task=>!task.IsCompleted);
            if(pending==null)return true;
            int remaining=(int)Math.Max(0,(long)Math.Max(0,maximumMilliseconds)-clock.ElapsedMilliseconds);
            if(remaining==0)return false;
            try { if(!pending.Wait(remaining))return false; }
            catch(AggregateException) { /* The writer retains its partial and reports failure. */ }
        }
    }
    public static string[] FindCandidates(string replayDirectory)
    {
        if(!Directory.Exists(replayDirectory))return Array.Empty<string>();
        return Directory.EnumerateFiles(replayDirectory,"*.replay.partial",SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(256).ToArray();
    }
    public static Task<ReplayRecoveryResult> RecoverAsync(string sourcePath,CancellationToken token=default)
    {
        lock(gate)
        {
            if(busy)throw new InvalidOperationException("A recording is already being recovered");
            if(recordings>0)throw new InvalidOperationException("Stop recording and wait for its file save before recovering a file");
            busy=true;
        }
        return RunRecoveryAsync(sourcePath,token);
    }
    private static async Task<ReplayRecoveryResult> RunRecoveryAsync(string sourcePath,CancellationToken token)
    {try{return await Task.Run(()=>Recover(sourcePath,token));}finally{lock(gate)busy=false;}}
    private static ReplayRecoveryResult Recover(string sourcePath,CancellationToken token)
    {
        string source=System.IO.Path.GetFullPath(sourcePath);
        if(!source.EndsWith(".replay.partial",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose an interrupted .replay.partial recording");
        string output=source.Substring(0,source.Length-".replay.partial".Length)+"-recovered-"+Guid.NewGuid().ToString("N").Substring(0,8)+".replay";
        string temporary=output+".partial";bool created=false;
        try
        {
            // Exclusive read both prevents recovering a live writer and keeps
            // the original stable for the full copy. Never truncate/rename it.
            using(var file=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None,65536))
            using(var reader=new BinaryReader(file))
            {
                var header=ReplayRecordingHeader.ReadForRecovery(reader);
                if(header.Format<6)throw new InvalidDataException("Only checksummed format-6 or newer interrupted recordings support safe recovery");
                bool committed=header.Complete,invalid=false;long expectedCount=header.EventCount;
                long count=0,end=header.DataOffset,closedCount=0,closedSourceEnd=end,closedCopyEnd=0;double previous=-1,closedTime=0;string reason=header.DamagedCompletionMarker?"Damaged completion marker; validated event frames recovered":"All complete event frames recovered";
                using(var copy=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536))
                using(var writer=new BinaryWriter(copy))
                {
                    created=true;header.Duration=0;header.EventCount=0;header.Write(writer);closedCopyEnd=copy.Position;
                    while(file.Position<file.Length)
                    {
                        token.ThrowIfCancellationRequested();long start=file.Position;byte[] body;
                        try
                        {
                            body=ReplayRecordTransport.ReadBody(reader);double time=ReplayRecordTransport.ValidateEnvelope(body);
                            if(time<previous)throw new InvalidDataException("Event timestamps moved backwards");
                            // A following valid envelope closes the preceding
                            // same-timestamp group, even if its own semantic
                            // payload is incomplete and cannot be recovered.
                            if(count>0&&time>previous){closedCount=count;closedTime=previous;closedSourceEnd=end;closedCopyEnd=copy.Position;}
                            var decoded=ReplayRecordTransport.DecodeBody(body,header.Format);
                            try{if(decoded.Time!=time)throw new InvalidDataException("Event timestamp does not match its frame");}
                            finally{ReplayEventFactory.Return(decoded);}
                            previous=time;
                        }
                        catch(Exception error) when(!(error is OperationCanceledException)&&!(error is OutOfMemoryException)&&!(error is StackOverflowException))
                        {invalid=true;reason="Stopped at byte "+start+": "+error.Message;break;}
                        writer.Write(ReplayRecordTransport.Frame(body));count++;end=file.Position;
                    }
                    if(!committed||invalid||count!=expectedCount)
                    {
                        count=closedCount;previous=closedTime;end=closedSourceEnd;copy.SetLength(closedCopyEnd);copy.Position=closedCopyEnd;
                        reason+=". The final unconfirmed timestamp group was omitted.";
                    }
                    if(count==0)throw new InvalidDataException("This file has no complete closed timestamp groups to recover. Its original is unchanged.");
                    token.ThrowIfCancellationRequested();header.FinalizeHeader(writer,Math.Max(0,previous),count);writer.Flush();copy.Flush(true);
                }
                // Re-read the closed copy before presenting it as a playable
                // file. The interrupted source remains untouched if this fails.
                token.ThrowIfCancellationRequested();ReplayIntegrityCheck.Check(temporary);
                token.ThrowIfCancellationRequested();File.Move(temporary,output);created=false;
                return new ReplayRecoveryResult{Path=output,EventCount=count,Duration=Math.Max(0,previous),DiscardedBytes=file.Length-end,Reason=reason};
            }
        }
        finally{if(created){try{File.Delete(temporary);}catch{}}}
    }
}
