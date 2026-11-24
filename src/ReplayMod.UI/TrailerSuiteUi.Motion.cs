using ReplayMod.Core;
using TMPro;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private TextMeshProUGUI motionSubjectText;
    private void MotionMenu(MenuPage page)
    {
        page.Note("Reduce small corrections captured in aircraft movement. The original recording stays intact.",58);
        if(!Playback)
        {
            page.Action("Open library","Choose a recording to adjust its aircraft motion.",()=>OpenMenu("Library"),true);
            return;
        }
        string[] presets={"Off","Light","Normal","Strong"};
        var choices=page.Choices(presets,new[]{
            "Use the exact recorded movement with ordinary interpolation.",
            "Reduce subtle aircraft shake while retaining more of the original motion.",
            "Reduce recorded aircraft shake with a moderate replay-time smoothing window.",
            "Use a wider window for stronger shake reduction. This can soften real manoeuvres."},index=> {Director.SetMotionSmoothing(index);RebuildMenu();},4);
        for(int index=0;index<choices.Length;index++)SelectButton(choices[index],Director.Project.motionSmoothing==index);
        eventControls.AddRange(choices);
        page.Heading("Apply to");
        var scopes=page.Choices(new[]{"Selected aircraft","All aircraft"},new[]{
            "Smooth only the aircraft selected in Camera or on the native map. Other units keep recorded motion.",
            "Smooth eligible aircraft throughout this replay. Buildings, missiles and detached debris keep recorded motion."},index=> {Director.SetMotionSmoothingScope(index);RebuildMenu();},2);
        for(int index=0;index<scopes.Length;index++)SelectButton(scopes[index],Director.Project.motionSmoothingScope==index);
        eventControls.AddRange(scopes);
        motionSubjectText=page.Note("",50,false);RefreshMotionControls();
        if(Director.Project.motionSmoothingScope==0)
            page.Action("Choose aircraft","Open the camera subject picker. Return to Settings → Motion after choosing.",()=> {cameraTab=0;targetType=1;targetPicker=true;pendingCameraRig=-1;currentMenu="Camera";RebuildMenu();});
        page.Note("Start with Light, then compare Normal. Strong may soften real movement. Damage, ejection, gaps and sudden jumps keep their recorded motion.",90);
        page.Note("This changes offline replay presentation. It cannot reconstruct positions the network never delivered. Save project keeps the chosen preset and scope.",78);
    }
    private void RefreshMotionControls()
    {
        if(motionSubjectText==null||!Playback||Director==null)return;
        var project=Director.Project;
        if(project.motionSmoothingScope==1){motionSubjectText.text="Applies to eligible aircraft in this replay.";return;}
        var subject=Manager.Playback.FindActor(project.target);
        motionSubjectText.text=subject is Aircraft?"Selected aircraft: "+ActorName(project.target,subject):"Choose a live aircraft in Camera or on the map.";
    }
}
