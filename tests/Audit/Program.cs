using Mono.Cecil;
using System.Security.Cryptography;

var mod = AssemblyDefinition.ReadAssembly(args[0]);
var game = AssemblyDefinition.ReadAssembly(args[1]);
var managedDirectory = Path.GetDirectoryName(args[1])!;
using var mirage = AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory, "Mirage.dll"));
using var core = AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory, "UnityEngine.CoreModule.dll"));
var particlePath=Path.Combine(managedDirectory,"UnityEngine.ParticleSystemModule.dll");
using var particles=AssemblyDefinition.ReadAssembly(particlePath);
var audioPath=Path.Combine(managedDirectory,"UnityEngine.AudioModule.dll");
using var audio=AssemblyDefinition.ReadAssembly(audioPath);
using var uniTask=AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory,"UniTask.dll"));
// Private nested native structures are publicized at build time. Audit the
// original installed metadata, including those nested types and base APIs.
var types = AllTypes(game.MainModule.Types).Concat(AllTypes(mirage.MainModule.Types)).Concat(AllTypes(core.MainModule.Types)).Concat(AllTypes(particles.MainModule.Types)).Concat(AllTypes(audio.MainModule.Types)).Concat(AllTypes(uniTask.MainModule.Types))
    .Where(t => t.Name != "<Module>").GroupBy(t => t.FullName).ToDictionary(group => group.Key, group => group.First());
