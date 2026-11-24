using System;
using System.Linq;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private void AircraftLightingControls(MenuPage page)
    {
        var project=Director.Project;
        page.Heading("Aircraft lights");
        page.Switch("Apply to all aircraft","ON changes all replay aircraft. OFF changes the aircraft selected in Camera. Custom studio lights are separate.",
            ()=>project.lightsAll,value=>{project.lightsAll=value;RefreshAircraftLightChoices();});
        if(!project.lightsAll)
        {
            var subject=Director.Actors().FirstOrDefault(actor=>actor.Key==project.lightTarget).Value;
            page.Note(subject is Aircraft?"Lighting subject: "+ActorName(project.lightTarget,subject):"Choose an aircraft in Camera → View to change its lights.",44);
        }
        AircraftLightChoices(page,"Navigation lights",()=>project.lightMode,value=>project.lightMode=value);
        AircraftLightChoices(page,"Landing lights",()=>project.landingLightMode,value=>project.landingLightMode=value);
        page.Pair("Aircraft blackout","Turn off navigation and landing lights for the chosen scope, including their emissive lenses. Your custom studio lights stay on.",()=>{
            project.lightMode=project.landingLightMode=2;RefreshAircraftLightChoices();
        },"Restore recorded lights","Return navigation and landing lights to the recording. Older recordings use native lighting as a fallback.",()=>{
            project.lightMode=project.landingLightMode=0;RefreshAircraftLightChoices();
        });
        page.Note("These replay-only settings save with the camera project. Off also works while paused and on the ground. Damaged or detached lights stay unavailable.",72);
    }
    private void AircraftLightChoices(MenuPage page,string title,Func<int> read,Action<int> change)
    {
        page.Heading(title);
        var buttons=page.Choices(new[]{"Recorded","On","Off"},new[]{
            "Use the recorded lamp state, or native state when the recording has no lighting data.",
            "Turn intact lights on, including landing lights with raised gear.",
            "Turn the lamps and their emissive lenses off, including while on the ground."
        },index=>{change(index);RefreshAircraftLightChoices();},3);
        for(int i=0;i<buttons.Length;i++)SelectButton(buttons[i],read()==i);
    }
    private void RefreshAircraftLightChoices()
    {
        if(Playback)Manager.Playback.RefreshPlaybackLights();
        RebuildMenu();
    }
}
