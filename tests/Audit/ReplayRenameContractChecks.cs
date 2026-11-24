using Mono.Cecil;

public static class ReplayRenameContractChecks
{
    public static (int Checks,int Failures) Run(AssemblyDefinition mod)
    {
        int checks=0,failures=0;
        void Check(string name,bool good){checks++;if(!good)failures++;Console.WriteLine((good?"PASS ":"FAIL ")+"replay rename "+name);}
        var ui=mod.MainModule.Types.Single(type=>type.FullName=="ReplayMod.UI.TrailerSuiteUi");
        var methods=All(ui).SelectMany(type=>type.Methods).Where(method=>method.HasBody).ToArray();
        MethodDefinition? Body(string name)=>ui.Methods.FirstOrDefault(method=>method.Name==name&&method.HasBody);
        bool Calls(MethodDefinition? body,string type,string name)=>body?.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference target&&target.DeclaringType.FullName==type&&target.Name==name)==true;
        var storage=Body("get_RenameStorageIdle");
        Check("selected Library recording exposes owned rename form",Calls(Body("LibraryMenu"),ui.FullName,"ReplayRenameMenu")&&
            Calls(Body("ReplayRenameMenu"),"ReplayMod.UI.TrailerSuiteUi/MenuPage","Field")&&
            Body("ReplayRenameMenu")?.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.Name=="selectedFile")==true);
        Check("idle guard blocks loaded playback, recording, pending saves and recovery",Calls(storage,"ReplayMod.Core.ReplayManager","GetState")&&
            Calls(storage,"ReplayMod.Core.ReplayManager","get_IsTransitioning")&&Calls(storage,"ReplayMod.Core.Recorder","get_IsRecording")&&
            Calls(storage,"ReplayMod.Core.ReplayRecordingRecovery","get_IsBusy")&&Calls(storage,"ReplayMod.Core.ReplayRecordingRecovery","get_HasRecordingReservation"));
        Check("rename callback reserves storage and releases it in finally",methods.Any(method=>Calls(method,"ReplayMod.Core.ReplayFileRename","Rename")&&
            Calls(method,"ReplayMod.Core.ReplayRecordingRecovery","TryBeginRecording")&&Calls(method,"ReplayMod.Core.ReplayRecordingRecovery","EndRecording")&&
            method.Body.ExceptionHandlers.Any(handler=>handler.HandlerType==Mono.Cecil.Cil.ExceptionHandlerType.Finally)));
        Check("successful rename selects new path and refreshes Library",methods.Any(method=>Calls(method,"ReplayMod.Core.ReplayFileRename","Rename")&&
            Calls(method,ui.FullName,"RebuildMenuAtTop")&&method.Body.Instructions.Any(instruction=>instruction.Operand is FieldReference field&&field.Name=="selectedFile"))&&
            Calls(Body("ClosePopout"),ui.FullName,"ClearReplayRenameControls")&&Calls(Body("RebuildMenu"),ui.FullName,"ClearReplayRenameControls"));
        var files=mod.MainModule.Types.Single(type=>type.FullName=="ReplayMod.Core.ReplayFileRename");
        var fileMethods=All(files).SelectMany(type=>type.Methods).Where(method=>method.HasBody).ToArray();
        Check("transaction changes file names without content writes, replacements or deletions",fileMethods.Any(method=>Calls(method,"System.IO.File","Move"))&&
            fileMethods.All(method=>method.Body.Instructions.All(instruction=>instruction.Operand is not MethodReference target||
                target.DeclaringType.FullName!="System.IO.File"||!new[]{"WriteAllText","WriteAllBytes","Replace","Delete","Copy","Create"}.Contains(target.Name))));
        Console.WriteLine($"Replay rename API/integration audit: {checks} groups, {failures} failures");return(checks,failures);
    }
    private static IEnumerable<TypeDefinition> All(TypeDefinition type)
    {yield return type;foreach(var nested in type.NestedTypes)foreach(var child in All(nested))yield return child;}
}
