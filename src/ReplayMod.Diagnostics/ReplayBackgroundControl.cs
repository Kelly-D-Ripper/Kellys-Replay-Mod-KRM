using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NuclearOption.Networking;
using ReplayMod.Core;
using ReplayMod.Director;
using UnityEngine;

namespace ReplayMod.Diagnostics;

// Opt-in developer testing only. All game access stays on the main thread;
// file workers receive immutable strings and never invoke Unity APIs.
public sealed class ReplayBackgroundControl : MonoBehaviour
{
    private string directory, session;
    private float nextPoll, nextStatus;
    private bool priorBackground, ownsBackground, busy;
    private Task<ReadResult> reading;
    private Task writing;
    private long lastWriteTicks, lastLength;
    private readonly HashSet<string> seen = new HashSet<string>();
    private readonly Queue<string> recent = new Queue<string>();
    private string lastId = "", result = "Ready", pendingStatus;

    [Serializable]
    private sealed class Snapshot
    {
        public string session, utc, startupPhase, commandId, result, state, seekPhase;
        public long heartbeat;
        public bool managerReady, replay, paused, seeking, buffering, commandBusy;
        public double time, duration;
        public float speed, cameraX, cameraY, cameraZ, fov;
    }
    private sealed class ReadResult { public string Text, Error; public long Ticks, Length; }
    private void Awake()
    {
        if (ConfigManager.EnableBackgroundControl?.Value != true)
        {
            enabled = false;
            return;
        }

        priorBackground = Application.runInBackground;
        Application.runInBackground = true;
        ownsBackground = true;
        session = Guid.NewGuid().ToString("N");
        directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replays", "Diagnostics", "BackgroundControl");
        ReplayStartup.Mark("Background test control enabled");
        Publish();
    }
    private void Update()
    {
        if (directory == null) return;
        if (writing != null && writing.IsCompleted)
        {
            if (writing.IsFaulted)
            {
                Plugin.logger?.LogWarning("Background test status write failed: " + writing.Exception.GetBaseException().GetType().Name);
            }
            writing = null;
        }
        if (writing == null && pendingStatus != null)
        {
            var json = pendingStatus;
            pendingStatus = null;
            writing = Task.Run(() => WriteStatus(directory, json));
        }
        if (reading != null && reading.IsCompleted)
        {
            var read = reading.GetAwaiter().GetResult();
            reading = null;
            lastWriteTicks = read.Ticks;
            lastLength = read.Length;
            if (read.Error != null) result = read.Error;
            else if (read.Text != null) Accept(read.Text);
        }
        if (!busy && reading == null && Time.realtimeSinceStartup >= nextPoll)
        {
            nextPoll = Time.realtimeSinceStartup + .25f;
            var path = Path.Combine(directory, "command.txt");
            var ticks = lastWriteTicks;
            var length = lastLength;
            reading = Task.Run(() => ReadCommand(path, ticks, length));
        }
        if (Time.realtimeSinceStartup >= nextStatus)
        {
            nextStatus = Time.realtimeSinceStartup + .5f;
            Publish();
        }
    }
    private static ReadResult ReadCommand(string path, long priorTicks, long priorLength)
    {
        var result = new ReadResult { Ticks = priorTicks, Length = priorLength };
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return result;
            result.Ticks = info.LastWriteTimeUtc.Ticks;
            result.Length = info.Length;
            if (result.Ticks == priorTicks && result.Length == priorLength) return result;
            if (info.Length > BackgroundCommand.MaxBytes) { result.Error = "Rejected oversized command"; return result; }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bytes = new byte[BackgroundCommand.MaxBytes + 1]; int count = 0, read;
                while (count < bytes.Length && (read = stream.Read(bytes, count, bytes.Length - count)) > 0) count += read;
                if (count > BackgroundCommand.MaxBytes) { result.Error = "Rejected oversized command"; return result; }
                result.Text = new UTF8Encoding(false, true).GetString(bytes, 0, count);
            }
        }
        // nobodys going to use this, im just going to get DMs of its broken it shit fuck fuck fuck fuck
        // A concurrent write can temporarily lock the file; retry on the next poll.
        catch (IOException) { result.Ticks = priorTicks; result.Length = priorLength; }
        catch (Exception error) { result.Error = "Command read failed: " + error.GetType().Name; }
        return result;
    }
    private void Accept(string text)
    {
        BackgroundCommand command;
        try { command = BackgroundCommand.Parse(text, session); }
        catch (Exception error) { result = "Rejected: " + error.Message; return; }
        if (seen.Contains(command.Id)) return;
        seen.Add(command.Id);
        recent.Enqueue(command.Id);
        if (recent.Count > 256) seen.Remove(recent.Dequeue());
        lastId = command.Id;
        _ = Execute(command);
    }
    private async Task Execute(BackgroundCommand command)
    {
        busy = true;
        try
        {
            if (command.Action == "status") { result = "Status refreshed"; return; }
            var manager = ReplayManager.i;
            var director = ReplayDirector.i;
            if (manager == null || director == null || manager.IsTransitioning) throw new InvalidOperationException("Replay tools are not ready or are changing scenes.");
            if (command.Action == "open")
            {
                var network = NetworkManagerNuclearOption.i;
                if (manager.GetState() != ModStates.Idle || MissionManager.IsRunning || network?.Client.Active == true || network?.Server.Active == true)
                    throw new InvalidOperationException("Open is allowed only from an idle offline menu.");
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replays", command.File);
                if (!File.Exists(path)) throw new FileNotFoundException("Replay filename was not found.");
                await manager.StartPlaying(command.File);
                if (manager.GetState() != ModStates.Replay || manager.Playback == null || !manager.Playback.enabled || !string.IsNullOrEmpty(manager.Playback.PlaybackFailure))
                    throw new InvalidOperationException("Replay did not finish loading; see the game log.");
                result = "Opened replay";
                return;
            }
            var player = manager.Playback;
            if (manager.GetState() != ModStates.Replay || player == null || player.IsSeeking || director.TakeActive)
                throw new InvalidOperationException("A ready replay without an active take is required.");
            switch (command.Action)
            {
                case "pause": player.Pause(); break;
                case "play": player.Resume(); break;
                case "speed": director.SetPlaybackSpeed((float)command.Value); break;
                case "seek":
                    if (command.Value > player.replayDuration) throw new InvalidOperationException("Seek exceeds replay duration.");
                    await player.TimelineJump(command.Value, 0, false);
                    if (manager != ReplayManager.i || !ReferenceEquals(player, manager.Playback) || !player.enabled || player.IsSeeking ||
                        !string.IsNullOrEmpty(player.PlaybackFailure) || Math.Abs(player.currentVirtualTime - command.Value) > .001)
                        throw new InvalidOperationException("Seek was superseded, canceled or failed before reaching its destination.");
                    break;
            }
            result = "Completed " + command.Action;
        }
        catch (Exception error) { result = "Failed: " + error.GetType().Name + ": " + error.Message; }
        finally { busy = false; Publish(); }
    }
    private void Publish()
    {
        var manager = ReplayManager.i;
        var player = manager?.Playback;
        var camera = SceneSingleton<CameraStateManager>.i;
        var state = new Snapshot
        {
            session = session,
            utc = DateTime.UtcNow.ToString("O"),
            startupPhase = ReplayStartup.Phase,
            heartbeat = ReplayStartup.Heartbeats,
            commandId = lastId,
            result = result,
            state = manager == null ? "Starting" : manager.GetState().ToString(),
            managerReady = manager != null,
            replay = manager?.GetState() == ModStates.Replay,
            paused = player?.IsPaused ?? true,
            seeking = player?.IsSeeking ?? false,
            buffering = player?.IsBuffering ?? false,
            time = player?.currentVirtualTime ?? 0,
            duration = player?.replayDuration ?? 0,
            speed = player?.PlaybackSpeed ?? 0,
            seekPhase = player?.SeekPhase ?? "",
            commandBusy = busy
        };
        if (camera != null)
        {
            var pos = camera.transform.position;
            state.cameraX = pos.x;
            state.cameraY = pos.y;
            state.cameraZ = pos.z;
            state.fov = camera.mainCamera?.fieldOfView ?? 0;
        }
        pendingStatus = JsonUtility.ToJson(state, true);
        if (writing == null) { var json = pendingStatus; pendingStatus = null; var path = directory; writing = Task.Run(() => WriteStatus(path, json)); }
    }
    private static void WriteStatus(string directory, string json)
    {
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "status.json"), temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
    private void OnDestroy()
    {
        if (ownsBackground && Application.runInBackground) Application.runInBackground = priorBackground;
    }
}
