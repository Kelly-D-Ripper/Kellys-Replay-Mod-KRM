using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ReplayMod.Core;
using UnityEngine.UI;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private string renameReplayPath,renameReplayText="";
    private readonly List<Selectable> renameReplayControls=new List<Selectable>();
    private bool RenameStorageIdle=>Manager!=null&&!Manager.IsTransitioning&&Manager.GetState()==ModStates.Idle&&
        Recorder.Active?.IsRecording!=true&&RollingRecordingController.Instance?.Busy!=true&&
        !ReplayRecordingRecovery.IsBusy&&!ReplayRecordingRecovery.HasRecordingReservation;
    private bool CanRenameReplay=>!Busy&&RenameStorageIdle;

    private void ReplayRenameMenu(MenuPage page)
    {
        if(renameReplayPath!=selectedFile)renameReplayPath=null;
        if(renameReplayPath==null)
        {
            renameReplayControls.Add(page.Action("Rename recording","Change this recording's name and keep its camera project and backups. Close playback and stop recording before renaming.",()=>
            {
                if(!CanRenameReplay){Notice("Close the replay and stop recording before renaming. Wait for saving or recovery to finish.");return;}
                renameReplayPath=selectedFile;renameReplayText=Path.GetFileNameWithoutExtension(selectedFile);RebuildMenuAtTop();
            }));
        }
        else
        {
            page.Heading("Rename recording");
            var field=page.Field(renameReplayText,"Choose a name for this recording. The .replay extension is kept automatically; camera project and backups move with it. Nothing is overwritten.",value=>renameReplayText=value,"Recording name");
            field.characterLimit=ReplayFileRename.MaximumNameLength+7;renameReplayControls.Add(field);
            var actions=page.Pair("Rename","Apply this name without changing any replay or camera-project data.",ApplyReplayRename,
                "Cancel","Keep the current recording name.",()=>{renameReplayPath=null;RebuildMenuAtTop();});
            renameReplayControls.Add(actions[0]);
        }
        if(!RenameStorageIdle)page.Note("Close playback and stop recording before renaming. Wait for saving or recovery to finish.",64);
        RefreshReplayRenameControls();
    }

    private void ApplyReplayRename()
    {
        if(!CanRenameReplay||renameReplayPath!=selectedFile)
        {Notice("Close the replay and stop recording before renaming. Wait for saving or recovery to finish.");return;}
        string source=renameReplayPath,entered=renameReplayText;
        Run(()=>
        {
            if(!RenameStorageIdle||!ReplayRecordingRecovery.TryBeginRecording())
                throw new InvalidOperationException("Wait for recording or recovery to finish before renaming.");
            ReplayRenameResult result;
            try
            {
                string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays");
                result=ReplayFileRename.Rename(directory,source,entered);
                selectedFile=result.Path;renameReplayPath=null;
            }
            finally{ReplayRecordingRecovery.EndRecording();}
            Notice(result.Renamed?"Renamed to "+Path.GetFileNameWithoutExtension(result.Path)+". Camera project and backups kept.":"Recording name unchanged.");
            RebuildMenuAtTop();
            return Task.CompletedTask;
        },false,"Renaming recording…");
    }

    private void RefreshReplayRenameControls()
    {foreach(var control in renameReplayControls)if(control!=null)control.interactable=CanRenameReplay;}
    private void ClearReplayRenameControls()=>renameReplayControls.Clear();
}
