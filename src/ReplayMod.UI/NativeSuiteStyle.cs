using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.UI;

internal enum SuiteColourRole
{ Background, Surface, SelectedSurface, Accent, Text, Muted, Border, Selection, ButtonText, SelectedText, Handle }

// Only reads presentation assets. Native event handlers and game controllers stay on their objects.
internal sealed class NativeSuiteStyle
{
    internal TMP_FontAsset Font;
    internal Material FontMaterial;
    internal Sprite ButtonSprite;
    internal Image.Type ButtonType;
    // Stock primary/sage/text, olive panels and dark green menu/success tones.
    // Panel opacity is adapted for readable camera editing; only our graphics change.
    internal Color Background = new Color32(5,10,5,248);
    internal Color Surface = new Color32(62,70,60,255);
    internal Color SelectedSurface = new Color32(23,48,33,255);
    internal Color Accent = new Color32(88,225,135,255);
    internal Color Text = new Color32(224,224,224,255);
    internal Color Muted = new Color32(125,174,125,255);
    internal Color Border = new Color32(125,174,125,160);
    internal Color Selection = new Color32(88,225,135,88);
    internal Color HoverTint = new Color(1.12f,1.3f,1.12f,1);
    internal Color PressedTint = new Color(.65f,.9f,.65f,1);
    private Color buttonText=new Color32(224,224,224,255),selectedText=new Color32(88,225,135,255),handle=new Color32(224,224,224,255),trackAccent=new Color32(88,225,135,255);
    private readonly Dictionary<Graphic,SuiteColourRole> bindings=new Dictionary<Graphic,SuiteColourRole>();
    private readonly HashSet<TMP_InputField> inputs=new HashSet<TMP_InputField>();
    private readonly HashSet<Selectable> controls=new HashSet<Selectable>();
    private string themeSignature;
    private int registrationCount;

    private static Color Colour(SuiteRgb value,float alpha=1)=>new Color(value.R/255f,value.G/255f,value.B/255f,alpha);
    internal Color Colour(SuiteColourRole role)
    {
        switch(role)
        {
            case SuiteColourRole.Background:return Background;
            case SuiteColourRole.Surface:return Surface;
            case SuiteColourRole.SelectedSurface:return SelectedSurface;
            case SuiteColourRole.Accent:return trackAccent;
            case SuiteColourRole.Muted:return Muted;
            case SuiteColourRole.Border:return Border;
            case SuiteColourRole.Selection:return Selection;
            case SuiteColourRole.ButtonText:return buttonText;
            case SuiteColourRole.SelectedText:return selectedText;
            case SuiteColourRole.Handle:return handle;
            default:return Text;
        }
    }
    internal void Bind(Graphic graphic,SuiteColourRole role)
    {
        if(graphic==null)return;
        if(bindings.TryGetValue(graphic,out var oldRole)&&oldRole==role)return;
        bindings[graphic]=role;graphic.color=Colour(role);
        // Menus are rebuilt on navigation, so discard destroyed owned graphics at registration boundaries.
        if(++registrationCount%64==0)Prune();
    }
    internal void Unbind(Graphic graphic)
    {if(!ReferenceEquals(graphic,null))bindings.Remove(graphic);}
    internal void Bind(TMP_InputField input)
    {if(input==null)return;inputs.Add(input);input.caretColor=trackAccent;input.selectionColor=Selection;Bind((Selectable)input);}
    internal void Bind(Selectable control)
    {if(control==null)return;controls.Add(control);ApplyControlColours(control);}
    private void ApplyControlColours(Selectable control)
    {
        var colours=control.colors;colours.normalColor=Color.white;colours.highlightedColor=HoverTint;
        colours.pressedColor=PressedTint;colours.selectedColor=Color.white;colours.disabledColor=new Color(.6f,.6f,.6f,.55f);colours.fadeDuration=.06f;control.colors=colours;
    }
    private void Prune()
    {
        foreach(var graphic in bindings.Keys.Where(g=>g==null).ToArray())bindings.Remove(graphic);
        inputs.RemoveWhere(field=>field==null);controls.RemoveWhere(control=>control==null);
    }
    internal bool ConfigureTheme(SuiteThemeSettings settings)
    {
        settings=settings??new SuiteThemeSettings();string signature=settings.Signature;
        if(signature==themeSignature)return false;themeSignature=signature;
        var palette=SuiteThemePalette.Build(settings);
        Background=Colour(palette.Panel,palette.Opacity);Surface=Colour(palette.Button);SelectedSurface=Colour(palette.Selected);
        Accent=Colour(palette.MarkerAccent);trackAccent=Colour(palette.TrackAccent);Text=Colour(palette.Text);Muted=Colour(palette.Muted);
        Border=Colour(palette.Native?new SuiteRgb(125,174,125):palette.MarkerAccent,160f/255);Selection=Colour(palette.TrackAccent,88f/255);
        buttonText=Colour(palette.ButtonText);selectedText=Colour(palette.SelectedText);handle=Colour(palette.Handle);
        // Retain native feedback; custom tints are used only when labels and handles keep their contrast.
        HoverTint=palette.Native?new Color(1.12f,1.3f,1.12f,1):new Color(palette.HoverMultiplier,palette.HoverMultiplier,palette.HoverMultiplier,1);
        PressedTint=palette.Native?new Color(.65f,.9f,.65f,1):new Color(palette.PressedMultiplier,palette.PressedMultiplier,palette.PressedMultiplier,1);
        Prune();foreach(var pair in bindings)pair.Key.color=Colour(pair.Value);
        foreach(var field in inputs){field.caretColor=trackAccent;field.selectionColor=Selection;}
        foreach(var control in controls)ApplyControlColours(control);
        return true;
    }

    internal void Sample()
    {
        var texts = Resources.FindObjectsOfTypeAll<TMP_Text>()
            .Where(t => t != null && t.font != null && !WithinSuite(t.transform)).ToArray();
        var source = texts.FirstOrDefault(t => t.text != null &&
            (t.text.Contains("MAP OPTIONS") || t.text.Contains("MULTIPLAYER") || t.text.Contains("SINGLE PLAYER")))
            ?? texts.FirstOrDefault(t => t.gameObject.activeInHierarchy) ?? texts.FirstOrDefault();
        if(source != null)
        {
            Font = source.font; FontMaterial = source.fontSharedMaterial;
        }
        else Font = TMP_Settings.defaultFontAsset;
        var button = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b =>
            b != null && !WithinSuite(b.transform) && b.targetGraphic is Image image && image.sprite != null);
        if(button?.targetGraphic is Image native)
        { ButtonSprite = native.sprite; ButtonType = native.type; }
    }

    private static bool WithinSuite(Transform transform)
    {
        while(transform != null)
        { if(transform.name == "Replay trailer suite") return true; transform = transform.parent; }
        return false;
    }

    internal void Apply(TextMeshProUGUI text, float size, bool muted = false)
    {
        if(Font != null) text.font = Font;
        if(FontMaterial != null) text.fontSharedMaterial = FontMaterial;
        text.fontSize = size; Bind(text,muted?SuiteColourRole.Muted:SuiteColourRole.Text);
        text.richText = false; text.raycastTarget = false;
        text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Ellipsis;
        text.margin = new Vector4(0, 0, 0, 0);
    }
}
