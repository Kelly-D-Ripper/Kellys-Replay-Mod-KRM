using System;
using System.IO;
using System.Linq;
using ReplayMod.Core;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private bool showRecovery;
    private string selectedRecovery;
    private bool CanRecoverRecording=>Manager!=null&&!Busy&&Manager.GetState()==ModStates.Idle&&
        Recorder.Active?.IsRecording!=true&&RollingRecordingController.Instance?.Busy!=true&&
        !ReplayRecordingRecovery.IsBusy&&!ReplayRecordingRecovery.HasRecordingReservation;

    private void RecoveryMenu(MenuPage page)
    {
        if(libraryTab!=1)page.Action(showRecovery?"Hide recording recovery":"Recover interrupted recording",
            "Create a separate playable copy from the valid beginning of an interrupted recording. The original stays untouched.",
            ()=> {showRecovery=!showRecovery;RebuildMenu();});
        if(!showRecovery&&libraryTab!=1)return;
        page.Note("Recovery keeps complete valid events before the damaged or unfinished tail. It saves a separate copy beside the original.",70,false);
        if(Playback)page.Action("Close replay for recovery","Save camera edits and close playback before recovering an interrupted recording.",()=>Run(async()=> {await Manager.SwitchState(ModStates.Idle);RebuildMenu();}));
        if(!CanRecoverRecording)page.Note("Close the current replay and stop recording before recovery. Wait for any recording operation to finish.",76,false);
        string[] candidates;
        try
        {
            candidates=ReplayRecordingRecovery.FindCandidates(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays"))
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(50).ToArray();
        }
        catch(Exception error){page.Note("Could not read interrupted recordings: "+error.Message,80);return;}
        if(candidates.Length==0){page.Note("No interrupted recordings found. Recovery is available for new recordings made with this version.",76);return;}
        if(selectedRecovery!=null&&candidates.Contains(selectedRecovery))
        {
            page.Note("Selected: "+Path.GetFileName(selectedRecovery),46,false);
            recoveryControls.Add(page.Action("Recover a copy","Read this interrupted recording and save its complete valid prefix to a new replay. Never overwrites the original.",RecoverSelectedRecording,true));
        }
        foreach(string candidate in candidates)
        {
            string path=candidate;
            recoveryControls.Add(page.Action((path==selectedRecovery?"> ":"")+Path.GetFileName(path),
                "Select this interrupted recording. Recover a copy is a separate button.",()=> {selectedRecovery=path;RebuildMenuAtTop();}));
        }
        if(candidates.Length==50)page.Note("Showing up to 50 interrupted files. All originals remain in the Replays folder.",58);
    }
    private void RecoverSelectedRecording()
    {
        if(!CanRecoverRecording||string.IsNullOrEmpty(selectedRecovery))
        {Notice("Stop recording and close the replay before recovering a copy.");return;}
        string source=selectedRecovery;
        Run(async()=>
        {
            if(Manager.GetState()!=ModStates.Idle||Recorder.Active?.IsRecording==true)
                throw new InvalidOperationException("Recording started. Stop it before recovering a copy.");
            var result=await ReplayRecordingRecovery.RecoverAsync(source);
            selectedFile=result.Path;selectedRecovery=null;showRecovery=false;libraryTab=0;
            Notice("Recovered "+result.EventCount+" events to "+Path.GetFileName(result.Path)+". Original kept."+
                (result.DiscardedBytes>0?" Incomplete or damaged tail omitted.":""));
            RebuildMenuAtTop();
        },false,"Recovering a copy…");
    }
}