int count = 0, failures = 0;
foreach (var type in mod.MainModule.Types)
{
    var classAttributes = type.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch").ToArray();
    foreach (var patch in type.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.Name is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyFinalizer")))
    {
        TypeReference? targetType = null;
        string? targetName = null;
        string[]? parameters = null;
        foreach (var attribute in classAttributes.Concat(patch.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch")))
            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Value is TypeReference t) targetType = t;
                if (argument.Value is string name) targetName = name;
                if (argument.Value is CustomAttributeArgument[] array && argument.Type.FullName == "System.Type[]")
                    parameters = array.Select(a => ((TypeReference)a.Value).FullName).ToArray();
            }
        if (type.Name == "EjectionVisualPatch")
        {
            AuditDynamicTargets(type, patch, "Aircraft", 2, "UserCode_RpcJettisonCanopy_", "UserCode_RpcEscapeCapsule_");
            continue;
        }
        if (type.Name == "ReplayMissileDetonationPatch")
        {
            AuditDynamicTargets(type, patch, "Missile", 1, "UserCode_RpcDetonate_");
            continue;
        }
        if(type.Name=="NativeCameraOwnershipPatch")
        {
            var names=new[]{"OpenMenu","SetCamFree","SetCamCockpit","SetCamOrbit","SetCamChase","SetCamControlled","SetCamFlyby","CancelMovement","ReverseMovement","ApplyCamPos","ApplyCamAngle","ApplyPivotPos","ApplyPivotAngle","ReturnPivotToUnit","SwitchPivotUp","ToggleCameraTarget","ApplyFOV","ResetFOV","ApplyFOVSpeed","ApplyFOVInertia","ApplyInputSpeed","ApplyInputsToggle"};
            var targets=types["CameraControlUI"].Methods.Where(m=>names.Contains(m.Name)).ToArray();
            if(targets.Length!=22||names.Any(n=>targets.Count(m=>m.Name==n)!=1)){failures++;Console.WriteLine("FAIL native camera ownership targets");continue;}
            foreach(var nativeTarget in targets){ValidateInjection(type,patch,nativeTarget);count++;Console.WriteLine("PASS dynamic "+nativeTarget.FullName);}continue;
        }
        if (targetType == null || targetName == null || !types.TryGetValue(targetType.FullName, out var definition))
        { Console.WriteLine($"FAIL unresolved target {type.FullName}.{patch.Name}: {targetType}::{targetName}"); failures++; continue; }
        var matches2 = FindMethods(definition, targetName).Where(m => parameters == null || m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
        if (matches2.Length != 1) { Console.WriteLine($"FAIL {targetType}::{targetName} has {matches2.Length} candidates"); failures++; continue; }
        var target = matches2[0];
        ValidateInjection(type, patch, target);
        count++; Console.WriteLine($"PASS {target.FullName}");
    }
}
Console.WriteLine($"Hook audit: {count} targets, {failures} failures");
int nativeContracts = 0;
if (types.TryGetValue("CameraFreeState", out var freeState) && freeState.Fields.Any(f => f.Name == "FOVAdjustment" && f.FieldType.FullName == "System.Single" && !f.IsStatic))
{ nativeContracts++; Console.WriteLine("PASS native free camera lens offset field"); }
else { failures++; Console.WriteLine("FAIL native free camera lens offset field"); }
if (types.TryGetValue("CameraStateManager", out var cameraManager) && cameraManager.Methods.Any(m => m.Name == "SetDesiredFoV" && !m.IsStatic && m.ReturnType.FullName == "System.Void" && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Single", "System.Single" })))
{ nativeContracts++; Console.WriteLine("PASS native camera lens setter"); }
else { failures++; Console.WriteLine("FAIL native camera lens setter"); }
Console.WriteLine($"Camera API audit: {nativeContracts} contracts, {2-nativeContracts} failures");
int boosterContracts = 0;
if (types.TryGetValue("VLSBooster", out var booster))
{
    foreach (var methodName in new[] { "Activate", "Burnout" })
        if (booster.Methods.Count(m => m.Name == methodName && !m.IsStatic && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 0) == 1)
        { boosterContracts++; Console.WriteLine("PASS native booster " + methodName); }
        else { failures++; Console.WriteLine("FAIL native booster " + methodName); }
    foreach (var (fieldName, fieldType) in new[] { ("delayTimer", "System.Single"), ("activated", "System.Boolean"), ("separated", "System.Boolean"), ("fuelMass", "System.Single") })
        if (booster.Fields.Any(f => f.Name == fieldName && !f.IsStatic && f.FieldType.FullName == fieldType))
        { boosterContracts++; Console.WriteLine("PASS native booster " + fieldName); }
        else { failures++; Console.WriteLine("FAIL native booster " + fieldName); }
}
else { failures += 6; Console.WriteLine("FAIL native VLSBooster type"); }
Console.WriteLine($"Booster API audit: {boosterContracts} contracts, {6-boosterContracts} failures");
int uiContracts = 0;
var uiPath = Path.Combine(Path.GetDirectoryName(args[1])!, "UnityEngine.UI.dll");
using (var ui = AssemblyDefinition.ReadAssembly(uiPath))
{
    foreach (var (typeName, methodName) in new[] {
        ("UnityEngine.UI.Graphic", "OnEnable"),
        ("UnityEngine.UI.Graphic", "OnTransformParentChanged"),
        ("UnityEngine.UI.CanvasScaler", "OnEnable"),
        ("UnityEngine.EventSystems.BaseRaycaster", "OnEnable") })
    {
        var definition = ui.MainModule.Types.SingleOrDefault(t => t.FullName == typeName);
        var matches = definition?.Methods.Where(m => m.Name == methodName && !m.IsStatic &&
            m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Void" && m.HasBody).ToArray();
        if (matches?.Length == 1)
        { uiContracts++; Console.WriteLine("PASS managed UI lifecycle " + typeName + "::" + methodName); }
        else
        { failures++; Console.WriteLine("FAIL managed UI lifecycle " + typeName + "::" + methodName); }
    }
}
Console.WriteLine($"UI lifecycle audit: {uiContracts} contracts, {4-uiContracts} failures");
Console.WriteLine($"Game UI SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(uiPath)))}");
int replayNativeContracts = 0;
if (types.TryGetValue("GameplayUI", out var gameplayUi) && gameplayUi.Properties.Any(p => p.Name == "GameSlowMotion" &&
    p.PropertyType.FullName == "System.Boolean" && p.GetMethod?.IsStatic == true && p.SetMethod?.IsStatic == true &&
    p.SetMethod.HasBody && p.SetMethod.Parameters.Select(a => a.ParameterType.FullName).SequenceEqual(new[] { "System.Boolean" })))
{ replayNativeContracts++; Console.WriteLine("PASS native slow-motion toggle state setter"); }
else { failures++; Console.WriteLine("FAIL native slow-motion toggle state setter"); }
var corePath = Path.Combine(managedDirectory, "UnityEngine.CoreModule.dll");
{
    var objectType = core.MainModule.Types.SingleOrDefault(t => t.FullName == "UnityEngine.Object");
    if (objectType?.Methods.Any(m => m.Name == "FindObjectsByType" && m.IsStatic &&
        m.ReturnType.FullName == "UnityEngine.Object[]" && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(
            new[] { "System.Type", "UnityEngine.FindObjectsInactive", "UnityEngine.FindObjectsSortMode" })) == true)
    { replayNativeContracts++; Console.WriteLine("PASS unsorted sparse UI discovery API"); }
    else { failures++; Console.WriteLine("FAIL unsorted sparse UI discovery API"); }
}
Console.WriteLine($"Replay native API audit: {replayNativeContracts} contracts, {2-replayNativeContracts} failures");

int sceneContracts = 0, sceneChecks = 0;
Scene("native offline host startup", HasMethod("NuclearOption.Networking.NetworkManagerNuclearOption", "StartHostAsync", "Cysharp.Threading.Tasks.UniTask", false, "NuclearOption.Networking.HostOptions"));
Scene("native offline host stop", HasMethod("NuclearOption.Networking.NetworkManagerNuclearOption", "StopAsync", "Cysharp.Threading.Tasks.UniTask", false, "System.Boolean"));
Scene("native stop-in-progress guard", HasFields("NuclearOption.Networking.NetworkManagerNuclearOption", false, ("stopping", "System.Boolean")));
Scene("native scene-loading flag", HasProperty("NuclearOption.Networking.NetworkManagerNuclearOption", "IsLoadingScene", "System.Boolean", true));
Scene("native network manager identity", HasProperty("NuclearOption.Networking.NetworkManagerNuclearOption", "i", "NuclearOption.Networking.NetworkManagerNuclearOption", true));
Scene("native mission-running flag", HasProperty("MissionManager", "IsRunning", "System.Boolean", true));
Scene("native mission reference", HasProperty("MissionManager", "CurrentMission", "NuclearOption.SavedMission.Mission", true));
Scene("native mission assignment", HasMethod("MissionManager", "SetMission", "System.Void", true, "NuclearOption.SavedMission.Mission", "System.Boolean"));
Scene("native loaded map settings", HasProperty("LevelInfo", "LoadedMapSettings", "MapSettings", false));
Scene("native map settings loader", HasFields("MapSettingsManager", false, ("MapLoader", "NuclearOption.SceneLoading.MapLoader")));
Scene("native current map identity", HasProperty("NuclearOption.SceneLoading.MapLoader", "CurrentMap", "NuclearOption.SceneLoading.MapKey", false));
Scene("native map-load result semantics", EnumValues("NuclearOption.SceneLoading.MapLoader/LoadResult", ("None", 0), ("InvalidKey", 1), ("Failed", 2), ("ChangedScene", 3), ("ChangedWorldPrefab", 4), ("AlreadyLoaded", 5)));
Scene("native menu scene identities", HasFields("NuclearOption.SceneLoading.MapLoader", true, ("MainMenu", "System.String"), ("MultiplayerMenu", "System.String")));
Scene("native network server activity", HasProperty("Mirage.NetworkServer", "Active", "System.Boolean", false));
Scene("native network client activity", HasProperty("Mirage.NetworkClient", "Active", "System.Boolean", false));
Scene("native host local player", HasProperty("Mirage.NetworkServer", "LocalPlayer", "Mirage.INetworkPlayer", false));
Scene("native host player scene readiness", HasProperty("Mirage.INetworkPlayer", "SceneIsReady", "System.Boolean", false));
Scene("native scene singleton slots", HasFields("SceneSingleton`1", true, ("i", "T")) && HasFields("NetworkSceneSingleton`1", true, ("i", "T")));
Scene("Unity active scene getter", HasMethod("UnityEngine.SceneManagement.SceneManager", "GetActiveScene", "UnityEngine.SceneManagement.Scene", true));
Scene("Unity scene validity and generation", HasMethod("UnityEngine.SceneManagement.Scene", "IsValid", "System.Boolean", false) && HasProperty("UnityEngine.SceneManagement.Scene", "handle", "System.Int32", false) && HasProperty("UnityEngine.SceneManagement.Scene", "isLoaded", "System.Boolean", false));
Scene("Unity object scene ownership", HasProperty("UnityEngine.GameObject", "scene", "UnityEngine.SceneManagement.Scene", false));
Console.WriteLine($"Scene lifecycle API audit: {sceneContracts} contracts, {sceneChecks-sceneContracts} failures");

int aircraftContracts = 0, aircraftChecks = 0;
Aircraft("native aircraft gear and controls", HasFields("Aircraft", false, ("gearDeployed", "System.Boolean"), ("gearState", "LandingGear/GearState"), ("controlInputs", "ControlInputs")));
Aircraft("native aircraft part ordering", HasFields("Unit", false, ("partLookup", "System.Collections.Generic.List`1<UnitPart>")) && HasFields("UnitPart", false, ("id", "System.Byte"), ("attachInfo", "UnitPart/AttachInfo")) && HasFields("UnitPart/AttachInfo", false, ("parentPart", "UnitPart")));
Aircraft("native detached part predicate", HasMethod("UnitPart", "IsDetached", "System.Boolean", false));
Aircraft("native gear state enum", EnumValues("LandingGear/GearState", ("Uninitialized", 0), ("LockedRetracted", 1), ("LockedExtended", 2), ("Retracting", 3), ("Extending", 4)));
Aircraft("native immediate gear setter", HasMethod("Aircraft", "SetGear", "System.Void", false, "LandingGear/GearState"));
Aircraft("native gear channel transforms", HasFields("LandingGear", false, ("attachedPart", "AeroPart"), ("aircraft", "Aircraft"), ("gearHinge", "UnityEngine.Transform"), ("strutRotationTransform", "UnityEngine.Transform"), ("unsprung", "UnityEngine.GameObject"), ("wheels", "UnityEngine.Transform[]"), ("gearDoors", "System.Collections.Generic.List`1<LandingGear/GearDoor>"), ("movingParts", "GearPart[]"), ("joints", "LandingGear/IKJoint[]")));
Aircraft("native gear door and link transforms", HasFields("LandingGear/GearDoor", false, ("transform", "UnityEngine.Transform")) && HasFields("GearPart", false, ("transform", "UnityEngine.Transform")));
Aircraft("native gear IK transforms and update", HasFields("LandingGear/IKJoint", false, ("hinge1", "UnityEngine.Transform"), ("hinge2", "UnityEngine.Transform")) && HasMethod("LandingGear/IKJoint", "Update", "System.Void", false));
Aircraft("native gear suspension visual fallback", HasFields("LandingGear", false, ("bumpStop", "UnityEngine.GameObject"), ("castPoint", "UnityEngine.Transform"), ("suspensionTravel", "System.Single"), ("wheelRadius", "System.Single"), ("foldAmount", "System.Single"), ("compressionDistance", "System.Single"), ("steering", "System.Boolean"), ("steeringAngle", "System.Single"), ("steeringLock", "System.Single")));
Aircraft("native sweep controllers and remapping", HasFields("SwingWingController", false, ("aircraft", "Aircraft"), ("rotators", "SwingWingController/RotatorInput[]"), ("lockingControlSurfaces", "ControlSurface[]"), ("axisRemapLower", "System.Single"), ("axisRemapUpper", "System.Single"), ("positionPrev", "System.Single")));
Aircraft("native sweep part and transform channels", HasFields("SwingWingController/RotatorInput", false, ("part", "AeroPart"), ("anchor", "UnityEngine.Transform"), ("connectedParts", "AeroPart[]"), ("counterRotators", "UnityEngine.Transform[]")));
Aircraft("native sweep pose fallback", HasFields("SwingWingController/RotatorInput", false, ("minAngle", "System.Single"), ("maxAngle", "System.Single"), ("currentAngle", "System.Single"), ("condition", "System.Single")) && HasMethod("SwingWingController/RotatorInput", "Animate", "System.Void", false, "System.Single", "System.Single&"));
Aircraft("native control surface channel transforms", HasFields("ControlSurface", false, ("attachedSurface", "UnitPart"), ("visibleMesh", "UnityEngine.GameObject"), ("splitUpper", "UnityEngine.Transform"), ("splitLower", "UnityEngine.Transform")) && HasMethod("ControlSurface", "SetLocked", "System.Void", false, "System.Boolean"));
Aircraft("native airbrake ownership and transforms", HasFields("Airbrake", false, ("aircraft", "Aircraft"), ("part", "UnitPart"), ("transforms", "UnityEngine.Transform[]")));
Aircraft("native tilt-wing controller and state", HasFields("TiltWingController", false, ("rotatingJoints", "TiltWingController/RotatorInput[]"), ("tiltCorrectionPosition", "System.Single")));
Aircraft("native tilt-wing part and linkage channels", HasFields("TiltWingController/RotatorInput", false, ("part", "AeroPart"), ("links", "TiltWingController/RotatorLinkage[]"), ("currentAngle", "System.Single")));
Aircraft("native tilt-wing legacy pose fallback", HasFields("TiltWingController/RotatorInput", false, ("minAngle", "System.Single"), ("maxAngle", "System.Single"), ("yawFactor", "System.Single"), ("pitchFactor", "System.Single")) && HasMethod("TiltWingController/RotatorInput", "Animate", "System.Void", false, "ControlInputs", "System.Single", "System.Single"));
Aircraft("native tilt-wing linkage geometry", HasFields("TiltWingController/RotatorLinkage", false, ("linkage", "UnityEngine.Transform"), ("stretchTo", "UnityEngine.Transform")) && HasMethod("TiltWingController/RotatorLinkage", "Animate", "System.Void", false));
Aircraft("native ducted thrust channel collections", HasFields("DuctedThrustSystem", false, ("swivelTransforms", "DuctedThrustSystem/SwivelTransform[]"), ("intakeDoors", "DuctedThrustSystem/IntakeDoor[]")));
Aircraft("native ducted nozzle and intake channels", HasFields("DuctedThrustSystem/SwivelTransform", false, ("transform", "UnityEngine.Transform"), ("part", "UnitPart")) && HasFields("DuctedThrustSystem/IntakeDoor", false, ("hinge", "UnityEngine.Transform")));
Aircraft("native jet nozzle vector channels", HasFields("JetNozzle", false, ("vectorTransforms", "UnityEngine.Transform[]"), ("part", "UnitPart")));
Aircraft("native tailhook ownership and transform", HasFields("TailHook", false, ("hook", "UnityEngine.Transform"), ("aircraft", "Aircraft"), ("unitPart", "UnitPart")));
Aircraft("native tailhook legacy pose fallback", HasFields("TailHook", false, ("deployedAmount", "System.Single"), ("deployed", "System.Boolean"), ("stowedAngle", "System.Single"), ("deployedAngle", "System.Single")) && HasMethod("TailHook", "CheckDeployConditions", "System.Void", false));
Aircraft("native canopy hinge channels", HasFields("Canopy", false, ("canopyHinges", "Canopy/CanopyHinge[]"), ("attachedPart", "AeroPart")) && HasFields("Canopy/CanopyHinge", false, ("transform", "UnityEngine.Transform")));
Aircraft("native bay-door transform channel", HasProperty("BayDoor", "transform", "UnityEngine.Transform", false));
Console.WriteLine($"Aircraft animation API audit: {aircraftContracts} contracts, {aircraftChecks-aircraftContracts} failures");

int vfxContracts = 0, vfxChecks = 0;
Vfx("native unit presentation state", HasFields("Unit", false, ("disabled", "System.Boolean"), ("unitState", "Unit/UnitState")) && HasFields("UnitPart", false, ("parentUnit", "Unit")));
Vfx("native fuel tank part and fire flags", HasFields("FuelTank", false, ("part", "UnitPart"), ("ruptured", "System.Boolean"), ("onFire", "System.Boolean"), ("leakRate", "System.Single"), ("maxLeakRate", "System.Single")));
Vfx("native fuel tank presentation effects", HasFields("FuelTank", false, ("fireEffect", "UnityEngine.GameObject"), ("fireEffectSpawn", "UnityEngine.GameObject"), ("fireballSpawn", "UnityEngine.GameObject"), ("fireParticles", "DamageParticles"), ("leakSystem", "UnityEngine.ParticleSystem")));
Vfx("native fuel presentation callbacks", HasMethod("FuelTank", "RuptureEffects", "System.Void", false) && HasMethod("FuelTank", "IgnitionEffects", "System.Void", false));
Vfx("native hosted flame registration", HasMethod("UnitPart", "AddHostedParticles", "System.Void", false, "DamageParticles"));
Vfx("native attached damage particles", HasFields("DamageParticles", false, ("fireDamage", "System.Single"), ("unitPart", "UnitPart"), ("rb", "UnityEngine.Rigidbody"), ("detached", "System.Boolean"), ("speed", "System.Single")));
Vfx("native missile detonation presentation members", HasFields("Missile", false, ("warhead", "Missile/Warhead"), ("blastYield", "System.Single"), ("motors", "Missile/Motor[]"), ("effectsTransform", "UnityEngine.Transform"), ("flightSound", "UnityEngine.AudioSource"), ("nearbyDetonationClip", "UnityEngine.AudioClip")));
Vfx("native missile warhead effect variants", HasFields("Missile/Warhead", false, ("fizzleEffect", "UnityEngine.GameObject"), ("airEffect", "UnityEngine.GameObject"), ("terrainEffect", "UnityEngine.GameObject"), ("armorEffect", "UnityEngine.GameObject"), ("underwaterEffect", "UnityEngine.GameObject"), ("waterSurfaceEffect", "UnityEngine.GameObject")));
Vfx("native missile motor visual burnout", HasMethod("Missile/Motor", "Burnout", "System.Void", false, "System.Boolean"));
Vfx("native shockwave ownership setter", HasMethod("Shockwave", "SetOwner", "System.Void", false, "PersistentID", "System.Single"));
Vfx("native shockwave force influence list", HasFields("Shockwave", false, ("influencedObjects", "System.Collections.Generic.List`1<Shockwave/InfluencedObject>")));
Vfx("Unity particle simulation module accessor",HasProperty("UnityEngine.ParticleSystem","main","UnityEngine.ParticleSystem/MainModule",false));
Vfx("Unity owned particle culling mode getter and setter",HasProperty("UnityEngine.ParticleSystem/MainModule","cullingMode","UnityEngine.ParticleSystemCullingMode",false)&&
    HasMethod("UnityEngine.ParticleSystem/MainModule","set_cullingMode","System.Void",false,"UnityEngine.ParticleSystemCullingMode"));
Vfx("Unity always-simulate particle mode",EnumValues("UnityEngine.ParticleSystemCullingMode",("AlwaysSimulate",3)));
Vfx("Unity native material fade lookup",HasMethod("UnityEngine.Material","HasProperty","System.Boolean",false,"System.Int32")&&HasMethod("UnityEngine.Material","GetVector","UnityEngine.Vector4",false,"System.Int32"));
Vfx("Unity property block fade preservation",HasProperty("UnityEngine.MaterialPropertyBlock","isEmpty","System.Boolean",false)&&HasMethod("UnityEngine.MaterialPropertyBlock","GetVector","UnityEngine.Vector4",false,"System.Int32")&&HasMethod("UnityEngine.MaterialPropertyBlock","SetVector","System.Void",false,"System.Int32","UnityEngine.Vector4"));
Vfx("Unity per-renderer and per-material property blocks",HasMethod("UnityEngine.Renderer","GetPropertyBlock","System.Void",false,"UnityEngine.MaterialPropertyBlock")&&HasMethod("UnityEngine.Renderer","GetPropertyBlock","System.Void",false,"UnityEngine.MaterialPropertyBlock","System.Int32")&&HasMethod("UnityEngine.Renderer","SetPropertyBlock","System.Void",false,"UnityEngine.MaterialPropertyBlock","System.Int32"));
Vfx("Unity shared material identity accessor",HasProperty("UnityEngine.Renderer","sharedMaterials","UnityEngine.Material[]",false));
Vfx("native distant fragment spawn lifetime guard",HasFields("UnitPart",false,("fragmented","System.Boolean"))&&HasMethod("AeroPart","SpawnFragments","System.Void",false)&&HasMethod("AeroPart","Detach","System.Void",false,"UnityEngine.Vector3","UnityEngine.Vector3"));
Console.WriteLine($"VFX API audit: {vfxContracts} contracts, {vfxChecks-vfxContracts} failures");
int gunContracts=0,gunChecks=0;
Gun("native gun firing entry points",HasMethod("Gun","SpawnBullet","System.Void",false,"System.Single")&&
    HasMethod("Gun","FixedUpdate","System.Void",false)&&
    HasMethod("Gun","RemoteSingleFire","System.Void",false,"Unit","Unit","UnityEngine.Vector3","WeaponStation","GlobalPosition"));
Gun("native resolved bullet insertion API",HasMethod("BulletSim","AddBullet","System.Void",false,"UnityEngine.Transform","UnityEngine.Vector3","System.Single","System.Single","System.Boolean","System.Single","UnityEngine.Color","System.Single","Unit")&&
    HasMethod("BulletSim","Create","BulletSim",true,"Unit","Gun","Turret"));
Gun("native resolved projectile fields",HasFields("BulletSim",false,("owner","Unit"),("gun","Gun"),("bullets","System.Collections.Generic.List`1<BulletSim/Bullet>"))&&
    HasFields("BulletSim/Bullet",false,("position","GlobalPosition"),("velocity","UnityEngine.Vector3"),("active","System.Boolean"),("impacted","System.Boolean"),("destructTimer","System.Single"),("reliability","System.Single"),("explosive","System.Boolean"),("tracer","BulletSim/TracerView")));
Gun("native gun station and complex-part identity",HasFields("Unit",false,("weaponStations","System.Collections.Generic.List`1<WeaponStation>"),("partLookup","System.Collections.Generic.List`1<UnitPart>"))&&
    HasFields("WeaponStation",false,("Weapons","System.Collections.Generic.List`1<Weapon>"),("Number","System.Byte"))&&
    HasFields("Weapon",false,("attachedUnit","Unit"),("hardpoint","Hardpoint"))&&HasFields("Hardpoint",false,("part","UnitPart"))&&
    HasFields("UnitPart",false,("id","System.Byte"),("parentUnit","Unit"))&&HasFields("WeaponInfo",false,("weaponName","System.String")));
Gun("native burst sound state and loop maintenance",HasMethod("Gun","ShotSound","System.Void",false)&&HasMethod("Gun","LoopSounds","System.Void",false)&&
    HasFields("Gun",false,("sources","UnityEngine.AudioSource[]"),("recoilSound","UnityEngine.AudioSource"),("muzzleParticles","UnityEngine.ParticleSystem[]"),("fireInterval","System.Single"))&&
    HasFields("Weapon",false,("lastFired","System.Single")));
Gun("native audio pause resume and stop",HasMethod("UnityEngine.AudioSource","Pause","System.Void",false)&&
    HasMethod("UnityEngine.AudioSource","UnPause","System.Void",false)&&HasMethod("UnityEngine.AudioSource","Stop","System.Void",false)&&
    HasProperty("UnityEngine.AudioSource","isPlaying","System.Boolean",false));
Console.WriteLine($"Gun API audit: {gunContracts} contracts, {gunChecks-gunContracts} failures");
int presentationContracts = 0, presentationChecks = 0;
var modTypes = AllTypes(mod.MainModule.Types).ToDictionary(type => type.FullName);
Presentation("late recording execution order", ModExecutionOrder("ReplayMod.Core.Recorder", 10000));
Presentation("late replay presentation execution order", ModExecutionOrder("ReplayMod.Core.ReplayAircraftVisualLatePass", 10000));
Presentation("late recorder captures aircraft geometry", CallsModMethod("ReplayMod.Core.Recorder", "LateUpdate", "ReplayMod.Core.Recorder", "RecordAircraftVisuals"));
Presentation("late replay presentation reaches recorded geometry", CallsModMethod("ReplayMod.Core.ReplayAircraftVisualLatePass", "LateUpdate", "ReplayMod.Core.Player", "RefreshAircraftVisualsLate") && CallsModMethod("ReplayMod.Core.Player", "RefreshAircraftVisualsLate", "ReplayMod.Core.UnitController", "RefreshAircraftVisualsLate") && CallsModMethod("ReplayMod.Core.UnitController", "RefreshAircraftVisualsLate", "ReplayMod.Core.UnitController", "ApplyRecordedAircraftVisuals"));
Presentation("replay manager installs late presentation component", ModMethod("ReplayMod.Core.ReplayManager", "Start")?.Body.Instructions.Any(instruction => instruction.Operand is GenericInstanceMethod method && method.DeclaringType.FullName == "UnityEngine.GameObject" && method.Name == "AddComponent" && method.GenericArguments.Any(argument => argument.FullName == "ReplayMod.Core.ReplayAircraftVisualLatePass")) == true);
Presentation("late presentation does not advance legacy animation", ModMethod("ReplayMod.Core.UnitController", "RefreshAircraftVisualsLate")?.Body.Instructions.All(instruction => !(instruction.Operand is MethodReference method && (method.Name.StartsWith("ApplyLegacy", StringComparison.Ordinal) || method.Name == "UpdateAircraftVisuals"))) == true);
Console.WriteLine($"Replay presentation IL audit: {presentationContracts} contracts, {presentationChecks-presentationContracts} failures");
int cameraControlContracts=0,cameraControlChecks=0;
CameraControl("native panel refresh and camera selection contracts",HasMethod("CameraControlUI","GetValues","System.Void",false)&&HasMethod("CameraControlUI","UpdateValues","System.Void",false)&&
    HasMethod("CameraStateManager","SetFollowingUnit","System.Void",false,"Unit"));
CameraControl("native refresh uses a callback-emitting time slider",types["CameraControlUI"].Methods.Any(m=>m.Name=="GetValues"&&m.HasBody&&
    m.Body.Instructions.Any(i=>i.Operand is FieldReference field&&field.Name=="timeFactor")&&
    m.Body.Instructions.Any(i=>i.Operand is MethodReference method&&method.Name=="set_value"&&method.DeclaringType.FullName=="UnityEngine.UI.Slider")));
CameraControl("programmatic refresh enters and finally releases callback suppression",
    CallsModMethod("ReplayMod.Patches.NativeCameraPanelRefreshPatch","Prefix","ReplayMod.Core.NativeCameraRefreshScope","Enter")&&
    CallsModMethod("ReplayMod.Patches.NativeCameraPanelRefreshPatch","Finalizer","ReplayMod.Core.NativeCameraRefreshScope","Exit")&&
    ModMethod("ReplayMod.Patches.NativeCameraPanelRefreshPatch","Finalizer")?.ReturnType.FullName=="System.Exception");
CameraControl("native time edit ignores refresh and requested clock display has no callback",
    CallsModMethod("ReplayMod.Patches.NativeCameraTimeFactorPatch","Prefix","ReplayMod.Core.NativeCameraRefreshScope","get_IsActive")&&
    CallsModMethod("ReplayMod.Patches.NativeCameraPanelRefreshPatch","Postfix","UnityEngine.UI.Slider","SetValueWithoutNotify"));
CameraControl("native movement handoff releases studio ownership without changing clock",
    CallsModMethod("ReplayMod.Director.ReplayDirector","EnterNativeCameraControls","ReplayMod.Director.ReplayDirector","CancelCameraTake")&&
    CallsModMethod("ReplayMod.Director.ReplayDirector","EnterNativeCameraControls","ReplayMod.Director.ReplayDirector","ReleaseCamera")&&
    !CallsModMethod("ReplayMod.Director.ReplayDirector","EnterNativeCameraControls","TimeScaleManager","set_Scale"));
Console.WriteLine($"Camera control integration audit: {cameraControlContracts} contracts, {cameraControlChecks-cameraControlContracts} failures");
int takeContracts=0,takeChecks=0;
Take("restoring UI cancels an active camera take",CallsModMethod("ReplayMod.Director.ReplayDirector","SetClean","ReplayMod.Director.ReplayDirector","CancelCameraTake"));
Take("play and toggle cannot escape a camera take hold",CallsModMethod("ReplayMod.Core.Player","Resume","ReplayMod.Director.ReplayDirector","get_CameraTakeHoldsPlayback")&&CallsModMethod("ReplayMod.Core.Player","TogglePause","ReplayMod.Director.ReplayDirector","get_CameraTakeHoldsPlayback"));
Take("player applies an exact camera take endpoint",CallsModMethod("ReplayMod.Core.Player","ClampPlaybackTime","ReplayMod.Core.PlaybackControlState","LimitTime")&&CallsModMethod("ReplayMod.Core.Player","LateUpdate","ReplayMod.Core.Player","ClampPlaybackTime"));
Console.WriteLine($"Camera take integration audit: {takeContracts} contracts, {takeChecks-takeContracts} failures");
int gunIntegrationContracts=0,gunIntegrationChecks=0;
GunIntegration("recording delegates to resolved post-spread gun capture",CallsModMethod("ReplayMod.Core.Recorder","RecordBullet","ReplayMod.Core.Recorder","RecordResolvedBullet")&&
    CallsModMethod("ReplayMod.Debug.Patches.BulletSimPatch","Postfix","ReplayMod.Core.Recorder","RecordBullet"));
GunIntegration("pending native shots are accepted and ordered behind their actor spawn",new[]{"BeginGunShot","RecordResolvedBullet"}.All(name=>
    CallsModMethod("ReplayMod.Core.Recorder",name,"ReplayMod.Core.Recorder","IsTrackedOrPending")&&CallsModMethod("ReplayMod.Core.Recorder",name,"ReplayMod.Core.Recorder","QueueEventForUnit")));
GunIntegration("native shot grouping ends through exception-safe finalizer",CallsModMethod("ReplayMod.Patches.ReplayGunShotPatch","Prefix","ReplayMod.Core.Recorder","BeginGunShot")&&
    CallsModMethod("ReplayMod.Patches.ReplayGunShotPatch","Finalizer","ReplayMod.Core.Recorder","EndGunShot")&&
    ModMethod("ReplayMod.Patches.ReplayGunShotPatch","Finalizer")?.ReturnType.FullName=="System.Exception");
GunIntegration("both gun suppression paths require exact replay actor ownership",CallsModMethod("ReplayMod.Patches.ReplayGunShotPatch","Prefix","ReplayMod.Patches.ReplayGunPatchOwnership","ShouldSuppress")&&
    CallsModMethod("ReplayMod.Patches.ReplayGunSimulationPatch","Prefix","ReplayMod.Patches.ReplayGunPatchOwnership","ShouldSuppress")&&
    CallsModMethod("ReplayMod.Patches.ReplayGunPatchOwnership","ShouldSuppress","ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&
    CallsModMethod("ReplayMod.Patches.ReplayGunPatchOwnership","ShouldSuppress","ReplayMod.Core.Player","OwnsReplayUnit")&&
    CallsModMethod("ReplayMod.Core.Player","OwnsReplayUnit","ReplayMod.Core.UnitController","OwnsReplayUnit"));
GunIntegration("new and legacy bullet sound use replay burst helper",CallsModOverload("ReplayMod.Core.UnitController","Execute",new[]{"ReplayMod.Events.ConcreteEvents.BulletEvent"},"ReplayMod.Core.UnitController","PlayReplayGunShot")&&
    CallsModOverload("ReplayMod.Core.UnitController","Execute",new[]{"ReplayMod.Events.ConcreteEvents.GunBulletEvent"},"ReplayMod.Core.UnitController","PlayReplayGunShot"));
GunIntegration("gun audio maintenance follows presentation clock",CallsModMethod("ReplayMod.Core.UnitController","Update","ReplayMod.Core.UnitController","UpdateReplayGunAudio")&&
    CallsModMethod("ReplayMod.Core.UnitController","UpdateReplayGunAudio","ReplayMod.Core.ReplayGunAudioPlayback","Update")&&
    CallsModMethod("ReplayMod.Core.UnitController","PlayReplayGunShot","ReplayMod.Core.ReplayGunAudioPlayback","PlayShot")&&
    CallsModMethod("ReplayMod.Core.ReplayGunAudioPlayback","Update","Gun","LoopSounds")&&
    CallsModMethod("ReplayMod.Core.ReplayGunAudioPlayback","PlayShot","Gun","ShotSound"));
GunIntegration("gun audio is cleaned at actor and world teardown",CallsModMethod("ReplayMod.Core.UnitController","DespawnUnit","ReplayMod.Core.UnitController","ForgetReplayGuns")&&
    CallsModMethod("ReplayMod.Core.UnitController","ReleaseOwnedPresentationObjects","ReplayMod.Core.UnitController","ClearReplayGuns")&&
    CallsModMethod("ReplayMod.Core.Recorder","HandleUnitUnregister","ReplayMod.Core.Recorder","ForgetRecordingGuns"));
Console.WriteLine($"Gun integration audit: {gunIntegrationContracts} contracts, {gunIntegrationChecks-gunIntegrationContracts} failures");
var worldAudit=WorldContractChecks.Run(types,mod,managedDirectory);failures+=worldAudit.Failures;
var lifecycleAudit=AircraftLifecycleChecks.Run(types,mod,managedDirectory);failures+=lifecycleAudit.Failures;
var seekLifetimeAudit=SeekLifetimeChecks.Run(types,mod);failures+=seekLifetimeAudit.Failures;
var propAudioAudit=PropAudioChecks.Run(types,mod);failures+=propAudioAudit.Failures;
var navMaterialAudit=NavMaterialContractChecks.Run(types,mod);failures+=navMaterialAudit.Failures;
var inputReaderAudit=ReaderContractChecks.Run(mod);failures+=inputReaderAudit.Failures;
var cloudClockAudit=CloudClockChecks.Run(types,mod,managedDirectory);failures+=cloudClockAudit.Failures;
var startupControlAudit=StartupControlChecks.Run(types,mod);failures+=startupControlAudit.Failures;
var closeCameraAudit=CloseCameraChecks.Run(types,mod,managedDirectory);failures+=closeCameraAudit.Failures;
var interiorAudit=InteriorPresentationChecks.Run(types,mod,managedDirectory);failures+=interiorAudit.Failures;
var previewAudit=CameraPreviewContractChecks.Run(mod);failures+=previewAudit.Failures;
var countermeasureAudit=CountermeasureContractChecks.Run(types,mod);failures+=countermeasureAudit.Failures;
var identityAudit=ActorIdentityContractChecks.Run(types,mod);failures+=identityAudit.Failures;
var lightingFireAudit=LightingFireContractChecks.Run(types,mod);failures+=lightingFireAudit.Failures;
var missileImpactAudit=MissileImpactContractChecks.Run(types,mod);failures+=missileImpactAudit.Failures;
var studioLightUiAudit=StudioLightUiChecks.Run(mod);failures+=studioLightUiAudit.Failures;
var studioLightRuntimeAudit=StudioLightRuntimeChecks.Run(types,mod);failures+=studioLightRuntimeAudit.Failures;
var neutralCargoSpawnAudit=NeutralCargoSpawnChecks.Run(types,mod);failures+=neutralCargoSpawnAudit.Failures;
var pathTimingAudit=CameraPathTimingChecks.Run(mod);failures+=pathTimingAudit.Failures;
var cameraSmoothnessAudit=CameraSmoothnessContractChecks.Run(mod);failures+=cameraSmoothnessAudit.Failures;
var gunSpinAudit=GunSpinContractChecks.Run(types,mod);failures+=gunSpinAudit.Failures;
var mapBuildingAudit=MapBuildingContractChecks.Run(types,mod);failures+=mapBuildingAudit.Failures;
var editorReplayAudit=EditorReplaySceneChecks.Run(types,mod);failures+=editorReplayAudit.Failures;
var ejectionAudit=EjectionCameraContractChecks.Run(mod);failures+=ejectionAudit.Failures;
var replayRenameAudit=ReplayRenameContractChecks.Run(mod);failures+=replayRenameAudit.Failures;
var openingFailureAudit=ReplayOpeningFailureContractChecks.Run(mod);failures+=openingFailureAudit.Failures;
var openingAirframeAudit=AirframeOpeningStateContractChecks.Run(mod);failures+=openingAirframeAudit.Failures;
var damageRestoreAudit=DamageRestoreContractChecks.Run(types,mod);failures+=damageRestoreAudit.Failures;
var vehicleGunAudioAudit=VehicleGunAudioContractChecks.Run(types,mod);failures+=vehicleGunAudioAudit.Failures;
int replay0345Checks=0,replay0345Contracts=0;
Replay0345("native pause menu pauses the active replay",CallsModMethod("ReplayMod.Patches.NativePauseReplayPatch","Prefix","ReplayMod.Core.Player","Pause"));
Replay0345("native resume restores only a saved playing replay",CallsModMethod("ReplayMod.Patches.NativeResumeReplayPatch","Postfix","ReplayMod.Core.Player","Resume"));
Replay0345("quit mission tears down replay state",CallsModMethod("ReplayMod.Patches.NativeQuitReplayPatch","Prefix","ReplayMod.Core.ReplayManager","SwitchState"));
Replay0345("comma no longer invokes the backward seek",!CallsModMethod("ReplayMod.Director.ReplayDirector","Update","ReplayMod.Director.ReplayDirector","Seek"));
Replay0345("main menu capture shortcut requires an active mission",CallsModMethod("ReplayMod.Director.ReplayDirector","Update","MissionManager","get_IsRunning"));
Replay0345("every clean capture entry point requires an active mission",CallsModMethod("ReplayMod.Director.ReplayDirector","SetClean","MissionManager","get_IsRunning"));
Replay0345("timeline receives current camera path keys",CallsModMethod("ReplayMod.UI.TrailerSuiteUi","RefreshTimelineMarkers","ReplayMod.UI.TimelineMarkerGraphic","SetCameraKeys"));
Replay0345("chase movement is replay controlled",CallsModMethod("ReplayMod.Director.ReplayDirector","Update","ReplayMod.Director.ReplayDirector","UpdateChaseInput"));
Replay0345("reconstruction status is explicitly visible during clean capture",!CallsModMethod("ReplayMod.UI.TrailerSuiteUi","LateUpdate","ReplayMod.Director.ReplayDirector","get_Clean")&&
    ModMethod("ReplayMod.UI.TrailerSuiteUi","LateUpdate")?.Body.Instructions.Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Ldc_I4_1&&i.Next?.Operand is MethodReference m&&m.Name=="set_enabled"&&m.DeclaringType.FullName=="UnityEngine.Behaviour")==true);
Console.WriteLine($"Replay 0.34.5 integration audit: {replay0345Contracts} groups, {replay0345Checks-replay0345Contracts} failures");
Console.WriteLine($"Native API contracts total: {nativeContracts+boosterContracts+uiContracts+replayNativeContracts+sceneContracts+aircraftContracts+vfxContracts+gunContracts+worldAudit.Checks+lifecycleAudit.Checks+seekLifetimeAudit.Checks+propAudioAudit.Checks+navMaterialAudit.Checks+inputReaderAudit.Checks+cloudClockAudit.Checks+startupControlAudit.Checks+closeCameraAudit.Checks+interiorAudit.Checks+previewAudit.Checks+countermeasureAudit.Checks+identityAudit.Checks+lightingFireAudit.Checks+missileImpactAudit.Checks+studioLightUiAudit.Checks+studioLightRuntimeAudit.Checks+neutralCargoSpawnAudit.Checks+pathTimingAudit.Checks+cameraSmoothnessAudit.Checks+gunSpinAudit.Checks+mapBuildingAudit.Checks+editorReplayAudit.Checks+ejectionAudit.Checks+replayRenameAudit.Checks+openingFailureAudit.Checks+openingAirframeAudit.Checks+damageRestoreAudit.Checks+vehicleGunAudioAudit.Checks}");
Console.WriteLine($"Game core SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(corePath)))}");
Console.WriteLine($"Game particles SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(particlePath)))}");
Console.WriteLine($"Game audio SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(audioPath)))}");
Console.WriteLine($"Game SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))}");
return failures == 0 ? 0 : 1;

void Scene(string name, bool ok) { sceneChecks++; sceneContracts += Contract(name, ok); }
void Aircraft(string name, bool ok) { aircraftChecks++; aircraftContracts += Contract(name, ok); }
void Vfx(string name, bool ok) { vfxChecks++; vfxContracts += Contract(name, ok); }
void Presentation(string name, bool ok) { presentationChecks++; presentationContracts += Contract(name, ok); }
void CameraControl(string name, bool ok) { cameraControlChecks++; cameraControlContracts += Contract(name, ok); }
void Take(string name,bool ok){takeChecks++;takeContracts+=Contract(name,ok);}
void Gun(string name,bool ok){gunChecks++;gunContracts+=Contract(name,ok);}
void GunIntegration(string name,bool ok){gunIntegrationChecks++;gunIntegrationContracts+=Contract(name,ok);}
void Replay0345(string name,bool ok){replay0345Checks++;replay0345Contracts+=Contract(name,ok);}
MethodDefinition? ModMethod(string typeName, string methodName)
    => modTypes.TryGetValue(typeName, out var definition) ? definition.Methods.SingleOrDefault(method => method.Name == methodName && method.HasBody) : null;
bool ModExecutionOrder(string typeName, int order)
    => modTypes.TryGetValue(typeName, out var definition) && definition.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "UnityEngine.DefaultExecutionOrder" && attribute.ConstructorArguments.Count == 1 && Convert.ToInt32(attribute.ConstructorArguments[0].Value) == order);
bool CallsModMethod(string typeName, string methodName, string calledType, string calledMethod)
    => ModMethod(typeName, methodName)?.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.DeclaringType.FullName == calledType && method.Name == calledMethod) == true;
bool CallsModOverload(string typeName,string methodName,string[] parameters,string calledType,string calledMethod)
    => modTypes.TryGetValue(typeName,out var type)&&type.Methods.SingleOrDefault(method=>method.Name==methodName&&method.HasBody&&
        method.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters))?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference method&&method.DeclaringType.FullName==calledType&&method.Name==calledMethod)==true;
int Contract(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    if (ok) return 1;
    failures++; return 0;
}
bool HasFields(string typeName, bool isStatic, params (string Name, string Type)[] fields)
{
    if (!types.TryGetValue(typeName, out var type)) { Console.WriteLine("Missing native type " + typeName); return false; }
    bool ok = true;
    foreach (var (name, fieldType) in fields)
    {
        var field = FindField(type, name);
        if (field == null || field.IsStatic != isStatic || field.FieldType.FullName != fieldType)
        { Console.WriteLine($"Native field mismatch {typeName}::{name}: expected {fieldType}, actual {field?.FieldType.FullName ?? "missing"}"); ok = false; }
    }
    return ok;
}
bool HasProperty(string typeName, string name, string propertyType, bool isStatic)
{
    if (!types.TryGetValue(typeName, out var type)) return false;
    var property = FindProperty(type, name);
    return property != null && property.PropertyType.FullName == propertyType && property.GetMethod?.IsStatic == isStatic;
}
bool HasMethod(string typeName, string name, string returnType, bool isStatic, params string[] parameters)
    => types.TryGetValue(typeName, out var type) && FindMethods(type, name).Count(m => m.IsStatic == isStatic && m.ReturnType.FullName == returnType && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)) == 1;
