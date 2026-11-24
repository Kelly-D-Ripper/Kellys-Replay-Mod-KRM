using System;
using System.Collections.Generic;

namespace ReplayMod.Camera;

// Requested editor visibility is independent of temporary capture hiding.
// Unknown/overflow objects fail closed instead of exposing an unowned preview.
public sealed class CameraPreviewVisibility<T> where T:class
{
    private readonly Dictionary<T,bool> requested=new Dictionary<T,bool>();
    private readonly int capacity;
    public bool CaptureHidden {get;private set;}
    public int Count=>requested.Count;
    public CameraPreviewVisibility(int capacity=32768)
    {if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;}
    public bool Register(T item,bool visible)
    {
        if(item==null)return false;
        if(requested.ContainsKey(item))return true;
        if(requested.Count>=capacity)return false;
        requested.Add(item,visible);return true;
    }
    public bool SetRequested(T item,bool visible)
    {if(item==null||!requested.ContainsKey(item))return false;requested[item]=visible;return true;}
    public bool IsVisible(T item)=>item!=null&&requested.TryGetValue(item,out bool visible)&&visible&&!CaptureHidden;
    public bool SetCaptureHidden(bool hidden)
    {if(CaptureHidden==hidden)return false;CaptureHidden=hidden;return true;}
    public void Forget(T item){if(item!=null)requested.Remove(item);}
}
