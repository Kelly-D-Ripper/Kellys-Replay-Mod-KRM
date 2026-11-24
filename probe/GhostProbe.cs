using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Events.ConcreteEvents;
using ReplayMod.Misc;
using NuclearOption.SceneLoading;
using UnityEngine;

[BepInPlugin("local.replaymod.ghost.probe","Ghost flight probe","1.0")]
[BepInDependency("com.chiester3105.replayMod")]
public sealed class GhostProbe : BaseUnityPlugin
{
    void Awake(){Application.runInBackground=true;if(Environment.GetEnvironmentVariable("REPLAY_ENGINE_AUDIO_TAP")=="1")AudioListener.volume=0;new Harmony("local.ghost.probe").PatchAll(typeof(GhostProbe).Assembly);}
}
[HarmonyPatch(typeof(ReplayManager),"Start")]
public static class GhostProbeAttach
{ [HarmonyPostfix] public static void Postfix(ReplayManager __instance){__instance.gameObject.AddComponent<GhostProbeRunner>();} }
public sealed class GhostProbeRunner:MonoBehaviour
{
    string output;
    delegate bool EnumProc(IntPtr h,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder name,int max);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr insert,int x,int y,int w,int hgt,uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int how);
    void Awake(){output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ghost-probe");Directory.CreateDirectory(output);}
    void Log(string s){Debug.Log("GHOST_PROBE "+s);File.AppendAllText(Path.Combine(output,"result.txt"),DateTime.UtcNow.ToString("O")+" "+s+"\n");}
    async UniTask Until(Func<bool> ready,float timeout,string reason){float end=Time.realtimeSinceStartup+timeout;while(!ready()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(reason);await UniTask.NextFrame();}}
    async UniTask Wait(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)await UniTask.NextFrame();}
    async void Start()
    {
        ReplayDeadLatchProbe.StartPump();
        try
        {
            await Until(()=>ReplayManager.i!=null&&GameManager.gameState==GameState.Menu,120,"main menu");
            IntPtr window=IntPtr.Zero;uint own=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows((h,d)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(pid==own){var name=new StringBuilder(256);GetClassName(h,name,256);if(name.ToString().Contains("UnityWndClass")){window=h;return false;}}return true;},IntPtr.Zero);
            if(window==IntPtr.Zero||!SetWindowPos(window,IntPtr.Zero,-16000,-16000,1280,720,0x0014))throw new Exception("cannot move test window offscreen");
            ShowWindow(window,4);
            ReleaseChecks.Menu(Log);
            if(Environment.GetEnvironmentVariable("REPLAY_DOORMAN_METADATA")=="1")
            {DoormanMetadataProbe.Run(Log);Log("COMPLETE DOORMAN metadata test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_CLEAN_FRAME")=="1")await CleanUiFrameProbe.MainMenu(Log);
            if(Environment.GetEnvironmentVariable("REPLAY_CLEAN_KEY")=="1")await CleanKeyProbe.Menu(Log);
            await ReplayManager.i.StartPlaying("test.replay");
            await Until(()=>ReplayRuntime.IsPlayback&&ReplayManager.i.Playback!=null&&!ReplayManager.i.Playback.IsSeeking,120,"initial replay");
            var player=ReplayManager.i.Playback; player.Pause();
            if(Environment.GetEnvironmentVariable("REPLAY_INDEX_STRESS")=="1")
            {await IndexStressProbe.Run(Log);Log("COMPLETE replay index and reader stress");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_UI_INPUT")=="1")
            {await UiInputProbe.Run(Log);Log("COMPLETE studio keyboard input test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_MIXED_WEAPONS")=="1")
            {
                var weaponKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await MissileWeaponProbe.Run(weaponKey,Log);Log("COMPLETE mixed weapon presentation test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_MISSILE_LAUNCH")=="1")
            {
                var launchKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await MissileLaunchProbe.Run(launchKey,Log);Log("COMPLETE missile launch presentation test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_LEGACY_DELTA")=="1")
            {
                var deltaKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await LegacyDeltaInputProbe.Run(deltaKey,Log);Log("COMPLETE legacy delta-input test");return;
            }
            if(LoopbackHostProbe.Enabled)
            {
                var loopbackKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await LoopbackHostProbe.Run(loopbackKey,Log);Log("COMPLETE loopback host feasibility");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_V15_BACKWARD")=="1")
            {
                await BackwardReplayProbe.Run(Environment.GetEnvironmentVariable("REPLAY_PROVIDED_FILE"),Log);
                Environment.SetEnvironmentVariable("REPLAY_EXPECT_AIRCRAFT","28");Environment.SetEnvironmentVariable("REPLAY_LONG_MIN_SECONDS","590");
                await LongRecordingProbe.Verify("Terrain1_2026-11-17_05-42-59_b0ba1138.replay",Log);
                Log("COMPLETE v15 provided and ten-minute replay tests");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_INVALID_POSE")=="1")
            {
                var invalidKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await InvalidPoseCaptureProbe.Run(invalidKey,Log);Log("COMPLETE invalid pose capture test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_PROJECT_PERSISTENCE")=="1")
            {
                var projectKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                var aircraft=Encyclopedia.Lookup.First(p=>p.Value is AircraftDefinition&&p.Value.unitPrefab!=null);
                string first=WriteFixture(projectKey,aircraft.Key);await Wait(.05f);string second=WriteFixture(projectKey,aircraft.Key);
                await PathProjectsProbe.Run(first,second,Log);Log("COMPLETE per-replay camera projects test");
                if(Environment.GetEnvironmentVariable("REPLAY_PROJECT_AND_ACCEPTANCE")=="1")
                {
                    await CargoLiveProbe.Run(projectKey,Log);await EditorLiveProbe.Run(projectKey,Log);await ShipLiveProbe.Run(projectKey,Log);
                    Log("COMPLETE native cargo editor and Argus checks");
                    if(Environment.GetEnvironmentVariable("REPLAY_ACCEPTANCE_LONG")=="1")
                    {await LongRecordingProbe.Run(projectKey,Log);Log("COMPLETE native acceptance and sustained recording");}
                }
                return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_MAP_DIAGNOSTIC")=="1")
            {
                var mapKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                var aircraft=Encyclopedia.Lookup.First(p=>p.Value is AircraftDefinition&&p.Value.unitPrefab!=null&&p.Value.mapIconSize>0);
                var missile=Encyclopedia.Lookup.First(p=>p.Key=="AAM4"&&p.Value is MissileDefinition&&p.Value.unitPrefab!=null&&p.Value.unitPrefab.GetComponent<Missile>()!=null&&p.Value.mapIconSize>0);
                string mapFixture=WriteMapFixture(mapKey,aircraft.Key,missile.Key);
                await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(mapFixture);
                await Until(()=>ReplayRuntime.IsPlayback&&ReplayManager.i.Playback!=null&&!ReplayManager.i.Playback.IsSeeking,120,"map fixture");
                player=ReplayManager.i.Playback;player.Pause();
                var map=SceneSingleton<DynamicMap>.i;if(map==null)throw new Exception("Native replay map unavailable");
                map.gameObject.SetActive(true);await Wait(1);
                Log("MAP FIXTURE aircraft="+aircraft.Key+" missile="+missile.Key+" active="+map.gameObject.activeInHierarchy+" hud="+(SceneSingleton<CombatHUD>.i!=null)+" options="+(SceneSingleton<MapOptions>.i!=null)+" selector="+(SceneSingleton<TargetListSelector>.i!=null));
                foreach(double time in new[]{.5,2.0,4.0,6.0,2.0})
                {
                    await player.TimelineJump(time,0,false);
                    var currentMap=SceneSingleton<DynamicMap>.i;if(currentMap==null)throw new Exception("Native map missing after seek");currentMap.gameObject.SetActive(true);await Wait(1f);
                    var unit=player.FindActor(778);UnitMapIcon icon=null;bool exists=unit!=null&&currentMap.TryGetIcon(unit,out icon)&&icon!=null;
                    Log("MAP FIXTURE same-map="+ReferenceEquals(map,currentMap)+" active="+currentMap.gameObject.activeInHierarchy+" disabled="+(unit!=null&&unit.disabled)+" size="+(unit==null?0:unit.definition.mapIconSize));map=currentMap;
                    Log("MAP FIXTURE time="+time+" missile="+(unit!=null)+" icon="+exists+" live-tracking="+(icon!=null&&icon.trackingInfo!=null));
                    if((time>=1&&time<5)!=(unit!=null))throw new Exception("Missile lifecycle incorrect at "+time);
                    if(unit!=null&&!exists)throw new Exception("Missile map icon absent at "+time);
                    if(icon!=null&&icon.trackingInfo!=null)throw new Exception("Missile icon using stale live tracking at "+time);
                }
                Log("MAP FIXTURE PASS native missile icon appeared and was restored on rewind while paused");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_V9_BATCH")=="1")
            {
                var batchKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                Environment.SetEnvironmentVariable("REPLAY_EARLY_GEAR","1");Environment.SetEnvironmentVariable("REPLAY_FALLBACK_AIRCRAFT","1");
                await RapidAircraftProbe.Run(batchKey,Log);
                Environment.SetEnvironmentVariable("REPLAY_EARLY_GEAR",null);Environment.SetEnvironmentVariable("REPLAY_FALLBACK_AIRCRAFT",null);
                Environment.SetEnvironmentVariable("REPLAY_EARLY_OVERFLOW","1");await RapidAircraftProbe.Run(batchKey,Log);Environment.SetEnvironmentVariable("REPLAY_EARLY_OVERFLOW",null);
                Environment.SetEnvironmentVariable("REPLAY_EARLY_GUN","1");await RapidAircraftProbe.Run(batchKey,Log);Environment.SetEnvironmentVariable("REPLAY_EARLY_GUN",null);
                await EarlyFlareProbe.Run(batchKey,Log);await RegistrationReuseProbe.Run(batchKey,Log);await EarlyInterleaveProbe.Run(batchKey,Log);
                await BrawlerLiveEngineProbe.Run(batchKey,Log);await CargoLiveProbe.Run(batchKey,Log);await EditorLiveProbe.Run(batchKey,Log);await ShipLiveProbe.Run(batchKey,Log);
                Log("COMPLETE v9 native recording batch");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_V10_EXTRAS")=="1")
            {
                var extraKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                Environment.SetEnvironmentVariable("REPLAY_FLARE_SAMPLE","1");await EarlyFlareProbe.Run(extraKey,Log);Environment.SetEnvironmentVariable("REPLAY_FLARE_SAMPLE",null);
                Environment.SetEnvironmentVariable("REPLAY_FLARE_OVERFLOW","1");await EarlyFlareProbe.Run(extraKey,Log);Environment.SetEnvironmentVariable("REPLAY_FLARE_OVERFLOW",null);
                Environment.SetEnvironmentVariable("REPLAY_FLARE_REUSE","1");await EarlyFlareProbe.Run(extraKey,Log);Environment.SetEnvironmentVariable("REPLAY_FLARE_REUSE",null);
                Log("COMPLETE v10 flare edge cases");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_SHIP_INVENTORY")=="1")
            {
                var shipController=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                ShipInventoryProbe.Run(shipController,Log);Log("COMPLETE ship gun inventory");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_ORDER")=="1")
            {await FoliageOrderProbe.Run(Log);Log("COMPLETE foliage culling order test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_FOLIAGE_VIDEO")=="1")
            {await FoliageVideoProbe.Run(Log);Log("COMPLETE foliage video test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_CLEAN_PERF")=="1"||Environment.GetEnvironmentVariable("REPLAY_CLEAN_PERF_LIVE")=="1")
            {
                var perfKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await CleanPerfProbe.Run(perfKey,Environment.GetEnvironmentVariable("REPLAY_CLEAN_PERF_LIVE")=="1",Log);Log("COMPLETE hide UI frame-time comparison");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_CONTINUATION_ACCEPTANCE")=="1")
            {
                var acceptanceKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                if(ProductionContinuationProbe.Enabled)ProductionContinuationProbe.CheckOneShot(Log);
                Environment.SetEnvironmentVariable("REPLAY_CARGO_KEY","MunitionsSmallPallet2x4");Environment.SetEnvironmentVariable("REPLAY_CARGO_ALL","1");
                await CargoLiveProbe.Run(acceptanceKey,Log);
                Environment.SetEnvironmentVariable("REPLAY_CARGO_KEY","6x6_1_APCx1");Environment.SetEnvironmentVariable("REPLAY_CARGO_ALL",null);
                await CargoLiveProbe.Run(acceptanceKey,Log);await EjectionLiveProbe.Run(acceptanceKey,Log);
                Log("COMPLETE continuation cargo and ejection acceptance");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_EJECTION_LIVE")=="1")
            {
                var ejectionKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await EjectionLiveProbe.Run(ejectionKey,Log);Log("COMPLETE native ejection test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_CLEAN_KEY")=="1")
            {await CleanKeyProbe.Replay(Log);Log("COMPLETE simulated clean shortcut test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_CLEAN_FRAME")=="1")
            {
                var cleanKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await CleanUiFrameProbe.Run(cleanKey,Log);Log("COMPLETE clean UI frame test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_MOVING_NAVAL")=="1")
            {
                var navalKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await MovingNavalProbe.Run(navalKey,"Frigate1","20mm",Log);
                await MovingNavalProbe.Run(navalKey,"Frigate1","127mm",Log);
                Log("COMPLETE moving naval gun test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_ROLLING_TO_FULL_UI")=="1")
            {
                var rollingFullKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await RollingFullUiProbe.Run(rollingFullKey,Log);Log("COMPLETE rolling to full UI test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_STALE_RESOURCE")=="1")
            {
                var rollingKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await RollingPendingProbe.Run(rollingKey,Log);
                if(Environment.GetEnvironmentVariable("REPLAY_STALE_EXPECT_FIXED")=="1")await MissingResourceProbe.Run(rollingKey,Log);
                if(Environment.GetEnvironmentVariable("REPLAY_STALE_BACKWARD")=="1")
                {
                    await BackwardReplayProbe.Run(Environment.GetEnvironmentVariable("REPLAY_PROVIDED_FILE"),Log);
                    Environment.SetEnvironmentVariable("REPLAY_EXPECT_AIRCRAFT","28");Environment.SetEnvironmentVariable("REPLAY_LONG_MIN_SECONDS","590");
                    await LongRecordingProbe.Verify("Terrain1_2026-11-17_05-42-59_b0ba1138.replay",Log);
                }
                Log("COMPLETE stale rolling resource test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_FAST_V16")=="1")
            {
                var fastKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await FastPlaybackProbe.Run(fastKey,Log);Log("COMPLETE dense fast playback test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_CARGO_V16_BATCH")=="1")
            {
                var cargoKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                Environment.SetEnvironmentVariable("REPLAY_CARGO_KEY","MunitionsSmallPallet2x4");Environment.SetEnvironmentVariable("REPLAY_CARGO_ALL","1");
                await CargoLiveProbe.Run(cargoKey,Log);
                Environment.SetEnvironmentVariable("REPLAY_CARGO_ALL",null);Environment.SetEnvironmentVariable("REPLAY_CARGO_DESTROY_CHUTE","1");Environment.SetEnvironmentVariable("REPLAY_CARGO_KEY","6x6_1_APCx1");
                await CargoLiveProbe.Run(cargoKey,Log);
                Environment.SetEnvironmentVariable("REPLAY_CARGO_DESTROY_CHUTE",null);Environment.SetEnvironmentVariable("REPLAY_CARGO_KEY",null);
                Log("COMPLETE v16 cargo multi-release and parachute removal tests");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_HANGAR_LIVE")=="1")
            {
                var hangarKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await HangarLiveProbe.Run(hangarKey,Log);Log("COMPLETE native hangar motion test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_WORLD_LIVE")=="1")
            {
                var worldKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await WorldLiveProbe.Run(worldKey,Log);Log("COMPLETE recorded environment test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_FUEL_FIRE_LIVE")=="1")
            {
                var fireKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await FuelFireLiveProbe.Run(fireKey,Log);Log("COMPLETE live fuel fire test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_FLARE_LIVE")=="1")
            {
                var flareKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await FlareLiveProbe.Run(flareKey,Log);Log("COMPLETE live flare visual test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_TWO_HOUR")=="1")
            {await LongTimelineProbe.Run(Environment.GetEnvironmentVariable("REPLAY_TWO_HOUR_FILE"),Log);Log("COMPLETE synthetic two-hour Unity playback test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_V16_VARIETY")=="1")
            {
                var varietyKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                foreach(var test in new[]{("AssaultCarrier1","76mm"),("Corvette1","57mm"),("Destroyer1","155mm"),("Frigate1","20mm")})
                {
                    Environment.SetEnvironmentVariable("REPLAY_NAVAL_SHIP",test.Item1);Environment.SetEnvironmentVariable("REPLAY_NAVAL_GUN",test.Item2);
                    await ShipLiveProbe.Run(varietyKey,Log);
                }
                Environment.SetEnvironmentVariable("REPLAY_NAVAL_SHIP",null);Environment.SetEnvironmentVariable("REPLAY_NAVAL_GUN",null);
                await NeutralEditorProbe.Run(varietyKey,Log);
                Log("COMPLETE v16 naval and neutral scenarios");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_NAVAL_CASES")=="1")
            {
                var navalKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                foreach(var test in new[]{("AssaultCarrier1","76mm"),("Corvette1","57mm"),("Destroyer1","155mm"),("Frigate1","20mm")})
                {
                    Environment.SetEnvironmentVariable("REPLAY_NAVAL_SHIP",test.Item1);Environment.SetEnvironmentVariable("REPLAY_NAVAL_GUN",test.Item2);
                    await ShipLiveProbe.Run(navalKey,Log);
                }
                Log("COMPLETE native naval gun cases");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_BURST_BATCH")=="1")
            {
                var burstKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                Environment.SetEnvironmentVariable("REPLAY_BURST_BRAWLER","1");Environment.SetEnvironmentVariable("REPLAY_BURST_SPEED","0.1");await GunBurstLiveProbe.Run(burstKey,Log);
                Environment.SetEnvironmentVariable("REPLAY_BURST_BRAWLER",null);Environment.SetEnvironmentVariable("REPLAY_BURST_SPEED","1");await GunBurstLiveProbe.Run(burstKey,Log);
                Environment.SetEnvironmentVariable("REPLAY_BURST_SPEED",null);Log("COMPLETE gun burst batch");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_GUN_BURST")=="1")
            {
                var burstKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await GunBurstLiveProbe.Run(burstKey,Log);Log("COMPLETE muted gun burst test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_ENGINE_INVENTORY")=="1")
            {
                var engineController=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                EngineInventoryProbe.Run(engineController,Log);Log("COMPLETE engine inventory");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_AIRCRAFT_AUDIO_MATRIX")=="1")
            {
                var engineKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await AircraftAudioMatrixProbe.Run(engineKey,Log);Log("COMPLETE aircraft audio matrix");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_JET_CORE_LIVE")=="1")
            {
                var engineKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await JetCoreLiveProbe.Run(engineKey,Log);Log("COMPLETE native jet core test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_ROTOR_LIVE")=="1")
            {
                var rotorKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await RotorLiveProbe.Run(rotorKey,Log);Log("COMPLETE native rotor test");return;
            }
            if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPLAY_ROTOR_LEGACY_FILE")))
            {await RotorLiveProbe.RunLegacy(Environment.GetEnvironmentVariable("REPLAY_ROTOR_LEGACY_FILE"),Log);Log("COMPLETE legacy rotor seek test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_BRAWLER_ENGINE")=="1")
            {
                var engineKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await BrawlerEngineProbe.Run(engineKey,Log);Log("COMPLETE muted Brawler engine test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_BRAWLER_LIVE")=="1")
            {
                var liveEngineKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await BrawlerLiveEngineProbe.Run(liveEngineKey,Log);Log("COMPLETE recorded Brawler engine test");return;
            }
            if((Environment.GetEnvironmentVariable("REPLAY_RAPID_AIRCRAFT")=="1"||Environment.GetEnvironmentVariable("REPLAY_FALLBACK_AIRCRAFT")=="1"||Environment.GetEnvironmentVariable("REPLAY_EARLY_GEAR")=="1"||Environment.GetEnvironmentVariable("REPLAY_EARLY_OVERFLOW")=="1"||Environment.GetEnvironmentVariable("REPLAY_EARLY_GUN")=="1"))
            {
                var rapidKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await RapidAircraftProbe.Run(rapidKey,Log);Log("COMPLETE rapid aircraft registration test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_PENDING_REUSE")=="1")
            {
                var reuseKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await RegistrationReuseProbe.Run(reuseKey,Log);Log("COMPLETE pending registration ID reuse test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_EARLY_INTERLEAVE")=="1")
            {
                var interleaveKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await EarlyInterleaveProbe.Run(interleaveKey,Log);Log("COMPLETE pending event interleave test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_EARLY_FLARE")=="1"||Environment.GetEnvironmentVariable("REPLAY_EARLY_CHAFF")=="1"||Environment.GetEnvironmentVariable("REPLAY_FLARE_OVERFLOW")=="1"||Environment.GetEnvironmentVariable("REPLAY_FLARE_REUSE")=="1"||Environment.GetEnvironmentVariable("REPLAY_FLARE_SAMPLE")=="1")
            {
                var flareKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await EarlyFlareProbe.Run(flareKey,Log);Log("COMPLETE pending flare test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_ROLLING_PENDING")=="1"||Environment.GetEnvironmentVariable("REPLAY_ROLLING_DELAYED")=="1"||Environment.GetEnvironmentVariable("REPLAY_ROLLING_FALLBACK")=="1"||Environment.GetEnvironmentVariable("REPLAY_ROLLING_ENDS")=="1"||Environment.GetEnvironmentVariable("REPLAY_ROLLING_WEAPONS")=="1"||Environment.GetEnvironmentVariable("REPLAY_ROLLING_PAUSED")=="1"||Environment.GetEnvironmentVariable("REPLAY_ROLLING_INVALID")=="1")
            {
                var rollingKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await RollingPendingProbe.Run(rollingKey,Log);Log("COMPLETE rolling pending aircraft test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_ROLLQUIT_RECORD")=="1")
            {
                var rollingQuitKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await RollingQuitProbe.Record(rollingQuitKey,Log);return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_ROLLQUIT_VERIFY")=="1")
            {await RollingQuitProbe.Verify(Log);Log("COMPLETE rolling quit verification");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_ROLLING_BATCH")=="1")
            {
                var rollingBatchKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                foreach(var setting in new[]{"REPLAY_ROLLING_PENDING","REPLAY_ROLLING_DELAYED","REPLAY_ROLLING_FALLBACK","REPLAY_ROLLING_PAUSED","REPLAY_ROLLING_WEAPONS","REPLAY_ROLLING_ENDS","REPLAY_ROLLING_INVALID"})
                {Environment.SetEnvironmentVariable(setting,"1");await RollingPendingProbe.Run(rollingBatchKey,Log);Environment.SetEnvironmentVariable(setting,null);}
                Log("COMPLETE rolling recording batch");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_EDITOR_CASES")=="1")
            {
                var editorCasesKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await NeutralEditorProbe.Run(editorCasesKey,Log);await NukeEditorProbe.Run(editorCasesKey,Log);
                Log("COMPLETE saved neutral and nuclear mission cases");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_NEUTRAL_EDITOR")=="1")
            {
                var neutralKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await NeutralEditorProbe.Run(neutralKey,Log);Log("COMPLETE saved neutral mission test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_NUKE_INVENTORY")=="1")
            {
                foreach(var d in Encyclopedia.Lookup.Values.OfType<MissileDefinition>().Where(d=>d.unitPrefab!=null).Select(d=>new{definition=d,missile=d.unitPrefab.GetComponent<Missile>()}).Where(x=>x.missile!=null).OrderByDescending(x=>x.missile.blastYield).Take(25))
                    Log("NUKE INVENTORY key="+d.definition.jsonKey+" name="+d.definition.unitName+" blastYield="+d.missile.blastYield+" effect="+(d.missile.warhead?.airEffect!=null?d.missile.warhead.airEffect.name:"none"));
                Log("COMPLETE nuclear missile inventory");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_NUKE_EDITOR")=="1")
            {
                var nukeKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await NukeEditorProbe.Run(nukeKey,Log);Log("COMPLETE saved nuclear mission test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_BUILDING_LIVE")=="1"||Environment.GetEnvironmentVariable("REPLAY_MAP_BUILDING_LIVE")=="1")
            {
                var buildingKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await BuildingLiveProbe.Run(buildingKey,Log);Log("COMPLETE native building collapse test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_SCENE_CONTROL")=="1")
            {
                var controlKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await SceneReloadControlProbe.Run(controlKey,Log);Log("COMPLETE native scene reload control");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_SEEK_MEMORY")=="1")
            {
                string memoryFile=Environment.GetEnvironmentVariable("REPLAY_MEMORY_FILE");
                if(!string.IsNullOrEmpty(memoryFile))
                {
                    if(memoryFile!=System.IO.Path.GetFileName(memoryFile))throw new Exception("Expected isolated replay filename");
                    await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(memoryFile);
                    player=ReplayManager.i.Playback;if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Memory replay did not open");
                    player.Pause();
                }
                ReleaseChecks.CheckJoinWait(false);await SeekMemoryProbe.Run(player,Log);Log("COMPLETE seek memory diagnostic");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_LONG_RECORD")=="1")
            {
                var longKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await LongRecordingProbe.Run(longKey,Log);Log("COMPLETE sustained recording test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_REGRESSION_BATCH")!="1"&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REPLAY_LONG_FILE")))
            {
                await LongRecordingProbe.Verify(Environment.GetEnvironmentVariable("REPLAY_LONG_FILE"),Log);Log("COMPLETE sustained replay verification");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_MISSING_RESOURCE")=="1")
            {
                var missingKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await MissingResourceProbe.Run(missingKey,Log);Log("COMPLETE missing resource checks");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_LIVE_PREFLIGHT")=="1")
            {
                var preflightKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await LivePreflightProbe.Run(preflightKey,Log);Log("COMPLETE live replay preflight checks");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_LIVE_PREFLIGHT_AND_SUITE")=="1")
            {
                var preflightKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await LivePreflightProbe.Run(preflightKey,Log);
                await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying("test.replay");
                player=ReplayManager.i.Playback;player.Pause();
            }
            if(Environment.GetEnvironmentVariable("REPLAY_REGRESSION_BATCH")=="1")
            {
                var batchKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await MissingResourceProbe.Run(batchKey,Log);
                await CargoLiveProbe.Run(batchKey,Log);
                await LongRecordingProbe.Verify(Environment.GetEnvironmentVariable("REPLAY_LONG_FILE"),Log);
                Log("COMPLETE targeted regression batch");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_SEEK_STRESS")=="1")
            {
                string stressFile=Environment.GetEnvironmentVariable("REPLAY_SEEK_STRESS_FILE");
                if(!string.IsNullOrEmpty(stressFile))
                {
                    if(stressFile!=System.IO.Path.GetFileName(stressFile))throw new Exception("Expected isolated replay filename");
                    await ReplayManager.i.SwitchState(ModStates.Idle);await ReplayManager.i.StartPlaying(stressFile);
                    player=ReplayManager.i.Playback;if(player==null||!ReplayRuntime.IsPlayback)throw new Exception("Stress replay did not open");
                    player.Pause();
                }
                int rounds=int.TryParse(Environment.GetEnvironmentVariable("REPLAY_SEEK_STRESS_ROUNDS"),out var n)?Math.Max(1,Math.Min(3,n)):1;
                for(int round=1;round<=rounds;round++){Log("STRESS ROUND "+round+"/"+rounds);await SeekStressProbe.Run(player,Log);}
                if(Environment.GetEnvironmentVariable("REPLAY_SEEK_RESUME_CHECK")=="1")
                {
                    var errors=new List<string>();Application.LogCallback callback=(condition,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(condition);};
                    Application.logMessageReceived+=callback;
                    try
                    {
                        double before=player.currentVirtualTime;ReplayMod.Director.ReplayDirector.i.SetPlaybackSpeed(1);player.Resume();await Wait(10);player.Pause();
                        if(!ReplayRuntime.IsPlayback||player.currentVirtualTime-before<5||!string.IsNullOrEmpty(player.PlaybackFailure)||errors.Count>0)
                            throw new Exception("Playback after seek stress failed; elapsed="+(player.currentVirtualTime-before)+" errors="+string.Join(" | ",errors.Take(4)));
                        Log("STRESS RESUME PASS replay advanced "+(player.currentVirtualTime-before).ToString("F1")+" seconds with no logged errors");
                    }
                    finally{Application.logMessageReceived-=callback;}
                }
                Log("COMPLETE seek stress test");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_VISUAL")=="1")
            {
                await VisualProbe.Run(Log);Log("COMPLETE cargo visual capture");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_HANGAR_AUDIT")=="1")
            {
                await Wait(3);HangarProbe.Run(player,Log);
                await player.TimelineJump(30,0,false);await Wait(3);HangarProbe.Run(player,Log);
                await player.TimelineJump(0,0,false);await Wait(3);HangarProbe.Run(player,Log);
                Log("COMPLETE hangar identity audit and repeated seeks");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_TORN_MARKER")=="1")
            {await CrashRecoveryProbe.RecoverTornMarker(Log);Log("COMPLETE torn marker recovery test");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_TORN_HEADER")=="1")
            {
                var recoveryKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await CrashRecoveryProbe.RecoverTornHeader(Log);Log("COMPLETE torn header recovery test");
                if(Environment.GetEnvironmentVariable("REPLAY_TORN_AND_LONG")=="1")
                {await LongRecordingProbe.Run(recoveryKey,Log);Log("COMPLETE v14 sustained recording test");}
                return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_CRASH_RECORD")=="1")
            {
                var crashKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await CrashRecoveryProbe.Record(crashKey,Log);return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_QUIT_RECORD")=="1")
            {
                var quitKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await QuitRecordingProbe.Record(quitKey,Log);return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_QUIT_VERIFY")=="1")
            {
                await QuitRecordingProbe.Verify(Log);Log("COMPLETE graceful quit verification");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_QUIT_VERIFY_AND_SUITE")=="1")
            {
                await QuitRecordingProbe.Verify(Log);await ReplayManager.i.SwitchState(ModStates.Idle);
                await ReplayManager.i.StartPlaying("test.replay");
                player=ReplayManager.i.Playback;player.Pause();
            }
            if(Environment.GetEnvironmentVariable("REPLAY_CORRUPT_FRAME")=="1")
            {await CrashRecoveryProbe.RecoverCorruptFrame(Log);Log("COMPLETE corrupted frame recovery");return;}
            if(Environment.GetEnvironmentVariable("REPLAY_CRASH_RECOVER")=="1")
            {
                await CrashRecoveryProbe.Recover(Log);Log("COMPLETE interrupted recording recovery");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_ACCEPTANCE")=="1")
            {
                var acceptanceKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await CargoLiveProbe.Run(acceptanceKey,Log);
                await EditorLiveProbe.Run(acceptanceKey,Log);
                await ShipLiveProbe.Run(acceptanceKey,Log);
                Log("COMPLETE combined native acceptance checks");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_SHIP_LIVE")=="1")
            {
                var shipKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await ShipLiveProbe.Run(shipKey,Log);Log("COMPLETE live ship recording");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_EDITOR_LIVE")=="1")
            {
                var editorKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await EditorLiveProbe.Run(editorKey,Log);Log("COMPLETE live editor recording");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_CARGO_LIVE")=="1")
            {
                var liveKey=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await CargoLiveProbe.Run(liveKey,Log);Log("COMPLETE live cargo recording");return;
            }
            if(Environment.GetEnvironmentVariable("REPLAY_CARGO_ONLY")=="1")
            {
                var cargoController=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
                await CargoProbe.Run(cargoController,Log);Log("COMPLETE cargo reconnaissance");return;
            }
            await ReleaseChecks.Playback(player,Log);
            var key=(MapKey)typeof(Player).GetField("_key",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
            var def=Encyclopedia.Lookup.First(p=>p.Value is AircraftDefinition&&p.Value.unitPrefab!=null);
            Log("fixture aircraft="+def.Key+" map="+key.Path);
            string fixture=WriteFixture(key,def.Key);
            await ReplayManager.i.SwitchState(ModStates.Idle);
            await ReplayManager.i.StartPlaying(fixture);
            await Until(()=>ReplayRuntime.IsPlayback&&ReplayManager.i.Playback!=null&&!ReplayManager.i.Playback.IsSeeking,120,"fixture replay");
            player=ReplayManager.i.Playback;player.Pause();await player.TimelineJump(3,0,false);
            if(!(player.FindActor(777) is Aircraft))throw new Exception("fixture aircraft absent");
            var controller=(UnitController)typeof(Player).GetField("_unitCotroller",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
            LightingProbe.Run(controller,Log);CopilotProbe.Run(controller,Log);FramingProbe.Run(controller,Log);
            ShipProbe.Run(controller,Log);NeutralPalletProbe.Run(controller,Log);
            await CargoProbe.Run(controller,Log);
            await PathPointProbe.Run(player,Log);
            await ReleaseChecks.DayNight(key,Log);
            await ReleaseChecks.RecordPaused(key,Log);
            Log("COMPLETE public release checks");
        }
        catch(Exception e){Log("FAILED "+e);}
        finally{Application.Quit();}
    }
    string WriteMapFixture(MapKey key,string aircraftKey,string missileKey)
    {
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays");Directory.CreateDirectory(folder);
        string name="map-fixture-"+Guid.NewGuid().ToString("N")+".replay";
        using(var stream=new FileStream(Path.Combine(folder,name),FileMode.CreateNew,FileAccess.Write))
        using(var writer=new BinaryWriter(stream))
        {
            writer.Write(12549);writer.Write((byte)5);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)key.Type);writer.Write(key.Path);
            writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);writer.Write(12d);writer.Write((long)6);writer.Write((ushort)0);
            var aircraft=new SpawnEvent{Time=0,unitId=777,ownerId=777,jsonKey=aircraftKey,factionName="no faction",pos=new GlobalPosition(0,3000,0),rotation=Quaternion.identity,startingVelocity=new Vector3(0,0,100),isAircraft=true,weapons=new List<WeaponMount>()};writer.Write((byte)aircraft.EventType);aircraft.Write(writer);
            var missile=new SpawnEvent{Time=1,unitId=778,ownerId=777,jsonKey=missileKey,factionName="no faction",pos=new GlobalPosition(0,3000,0),rotation=Quaternion.identity,startingVelocity=new Vector3(0,0,200),isAircraft=false};writer.Write((byte)missile.EventType);missile.Write(writer);
            foreach(int i in new[]{2,4}){var move=new UpdatePositionEvent{Time=i,unitId=778,position=new GlobalPosition(0,3000,i*200),rotation=Quaternion.identity,velocity=new Vector3(0,0,200)};writer.Write((byte)move.EventType);move.Write(writer);}
            var gone=new DespawnEvent{Time=5,unitId=778};writer.Write((byte)gone.EventType);gone.Write(writer);
            var end=new DespawnEvent{Time=12,unitId=777};writer.Write((byte)end.EventType);end.Write(writer);
        }
        return name;
    }
    string WriteFixture(MapKey key,string aircraftKey)
    {
        string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays");Directory.CreateDirectory(folder);
        string name="ghost-fixture-"+Guid.NewGuid().ToString("N")+".replay";
        using(var stream=new FileStream(Path.Combine(folder,name),FileMode.CreateNew,FileAccess.Write))
        using(var writer=new BinaryWriter(stream))
        {
            const int samples=241;writer.Write(12549);writer.Write((byte)5);writer.Write((byte)0);writer.Write((byte)0);writer.Write((byte)key.Type);writer.Write(key.Path);
            writer.Write(DateTime.UtcNow.Ticks);writer.Write(0);writer.Write(12d);writer.Write((long)(samples+3));writer.Write((ushort)0);
            var spawn=new SpawnEvent {Time=0,unitId=777,ownerId=777,jsonKey=aircraftKey,factionName="no faction",pos=new GlobalPosition(0,3000,0),rotation=Quaternion.identity,startingVelocity=new Vector3(0,0,180),isAircraft=true,weapons=new List<WeaponMount>()};
            writer.Write((byte)spawn.EventType);spawn.Write(writer);
            var gear=new SetGearEvent {Time=0,unitId=777,deployed=false};writer.Write((byte)gear.EventType);gear.Write(writer);
            for(int i=0;i<samples;i++)
            {
                var input=new UpdateInputsEvent {Time=i*0.05,id=777,pitch=0,roll=0,yaw=0,throttle=0.8f,brake=0,customAxis1=0};
                writer.Write((byte)input.EventType);input.Write(writer);
            }
            var gone=new DespawnEvent {Time=12,unitId=777};writer.Write((byte)gone.EventType);gone.Write(writer);
        }
        return name;
    }
}
[HarmonyPatch(typeof(ScreenResolutionHelper),"LoadPrefs")]
public static class CombinedProbeLoadPrefs
{ [HarmonyPrefix] public static bool Prefix(ScreenResolutionHelper __instance){__instance.Current=new Resolution{width=1280,height=720};__instance.FullScreen=false;__instance.CurrentString=ScreenResolutionHelper.GetResolutionString(__instance.Current);return false;} }
[HarmonyPatch(typeof(ScreenResolutionHelper),"SetValues")]
public static class CombinedProbeSetValues { [HarmonyPrefix] public static bool Prefix(ScreenResolutionHelper __instance)=>CombinedProbeLoadPrefs.Prefix(__instance); }
[HarmonyPatch(typeof(ScreenResolutionHelper),"SavePrefs")]
public static class CombinedProbeSavePrefs { [HarmonyPrefix] public static bool Prefix()=>false; }
[HarmonyPatch(typeof(PlayerPrefs),"SetInt")]
public static class CombinedProbeInt { [HarmonyPrefix] public static bool Prefix()=>false; }
[HarmonyPatch(typeof(PlayerPrefs),"SetFloat")]
public static class CombinedProbeFloat { [HarmonyPrefix] public static bool Prefix()=>false; }
[HarmonyPatch(typeof(PlayerPrefs),"SetString")]
public static class CombinedProbeString { [HarmonyPrefix] public static bool Prefix()=>false; }
[HarmonyPatch(typeof(PlayerPrefs),"Save")]
public static class CombinedProbeSave { [HarmonyPrefix] public static bool Prefix()=>false; }
[HarmonyPatch(typeof(StringHelper),"GetSanitizeSteamName")]
public static class CombinedProbeOfflineName { [HarmonyPrefix] public static bool Prefix(out string __0,out string __1){__0="Local Test";__1="Local Test";return false;} }







