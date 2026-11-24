using ReplayMod.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayMod.UI;

public sealed partial class TrailerSuiteUi
{
    private RectTransform identityCaption;
    private TextMeshProUGUI identityCaptionText;
    private void BuildIdentityCaption()
    {
        identityCaption=Rect("Spectated aircraft identity",root);
        identityCaption.anchorMin=identityCaption.anchorMax=new Vector2(0,1);identityCaption.pivot=new Vector2(0,1);
        identityCaption.anchoredPosition=new Vector2(18,-18);identityCaption.sizeDelta=new Vector2(600,65);
        Fill(identityCaption,SuiteColourRole.Background,false);Border(identityCaption);
        identityCaption.gameObject.AddComponent<RectMask2D>();
        Text(identityCaption,"SPECTATING",12,5,576,19,13,true);
        identityCaptionText=Text(identityCaption,"",12,25,576,32,20);
        identityCaptionText.richText=false;identityCaptionText.overflowMode=TextOverflowModes.Ellipsis;
        identityCaption.gameObject.SetActive(false);
        Canvas.willRenderCanvases+=RefreshIdentityCaption;
    }
    private void RefreshIdentityCaption()
    {
        if(identityCaption==null)return;
        if(!isActiveAndEnabled||!Playback||loadingReplay||operationBusy||Director==null||!Director.TryGetCaptionActor(out var id))
        {identityCaption.gameObject.SetActive(false);return;}
        string label=ActorIdentityText.Caption(Manager.Playback.GetActorIdentity(id));
        bool show=label.Length>0;if(identityCaption.gameObject.activeSelf!=show)identityCaption.gameObject.SetActive(show);if(!show)return;
        float width=Mathf.Clamp(root.rect.width-36,160,600);
        identityCaption.sizeDelta=new Vector2(width,65);identityCaptionText.rectTransform.sizeDelta=new Vector2(width-24,32);
        if(identityCaptionText.text!=label)identityCaptionText.text=label;
    }
}
