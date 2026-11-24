using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private RectTransform Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }
    private static void Box(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Horizontal(RectTransform rect,float left,float right,float top,float height)
    {
        rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);
        rect.offsetMin=new Vector2(left,-top-height);rect.offsetMax=new Vector2(-right,-top);
    }
    private Image Fill(RectTransform rect, Color color, bool raycast = true)
    {
        var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image;
    }
    private Image Fill(RectTransform rect,SuiteColourRole role,bool raycast=true)
    {var image=Fill(rect,style.Colour(role),raycast);style.Bind(image,role);return image;}
    private void Border(RectTransform parent)
    {
        var top = Rect("Fine border", parent); top.anchorMin = new Vector2(0, 1); top.anchorMax = Vector2.one;
        top.pivot = new Vector2(.5f, 1); top.sizeDelta = new Vector2(0, 1); top.anchoredPosition = Vector2.zero;
        Fill(top, SuiteColourRole.Border, false);
    }
    private TextMeshProUGUI Text(Transform parent, string value, float x, float y, float width, float height, float size = 18, bool muted = false)
    {
        var rect = Rect(value, parent); Box(rect, x, y, width, height);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); style.Apply(text, size, muted); text.text = value;
        text.verticalAlignment = VerticalAlignmentOptions.Middle; return text;
    }
    private void Tip(GameObject obj, string text)
    { var tip = obj.AddComponent<SuiteTooltip>(); tip.Owner = this; tip.Description = text; }
    private Button Button(Transform parent, string name, string tip, float x, float y, float width, float height, Action action, bool accent = false)
    {
        var rect = Rect(name, parent); Box(rect,x,y,width,height);
        var image = Fill(rect, accent ? SuiteColourRole.SelectedSurface : SuiteColourRole.Surface);
        if(style.ButtonSprite != null)
        { image.sprite = style.ButtonSprite; image.type = style.ButtonType; image.fillCenter = true; }
        var button = rect.gameObject.AddComponent<SuiteButton>(); button.targetGraphic = image;
        style.Bind((Selectable)button);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var label = Text(rect,name,7,0,width-14,height,18); label.alignment = TextAlignmentOptions.Center;
        style.Bind(label,accent?SuiteColourRole.SelectedText:SuiteColourRole.ButtonText);
        Stretch(label.rectTransform);label.rectTransform.offsetMin=new Vector2(7,0);label.rectTransform.offsetMax=new Vector2(-7,0);
        label.enableWordWrapping = false;
        button.onClick.AddListener(()=> { HideTooltip(null); action(); }); Tip(rect.gameObject,tip); return button;
    }
    private Slider Slider(Transform parent, string name, string tip, float x,float y,float width,float height,float min,float max,float value,Action<float> change,bool timeline = false)
    {
        var rect = Rect(name,parent);Box(rect,x,y,width,height);Fill(rect,Color.clear);
        var track=Rect("Track",rect);Horizontal(track,0,0,height*.5f-3,6);Fill(track,SuiteColourRole.Surface,false);
        var fillArea=Rect("Fill area",rect);Horizontal(fillArea,8,8,height*.5f-3,6);
        var fill=Rect("Position",fillArea);Stretch(fill);Fill(fill,SuiteColourRole.Accent,false);
        var handleArea=Rect("Handle area",rect);Horizontal(handleArea,8,8,0,height);
        var handle=Rect("Handle",handleArea);handle.anchorMin=handle.anchorMax=new Vector2(0,.5f);handle.sizeDelta=new Vector2(timeline?18:14,timeline?28:22);
        var handleImage=Fill(handle,SuiteColourRole.Handle,false);
        var slider=timeline?(Slider)rect.gameObject.AddComponent<TimelineSlider>():rect.gameObject.AddComponent<SuiteSlider>();
        slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handleImage;slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue=min;slider.maxValue=max;slider.SetValueWithoutNotify(value);slider.navigation=new Navigation {mode=Navigation.Mode.None};
        style.Bind((Selectable)slider);
        slider.onValueChanged.AddListener(v=>change(v));Tip(rect.gameObject,tip);return slider;
    }
    private TMP_InputField InputField(Transform parent,string value,string tip,float x,float y,float width,float height,Action<string> changed,string placeholder=null)
    {
        var rect=Rect("Text input",parent);Box(rect,x,y,width,height);var background=Fill(rect,SuiteColourRole.Surface);
        var viewport=Rect("Text viewport",rect);Box(viewport,10,3,width-20,height-6);viewport.gameObject.AddComponent<RectMask2D>();
        var text=Text(viewport,value,0,0,width-20,height-6);text.overflowMode=TextOverflowModes.Overflow;
        style.Bind(text,SuiteColourRole.ButtonText);
        var field=rect.gameObject.AddComponent<TMP_InputField>();field.targetGraphic=background;field.textViewport=viewport;field.textComponent=text;
        field.lineType=TMP_InputField.LineType.SingleLine;field.characterLimit=120;field.text=value;field.onValueChanged.AddListener(v=>changed(v));
        if(!string.IsNullOrEmpty(placeholder))
        {var hint=Text(viewport,placeholder,0,0,width-20,height-6,18,true);style.Bind(hint,SuiteColourRole.ButtonText);hint.enableWordWrapping=false;field.placeholder=hint;}
        style.Bind(field);field.navigation=new Navigation {mode=Navigation.Mode.None};Tip(rect.gameObject,tip);return field;
    }
    private void SelectButton(Button button,bool selected)
    {
        if(button==null)return;
        var image=button.targetGraphic as Image;
        if(image!=null)style.Bind(image,selected?SuiteColourRole.SelectedSurface:SuiteColourRole.Surface);
        var label=button.GetComponentInChildren<TMP_Text>();if(label!=null)style.Bind(label,selected?SuiteColourRole.SelectedText:SuiteColourRole.ButtonText);
    }
    private MenuPage Page(string title,string intro,bool projectFooter=false)
    {
        style.Bind(Text(popout,title.ToUpperInvariant(),18,8,popoutWidth-76,32,20),SuiteColourRole.Muted);
        Button(popout,"X","Close this menu. The playback controls stay available.",popoutWidth-50,8,32,32,ClosePopout);
        float footer=projectFooter?50:0;
        if(projectFooter)
        {
            var save=Button(popout,"Save project","Save camera views, paths, bookmarks, scene settings and custom lights beside this replay.",16,popoutHeight-46,popoutWidth-32,34,()=> {Director.Save();Notice(Director.Status);},true);
            lensControls.Add(save);
        }
        menuViewportHeight=popoutHeight-66-footer;
        var scrollRoot=Rect("Scrollable tools",popout);Box(scrollRoot,16,52,popoutWidth-32,menuViewportHeight);
        var viewport=Rect("Viewport",scrollRoot);Stretch(viewport);viewport.offsetMax=new Vector2(-20,0);Fill(viewport,Color.clear);viewport.gameObject.AddComponent<RectMask2D>();
        var content=Rect("Tools",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=Vector2.zero;
        var scroll=scrollRoot.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.inertia=false;scroll.scrollSensitivity=32;
        menuScroll=scroll;currentScrollContext=MenuTaskKey;
        var rail=Rect("Scroll bar",scrollRoot);rail.anchorMin=new Vector2(1,0);rail.anchorMax=Vector2.one;rail.pivot=new Vector2(1,.5f);rail.sizeDelta=new Vector2(12,0);rail.anchoredPosition=Vector2.zero;Fill(rail,SuiteColourRole.Surface);
        var sliding=Rect("Sliding area",rail);Stretch(sliding);var handle=Rect("Handle",sliding);Stretch(handle);var handleImage=Fill(handle,SuiteColourRole.Handle);
        var scrollbar=rail.gameObject.AddComponent<SuiteScrollbar>();scrollbar.handleRect=handle;scrollbar.targetGraphic=handleImage;scrollbar.direction=Scrollbar.Direction.BottomToTop;scrollbar.navigation=new Navigation {mode=Navigation.Mode.None};scroll.verticalScrollbar=scrollbar;
        style.Bind((Selectable)scrollbar);
        Tip(rail.gameObject,"Drag this bar or use the mouse wheel to see the rest of this menu.");Tip(viewport.gameObject,"Use the mouse wheel to scroll through these tools.");
        var page=new MenuPage(this,content,popoutWidth-56);if(!string.IsNullOrWhiteSpace(intro))page.Note(intro,42);return page;
    }
    private sealed class MenuPage
    {
        internal readonly RectTransform Content;
        private readonly TrailerSuiteUi ui;
        internal readonly float Width;
        private float y;
        internal MenuPage(TrailerSuiteUi ui,RectTransform content,float width) {this.ui=ui;Content=content;Width=width;}
        private float Row(float height) {float pos=y;y+=height+8;Content.sizeDelta=new Vector2(0,y+4);return pos;}
        internal TextMeshProUGUI Note(string value,float height=40,bool muted=true)
        {return ui.Text(Content,value,0,Row(height),Width,height,17,muted);}
        internal void Heading(string value) {ui.style.Bind(ui.Text(Content,value.ToUpperInvariant(),0,Row(28),Width,28,17),SuiteColourRole.Muted);}
        internal Button Action(string label,string tip,Action action,bool accent=false)
        {return ui.Button(Content,label,tip,0,Row(38),Width,38,action,accent);}
        internal Button[] Pair(string a,string tipA,Action actionA,string b,string tipB,Action actionB)
        {float pos=Row(38),half=(Width-8)*.5f;return new[]{ui.Button(Content,a,tipA,0,pos,half,38,actionA),ui.Button(Content,b,tipB,half+8,pos,half,38,actionB)};}
        internal Button[] Tabs(string[] labels,int selected,Action<int> change)
        {
            // Task navigation stays available while a long tool page scrolls.
            var strip=ui.Rect("Task tabs",ui.popout);Box(strip,16,52,Width,38);
            float width=(Width-8*(labels.Length-1))/labels.Length;
            var buttons=new Button[labels.Length];
            for(int i=0;i<labels.Length;i++)
            {
                int index=i;
                buttons[i]=ui.Button(strip,labels[i],"Open "+labels[i].ToLowerInvariant()+" tools.",i*(width+8),0,width,38,()=>change(index));
                ui.SelectButton(buttons[i],i==selected);
            }
            ui.menuViewportHeight-=46;
            Box((RectTransform)ui.menuScroll.transform,16,98,ui.popoutWidth-32,ui.menuViewportHeight);
            return buttons;
        }
        internal Button[] Choices(string[] labels,string[] tips,Action<int> change,int columns=3)
        {
            var buttons=new Button[labels.Length];float width=(Width-8*(columns-1))/columns;
            for(int row=0;row*columns<labels.Length;row++)
            {
                float pos=Row(38);
                for(int column=0;column<columns;column++)
                {int index=row*columns+column;if(index>=labels.Length)break;buttons[index]=ui.Button(Content,labels[index],tips[index],column*(width+8),pos,width,38,()=>change(index));}
            }
            return buttons;
        }
        internal Slider Slide(string label,string tip,float value,float min,float max,Action<float> change,string format="0.0",Action<TextMeshProUGUI> receiveLabel=null)
        {
            float pos=Row(60);var text=ui.Text(Content,label+": "+value.ToString(format),0,pos,Width,24,18);
            receiveLabel?.Invoke(text);
            return ui.Slider(Content,label,tip,0,pos+26,Width,34,min,max,value,v=> {text.text=label+": "+v.ToString(format);change(v);});
        }
        internal Button Switch(string label,string tip,Func<bool> read,Action<bool> change)
        {
            Button button=null;button=Action(label+": "+(read()?"ON":"OFF"),tip,()=> {change(!read());button.GetComponentInChildren<TMP_Text>().text=label+": "+(read()?"ON":"OFF");});return button;
        }
        internal TMP_InputField Field(string value,string tip,Action<string> change,string placeholder=null)
        {return ui.InputField(Content,value,tip,0,Row(38),Width,38,change,placeholder);}
        internal Selectable[] PointTimeRow(string label,double seconds,Action jump,Action<double> apply)
        {
            float pos=Row(44),inputWidth=118,applyWidth=76,buttonWidth=Width-inputWidth-applyWidth-20;
            string value=seconds.ToString("0.###");
            var seek=ui.Button(Content,label,"Pause at this point and restore its saved camera position, aim, roll and FOV.",0,pos,buttonWidth,44,jump);
            var field=ui.InputField(Content,value,"Replay time in seconds. Keep it between the neighbouring points, then press Apply.",buttonWidth+10,pos,inputWidth,44,v=>value=v);
            var save=ui.Button(Content,"APPLY","Save this point's replay time in seconds.",buttonWidth+inputWidth+20,pos,applyWidth,44,()=> {
                if(double.TryParse(value,out double time)&&!double.IsNaN(time)&&!double.IsInfinity(time))apply(time);
                else ui.Notice("Enter a valid replay time in seconds.");
            });
            return new Selectable[]{seek,field,save};
        }
        internal Button[] EventRow(string label,Action jump,Action follow,Action remove=null)
        {
            float pos=Row(68),half=(Width-8)*.5f;
            ui.Text(Content,label,0,pos,Width-(remove==null?0:42),28,18);
            var jumpButton=ui.Button(Content,"Jump","Jump to this exact replay time and pause. Keeps your camera and selected subject.",0,pos+32,half,36,jump);
            var followButton=ui.Button(Content,"Follow","Start before this event, follow its recorded subject with the chosen camera preset, then play. An absent subject never switches to another unit.",half+8,pos+32,half,36,follow);
            if(remove==null)return new[]{jumpButton,followButton};
            var removeButton=ui.Button(Content,"X","Remove this named bookmark. Recorded automatic events remain.",Width-34,pos,34,28,remove);
            return new[]{jumpButton,followButton,removeButton};
        }
    }
}
