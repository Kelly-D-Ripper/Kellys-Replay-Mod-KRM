using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using ReplayMod.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Keyboard Move (A/D, arrows) and Submit (Enter, Space) must not drive studio
// controls left selected by a mouse click.
public static class UiInputProbe
{
    static readonly string[] Inert={"SuiteButton","SuiteSlider","TimelineSlider","SuiteScrollbar"};
    static bool Move(GameObject target,MoveDirection direction)
    {
        var data=new AxisEventData(EventSystem.current){moveDir=direction,moveVector=direction==MoveDirection.Right?Vector2.right:Vector2.left};
        EventSystem.current.SetSelectedGameObject(target);
        return ExecuteEvents.Execute(target,data,ExecuteEvents.moveHandler);
    }
    public static async UniTask Run(Action<string> log)
    {
        if(EventSystem.current==null)throw new Exception("No event system");
        // Control: a stock slider configured like the old studio sliders moves on keyboard input.
        var stockObject=new GameObject("Stock slider",typeof(RectTransform));
        try
        {
            var stock=stockObject.AddComponent<Slider>();stock.navigation=new Navigation{mode=Navigation.Mode.None};stock.minValue=0;stock.maxValue=10;stock.value=5;
            Move(stockObject,MoveDirection.Right);
            log("UI INPUT control stock slider after Move right: "+stock.value+" (was 5)");
            if(stock.value==5)throw new Exception("Control slider ignored Move; probe cannot demonstrate the bug");
        }
        finally{UnityEngine.Object.Destroy(stockObject);}
        var buttonObject=new GameObject("Probe button",typeof(RectTransform));
        try
        {
            var button=(Button)buttonObject.AddComponent(typeof(TrailerSuiteUi).Assembly.GetType("ReplayMod.UI.SuiteButton",true));int clicks=0;button.onClick.AddListener(()=>clicks++);
            EventSystem.current.SetSelectedGameObject(buttonObject);
            ExecuteEvents.Execute(buttonObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            if(clicks!=0)throw new Exception("Submit clicked a studio button");
            log("UI INPUT studio button ignored Submit");
        }
        finally{UnityEngine.Object.Destroy(buttonObject);}
        var ui=TrailerSuiteUi.Instance;if(!ui.IsVisible)ui.Toggle();
        var open=AccessTools.Method(typeof(TrailerSuiteUi),"OpenMenu");var rebuild=AccessTools.Method(typeof(TrailerSuiteUi),"RebuildMenu");
        int sliders=0,scrollbars=0,buttons=0;
        async UniTask CheckCurrent(string where)
        {
            await UniTask.NextFrame();
            var selectables=ui.GetComponentsInChildren<Selectable>(false);
            var foreign=selectables.Where(s=>!(Inert.Contains(s.GetType().Name)||s is TMPro.TMP_InputField)).ToArray();
            if(foreign.Length>0)throw new Exception(where+": keyboard-driven controls remain: "+string.Join(", ",foreign.Select(s=>s.GetType().Name+" "+s.name)));
            foreach(var slider in selectables.OfType<Slider>())
            {
                float before=slider.value;Move(slider.gameObject,MoveDirection.Right);Move(slider.gameObject,MoveDirection.Left);
                if(slider.value!=before)throw new Exception(where+": slider "+slider.name+" changed from "+before+" to "+slider.value);
                sliders++;
            }
            foreach(var bar in selectables.OfType<Scrollbar>())
            {
                float before=bar.value;Move(bar.gameObject,MoveDirection.Up);Move(bar.gameObject,MoveDirection.Down);
                if(bar.value!=before)throw new Exception(where+": scrollbar changed");
                scrollbars++;
            }
            buttons+=selectables.Count(s=>s.GetType().Name=="SuiteButton");
            EventSystem.current.SetSelectedGameObject(null);
        }
        await CheckCurrent("toolbar");
        foreach(string menu in new[]{"Library","Record","Camera","Events","Scene","Lights","Help","Settings"})
        {open.Invoke(ui,new object[]{menu});await CheckCurrent(menu);}
        open.Invoke(ui,new object[]{"Camera"});
        for(int tab=0;tab<5;tab++){AccessTools.Field(typeof(TrailerSuiteUi),"cameraTab").SetValue(ui,tab);rebuild.Invoke(ui,null);await CheckCurrent("Camera tab "+tab);}
        log("UI INPUT PASS "+sliders+" slider and "+scrollbars+" scrollbar checks unchanged by keyboard Move; "+buttons+" studio buttons are Submit-inert");
    }
}
