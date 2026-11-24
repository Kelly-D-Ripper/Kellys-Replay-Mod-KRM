using System;
using System.Collections.Generic;
using TMPro;
using ReplayMod.Director;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private int lightTab;
    private bool lightRgbOptions,lightShadowOptions;
    private float lightMoveStep=1,lightTurnStep=15,lightHue,lightSaturation,lightValue=1;
    private readonly List<Selectable> studioLightControls=new List<Selectable>();
    private readonly List<Selectable> studioLightSpotControls=new List<Selectable>();
    private readonly List<Selectable> studioLightCookieControls=new List<Selectable>();
    private readonly List<Selectable> studioLightCookieChoices=new List<Selectable>();
    private readonly List<Selectable> studioLightShadowControls=new List<Selectable>();
    private readonly List<Selectable> studioLightSoftShadowControls=new List<Selectable>();
    private readonly List<StudioLightSourceLabel> studioLightSourceLabels=new List<StudioLightSourceLabel>();
    private sealed class StudioLightSourceLabel
    {
        internal StudioLightSettings Light;
        internal Button Button;
        internal TMP_Text Text;
        internal int Index;
        internal string LastName;
        internal bool LastEnabled,LastSelected,Rendered;
    }
    private StudioLightSettings shownStudioLight;
    private DirectorProject shownLightProject;
    private int shownStudioLightCount=-1;
    private TextMeshProUGUI lightStatus,lightCapabilityText,lightColourText,lightBeamText;
    private TMP_InputField lightHex;
    private Image lightColourSwatch;
    private Slider[] lightHsvSliders,lightRgbSliders;
    private TextMeshProUGUI[] lightHsvLabels,lightRgbLabels;

    private void ClearStudioLightControls()
    {
        studioLightControls.Clear();studioLightSpotControls.Clear();studioLightCookieControls.Clear();studioLightCookieChoices.Clear();studioLightShadowControls.Clear();studioLightSoftShadowControls.Clear();lightStatus=lightCapabilityText=lightColourText=lightBeamText=null;
        studioLightSourceLabels.Clear();
        lightHex=null;lightColourSwatch=null;lightHsvSliders=lightRgbSliders=null;lightHsvLabels=lightRgbLabels=null;
    }
    private bool LightsEditable=>Playback&&!Busy&&!loadingReplay&&Director?.TakeActive!=true;
    private void RefreshStudioLightControls()
    {
        if(currentMenu!="Lights"||popout==null||Director==null)return;
        var selected=Director.SelectedStudioLight;
        if(!ReferenceEquals(shownLightProject,Director.Project)||!ReferenceEquals(shownStudioLight,selected)||shownStudioLightCount!=Director.StudioLightCount)
        {RebuildMenu();return;}
        bool editable=LightsEditable;
        foreach(var control in studioLightControls)if(control!=null)control.interactable=editable;
        foreach(var control in studioLightSpotControls)if(control!=null)control.interactable=editable&&selected?.type==1;
        foreach(var control in studioLightCookieControls)if(control!=null)control.interactable=editable&&selected?.type==1&&selected.beamProfile!=0&&Director.StudioLightCookiesAvailable;
        foreach(var control in studioLightCookieChoices)if(control!=null)control.interactable=editable&&selected?.type==1&&Director.StudioLightCookiesAvailable;
        foreach(var control in studioLightShadowControls)if(control!=null)control.interactable=editable&&Director.StudioLightShadowsAvailable;
        foreach(var control in studioLightSoftShadowControls)if(control!=null)control.interactable=editable&&Director.StudioLightSoftShadowsAvailable;
        if(lightStatus!=null)
            lightStatus.text=selected==null?"Choose Add point or Add spot to start.":
                (Director.StudioLightDragging?"Moving light · release to place":(selected.enabled?"ON":"OFF")+" · "+(selected.type==0?"Point":"Spot")+" light")+"\n"+
                (!Director.StudioLightMarkers?"Boxes are hidden. Place → Show light boxes restores them.":Director.StudioLightCanDrag?"Drag a box; scroll while dragging to change depth.":"Box dragging needs Free camera. Place → Free camera to drag.");
        RefreshStudioLightSourceLabels();
        if(lightCapabilityText!=null)lightCapabilityText.text=Director.StudioLightCapabilities;
    }
    private void RefreshStudioLightSourceLabels()
    {
        foreach(var row in studioLightSourceLabels)
        {
            bool selected=row.Index==Director.SelectedStudioLightIndex;
            if(row.Rendered&&row.LastName==row.Light.name&&row.LastEnabled==row.Light.enabled&&row.LastSelected==selected)continue;
            row.Rendered=true;row.LastName=row.Light.name;row.LastEnabled=row.Light.enabled;row.LastSelected=selected;
            row.Text.text=(selected?"> ":"")+(row.Index+1)+" · "+row.Light.name+(row.Light.enabled?"":" · OFF");
            row.Button.GetComponent<SuiteTooltip>().Description=StudioLightSelectionTip(row.Light);
            SelectButton(row.Button,selected);
        }
    }
    private void LightsMenu()
    {
        var page=Page("Virtual lights","Light a replay shot. Lights save beside this replay; Hide All UI hides their boxes.",Playback);
        var selected=Director.SelectedStudioLight;
        bool selectionChanged=!ReferenceEquals(shownStudioLight,selected);
        shownLightProject=Director.Project;shownStudioLight=selected;shownStudioLightCount=Director.StudioLightCount;
        if(!Playback)
        {
            page.Note("Open a replay in Library to place custom lights. These tools are unavailable during live gameplay.",72);
            page.Action("Open library","Choose a recording to edit.",()=>OpenMenu("Library"));return;
        }
        if(selectionChanged&&selected!=null)ReadStudioLightHsv(selected);
        var tabs=page.Tabs(new[]{"Sources","Shape","Colour","Place","Advanced","Aircraft"},lightTab,index=> {lightTab=index;RebuildMenuAtTop();});
        foreach(var tab in tabs)
        {var label=tab.GetComponentInChildren<TMP_Text>();label.enableAutoSizing=true;label.fontSizeMin=13;label.fontSizeMax=18;}
        if(lightTab==5)AircraftLightingControls(page);
        else if(lightTab==0)StudioLightSources(page,selected);
        else if(selected==null)
        {
            page.Note("Choose a light in Sources, or use Free camera to click its box in the world.",52);
            page.Action("Choose a light","Open the light list and add or select a light.",()=> {lightTab=0;RebuildMenuAtTop();});
        }
        else
        {
            var caption=page.Note(selected.name+" · "+(selected.type==0?"Point":"Spot"),30,false);caption.richText=false;caption.overflowMode=TextOverflowModes.Ellipsis;caption.enableWordWrapping=false;
            switch(lightTab){case 1:StudioLightShape(page,selected);break;case 2:StudioLightColour(page,selected);break;case 3:StudioLightPlacement(page,selected);break;default:StudioLightAdvanced(page,selected);break;}
        }
        RefreshStudioLightControls();
    }
    private void StudioLightSources(MenuPage page,StudioLightSettings selected)
    {
        lightCapabilityText=page.Note(Director.StudioLightCapabilities,76);
        var add=page.Pair("Add point","Add an all-direction light just in front of the camera. Up to 16 lights per replay project.",()=>AddStudioLight(0),
            "Add spot","Add a directional beam just in front of the camera, pointing along the camera view.",()=>AddStudioLight(1));
        if(Director.StudioLightCount>=StudioLightSettings.MaximumLights)
        {add[0].interactable=add[1].interactable=false;page.Note("16-light limit reached. Delete a light before adding another.",42);}
        else studioLightControls.AddRange(add);
        lightStatus=page.Note("",76,false);lightStatus.richText=false;
        if(selected!=null)
        {
            var name=page.Field(selected.name,"Give this light a short name, such as Hangar fill or Cockpit rim. No coordinates are needed.",value=>selected.name=CleanStudioLightName(value));
            name.characterLimit=80;name.textComponent.richText=false;studioLightControls.Add(name);
            studioLightControls.Add(page.Switch("Light enabled","Switch this light on or off without removing its saved settings.",()=>selected.enabled,value=>selected.enabled=value));
            var manage=page.Pair("Duplicate","Copy this light and select the copy. Move its box or use Place to separate the lights.",()=> {Director.DuplicateStudioLight();Notice(Director.Status);RebuildMenu();},
                "Delete light","Remove the selected light and its box from this project. Save project to keep that change.",()=> {Director.DeleteStudioLight();Notice(Director.Status);RebuildMenu();});
            if(Director.StudioLightCount<StudioLightSettings.MaximumLights)studioLightControls.Add(manage[0]);else manage[0].interactable=false;
            studioLightControls.Add(manage[1]);
            studioLightControls.AddRange(page.Pair("Shape & brightness","Change reach, brightness, beam width and beam softness.",()=> {lightTab=1;RebuildMenuAtTop();},
                "Move & aim","Use camera-relative movement buttons or put this light at the camera.",()=> {lightTab=3;RebuildMenuAtTop();}));
        }
        page.Heading("Saved lights · "+Director.StudioLightCount+" / "+StudioLightSettings.MaximumLights);
        for(int i=0;i<Director.Project.studioLights.Count;i++)
        {
            int index=i;var light=Director.Project.studioLights[i];
            var button=page.Action((i==Director.SelectedStudioLightIndex?"> ":"")+(i+1)+" · "+light.name+(light.enabled?"":" · OFF"),
                StudioLightSelectionTip(light),()=> {Director.SelectStudioLight(index);RebuildMenuAtTop();});
            var label=button.GetComponentInChildren<TMP_Text>();label.richText=false;label.overflowMode=TextOverflowModes.Ellipsis;studioLightControls.Add(button);SelectButton(button,i==Director.SelectedStudioLightIndex);
            studioLightSourceLabels.Add(new StudioLightSourceLabel{Light=light,Button=button,Text=label,Index=index});
        }
    }
    private void AddStudioLight(int type)
    {
        if(!LightsEditable)return;
        Director.AddStudioLight(type);Notice(Director.Status);RebuildMenuAtTop();
    }
    private void StudioLightShape(MenuPage page,StudioLightSettings light)
    {
        var types=page.Choices(new[]{"Point","Spot"},new[]{"Shines in every direction. Useful for fill lighting or a glow.","Shines along its arrow. Use Place to aim the beam and adjust its cone below."},index=> {light.type=index;RebuildMenu();},2);
        studioLightControls.AddRange(types);for(int i=0;i<types.Length;i++)SelectButton(types[i],i==light.type);
        LightLogSlide(page,"Brightness","Native Unity light intensity, from 0 to 1000. This is a relative game-light value, not lumens. A logarithmic slider gives fine control near zero.",light.intensity,0,1000,value=>light.intensity=value);
        LightLogSlide(page,"Reach / depth (m)","Maximum light reach in metres. Point lights cover a sphere; spot lights cover a cone. Native smooth distance falloff fades the light toward this limit.",light.range,.1f,5000,value=> {light.range=value;if(lightBeamText!=null)lightBeamText.text=StudioLightBeamDescription(light);});
        if(light.type==1)
        {
            studioLightControls.Add(page.Slide("Beam width (degrees)","Full outer cone angle. Wider beams light a larger area. This changes beam shape, not a physical rectangular area source.",light.spotOuter,1,179,value=>
            {float innerRatio=light.spotInner/light.spotOuter;light.spotOuter=value;light.spotInner=value*innerRatio;if(lightBeamText!=null)lightBeamText.text=StudioLightBeamDescription(light);},"0.0"));
            studioLightControls.Add(page.Slide("Beam edge softness (%)","0% keeps the inner and outer cones together for a hard edge; 100% shrinks the bright inner cone to a point for a broad fade. Distance falloff remains the game's native falloff.",100*(1-light.spotInner/light.spotOuter),0,100,value=>light.spotInner=light.spotOuter*(1-value/100),"0"));
            lightBeamText=page.Note(StudioLightBeamDescription(light),44);
            studioLightControls.Add(page.Action("Aim along camera","Keep the light's position and rotate its beam to match the current camera direction.",()=>Director.AimStudioLightFromView()));
            page.Heading("Beam shape");
            var profiles=page.Choices(new[]{"Cone","Ellipse","Rectangle","Window","Slats"},new[]{"Use the native circular spot cone with no pattern.","Use an elliptical light mask with independent width and height.","Use a rectangular light mask with adjustable edges.","Use a window pattern made for this mod.","Use a slatted light pattern made for this mod."},index=> {light.beamProfile=index;RebuildMenu();},3);
            for(int i=0;i<profiles.Length;i++)
            {SelectButton(profiles[i],light.beamProfile==i);if(i==0)studioLightControls.Add(profiles[i]);else studioLightCookieChoices.Add(profiles[i]);}
            if(!Director.StudioLightCookiesAvailable)page.Note("The active renderer does not support spot-light patterns; the native cone is used.",58);
            if(light.beamProfile!=0)
            {
                studioLightCookieControls.Add(page.Slide("Pattern width (%)","Width of the pattern inside the spot cone. This is a projected light mask, not a physical emitting rectangle.",light.beamWidth*100,10,100,value=>light.beamWidth=value/100,"0"));
                studioLightCookieControls.Add(page.Slide("Pattern height (%)","Height of the pattern inside the spot cone. The outer cone still clips its corners.",light.beamHeight*100,10,100,value=>light.beamHeight=value/100,"0"));
                studioLightCookieControls.Add(page.Slide("Pattern edge softness (%)","Fade the edge of this light pattern. Beam cone softness and native distance falloff remain separate.",light.beamSoftness*100,0,100,value=>light.beamSoftness=value/100,"0"));
                page.Note("Widen the cone or narrow the pattern if the cone clips a rectangular corner.",42);
            }
        }
        else page.Note("Point lights shine in all directions. Choose Spot for beam width, softness and aiming.",58);
        page.Note("Distance falloff uses the native renderer. Wider reach can light more objects and cost more performance.",58);
    }
    private static string StudioLightBeamDescription(StudioLightSettings light)=>"Approximate cone width at full reach: "+StudioLightControlMath.BeamWidth(light.range,light.spotOuter).ToString("0.0")+" m. Brightness fades before that edge.";
    private Slider LightLogSlide(MenuPage page,string label,string tip,float value,float minimum,float maximum,Action<float> change)
    {
        TextMeshProUGUI display=null;
        var slider=page.Slide(label,tip,StudioLightControlMath.ToSlider(value,minimum,maximum),0,1,position=>
        {float actual=StudioLightControlMath.FromSlider(position,minimum,maximum);if(display!=null)display.text=label+": "+actual.ToString("0.###");change(actual);},"0.###",text=>display=text);
        display.text=label+": "+value.ToString("0.###");studioLightControls.Add(slider);return slider;
    }
    private void StudioLightColour(MenuPage page,StudioLightSettings light)
    {
        var preview=page.Note("Base light colour",40,false);lightColourText=preview;
        var swatch=Rect("Light colour preview",preview.transform);Box(swatch,page.Width-58,4,50,32);lightColourSwatch=Fill(swatch,new Color(light.r,light.g,light.b),false);
        lightHsvSliders=new Slider[3];lightHsvLabels=new TextMeshProUGUI[3];
        string[] names={"Hue (degrees)","Saturation (%)","Colour value (%)"};
        string[] tips={"Choose the hue: red near 0°, green near 120°, blue near 240°. Increase saturation to see the selected hue.","0% is white or grey; 100% is the strongest chosen hue. Brightness is a separate control in Shape.","0% makes the base colour black; 100% gives the brightest version of this colour. Use Shape brightness for light output."};
        float[] values={lightHue*360,lightSaturation*100,lightValue*100};
        for(int i=0;i<3;i++)
        {
            int channel=i;
            lightHsvSliders[i]=page.Slide(names[i],tips[i],values[i],0,i==0?360:100,value=>
            {
                if(channel==0)lightHue=value/360;else if(channel==1)lightSaturation=value/100;else lightValue=value/100;
                var colour=Color.HSVToRGB(lightHue,lightSaturation,lightValue);light.r=colour.r;light.g=colour.g;light.b=colour.b;RefreshStudioLightColour(light);
            },"0.0",text=>lightHsvLabels[channel]=text);
            studioLightControls.Add(lightHsvSliders[i]);
        }
        studioLightControls.Add(page.Switch("Colour temperature","Apply the native warm/cool colour-temperature multiplier to this base RGB colour. Turn off for pure RGB lighting.",()=>light.useTemperature,value=> {light.useTemperature=value;RebuildMenu();}));
        if(light.useTemperature)studioLightControls.Add(page.Slide("Temperature (K)","1000 K is very warm, around 6500 K is daylight white, and higher values are cooler. Native rendering multiplies this with the base colour.",light.temperature,1000,20000,value=>light.temperature=value,"0"));
        page.Action(lightRgbOptions?"Hide RGB & hex":"RGB & hex values","Use exact RGB channels or a six-digit web-style colour.",()=> {lightRgbOptions=!lightRgbOptions;RebuildMenu();});
        if(lightRgbOptions)
        {
            lightRgbSliders=new Slider[3];lightRgbLabels=new TextMeshProUGUI[3];string[] labels={"Red","Green","Blue"};float[] rgb={light.r,light.g,light.b};
            for(int i=0;i<3;i++)
            {
                int channel=i;
                lightRgbSliders[i]=page.Slide(labels[i],"Set the "+labels[i].ToLowerInvariant()+" channel from 0 to 255.",Mathf.Round(rgb[i]*255),0,255,value=>
                {if(channel==0)light.r=value/255;else if(channel==1)light.g=value/255;else light.b=value/255;ReadStudioLightHsv(light);RefreshStudioLightColour(light);},"0",text=>lightRgbLabels[channel]=text);
                lightRgbSliders[i].wholeNumbers=true;studioLightControls.Add(lightRgbSliders[i]);
            }
            lightHex=page.Field(StudioLightHex(light),"Enter #RRGGBB or RRGGBB, then choose Apply hex. Invalid values leave the light unchanged.",value=>{},"#RRGGBB");lightHex.characterLimit=7;studioLightControls.Add(lightHex);
            studioLightControls.Add(page.Action("Apply hex","Apply the entered six-digit RGB colour to this light.",()=>
            {
                if(!SuiteRgb.TryParse(lightHex.text,out var parsed)){Notice("Enter six hexadecimal digits, for example #58E187.");return;}
                light.r=parsed.R/255f;light.g=parsed.G/255f;light.b=parsed.B/255f;ReadStudioLightHsv(light);RefreshStudioLightColour(light);
            }));
        }
        RefreshStudioLightColour(light);
    }
    private static string StudioLightHex(StudioLightSettings light)=>"#"+ColorUtility.ToHtmlStringRGB(new Color(light.r,light.g,light.b));
    private void ReadStudioLightHsv(StudioLightSettings light)=>Color.RGBToHSV(new Color(light.r,light.g,light.b),out lightHue,out lightSaturation,out lightValue);
    private void RefreshStudioLightColour(StudioLightSettings light)
    {
        float[] hsv={lightHue*360,lightSaturation*100,lightValue*100};string[] labels={"Hue (degrees)","Saturation (%)","Colour value (%)"};
        for(int i=0;i<3;i++)
        {if(lightHsvSliders?[i]!=null)lightHsvSliders[i].SetValueWithoutNotify(hsv[i]);if(lightHsvLabels?[i]!=null)lightHsvLabels[i].text=labels[i]+": "+hsv[i].ToString("0.0");}
        float[] rgb={light.r*255,light.g*255,light.b*255};string[] names={"Red","Green","Blue"};
        for(int i=0;i<3;i++)
        {if(lightRgbSliders?[i]!=null)lightRgbSliders[i].SetValueWithoutNotify(Mathf.Round(rgb[i]));if(lightRgbLabels?[i]!=null)lightRgbLabels[i].text=names[i]+": "+Mathf.Round(rgb[i]).ToString("0");}
        if(lightColourText!=null)lightColourText.text="Base colour · "+StudioLightHex(light);
        if(lightColourSwatch!=null)lightColourSwatch.color=new Color(light.r,light.g,light.b);
        if(lightHex!=null&&!lightHex.isFocused)lightHex.SetTextWithoutNotify(StudioLightHex(light));
    }
    private void StudioLightPlacement(MenuPage page,StudioLightSettings light)
    {
        studioLightControls.Add(page.Switch("Show light boxes","Show clickable light boxes only while Lights is open. Hide All UI hides all boxes while leaving lighting on.",()=>Director.StudioLightMarkers,value=>Director.SetStudioLightMarkers(value)));
        studioLightControls.Add(page.Action("Free camera to drag","Switch to Free camera while preserving this view, and release camera paths/rails/tracking. Dragging boxes requires Free camera so native camera controls do not compete with the mouse wheel.",()=> {Director.EnterFreeCamera();Notice("Free camera selected. Drag a box and scroll while holding it to change depth.");}));
        studioLightControls.Add(page.Action("Pause for placement","Pause replay time while composing the light. Light editing also works while playback is running.",()=>Director.PausePlayback()));
        var placement=page.Pair("Place at camera","Move the selected light just in front of the current camera. A spot also points along the camera view.",()=>Director.PlaceStudioLightAtView(),
            "Aim along camera","Keep position and match the current camera direction. Point lights shine in all directions.",()=>Director.AimStudioLightFromView());
        studioLightControls.Add(placement[0]);studioLightSpotControls.Add(placement[1]);
        page.Note("Box dragging requires Free camera. Left-drag across the view and scroll while holding the box to change depth. The movement buttons work from other camera modes too.",90);
        LightLogSlide(page,"Move step (m)","Distance per movement click, in metres. Right, up and forward follow the current camera view, so you do not need coordinates.",lightMoveStep,.1f,1000,value=>lightMoveStep=value);
        studioLightControls.AddRange(page.Pair("← Left","Move left across the current camera view.",()=>Director.NudgeStudioLight(0,-lightMoveStep,true),"Right →","Move right across the current camera view.",()=>Director.NudgeStudioLight(0,lightMoveStep,true)));
        studioLightControls.AddRange(page.Pair("↑ Up","Move up in the current camera view.",()=>Director.NudgeStudioLight(1,lightMoveStep,true),"Down ↓","Move down in the current camera view.",()=>Director.NudgeStudioLight(1,-lightMoveStep,true)));
        studioLightControls.AddRange(page.Pair("Nearer","Move toward the camera along its viewing direction.",()=>Director.NudgeStudioLight(2,-lightMoveStep,true),"Farther","Move away from the camera along its viewing direction.",()=>Director.NudgeStudioLight(2,lightMoveStep,true)));
        if(light.type!=1)return;
        page.Heading("Aim the spot");
        studioLightControls.Add(page.Slide("Turn step (degrees)","Angle per rotation click. Pitch, yaw and roll follow the light's own axes.",lightTurnStep,1,90,value=>lightTurnStep=value,"0"));
        studioLightControls.AddRange(page.Pair("Tilt up","Pitch the spot upward around its local right axis.",()=>Director.RotateStudioLight(0,-lightTurnStep,true),"Tilt down","Pitch the spot downward around its local right axis.",()=>Director.RotateStudioLight(0,lightTurnStep,true)));
        studioLightControls.AddRange(page.Pair("Pan left","Yaw the spot left around its local up axis.",()=>Director.RotateStudioLight(1,-lightTurnStep,true),"Pan right","Yaw the spot right around its local up axis.",()=>Director.RotateStudioLight(1,lightTurnStep,true)));
        studioLightControls.AddRange(page.Pair("Roll left","Roll the spot around its beam axis. Useful for rotating Rectangle, Window and Slats patterns.",()=>Director.RotateStudioLight(2,lightTurnStep,true),"Roll right","Roll the spot around its beam axis in the opposite direction.",()=>Director.RotateStudioLight(2,-lightTurnStep,true)));
        studioLightControls.Add(page.Action("Reset aim","Reset this light's rotation to world forward, level with the horizon.",()=>Director.ResetStudioLightRotation()));
    }
    private void StudioLightAdvanced(MenuPage page,StudioLightSettings light)
    {
        lightCapabilityText=page.Note(Director.StudioLightCapabilities,90);
        page.Note("Up to 16 saved lights. The game can limit the number affecting each object. Smaller reach and fewer overlapping lights usually cost less. Transparent smoke and unlit surfaces may not respond to dynamic lights.",100);
        page.Action(lightShadowOptions?"Hide shadow options":"Shadow options","Show native shadow settings and whether this game's active renderer supports them.",()=> {lightShadowOptions=!lightShadowOptions;RebuildMenu();});
        if(!lightShadowOptions)return;
        page.Note(Director.StudioLightShadowsAvailable?"Shadows add rendering work; start with Off and enable only when the shot benefits.":"The active game renderer does not support additional-light shadows. These settings are unavailable; the mod leaves the game's renderer unchanged.",86);
        var modes=page.Choices(new[]{"Off","Hard","Soft"},new[]{"No additional shadow rendering.","Native hard shadows, when supported by the active game renderer.","Native filtered soft shadows, when supported by the active game renderer."},index=> {light.shadowMode=index;RebuildMenu();});
        studioLightShadowControls.Add(modes[0]);studioLightShadowControls.Add(modes[1]);studioLightSoftShadowControls.Add(modes[2]);for(int i=0;i<modes.Length;i++)SelectButton(modes[i],light.shadowMode==i);
        studioLightShadowControls.Add(page.Slide("Shadow strength (%)","0% removes shadow darkness; 100% uses full shadow strength.",light.shadowStrength*100,0,100,value=>light.shadowStrength=value/100,"0"));
        studioLightShadowControls.Add(page.Slide("Shadow near plane (m)","Nearest shadow-casting distance, in metres. Increasing it can improve precision while losing nearby shadows.",light.shadowNearPlane,.01f,10,value=>light.shadowNearPlane=value,"0.###"));
        page.Note("Shadow resolution and bias use the game renderer's settings.",44);
    }
    private static string CleanStudioLightName(string value)
    {
        if(string.IsNullOrEmpty(value))return "";
        var text=new System.Text.StringBuilder(Math.Min(80,value.Length));
        foreach(char character in value)if(!char.IsControl(character)&&text.Length<80)text.Append(character);
        return text.ToString();
    }
    private static string StudioLightSelectionTip(StudioLightSettings light)=>"Select "+(light.name??"").Replace('<','‹').Replace('>','›')+". Its box is highlighted in the world; Shape, Colour and Place edit this light.";
}