bool EnumValues(string typeName, params (string Name, int Value)[] values)
    => types.TryGetValue(typeName, out var type) && type.IsEnum && values.All(value => type.Fields.Any(field => field.Name == value.Name && field.HasConstant && Convert.ToInt32(field.Constant) == value.Value));
FieldDefinition? FindField(TypeDefinition type, string name)
    => type.Fields.FirstOrDefault(field => field.Name == name) ?? (type.BaseType != null && types.TryGetValue(type.BaseType.FullName, out var parent) ? FindField(parent, name) : null);
PropertyDefinition? FindProperty(TypeDefinition type, string name)
{
    var own = type.Properties.FirstOrDefault(property => property.Name == name);
    if (own != null) return own;
    if (type.BaseType != null && types.TryGetValue(type.BaseType.FullName, out var parent))
    {
        var inherited = FindProperty(parent, name);
        if (inherited != null) return inherited;
    }
    foreach (var implemented in type.Interfaces)
        if (types.TryGetValue(implemented.InterfaceType.FullName, out var definition))
        {
            var inherited = FindProperty(definition, name);
            if (inherited != null) return inherited;
        }
    return null;
}
IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> definitions)
{
    foreach (var definition in definitions)
    {
        yield return definition;
        foreach (var nested in AllTypes(definition.NestedTypes)) yield return nested;
    }
}
void ValidateInjection(TypeDefinition patchType, MethodDefinition patch, MethodDefinition target)
{
    foreach(var parameter in patch.Parameters.Where(p=>p.Name.StartsWith("___",StringComparison.Ordinal)))
    {
        string fieldName=parameter.Name.Substring(3);var owner=target.DeclaringType;FieldDefinition? field=null;
        while(owner!=null)
        {
            field=owner.Fields.FirstOrDefault(f=>f.Name==fieldName);if(field!=null)break;
            owner=owner.BaseType!=null&&types.TryGetValue(owner.BaseType.FullName,out var parent)?parent:null;
        }
        string injected=parameter.ParameterType is ByReferenceType reference?reference.ElementType.FullName:parameter.ParameterType.FullName;
        if(field==null||field.FieldType.FullName!=injected||(target.IsStatic&&!field.IsStatic))
        {Console.WriteLine($"FAIL field injection {patchType.Name}.{patch.Name}: {parameter.Name} {parameter.ParameterType} vs {field?.FullName}");failures++;}
    }
    foreach (var parameter in patch.Parameters.Where(p => !p.Name.StartsWith("__")))
    {
        var original = target.Parameters.FirstOrDefault(p => p.Name == parameter.Name);
        if (original == null || original.ParameterType.FullName != parameter.ParameterType.FullName)
        { Console.WriteLine($"FAIL injection {patchType.Name}.{patch.Name}: {parameter.Name} {parameter.ParameterType} vs {original?.ParameterType}"); failures++; }
    }
    var instance = patch.Parameters.FirstOrDefault(p => p.Name == "__instance");
    if (instance != null && instance.ParameterType.FullName != target.DeclaringType.FullName)
    { Console.WriteLine($"FAIL instance injection {patchType.Name}: {instance.ParameterType} vs {target.DeclaringType}"); failures++; }
}
void AuditDynamicTargets(TypeDefinition patchType, MethodDefinition patch, string targetType, int expected, params string[] prefixes)
{
    var matches = types[targetType].Methods.Where(method => prefixes.Any(prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    if (matches.Length != expected || matches.Any(method => method.IsStatic || method.ReturnType.FullName != "System.Void" || !method.HasBody))
    { Console.WriteLine($"FAIL dynamic {patchType.Name} targets: {matches.Length}, expected {expected}"); failures++; return; }
    foreach (var target in matches) { ValidateInjection(patchType, patch, target); count++; Console.WriteLine("PASS dynamic " + target.FullName); }
}

IEnumerable<MethodDefinition> FindMethods(TypeDefinition type, string name)
{
    var own = type.Methods.Where(m => m.Name == name).ToArray();
    if (own.Length > 0) return own;
    if (type.BaseType != null && types.TryGetValue(type.BaseType.FullName, out var parent)) return FindMethods(parent, name);
    return own;
}
