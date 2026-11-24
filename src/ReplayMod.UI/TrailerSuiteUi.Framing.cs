using System.Linq;
using ReplayMod.Core;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private bool pairSubjectPicker;
    private void PairSubjectPickerMenu(MenuPage page)
    {
        page.Action("Back to camera","Keep the current second subject.",()=>{pairSubjectPicker=false;RebuildMenu();});
        page.Heading("Second aircraft");
        foreach(var pair in Director.Actors().Where(p=>p.Value is Aircraft&&p.Key!=Director.Project.target).Take(100))
        {
            uint id=pair.Key;
            page.Action(ActorName(id,pair.Value),"Keep this aircraft and the main subject in frame together.",()=>{
                if(Director.SelectSecondaryCameraTarget(id)){pairSubjectPicker=false;Director.SelectCameraRig(6);}
                Notice(Director.Status);RebuildMenu();
            });
        }
        page.Note("Both aircraft must exist at the current replay time. Choose the main subject in Camera → View.",62);
    }
    private void FramingControls(MenuPage page)
    {
        var p=Director.Project;
        if(p.rig==6)
        {
            var second=Director.Actors().FirstOrDefault(a=>a.Key==p.secondTarget).Value;
            page.Note(second is Aircraft?"Second aircraft: "+ActorName(p.secondTarget,second):"Choose the second aircraft below.",36);
            page.Action("Choose second aircraft","Select the other aircraft. The camera tracks the midpoint and adjusts distance to fit both at your chosen FOV.",()=>{pairSubjectPicker=true;RebuildMenu();});
            page.Note("Framing moves the camera back as the aircraft separate. Terrain can still obscure a subject. Pilot target selections are not stored in existing replays.",76);
        }
        else if(p.rig==7)
        {
            page.Switch("Rotate with aircraft","OFF holds a steady world angle. ON carries the viewing angle around with the aircraft's heading and bank.",()=>p.followWithAircraft,value=>{p.followWithAircraft=value;Director.ResetCameraSmoothing();});
            page.Action("Use current viewing angle","Lock the camera's current angle and distance around the selected aircraft. To compose a new angle, switch to Free and use Lock current angle below.",()=>{Director.CaptureFollowAngle();Notice(Director.Status);RebuildMenu();});
        }
        else return;
        page.Slide("Viewing angle","Move the camera around the aircraft or pair without entering coordinates.",p.followAzimuth,-180,180,v=>{p.followAzimuth=v;Director.ResetCameraSmoothing();},"0°");
        page.Slide("Elevation","Look from above or below the aircraft.",p.followElevation,-85,85,v=>{p.followElevation=v;Director.ResetCameraSmoothing();},"0°");
        page.Slide(p.rig==6?"Minimum distance":"Follow distance","Distance in metres. Two-Aircraft Framing increases this when necessary to keep both subjects in frame.",p.distance,1,1000,v=>{p.distance=v;Director.ResetCameraSmoothing();},"0 m");
    }
}
