using System;
using ReplayMod.Director;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private UnityEngine.UI.Button[] cockpitSeatButtons;
    private void RefreshCockpitSeatControls()
    {
        if(cockpitSeatButtons==null)return;
        for(int i=0;i<cockpitSeatButtons.Length;i++)
            if(cockpitSeatButtons[i]!=null)cockpitSeatButtons[i].interactable=Playback&&!Busy&&Director?.TakeActive!=true&&Director.CockpitSeatAvailable(i);
    }
    private void CockpitSeatMenu(MenuPage page)
    {
        int count=Math.Max(1,Director.CockpitSeatCount);
        var names=new string[count];var tips=new string[count];
        for(int i=0;i<count;i++)
        {
            names[i]=ReplayDirector.CockpitSeatName(i);
            tips[i]=i==0?"Use the aircraft's native pilot viewpoint.":"Follow this actual crew seat's modeled head position, calibrated to the aircraft's native eye height. Unavailable or ejected seats cannot be selected.";
        }
        page.Note("Seat: "+ReplayDirector.CockpitSeatName(Director.Project.cockpitSeat),28,false);
        var buttons=cockpitSeatButtons=page.Choices(names,tips,index=>{Director.SelectCockpitSeat(index);Notice(Director.Status);RebuildMenu();},Math.Min(count,3));
        for(int i=0;i<buttons.Length;i++)
        {
            buttons[i].interactable=Director.CockpitSeatAvailable(i);
            SelectButton(buttons[i],Director.Project.cockpitSeat==i);
        }
        if(count<2)page.Note("This aircraft has no additional crew seat.",36);
        else if(!Director.CockpitSeatAvailable(1))page.Note("The copilot seat is unavailable here. Its crew model must expose an attached head anchor.",52);
        if(Director.Project.cockpitSeat!=0)page.Note("First-person ejection currently follows the primary pilot only; the copilot view will not jump into that seat.",58);
    }
}

