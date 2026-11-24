using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private string deleteReplayPath;
    private bool bulkSelecting,bulkConfirm;
    private readonly HashSet<string> bulkReplayPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private void BulkReplayMenu(MenuPage page)
    {
        page.Action(bulkSelecting?"Exit bulk selection":"Select recordings to delete","Choose multiple recordings, then confirm. Files move to Deleted recordings for recovery.",()=>{bulkSelecting=!bulkSelecting;bulkConfirm=false;bulkReplayPaths.Clear();RebuildMenu();});
        if(!bulkSelecting)return;
        page.Note(bulkReplayPaths.Count+" selected. Select recordings below. Files and their camera projects move to Deleted recordings; disk space is not freed.",70);
        if(bulkReplayPaths.Count==0)return;
        page.Action(bulkConfirm?"Confirm removal of "+bulkReplayPaths.Count:"Delete selected recordings", "Move the selected recordings to the recovery folder; press twice to confirm.",()=>{
            if(!bulkConfirm){bulkConfirm=true;RebuildMenu();return;}
            if(!CanRenameReplay){Notice("Close playback and stop recording before removing recordings.");return;}
            if(!RenameStorageIdle||!ReplayMod.Core.ReplayRecordingRecovery.TryBeginRecording()){Notice("Wait for recording or recovery to finish.");return;}
            int removed=0;var errors=new List<string>();
            try{
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Replays");
                foreach(var file in bulkReplayPaths.ToArray())try{ReplayMod.Core.ReplayFileDelete.MoveToTrash(root,file);bulkReplayPaths.Remove(file);removed++;if(file==selectedFile)selectedFile=null;}catch(Exception e){errors.Add(Path.GetFileName(file)+": "+e.Message);}
            }finally{ReplayMod.Core.ReplayRecordingRecovery.EndRecording();}
            bulkConfirm=false;Notice(removed+" moved to Deleted recordings."+(errors.Count>0?" Failed: "+string.Join("; ",errors):""));RebuildMenu();
        });
    }

    private void ReplayDeleteMenu(MenuPage page)
    {
        if (deleteReplayPath != selectedFile) deleteReplayPath = null;
        if (deleteReplayPath == null)
        {
            page.Action("Delete recording", "Remove this recording from the library. A confirmation appears first; its files can be recovered.", () =>
            {
                if (!CanRenameReplay) { Notice("Close playback and stop recording before removing a recording."); return; }
                deleteReplayPath = selectedFile;
                RebuildMenuAtTop();
            });
            return;
        }

        page.Note("Remove this recording and its camera project from the library? Files move to Replays/Deleted recordings for manual recovery; this does not free disk space.", 80);
        page.Pair("Confirm delete", "Move this recording and its camera files to the recovery folder.", ApplyReplayDelete,
            "Cancel", "Keep this recording in the library.", () => { deleteReplayPath = null; RebuildMenuAtTop(); });
    }

    private void ApplyReplayDelete()
    {
        if (!CanRenameReplay || deleteReplayPath != selectedFile)
        { Notice("Close playback and stop recording before removing a recording."); return; }
        string source = deleteReplayPath;
        Run(() =>
        {
            if (!RenameStorageIdle || !ReplayMod.Core.ReplayRecordingRecovery.TryBeginRecording())
                throw new InvalidOperationException("Wait for recording or recovery to finish before removing a recording.");
            try
            {
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replays");
                ReplayMod.Core.ReplayFileDelete.MoveToTrash(root, source);
                selectedFile = null;
                deleteReplayPath = null;
            }
            finally { ReplayMod.Core.ReplayRecordingRecovery.EndRecording(); }
            Notice("Recording removed. Recover its files from Replays/Deleted recordings.");
            RebuildMenuAtTop();
            return Task.CompletedTask;
        }, false, "Removing recording…");
    }
}
