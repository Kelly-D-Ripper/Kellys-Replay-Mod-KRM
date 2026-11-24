using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NuclearOption.SceneLoading;
using ReplayMod.Core;
using ReplayMod.Events;
using ReplayMod.Events.ConcreteEvents;
using UnityEngine;

public static class LegacyDeltaInputProbe
{
    public static async UniTask Run(MapKey key,Action<string> log)
    {
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays","legacy-delta-"+Guid.NewGuid().ToString("N")+".replay");
        var def=Encyclopedia.Lookup.First(p=>p.Value is AircraftDefinition&&p.Value.unitPrefab!=null);
        using(var writer=new BinaryWriter(File.Create(file)))
        {
            writer.Write(12549);writer.Write((byte)2);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)key.Type);writer.Write(key.Path);
            writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);writer.Write(6d);writer.Write(6L);writer.Write((ushort)0);
            var spawn=new SpawnEvent{Time=0,unitId=777,ownerId=777,jsonKey=def.Key,factionName="no faction",pos=new GlobalPosition(0,3000,0),rotation=Quaternion.identity,isAircraft=true,weapons=new List<WeaponMount>()};
            writer.Write((byte)spawn.EventType);spawn.Write(writer);
            void Input(double time,byte mask,params short[] axes){writer.Write((byte)EventType.ControlInputs);writer.Write(time);writer.Write(777u);writer.Write(mask);foreach(short value in axes)writer.Write(value);}
            Input(1,63,1000,2000,3000,4000,5000,6000);Input(2,1,7000);Input(3,8,8000);Input(4,2,-2500);
            var gone=new DespawnEvent{Time=6,unitId=777};writer.Write((byte)gone.EventType);gone.Write(writer);
        }
        string hash;using(var sha=System.Security.Cryptography.SHA256.Create())using(var stream=File.OpenRead(file))hash=BitConverter.ToString(sha.ComputeHash(stream));
        await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(Path.GetFileName(file));var player=ReplayManager.i.Playback;
        if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Legacy delta fixture could not be opened");
        foreach(double time in new[]{4d,2d,3d,1d,4d,2d})
        {
            player.Pause();await player.TimelineJump(time,0,false);await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            var aircraft=player.FindActor(777) as Aircraft;var input=aircraft?.controlInputs;if(input==null)throw new Exception("Legacy delta actor missing");
            float pitch=time>=2?.7f:.1f,throttle=time>=3?.8f:.4f,roll=time>=4?-.25f:.2f;
            log("LEGACY DELTA seek="+time+" pitch="+input.pitch.ToString("F3")+" roll="+input.roll.ToString("F3")+" throttle="+input.throttle.ToString("F3"));
            if(Math.Abs(input.pitch-pitch)>.002||Math.Abs(input.roll-roll)>.002||Math.Abs(input.throttle-throttle)>.002||Math.Abs(input.yaw-.3f)>.002||Math.Abs(input.brake-.5f)>.002||Math.Abs(input.customAxis1-.6f)>.002)
                throw new Exception("Legacy delta input did not survive rewind at "+time);
        }
        using(var sha=System.Security.Cryptography.SHA256.Create())using(var stream=File.OpenRead(file))if(hash!=BitConverter.ToString(sha.ComputeHash(stream)))throw new Exception("Legacy source changed");
        log("LEGACY DELTA PASS format-2 partial masks survive repeated independent forward/backward seeks; source unchanged");
    }
}
