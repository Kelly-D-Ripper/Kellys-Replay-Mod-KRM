using System.Collections.Generic;
using HarmonyLib;
using ReplayMod.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.UI;

[HarmonyPatch(typeof(global::MainMenu),"Start")]
public static class NativeReplayMenuEntryPatch
{
    [HarmonyPostfix]
    public static void Postfix(global::MainMenu __instance)
    {
        if(Application.isBatchMode||__instance==null||__instance.GetComponent<NativeReplayMenuEntry>()!=null)return;
        __instance.gameObject.AddComponent<NativeReplayMenuEntry>();
    }
}

// A fresh button copies presentation only. It never clones or replaces native
// controllers, listeners, layouts or navigation links.
public sealed class NativeReplayMenuEntry : MonoBehaviour
{
    private static NativeReplayMenuEntry visibleOwner;
    private global::MainMenu owner;
    private Button source,button;
    private RectTransform canvasRect,entry;
    private Canvas nativeCanvas;
    private SuiteTooltip tip;
    private readonly Vector3[] corners=new Vector3[4];
    private int lastWidth,lastHeight;
    private bool visibleLastFrame;
    private float nextAttempt;

    private void Awake(){owner=GetComponent<global::MainMenu>();}
    private void LateUpdate()
    {
        bool available=owner!=null&&owner.isActiveAndEnabled&&ReplayRuntimeHost.Instance!=null&&ReplayRuntimeHost.Instance.isActiveAndEnabled&&
            global::MainMenu.State==global::MainMenu.LoadingState.Loaded&&TrailerSuiteUi.Instance!=null&&
            owner.missionsButton!=null&&owner.missionsButton.gameObject.activeInHierarchy&&
            ReplayManager.i!=null&&!ReplayManager.i.IsTransitioning&&ReplayMod.Director.ReplayDirector.i?.Clean!=true&&
            (owner.overlayMenuLayer==null||owner.overlayMenuLayer.childCount==0);
        if(!available){Hide();return;}
        if(visibleOwner!=null&&visibleOwner!=this){Hide();return;}
        bool changed=!visibleLastFrame||lastWidth!=Screen.width||lastHeight!=Screen.height;
        if(entry==null||changed)
        {
            if(Time.unscaledTime<nextAttempt)return;nextAttempt=Time.unscaledTime+.5f;
            if(!Place()){Hide();return;}
        }
        if(entry==null)return;
        visibleOwner=this;visibleLastFrame=true;entry.gameObject.SetActive(true);button.interactable=true;
        if(tip!=null)tip.Owner=TrailerSuiteUi.Instance;
    }
    private bool Place()
    {
        source=owner.missionsButton;if(source==null||!source.gameObject.activeInHierarchy)return false;
        nativeCanvas=source.GetComponentInParent<Canvas>();if(nativeCanvas==null)return false;
        nativeCanvas=nativeCanvas.rootCanvas;canvasRect=nativeCanvas.transform as RectTransform;if(canvasRect==null)return false;
        var sourceRect=source.transform as RectTransform;if(sourceRect==null)return false;
        var occupied=new List<OverlayBox>();
        foreach(var selectable in nativeCanvas.GetComponentsInChildren<Selectable>())
            if(selectable!=null&&selectable!=button&&selectable.gameObject.activeInHierarchy&&selectable.transform is RectTransform rect&&TryBox(rect,out var box))occupied.Add(box);
        foreach(var label in nativeCanvas.GetComponentsInChildren<TMP_Text>())
            if(label!=null&&label.gameObject.activeInHierarchy&&(entry==null||!label.transform.IsChildOf(entry))&&TryBox(label.rectTransform,out var box))occupied.Add(box);
        foreach(var label in nativeCanvas.GetComponentsInChildren<UnityEngine.UI.Text>())
            if(label!=null&&label.gameObject.activeInHierarchy&&TryBox(label.rectTransform,out var box))occupied.Add(box);
        if(!TryBox(sourceRect,out var shape)||!NativeMenuPlacement.TryFind(canvasRect.rect.width,canvasRect.rect.height,shape,occupied.ToArray(),out var slot))return false;
        if(entry==null)BuildButton();
        else if(entry.parent!=canvasRect)entry.SetParent(canvasRect,false);
        entry.anchorMin=entry.anchorMax=new Vector2(0,1);entry.pivot=new Vector2(0,1);
        entry.anchoredPosition=new Vector2(slot.X,-slot.Y);entry.sizeDelta=new Vector2(slot.Width,slot.Height);
        lastWidth=Screen.width;lastHeight=Screen.height;return true;
    }
    private bool TryBox(RectTransform rect,out OverlayBox box)
    {
        rect.GetWorldCorners(corners);var low=canvasRect.InverseTransformPoint(corners[0]);var high=canvasRect.InverseTransformPoint(corners[2]);
        box=new OverlayBox(low.x-canvasRect.rect.xMin,canvasRect.rect.yMax-high.y,high.x-low.x,high.y-low.y);
        return box.Width>0&&box.Height>0;
    }
    private void BuildButton()
    {
        var obj=new GameObject("ReplayMod Replays entry",typeof(RectTransform),typeof(Image),typeof(Button));
        obj.layer=source.gameObject.layer;
        entry=obj.GetComponent<RectTransform>();entry.SetParent(canvasRect,false);
        var image=obj.GetComponent<Image>();
        if(source.targetGraphic is Image original){image.sprite=original.sprite;image.type=original.type;image.color=original.color;}
        else image.color=new NativeSuiteStyle().Surface;
        button=obj.GetComponent<Button>();button.targetGraphic=image;button.colors=source.colors;
        button.navigation=new Navigation{mode=Navigation.Mode.Automatic};button.onClick.AddListener(OpenLibrary);
        var labelObject=new GameObject("Replays label",typeof(RectTransform),typeof(TextMeshProUGUI));
        labelObject.layer=source.gameObject.layer;
        var labelRect=labelObject.GetComponent<RectTransform>();labelRect.SetParent(entry,false);
        labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;labelRect.offsetMin=new Vector2(8,0);labelRect.offsetMax=new Vector2(-8,0);
        var label=labelObject.GetComponent<TextMeshProUGUI>();var sample=source.GetComponentInChildren<TMP_Text>(true);
        if(sample!=null){label.font=sample.font;label.fontSharedMaterial=sample.fontSharedMaterial;label.fontSize=sample.fontSize;label.fontStyle=sample.fontStyle;label.color=sample.color;}
        else{var style=new NativeSuiteStyle();style.Sample();style.Apply(label,22);}
        label.text="REPLAYS";label.alignment=TextAlignmentOptions.Center;label.richText=false;label.raycastTarget=false;label.enableWordWrapping=false;
        tip=obj.AddComponent<SuiteTooltip>();tip.Owner=TrailerSuiteUi.Instance;tip.AllowWhenClosed=true;
        tip.Description="Open your replay library, camera tools and interrupted-recording recovery.";
    }
    private void OpenLibrary(){if(TrailerSuiteUi.Instance!=null&&ReplayManager.i!=null&&!ReplayManager.i.IsTransitioning)TrailerSuiteUi.Instance.OpenLibrary();}
    private void Hide(){if(visibleOwner==this)visibleOwner=null;visibleLastFrame=false;if(entry!=null)entry.gameObject.SetActive(false);}
    private void OnDisable(){Hide();}
    private void OnDestroy(){if(visibleOwner==this)visibleOwner=null;if(button!=null)button.onClick.RemoveListener(OpenLibrary);if(entry!=null)Destroy(entry.gameObject);}
}
