using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReplayMod.UI;

internal sealed class SuiteTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal TrailerSuiteUi Owner;
    internal string Description;
    internal bool AllowWhenClosed;
    public void OnPointerEnter(PointerEventData data) { Owner?.ShowTooltip(this, Description); }
    public void OnPointerExit(PointerEventData data) { Owner?.HideTooltip(this); }
    private void OnDisable() { Owner?.HideTooltip(this); }
}

// The studio is mouse-driven. A clicked control stays selected, and Unity's Move
// (A/D, arrows) and Submit (Enter, Space) would otherwise keep changing the last
// slider while flying the free camera, or click the last button again on Space.
internal sealed class SuiteButton : Button
{
    public override void OnSubmit(BaseEventData eventData) { }
}
internal sealed class SuiteSlider : Slider
{
    public override void OnMove(AxisEventData eventData) { }
}
internal sealed class SuiteScrollbar : Scrollbar
{
    public override void OnMove(AxisEventData eventData) { }
}

// Slider receives pointer release even when its handle consumes the event. Polling also
// commits a drag released outside the canvas or when the EventSystem loses selection.
internal sealed class TimelineSlider : Slider
{
    internal Action DragStarted;
    internal Action<float> Released;
    internal bool Dragging { get; private set; }
    public override void OnPointerDown(PointerEventData data)
    {
        if(data.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
        Dragging = true; DragStarted?.Invoke(); base.OnPointerDown(data);
    }
    public override void OnPointerUp(PointerEventData data)
    {
        if(data.button==PointerEventData.InputButton.Left&&Dragging&&IsInteractable())base.OnDrag(data);
        base.OnPointerUp(data);if(data.button==PointerEventData.InputButton.Left)Commit();
    }
    internal void CheckRelease()
    {
        if(Dragging&&!Application.isFocused){CancelDrag();return;}
        // Give EventSystem the actual MouseUp frame so its final pointer position
        // wins. Poll one frame later only when the native pointer release was lost.
        if(Dragging&&!Input.GetMouseButton(0)&&!Input.GetMouseButtonUp(0))Commit();
    }
    internal void Commit()
    {if(!Dragging)return;Dragging=false;if(IsActive()&&IsInteractable())Released?.Invoke(value);}
    internal void CancelDrag() {Dragging=false;}
    public override void OnMove(AxisEventData eventData) { }
    protected override void OnDisable() {CancelDrag();base.OnDisable();}
}
