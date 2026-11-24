using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private SuiteThemeSettings appearanceDraft;
    private bool appearancePending;
    private float appearanceSaveAt,nextAppearanceReadAt;
    private int appearanceColour;
    private Button[] appearancePresets,appearanceColourButtons;
    private Button appearanceCustom;
    private TMP_InputField appearanceHex;
    private TextMeshProUGUI appearanceMessage;
    private readonly Slider[] appearanceChannels=new Slider[3];
    private readonly TextMeshProUGUI[] appearanceChannelLabels=new TextMeshProUGUI[3];
    private static readonly string[] AppearancePresets={"Native","Amber","Blue","Neutral"};
    private static readonly string[] AppearanceColourNames={"Accent","Panel","Buttons","Text"};

    private void RefreshAppearanceIfChanged()
    {
        if(appearancePending)
        {if(Time.unscaledTime>=appearanceSaveAt)FlushAppearance();return;}
        if(Time.unscaledTime<nextAppearanceReadAt)return;nextAppearanceReadAt=Time.unscaledTime+.25f;
        var settings=ConfigManager.ReadAppearance();
        if(style.ConfigureTheme(settings))timelineMarkers?.SetAccent(style.Accent);
    }
    private void FlushAppearance()
    {
        if(!appearancePending)return;appearancePending=false;
        try
        {
            ConfigManager.SaveAppearance(appearanceDraft);nextAppearanceReadAt=Time.unscaledTime+.25f;
            if(appearanceMessage!=null)appearanceMessage.text="Saved for all replays. Text contrast is adjusted automatically.\nOpacity may increase to keep text readable.";
        }
        catch(Exception error)
        {Plugin.logger?.LogWarning("Could not save replay panel appearance: "+error.Message);Notice("Could not save appearance settings: "+error.Message);}
    }
    private void PreviewAppearance()
    {
        if(style.ConfigureTheme(appearanceDraft))timelineMarkers?.SetAccent(style.Accent);
        appearancePending=true;appearanceSaveAt=Time.unscaledTime+.5f;
        if(appearanceMessage!=null)appearanceMessage.text="Previewing colours… changes save when you finish adjusting.\nOpacity may increase to keep text readable.";
        if(appearancePresets!=null)
            for(int i=0;i<appearancePresets.Length;i++)SelectButton(appearancePresets[i],appearanceDraft.Preset==AppearancePresets[i]);
        SelectButton(appearanceCustom,appearanceDraft.Preset=="Custom");
    }
    private void AppearanceMenu(MenuPage page)
    {
        if(!appearancePending)appearanceDraft=ConfigManager.ReadAppearance();
        appearancePresets=null;appearanceCustom=null;appearanceHex=null;appearanceMessage=null;appearanceColourButtons=null;
        for(int i=0;i<3;i++){appearanceChannels[i]=null;appearanceChannelLabels[i]=null;}
        page.Heading("Studio colours");
        appearancePresets=page.Choices(AppearancePresets,new[]{"The game's native green and olive palette.","Warm amber highlights.","Cool blue highlights.","Neutral grey highlights."},index=>
        {appearanceDraft.Preset=AppearancePresets[index];PreviewAppearance();RebuildMenu();},4);
        var actions=page.Pair("Custom colours","Adjust the current palette using RGB sliders or a #RRGGBB colour.",()=>
        {
            if(appearanceDraft.Preset!="Custom")
            {
                var palette=SuiteThemePalette.Build(appearanceDraft);
                appearanceDraft.Accent=palette.Accent.Hex;appearanceDraft.Panel=palette.Panel.Hex;
                appearanceDraft.Button=palette.Button.Hex;appearanceDraft.Text=palette.Text.Hex;
            }
            appearanceDraft.Preset="Custom";PreviewAppearance();RebuildMenu();
        },"Restore native","Restore the default green palette, original panel opacity and custom colour defaults.",()=>
        {appearanceDraft=new SuiteThemeSettings();PreviewAppearance();FlushAppearance();RebuildMenu();});
        appearanceCustom=actions[0];
        for(int i=0;i<appearancePresets.Length;i++)SelectButton(appearancePresets[i],appearanceDraft.Preset==AppearancePresets[i]);
        SelectButton(appearanceCustom,appearanceDraft.Preset=="Custom");
        page.Slide("Panel opacity","Preferred replay panel opacity. Actual opacity may increase to keep text and highlights readable over the game. The seek cover remains opaque black.",appearanceDraft.Opacity,.65f,1,value=>
        {appearanceDraft.Opacity=value;PreviewAppearance();},"0%");
        appearanceMessage=page.Note((appearancePending?"Previewing colours… changes save when you finish adjusting.":"Saved for all replays. Text contrast is adjusted automatically.")+"\nOpacity may increase to keep text readable.",68);
        if(appearanceDraft.Preset!="Custom")return;
        page.Heading("Choose a colour to edit");
        appearanceColourButtons=page.Choices(AppearanceColourNames,new[]{"Highlight and slider fill.","Studio panel background.","Buttons and text inputs.","Preferred label colour. Contrast correction keeps it readable."},index=>
        {appearanceColour=index;RebuildMenu();},4);
        for(int i=0;i<appearanceColourButtons.Length;i++)SelectButton(appearanceColourButtons[i],appearanceColour==i);
        var colour=ReadAppearanceColour();string[] labels={"Red","Green","Blue"};byte[] channels={colour.R,colour.G,colour.B};
        for(int i=0;i<3;i++)
        {
            int channel=i;appearanceChannels[i]=page.Slide(labels[i],"Adjust "+labels[i].ToLowerInvariant()+" for "+AppearanceColourNames[appearanceColour].ToLowerInvariant()+". Preview updates immediately.",channels[i],0,255,value=>
            {
                var previous=ReadAppearanceColour();byte next=(byte)Mathf.RoundToInt(value);
                WriteAppearanceColour(new SuiteRgb(channel==0?next:previous.R,channel==1?next:previous.G,channel==2?next:previous.B));
                PreviewAppearance();RefreshAppearanceColourFields();
            },"0",label=>appearanceChannelLabels[channel]=label);
            appearanceChannels[i].wholeNumbers=true;
        }
        appearanceHex=page.Field(colour.Hex,"Type a six-digit RGB colour such as #58E187, then press Apply hex. Invalid text does not change or save your palette.",value=>{},"#RRGGBB");
        appearanceHex.characterLimit=7;
        page.Action("Apply hex","Apply this #RRGGBB colour. A leading # is optional.",()=>
        {
            if(!SuiteRgb.TryParse(appearanceHex.text,out var parsed)){Notice("Enter six hexadecimal digits, for example #58E187.");return;}
            WriteAppearanceColour(parsed);PreviewAppearance();RefreshAppearanceColourFields();
        });
    }
    private SuiteRgb ReadAppearanceColour()
    {
        string hex=appearanceColour==0?appearanceDraft.Accent:appearanceColour==1?appearanceDraft.Panel:appearanceColour==2?appearanceDraft.Button:appearanceDraft.Text;
        return SuiteRgb.Parse(hex,"#58E187");
    }
    private void WriteAppearanceColour(SuiteRgb colour)
    {
        switch(appearanceColour)
        {case 0:appearanceDraft.Accent=colour.Hex;break;case 1:appearanceDraft.Panel=colour.Hex;break;case 2:appearanceDraft.Button=colour.Hex;break;default:appearanceDraft.Text=colour.Hex;break;}
    }
    private void RefreshAppearanceColourFields()
    {
        var colour=ReadAppearanceColour();int[] values={colour.R,colour.G,colour.B};string[] labels={"Red","Green","Blue"};
        for(int i=0;i<3;i++)
        {
            if(appearanceChannels[i]!=null)appearanceChannels[i].SetValueWithoutNotify(values[i]);
            if(appearanceChannelLabels[i]!=null)appearanceChannelLabels[i].text=labels[i]+": "+values[i];
        }
        if(appearanceHex!=null&&!appearanceHex.isFocused)appearanceHex.SetTextWithoutNotify(colour.Hex);
    }
}
