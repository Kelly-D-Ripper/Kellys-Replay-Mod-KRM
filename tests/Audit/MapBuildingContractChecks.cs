using Mono.Cecil;

public static class MapBuildingContractChecks
{
    public static (int Checks,int Failures) Run(IReadOnlyDictionary<string,TypeDefinition> native,AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"map/building "+name);}
        bool Fields(string type,params (string Name,string Type)[] fields)=>native.TryGetValue(type,out var value)&&
            fields.All(expected=>value.Fields.Any(f=>f.Name==expected.Name&&!f.IsStatic&&f.FieldType.FullName==expected.Type));
        bool Method(string type,string name,string result,params string[] args)=>native.TryGetValue(type,out var value)&&
            value.Methods.Count(m=>m.Name==name&&!m.IsStatic&&m.ReturnType.FullName==result&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(args))==1;
        Check("native foundation and collapse-duration contract",Fields("Building",("collapseTime","System.Single"),("hideOnMap","System.Boolean"))&&
            Fields("Unit",("startPosition","GlobalPosition"),("startRotation","UnityEngine.Quaternion"),("disabled","System.Boolean")));
        Check("native initialization and asynchronous collapse targets",Method("Unit","InitializeUnit","System.Void")&&
            Method("Building","UnitDisabled","System.Void","System.Boolean","System.Boolean")&&Method("Building","Collapse","Cysharp.Threading.Tasks.UniTask"));
        Check("native direct icon creation and lookup contract",Method("DynamicMap","SpawnIconForUnit","UnitMapIcon","Unit")&&
            Method("DynamicMap","TryGetIcon","System.Boolean","Unit","UnitMapIcon&")&&Method("DynamicMap","RemoveIcon","System.Void","MapIcon"));
        Check("native map icon dependencies and recorded-position support",Fields("DynamicMap",("iconLayer","UnityEngine.GameObject"),("unitMapIconPrefab","UnitMapIcon"))&&
            Fields("UnitMapIcon",("trackingInfo","TrackingInfo")));
        var compiled=AllTypes(mod.MainModule.Types).ToDictionary(t=>t.FullName);
        MethodDefinition? Body(string type,string name)=>compiled.TryGetValue(type,out var value)?value.Methods.FirstOrDefault(m=>m.Name==name&&m.HasBody):null;
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==type&&m.Name==name)==true;
        bool TypeCalls(string caller,string type,string name)=>compiled.TryGetValue(caller,out var value)&&value.Methods.Any(m=>m.HasBody&&Calls(m,type,name));
        bool NoCalls(string caller,params string[] names)=>compiled.TryGetValue(caller,out var value)&&value.Methods.Where(m=>m.HasBody).All(m=>m.Body.Instructions.All(i=>i.Operand is not MethodReference target||!names.Contains(target.Name)));
        const string controller="ReplayMod.Core.UnitController",map="ReplayMod.Core.ReplayMapPlayback",building="ReplayMod.Core.ReplayBuildingPresentation",recorder="ReplayMod.Core.Recorder";
        var spawnBody=compiled.TryGetValue(controller,out var controllerType)?controllerType.Methods.FirstOrDefault(m=>m.Name=="Execute"&&m.HasBody&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="ReplayMod.Events.ConcreteEvents.SpawnEvent"):null;
        Check("compiled spawn/despawn/reset presentation lifetimes",Calls(spawnBody,controller,"TrackMapBuildingActor")&&
            Calls(Body(controller,"DespawnUnit"),controller,"ForgetMapBuildingActor")&&Calls(Body(controller,"ReleaseOwnedPresentationObjects"),controller,"ClearMapBuildingPlayback"));
        Check("compiled bounded exact-actor map upkeep without world rebuild",TypeCalls(map,controller,"FindActor")&&TypeCalls(map,"DynamicMap","SpawnIconForUnit")&&
            TypeCalls(map,"ReplayMod.Core.ReplayMapPolicy","Visible")&&NoCalls(map,"FindObjectsOfType","FindObjectsByType","GenerateMap","ClearMap","SetFaction"));
        Check("compiled native map icons use recorded positions",compiled.TryGetValue(map,out var mapType)&&mapType.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions)
            .Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.FullName=="UnitMapIcon"&&f.Name=="trackingInfo"));
        Check("compiled building clock drives final world and late camera pose",Calls(Body(controller,"Update"),controller,"RefreshReplayMapBuildings")&&
            Calls(Body(controller,"RefreshRecordedUnitPoses"),controller,"RefreshReplayBuildingPresentation")&&TypeCalls(building,"ReplayMod.Data.BuildingStateSnapshot","DropAt")&&TypeCalls(building,"ReplayMod.Data.BuildingStateSnapshot","HiddenAt"));
        Check("compiled collapse avoids native destroy and asynchronous animation",NoCalls(building,"Collapse","UnitDisabled","Destroy","DestroyImmediate","StartCoroutine")&&
            TypeCalls(building,controller,"FindActor"));
        Check("compiled native building suppression requires replay ownership",TypeCalls("ReplayMod.Patches.BuildingPatch","ReplayMod.Core.ReplayRuntime","get_IsPlayback")&&
            TypeCalls("ReplayMod.Patches.BuildingPatch","ReplayMod.Core.ReplayVfxPlayback","IsOwnedActor"));
        Check("compiled building recording includes hidden births and state capture",TypeCalls("ReplayMod.Patches.ReplayBuildingInitializedPatch",recorder,"ObserveInitializedBuilding")&&
            Calls(Body(recorder,"ObserveInitializedBuilding"),recorder,"HandleUnitRegister")&&Calls(Body(recorder,"RecordWorldActor"),recorder,"RecordBuildingActor")&&
            Calls(Body(recorder,"RecordWorldState"),recorder,"RecordBuildingStates")&&Calls(Body(recorder,"ClearRecordingWorld"),recorder,"ClearRecordingBuildings"));
        Check("compiled cached building capture avoids repeated discovery",Body(recorder,"RecordBuildingState")?.Body.Instructions.All(i=>i.Operand is not MethodReference target||
            target.Name is not ("FindObjectsOfType" or "FindObjectsByType" or "GetComponent" or "GetComponentsInChildren"))==true&&
            TypeCalls("ReplayMod.Patches.BuildingPatch",recorder,"ObserveDisabledBuilding"));
        var track=Body(map,"Track");
        var tick=compiled.TryGetValue(map,out var mapDefinition)?mapDefinition.Methods.SingleOrDefault(m=>m.Name=="Refresh"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.Double"&&m.HasBody):null;
        bool FinallyCalls(MethodDefinition? method,string name)=>method?.Body.ExceptionHandlers.Where(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Finally).Any(h=>
            method.Body.Instructions.Where(i=>i.Offset>=h.HandlerStart.Offset&&(h.HandlerEnd==null||i.Offset<h.HandlerEnd.Offset)).Any(i=>i.Operand is MethodReference target&&target.Name==name))==true;
        bool Catches(MethodDefinition? method,int count)=>method!=null&&method.Body.ExceptionHandlers.Count(h=>h.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Catch&&h.CatchType.FullName=="System.Exception")>=count;
        Check("compiled spawn queues map work and failed icons requeue in finally",track!=null&&!Calls(track,map,"Refresh")&&!Calls(track,map,"Ready")&&!Calls(track,map,"Forget")&&
            track.Body.Instructions.All(i=>i.Operand is not MethodReference target||target.DeclaringType.FullName!="DynamicMap")&&
            Catches(tick,2)&&FinallyCalls(tick,"Enqueue")&&Calls(tick,"System.Math","Pow")&&Calls(tick,map,"Warn"));
        Check("compiled native UI cleanup cannot block actor/building teardown",Catches(Body(map,"RemoveFromMaps"),3)&&FinallyCalls(Body(map,"Forget"),"Remove")&&
            FinallyCalls(Body(map,"Clear"),"Clear")&&FinallyCalls(Body(map,"RemoveIcon"),"RemoveIcon")&&
            Catches(Body(controller,"TrackMapBuildingActor"),2)&&Catches(Body(controller,"ForgetMapBuildingActor"),2)&&Catches(Body(controller,"ClearMapBuildingPlayback"),2)&&
            FinallyCalls(Body(controller,"ClearMapBuildingPlayback"),"Clear"));
        Console.WriteLine($"Map/building API/integration audit: {checks} groups, {failures} failures");
        return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {foreach(var type in types){yield return type;foreach(var child in AllTypes(type.NestedTypes))yield return child;}}
}
