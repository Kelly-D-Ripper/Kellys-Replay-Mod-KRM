using System;
using System.Collections.Generic;
using ReplayMod.Director;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReplayMod.Core;

// Owns additional replay presentation only: no native light, renderer, physics
// collider, actor, pipeline asset or recorded event is changed by this editor.
public sealed class ReplayStudioLights
{
    private sealed class Binding
    {
        public StudioLightSettings settings;
        public GameObject root,box;
        public Light light;
        public Renderer renderer;
        public Material material;
        public LineRenderer direction;
        public float boxSize;
        public readonly ReplayStudioLightCookie cookie=new ReplayStudioLightCookie();
    }
    private readonly Dictionary<string,Binding> owned=new Dictionary<string,Binding>(StringComparer.Ordinal);
    private readonly List<string> stale=new List<string>();
    private StudioLightSettings dragging;
    private Plane dragPlane;
    private Vector3 dragOffset;
    private float dragDepth;
    private UnityEngine.Camera dragCamera;
    private double dragOriginX,dragOriginY,dragOriginZ;
    public bool Dragging=>dragging!=null;
    public int Count=>owned.Count;

    public void Update(List<StudioLightSettings> settings,int selected,UnityEngine.Camera camera,bool active,bool markers,bool allowDrag,Action<int> select)
    {
        if(!active){Hide();return;}
        ReplayStudioLightCapabilities.Refresh();
        stale.Clear();foreach(var pair in owned)stale.Add(pair.Key);
        int count=Math.Min(settings?.Count??0,StudioLightSettings.MaximumLights);
        for(int i=0;i<count;i++)
        {
            var item=settings[i];if(item==null||!item.Valid())continue;
            stale.Remove(item.id);
            if(!owned.TryGetValue(item.id,out var binding)||binding.root==null)
            {
                if(binding!=null)Dispose(binding);
                binding=Create(item);owned[item.id]=binding;
            }
            binding.settings=item;Apply(binding,i==selected,camera,markers);
        }
        foreach(string key in stale){Dispose(owned[key]);owned.Remove(key);}
        if(!markers||!allowDrag||camera==null){CancelDrag();return;}
        UpdatePointer(settings,camera,select);
    }
    private static Binding Create(StudioLightSettings settings)
    {
        var binding=new Binding{settings=settings};
        binding.root=new GameObject("Replay studio light"){hideFlags=HideFlags.HideAndDontSave};
        if(Datum.origin!=null)binding.root.transform.SetParent(Datum.origin,true);
        binding.light=binding.root.AddComponent<Light>();binding.light.enabled=false;
        binding.box=GameObject.CreatePrimitive(PrimitiveType.Cube);binding.box.name="Replay studio light grab box";
        binding.box.hideFlags=HideFlags.HideAndDontSave;binding.box.layer=2;
        binding.box.transform.SetParent(binding.root.transform,false);
        var collider=binding.box.GetComponent<Collider>();if(collider!=null){collider.enabled=false;UnityEngine.Object.Destroy(collider);}
        binding.renderer=binding.box.GetComponent<Renderer>();binding.renderer.enabled=false;
        binding.renderer.shadowCastingMode=ShadowCastingMode.Off;binding.renderer.receiveShadows=false;
        binding.renderer.lightProbeUsage=LightProbeUsage.Off;binding.renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
        // Built-in Standard is unsupported in URP. Hide the helper if every
        // supported native shader is unavailable, rather than showing magenta.
        Shader shader=null;
        foreach(string name in new[]{"Universal Render Pipeline/Unlit","Universal Render Pipeline/Lit","Universal Render Pipeline/Simple Lit"})
        {var candidate=Shader.Find(name);if(candidate!=null&&candidate.isSupported){shader=candidate;break;}}
        if(shader!=null)
        {
            binding.material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            binding.renderer.sharedMaterial=binding.material;
            var arrow=new GameObject("Replay studio light aim guide"){hideFlags=HideFlags.HideAndDontSave};
            arrow.transform.SetParent(binding.root.transform,false);arrow.layer=2;
            binding.direction=arrow.AddComponent<LineRenderer>();binding.direction.enabled=false;
            binding.direction.sharedMaterial=binding.material;binding.direction.positionCount=2;
            binding.direction.useWorldSpace=false;binding.direction.shadowCastingMode=ShadowCastingMode.Off;
            binding.direction.receiveShadows=false;binding.direction.lightProbeUsage=LightProbeUsage.Off;
            binding.direction.reflectionProbeUsage=ReflectionProbeUsage.Off;
        }
        return binding;
    }
    private static void Apply(Binding binding,bool selected,UnityEngine.Camera camera,bool markers)
    {
        var s=binding.settings;var origin=Datum.originPosition;
        binding.root.transform.SetPositionAndRotation(new Vector3((float)(s.x+origin.x),(float)(s.y+origin.y),(float)(s.z+origin.z)),new Quaternion(s.qx,s.qy,s.qz,s.qw));
        if(!binding.root.activeSelf)binding.root.SetActive(true);
        var light=binding.light;light.type=s.type==1?LightType.Spot:LightType.Point;
        light.color=new Color(s.r,s.g,s.b,1);light.intensity=s.intensity;light.range=s.range;
        light.spotAngle=s.spotOuter;light.innerSpotAngle=s.spotInner;
        light.useColorTemperature=s.useTemperature;light.colorTemperature=s.temperature;
        light.cookie=binding.cookie.CookieFor(s,ReplayStudioLightCapabilities.CookiesAvailable);
        light.shadows=ReplayStudioLightCapabilities.ShadowsAvailable?(s.shadowMode==2&&ReplayStudioLightCapabilities.SoftShadowsAvailable?LightShadows.Soft:s.shadowMode!=0?LightShadows.Hard:LightShadows.None):LightShadows.None;
        light.shadowStrength=s.shadowStrength;light.shadowNearPlane=s.shadowNearPlane;
        light.enabled=s.enabled&&ReplayStudioLightCapabilities.DynamicLightsAvailable;
        // A modest screen-size helper stays grabbable as the editor moves. It
        // never changes the physical light range or the camera's clip planes.
        float distance=camera!=null?Vector3.Distance(camera.transform.position,binding.root.transform.position):10;
        float vertical=camera!=null?2*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)*distance:distance;
        binding.boxSize=Mathf.Clamp(vertical*22/Mathf.Max(240,camera!=null?camera.pixelHeight:1080),.15f,30);
        binding.box.transform.localScale=Vector3.one*binding.boxSize;
        // Keep the grab cube axis-aligned; aiming belongs to its separate line.
        binding.box.transform.rotation=Quaternion.identity;
        Color tint=selected?new Color(.58f,1,.65f,1):s.enabled?new Color(Mathf.Max(.25f,s.r),Mathf.Max(.25f,s.g),Mathf.Max(.25f,s.b),1):new Color(.4f,.4f,.4f,1);
        if(binding.material!=null)
        {
            if(binding.material.HasProperty("_BaseColor"))binding.material.SetColor("_BaseColor",tint);
            if(binding.material.HasProperty("_Color"))binding.material.SetColor("_Color",tint);
            // Native builds may strip Unlit. A private Lit fallback stays
            // visible at night without lighting any native material or mesh.
            if(binding.material.HasProperty("_EmissionColor"))
            {binding.material.EnableKeyword("_EMISSION");binding.material.SetColor("_EmissionColor",tint);}
        }
        bool show=markers&&binding.material!=null;
        binding.renderer.enabled=show;if(binding.box.activeSelf!=show)binding.box.SetActive(show);
        if(binding.direction!=null)
        {
            binding.direction.enabled=show&&selected&&s.type==1;
            binding.direction.startWidth=binding.direction.endWidth=binding.boxSize*.07f;
            binding.direction.SetPosition(0,Vector3.zero);binding.direction.SetPosition(1,Vector3.forward*Mathf.Min(s.range,Mathf.Max(3,binding.boxSize*8)));
        }
    }
    public bool PointerCaptured(UnityEngine.Camera camera,bool allowEditing,bool uiBlocked)
        =>allowEditing&&!uiBlocked&&camera!=null&&(Dragging||Hit(camera.ScreenPointToRay(Input.mousePosition),out _)!=null);
    private Binding Hit(Ray ray,out float distance)
    {
        Binding hit=null;distance=float.PositiveInfinity;
        foreach(var pair in owned)
        {
            var candidate=pair.Value;
            if(candidate.root==null||candidate.box==null||!candidate.box.activeInHierarchy||candidate.renderer==null||!candidate.renderer.enabled)continue;
            var bounds=new Bounds(candidate.box.transform.position,Vector3.one*candidate.boxSize);
            if(bounds.IntersectRay(ray,out float along)&&along>=0&&along<distance){hit=candidate;distance=along;}
        }
        return hit;
    }
    private static bool UiBlocked()=>ReplayMod.UI.TrailerSuiteUi.Instance?.PointerOverUi==true||ReplayMod.UI.TrailerSuiteUi.Instance?.HasInputFocus==true;
    private void UpdatePointer(List<StudioLightSettings> settings,UnityEngine.Camera camera,Action<int> select)
    {
        if(UiBlocked()||Input.GetMouseButtonUp(0)||dragCamera!=null&&dragCamera!=camera){CancelDrag();return;}
        Ray ray=camera.ScreenPointToRay(Input.mousePosition);
        if(Dragging)
        {
            var currentOrigin=Datum.originPosition;
            // Native floating-origin shifts move the camera and owned helpers
            // together. Move the drag plane by the same double-precision delta.
            var shift=new Vector3((float)(currentOrigin.x-dragOriginX),(float)(currentOrigin.y-dragOriginY),(float)(currentOrigin.z-dragOriginZ));
            dragPlane.distance-=Vector3.Dot(dragPlane.normal,shift);
            dragOriginX=currentOrigin.x;dragOriginY=currentOrigin.y;dragOriginZ=currentOrigin.z;
        }
        if(!Dragging&&Input.GetMouseButtonDown(0))
        {
            var hit=Hit(ray,out _);if(hit==null)return;
            int index=settings.IndexOf(hit.settings);if(index<0)return;
            select(index);dragging=hit.settings;dragCamera=camera;
            Vector3 point=hit.root.transform.position;
            dragDepth=Vector3.Dot(point-camera.transform.position,camera.transform.forward);
            var currentOrigin=Datum.originPosition;dragOriginX=currentOrigin.x;dragOriginY=currentOrigin.y;dragOriginZ=currentOrigin.z;
            dragPlane=new Plane(camera.transform.forward,point);
            dragOffset=dragPlane.Raycast(ray,out float distance)?point-ray.GetPoint(distance):Vector3.zero;
        }
        if(!Dragging)return;
        if(!Input.GetMouseButton(0)||!settings.Contains(dragging)){CancelDrag();return;}
        // Scroll while holding a box changes depth; native camera zoom is
        // blocked by StudioLightPointerCaptured for the entire drag.
        float scroll=Input.mouseScrollDelta.y;
        if(scroll!=0)
        {
            dragDepth=Mathf.Clamp(dragDepth*Mathf.Pow(1.12f,scroll),Mathf.Max(.1f,camera.nearClipPlane*2),5000);
            dragPlane=new Plane(camera.transform.forward,camera.transform.position+camera.transform.forward*dragDepth);
        }
        if(dragPlane.Raycast(ray,out float along)&&along>=0)
        {
            var position=ray.GetPoint(along)+dragOffset;var origin=Datum.originPosition;
            double x=(double)position.x-origin.x,y=(double)position.y-origin.y,z=(double)position.z-origin.z;
            if(DirectorProject.Range(x,-1e8,1e8)&&DirectorProject.Range(y,-1e8,1e8)&&DirectorProject.Range(z,-1e8,1e8))
            {dragging.x=x;dragging.y=y;dragging.z=z;}
        }
    }
    public void CancelDrag(){dragging=null;dragCamera=null;dragOffset=Vector3.zero;}
    public void Hide()
    {
        CancelDrag();foreach(var pair in owned)
        {var binding=pair.Value;if(binding.light!=null)binding.light.enabled=false;if(binding.root!=null)binding.root.SetActive(false);}
    }
    public void Clear()
    {
        CancelDrag();foreach(var pair in owned)Dispose(pair.Value);owned.Clear();stale.Clear();
    }
    private static void Dispose(Binding binding)
    {
        if(binding.light!=null)binding.light.cookie=null;
        binding.cookie.Clear();
        if(binding.root!=null){binding.root.SetActive(false);UnityEngine.Object.Destroy(binding.root);}
        if(binding.material!=null)UnityEngine.Object.Destroy(binding.material);
        binding.root=null;binding.box=null;binding.light=null;binding.material=null;
    }
}
