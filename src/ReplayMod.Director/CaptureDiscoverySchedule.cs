namespace ReplayMod.Director;

// Full scene discovery belongs to entry/scene changes and a sparse fallback,
// never to the canvas render callback. Lifecycle hooks handle normal new UI.
public sealed class CaptureDiscoverySchedule
{
    public const double FallbackSeconds=2;
    private double next;
    private int lastFrame=-1;
    private bool invalidated=true;
    public void Invalidate()=>invalidated=true;
    public void Reset(){next=0;lastFrame=-1;invalidated=true;}
    public bool ShouldDiscover(double now,int frame)
    {
        if(frame==lastFrame || (!invalidated && now<next))return false;
        lastFrame=frame;invalidated=false;next=now+FallbackSeconds;return true;
    }
}
